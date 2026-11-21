using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-04: adding characters/stages and connecting VMDs through the file dialog (split from MmdSceneWorkspace.cs).
    public sealed partial class MmdSceneWorkspace
    {
        // Native dialogs by default; probes substitute scripted answers (StudioFileDialogs.cs).
        internal IStudioFileDialogs FileDialogs { get; set; } = new WindowsStudioFileDialogs();
        private void Pick(PickKind kind)
        {
            if (Busy) return;
            var playing = Scene.Playing; Scene.Playing = false;
            string path;
            try { path = kind == PickKind.Audio ? FileDialogs.OpenAudio() : kind == PickKind.CameraMotion ? FileDialogs.OpenCameraMotion() : kind != PickKind.Motion ? FileDialogs.OpenModel() : FileDialogs.OpenMotion(); }
            catch (Exception ex) { Error = ex.Message; Refresh(); return; }
            finally { Scene.ResumeAfterInterruption(playing); }
            if (string.IsNullOrEmpty(path)) { message = "파일 선택을 취소했습니다."; Error = ""; Refresh(); return; }
            StartCoroutine(ApplyPickerResult(path, kind));
        }
        public IEnumerator ApplyPickerResult(string path, bool model) => ApplyPickerResult(path, model ? PickKind.Character : PickKind.Motion);
        public IEnumerator ApplyPickerResult(string path, PickKind kind)
        {
            if (Busy) yield break;
            if (string.IsNullOrEmpty(path)) { message = "파일 선택을 취소했습니다."; Error = ""; Refresh(); yield break; }
            var previousMessage = message;
            Busy = true; Error = ""; message = "불러오는 중…";
            var playing = Scene.Playing; Scene.Playing = false; Refresh();
            yield return null;
            System.Threading.Tasks.Task<MmdSceneService.SceneObject> importTask = null;
            System.Threading.Tasks.Task motionTask = null;
            System.Threading.Tasks.Task<MmdAudioTrack> audioTask = null;
            System.Threading.Tasks.Task<MmdCameraMotion> cameraTask = null;
            if (kind == PickKind.CameraMotion)
            {
                message = "카메라 VMD 읽는 중: " + Path.GetFileName(path); UpdateStatus();
                cameraTask = System.Threading.Tasks.Task.Run(() => MmdCameraMotion.Load(path));
                while (!cameraTask.IsCompleted) yield return null;
            }
            else if (kind == PickKind.Audio)
            {
                loadCancellation = new System.Threading.CancellationTokenSource();
                RefreshLoadingCard();
                audioTask = MmdAudioTrack.LoadAsync(path, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = stage; UpdateStatus(); RefreshLoadingCard();
                }, loadCancellation.Token);
                while (!audioTask.IsCompleted) yield return null;
            }
            else if (kind != PickKind.Motion)
            {
                loadCancellation = new System.Threading.CancellationTokenSource();
                RefreshLoadingCard();
                var objectKind = kind == PickKind.Stage ? MmdSceneService.ObjectKind.Stage : MmdSceneService.ObjectKind.Character;
                var position = objectKind == MmdSceneService.ObjectKind.Stage ? Vector3.zero :
                    new Vector3(Scene.Objects.Count(item => item.Kind == MmdSceneService.ObjectKind.Character) * 1.5f, 0, 0);
                importTask = Scene.ImportAsync(path, position, objectKind, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = stage; UpdateStatus(); RefreshLoadingCard();
                }, loadCancellation.Token);
                while (!importTask.IsCompleted) yield return null;
            }
            else if (Selected != null && Selected.Kind == MmdSceneService.ObjectKind.Character)
            {
                loadCancellation = new System.Threading.CancellationTokenSource();
                RefreshLoadingCard();
                motionTask = Scene.ReplaceMotionAsync(SelectedId, path, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = stage; UpdateStatus(); RefreshLoadingCard();
                }, loadCancellation.Token);
                while (!motionTask.IsCompleted) yield return null;
            }
            try
            {
                if (kind == PickKind.CameraMotion) ConnectCameraMotion(cameraTask.GetAwaiter().GetResult());
                else if (kind == PickKind.Audio)
                {
                    var track = audioTask.GetAwaiter().GetResult();
                    if (loadCancellation.IsCancellationRequested) { track.Dispose(); throw new OperationCanceledException(); }
                    ConnectAudio(track);
                }
                else if (kind == PickKind.Character)
                {
                    var item = importTask.GetAwaiter().GetResult();
                    SelectedId = item.Id; Push(new AddCommand { Label = "캐릭터 추가", ObjectId = item.Id, Item = item, Index = Scene.Objects.ToList().IndexOf(item) });
                    message = "캐릭터를 추가했습니다. VMD를 연결하세요."; FrameAll();
                }
                else if (kind == PickKind.Stage)
                {
                    var item = importTask.GetAwaiter().GetResult();
                    SelectedId = item.Id; Push(new AddCommand { Label = "배경 추가", ObjectId = item.Id, Item = item, Index = Scene.Objects.ToList().IndexOf(item) });
                    message = "배경을 추가했습니다. 배경은 모션 없이 고정됩니다."; FrameAll();
                }
                else
                {
                    if (Selected == null) throw new InvalidOperationException("캐릭터를 먼저 선택하세요.");
                    if (Selected.Kind != MmdSceneService.ObjectKind.Character) throw new InvalidOperationException("배경에는 VMD를 연결할 수 없습니다. 캐릭터를 선택하세요.");
                    motionTask.GetAwaiter().GetResult();
                    unresolvedMotions.TryGetValue(SelectedId, out var missingBefore);
                    ForgetUnresolvedMotion(SelectedId);
                    var replaced = Scene.LastReplacedMotion;
                    Push(new MotionCommand { Label = "VMD 연결", ObjectId = SelectedId, Previous = replaced.motion, PreviousSource = replaced.source,
                        Next = Selected.Motion, NextSource = Selected.MotionSource, PreviousMissing = missingBefore });
                    message = "선택한 캐릭터에 VMD를 연결했습니다.";
                }
            }
            catch (OperationCanceledException) { Error = ""; message = "불러오기를 취소했습니다."; }
            // The failure text lives in Error so it disappears with the error instead of
            // lingering in the normal status message after later successful actions.
            catch (Exception ex) { Error = "불러오지 못했습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; message = previousMessage; }
            finally
            {
                loadCancellation?.Dispose(); loadCancellation = null;
                Busy = false; Scene.ResumeAfterInterruption(playing); Refresh(); RefreshLoadingCard();
            }
        }
    }
}
