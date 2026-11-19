using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // Fixed-step diagnostic only. It does not enable live physics in the UI.
    public sealed class PocPhysicsProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, source, backend;
            public int bodies, joints, dynamicBodies, steps, changedBones;
            public float deltaSeconds, maximumPhysicsOffset, maximumResetReplayError, maximumSeekReplayError;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed)
        {
            if (!passed) throw new InvalidOperationException("Physics check failed: " + name);
            checks.Add(name);
        }

        public IEnumerator Run(PocWorkspace workspace, string reportPath)
        {
            var report = new Report { source = PocArguments.Value("-pocPmx"), steps = 360, deltaSeconds = 1f/60 };
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            yield return workspace.LoadModel(report.source, PocArguments.Value("-pocVmd"));
            if (workspace.Motion == null) { Finish(reportPath, report, workspace.Error); yield break; }
            var motion = workspace.Motion;
            motion.Playing = false;
            yield return null;
            var manager = workspace.Current.mmdTransformResult.transformManager;
            var physics = manager.physicsManager;
            Vector3[] finalPose = null;
            try
            {
                report.bodies = physics.rigidBodies.Length;
                report.joints = physics.joints.Length;
                report.dynamicBodies = physics.rigidBodies.Count(body => body.mode != PMXRigidBody.Mode.Kinetic);
                Check("physics_bodies_present", report.bodies > 0 && report.dynamicBodies > 0 && report.joints > 0);
                Check("automatic_advancement_disabled", !manager.transformEnabled && !motion.Playing && !manager.livePhysics);
                finalPose = Replay(motion, manager, report.steps, report.deltaSeconds);
                motion.Sample(report.steps * report.deltaSeconds);
                var fk = motion.PosePositions();
                for (var i = 0; i < fk.Length; i++)
                {
                    var delta = Vector3.Distance(fk[i], finalPose[i]);
                    report.maximumPhysicsOffset = Mathf.Max(report.maximumPhysicsOffset, delta);
                    if (delta > .0001f) report.changedBones++;
                }
                Check("physics_changes_solved_pose", report.changedBones > 2 && report.maximumPhysicsOffset > .0001f);
                var repeated = Replay(motion, manager, report.steps, report.deltaSeconds);
                report.maximumResetReplayError = Difference(finalPose, repeated);
                Check("reset_replay_repeatable", report.maximumResetReplayError < .001f);
                var seekA = Replay(motion, manager, 120, report.deltaSeconds);
                Replay(motion, manager, 35, report.deltaSeconds);
                var seekB = Replay(motion, manager, 120, report.deltaSeconds);
                report.maximumSeekReplayError = Difference(seekA, seekB);
                Check("reset_preroll_seek_repeatable", report.maximumSeekReplayError < .001f);
                Replay(motion, manager, report.steps, report.deltaSeconds);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return null;
            var paused = motion.PosePositions();
            yield return new WaitForSecondsRealtime(.1f);
            try
            {
                Check("paused_physics_does_not_advance", Difference(paused, motion.PosePositions()) < 1e-6f);
                PocSmoke.CaptureImage(Camera.main, Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-on.png"));
                motion.Sample(report.steps * report.deltaSeconds);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return null;
            try
            {
                PocSmoke.CaptureImage(Camera.main, Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-off.png"));
                Check("physics_gpu_images", true);
                workspace.Clear();
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return null;
            try { Check("native_model_owner_released", workspace.Owner == null && FindObjectsByType<PocAssetLifetime>(FindObjectsSortMode.None).Length == 0); }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            Finish(reportPath, report, null);
        }

        private static Vector3[] Replay(MmdPocMotion motion, MMDTransformManager manager, int steps, float delta)
        {
            // A reset of velocities/poses alone retains Bullet context state.
            // Rebuild the solver and physics context before deterministic preroll.
            manager.ResetToBindPose();
            motion.SampleWithPhysics(0, 0);
            for (var step = 1; step <= steps; step++)
            {
                motion.SampleWithPhysics(step * delta, delta);
                foreach (var position in motion.PosePositions())
                    if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || position.magnitude > 100)
                        throw new InvalidOperationException("Physics pose is nonfinite or exceeds 100 m probe bound at step " + step);
                foreach (var rotation in motion.PoseRotations())
                    if (!Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w))
                        throw new InvalidOperationException("Physics rotation is nonfinite at step " + step);
            }
            return motion.PosePositions();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Difference(Vector3[] a, Vector3[] b)
        {
            var maximum = 0f;
            for (var i = 0; i < a.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(a[i], b[i]));
            return maximum;
        }
        private void Finish(string path, Report report, string error)
        {
            report.success = string.IsNullOrEmpty(error);
            report.error = error ?? "";
            report.checks = checks.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(report.error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
