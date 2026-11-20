using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // MVP-07: .mmdscene new/open/save, unsaved-change tracking and prompts (spec 4.10, 5.2, MMD-112).
    public sealed partial class MmdSceneWorkspace
    {
        public string ProjectPath { get; private set; }
        public bool Dirty { get; private set; }
        internal bool DialogVisible { get; private set; }
        internal string DialogText => dialogText?.text;
        // Set by automated probes so Application.Quit at the end of a run is never blocked.
        internal bool SuppressQuitPrompt { get; set; }
        private Label titleLabel, dialogText;
        private VisualElement dialog, dialogButtons;
        private Action dialogCancel;
        private bool quitConfirmed;
        private string projectId;

        private void MarkDirty() { Dirty = true; }

        // ----- R0-02a: unresolved references are part of the document -----
        // A scene entry whose PMX is missing or fails to load is kept verbatim (ID, type, stored
        // path strings, placement, visibility, motion path) instead of being dropped; a loaded
        // character whose VMD is missing keeps its stored motion path. Saving writes them back in
        // the original document order. Only an explicit delete removes an entry.
        internal sealed class UnresolvedItem
        {
            public SceneObjectData Data;
            public string BaseScene, Reason;
        }
        private sealed class UnresolvedMotion
        {
            public string Stored, BaseScene, Reason;
        }
        private readonly List<string> documentOrder = new List<string>();
        private readonly Dictionary<string, UnresolvedItem> unresolved = new Dictionary<string, UnresolvedItem>();
        private readonly Dictionary<string, UnresolvedMotion> unresolvedMotions = new Dictionary<string, UnresolvedMotion>();
        internal int UnresolvedCount => unresolved.Count;
        internal int UnresolvedMotionCount => unresolvedMotions.Count;
        internal UnresolvedItem SelectedUnresolved => SelectedId != null && unresolved.TryGetValue(SelectedId, out var item) ? item : null;
        internal IEnumerable<UnresolvedItem> UnresolvedOfType(string type) =>
            documentOrder.Where(unresolved.ContainsKey).Select(id => unresolved[id]).Where(item => item.Data.type == type);
        internal string UnresolvedMotionOf(string id) => unresolvedMotions.TryGetValue(id, out var motion) ? motion.Stored : null;
        internal void SelectUnresolved(string id)
        {
            if (!unresolved.TryGetValue(id, out var item)) throw new ArgumentException("누락 항목을 찾을 수 없습니다.");
            SelectedId = id; Target = InspectorTarget.Object; Error = "";
            message = "누락 항목 선택: " + Path.GetFileNameWithoutExtension(item.Data.assetPath);
            Refresh();
        }
        private void ClearDocumentState()
        {
            documentOrder.Clear(); unresolved.Clear(); unresolvedMotions.Clear();
        }
        private static SceneObjectData CopyData(SceneObjectData data) => JsonUtility.FromJson<SceneObjectData>(JsonUtility.ToJson(data));
        // Same folder as the scene it was read from: keep the stored string verbatim. Saved
        // elsewhere: recompute so the reference still points at the same (missing) file.
        private static string Rebase(string stored, string baseScene, string targetScene)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            var from = Path.GetDirectoryName(Path.GetFullPath(baseScene)); var to = Path.GetDirectoryName(Path.GetFullPath(targetScene));
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(stored.Replace('/', Path.DirectorySeparatorChar))) return stored;
            return MmdSceneDocument.ToStored(targetScene, MmdSceneDocument.Resolve(baseScene, stored));
        }
        private void RebaseUnresolved(string targetScene)
        {
            foreach (var item in unresolved.Values)
            {
                item.Data.assetPath = Rebase(item.Data.assetPath, item.BaseScene, targetScene);
                item.Data.motionPath = Rebase(item.Data.motionPath, item.BaseScene, targetScene);
                item.BaseScene = targetScene;
            }
            foreach (var motion in unresolvedMotions.Values) { motion.Stored = Rebase(motion.Stored, motion.BaseScene, targetScene); motion.BaseScene = targetScene; }
        }
        private void ForgetUnresolvedMotion(string id) => unresolvedMotions.Remove(id);
        private bool RemoveUnresolved(string id)
        {
            if (!unresolved.Remove(id)) return false;
            documentOrder.Remove(id); MarkDirty();
            return true;
        }

        private void BuildProjectToolbar(VisualElement toolbar)
        {
            IconButton(toolbar, "NewScene", MmdIconKind.NewFile, "새로 만들기", () => RequestDiscardOrSave(NewScene));
            IconButton(toolbar, "OpenScene", MmdIconKind.Open, "열기 — .mmdscene 장면 파일", () => RequestDiscardOrSave(() =>
            {
                string path;
                try { path = WindowsRuntimeFilePicker.OpenScene(); }
                catch (Exception ex) { Error = ex.Message; Refresh(); return; }
                if (path == null) { message = "파일 선택을 취소했습니다."; Refresh(); return; }
                StartCoroutine(LoadScene(path));
            }));
            IconButton(toolbar, "SaveScene", MmdIconKind.Save, "저장 (Ctrl+S)", () => Guard(() => Save()));
            IconButton(toolbar, "SaveSceneAs", MmdIconKind.SaveAs, "다른 이름으로 저장", () => Guard(() => SaveAs()));
        }
        private void UpdateTitle()
        {
            if (titleLabel == null) return;
            var name = ProjectPath == null ? "새 장면" : Path.GetFileName(ProjectPath);
            titleLabel.text = name + (Dirty ? " *" : "");
        }

        // ----- Save -----
        // Returns true when the scene was written (false when the save dialog was cancelled).
        internal bool Save() => ProjectPath == null ? SaveAs() : SaveTo(ProjectPath);
        private bool SaveAs()
        {
            if (Busy) return false;
            var suggested = ProjectPath == null ? "새 장면" + MmdSceneDocument.Extension : Path.GetFileName(ProjectPath);
            var path = WindowsRuntimeFilePicker.SaveScene(suggested);
            if (path == null) { message = "저장을 취소했습니다."; return false; }
            return SaveTo(path);
        }
        internal bool SaveTo(string path)
        {
            if (Busy) return false;
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(full), MmdSceneDocument.Extension, StringComparison.OrdinalIgnoreCase)) full += MmdSceneDocument.Extension;
            MmdSceneDocument.Write(full, Capture(full));
            RebaseUnresolved(full);
            ProjectPath = full; Dirty = false; Error = "";
            message = "저장했습니다: " + Path.GetFileName(full);
            Refresh();
            return true;
        }
        internal SceneFile Capture(string sceneFile)
        {
            if (projectId == null) projectId = Guid.NewGuid().ToString("N");
            var scene = new SceneFile
            {
                projectId = projectId,
                selectedId = SelectedId,
                graphics = Graphics.Settings,
                view = new ViewData { pivot = View.Pivot, yaw = View.Yaw, pitch = View.Pitch, distance = View.Distance },
                timeline = new TimelineData { timeSeconds = Scene.Seconds, loop = Scene.Loop, speed = Scene.Speed },
            };
            SceneObjectData Live(MmdSceneService.SceneObject item)
            {
                var motion = item.MotionSource != null ? MmdSceneDocument.ToStored(sceneFile, item.MotionSource)
                    : unresolvedMotions.TryGetValue(item.Id, out var missing) ? Rebase(missing.Stored, missing.BaseScene, sceneFile) : null;
                return new SceneObjectData
                {
                    id = item.Id,
                    type = item.Kind == MmdSceneService.ObjectKind.Stage ? SceneObjectData.StageType : SceneObjectData.CharacterType,
                    assetPath = MmdSceneDocument.ToStored(sceneFile, item.ModelSource), motionPath = motion,
                    position = item.Placement.localPosition, rotationEuler = item.Placement.localEulerAngles, scale = item.Placement.localScale,
                    visible = item.Visible,
                };
            }
            SceneObjectData Missing(UnresolvedItem item)
            {
                var data = CopyData(item.Data);
                data.assetPath = Rebase(data.assetPath, item.BaseScene, sceneFile);
                data.motionPath = Rebase(data.motionPath, item.BaseScene, sceneFile);
                return data;
            }
            // Original document order first (live and unresolved entries interleaved as read),
            // then objects added since.
            var written = new HashSet<string>();
            foreach (var id in documentOrder)
            {
                var live = Scene.Objects.FirstOrDefault(item => item.Id == id);
                if (live != null) scene.sceneObjects.Add(Live(live));
                else if (unresolved.TryGetValue(id, out var missing)) scene.sceneObjects.Add(Missing(missing));
                else continue;
                written.Add(id);
            }
            foreach (var item in Scene.Objects) if (!written.Contains(item.Id)) scene.sceneObjects.Add(Live(item));
            return scene;
        }

        // ----- New / Open -----
        private void NewScene()
        {
            if (Busy) return;
            Scene.Clear(); Scene.Loop = false; Scene.Speed = 1; ClearDocumentState();
            Graphics.Apply(new SceneGraphicsSettings());
            View.Reset(); View.Apply(Camera.main);
            SelectedId = null; ProjectPath = null; projectId = null; Dirty = false; Error = "";
            message = "새 장면을 시작했습니다.";
            Refresh();
        }
        // Probe/command-line entry: asks about unsaved changes first, then loads.
        internal IEnumerator OpenFrom(string path)
        {
            RequestDiscardOrSave(() => StartCoroutine(LoadScene(path)));
            yield return null;
            while (Busy) yield return null;
        }
        // Parses and validates the whole file, then assembles the scene as hidden candidates with
        // the staged async loaders while the current scene stays as it is (H1-01c). Only when every
        // entry has been prepared is the current scene replaced in one step. Cancel (loading card)
        // or an unexpected failure discards the candidates and keeps the current scene, its file
        // path and selection. Missing or failing assets become unresolved entries (R0-02a).
        private IEnumerator LoadScene(string path)
        {
            if (Busy) yield break;
            var previousMessage = message;
            var playing = Scene.Playing;
            Busy = true; Error = ""; message = "장면을 불러오는 중…"; Scene.Playing = false; Refresh();
            yield return null;
            SceneFile file;
            try { file = MmdSceneDocument.Read(path); }
            catch (Exception ex)
            {
                Busy = false; Scene.ResumeAfterInterruption(playing);
                Error = "장면 파일을 열 수 없습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; Refresh();
                yield break;
            }
            var full = Path.GetFullPath(path);
            var problems = new List<string>();
            var prepared = new List<MmdSceneService.PreparedObject>();
            var pendingUnresolved = new Dictionary<string, UnresolvedItem>();
            var pendingMotions = new Dictionary<string, UnresolvedMotion>();
            var order = new List<string>();
            var cancelled = false; Exception failure = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            loadCancellation = new System.Threading.CancellationTokenSource();
            var token = loadCancellation.Token;
            void Unresolved(SceneObjectData data, string reason)
            {
                pendingUnresolved[data.id] = new UnresolvedItem { Data = CopyData(data), BaseScene = full, Reason = reason };
                problems.Add(reason + ": " + data.assetPath);
            }
            var count = file.sceneObjects.Count;
            for (var i = 0; i < count && !cancelled && failure == null; i++)
            {
                var data = file.sceneObjects[i];
                order.Add(data.id);
                var prefix = "장면 불러오는 중 (" + (i + 1) + "/" + count + ")";
                message = prefix + "…"; UpdateStatus(); RefreshLoadingCard();
                var pmx = MmdSceneDocument.Resolve(path, data.assetPath);
                if (!File.Exists(pmx)) { Unresolved(data, "모델 파일 없음"); continue; }
                var kind = data.type == SceneObjectData.StageType ? MmdSceneService.ObjectKind.Stage : MmdSceneService.ObjectKind.Character;
                var vmd = string.IsNullOrEmpty(data.motionPath) ? null : MmdSceneDocument.Resolve(path, data.motionPath);
                var task = Scene.PrepareAsync(pmx, vmd, kind, data.id, data.position, data.rotationEuler, data.scale, data.visible, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = prefix + " · " + stage; UpdateStatus(); RefreshLoadingCard();
                }, token);
                while (!task.IsCompleted) yield return null;
                if (task.IsCanceled || (task.IsFaulted && task.Exception.GetBaseException() is OperationCanceledException)) { cancelled = true; break; }
                if (task.IsFaulted)
                {
                    Unresolved(data, "모델 불러오기 실패 (" + task.Exception.GetBaseException().Message + ")");
                    continue;
                }
                var item = task.Result;
                prepared.Add(item);
                if (item.MotionError != null)
                {
                    pendingMotions[item.Id] = new UnresolvedMotion { Stored = data.motionPath, BaseScene = full, Reason = item.MotionError };
                    problems.Add(item.MotionError + ": " + data.motionPath);
                }
                if (token.IsCancellationRequested) cancelled = true;
            }
            SceneOpenSeconds = (float)clock.Elapsed.TotalSeconds;
            try
            {
                if (cancelled)
                {
                    foreach (var item in prepared) Scene.Discard(item);
                    Error = ""; message = "장면 열기를 취소했습니다. 기존 장면은 유지됩니다.";
                    Scene.ResumeAfterInterruption(playing);
                }
                else
                {
                    try { Scene.CommitScene(prepared); }
                    catch
                    {
                        foreach (var item in prepared) Scene.Discard(item);
                        throw;
                    }
                    ClearDocumentState();
                    documentOrder.AddRange(order);
                    foreach (var entry in pendingUnresolved) unresolved[entry.Key] = entry.Value;
                    foreach (var entry in pendingMotions) unresolvedMotions[entry.Key] = entry.Value;
                    Graphics.Apply(file.graphics);
                    View.Set(file.view.pivot, file.view.yaw, file.view.pitch, file.view.distance); View.Apply(Camera.main);
                    Scene.Loop = file.timeline.loop; Scene.Speed = file.timeline.speed;
                    Scene.Playing = false; Scene.Seek(Math.Min(file.timeline.timeSeconds, Scene.Duration));
                    SelectedId = file.selectedId != null && (Scene.Objects.Any(item => item.Id == file.selectedId) || unresolved.ContainsKey(file.selectedId))
                        ? file.selectedId : Scene.Objects.FirstOrDefault()?.Id;
                    Target = InspectorTarget.Object;
                    ProjectPath = full; projectId = file.projectId; Dirty = false;
                    message = "장면을 불러왔습니다: " + Path.GetFileName(path);
                    LastLoadProblems = problems.ToArray();
                    if (problems.Count > 0)
                        ShowDialog("일부 항목을 불러오지 못했습니다. 나머지 장면은 열렸습니다.\n" + string.Join("\n", problems.Take(8)) +
                            (problems.Count > 8 ? "\n외 " + (problems.Count - 8) + "개" : "") +
                            "\n누락 항목은 장면 탐색기에 '누락'으로 표시되고, 저장해도 장면 파일에서 사라지지 않습니다.",
                            null, ("NoticeOk", "확인", () => { }));
                }
            }
            catch (Exception ex)
            {
                Error = "장면을 복원하지 못했습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; message = previousMessage;
                Scene.ResumeAfterInterruption(playing);
            }
            finally
            {
                loadCancellation?.Dispose(); loadCancellation = null;
                Busy = false; Refresh(); RefreshLoadingCard();
            }
        }
        internal float SceneOpenSeconds { get; private set; }
        internal string[] LastLoadProblems { get; private set; } = new string[0];

        // ----- Unsaved changes -----
        internal void RequestDiscardOrSave(Action proceed)
        {
            if (Busy || ConfirmVisible || DialogVisible) return;
            if (!Dirty) { proceed(); return; }
            ShowDialog("저장하지 않은 변경 사항이 있습니다. 저장할까요?", () => { message = "취소했습니다."; },
                ("UnsavedSave", "저장", () => { try { if (Save()) proceed(); } catch (Exception ex) { Error = "저장하지 못했습니다: " + ex.Message; Refresh(); } }),
                ("UnsavedDiscard", "저장 안 함", proceed),
                ("UnsavedCancel", "취소", null));
        }
        // Application.wantsToQuit handler. Returns false and asks when there are unsaved changes.
        internal bool HandleQuitRequest()
        {
            if (quitConfirmed || !Dirty) return true;
            RequestDiscardOrSave(() => { quitConfirmed = true; Application.Quit(); });
            return false;
        }
        private bool OnWantsToQuit() => SuppressQuitPrompt || HandleQuitRequest();

        // ----- Generic modal dialog (unsaved changes, load notices) -----
        private void CreateDialog()
        {
            dialog = Element(Root, "eds-overlay"); dialog.name = "ProjectDialog";
            var box = Element(dialog, "eds-dialog");
            dialogText = Text(box, "", "eds-dialog-text"); dialogText.name = "ProjectDialogText";
            dialogButtons = Element(box, "eds-dialog-buttons");
            dialog.style.display = DisplayStyle.None;
        }
        private void ShowDialog(string text, Action cancel, params (string name, string label, Action action)[] buttons)
        {
            dialogText.text = text; dialogButtons.Clear(); dialogCancel = cancel;
            for (var i = 0; i < buttons.Length; i++)
            {
                var (name, label, action) = buttons[i]; var act = action;
                // The first button is the primary action (save / OK).
                var button = TextButton(dialogButtons, name, label, () => { CloseDialog(); if (act != null) act(); else dialogCancel?.Invoke(); Refresh(); });
                if (i == 0) button.AddToClassList("eds-button--primary");
                if (i == buttons.Length - 1) button.style.marginRight = 0;
            }
            dialog.style.display = DisplayStyle.Flex; DialogVisible = true;
            dialogButtons.Q<Button>(buttons[buttons.Length - 1].name)?.Focus();
        }
        private void CloseDialog() { dialog.style.display = DisplayStyle.None; DialogVisible = false; }
        private void CancelDialog()
        {
            if (!DialogVisible) return;
            CloseDialog(); dialogCancel?.Invoke(); Refresh();
        }
    }
}
