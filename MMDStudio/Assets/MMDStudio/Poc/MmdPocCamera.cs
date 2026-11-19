using System;
using System.IO;
using System.Linq;
using UMT;
using UnityEngine;
using UnityEngine.Playables;

namespace MmdStudio.Poc
{
    // Reference-path experiment: intentionally uses the unmodified official demo
    // evaluator. Baked 60 Hz curves are not exact continuous VMD interpolation.
    public sealed class MmdPocCamera : IDisposable
    {
        private GameObject rig;
        private Camera sampleCamera;
        private PlayableGraph graph;
        private ScriptPlayable<UMTDemo.VMDCameraPlayableBehaviour> playable;
        private uint[] sourceFrames;
        public string Source { get; private set; }
        public int KeyCount { get; private set; }
        public float Duration { get; private set; }
        public const string Limit = "카메라: UMT 60Hz 근사 · 위치 XYZ 보간 통합 · 투영 전환/양수 거리 미지원";

        public static MmdPocCamera Load(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists || !string.Equals(file.Extension, ".vmd", StringComparison.OrdinalIgnoreCase) ||
                file.Length < 50 || file.Length > 32L * 1024 * 1024)
                throw new InvalidDataException("Camera VMD is missing or exceeds the 32 MiB probe limit.");
            VMDAnimation animation = null;
            MmdPocCamera result = null;
            try
            {
                using (var stream = file.OpenRead()) animation = VMDReader.Read(stream);
                if (animation.cameraFrames.Length == 0) throw new InvalidDataException("Camera VMD requires camera keyframes.");
                uint last = 0;
                foreach (var key in animation.cameraFrames)
                {
                    if (key.frame > 18000) throw new InvalidDataException("Camera VMD exceeds the 10 minute probe limit.");
                    if (key.perspectiveOff || key.distance > 0)
                        throw new InvalidDataException("Camera projection toggle / positive distance is unsupported by this reference path.");
                    if (!Finite(key.distance) || !Finite(key.targetPosition.x) || !Finite(key.targetPosition.y) ||
                        !Finite(key.targetPosition.z) || !Finite(key.rotation.x) || !Finite(key.rotation.y) || !Finite(key.rotation.z) ||
                        key.viewAngle < 1 || key.viewAngle > 179)
                        throw new InvalidDataException("Camera VMD contains invalid numeric values.");
                    last = Math.Max(last, key.frame);
                }
                result = new MmdPocCamera { Source = file.FullName, KeyCount = animation.cameraFrames.Length, Duration = last / 30f };
                result.sourceFrames = animation.cameraFrames.Select(key => key.frame).Distinct().OrderBy(frame => frame).ToArray();
                var data = VMDAnimationClipConverter.ConvertCamera(animation, 60);
                result.rig = new GameObject("POC Camera Reference Rig");
                var target = new GameObject(VMDAnimationClipConverter.k_DefaultCameraTargetName).transform;
                target.SetParent(result.rig.transform, false);
                result.sampleCamera = new GameObject(VMDAnimationClipConverter.k_DefaultCameraChildName).AddComponent<Camera>();
                result.sampleCamera.enabled = false;
                result.sampleCamera.transform.SetParent(target, false);
                result.graph = PlayableGraph.Create("POC Camera Reference");
                result.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                result.playable = ScriptPlayable<UMTDemo.VMDCameraPlayableBehaviour>.Create(result.graph);
                result.playable.GetBehaviour().root = result.rig.transform;
                result.playable.GetBehaviour().clipData = data;
                ScriptPlayableOutput.Create(result.graph, "Camera").SetSourcePlayable(result.playable);
                result.graph.Play();
                return result;
            }
            catch { result?.Dispose(); throw; }
            finally { if (animation != null) UnityEngine.Object.Destroy(animation); }
        }

        public void Sample(Camera destination, float seconds)
        {
            seconds = Mathf.Clamp(seconds, 0, Duration);
            var low = 0;
            var high = sourceFrames.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (sourceFrames[mid] / 30f <= seconds) low = mid + 1;
                else high = mid;
            }
            // UMT bakes a hard cut correctly at its samples, but AnimationCurve's
            // linear segment to the next sample blends the final half-frame.
            // Hold the source key for the entire one-frame cut interval.
            if (low > 0 && low < sourceFrames.Length && sourceFrames[low] - sourceFrames[low - 1] == 1)
                seconds = sourceFrames[low - 1] / 30f;
            playable.SetTime(seconds);
            graph.Evaluate(0);
            destination.transform.SetPositionAndRotation(sampleCamera.transform.position, sampleCamera.transform.rotation);
            destination.fieldOfView = sampleCamera.fieldOfView;
            destination.orthographic = false;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (rig != null) UnityEngine.Object.Destroy(rig);
            rig = null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
