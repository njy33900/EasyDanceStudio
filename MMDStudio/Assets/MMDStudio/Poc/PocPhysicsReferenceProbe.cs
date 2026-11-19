using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UMT;
using UnityEngine;
using UnityEngine.Playables;

namespace MmdStudio.Poc
{
    // Controlled comparison to the pinned official converter and actual demo
    // evaluator. This is not an independent engine or a whole-motion oracle.
    public sealed class PocPhysicsReferenceProbe : MonoBehaviour
    {
        [Serializable] private sealed class FrameError { public int frame; public float position; public float rotation; }
        [Serializable] private sealed class Report
        {
            public bool success, parityPassed;
            public string error, source, backend, worstPositionBone, worstRotationBone;
            public string policy = "direct-default-ik vs official physics bake; 30Hz; zero warmup; seed 0; synthetic 3sec center/torso motion";
            public int comparisons;
            public int firstDivergentFrame = -1;
            public FrameError[] frames;
            public float maximumLocalPositionError, maximumLocalRotationError;
            public PocPhysicsPhaseTrace.Report phaseTrace;
            public bool retainedSolverStateExperiment;
            public PocPhysicsContextProbe.ContextReport contextComparison;
        }
        public IEnumerator Run(PocWorkspace workspace, string path)
        {
            var report = new Report { source = PocArguments.Value("-pocPmx") };
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            var fixture = Path.Combine(Path.GetDirectoryName(path), "physics-reference-fixture.vmd");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteFixture(fixture);
            workspace.Initialize(false, "direct-default-ik");
            yield return workspace.LoadModel(report.source, fixture);
            if (workspace.Motion == null) { Finish(path, report, workspace.Error); yield break; }
            workspace.Motion.Playing = false;
            yield return null;
            VMDAnimation animation = null;
            PlayableGraph graph = default;
            try
            {
                var model = workspace.Current;
                var manager = model.mmdTransformResult.transformManager;
                var controlled = manager.physicsManager.rigidBodies.Where(b => b.mode != PMXRigidBody.Mode.Kinetic && b.relatedBoneIndex >= 0)
                    .Select(b => b.relatedBoneIndex).Distinct().ToArray();
                var positions = new Vector3[91][];
                var rotations = new Quaternion[91][];
                manager.ResetToBindPose();
                for (var frame = 0; frame <= 90; frame++)
                {
                    workspace.Motion.SampleWithPhysics(frame/30f, frame == 0 ? 0 : 1f/30);
                    positions[frame] = controlled.Select(i => model.bones[i].localPosition).ToArray();
                    rotations[frame] = controlled.Select(i => model.bones[i].localRotation).ToArray();
                }
                if (Environment.GetCommandLineArgs().Contains("-pocPhysicsPhaseTrace"))
                {
                    var trace = new PocPhysicsPhaseTrace();
                    manager.ResetToBindPose();
                    workspace.Motion.DiagnosticPhysicsSolve = trace.Solve;
                    try
                    {
                        for (var frame = 0; frame <= 90; frame++)
                        {
                            workspace.Motion.SampleWithPhysics(frame/30f, frame == 0 ? 0 : 1f/30);
                            for (var index = 0; index < controlled.Length; index++)
                            {
                                var bone = model.bones[controlled[index]];
                                trace.Result.maximumReplayPositionError = Mathf.Max(trace.Result.maximumReplayPositionError, Vector3.Distance(positions[frame][index], bone.localPosition));
                                trace.Result.maximumReplayRotationError = Mathf.Max(trace.Result.maximumReplayRotationError, Quaternion.Angle(rotations[frame][index], bone.localRotation));
                            }
                        }
                    }
                    finally { workspace.Motion.DiagnosticPhysicsSolve = null; }
                    trace.Complete();
                    report.phaseTrace = trace.Result;
                    if (!trace.Result.equivalent) throw new InvalidOperationException("Split phase trace differs from public solve; do not use trace as evidence");
                }
                if (Environment.GetCommandLineArgs().Contains("-pocPhysicsRetainSolverState"))
                {
                    if (report.phaseTrace == null || !report.phaseTrace.equivalent)
                        throw new InvalidOperationException("Retain-state experiment requires a verified phase trace");
                    manager.ResetToBindPose();
                    var allHistory = Environment.GetCommandLineArgs().Contains("-pocPhysicsRetainAllSolverState");
                    var experiment = new PocPhysicsPhaseTrace { RetainPhysicsSolverState = true, RetainOtherSolverHistory = allHistory };
                    workspace.Motion.DiagnosticPhysicsSolve = experiment.Solve;
                    try
                    {
                        for (var frame = 0; frame <= 90; frame++)
                        {
                            workspace.Motion.SampleWithPhysics(frame/30f, frame == 0 ? 0 : 1f/30);
                            positions[frame] = controlled.Select(i => model.bones[i].localPosition).ToArray();
                            rotations[frame] = controlled.Select(i => model.bones[i].localRotation).ToArray();
                        }
                    }
                    finally { workspace.Motion.DiagnosticPhysicsSolve = null; }
                    report.retainedSolverStateExperiment = true;
                    report.policy = "EXPERIMENT: retain " + (allHistory ? "all solver history with nonphysical input/flags replaced" : "physical BoneSolverState") + "; vs official bake; 30Hz; seed 0; zero warmup";
                }
                using (var stream = File.OpenRead(fixture)) animation = VMDReader.Read(stream);
                PocPhysicsContextProbe contextProbe = null;
                if (Environment.GetCommandLineArgs().Contains("-pocPhysicsContextCompare"))
                {
                    if (report.phaseTrace == null || !report.phaseTrace.equivalent || report.retainedSolverStateExperiment)
                        throw new InvalidOperationException("Context comparison requires an unmodified verified runtime trace");
                    contextProbe = new PocPhysicsContextProbe();
                    contextProbe.Run(model, animation, controlled, report.phaseTrace);
                    contextProbe.RunIdenticalInputs(model, workspace.Motion, controlled);
                    report.contextComparison = contextProbe.Result;
                }
                var clip = VMDAnimationClipConverter.Convert(animation, model.model, new VMDAnimationClipOptions
                { frameRate = 30, bakeIKToFK = true, bakePhysicsToFK = true, physicsWarmUpDuration = 0, physicsSeed = 0 });
                graph = PlayableGraph.Create("Official Physics Comparison");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = ScriptPlayable<UMTDemo.VMDModelPlayableBehaviour>.Create(graph);
                playable.GetBehaviour().root = model.root.transform;
                playable.GetBehaviour().clipData = clip;
                ScriptPlayableOutput.Create(graph, "Reference").SetSourcePlayable(playable);
                graph.Play();
                report.frames = new FrameError[91];
                for (var frame = 0; frame <= 90; frame++)
                {
                    var frameError = new FrameError { frame = frame };
                    report.frames[frame] = frameError;
                    // Demo output owns the entire baked pose; no second solve.
                    foreach (var bone in manager.bones)
                    { bone.transform.localPosition = bone.initialLocalPosition; bone.transform.localRotation = bone.initialLocalRotation; }
                    playable.SetTime(frame/30.0);
                    graph.Evaluate(0);
                    for (var index = 0; index < controlled.Length; index++)
                    {
                        var bone = controlled[index];
                        var p = Vector3.Distance(positions[frame][index], model.bones[bone].localPosition);
                        var q = Quaternion.Angle(rotations[frame][index], model.bones[bone].localRotation);
                        if (contextProbe != null)
                        {
                            var contextP = Vector3.Distance(contextProbe.Positions[frame][index], model.bones[bone].localPosition);
                            var contextQ = Quaternion.Angle(contextProbe.Rotations[frame][index], model.bones[bone].localRotation);
                            if (float.IsNaN(contextP) || float.IsNaN(contextQ)) throw new InvalidOperationException("Nonfinite standalone comparison");
                            contextProbe.Result.maximumReplayPositionError = Mathf.Max(contextProbe.Result.maximumReplayPositionError, contextP);
                            contextProbe.Result.maximumReplayRotationError = Mathf.Max(contextProbe.Result.maximumReplayRotationError, contextQ);
                            contextProbe.CompareExperiments(frame, index, model.bones[bone].localPosition, model.bones[bone].localRotation);
                        }
                        if (float.IsNaN(p) || float.IsNaN(q)) throw new InvalidOperationException("Nonfinite comparison");
                        frameError.position = Mathf.Max(frameError.position, p);
                        frameError.rotation = Mathf.Max(frameError.rotation, q);
                        if (p > report.maximumLocalPositionError) { report.maximumLocalPositionError = p; report.worstPositionBone = manager.bones[bone].boneName; }
                        if (q > report.maximumLocalRotationError) { report.maximumLocalRotationError = q; report.worstRotationBone = manager.bones[bone].boneName; }
                        report.comparisons++;
                    }
                    if (report.firstDivergentFrame < 0 && (frameError.position > .001f || frameError.rotation > .1f)) report.firstDivergentFrame = frame;
                }
                if (report.comparisons == 0) throw new InvalidOperationException("No physics bone comparisons");
                report.parityPassed = report.maximumLocalPositionError <= .001f && report.maximumLocalRotationError <= .1f;
                if (contextProbe != null)
                {
                    contextProbe.Result.converterReplayParity = contextProbe.Result.maximumReplayPositionError <= .001f && contextProbe.Result.maximumReplayRotationError <= .1f;
                    if (!contextProbe.Result.converterReplayParity) throw new InvalidOperationException("Standalone converter trace does not match official clip; do not use as bake evidence");
                }
                Finish(path, report, null);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); }
            finally { if (graph.IsValid()) graph.Destroy(); if (animation != null) Destroy(animation); }
        }
        internal static void WriteFixture(string path, float amplitude = 1)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                var signature = Encoding.ASCII.GetBytes("Vocaloid Motion Data 0002"); writer.Write(signature); writer.Write(new byte[30-signature.Length]); writer.Write(new byte[20]);
                writer.Write(8u);
                foreach (var encodedName in new[] { "g1qDk4NegVs=", "j+OUvJBn" })
                for (var index = 0; index < 4; index++)
                {
                    var name = Convert.FromBase64String(encodedName); writer.Write(name); writer.Write(new byte[15-name.Length]); writer.Write((uint)(index*30));
                    var weight = index == 1 ? 1f : index == 2 ? -1f : 0;
                    writer.Write(encodedName == "g1qDk4NegVs=" ? weight * amplitude : 0f); writer.Write(0f); writer.Write(0f);
                    writer.Write(0f); writer.Write(encodedName == "j+OUvJBn" ? Mathf.Sin(weight*.05f) : 0f); writer.Write(0f); writer.Write(encodedName == "j+OUvJBn" ? Mathf.Cos(weight*.05f) : 1f);
                    writer.Write(Enumerable.Repeat((byte)20, 64).ToArray());
                }
                for (var section = 0; section < 5; section++) writer.Write(0u);
            }
        }
        private static void Finish(string path, Report report, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? "";
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
