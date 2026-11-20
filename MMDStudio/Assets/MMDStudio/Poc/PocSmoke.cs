using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmdStudio.Poc
{
    public static class PocArguments
    {
        public static bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        public static bool HasAny(params string[] names)
        {
            foreach (var name in names) if (Has(name)) return true;
            return false;
        }
        public static string Value(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.Ordinal)) continue;
                if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException("Missing value for " + name);
                return args[i + 1];
            }
            return null;
        }
    }

    public sealed class PocSmoke : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string path;
            try { path = PocArguments.Value("-pocSmokeReport"); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Application.Quit(1);
                yield break;
            }
            if (path == null)
            {
                var physicsBakeReport = PocArguments.Value("-pocPhysicsBakeReport");
                if (physicsBakeReport != null) { yield return gameObject.AddComponent<PocPhysicsBakeProbe>().Run(physicsBakeReport); yield break; }
                var physicsMeasureReport = PocArguments.Value("-pocPhysicsMeasureReport");
                if (physicsMeasureReport != null) { yield return gameObject.AddComponent<PocPhysicsMeasureProbe>().Run(physicsMeasureReport); yield break; }
                var stagingReport = PocArguments.Value("-pocPmxStagingReport");
                if (stagingReport != null)
                {
                    yield return gameObject.AddComponent<PocPmxStagingProbe>().Run(stagingReport);
                    yield break;
                }
                var sceneUiReport = PocArguments.Value("-pocSceneUiReport");
                var restoreReport = PocArguments.Value("-pocSceneRestoreReport");
                var perfReport = PocArguments.Value("-pocScenePerfReport");
                var cycleReport = PocArguments.Value("-pocSceneCycleReport");
                var physicsShots = PocArguments.Value("-pocPhysicsShots");
                var sceneFile = PocArguments.Value("-sceneFile");
                var legacyProbe = PocArguments.HasAny("-pocRecoveryReport", "-pocCameraReport", "-pocDeformationReport",
                    "-pocPhysicsReport", "-pocPhysicsEnduranceReport", "-pocPhysicsReferenceReport",
                    "-pocPhysicsLifecycleReport", "-pocPhysicsFeatureReport", "-pocPhysicsRealMotionReport", "-pocSceneReport");
                // Double-clicking the current Player opens the studio. Older diagnostic
                // routes remain explicit so their probes and single-model launcher still work.
                if (sceneUiReport != null || restoreReport != null || perfReport != null || cycleReport != null || physicsShots != null || sceneFile != null ||
                    PocArguments.Has("-sceneUi") || (!PocArguments.Has("-legacyWorkspace") && !legacyProbe))
                {
                    var sceneUi = gameObject.AddComponent<MmdSceneWorkspace>(); sceneUi.Initialize();
                    Debug.Log("EasyDanceStudio: scene workspace started.");
                    if (sceneUiReport != null) yield return gameObject.AddComponent<PocSceneUiProbe>().Run(sceneUi, sceneUiReport);
                    else if (restoreReport != null) yield return gameObject.AddComponent<PocSceneRestoreProbe>().Run(sceneUi, restoreReport, sceneFile);
                    else if (physicsShots != null) yield return gameObject.AddComponent<PocPhysicsShotProbe>().Run(sceneUi, physicsShots);
                    else if (cycleReport != null) yield return gameObject.AddComponent<PocSceneCycleProbe>().Run(sceneUi, cycleReport, sceneFile);
                    else if (perfReport != null) yield return gameObject.AddComponent<PocScenePerformanceProbe>().Run(sceneUi, perfReport);
                    else if (sceneFile != null) yield return sceneUi.OpenFrom(sceneFile);
                    else if (PocArguments.Value("-pocPmx") != null)
                    {
                        yield return sceneUi.ApplyPickerResult(PocArguments.Value("-pocPmx"), true);
                        if (PocArguments.Value("-pocVmd") != null) yield return sceneUi.ApplyPickerResult(PocArguments.Value("-pocVmd"), false);
                    }
                    yield break;
                }
                var workspace = gameObject.AddComponent<PocWorkspace>();
                workspace.Initialize(!Application.isBatchMode, PocArguments.Value("-pocMotionMode") ?? "direct");
                var recoveryReport = PocArguments.Value("-pocRecoveryReport");
                var cameraReport = PocArguments.Value("-pocCameraReport");
                var deformationReport = PocArguments.Value("-pocDeformationReport");
                var physicsReport = PocArguments.Value("-pocPhysicsReport");
                var enduranceReport = PocArguments.Value("-pocPhysicsEnduranceReport");
                var physicsReferenceReport = PocArguments.Value("-pocPhysicsReferenceReport");
                var physicsLifecycleReport = PocArguments.Value("-pocPhysicsLifecycleReport");
                var physicsFeatureReport = PocArguments.Value("-pocPhysicsFeatureReport");
                var sceneReport = PocArguments.Value("-pocSceneReport");
                var physicsRealReport = PocArguments.Value("-pocPhysicsRealMotionReport");
                if (sceneReport != null)
                    yield return gameObject.AddComponent<PocSceneProbe>().Run(sceneReport);
                else if (physicsFeatureReport != null || physicsRealReport != null)
                    yield return gameObject.AddComponent<PocPhysicsFeatureProbe>().Run(workspace, physicsFeatureReport ?? physicsRealReport, physicsRealReport != null);
                else if (physicsLifecycleReport != null)
                    yield return gameObject.AddComponent<PocPhysicsLifecycleProbe>().Run(workspace, physicsLifecycleReport);
                else if (physicsReferenceReport != null)
                    yield return gameObject.AddComponent<PocPhysicsReferenceProbe>().Run(workspace, physicsReferenceReport);
                else if (enduranceReport != null)
                    yield return gameObject.AddComponent<PocPhysicsEnduranceProbe>().Run(workspace, enduranceReport);
                else if (physicsReport != null)
                    yield return gameObject.AddComponent<PocPhysicsProbe>().Run(workspace, physicsReport);
                else if (deformationReport != null)
                    yield return gameObject.AddComponent<PocDeformationProbe>().Run(workspace, deformationReport);
                else if (cameraReport != null)
                    yield return gameObject.AddComponent<PocCameraProbe>().Run(workspace, cameraReport);
                else if (recoveryReport != null)
                    yield return gameObject.AddComponent<PocRecoveryProbe>().Run(workspace, recoveryReport);
                else if (PocArguments.Value("-pocPmx") != null)
                {
                    yield return workspace.LoadModel(PocArguments.Value("-pocPmx"), PocArguments.Value("-pocVmd"));
                    if (PocArguments.Value("-pocCameraVmd") != null)
                        yield return workspace.LoadCamera(PocArguments.Value("-pocCameraVmd"));
                }
                else
                {
                    var cube = GameObject.Find("Cube");
                    if (cube != null) cube.SetActive(false);
                }
                yield break;
            }

            // Let the Player initialize its graphics device and URP instance.
            for (var i = 0; i < 3; i++) yield return null;
            var result = new SmokeResult
            {
#if ENABLE_IL2CPP
                scriptingBackend = "IL2CPP",
#else
                scriptingBackend = "Mono",
#endif
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                renderPipeline = GraphicsSettings.currentRenderPipeline == null
                    ? "None" : GraphicsSettings.currentRenderPipeline.GetType().FullName,
                error = ""
            };
            MmdPocMotion motion = null;
            try
            {
                if (path != null && !Path.IsPathRooted(path)) throw new ArgumentException("Report path must be absolute.");
                if (Camera.main == null) throw new InvalidOperationException("Main camera not found.");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("No graphics device; do not pass -nographics to the Player.");
                var pmxPath = PocArguments.Value("-pocPmx");
                var vmdPath = PocArguments.Value("-pocVmd");
                if (vmdPath != null && pmxPath == null) throw new ArgumentException("-pocVmd requires -pocPmx.");
                if (pmxPath != null)
                {
                    result.model = MmdPocProbe.Load(pmxPath, Camera.main, out var imported);
                    if (vmdPath != null)
                    {
                        motion = imported.root.AddComponent<MmdPocMotion>();
                        motion.Load(imported, vmdPath, PocArguments.Value("-pocMotionMode") ?? "sparse");
                        result.motion = motion.Summary;
                    }
                }
                // Hidden windows can defer the display render loop. An explicit URP
                // render request exercises the same camera on a real GPU offscreen.
                if (path != null)
                {
                    result.screenshot = Path.ChangeExtension(path, ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (motion == null) Capture(Camera.main, result);
                }
                result.success = true;
            }
            catch (Exception ex)
            {
                result.error = ex.ToString();
                Debug.LogException(ex);
            }

            if (path == null)
            {
                if (motion != null) motion.Playing = result.success;
                yield break;
            }
            if (result.success && motion != null) yield return ValidateMotion(motion, result);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(result, true));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception ex)
            {
                result.success = false;
                Debug.LogException(ex);
            }
            Debug.Log(result.success ? "POC_PLAYER_SUCCESS" : "POC_PLAYER_FAILED");
            Application.Quit(result.success ? 0 : 1);
        }

        private IEnumerator ValidateMotion(MmdPocMotion motion, SmokeResult result)
        {
            yield return null; // Complete UMT's one-time Start solve before sampling.
            var image = result.screenshot;
            var times = new[] { 0f, 0.5f / 30, 15, 15 + 0.5f / 30, 30, 30 + 0.5f / 30 };
            for (var i = 0; i < times.Length; i++) times[i] = Mathf.Min(times[i], motion.Summary.durationSeconds);
            var snapshots = new string[times.Length];
            var poses = new MmdPoseSnapshot[times.Length];
            Vector3[] startPose = null;
            Vector3[] referencePose = null;
            Quaternion[] startRotations = null;
            Quaternion[] referenceRotations = null;
            for (var i = 0; i < times.Length; i++)
            {
                var time = times[i];
                if (!TryStep(() => motion.Sample(time), result)) yield break;
                yield return null; // Let Unity update skinned renderers before GPU readback.
                var index = i;
                if (!TryStep(() =>
                {
                    result.screenshot = Path.Combine(Path.GetDirectoryName(image),
                        Path.GetFileNameWithoutExtension(image) + "-pose-" + index + ".png");
                    Capture(Camera.main, result);
                    snapshots[index] = result.screenshot;
                    poses[index] = motion.Snapshot();
                    if (index == 0) { startPose = motion.PosePositions(); startRotations = motion.PoseRotations(); }
                    if (index == 2) { referencePose = motion.PosePositions(); referenceRotations = motion.PoseRotations(); }
                }, result)) yield break;
            }
            if (!TryStep(() =>
            {
                for (var i = 0; i < startPose.Length; i++)
                {
                    if (Vector3.Distance(startPose[i], referencePose[i]) > 0.001f) motion.Summary.changedBones++;
                    if (Quaternion.Angle(startRotations[i], referenceRotations[i]) > 1) motion.Summary.rotatedBones++;
                }
                if (motion.Summary.changedBones < 5 || motion.Summary.rotatedBones < 5)
                    throw new InvalidOperationException("VMD did not move/rotate enough bones.");
                motion.Sample(times[2]);
                var repeat = motion.PosePositions();
                var repeatRotations = motion.PoseRotations();
                for (var i = 0; i < repeat.Length; i++)
                    if (Vector3.Distance(repeat[i], referencePose[i]) > 0.0001f ||
                        Quaternion.Angle(repeatRotations[i], referenceRotations[i]) > 0.05f)
                        throw new InvalidOperationException("Physics-off repeated seek changed the pose.");
                motion.Summary.seekRepeatPassed = true;
                motion.Sample(0);
                motion.Playing = true;
            }, result)) yield break;

            yield return new WaitForSecondsRealtime(1);
            motion.Playing = false;
            var pausedAt = motion.TimeSeconds;
            motion.Summary.realtimeAdvancedSeconds = pausedAt;
            for (var i = 0; i < 3; i++) yield return null;
            if (!TryStep(() =>
            {
                motion.Summary.pausePassed = Mathf.Abs(motion.TimeSeconds - pausedAt) < 0.00001f;
                motion.Sample(motion.Summary.durationSeconds - 0.1f);
                motion.Advance(0.2f);
                motion.Summary.loopPassed = Mathf.Abs(motion.TimeSeconds - 0.1f) < 0.001f;
                motion.Summary.poseScreenshots = snapshots;
                motion.Summary.poses = poses;
                if (!motion.Summary.pausePassed || !motion.Summary.loopPassed || pausedAt < 0.1f)
                    throw new InvalidOperationException("Play/pause/loop verification failed.");
                motion.SweepSourceRotations();
            }, result)) yield break;
        }

        private static bool TryStep(Action action, SmokeResult result)
        {
            try { action(); return true; }
            catch (Exception ex)
            {
                result.success = false;
                result.error = ex.ToString();
                Debug.LogException(ex);
                return false;
            }
        }

        internal static void CaptureImage(Camera camera, string path)
        {
            Capture(camera, new SmokeResult { screenshot = path,
                renderPipeline = GraphicsSettings.currentRenderPipeline?.GetType().FullName ?? "None" });
        }

        private static void Capture(Camera camera, SmokeResult result)
        {
            result.nonBackgroundPixels = 0;
            result.magentaPixels = 0;
            var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest
                {
                    destination = target,
                    face = CubemapFace.Unknown
                });
                if (RenderPipelineManager.currentPipeline == null ||
                    !result.renderPipeline.Contains("UniversalRenderPipelineAsset"))
                    throw new InvalidOperationException("Universal Render Pipeline was not initialized.");
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                var background = pixels[0];
                foreach (var pixel in pixels)
                {
                    if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) +
                        Math.Abs(pixel.b - background.b) > 60) result.nonBackgroundPixels++;
                    if (pixel.r > 220 && pixel.b > 220 && pixel.g < 40) result.magentaPixels++;
                }
                File.WriteAllBytes(result.screenshot, image.EncodeToPNG());
                if (result.nonBackgroundPixels < 100 || result.magentaPixels > 100)
                    throw new InvalidOperationException("Render is empty or contains missing-shader magenta pixels.");
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Destroy(target);
                if (image != null) Destroy(image);
            }
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public string scriptingBackend;
            public string unityVersion;
            public string operatingSystem;
            public string graphicsDeviceName;
            public string graphicsDeviceType;
            public string renderPipeline;
            public bool success;
            public string error;
            public string screenshot;
            public int nonBackgroundPixels;
            public int magentaPixels;
            public MmdPocSummary model;
            public MmdMotionSummary motion;
        }
    }
}
