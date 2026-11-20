using System;
using System.Collections.Generic;
using System.Diagnostics;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // PH-03 physics bake cache (docs/PH00_PHYSICS_INTEGRATION.md): one character's stateful 60 Hz
    // physics is computed ahead of playback a slice at a time (time budget per frame) and the
    // physics-affected bones' local poses are stored per tick. Playback samples the ordinary
    // physics-OFF pose and then overlays the cached bones (Apply), so seeking inside the baked
    // range is instant and repeatable.
    //
    // The bake runs on the model the scene displays: each step samples the motion with physics,
    // which moves the bone transforms, and the scene re-samples its own pose after every slice.
    // That physics-OFF sample also rewrites the shared solver state array; without restoring it
    // the first tick after every slice boundary is wrong, so the bake saves and restores it around
    // each step (docs/PH00_PHYSICS_INTEGRATION.md section 10).
    //
    // Storage (physics finish): rotation as 4 x int16 (8 B) and position as a float16 offset from
    // the bone's rest position (6 B). When the bake completes, bones whose position never moved
    // drop their position track. Jump check: weighted (visible) bones that move more than
    // JumpMeters in one tick are counted as instability.
    internal sealed class MmdPhysicsBake
    {
        internal const int Rate = 60;
        internal const float JumpMeters = .3f;
        private readonly MmdPocMotion motion;
        private readonly object owner;
        private readonly MmdStatefulPhysicsSolver solver;
        private readonly Transform[] bones;
        private readonly int[] cached;
        private readonly Vector3[] restPositions;
        private readonly bool[] weighted;
        private readonly short[] rotations;      // [tick * n + k] * 4
        private ushort[] positions;              // [tick * m + slot] * 3, slot = positionSlot[k]
        private int[] positionSlot;              // -1: constant (rest) position after compaction
        private int positionBones;
        private readonly Vector3[] previousWorld;
        // PH-04: part of each cached bone (null: not under a classified physics bone) and the
        // blend weight Apply uses between the physics-OFF pose (0) and the baked pose (1).
        private readonly string[] boneParts;
        private readonly float[] blend;
        private readonly int revision;

        internal int TotalTicks { get; }
        // Parts that occur in this model, in display order.
        internal string[] Parts { get; }

        // Strength of one part's physics, 0 (follow the motion only) .. 1 (baked physics).
        // Applied at playback; no re-bake.
        internal void SetStrength(string part, float strength)
        {
            strength = Mathf.Clamp01(strength);
            for (var k = 0; k < cached.Length; k++) if (boneParts[k] == part) blend[k] = strength;
        }
        internal int BakedTicks { get; private set; }
        internal bool Complete => BakedTicks >= TotalTicks;
        internal float BakedSeconds => Math.Max(0, BakedTicks - 1) / (float)Rate;
        internal float Progress => TotalTicks == 0 ? 1 : BakedTicks / (float)TotalTicks;
        internal int CachedBoneCount => cached.Length;
        internal int WeightedBoneCount { get; }
        internal int PositionTrackCount => positionBones;
        internal long Bytes => (long)rotations.Length * 2 + (long)positions.Length * 2;
        internal double LongestStepMilliseconds { get; private set; }
        // Ticks in which some visible physics bone moved more than JumpMeters.
        internal int JumpTicks { get; private set; }
        internal float FirstJumpSeconds { get; private set; } = -1;
        internal float WorstJumpMeters { get; private set; }
        internal string WorstJumpBone { get; private set; }
        // Probe-only control: bake without restoring the saved solver state.
        internal bool SkipStateRestoreForTest { get; set; }
        // Probe-only: skip the per-tick world-position reads of the jump check, to test whether
        // those reads (which force a transform-hierarchy sync) change the simulation.
        internal bool SkipJumpCheckForTest { get; set; }

        internal MmdPhysicsBake(MmdPocMotion motion, PMXImportResult model, object sceneOwner)
        {
            this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
            owner = sceneOwner;
            revision = motion.ClipRevision;
            var manager = model.mmdTransformResult.transformManager;
            if (manager == null || manager.physicsManager == null) throw new InvalidOperationException("Model has no physics");
            solver = new MmdStatefulPhysicsSolver(manager);
            cached = solver.PhysicsAffectedBones();
            bones = new Transform[cached.Length];
            restPositions = new Vector3[cached.Length];
            for (var k = 0; k < cached.Length; k++)
            {
                bones[k] = manager.bones[cached[k]].transform;
                restPositions[k] = manager.bones[cached[k]].initialLocalPosition;
            }
            weighted = WeightedBones(model, bones);
            var modelParts = MmdPhysicsParts.BoneParts(model.model);
            boneParts = new string[cached.Length]; blend = new float[cached.Length];
            for (var k = 0; k < cached.Length; k++) { boneParts[k] = modelParts[cached[k]]; blend[k] = 1; }
            var present = new HashSet<string>();
            foreach (var part in boneParts) if (part != null) present.Add(part);
            Parts = Array.FindAll(MmdPhysicsParts.Order, present.Contains);
            foreach (var w in weighted) if (w) WeightedBoneCount++;
            previousWorld = new Vector3[cached.Length];
            TotalTicks = (int)Math.Floor(motion.Summary.durationSeconds * (double)Rate + .0001) + 1;
            rotations = new short[(long)TotalTicks * cached.Length * 4];
            ResetPositionStorage();
            solver.Reset();
        }

        // Bones that deform at least one vertex of the model's skinned meshes.
        private static bool[] WeightedBones(PMXImportResult model, Transform[] cachedBones)
        {
            var index = new Dictionary<Transform, int>();
            for (var k = 0; k < cachedBones.Length; k++) index[cachedBones[k]] = k;
            var result = new bool[cachedBones.Length];
            foreach (var renderer in model.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                var rendererBones = renderer.bones;
                var used = new bool[rendererBones.Length];
                using (var weights = mesh.GetAllBoneWeights())
                    foreach (var weight in weights)
                        if (weight.weight > 0 && weight.boneIndex >= 0 && weight.boneIndex < used.Length) used[weight.boneIndex] = true;
                for (var b = 0; b < rendererBones.Length; b++)
                    if (used[b] && rendererBones[b] != null && index.TryGetValue(rendererBones[b], out var k)) result[k] = true;
            }
            return result;
        }

        private void ResetPositionStorage()
        {
            positionBones = cached.Length;
            positionSlot = new int[cached.Length];
            for (var k = 0; k < cached.Length; k++) positionSlot[k] = k;
            if (positions == null || positions.Length != (long)TotalTicks * cached.Length * 3)
                positions = new ushort[(long)TotalTicks * cached.Length * 3];
        }

        // Starts over from time zero (placement changed).
        internal void Restart()
        {
            solver.Reset();
            BakedTicks = 0;
            JumpTicks = 0; FirstJumpSeconds = -1; WorstJumpMeters = 0; WorstJumpBone = null;
            ResetPositionStorage();
        }

        // Advances the bake by whole ticks until the budget is used (at least one tick).
        // Returns the number of ticks computed. The displayed pose is left disturbed: the caller
        // must re-sample the scene afterwards.
        internal int Step(double budgetMilliseconds)
        {
            if (revision != motion.ClipRevision) throw new InvalidOperationException("Motion was replaced; the bake is stale");
            var watch = Stopwatch.StartNew();
            var steps = 0;
            while (!Complete && (steps == 0 || watch.Elapsed.TotalMilliseconds < budgetMilliseconds))
            {
                var tickStart = watch.Elapsed.TotalMilliseconds;
                var tick = BakedTicks;
                if (tick > 0 && !SkipStateRestoreForTest) solver.RestoreState();
                motion.SampleBakeStep(owner, tick / (float)Rate, tick == 0 ? 0 : 1f / Rate, solver);
                solver.SaveState();
                var n = cached.Length;
                var rotationOffset = (long)tick * n * 4;
                var positionOffset = (long)tick * n * 3;
                var jumped = false;
                for (var k = 0; k < n; k++)
                {
                    var p = bones[k].localPosition; var q = bones[k].localRotation;
                    if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(q.w)) throw new InvalidOperationException("Physics produced a non-finite pose at tick " + tick);
                    var r = rotationOffset + k * 4;
                    rotations[r] = Pack(q.x); rotations[r + 1] = Pack(q.y); rotations[r + 2] = Pack(q.z); rotations[r + 3] = Pack(q.w);
                    var d = p - restPositions[k]; var o = positionOffset + k * 3;
                    positions[o] = Mathf.FloatToHalf(d.x); positions[o + 1] = Mathf.FloatToHalf(d.y); positions[o + 2] = Mathf.FloatToHalf(d.z);
                    if (!weighted[k] || SkipJumpCheckForTest) continue;
                    var world = bones[k].position;
                    if (tick > 0)
                    {
                        var moved = Vector3.Distance(world, previousWorld[k]);
                        if (moved > WorstJumpMeters) { WorstJumpMeters = moved; WorstJumpBone = bones[k].name; }
                        if (moved > JumpMeters) jumped = true;
                    }
                    previousWorld[k] = world;
                }
                if (jumped) { JumpTicks++; if (FirstJumpSeconds < 0) FirstJumpSeconds = tick / (float)Rate; }
                BakedTicks++; steps++;
                LongestStepMilliseconds = Math.Max(LongestStepMilliseconds, watch.Elapsed.TotalMilliseconds - tickStart);
            }
            if (Complete && positionBones == cached.Length) CompactPositions();
            return steps;
        }

        // Drops the position track of bones that stayed at their rest position for every tick.
        private void CompactPositions()
        {
            var n = cached.Length;
            var keep = new List<int>();
            for (var k = 0; k < n; k++)
            {
                var moves = false;
                for (long t = 0; t < TotalTicks && !moves; t++)
                {
                    var o = t * n * 3 + k * 3;
                    moves = positions[o] != 0 || positions[o + 1] != 0 || positions[o + 2] != 0;
                }
                if (moves) keep.Add(k);
            }
            var compact = new ushort[(long)TotalTicks * keep.Count * 3];
            var slots = new int[n];
            for (var k = 0; k < n; k++) slots[k] = -1;
            for (var j = 0; j < keep.Count; j++) slots[keep[j]] = j;
            for (long t = 0; t < TotalTicks; t++)
                for (var j = 0; j < keep.Count; j++)
                    Array.Copy(positions, t * n * 3 + keep[j] * 3, compact, t * keep.Count * 3 + j * 3, 3);
            positions = compact; positionSlot = slots; positionBones = keep.Count;
        }

        private static short Pack(float value) => (short)Mathf.Clamp(Mathf.RoundToInt(value * 32767f), -32767, 32767);
        private Quaternion Rotation(long tick, int k)
        {
            var r = tick * cached.Length * 4 + k * 4;
            var q = new Quaternion(rotations[r] / 32767f, rotations[r + 1] / 32767f, rotations[r + 2] / 32767f, rotations[r + 3] / 32767f);
            return Quaternion.Normalize(q);
        }
        private Vector3 Position(long tick, int k)
        {
            var slot = positionSlot[k];
            if (slot < 0) return restPositions[k];
            var o = tick * positionBones * 3 + slot * 3;
            return restPositions[k] + new Vector3(Mathf.HalfToFloat(positions[o]), Mathf.HalfToFloat(positions[o + 1]), Mathf.HalfToFloat(positions[o + 2]));
        }

        // Overlays the cached physics bones at `seconds` on the current (physics-OFF) pose.
        // Returns false, leaving the pose unchanged, when that time is not baked yet.
        internal bool Apply(float seconds)
        {
            if (BakedTicks == 0) return false;
            var exact = Math.Max(0, seconds) * (double)Rate;
            var i0 = (int)Math.Floor(exact + 1e-6);
            if (i0 > BakedTicks - 1) return false;
            var i1 = Math.Min(i0 + 1, BakedTicks - 1);
            var t = (float)Math.Max(0, Math.Min(1, exact - i0));
            if (i1 == i0 || t < 1e-6f) t = 0;
            for (var k = 0; k < cached.Length; k++)
            {
                var w = blend[k];
                if (w <= 0) continue; // keep the physics-OFF pose already on the bone
                var p = t == 0 ? Position(i0, k) : Vector3.LerpUnclamped(Position(i0, k), Position(i1, k), t);
                var q = t == 0 ? Rotation(i0, k) : Quaternion.Slerp(Rotation(i0, k), Rotation(i1, k), t);
                if (w < 1)
                {
                    bones[k].GetLocalPositionAndRotation(out var offP, out var offQ);
                    p = Vector3.LerpUnclamped(offP, p, w); q = Quaternion.Slerp(offQ, q, w);
                }
                bones[k].SetLocalPositionAndRotation(p, q);
            }
            return true;
        }
    }
}
