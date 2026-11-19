using System;
using System.Collections.Generic;
using System.Reflection;
using UMT;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MmdStudio.Poc
{
    // Diagnostic reproduction of pinned UMT TransformAll, split at its existing
    // phase boundaries. Reflection avoids modifying/vendor-copying the package.
    // Must be checked against the untouched public solve before using evidence.
    internal sealed class PocPhysicsPhaseTrace
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] internal sealed class Bone
        {
            public string name;
            public Vector3 inputPosition, solvedPosition, worldPosition;
            public Quaternion inputRotation, solvedRotation;
            public bool solved, solvedByIK;
        }
        [Serializable] internal sealed class Phase
        {
            public int step;
            public string phase;
            public float dt, accumulator;
            public Bone[] bones;
        }
        [Serializable] internal sealed class Report
        {
            public bool equivalent;
            public string policy = "Mono reflection of pinned UMT TransformAll; wrapper counts are not native Bullet substep counts";
            public int solveCalls, prePhysicsCalls, physicsWrapperCalls, postPhysicsCalls;
            public float maximumReplayPositionError, maximumReplayRotationError;
            public Phase[] phases;
        }
        internal readonly Report Result = new Report();
        private readonly List<Phase> phases = new List<Phase>();
        internal bool RetainPhysicsSolverState;
        internal bool RetainOtherSolverHistory;
        private MMDBoneTransform.BoneSolverState[] previousStates;
        internal MMDBoneTransform.BoneSolverState[][] ExactInputs;
        internal HashSet<int> PhysicsControlled;
        internal static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
        internal static object Call(Type type, object target, string method, params object[] args)
            => type.GetMethod(method, Flags).Invoke(target, args);

        internal void Solve(MMDTransformManager manager, float dt)
        {
            var type = typeof(MMDTransformManager);
            Call(type, manager, "SyncIKHandles");
            Call(type, manager, "SampleBoneTransforms");
            Call(type, manager, "ResetTransforms");
            var context = Get(manager, "m_RuntimeContext");
            Set(context, "rootParentWorldMatrix", (float4x4)manager.transform.localToWorldMatrix);
            var physics = Get(manager.physicsManager, "m_PhysicsSolverContext");
            if (RetainPhysicsSolverState && previousStates != null)
            {
                var states = (NativeArray<MMDBoneTransform.BoneSolverState>)Get(context, "boneStateData");
                if (RetainOtherSolverHistory)
                    for (var i = 0; i < states.Length; i++)
                    {
                        var sampled = states[i];
                        var retained = previousStates[i];
                        retained.localPosition = sampled.localPosition;
                        retained.localRotation = sampled.localRotation;
                        retained.hasSolvedTransform = false;
                        retained.solvedByIK = false;
                        states[i] = retained;
                    }
                foreach (var body in manager.physicsManager.rigidBodies)
                    if (body.mode != PMXRigidBody.Mode.Kinetic && body.relatedBoneIndex >= 0)
                        states[body.relatedBoneIndex] = previousStates[body.relatedBoneIndex];
            }
            if (ExactInputs != null)
            {
                var states = (NativeArray<MMDBoneTransform.BoneSolverState>)Get(context, "boneStateData");
                for (var i = 0; i < states.Length; i++)
                {
                    if (PhysicsControlled.Contains(i)) continue;
                    var state = states[i];
                    state.localPosition = ExactInputs[Result.solveCalls][i].localPosition;
                    state.localRotation = ExactInputs[Result.solveCalls][i].localRotation;
                    state.hasSolvedTransform = false;
                    state.solvedByIK = false;
                    states[i] = state;
                }
            }
            Capture(manager, context, physics, "sample-reset", dt);
            var mathType = type.GetNestedType("TransformMath", Flags);
            var args = new object[] { context, false };
            Call(mathType, null, "TransformBones", args);
            context = args[0];
            Result.prePhysicsCalls++;
            Capture(manager, context, physics, "pre-physics", dt);
            args = new object[] { dt, context, physics };
            Call(typeof(MMDPhysicsManager), null, "TransformPhysics", args);
            context = args[1]; physics = args[2];
            Result.physicsWrapperCalls++;
            Capture(manager, context, physics, "post-bullet", dt);
            args = new object[] { context, true };
            Call(mathType, null, "TransformBones", args);
            context = args[0];
            Result.postPhysicsCalls++;
            Capture(manager, context, physics, "post-constraints", dt);
            Set(manager, "m_RuntimeContext", context);
            Set(manager.physicsManager, "m_PhysicsSolverContext", physics);
            Call(type, manager, "FlushBoneTransforms");
            args = new object[] { context, true };
            Call(typeof(MMDPhysicsManager), manager.physicsManager, "UpdateTransforms", args);
            Set(manager, "m_RuntimeContext", args[0]);
            Capture(manager, args[0], Get(manager.physicsManager, "m_PhysicsSolverContext"), "flushed", dt);
            if (RetainPhysicsSolverState)
                previousStates = ((NativeArray<MMDBoneTransform.BoneSolverState>)Get(args[0], "boneStateData")).ToArray();
            Result.solveCalls++;
        }

        internal void Capture(MMDTransformManager manager, object context, object physics, string phase, float dt)
        {
            if (Result.solveCalls > 2) return;
            var states = (NativeArray<MMDBoneTransform.BoneSolverState>)Get(context, "boneStateData");
            var bones = new Bone[states.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                var state = states[i];
                bones[i] = new Bone { name = manager.bones[i].boneName,
                    inputPosition = state.localPosition, inputRotation = state.localRotation,
                    solvedPosition = state.solvedLocalPosition, solvedRotation = state.solvedLocalRotation,
                    worldPosition = state.worldMatrix.c3.xyz,
                    solved = state.hasSolvedTransform, solvedByIK = state.solvedByIK };
            }
            phases.Add(new Phase { step = Result.solveCalls, phase = phase, dt = dt,
                accumulator = (float)Get(physics, "timeAccumulator"), bones = bones });
        }

        internal void Complete()
        {
            Result.equivalent = Result.maximumReplayPositionError <= .000001f && Result.maximumReplayRotationError <= .001f;
            Result.phases = phases.ToArray();
        }
    }
}
