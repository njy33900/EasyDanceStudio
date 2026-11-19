using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UMT;
using Unity.Collections;
using UnityEngine;
using static MmdStudio.Poc.PocPhysicsPhaseTrace;

namespace MmdStudio.Poc
{
    // Pinned Mono diagnostics. Uses the converter's actual private sample builder
    // and solver methods rather than an independently approximated evaluator.
    internal sealed class PocPhysicsContextProbe
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        [Serializable] internal sealed class Difference { public string field, runtime, converter; }
        [Serializable] internal sealed class PhaseError
        {
            public int frame;
            public string phase, worstWorldBone;
            public float inputPosition, inputRotation, solvedPosition, solvedRotation, worldPosition;
            public int validityDifferences;
        }
        [Serializable] internal sealed class ContextReport
        {
            public bool converterReplayParity;
            public int configurationDifferences, physicalSelectionDifferences;
            public float maximumReplayPositionError, maximumReplayRotationError;
            public Difference[] differences;
            public PhaseError[] phases;
            public PocPhysicsPhaseTrace.Phase[] converterPhases;
            public uint runtimeSeed;
            public bool runtimeGround;
            public Replay sameInputs, sameInputsAndHistory;
        }
        [Serializable] internal sealed class Replay
        {
            public bool parityPassed;
            public float position, rotation;
            public int firstDivergentFrame = -1;
        }
        internal readonly ContextReport Result = new ContextReport();
        internal Vector3[][] Positions;
        internal Quaternion[][] Rotations;
        internal MMDBoneTransform.BoneSolverState[][] InputStates;
        internal Vector3[][][] ExperimentPositions = new Vector3[2][][];
        internal Quaternion[][][] ExperimentRotations = new Quaternion[2][][];
        private readonly List<Difference> differences = new List<Difference>();
        private readonly List<IDisposable> allocations = new List<IDisposable>();
        private object ArrayOf(Type element, int length)
        {
            var value = Activator.CreateInstance(typeof(NativeArray<>).MakeGenericType(element), length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            allocations.Add((IDisposable)value);
            return value;
        }
        private void Compare(string path, object a, object b)
        {
            var type = a.GetType();
            if (type.IsPrimitive || type.IsEnum)
            {
                var equal = type == typeof(float) ? Math.Abs((float)a - (float)b) <= .000001f : a.Equals(b);
                if (!equal)
                {
                    Result.configurationDifferences++;
                    if (differences.Count < 500) differences.Add(new Difference { field=path,
                        runtime=Convert.ToString(a, CultureInfo.InvariantCulture), converter=Convert.ToString(b, CultureInfo.InvariantCulture) });
                }
                return;
            }
            foreach (var field in type.GetFields(Flags))
                if (!field.IsStatic) Compare(path + "." + field.Name, field.GetValue(a), field.GetValue(b));
        }
        private void CompareArray(string field, object a, object b)
        {
            var type = a.GetType();
            var length = (int)type.GetProperty("Length").GetValue(a);
            var otherLength = (int)type.GetProperty("Length").GetValue(b);
            Compare(field + ".Length", length, otherLength);
            for (var i = 0; i < Math.Min(length, otherLength); i++)
                Compare(field + "[" + i + "]", type.GetProperty("Item").GetValue(a, new object[]{i}), type.GetProperty("Item").GetValue(b, new object[]{i}));
        }

        internal void Run(PMXImportResult model, VMDAnimation animation, int[] controlled, PocPhysicsPhaseTrace.Report runtimeTrace)
        {
            if (animation.showIKFrames.Length != 0) throw new InvalidOperationException("Context probe currently requires the controlled fixture without IK toggle keys");
            var manager = model.mmdTransformResult.transformManager;
            manager.ResetToBindPose();
            var type = typeof(MMDTransformManager);
            var physicsType = typeof(MMDPhysicsManager);
            object context = Activator.CreateInstance(type.GetNestedType("SolverContext", Flags));
            object physics = Activator.CreateInstance(physicsType.GetNestedType("PhysicsSolverContext", Flags));
            try
            {
                var args = new object[]{model.model, context};
                Call(type, null, "InitializeSolverContext", args); context = args[1];
                args = new object[]{model.model, 0u, true, physics};
                Call(physicsType, null, "InitializePhysicsContext", args); physics = args[3];
                var runtime = Get(manager, "m_RuntimeContext");
                var runtimePhysics = Get(manager.physicsManager, "m_PhysicsSolverContext");
                foreach (var field in new[]{"boneConfigData", "prePhysicsBoneIndices", "afterPhysicsBoneIndices", "ikControllerByBoneIndices", "ikControllers", "ikLinks"})
                    CompareArray(field, Get(runtime, field), Get(context, field));
                foreach (var field in new[]{"rigidBodySimulationData", "kineticRigidBodyIndices", "sortedSimulatedRigidBodyIndices"})
                    CompareArray(field, Get(runtimePhysics, field), Get(physics, field));
                foreach (var field in new[]{"solveConstraints", "solveIK", "rootParentWorldMatrix"}) Compare(field, Get(runtime, field), Get(context, field));
                Result.runtimeSeed = manager.physicsManager.physicsSeed;
                Result.runtimeGround = manager.physicsManager.enableGroundCollision;
                Compare("physicsSeed", Result.runtimeSeed, 0u);
                Compare("enableGroundCollision", Result.runtimeGround, true);
                var selected = (NativeArray<bool>)ArrayOf(typeof(bool), model.bones.Length);
                args = new object[]{physics, selected};
                Call(physicsType, null, "BuildPhysicsControlledBoneSelection", args);
                selected = (NativeArray<bool>)args[1];
                var controlledSet = new HashSet<int>(controlled);
                for (var i=0; i<selected.Length; i++) if (selected[i] != controlledSet.Contains(i)) Result.physicalSelectionDifferences++;
                Result.differences = differences.ToArray();

                var converterType = typeof(VMDAnimationClipConverter);
                var mathType = converterType.GetNestedType("AnimationMath", Flags);
                var resolver = Activator.CreateInstance(converterType.GetNestedType("IndexResolver", Flags), model.model, Allocator.Persistent);
                allocations.Add((IDisposable)resolver);
                var config = Get(context, "boneConfigData");
                args = new object[]{animation, resolver, config, model.bones.Length, 91, Allocator.Persistent};
                var frames = Call(converterType, null, "BuildSortedResolvedBoneFrames", args);
                allocations.Add((IDisposable)frames);
                var sourceSelection = ArrayOf(typeof(bool), model.bones.Length);
                var tracks = ArrayOf(typeof(int), model.bones.Length);
                object indices = new NativeList<int>(Allocator.Persistent);
                allocations.Add((IDisposable)indices);
                args = new object[]{frames, sourceSelection, tracks, indices};
                Call(mathType, null, "BuildSourceBoneTracks", args);
                tracks=args[2]; indices=args[3];
                var count=(int)indices.GetType().GetProperty("Length").GetValue(indices);
                var samples=ArrayOf(converterType.GetNestedType("BoneSample", Flags), count*91);
                args=new object[]{frames, tracks, samples, 91};
                Call(mathType,null,"FillCompactBoneSamples",args); samples=args[2];
                var last=ArrayOf(typeof(int),model.bones.Length);
                args=new object[]{config, indices, samples, 91, last};
                Call(mathType,null,"InterpolateCompactBoneSamples",args); samples=args[2];
                var trace=new PocPhysicsPhaseTrace();
                Positions=new Vector3[91][]; Rotations=new Quaternion[91][];
                InputStates=new MMDBoneTransform.BoneSolverState[91][];
                for(var frame=0;frame<=90;frame++)
                {
                    args=new object[]{config, Get(context,"boneStateData"), tracks, selected, samples, 91, frame, true};
                    Call(mathType,null,"ApplyCompactBoneSamples",args);
                    InputStates[frame]=((NativeArray<MMDBoneTransform.BoneSolverState>)Get(context,"boneStateData")).ToArray();
                    // The controlled fixture has no IK toggles. Standalone context
                    // initializes every IK controller enabled, matching the bake.
                    var dt=frame==0?0:1f/30;
                    trace.Capture(manager,context,physics,"sample-reset",dt);
                    var transformMath=type.GetNestedType("TransformMath",Flags);
                    args=new object[]{context,false}; Call(transformMath,null,"TransformBones",args); context=args[0];
                    trace.Capture(manager,context,physics,"pre-physics",dt);
                    args=new object[]{dt,context,physics}; Call(physicsType,null,"TransformPhysics",args); context=args[1]; physics=args[2];
                    trace.Capture(manager,context,physics,"post-bullet",dt);
                    args=new object[]{context,true}; Call(transformMath,null,"TransformBones",args); context=args[0];
                    trace.Capture(manager,context,physics,"post-constraints",dt);
                    var states=(NativeArray<MMDBoneTransform.BoneSolverState>)Get(context,"boneStateData");
                    Positions[frame]=new Vector3[controlled.Length]; Rotations[frame]=new Quaternion[controlled.Length];
                    for(var i=0;i<controlled.Length;i++)
                    {
                        var state=states[controlled[i]];
                        Positions[frame][i]=state.hasSolvedTransform?state.solvedLocalPosition:state.localPosition;
                        Rotations[frame][i]=state.hasSolvedTransform?state.solvedLocalRotation:state.localRotation;
                    }
                    trace.Result.solveCalls++;
                }
                trace.Complete();
                Result.converterPhases=trace.Result.phases;
                var errors=new List<PhaseError>();
                foreach(var phase in trace.Result.phases)
                {
                    var match=System.Array.Find(runtimeTrace.phases,p=>p.step==phase.step&&p.phase==phase.phase);
                    var error=new PhaseError{frame=phase.step,phase=phase.phase};
                    for(var i=0;i<phase.bones.Length;i++)
                    {
                        var a=match.bones[i];var b=phase.bones[i];
                        error.inputPosition=Mathf.Max(error.inputPosition,Vector3.Distance(a.inputPosition,b.inputPosition));
                        error.inputRotation=Mathf.Max(error.inputRotation,Quaternion.Angle(a.inputRotation,b.inputRotation));
                        if(a.solved&&b.solved)
                        {
                            error.solvedPosition=Mathf.Max(error.solvedPosition,Vector3.Distance(a.solvedPosition,b.solvedPosition));
                            error.solvedRotation=Mathf.Max(error.solvedRotation,Quaternion.Angle(a.solvedRotation,b.solvedRotation));
                        }
                        var world=Vector3.Distance(a.worldPosition,b.worldPosition);
                        if(world>error.worldPosition){error.worldPosition=world;error.worstWorldBone=a.name;}
                        if(a.solved!=b.solved||a.solvedByIK!=b.solvedByIK)error.validityDifferences++;
                    }
                    errors.Add(error);
                }
                Result.phases=errors.ToArray();
            }
            finally
            {
                foreach(var allocation in allocations) allocation.Dispose();
                var args=new object[]{physics}; Call(physicsType,null,"DisposePhysicsContext",args);
                args=new object[]{context}; Call(type,null,"DisposeSolverContext",args);
            }
        }

        internal void RunIdenticalInputs(PMXImportResult model, MmdPocMotion motion, int[] controlled)
        {
            for (var trial=0; trial<2; trial++)
            {
                var manager=model.mmdTransformResult.transformManager;
                manager.ResetToBindPose();
                var trace=new PocPhysicsPhaseTrace { ExactInputs=InputStates, PhysicsControlled=new HashSet<int>(controlled),
                    RetainPhysicsSolverState=trial==1, RetainOtherSolverHistory=trial==1 };
                ExperimentPositions[trial]=new Vector3[91][];
                ExperimentRotations[trial]=new Quaternion[91][];
                motion.DiagnosticPhysicsSolve=trace.Solve;
                try
                {
                    for(var frame=0;frame<=90;frame++)
                    {
                        motion.SampleWithPhysics(frame/30f,frame==0?0:1f/30);
                        ExperimentPositions[trial][frame]=new Vector3[controlled.Length];
                        ExperimentRotations[trial][frame]=new Quaternion[controlled.Length];
                        for(var i=0;i<controlled.Length;i++)
                        {
                            ExperimentPositions[trial][frame][i]=model.bones[controlled[i]].localPosition;
                            ExperimentRotations[trial][frame][i]=model.bones[controlled[i]].localRotation;
                        }
                    }
                }
                finally { motion.DiagnosticPhysicsSolve=null; }
            }
            Result.sameInputs=new Replay(); Result.sameInputsAndHistory=new Replay();
        }

        internal void CompareExperiments(int frame, int index, Vector3 position, Quaternion rotation)
        {
            for(var trial=0;trial<2;trial++)
            {
                var result=trial==0?Result.sameInputs:Result.sameInputsAndHistory;
                var p=Vector3.Distance(ExperimentPositions[trial][frame][index],position);
                var q=Quaternion.Angle(ExperimentRotations[trial][frame][index],rotation);
                if(float.IsNaN(p)||float.IsNaN(q)) throw new InvalidOperationException("Nonfinite identical-input experiment");
                result.position=Mathf.Max(result.position,p); result.rotation=Mathf.Max(result.rotation,q);
                if(result.firstDivergentFrame<0&&(p>.001f||q>.1f))result.firstDivergentFrame=frame;
                result.parityPassed=result.position<=.001f&&result.rotation<=.1f;
            }
        }
    }
}
