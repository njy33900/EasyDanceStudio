using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UMT;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MmdStudio.Poc
{
    // H1-01b check: every PMX given on the command line is loaded twice in the actual Player —
    // through the unchanged synchronous UMT import (reference) and through the staged async path
    // (MmdStagedPmxBuild) — and the resulting textures, materials, renderer bindings and warnings
    // are compared. The staged load's frame times are recorded while the UI keeps rendering.
    public sealed class PocPmxStagingProbe : MonoBehaviour
    {
        [Serializable] private sealed class ModelResult
        {
            public string path;
            public bool identical;
            public int textures, materials, sharedNameMaterials, compared;
            public string[] mismatches;
            public double referenceMilliseconds, stagedMilliseconds, stagedWorstFrameMilliseconds;
            public int stagedFrames;
            public string[] stagedTimings;
            // The slowest frames of the staged load and the loading stage shown during each.
            public string[] slowestFrames;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, configuration;
            public ModelResult[] models;
            // Late UMT failure injected into the first model: objects left after cleanup.
            public LateFailure[] lateFailures;
            // A progress callback that always throws during VMD load must not abort UMT's
            // conversion (which would leak its native contexts); the motion must still load.
            public bool throwingProgressVmdLoaded;
            public int throwingProgressCalls;
            public string throwingProgressError;
        }
        [Serializable] private sealed class LateFailure
        {
            public string stage;
            public bool threw, clean;
            public string leaked;
        }

        private static readonly Type[] TrackedTypes = { typeof(Mesh), typeof(Material), typeof(Texture2D), typeof(Avatar), typeof(GameObject), typeof(ScriptableObject) };
        private static int[] CountObjects() => TrackedTypes.Select(type => Resources.FindObjectsOfTypeAll(type).Length).ToArray();

        public IEnumerator Run(string reportPath)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                configuration = Debug.isDebugBuild ? "Debug" : "Release",
            };
            var results = new List<ModelResult>();
            var paths = new[] { PocArguments.Value("-pocPmx"), PocArguments.Value("-pocSecondPmx"), PocArguments.Value("-pocStagePmx") }
                .Where(value => !string.IsNullOrEmpty(value)).ToArray();
            if (paths.Length == 0) { Finish(reportPath, report, results, "No PMX given (-pocPmx/-pocSecondPmx/-pocStagePmx)."); yield break; }
            yield return null;
            foreach (var path in paths)
            {
                var result = new ModelResult { path = Path.GetFullPath(path) };
                results.Add(result);
                PMXImportResult reference = null, staged = null;
                List<string> referencePrint, stagedPrint;
                try
                {
                    var clock = Stopwatch.StartNew();
                    MmdPocProbe.Load(path, null, out reference, false);
                    result.referenceMilliseconds = clock.Elapsed.TotalMilliseconds;
                    referencePrint = Fingerprint(reference);
                }
                catch (Exception ex) { Release(reference); Finish(reportPath, report, results, "Reference import failed: " + path + "\n" + ex); yield break; }
                yield return null;

                var stages = new List<string>();
                var watch = Stopwatch.StartNew(); var last = Time.realtimeSinceStartup; var worst = 0f; var frames = 0;
                var currentStage = "start"; var frameLog = new List<(float ms, string stage)>();
                var task = MmdPocProbe.LoadAsync(path, label => currentStage = label, CancellationToken.None);
                while (!task.IsCompleted)
                {
                    var stageBefore = currentStage;
                    yield return null;
                    var now = Time.realtimeSinceStartup; worst = Mathf.Max(worst, now - last);
                    frameLog.Add(((now - last) * 1000, stageBefore + " -> " + currentStage)); last = now; frames++;
                }
                result.slowestFrames = frameLog.OrderByDescending(entry => entry.ms).Take(5).Select(entry => entry.ms.ToString("0.0") + " ms: " + entry.stage).ToArray();
                result.stagedMilliseconds = watch.Elapsed.TotalMilliseconds;
                result.stagedWorstFrameMilliseconds = worst * 1000; result.stagedFrames = frames;
                try
                {
                    if (task.IsFaulted) throw task.Exception.GetBaseException();
                    staged = task.Result.imported;
                    result.stagedTimings = task.Result.summary.loadStageTimings;
                    result.sharedNameMaterials = result.stagedTimings.Count(line => line.StartsWith("Materials left to UMT")) > 0
                        ? int.Parse(result.stagedTimings.First(line => line.StartsWith("Materials left to UMT")).Split(':')[1].Trim()) : 0;
                    stagedPrint = Fingerprint(staged);
                    result.textures = staged.texturesByIndex.Length; result.materials = staged.materials.Count;
                    result.compared = Math.Max(referencePrint.Count, stagedPrint.Count);
                    var mismatches = new List<string>();
                    for (var i = 0; i < result.compared; i++)
                    {
                        var a = i < referencePrint.Count ? referencePrint[i] : "<missing>";
                        var b = i < stagedPrint.Count ? stagedPrint[i] : "<missing>";
                        if (a != b && mismatches.Count < 20) mismatches.Add("reference: " + a + "\n   staged: " + b);
                    }
                    result.mismatches = mismatches.ToArray();
                    result.identical = mismatches.Count == 0;
                }
                catch (Exception ex) { Release(reference); Release(staged); Finish(reportPath, report, results, "Staged import failed: " + path + "\n" + ex); yield break; }
                Release(reference); Release(staged);
                yield return null;
            }
            // Inject a failure after UMT's late stages and check nothing the build created is left.
            var failures = new List<LateFailure>();
            foreach (var failStage in new[] { "Build Materials", "Build Meshes and Renderers", "Build Runtime" })
            {
                yield return null; yield return null;
                var before = CountObjects();
                var failure = new LateFailure { stage = failStage };
                MmdPocProbe.FailAfterStageForTest = failStage;
                var task = MmdPocProbe.LoadAsync(paths[0], null, CancellationToken.None);
                while (!task.IsCompleted) yield return null;
                MmdPocProbe.FailAfterStageForTest = null;
                failure.threw = task.IsFaulted && task.Exception.GetBaseException().Message.Contains("Injected failure");
                if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) Release(task.Result.imported);
                yield return null; yield return null; // Destroy is deferred to the end of the frame
                var after = CountObjects();
                failure.leaked = string.Join(", ", TrackedTypes.Select((type, i) => type.Name + " " + (after[i] - before[i]).ToString("+0;-0;0")));
                failure.clean = after.Zip(before, (x, y) => x - y).All(delta => delta <= 0);
                failures.Add(failure);
            }
            report.lateFailures = failures.ToArray();
            var vmd = PocArguments.Value("-pocVmd");
            if (!string.IsNullOrEmpty(vmd))
            {
                var modelTask = MmdPocProbe.LoadAsync(paths[0], null, CancellationToken.None);
                while (!modelTask.IsCompleted) yield return null;
                PMXImportResult model = modelTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? modelTask.Result.imported : null;
                if (model == null) report.throwingProgressError = "model load failed: " + modelTask.Exception?.GetBaseException().Message;
                else
                {
                    var motion = model.root.AddComponent<MmdPocMotion>();
                    var motionTask = motion.LoadAsync(model, vmd, "sparse", label => { report.throwingProgressCalls++; throw new InvalidOperationException("injected progress failure"); }, CancellationToken.None);
                    while (!motionTask.IsCompleted) yield return null;
                    report.throwingProgressVmdLoaded = motionTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && motion.Summary != null && motion.Summary.mappedBoneTracks > 0;
                    if (motionTask.IsFaulted) report.throwingProgressError = motionTask.Exception.GetBaseException().ToString();
                    Release(model);
                }
            }
            var error = !results.All(model => model.identical) ? "Staged import differs from the reference import."
                : !failures.All(f => f.threw && f.clean) ? "Late UMT failure left objects behind or did not throw."
                : !string.IsNullOrEmpty(PocArguments.Value("-pocVmd")) && !(report.throwingProgressVmdLoaded && report.throwingProgressCalls > 0)
                    ? "VMD load with a throwing progress callback failed: " + report.throwingProgressError : null;
            Finish(reportPath, report, results, error);
        }

        private static void Release(PMXImportResult result)
        {
            var owner = result?.root != null ? result.root.GetComponentInParent<PocAssetLifetime>() : null;
            if (owner != null) owner.Release();
        }

        // Order-sensitive description of what the import produced (object identities excluded).
        private static List<string> Fingerprint(PMXImportResult result)
        {
            var lines = new List<string> { "warnings: " + string.Join(" | ", result.warnings), "distinct textures: " + result.textures.Count };
            for (var i = 0; i < result.texturesByIndex.Length; i++)
            {
                var texture = result.texturesByIndex[i];
                lines.Add("texture[" + i + "]: " + (texture == null ? "null" :
                    texture.name + " " + texture.width + "x" + texture.height + " " + texture.format + " mips " + texture.mipmapCount + " " + PixelHash(texture)));
            }
            for (var i = 0; i < result.materials.Count; i++) lines.Add("material[" + i + "]: " + Describe(result.materials[i]));
            foreach (var renderer in result.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                lines.Add("renderer " + renderer.name + ": " + string.Join(", ", renderer.sharedMaterials.Select(material => material == null ? "null" : material.name)));
            return lines;
        }

        private static string Describe(Material material)
        {
            if (material == null) return "null";
            var text = new StringBuilder();
            text.Append(material.name).Append(" | shader ").Append(material.shader.name).Append(" | queue ").Append(material.renderQueue)
                .Append(" | RenderType ").Append(material.GetTag("RenderType", false))
                .Append(" | keywords ").Append(string.Join(",", material.shaderKeywords.OrderBy(value => value, StringComparer.Ordinal)));
            var shader = material.shader;
            for (var p = 0; p < shader.GetPropertyCount(); p++)
            {
                var name = shader.GetPropertyName(p);
                text.Append(" | ").Append(name).Append('=');
                switch (shader.GetPropertyType(p))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: { var c = material.GetColor(name); text.Append(c.r.ToString("R")).Append(',').Append(c.g.ToString("R")).Append(',').Append(c.b.ToString("R")).Append(',').Append(c.a.ToString("R")); break; }
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: { var v = material.GetVector(name); text.Append(v.x.ToString("R")).Append(',').Append(v.y.ToString("R")).Append(',').Append(v.z.ToString("R")).Append(',').Append(v.w.ToString("R")); break; }
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: text.Append(material.GetFloat(name).ToString("R")); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int: text.Append(material.GetInteger(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: { var t = material.GetTexture(name); text.Append(t == null ? "null" : t.name + " " + t.width + "x" + t.height); break; }
                }
            }
            return text.ToString();
        }

        private static string PixelHash(Texture2D texture)
        {
            if (!texture.isReadable) return "unreadable";
            var hash = 2166136261u;
            foreach (var pixel in texture.GetPixels32())
            {
                hash = (hash ^ pixel.r) * 16777619u; hash = (hash ^ pixel.g) * 16777619u;
                hash = (hash ^ pixel.b) * 16777619u; hash = (hash ^ pixel.a) * 16777619u;
            }
            return hash.ToString("x8");
        }

        private static void Finish(string path, Report report, List<ModelResult> results, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.models = results.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
