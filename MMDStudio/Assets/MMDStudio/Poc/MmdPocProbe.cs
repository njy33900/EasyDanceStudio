using System;
using System.IO;
using System.Collections.Generic;
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
            catch
            {
                lifetime.Track(imported);
                lifetime.Release();
                throw;
            }
        }
    }
}
