using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H2a-04: camera VMD in the camera inspector. While "따라가기" is on, the viewport camera takes
    // the motion's pose at the scene time every frame (playing, paused or seeking). Manual
    // navigation has priority: orbit, pan, zoom, framing, view reset or a saved view releases the
    // motion and continues from the pose it was showing (no jump); the track stays in the scene
    // and "따라가기" picks it up again. Following is a view mode like the orbit view: saved with
    // the scene, not an undo step. Connect/remove are undoable; a missing VMD stays a reference.
    public sealed partial class MmdSceneWorkspace
    {
        private Label cameraMotionInfo;
        private Button cameraMotionConnect, cameraMotionRemove;
        private Toggle cameraFollow;
        public bool CameraFollowing { get; private set; }
        private sealed class UnresolvedCamera
        {
            public string Stored, BaseScene, Reason;
        }
        private UnresolvedCamera unresolvedCamera;
        internal string UnresolvedCameraPath => unresolvedCamera?.Stored;

        private sealed class CameraMotionCommand : UndoCommand
        {
            public MmdCameraMotion Previous, Next; public UnresolvedCamera PreviousMissing; public bool PreviousFollow;
            public override void Undo(MmdSceneWorkspace w)
            {
                w.Scene.SwapCameraMotion(Previous); w.unresolvedCamera = PreviousMissing;
                w.SetCameraFollow(PreviousFollow && Previous != null);
            }
            public override void Redo(MmdSceneWorkspace w)
            {
                w.Scene.SwapCameraMotion(Next); w.unresolvedCamera = null;
                w.SetCameraFollow(Next != null);
            }
        }

        private void BuildCameraMotionControls(VisualElement section)
        {
            Text(section, "카메라 모션 (VMD)", "eds-section-title").style.marginTop = 10;
            cameraMotionInfo = Text(section, "", "eds-section-text"); cameraMotionInfo.name = "CameraMotionInfo";
            var row = Element(section, "eds-row-buttons");
            cameraMotionConnect = TextButton(row, "CameraMotionConnect", "카메라 VMD 연결", () => Pick(PickKind.CameraMotion), "eds-button--block");
            cameraMotionConnect.style.marginRight = 6;
            cameraMotionRemove = TextButton(row, "CameraMotionRemove", "제거", () => Guard(RemoveCameraMotion), "eds-button--block", "eds-button--danger");
            cameraFollow = new Toggle("카메라 모션 따라가기") { name = "CameraFollow" }; section.Add(cameraFollow);
            cameraFollow.RegisterValueChangedCallback(evt => Guard(() =>
            {
                SetCameraFollow(evt.newValue);
                message = evt.newValue ? "카메라 모션을 따라갑니다." : "수동 카메라로 전환했습니다.";
            }));
            Text(section, "뷰포트에서 카메라를 실제로 움직이면(회전·이동·확대·맞춤·뷰 초기화) 수동 카메라가 우선하고 모션은 해제됩니다. 누르기만 하거나 모델을 고르는 것으로는 해제되지 않습니다. 해제할 때 모션의 위치·방향·화면 기울기·시야각을 그대로 이어받습니다. 모션은 장면에 남아 있어 '따라가기'로 다시 연결합니다. 모델 추적·직교 투영은 지원하지 않습니다.", "eds-note").name = "CameraMotionNote";
        }

        internal void SetCameraFollow(bool follow)
        {
            if (follow && Scene.CameraMotion == null) throw new InvalidOperationException("연결된 카메라 VMD가 없습니다.");
            if (CameraFollowing == follow) return;
            if (!follow) { HandOverToManualView(); CameraFollowing = false; }
            else { CameraFollowing = true; ApplyCameraMotion(); }
        }

        // Manual navigation: release the motion, continuing from the pose it shows now.
        private void ReleaseCameraMotion()
        {
            if (!CameraFollowing) return;
            HandOverToManualView();
            CameraFollowing = false;
            message = "수동 카메라로 전환했습니다. '카메라 모션 따라가기'로 다시 연결할 수 있습니다.";
            Refresh();
        }
        // v0.61: the first manual frame shows exactly what the motion showed: position, direction,
        // screen tilt (roll) and lens. The orbit pivot sits at the motion's own target distance.
        // The lens becomes the camera setting (a view state like the orbit, not an undo step);
        // only a view angle outside 10~120 degrees or a pitch beyond ±89 degrees is clamped.
        private void HandOverToManualView()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var rotation = camera.transform.rotation;
            var forward = rotation * Vector3.forward;
            var pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1, 1)) * Mathf.Rad2Deg;
            var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            var level = Quaternion.Euler(pitch, yaw, 0);
            var roll = Vector3.SignedAngle(level * Vector3.up, rotation * Vector3.up, forward);
            var distance = Scene.CameraMotion != null ? Mathf.Abs(Scene.CameraMotion.Evaluate(Scene.Seconds).Distance) : View.Distance;
            if (distance < .5f) distance = Mathf.Max(.5f, View.Distance);
            View.Set(camera.transform.position + forward * distance, yaw, pitch, distance, roll);
            View.Apply(camera);
            var lens = Mathf.Clamp(camera.fieldOfView, 10, 120);
            if (Mathf.Abs(Graphics.CurrentSettings.fieldOfView - lens) > 1e-4f)
            {
                var next = Graphics.Settings; next.fieldOfView = lens; Graphics.Apply(next);
            }
            camera.fieldOfView = Graphics.CurrentSettings.fieldOfView;
        }

        // Every frame while following (also called right after a seek or a connect).
        internal void ApplyCameraMotion()
        {
            if (!CameraFollowing || Scene.CameraMotion == null) return;
            var camera = Camera.main;
            var pose = Scene.CameraMotion.Evaluate(Scene.Seconds);
            camera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            camera.fieldOfView = pose.FieldOfView;
            camera.nearClipPlane = Mathf.Max(.005f, Mathf.Abs(pose.Distance) * .002f);
            camera.farClipPlane = Mathf.Max(1000, Mathf.Abs(pose.Distance) * 20);
        }

        private void RefreshCameraMotion()
        {
            if (cameraMotionInfo == null) return;
            var motion = Scene.CameraMotion;
            cameraMotionInfo.text = motion != null
                ? Path.GetFileName(motion.Source) + "\n키 " + motion.Keys.Count + "개 · " + Clock(motion.DurationSeconds) +
                  (motion.Warnings.Length > 0 ? "\n주의: " + string.Join(" · ", motion.Warnings) : "")
                : unresolvedCamera != null ? "카메라 VMD 누락: " + unresolvedCamera.Stored + " (" + unresolvedCamera.Reason + ")\n다른 카메라 VMD를 연결하면 대체됩니다. 연결 전에는 저장해도 경로가 유지됩니다."
                : "연결된 카메라 VMD가 없습니다.";
            cameraMotionConnect.SetEnabled(!Busy);
            cameraMotionRemove.SetEnabled(!Busy && (motion != null || unresolvedCamera != null));
            cameraFollow.SetEnabled(!Busy && motion != null);
            cameraFollow.SetValueWithoutNotify(CameraFollowing);
        }

        private void RemoveCameraMotion()
        {
            if (Scene.CameraMotion == null && unresolvedCamera == null) return;
            var command = new CameraMotionCommand { Label = "카메라 VMD 제거", Previous = Scene.CameraMotion, PreviousMissing = unresolvedCamera, PreviousFollow = CameraFollowing };
            command.Redo(this); Push(command);
            message = "카메라 VMD를 장면에서 제거했습니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.";
        }
        private void ConnectCameraMotion(MmdCameraMotion motion)
        {
            var command = new CameraMotionCommand { Label = "카메라 VMD 연결", Previous = Scene.CameraMotion, Next = motion, PreviousMissing = unresolvedCamera, PreviousFollow = CameraFollowing };
            command.Redo(this); Push(command);
            message = "카메라 VMD를 연결했습니다: " + Path.GetFileName(motion.Source) + " · " + Clock(motion.DurationSeconds) +
                (motion.Warnings.Length > 0 ? " · 주의: " + string.Join(" · ", motion.Warnings) : "");
        }

        // New/opened scene: replaced outside the history.
        private void ResetCameraMotion(MmdCameraMotion motion, UnresolvedCamera missing, bool follow)
        {
            if (CameraFollowing) { CameraFollowing = false; View.Apply(Camera.main); Camera.main.fieldOfView = Graphics.Settings.fieldOfView; }
            Scene.SwapCameraMotion(motion);
            unresolvedCamera = missing;
            if (motion != null && follow) SetCameraFollow(true);
        }
        private CameraMotionData CaptureCameraMotion(string sceneFile) => new CameraMotionData
        {
            path = Scene.CameraMotion != null ? MmdSceneDocument.ToStored(sceneFile, Scene.CameraMotion.Source)
                : unresolvedCamera != null ? Rebase(unresolvedCamera.Stored, unresolvedCamera.BaseScene, sceneFile) : "",
            follow = Scene.CameraMotion != null ? CameraFollowing : unresolvedCamera != null,
            generator = CaptureAutoCamera(sceneFile),
        };
        private void RebaseUnresolvedCamera(string targetScene)
        {
            if (unresolvedCamera == null) return;
            unresolvedCamera.Stored = Rebase(unresolvedCamera.Stored, unresolvedCamera.BaseScene, targetScene);
            unresolvedCamera.BaseScene = targetScene;
        }

        private sealed class PreparedCamera
        {
            public MmdCameraMotion Motion; public UnresolvedCamera Missing; public string Problem;
        }
        private IEnumerator PrepareCameraMotion(SceneFile file, string scenePath, string full, System.Threading.CancellationToken token, PreparedCamera result)
        {
            var stored = file.cameraMotion?.path;
            if (string.IsNullOrEmpty(stored)) yield break;
            var vmd = MmdSceneDocument.Resolve(scenePath, stored);
            if (!File.Exists(vmd))
            {
                result.Missing = new UnresolvedCamera { Stored = stored, BaseScene = full, Reason = "카메라 VMD 파일 없음" };
                result.Problem = "카메라 VMD 파일 없음: " + stored;
                yield break;
            }
            var task = System.Threading.Tasks.Task.Run(() => MmdCameraMotion.Load(vmd), token);
            while (!task.IsCompleted) yield return null;
            if (task.IsCanceled) yield break;
            if (task.IsFaulted)
            {
                var reason = "카메라 VMD 불러오기 실패 (" + task.Exception.GetBaseException().Message + ")";
                result.Missing = new UnresolvedCamera { Stored = stored, BaseScene = full, Reason = reason };
                result.Problem = reason + ": " + stored;
                yield break;
            }
            result.Motion = task.Result;
        }
    }
}
