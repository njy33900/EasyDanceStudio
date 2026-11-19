using System;
using UnityEngine;

namespace UMT
{
    // Compiled into the pinned UMT.Runtime assembly through an asmref. This
    // project-owned adapter uses internal typed APIs; no reflection or package
    // cache edits. Revalidate this adapter when upgrading UMT.
    public sealed class MmdStatefulPhysicsSolver
    {
        private readonly MMDTransformManager manager;
        private readonly bool[] physical;
        public MmdStatefulPhysicsSolver(MMDTransformManager manager)
        {
            this.manager = manager;
            physical = new bool[manager.bones.Length];
            foreach (var body in manager.physicsManager.rigidBodies)
                if (body.mode != PMXRigidBody.Mode.Kinetic && body.relatedBoneIndex >= 0)
                    physical[body.relatedBoneIndex] = true;
        }

        public void Reset()
        {
            manager.ResetToBindPose();
            ref var context = ref manager.Context;
            for (var i = 0; i < context.boneStateData.Length; i++)
            {
                var state = MMDBoneTransform.BoneSolverState.CreateDefault();
                state.localPosition = context.boneConfigData[i].initialLocalPosition;
                state.localRotation = context.boneConfigData[i].initialLocalRotation;
                context.boneStateData[i] = state;
            }
        }

        public void Solve(float deltaSeconds)
        {
            if (manager.transformEnabled || manager.livePhysics)
                throw new InvalidOperationException("Stateful solver requires exclusive manual ownership");
            ref var context = ref manager.Context;
            context.rootParentWorldMatrix = manager.transform.localToWorldMatrix;
            context.solveConstraints = manager.solveConstraints;
            context.solveIK = manager.solveIK;
            for (var i = 0; i < manager.bones.Length; i++)
            {
                if (physical[i]) continue;
                var state = context.boneStateData[i];
                state.localPosition = manager.bones[i].transform.localPosition;
                state.localRotation = manager.bones[i].transform.localRotation;
                state.hasSolvedTransform = false;
                state.solvedByIK = false;
                context.boneStateData[i] = state;
            }
            for (var i = 0; i < context.ikControllers.Length; i++)
            {
                var controller = context.ikControllers[i];
                controller.enabled = manager.bones[controller.controllerBoneIndex].ikEnabled;
                context.ikControllers[i] = controller;
            }
            MMDTransformManager.TransformBonesWithPhysics(ref context, ref manager.physicsManager.Context, deltaSeconds);
            for (var i = 0; i < manager.bones.Length; i++)
            {
                var state = context.boneStateData[i];
                Vector3 position = state.hasSolvedTransform ? state.solvedLocalPosition : state.localPosition;
                Quaternion rotation = state.hasSolvedTransform ? state.solvedLocalRotation : state.localRotation;
                manager.bones[i].transform.SetLocalPositionAndRotation(position, rotation);
                manager.bones[i].solverResetPending = false;
            }
            manager.physicsManager.UpdateTransforms(ref context, true);
        }
    }
}
