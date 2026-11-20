using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // PH-05 (ADR-018): body secondary motion for chest/hip bones that have no rigid bodies, as a
    // project-owned spring-bone layer (damped verlet on each bone's tail, the approach of
    // DynamicBone / VRM SpringBone / Kawaii Physics). Baked ahead at 60 Hz like the physics cache
    // and overlaid on the displayed pose. The original PMX is never modified.
    internal sealed class MmdSpringCandidate
    {
        public int Bone;            // chain root that gets the spring
        public string Name;
        public string Part;         // "chest" or "hip"
        public bool HasRigidBody;   // root or descendants already simulated by Bullet
        public bool Weighted;       // root or descendants deform vertices
        public bool Usable => !HasRigidBody && Weighted;
    }

    internal static class MmdSpringBones
    {
        private static readonly string[] ChestKeys = { "胸", "おっぱい", "乳", "breast", "bust", "boob", "oppai" };
        private static readonly string[] HipKeys = { "尻", "ケツ", "臀", "屁股", "butt", "buttock" };
        // Names that look like chest/hip keys but are trunk bones.
        private static readonly string[] Excluded = { "上半身", "下半身", "腰", "胸元", "hips", "pelvis", "spine", "chest", "upperchest" };
        private static readonly string[] LegKeys = { "足", "ひざ", "膝", "脚", "leg", "knee", "foot" };

        internal static readonly string[] PresetIds = { "light", "medium", "strong" };
        internal static readonly Dictionary<string, string> PresetLabels = new Dictionary<string, string> { ["light"] = "약", ["medium"] = "중", ["strong"] = "강" };

        internal struct Preset { public float Stiffness, Drag, Gravity, MaxAngle; }
        // Per-tick fractions at 60 Hz. Gravity is relative to the bone length.
        internal static Preset PresetOf(string id)
        {
            switch (id)
            {
                case "light": return new Preset { Stiffness = .30f, Drag = .40f, Gravity = .004f, MaxAngle = 15 };
                case "strong": return new Preset { Stiffness = .10f, Drag = .14f, Gravity = .010f, MaxAngle = 35 };
                default: return new Preset { Stiffness = .18f, Drag = .25f, Gravity = .007f, MaxAngle = 25 };
            }
        }

        private static bool Has(string name, string[] keys) =>
            keys.Any(key => name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);

        // Chest/hip chain roots of the model with whether a spring can be added to them.
        internal static List<MmdSpringCandidate> Detect(PMXImportResult imported)
        {
            var model = imported.model;
            var n = model.bones.Length;
            var names = new string[n];
            for (var i = 0; i < n; i++) names[i] = model.bones[i].originalName.ToString() + " " + model.bones[i].originalNameEN.ToString();
            var children = new List<int>[n];
            for (var i = 0; i < n; i++) children[i] = new List<int>();
            for (var i = 0; i < n; i++) { var p = model.bones[i].parentBoneIndex; if (p >= 0 && p < n) children[p].Add(i); }
            IEnumerable<int> Subtree(int root)
            {
                var stack = new Stack<int>(); stack.Push(root);
                while (stack.Count > 0) { var b = stack.Pop(); yield return b; foreach (var c in children[b]) stack.Push(c); }
            }
            string PartOf(int i)
            {
                var name = names[i];
                if (Excluded.Any(e => name.Split(' ').Any(token => string.Equals(token, e, StringComparison.OrdinalIgnoreCase)))) return null;
                if (Has(name, ChestKeys)) return "chest";
                if (Has(name, HipKeys)) return "hip";
                return null;
            }
            var physical = new HashSet<int>();
            foreach (var body in model.rigidBodies)
                if (body.mode != PMXRigidBody.Mode.Kinetic && body.relatedBoneIndex >= 0 && body.relatedBoneIndex < n) physical.Add(body.relatedBoneIndex);
            var weighted = WeightedModelBones(imported);
            var result = new List<MmdSpringCandidate>();
            for (var i = 0; i < n; i++)
            {
                var part = PartOf(i);
                if (part == null) continue;
                var parent = model.bones[i].parentBoneIndex;
                if (parent >= 0 && PartOf(parent) == part) continue; // only chain roots
                var subtree = Subtree(i).ToList();
                if (subtree.Any(b => b != i && Has(names[b], LegKeys))) continue; // trunk bone, not a soft part
                result.Add(new MmdSpringCandidate
                {
                    Bone = i, Name = model.bones[i].originalName.ToString(), Part = part,
                    HasRigidBody = subtree.Any(physical.Contains), Weighted = subtree.Any(weighted.Contains),
                });
            }
            return result;
        }

        private static HashSet<int> WeightedModelBones(PMXImportResult imported)
        {
            var index = new Dictionary<Transform, int>();
            for (var i = 0; i < imported.bones.Length; i++) if (imported.bones[i] != null) index[imported.bones[i]] = i;
            var result = new HashSet<int>();
            foreach (var renderer in imported.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                var bones = renderer.bones;
                var used = new bool[bones.Length];
                using (var weights = mesh.GetAllBoneWeights())
                    foreach (var w in weights) if (w.weight > 0 && w.boneIndex >= 0 && w.boneIndex < used.Length) used[w.boneIndex] = true;
                for (var b = 0; b < bones.Length; b++) if (used[b] && bones[b] != null && index.TryGetValue(bones[b], out var i)) result.Add(i);
            }
            return result;
        }

        // Short summary for the inspector.
        internal static string Describe(List<MmdSpringCandidate> candidates)
        {
            if (candidates.Count == 0) return "가슴·엉덩이 본이 없어 신체 흔들림을 추가할 수 없습니다.";
            var parts = new List<string>();
            foreach (var part in new[] { "chest", "hip" })
            {
                var of = candidates.Where(c => c.Part == part).ToList();
                if (of.Count == 0) continue;
                var label = part == "chest" ? "가슴" : "엉덩이";
                var usable = of.Count(c => c.Usable);
                parts.Add(label + " " + of.Count + "개" + (usable == of.Count ? "(추가 가능)" : usable == 0 ? (of.Any(c => c.HasRigidBody) ? "(이미 물리 강체 있음)" : "(정점 가중치 없음)") : "(" + usable + "개 추가 가능)"));
            }
            return "감지: " + string.Join(" · ", parts);
        }
    }

    // Spring bake for one character: samples the physics-OFF pose per 60 Hz tick (scene-owned
    // motion), simulates each usable chain root's tail, stores the root's local rotation.
    internal sealed class MmdSpringBake
    {
        internal const int Rate = 60;
        private readonly MmdPocMotion motion;
        private readonly object owner;
        private readonly Transform[] roots;
        private readonly string[] parts;
        private readonly Vector3[] localTail;
        private readonly float[] length;
        private readonly Vector3[] tail, previousTail;
        private readonly short[] rotations;
        private readonly float[] blend;
        private readonly int revision;
        private MmdSpringBones.Preset preset;

        internal string PresetId { get; private set; }
        internal int TotalTicks { get; }
        internal int BakedTicks { get; private set; }
        internal bool Complete => BakedTicks >= TotalTicks;
        internal float Progress => TotalTicks == 0 ? 1 : BakedTicks / (float)TotalTicks;
        internal int RootCount => roots.Length;
        internal string[] Parts { get; }
        internal long Bytes => (long)rotations.Length * 2;
        internal double LongestStepMilliseconds { get; private set; }
        internal int JumpTicks { get; private set; }
        internal float MaxAngleUsed { get; private set; }

        internal MmdSpringBake(MmdPocMotion motion, PMXImportResult model, object sceneOwner, IEnumerable<MmdSpringCandidate> candidates, string presetId)
        {
            this.motion = motion; owner = sceneOwner; revision = motion.ClipRevision;
            var usable = candidates.Where(c => c.Usable).ToList();
            roots = usable.Select(c => model.bones[c.Bone]).ToArray();
            parts = usable.Select(c => c.Part).ToArray();
            Parts = parts.Distinct().ToArray();
            localTail = new Vector3[roots.Length]; length = new float[roots.Length];
            for (var k = 0; k < roots.Length; k++)
            {
                // Tail: the first child bone, else a point along the bone's local forward.
                var child = roots[k].childCount > 0 ? roots[k].GetChild(0) : null;
                var offset = child != null && child.localPosition.sqrMagnitude > 1e-8f ? child.localPosition : new Vector3(0, 0, .06f);
                localTail[k] = offset; length[k] = Mathf.Max(offset.magnitude, .01f);
            }
            tail = new Vector3[roots.Length]; previousTail = new Vector3[roots.Length];
            blend = Enumerable.Repeat(1f, roots.Length).ToArray();
            TotalTicks = (int)Math.Floor(motion.Summary.durationSeconds * (double)Rate + .0001) + 1;
            rotations = new short[(long)TotalTicks * roots.Length * 4];
            SetPreset(presetId);
        }

        // Changing the preset restarts the bake (the simulation changes).
        internal void SetPreset(string presetId)
        {
            PresetId = Array.IndexOf(MmdSpringBones.PresetIds, presetId) >= 0 ? presetId : "medium";
            preset = MmdSpringBones.PresetOf(PresetId);
            Restart();
        }

        internal void Restart() { BakedTicks = 0; JumpTicks = 0; MaxAngleUsed = 0; }

        internal void SetStrength(string part, float strength)
        {
            for (var k = 0; k < roots.Length; k++) if (parts[k] == part) blend[k] = Mathf.Clamp01(strength);
        }

        internal int Step(double budgetMilliseconds)
        {
            if (revision != motion.ClipRevision) throw new InvalidOperationException("Motion was replaced; the spring bake is stale");
            var watch = Stopwatch.StartNew(); var steps = 0;
            while (!Complete && (steps == 0 || watch.Elapsed.TotalMilliseconds < budgetMilliseconds))
            {
                var start = watch.Elapsed.TotalMilliseconds;
                var tick = BakedTicks;
                motion.SampleFromSceneClock(owner, tick / (float)Rate);
                var jumped = false;
                for (var k = 0; k < roots.Length; k++)
                {
                    var bone = roots[k];
                    var head = bone.position;
                    var animatedTail = bone.TransformPoint(localTail[k]);
                    var worldLength = Vector3.Distance(head, animatedTail);
                    if (worldLength < 1e-6f) worldLength = length[k];
                    if (tick == 0) { tail[k] = previousTail[k] = animatedTail; }
                    else
                    {
                        var velocity = (tail[k] - previousTail[k]) * (1 - preset.Drag);
                        var next = tail[k] + velocity + (animatedTail - tail[k]) * preset.Stiffness + Vector3.down * (preset.Gravity * worldLength);
                        var direction = (next - head).normalized;
                        var restDirection = (animatedTail - head).normalized;
                        var angle = Vector3.Angle(restDirection, direction);
                        if (angle > preset.MaxAngle) direction = Vector3.Slerp(restDirection, direction, preset.MaxAngle / angle);
                        next = head + direction * worldLength;
                        if (Vector3.Distance(next, tail[k]) > .3f) jumped = true;
                        previousTail[k] = tail[k]; tail[k] = next;
                    }
                    var swing = Quaternion.FromToRotation(animatedTail - head, tail[k] - head);
                    MaxAngleUsed = Mathf.Max(MaxAngleUsed, Quaternion.Angle(Quaternion.identity, swing));
                    var world = swing * bone.rotation;
                    var local = bone.parent != null ? Quaternion.Inverse(bone.parent.rotation) * world : world;
                    var o = ((long)tick * roots.Length + k) * 4;
                    rotations[o] = Pack(local.x); rotations[o + 1] = Pack(local.y); rotations[o + 2] = Pack(local.z); rotations[o + 3] = Pack(local.w);
                }
                if (jumped) JumpTicks++;
                BakedTicks++; steps++;
                LongestStepMilliseconds = Math.Max(LongestStepMilliseconds, watch.Elapsed.TotalMilliseconds - start);
            }
            return steps;
        }

        // Probe: two bakes of the same input must store identical rotations (determinism).
        internal bool SameAs(MmdSpringBake other) =>
            other != null && other.BakedTicks == BakedTicks && rotations.Length == other.rotations.Length && rotations.SequenceEqual(other.rotations);

        private static short Pack(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32767, 32767);
        private Quaternion Rotation(long tick, int k)
        {
            var o = (tick * roots.Length + k) * 4;
            return Quaternion.Normalize(new Quaternion(rotations[o] / 32767f, rotations[o + 1] / 32767f, rotations[o + 2] / 32767f, rotations[o + 3] / 32767f));
        }

        // Overlays the spring rotation at `seconds` on the current pose (blended by part strength).
        internal bool Apply(float seconds)
        {
            if (BakedTicks == 0) return false;
            var exact = Math.Max(0, seconds) * (double)Rate;
            var i0 = (int)Math.Floor(exact + 1e-6);
            if (i0 > BakedTicks - 1) return false;
            var i1 = Math.Min(i0 + 1, BakedTicks - 1);
            var t = (float)Math.Max(0, Math.Min(1, exact - i0));
            for (var k = 0; k < roots.Length; k++)
            {
                if (blend[k] <= 0) continue;
                var q = t < 1e-6f || i1 == i0 ? Rotation(i0, k) : Quaternion.Slerp(Rotation(i0, k), Rotation(i1, k), t);
                if (blend[k] < 1) q = Quaternion.Slerp(roots[k].localRotation, q, blend[k]);
                roots[k].localRotation = q;
            }
            return true;
        }
    }
}
