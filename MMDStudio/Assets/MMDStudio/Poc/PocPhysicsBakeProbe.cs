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
    // PH-03 check: the physics bake (MmdPhysicsBake) interleaved with on-screen physics-OFF
    // sampling must reproduce an uninterrupted stateful 60 Hz simulation. For each model the
    // reference pose is recorded at checkpoints, then the bake runs a slice per frame while the
    // displayed pose is re-sampled at an unrelated time between slices, and finally
    // (physics-OFF sample + cache overlay) is compared with the reference. A second bake without
    // solver-state restore is the negative control.
    public sealed class PocPhysicsBakeProbe : MonoBehaviour
    {
        [Serializable] private sealed class ModelResult
        {
            public string name;
            public int ticks, cachedBones, totalTicks, checkpoints;
            public float maxErrorM, controlMaxErrorM;
            public long fullLengthBytes;
            public float bakeWallSeconds, bakeTicksPerSecond, fullLengthEstimateSeconds;
            public float worstFrameMs, longestStepMs;
            public int bakeFrames;
            public bool midTickApplies, beyondBakedRejected;
            public float budgetMs, firstJumpSeconds, worstJumpM;
            public int jumpTicks, weightedBones, positionTracks;
            public string worstJumpBone;
            // Negative-control investigation: per variant, max error and where it occurs.
            public string[] variants;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, configuration;
            public float budgetMs;
            public ModelResult[] models;
        }

        private const float BudgetMs = 8;

        private static Vector3[] World(PMXImportResult model) => model.bones.Select(b => b.position).ToArray();

        private static float MaxError(Vector3[] a, Vector3[] b) => a.Zip(b, (x, y) => Vector3.Distance(x, y)).Max();

        public IEnumerator Run(string reportPath)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                configuration = Debug.isDebugBuild ? "Debug" : "Release", budgetMs = BudgetMs,
            };
            var results = new List<ModelResult>();
            var vmd = PocArguments.Value("-pocVmd");
            // Both models are baked for the whole motion: Gene with the playing budget (8 ms),
            // Sandrone with the paused budget (24 ms) used by the scene while not playing.
            var inputs = new[] { (PocArguments.Value("-pocPmx"), 1200, 8f), (PocArguments.Value("-pocSecondPmx"), 600, 24f) };
            yield return null;
            foreach (var (pmx, ticks, budget) in inputs)
            {
                var result = new ModelResult { name = Path.GetFileNameWithoutExtension(pmx), ticks = ticks, budgetMs = budget };
                results.Add(result);
                var load = MmdPocProbe.LoadAsync(pmx, null, CancellationToken.None);
                while (!load.IsCompleted) yield return null;
                if (load.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) { Finish(reportPath, report, results, "PMX load failed: " + load.Exception?.GetBaseException()); yield break; }
                var model = load.Result.imported;
                var motion = model.root.AddComponent<MmdPocMotion>();
                var motionLoad = motion.LoadAsync(model, vmd, "direct-default-ik", null, CancellationToken.None);
                while (!motionLoad.IsCompleted) yield return null;
                if (motionLoad.IsFaulted) { Release(model); Finish(reportPath, report, results, "VMD load failed: " + motionLoad.Exception.GetBaseException()); yield break; }
                motion.Playing = false;

                // Every tick is compared: a one-tick error right after a bake slice boundary (whose
                // position depends on frame timing) must not hide between sparse checkpoints.
                var checkpoints = Enumerable.Range(0, ticks + 1).ToArray();
                var reference = new Dictionary<int, Vector3[]>();
                string error = null;
                // Reference: uninterrupted stateful simulation.
                try
                {
                    using (var session = new MmdPhysicsPlayback(motion, model) { Playing = true })
                    {
                        session.Seek(0);
                        reference[0] = World(model);
                        session.Playing = true;
                        for (var tick = 1; tick <= ticks; tick++)
                        {
                            session.Advance(1.0 / MmdPhysicsPlayback.Rate);
                            if (session.Tick != tick) throw new InvalidOperationException("reference tick mismatch " + session.Tick + " != " + tick);
                            if (checkpoints.Contains(tick)) reference[tick] = World(model);
                        }
                    }
                }
                catch (Exception ex) { error = "reference: " + ex; }
                if (error != null) { Release(model); Finish(reportPath, report, results, error); yield break; }
                yield return null;

                // Interleaved bake, then controls: without state
                // restore (twice, for run-to-run determinism) and without restore or interleaving.
                var variantLog = new List<string>();
                foreach (var (variantName, control, interleave, skipJump) in new[]
                {
                    ("main", false, true, false), ("no-restore", true, true, false), ("no-restore-again", true, true, false),
                    ("no-restore-no-interleave", true, false, false), ("no-restore-no-world-reads", true, true, true), ("restore-no-world-reads", false, true, true),
                })
                {
                    var token = new object();
                    MmdPhysicsBake bake = null;
                    try
                    {
                        motion.AcquireSceneClock(token);
                        bake = new MmdPhysicsBake(motion, model, token) { SkipStateRestoreForTest = control, SkipJumpCheckForTest = skipJump };
                    }
                    catch (Exception ex) { error = "bake setup: " + ex; }
                    if (error != null) break;
                    var target = variantName == "main" ? bake.TotalTicks : ticks + 1;
                    var wall = Stopwatch.StartNew(); var frames = 0; var worst = 0f; var frameStart = Time.realtimeSinceStartup;
                    while (bake.BakedTicks < target && error == null)
                    {
                        try
                        {
                            bake.Step(variantName == "main" ? budget : BudgetMs);
                            // What the scene does after each slice: re-sample the displayed time.
                            if (interleave)
                            {
                                var shown = (frames * 0.37f) % motion.Summary.durationSeconds;
                                motion.SampleFromSceneClock(token, shown);
                                bake.Apply(shown);
                            }
                        }
                        catch (Exception ex) { error = "bake step: " + ex; }
                        frames++;
                        yield return null;
                        var now = Time.realtimeSinceStartup; worst = Mathf.Max(worst, (now - frameStart) * 1000); frameStart = now;
                    }
                    if (error != null) break;
                    var bakedTicks = Math.Min(bake.BakedTicks, target);
                    try
                    {
                        var maxError = 0f; var errors = new List<(float err, int tick, string bone)>();
                        foreach (var tick in checkpoints)
                        {
                            motion.SampleFromSceneClock(token, tick / (float)MmdPhysicsBake.Rate);
                            if (!bake.Apply(tick / (float)MmdPhysicsBake.Rate)) throw new InvalidOperationException("baked tick " + tick + " not applied");
                            var pose = World(model); var at = 0; var err = 0f;
                            for (var i = 0; i < pose.Length; i++) { var d = Vector3.Distance(pose[i], reference[tick][i]); if (d > err) { err = d; at = i; } }
                            maxError = Mathf.Max(maxError, err);
                            errors.Add((err, tick, model.bones[at].name));
                        }
                        var bad = errors.Where(e => e.err > .01f).ToList();
                        variantLog.Add(variantName + ": max " + maxError.ToString("0.0000") + " m, ticks over 1 cm " + bad.Count + "/" + errors.Count +
                            (bad.Count > 0 ? " | worst " + string.Join(", ", bad.OrderByDescending(e => e.err).Take(4).Select(e => "t" + e.tick + " " + e.err.ToString("0.000") + " m @" + e.bone)) : ""));
                        if (control) result.controlMaxErrorM = Mathf.Max(result.controlMaxErrorM, maxError);
                        if (variantName == "main")
                        {
                            result.maxErrorM = maxError; result.checkpoints = checkpoints.Length;
                            result.cachedBones = bake.CachedBoneCount; result.totalTicks = bake.TotalTicks; result.fullLengthBytes = bake.Bytes;
                            result.bakeFrames = frames; result.worstFrameMs = worst; result.longestStepMs = (float)bake.LongestStepMilliseconds;
                            result.bakeWallSeconds = (float)wall.Elapsed.TotalSeconds;
                            result.bakeTicksPerSecond = bakedTicks / result.bakeWallSeconds;
                            result.fullLengthEstimateSeconds = bake.TotalTicks / result.bakeTicksPerSecond;
                            motion.SampleFromSceneClock(token, 0);
                            result.midTickApplies = bake.Apply(10.5f / MmdPhysicsBake.Rate);
                            result.beyondBakedRejected = bake.Complete || !bake.Apply((bake.BakedTicks + 5) / (float)MmdPhysicsBake.Rate);
                            result.jumpTicks = bake.JumpTicks; result.firstJumpSeconds = bake.FirstJumpSeconds;
                            result.worstJumpM = bake.WorstJumpMeters; result.worstJumpBone = bake.WorstJumpBone;
                            result.weightedBones = bake.WeightedBoneCount; result.positionTracks = bake.PositionTrackCount;
                        }
                    }
                    catch (Exception ex) { error = "verify: " + ex; }
                    motion.ReleaseSceneClock(token);
                    if (error != null) break;
                    yield return null;
                }
                result.variants = variantLog.ToArray();
                Release(model);
                if (error != null) { Finish(reportPath, report, results, error); yield break; }
                yield return null; yield return null;
            }
            string failure = null;
            foreach (var r in results)
            {
                // Compressed cache (int16 rotations, float16 position offsets), every tick: 5 mm.
                if (r.maxErrorM > 5e-3f) failure = r.name + ": bake differs from the uninterrupted simulation by " + r.maxErrorM + " m";
                else if (!r.midTickApplies || !r.beyondBakedRejected) failure = r.name + ": cache range handling failed";
            }
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
