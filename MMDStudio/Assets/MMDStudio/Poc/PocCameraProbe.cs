using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocCameraProbe : MonoBehaviour
    {
        [Serializable] private sealed class Pose { public float seconds; public Vector3 position; public Quaternion rotation; public float fov; }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error;
            public string source;
            public string limitation = MmdPocCamera.Limit;
            public int keys;
            public float duration;
            public Pose[] poses;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed)
        {
            if (!passed) throw new InvalidOperationException("Camera check failed: " + name);
            checks.Add(name);
        }

        public IEnumerator Run(PocWorkspace workspace, string path)
        {
            var report = new Report { source = PocArguments.Value("-pocCameraVmd") };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            yield return workspace.LoadModel(PocArguments.Value("-pocPmx"), PocArguments.Value("-pocVmd"));
            if (workspace.Motion == null) { Finish(path, report, workspace.Error); yield break; }
            workspace.Motion.Playing = false;
            var camera = Camera.main;
            var originalPosition = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            var originalFov = camera.fieldOfView;
            yield return workspace.LoadCamera(report.source);
            yield return null;
            if (workspace.CameraMotion == null) { Finish(path, report, workspace.Error); yield break; }
            try
            {
                Check("external_camera_load", workspace.Error.Length == 0);
                report.keys = workspace.CameraMotion.KeyCount;
                report.duration = workspace.CameraMotion.Duration;
                var times = new List<float> { 0, 0.5f / 30, 15, 15 + 0.5f / 30, 30, 30 + 0.5f / 30 };
                VMDAnimation animation;
                using (var stream = File.OpenRead(report.source)) animation = VMDReader.Read(stream);
                try
                {
                    for (var i = 0; i < Math.Min(64, animation.cameraFrames.Length); i++)
                    {
                        var index = i * (animation.cameraFrames.Length - 1) / Math.Max(1, Math.Min(64, animation.cameraFrames.Length) - 1);
                        times.Add(animation.cameraFrames[index].frame / 30f);
                    }
                }
                finally { Destroy(animation); }
                for (var i = 0; i < 256; i++) times.Add(((i * 149) % 256 + 0.37f) / 256 * report.duration);
                var poses = new List<Pose>();
                foreach (var seconds in times)
                {
                    workspace.CameraMotion.Sample(camera, seconds);
                    poses.Add(new Pose { seconds = seconds, position = camera.transform.position, rotation = camera.transform.rotation, fov = camera.fieldOfView });
                }
                report.poses = poses.ToArray();
                workspace.CameraMotion.Sample(camera, 15);
                var position = camera.transform.position;
                var rotation = camera.transform.rotation;
                workspace.CameraMotion.Sample(camera, 30);
                workspace.CameraMotion.Sample(camera, 15);
                Check("deterministic_seek", Vector3.Distance(position, camera.transform.position) < 1e-6f && Quaternion.Angle(rotation, camera.transform.rotation) < 0.001f);
                Check("camera_changes", Vector3.Distance(poses[0].position, poses[2].position) > 0.01f);
                for (var i = 0; i < 3; i++)
                {
                    var seconds = i * 15;
                    workspace.Motion.Sample(seconds);
                    workspace.CameraMotion.Sample(camera, seconds);
                    PocSmoke.CaptureImage(camera, Path.Combine(Path.GetDirectoryName(path), "camera-pose-" + seconds + ".png"));
                }
                Check("gpu_camera_images", true);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            var previous = workspace.CameraMotion;
            var invalid = Path.Combine(Path.GetDirectoryName(path), "camera-invalid.vmd");
            File.WriteAllBytes(invalid, new byte[64]);
            yield return workspace.LoadCamera(invalid);
            try { Check("invalid_camera_preserves_track", workspace.Error.Length > 0 && workspace.CameraMotion == previous && !workspace.Busy); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return workspace.LoadCamera(PocArguments.Value("-pocVmd"));
            try { Check("character_only_rejected", workspace.Error.Length > 0 && workspace.CameraMotion == previous); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return workspace.LoadCamera(report.source);
            yield return null;
            try
            {
                Check("replacement_recovery", workspace.Error.Length == 0 && workspace.CameraMotion != previous &&
                    GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 2);
                workspace.Motion.Sample(15);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            var pausedPosition = camera.transform.position;
            yield return new WaitForSecondsRealtime(0.1f);
            try
            {
                Check("paused_camera_sync", Vector3.Distance(pausedPosition, camera.transform.position) < 1e-6f);
                workspace.DetachCamera();
                Check("detach_restores_camera", Vector3.Distance(originalPosition, camera.transform.position) < 1e-6f &&
                    Quaternion.Angle(originalRotation, camera.transform.rotation) < 0.001f && Math.Abs(originalFov - camera.fieldOfView) < 1e-5f);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            try { Check("rig_disposed", GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 1); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            Finish(path, report, null);
        }

        private void Finish(string path, Report report, string error)
        {
            report.error = error ?? "";
            report.success = string.IsNullOrEmpty(error);
            report.checks = checks.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(report.error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
