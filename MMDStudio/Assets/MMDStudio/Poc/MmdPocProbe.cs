using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    [Serializable]
    public sealed class MmdPocSummary
    {
        public string source;
        public int vertices;
        public int bones;
        public int materials;
        public int loadedTextures;
        public int textureSlots;
        public int blendShapes;
        public int ikControllers;
        public string[] warnings;
        public bool physicsEnabled;
        public string[] loadStageTimings;
    }

    // Controlled stage-0 probe, not yet the production importer or file picker.
    public static class MmdPocProbe
    {
        public static MmdPocSummary Load(string path, Camera camera, out PMXImportResult imported, bool frameCamera = true)
        {
            imported = null;
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path), ".pmx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected a PMX file.");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 9 || file.Length > 64L * 1024 * 1024)
                throw new InvalidDataException("PMX is missing, empty or exceeds the 64 MiB probe limit.");
            using (var stream = File.OpenRead(path))
            {
                if (stream.ReadByte() != 'P' || stream.ReadByte() != 'M' ||
                    stream.ReadByte() != 'X' || stream.ReadByte() != ' ')
                    throw new InvalidDataException("Invalid PMX signature.");
            }
            var resources = Resources.Load<UMTResources>("UMTResources");
            if (resources == null) throw new InvalidOperationException("UMTResources is missing from Player.");
            var container = new GameObject("External PMX Probe");
            var lifetime = container.AddComponent<PocAssetLifetime>();
            try
            {
                imported = PMXImporter.Import(path, new PMXImportOptions
                {
                    parent = container.transform,
                    umtResources = resources,
                    createAvatar = false // VMD drives the MMD rig; no Humanoid clone is needed.
                });
                lifetime.Track(imported);
                return FinishImport(path, camera, imported, frameCamera);
            }
            catch
            {
                lifetime.Track(imported);
                lifetime.Release();
                throw;
            }
        }

        // The pinned UMT async APIs yield between main-thread phases. Only file bytes are
        // read off-thread; PMXReader creates a ScriptableObject and must stay on Unity's thread.
        public static async Task<(MmdPocSummary summary, PMXImportResult imported)> LoadAsync(
            string path, Action<string> stage, CancellationToken cancellationToken)
        {
            PMXImportResult imported = null;
            PMXModel parsed = null;
            var phaseTimings = new List<string>();
            GameObject container = null;
            PocAssetLifetime lifetime = null;
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path), ".pmx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected a PMX file.");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 9 || file.Length > 64L * 1024 * 1024)
                throw new InvalidDataException("PMX is missing, empty or exceeds the 64 MiB probe limit.");
            var resources = Resources.Load<UMTResources>("UMTResources");
            if (resources == null) throw new InvalidOperationException("UMTResources is missing from Player.");
            try
            {
                stage?.Invoke("PMX 파일 읽는 중…");
                var bytes = await Task.Run(() => File.ReadAllBytes(path), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (bytes[0] != 'P' || bytes[1] != 'M' || bytes[2] != 'X' || bytes[3] != ' ')
                    throw new InvalidDataException("Invalid PMX signature.");
                stage?.Invoke("PMX 구조 분석 중…");
                using (var stream = new MemoryStream(bytes, false))
                    parsed = await PMXReader.ReadAsync(new UMTFrameBudget(8), stream);
                cancellationToken.ThrowIfCancellationRequested();
                stage?.Invoke("PMX 이름 정리 중…");
                // H1-01b: the synchronous rename took one 150–270 ms frame. The rename lists are
                // parsed once (pure JSON, off the main thread) and cached; RenameAsync runs the same
                // rename steps as Rename but yields between name categories.
                var renames = await RenameListsAsync(resources);
                var renameResult = await PMXRenameUtilities.RenameAsync(new UMTFrameBudget(8), parsed, renames, resources);
                cancellationToken.ThrowIfCancellationRequested();
                container = new GameObject("External PMX Probe");
                lifetime = container.AddComponent<PocAssetLifetime>();
                var options = new PMXImportOptions
                {
                    sourcePath = path, parent = container.transform, umtResources = resources,
                    createAvatar = false,
                    timingCallback = (label, elapsed) =>
                    {
                        var timing = label + ": " + elapsed.TotalMilliseconds.ToString("0.0") + " ms";
                        phaseTimings.Add(timing);
                        // Called inside UMT's build; a throwing UI callback must not abort it.
                        MmdPocMotion.ReportSafely(stage, label + " 완료 (" + elapsed.TotalMilliseconds.ToString("0") + " ms)");
                        if (label == FailAfterStageForTest) throw new InvalidOperationException("Injected failure after UMT stage: " + label);
                    }
                };
                // H1-01b: textures and materials are prepared one per frame (cancellable between
                // items, created objects owned by the staged build until handed over) and passed
                // to UMT through loadTextures/materialOverrides; UMT then builds the rest.
                var budget = new UMTFrameBudget(8);
                using (var staged = await MmdStagedPmxBuild.RunAsync(parsed, options, budget, cancellationToken, stage))
                {
                    phaseTimings.Add("Longest texture step: " + staged.LongestTextureStepMilliseconds.ToString("0.0") + " ms");
                    phaseTimings.Add("Longest material step: " + staged.LongestMaterialStepMilliseconds.ToString("0.0") + " ms");
                    if (staged.SharedNameMaterials > 0) phaseTimings.Add("Materials left to UMT (shared names): " + staged.SharedNameMaterials);
                    cancellationToken.ThrowIfCancellationRequested();
                    staged.Apply(options);
                    stage?.Invoke("PMX Unity 객체 생성 중…");
                    // UMT does not expose cancellation or partial resources during this call.
                    imported = await PMXImporter.BuildUnityObjectsAsync(budget, parsed, options);
                    staged.TransferTo(imported);
                }
                imported.renameResult = renameResult;
                imported.metadata = new PMXMetadata(parsed);
                lifetime.Track(imported);
                var summary = FinishImport(path, null, imported, false);
                summary.loadStageTimings = phaseTimings.ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                return (summary, imported);
            }
            catch
            {
                // UMT returns nothing when BuildUnityObjectsAsync fails part way, so the meshes,
                // materials and avatar it already attached under the container are swept here.
                if (imported == null && container != null) ReleaseUnreturnedBuild(container);
                if (lifetime != null)
                {
                    lifetime.Track(imported);
                    lifetime.Release();
                }
                if (imported == null && parsed != null) UnityEngine.Object.Destroy(parsed);
                throw;
            }
        }

        // Test hook (probe only): throw from UMT's timing callback right after the named stage
        // ("Build Meshes and Renderers", "Build Runtime"…) to exercise the late-failure cleanup.
        internal static string FailAfterStageForTest;

        // Destroys the runtime objects UMT bound to the hierarchy of a failed build. UMT creates
        // every mesh, material and avatar it attaches at runtime (`new Mesh`/`new Material`);
        // textures are not touched here because the staged build owns and disposes them and
        // UMTResources toon textures are shared assets. Meshes UMT built but had not yet bound to
        // a renderer when it failed are unreachable through the public API (UMT limitation).
        private static void ReleaseUnreturnedBuild(GameObject container)
        {
            var owned = new HashSet<UnityEngine.Object>();
            foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials) if (material != null) owned.Add(material);
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) owned.Add(skinned.sharedMesh);
            }
            foreach (var filter in container.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) owned.Add(filter.sharedMesh);
            foreach (var animator in container.GetComponentsInChildren<Animator>(true))
                if (animator.avatar != null) owned.Add(animator.avatar);
            foreach (var item in owned) UnityEngine.Object.Destroy(item);
        }

        private static UMTResources cachedRenameResources;
        private static PMXRenameLists cachedRenameLists;
        private static async Task<PMXRenameLists> RenameListsAsync(UMTResources resources)
        {
            if (cachedRenameLists != null && cachedRenameResources == resources) return cachedRenameLists;
            var json = resources.GetPMXRenameListsJson(); // TextAsset access stays on the main thread
            var lists = await Task.Run(() => PMXRenameUtilities.LoadRenameListsJson(json));
            cachedRenameResources = resources; cachedRenameLists = lists;
            return lists;
        }

        private static MmdPocSummary FinishImport(string path, Camera camera, PMXImportResult imported, bool frameCamera)
        {
                imported.mmdTransformResult.transformManager.livePhysics = false;
                var warnings = new List<string>(imported.warnings);
                var unsupported = new Dictionary<PMXMorph.Type, int>();
                foreach (var morph in imported.model.morphs)
                {
                    if (morph.type == PMXMorph.Type.Vertex || morph.type == PMXMorph.Type.Group) continue;
                    unsupported.TryGetValue(morph.type, out var count);
                    unsupported[morph.type] = count + 1;
                }
                if (unsupported.Count > 0)
                {
                    var descriptions = new List<string>();
                    foreach (var item in unsupported) descriptions.Add(item.Key + " " + item.Value);
                    warnings.Add("현재 모프 재생 미지원: " + string.Join(", ", descriptions));
                }
                var summary = new MmdPocSummary
                {
                    source = path,
                    vertices = imported.model.vertices.Length,
                    bones = imported.bones.Length,
                    materials = imported.materials.Count,
                    textureSlots = imported.texturesByIndex.Length,
                    ikControllers = imported.mmdTransformResult.ikControllerCount,
                    warnings = warnings.ToArray(),
                    physicsEnabled = false
                };
                foreach (var texture in imported.texturesByIndex)
                    if (texture != null) summary.loadedTextures++;
                var renderers = imported.root.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (renderers.Length == 0 || summary.bones == 0)
                    throw new InvalidDataException("Import created no skinned renderers or bones.");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    summary.blendShapes += renderer.sharedMesh.blendShapeCount;
                    renderer.updateWhenOffscreen = true;
                }
                if (frameCamera)
                {
                    var radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
                    camera.transform.position = bounds.center + Vector3.forward * radius * 2.5f;
                    camera.transform.LookAt(bounds.center);
                    camera.nearClipPlane = 0.01f;
                    camera.farClipPlane = Mathf.Max(100, radius * 10);
                    var cube = GameObject.Find("Cube");
                    if (cube != null) cube.SetActive(false);
                }
                return summary;
        }
    }
}
