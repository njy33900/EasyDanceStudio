using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UMT;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MmdStudio.Poc
{
    // PH-05 check: chest/hip spring candidates of each model, and for models with usable roots a
    // full-length spring bake per preset: time, determinism (two bakes identical), jumps and the
    // largest swing angle.
    public sealed class PocSpringProbe : MonoBehaviour
    {
        [Serializable] private sealed class PresetResult
        {
            public string preset;
            public float bakeSeconds, longestStepMs, maxAngleDeg;
            public int ticks, jumpTicks;
            public bool deterministic;
            public long bytes;
        }
        [Serializable] private sealed class ModelResult
        {
            public string name, summary;
            public string[] candidates;
            public int usableRoots;
            // Test only: no usable roots, so weighted roots blocked by rigid bodies were forced.
            public bool forced;
            public PresetResult[] presets;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend;
            public ModelResult[] models;
        }

        public IEnumerator Run(string reportPath)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
            };
            var results = new List<ModelResult>();
            var vmd = PocArguments.Value("-pocVmd");
            yield return null;
            foreach (var pmx in new[] { PocArguments.Value("-pocPmx"), PocArguments.Value("-pocSecondPmx"), PocArguments.Value("-pocThirdPmx") }.Where(p => !string.IsNullOrEmpty(p)))
            {
                var result = new ModelResult { name = Path.GetFileNameWithoutExtension(pmx) };
                results.Add(result);
                var load = MmdPocProbe.LoadAsync(pmx, null, CancellationToken.None);
                while (!load.IsCompleted) yield return null;
                if (load.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) { Finish(reportPath, report, results, "load failed: " + load.Exception?.GetBaseException().Message); yield break; }
                var model = load.Result.imported;
                List<MmdSpringCandidate> candidates;
                try
                {
                    candidates = MmdSpringBones.Detect(model);
                    result.summary = MmdSpringBones.Describe(candidates);
                    result.candidates = candidates.Select(c => c.Part + " " + c.Name + (c.HasRigidBody ? " [rigid body]" : "") + (c.Weighted ? "" : " [no weights]")).ToArray();
                    result.usableRoots = candidates.Count(c => c.Usable);
                }
                catch (Exception ex) { Release(model); Finish(reportPath, report, results, "detect: " + ex); yield break; }
                if (result.usableRoots == 0 && candidates.Any(c => c.Weighted && c.HasRigidBody))
                {
                    // Neither test model has chest/hip bones without rigid bodies. Exercise the
                    // spring solver on the rigid-body roots instead (physics stays off here).
                    foreach (var c in candidates) if (c.Weighted) c.HasRigidBody = false;
                    result.forced = true; result.usableRoots = candidates.Count(c => c.Usable);
                }
                if (result.usableRoots > 0 && !string.IsNullOrEmpty(vmd))
                {
                    var motion = model.root.AddComponent<MmdPocMotion>();
                    var motionLoad = motion.LoadAsync(model, vmd, "direct-default-ik", null, CancellationToken.None);
                    while (!motionLoad.IsCompleted) yield return null;
                    if (motionLoad.IsFaulted) { Release(model); Finish(reportPath, report, results, "vmd: " + motionLoad.Exception.GetBaseException()); yield break; }
                    motion.Playing = false;
                    var token = new object(); motion.AcquireSceneClock(token);
                    var presets = new List<PresetResult>();
                    foreach (var id in MmdSpringBones.PresetIds)
                    {
                        var pr = new PresetResult { preset = id };
                        MmdSpringBake first = null, second = null;
                        var watch = Stopwatch.StartNew();
                        foreach (var pass in new[] { 0, 1 })
                        {
                            var bake = new MmdSpringBake(motion, model, token, candidates, id);
                            while (!bake.Complete) { bake.Step(12); yield return null; }
                            if (pass == 0) { first = bake; pr.bakeSeconds = (float)watch.Elapsed.TotalSeconds; } else second = bake;
                        }
                        pr.ticks = first.BakedTicks; pr.jumpTicks = first.JumpTicks; pr.maxAngleDeg = first.MaxAngleUsed;
                        pr.longestStepMs = (float)first.LongestStepMilliseconds; pr.bytes = first.Bytes;
                        pr.deterministic = first.SameAs(second);
                        presets.Add(pr);
                    }
                    result.presets = presets.ToArray();
                    motion.ReleaseSceneClock(token);
                }
                Release(model);
                yield return null; yield return null;
            }
            string failure = null;
            foreach (var m in results)
                foreach (var p in m.presets ?? new PresetResult[0])
                    if (!p.deterministic) failure = m.name + " " + p.preset + ": spring bake is not deterministic";
                    else if (p.jumpTicks > 0) failure = m.name + " " + p.preset + ": spring jumped " + p.jumpTicks + " ticks";
            Finish(reportPath, report, results, failure);
        }

        private static void Release(PMXImportResult model)
        {
            var owner = model?.root != null ? model.root.GetComponentInParent<PocAssetLifetime>() : null;
            if (owner != null) owner.Release();
        }

        private static void Finish(string path, Report report, List<ModelResult> results, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.models = results.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
