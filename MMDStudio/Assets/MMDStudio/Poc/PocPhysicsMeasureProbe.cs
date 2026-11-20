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
    // PH-00 measurements (docs/PH00_PHYSICS_INTEGRATION.md): stateful 60 Hz physics on real models
    // with the full-length motion, outside the scene UI. M1 step cost (each model alone and both
    // together), M2 seek cost, M3 rigid-body classification and per-part motion, M4 repeated
    // session/model lifetime, M5 interaction between two overlapping characters.
    public sealed class PocPhysicsMeasureProbe : MonoBehaviour
    {
        [Serializable] private sealed class PartStats
        {
            public string part;
            public int bodies, dynamic, boneAligned;
            public string[] sampleNames;
            public float maxStepDisplacementM, p99StepDisplacementM;
            // Ticks (after the first) where some bone of this part moved more than 0.3 m in 1/60 s
            // (18 m/s): a sign of simulation instability rather than dance motion.
            public int jumpTicks;
            public float firstJumpSeconds;
            public string worstBody;
        }
        [Serializable] private sealed class ModelStats
        {
            public string name;
            public int bones, rigidBodies, kinetic, dynamic, boneAligned, joints;
            public float stepAvgMs, stepP99Ms, stepMaxMs;
            public int ticks;
            public bool finite;
            public PartStats[] parts;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, configuration, motion;
            public float motionSeconds;
            public ModelStats[] models;
            public float bothStepAvgMs, bothStepP99Ms, bothStepMaxMs;
            public int bothTicks;
            public float seek30Seconds, seek60Seconds, seekFullEstimateSeconds, secondSeek30Seconds;
            public bool seekRepeatIdentical;
            public float interactionMaxDifferenceM;
            public int sessionCycles, reloadCycles;
            public string objectsBefore, objectsAfter;
            public long allocatedGrowthBytes;
            public string[] notes;
        }

        private sealed class Loaded
        {
            public PMXImportResult model;
            public MmdPocMotion motion;
            public MmdPhysicsPlayback session;
            public int[] physicalBones;
            public string[] boneParts;
        }

        private static readonly (string part, string[] keys)[] Parts =
        {
            ("accessory", new[] { "飾", "饰", "アクセ", "イヤリング", "ピアス", "帽", "しっぽ", "尻尾", "尾巴", "tail" }),
            ("chest", new[] { "胸", "おっぱい", "乳", "bust", "breast" }),
            ("hip", new[] { "尻", "ケツ", "臀", "屁股", "hip", "butt" }),
            ("hair", new[] { "髪", "发", "髮", "もみあげ", "アホ毛", "ツイン", "hair" }),
            ("cloth", new[] { "スカート", "裾", "袖", "服", "コート", "マント", "クローク", "ケープ", "マフラー", "リボン", "ネクタイ", "フード", "裙", "衣", "带", "skirt", "cloth", "ribbon", "coat", "cloak", "cape", "hem" }),
        };

        private static string Classify(string bodyName, string boneName)
        {
            foreach (var (part, keys) in Parts)
                if (keys.Any(key => (bodyName ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (boneName ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                    return part;
            return "other";
        }

        private static string Counts()
        {
            return "Mesh " + Resources.FindObjectsOfTypeAll<Mesh>().Length + ", Material " + Resources.FindObjectsOfTypeAll<Material>().Length +
                   ", Texture2D " + Resources.FindObjectsOfTypeAll<Texture2D>().Length + ", GameObject " + Resources.FindObjectsOfTypeAll<GameObject>().Length +
                   ", ScriptableObject " + Resources.FindObjectsOfTypeAll<ScriptableObject>().Length;
        }

        private static long Allocated()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            return UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        }

        private static float Percentile(List<float> values, float p)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        }

        private IEnumerator Load(string pmx, string vmd, Action<Loaded> done, Action<string> fail)
        {
            var task = MmdPocProbe.LoadAsync(pmx, null, CancellationToken.None);
            while (!task.IsCompleted) yield return null;
            if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) { fail("PMX load failed: " + task.Exception?.GetBaseException().Message); yield break; }
            var loaded = new Loaded { model = task.Result.imported };
            loaded.motion = loaded.model.root.AddComponent<MmdPocMotion>();
            var motionTask = loaded.motion.LoadAsync(loaded.model, vmd, "direct-default-ik", null, CancellationToken.None);
            while (!motionTask.IsCompleted) yield return null;
            if (motionTask.IsFaulted) { Release(loaded); fail("VMD load failed: " + motionTask.Exception.GetBaseException().Message); yield break; }
            loaded.motion.Playing = false;
            var bodies = loaded.model.metadata.rigidBodies;
            var bones = loaded.model.model.bones;
            loaded.boneParts = new string[bones.Length];
            var physical = new HashSet<int>();
            foreach (var body in bodies)
            {
                if (body.mode == PMXRigidBody.Mode.Kinetic || body.relatedBoneIndex < 0 || body.relatedBoneIndex >= bones.Length) continue;
                physical.Add(body.relatedBoneIndex);
                loaded.boneParts[body.relatedBoneIndex] = Classify(body.originalName, bones[body.relatedBoneIndex].originalName.ToString());
            }
            loaded.physicalBones = physical.OrderBy(i => i).ToArray();
            done(loaded);
        }

        private static void Release(Loaded loaded)
        {
            if (loaded == null) return;
            loaded.session?.Dispose(); loaded.session = null;
            var owner = loaded.model?.root != null ? loaded.model.root.GetComponentInParent<PocAssetLifetime>() : null;
            if (owner != null) owner.Release();
        }

        private static ModelStats Describe(string name, Loaded loaded)
        {
            var bodies = loaded.model.metadata.rigidBodies;
            var bones = loaded.model.model.bones;
            var stats = new ModelStats
            {
                name = name, bones = bones.Length, rigidBodies = bodies.Count, joints = loaded.model.model.joints.Length,
                kinetic = bodies.Count(b => b.mode == PMXRigidBody.Mode.Kinetic),
                dynamic = bodies.Count(b => b.mode == PMXRigidBody.Mode.Dynamic),
                boneAligned = bodies.Count(b => b.mode == PMXRigidBody.Mode.DynamicBoneAligned),
            };
            stats.parts = Parts.Select(p => p.part).Concat(new[] { "other" }).Select(part =>
            {
                var matching = bodies.Where(b => b.mode != PMXRigidBody.Mode.Kinetic && b.relatedBoneIndex >= 0 && b.relatedBoneIndex < bones.Length &&
                                                 Classify(b.originalName, bones[b.relatedBoneIndex].originalName.ToString()) == part).ToList();
                return new PartStats
                {
                    part = part, bodies = matching.Count,
                    dynamic = matching.Count(b => b.mode == PMXRigidBody.Mode.Dynamic),
                    boneAligned = matching.Count(b => b.mode == PMXRigidBody.Mode.DynamicBoneAligned),
                    sampleNames = matching.Take(8).Select(b => b.originalName).ToArray(),
                };
            }).ToArray();
            return stats;
        }

        // Runs `ticks` 60 Hz steps on the given sessions, a few per rendered frame so the window
        // stays responsive. Records per-tick cost and per-part bone displacement for `measured`.
        private sealed class Jumps { public int ticks; public float first = -1; public float worst; public string worstBody; }
        private readonly Dictionary<string, Jumps> jumps = new Dictionary<string, Jumps>();

        private IEnumerator Run(List<Loaded> sessions, int ticks, List<float> costs, Dictionary<string, List<float>> displacement, Loaded measured, Action<bool> finiteResult)
        {
            jumps.Clear();
            var finite = true;
            Vector3[] previous = measured != null ? measured.physicalBones.Select(i => measured.model.bones[i].position).ToArray() : null;
            var watch = new Stopwatch();
            for (var tick = 0; tick < ticks; tick++)
            {
                watch.Restart();
                foreach (var loaded in sessions) loaded.session.Advance(1.0 / MmdPhysicsPlayback.Rate);
                watch.Stop();
                costs.Add((float)watch.Elapsed.TotalMilliseconds);
                if (measured != null)
                {
                    var jumpedParts = new HashSet<string>();
                    for (var k = 0; k < measured.physicalBones.Length; k++)
                    {
                        var position = measured.model.bones[measured.physicalBones[k]].position;
                        if (float.IsNaN(position.x) || float.IsInfinity(position.x) || position.magnitude > 1000) finite = false;
                        var part = measured.boneParts[measured.physicalBones[k]] ?? "other";
                        var moved = Vector3.Distance(position, previous[k]);
                        previous[k] = position;
                        if (tick == 0) continue; // the first tick leaves the reset pose
                        if (!displacement.TryGetValue(part, out var list)) displacement[part] = list = new List<float>();
                        list.Add(moved);
                        if (!jumps.TryGetValue(part, out var jump)) jumps[part] = jump = new Jumps();
                        if (moved > jump.worst) { jump.worst = moved; jump.worstBody = measured.model.bones[measured.physicalBones[k]].name + " @" + (tick / 60f).ToString("0.00") + "s"; }
                        if (moved > .3f && jumpedParts.Add(part)) { jump.ticks++; if (jump.first < 0) jump.first = tick / 60f; }
                    }
                }
                if (tick % 4 == 3) yield return null;
            }
            finiteResult(finite);
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
                configuration = Debug.isDebugBuild ? "Debug" : "Release",
                motion = PocArguments.Value("-pocVmd"),
            };
            var notes = new List<string>();
            var gene = PocArguments.Value("-pocPmx"); var second = PocArguments.Value("-pocSecondPmx");
            if (string.IsNullOrEmpty(gene) || string.IsNullOrEmpty(second) || string.IsNullOrEmpty(report.motion))
            { Finish(reportPath, report, notes, "Need -pocPmx, -pocSecondPmx and -pocVmd"); yield break; }
            yield return null;
            long allocatedBefore = 0;
            Loaded a = null, b = null; string error = null;
            yield return Load(gene, report.motion, l => a = l, e => error = e);
            if (error == null) yield return Load(second, report.motion, l => b = l, e => error = e);
            if (error != null) { Release(a); Release(b); Finish(reportPath, report, notes, error); yield break; }
            report.motionSeconds = a.motion.Summary.durationSeconds;
            b.model.root.transform.parent.position = new Vector3(30, 0, 0); // apart for M1

            ModelStats sa = null, sb = null;
            try
            {
                sa = Describe(Path.GetFileNameWithoutExtension(gene), a); sb = Describe("second", b);
                a.session = new MmdPhysicsPlayback(a.motion, a.model) { Playing = true };
                b.session = new MmdPhysicsPlayback(b.motion, b.model) { Playing = true };
            }
            catch (Exception ex) { Release(a); Release(b); Finish(reportPath, report, notes, ex.ToString()); yield break; }

            // M1/M3: each model alone over the first 20 s, then both together for 10 s.
            const int Ticks = 1200;
            foreach (var (loaded, stats) in new[] { (a, sa), (b, sb) })
            {
                var costs = new List<float>(); var displacement = new Dictionary<string, List<float>>(); var finite = true;
                loaded.session.Seek(0); loaded.session.Playing = true;
                yield return Run(new List<Loaded> { loaded }, Ticks, costs, displacement, loaded, f => finite = f);
                stats.ticks = Ticks; stats.finite = finite;
                stats.stepAvgMs = costs.Average(); stats.stepP99Ms = Percentile(costs, .99f); stats.stepMaxMs = costs.Max();
                foreach (var part in stats.parts)
                    if (displacement.TryGetValue(part.part, out var list) && list.Count > 0)
                    { part.maxStepDisplacementM = list.Max(); part.p99StepDisplacementM = Percentile(list, .99f); }
                foreach (var part in stats.parts)
                    if (jumps.TryGetValue(part.part, out var jump)) { part.jumpTicks = jump.ticks; part.firstJumpSeconds = jump.first; part.worstBody = jump.worstBody; }
            }
            report.models = new[] { sa, sb };
            {
                var costs = new List<float>();
                a.session.Seek(0); b.session.Seek(0); a.session.Playing = b.session.Playing = true;
                yield return Run(new List<Loaded> { a, b }, 600, costs, null, null, f => { });
                report.bothTicks = 600; report.bothStepAvgMs = costs.Average(); report.bothStepP99Ms = Percentile(costs, .99f); report.bothStepMaxMs = costs.Max();
            }
            yield return null;

            // M2: seek = reset + replay from zero.
            {
                var watch = Stopwatch.StartNew(); a.session.Seek(30); report.seek30Seconds = (float)watch.Elapsed.TotalSeconds;
                yield return null;
            }
            {
                var watch = Stopwatch.StartNew(); a.session.Seek(60); report.seek60Seconds = (float)watch.Elapsed.TotalSeconds;
                report.seekFullEstimateSeconds = report.seek60Seconds / 60f * report.motionSeconds;
                yield return null;
                var first = a.motion.PosePositions(); a.session.Seek(60); var again = a.motion.PosePositions();
                report.seekRepeatIdentical = first.Zip(again, (x, y) => Vector3.Distance(x, y)).All(d => d == 0);
                yield return null;
                watch = Stopwatch.StartNew(); b.session.Seek(30); report.secondSeek30Seconds = (float)watch.Elapsed.TotalSeconds;
            }
            yield return null;

            // M5: Gene alone vs with the second character overlapping at the same place.
            {
                b.session.Playing = false;
                b.model.root.transform.parent.position = new Vector3(30, 0, 0);
                a.session.Seek(0); a.session.Playing = true;
                yield return Run(new List<Loaded> { a }, 300, new List<float>(), null, null, f => { });
                var alone = a.physicalBones.Select(i => a.model.bones[i].position).ToArray();
                b.model.root.transform.parent.position = a.model.root.transform.parent.position;
                a.session.Seek(0); b.session.Seek(0); a.session.Playing = b.session.Playing = true;
                yield return Run(new List<Loaded> { a, b }, 300, new List<float>(), null, null, f => { });
                var together = a.physicalBones.Select(i => a.model.bones[i].position).ToArray();
                report.interactionMaxDifferenceM = alone.Zip(together, (x, y) => Vector3.Distance(x, y)).DefaultIfEmpty(0).Max();
            }
            yield return null;

            // M4: repeated physics sessions on one model, then full reload cycles.
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    a.session.Dispose();
                    a.session = new MmdPhysicsPlayback(a.motion, a.model) { Playing = true };
                    for (var t = 0; t < 30; t++) a.session.Advance(1.0 / MmdPhysicsPlayback.Rate);
                    report.sessionCycles++;
                }
            }
            catch (Exception ex) { notes.Add("session cycle failed: " + ex.Message); }
            Release(a); Release(b); a = b = null;
            yield return null; yield return null;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Loaded c = null; error = null;
                yield return Load(gene, report.motion, l => c = l, e => error = e);
                if (error != null) { notes.Add("reload failed: " + error); break; }
                try
                {
                    c.session = new MmdPhysicsPlayback(c.motion, c.model) { Playing = true };
                    for (var t = 0; t < 60; t++) c.session.Advance(1.0 / MmdPhysicsPlayback.Rate);
                }
                catch (Exception ex) { notes.Add("reload session failed: " + ex.Message); }
                Release(c);
                yield return null; yield return null;
                report.reloadCycles++;
                if (cycle == 0)
                {
                    // Baseline after one full cycle: shaders/toon textures loaded once from
                    // Resources by the first import are not leaks.
                    var warm = Resources.UnloadUnusedAssets();
                    while (!warm.isDone) yield return null;
                    report.objectsBefore = Counts(); allocatedBefore = Allocated();
                }
            }
            var unload = Resources.UnloadUnusedAssets();
            while (!unload.isDone) yield return null;
            report.objectsAfter = Counts();
            report.allocatedGrowthBytes = Allocated() - allocatedBefore;

            string failure = null;
            if (!report.models.All(m => m.finite)) failure = "non-finite or out-of-range physics pose";
            else if (!report.seekRepeatIdentical) failure = "repeated seek gave a different pose";
            else if (report.objectsBefore != report.objectsAfter) failure = "objects left after releasing all physics models";
            Finish(reportPath, report, notes, failure);
        }

        private static void Finish(string path, Report report, List<string> notes, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.notes = notes.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
