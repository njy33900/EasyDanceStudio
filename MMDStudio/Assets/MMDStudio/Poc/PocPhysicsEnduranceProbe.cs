using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocPhysicsEnduranceProbe : MonoBehaviour
    {
        [Serializable] private sealed class Checkpoint
        {
            public float seconds;
            public float maximumReplayError;
            public string screenshot;
            public Vector3[] positions;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, source, backend, largestPositionBone, largestStepBone;
            public int stepsPerTrial, completedSteps, comparedPoses, bodies, joints, trials;
            public bool largestStepBoneDirectlyWeighted;
            public string largestWeightedStepBone;
            public float maximumWeightedStepDisplacement;
            public float duration, deltaSeconds = 1f/60, maximumPositionMagnitude, maximumStepDisplacement, maximumReplayError;
            public double simulationMilliseconds, wallMilliseconds;
            public Checkpoint[] checkpoints;
            public string limitation = "Two fixed-step full-duration trials; finite/bounded/replay and selected renders only. No external-engine physics parity.";
        }
        private readonly Report report = new Report();
        private readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        private readonly System.Diagnostics.Stopwatch simulation = new System.Diagnostics.Stopwatch();

        public IEnumerator Run(PocWorkspace workspace, string path)
        {
            var wall = System.Diagnostics.Stopwatch.StartNew();
            report.source = PocArguments.Value("-pocPmx");
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            yield return workspace.LoadModel(report.source, PocArguments.Value("-pocVmd"));
            if (workspace.Motion == null) { Finish(path, workspace.Error, wall); yield break; }
            var motion = workspace.Motion;
            motion.Playing = false;
            yield return null;
            var manager = workspace.Current.mmdTransformResult.transformManager;
            report.duration = motion.Summary.durationSeconds;
            report.stepsPerTrial = Mathf.RoundToInt(report.duration * 60);
            report.bodies = manager.physicsManager.rigidBodies.Length;
            report.joints = manager.physicsManager.joints.Length;
            var weighted = new HashSet<int>();
            foreach (var vertex in workspace.Current.model.vertices)
            {
                var weight = vertex.weight;
                if (weight.weight0 > 0) weighted.Add(weight.boneIndex0);
                if (weight.weight1 > 0) weighted.Add(weight.boneIndex1);
                if (weight.weight2 > 0) weighted.Add(weight.boneIndex2);
                if (weight.weight3 > 0) weighted.Add(weight.boneIndex3);
            }
            var firstTrial = new Vector3[report.stepsPerTrial + 1][];
            var sampleSteps = new HashSet<int> { 0, 900, 1800, 3600, 5400, 7200, 9000, 10800, report.stepsPerTrial };
            for (var trial = 0; trial < 2; trial++)
            {
                string error = null;
                try { manager.ResetToBindPose(); motion.SampleWithPhysics(0, 0); }
                catch (Exception ex) { error = ex.ToString(); }
                if (error != null) { Finish(path, error, wall); yield break; }
                var previous = motion.PosePositions();
                for (var step = 0; step <= report.stepsPerTrial; step++)
                {
                    Vector3[] positions = null;
                    simulation.Start();
                    try
                    {
                        if (step > 0) motion.SampleWithPhysics(Mathf.Min(step/60f, report.duration), report.deltaSeconds);
                        positions = motion.PosePositions();
                        for (var i = 0; i < positions.Length; i++)
                        {
                            var p = positions[i];
                            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.magnitude > 100)
                                throw new InvalidOperationException("Nonfinite / >100m bone: " + manager.bones[i].boneName + " at " + step/60f);
                            if (p.magnitude > report.maximumPositionMagnitude)
                            { report.maximumPositionMagnitude = p.magnitude; report.largestPositionBone = manager.bones[i].boneName; }
                            var displacement = Vector3.Distance(previous[i], p);
                            if (displacement > report.maximumStepDisplacement)
                            { report.maximumStepDisplacement = displacement; report.largestStepBone = manager.bones[i].boneName; report.largestStepBoneDirectlyWeighted = weighted.Contains(i); }
                            if (weighted.Contains(i) && displacement > report.maximumWeightedStepDisplacement)
                            { report.maximumWeightedStepDisplacement = displacement; report.largestWeightedStepBone = manager.bones[i].boneName; }
                            if (trial == 1)
                                report.maximumReplayError = Mathf.Max(report.maximumReplayError, Vector3.Distance(firstTrial[step][i], p));
                        }
                        if (trial == 0) firstTrial[step] = positions;
                        else
                        {
                            report.comparedPoses++;
                            if (report.maximumReplayError > .001f) throw new InvalidOperationException("Full-duration replay exceeds 1mm at step " + step);
                        }
                        foreach (var q in motion.PoseRotations())
                            if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w))
                                throw new InvalidOperationException("Nonfinite rotation at " + step/60f);
                        previous = positions;
                        report.completedSteps++;
                    }
                    catch (Exception ex) { error = ex.ToString(); }
                    finally { simulation.Stop(); }
                    if (error != null) { Finish(path, error, wall); yield break; }
                    if (sampleSteps.Contains(step))
                    {
                        if (trial == 0)
                        {
                            var checkpoint = new Checkpoint { seconds = motion.TimeSeconds, positions = positions,
                                screenshot = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-" + step + ".png") };
                            checkpoints.Add(checkpoint);
                            yield return null; // Let SDEF skinning reconcile before the GPU request.
                            try { PocSmoke.CaptureImage(Camera.main, checkpoint.screenshot); }
                            catch (Exception ex) { error = ex.ToString(); }
                        }
                        else
                        {
                            var checkpoint = checkpoints.First(c => Math.Abs(c.seconds-motion.TimeSeconds) < .001f);
                            for (var i = 0; i < positions.Length; i++)
                                checkpoint.maximumReplayError = Mathf.Max(checkpoint.maximumReplayError, Vector3.Distance(checkpoint.positions[i], positions[i]));
                            report.maximumReplayError = Mathf.Max(report.maximumReplayError, checkpoint.maximumReplayError);
                            if (checkpoint.maximumReplayError > .001f) error = "Full-duration replay exceeds 1mm at " + motion.TimeSeconds;
                        }
                        Debug.Log("PHYSICS_ENDURANCE trial=" + trial + " seconds=" + motion.TimeSeconds);
                        Write(path, wall); // Fresh progress evidence, success remains false until both trials finish.
                    }
                    if (error != null) { Finish(path, error, wall); yield break; }
                    if (step % 120 == 0) yield return null;
                }
                report.trials++;
            }
            workspace.Clear();
            yield return null;
            Finish(path, null, wall);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void Write(string path, System.Diagnostics.Stopwatch wall)
        {
            report.simulationMilliseconds = simulation.Elapsed.TotalMilliseconds;
            report.wallMilliseconds = wall.Elapsed.TotalMilliseconds;
            report.checkpoints = checkpoints.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        private void Finish(string path, string error, System.Diagnostics.Stopwatch wall)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? "";
            Write(path, wall);
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
