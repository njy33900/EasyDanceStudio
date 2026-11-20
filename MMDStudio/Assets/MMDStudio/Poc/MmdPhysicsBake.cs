using System;
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
    // Because that physics-OFF sample overwrites the solver state shared with the physics bones,
    // the solver state is saved after each bake step and restored before the next.
    internal sealed class MmdPhysicsBake
    {
        internal const int Rate = 60;
        private readonly MmdPocMotion motion;
        private readonly object owner;
        private readonly MmdStatefulPhysicsSolver solver;
        private readonly Transform[] bones;
        private readonly int[] cached;
        private readonly Vector3[] positions;
        private readonly Quaternion[] rotations;
        private readonly int revision;

        internal int TotalTicks { get; }
        internal int BakedTicks { get; private set; }
        internal bool Complete => BakedTicks >= TotalTicks;
        internal float BakedSeconds => Math.Max(0, BakedTicks - 1) / (float)Rate;
        internal float Progress => TotalTicks == 0 ? 1 : BakedTicks / (float)TotalTicks;
        internal int CachedBoneCount => cached.Length;
        internal long Bytes => (long)positions.Length * 12 + (long)rotations.Length * 16;
        internal double LongestStepMilliseconds { get; private set; }
        // Probe-only negative control: shows that interleaved scene sampling corrupts the bake
        // when the solver state is not restored.
        internal bool SkipStateRestoreForTest { get; set; }

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
            for (var k = 0; k < cached.Length; k++) bones[k] = manager.bones[cached[k]].transform;
            TotalTicks = (int)Math.Floor(motion.Summary.durationSeconds * (double)Rate + .0001) + 1;
            positions = new Vector3[(long)TotalTicks * cached.Length];
            rotations = new Quaternion[(long)TotalTicks * cached.Length];
            solver.Reset();
        }

        // Starts over from time zero, keeping the buffers (placement changed).
        internal void Restart()
        {
            solver.Reset();
            BakedTicks = 0;
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
                var offset = (long)tick * cached.Length;
                for (var k = 0; k < cached.Length; k++)
                {
                    var p = bones[k].localPosition; var q = bones[k].localRotation;
                    if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(q.w)) throw new InvalidOperationException("Physics produced a non-finite pose at tick " + tick);
                    positions[offset + k] = p; rotations[offset + k] = q;
                }
                BakedTicks++; steps++;
                LongestStepMilliseconds = Math.Max(LongestStepMilliseconds, watch.Elapsed.TotalMilliseconds - tickStart);
            }
            return steps;
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
            long a = (long)i0 * cached.Length, b = (long)i1 * cached.Length;
            for (var k = 0; k < cached.Length; k++)
            {
                var p = t == 0 ? positions[a + k] : Vector3.LerpUnclamped(positions[a + k], positions[b + k], t);
                var q = t == 0 ? rotations[a + k] : Quaternion.Slerp(rotations[a + k], rotations[b + k], t);
                bones[k].SetLocalPositionAndRotation(p, q);
            }
            return true;
        }
    }
}
