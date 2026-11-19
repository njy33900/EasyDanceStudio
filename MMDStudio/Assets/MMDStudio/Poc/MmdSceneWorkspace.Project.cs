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
            foreach (var item in Scene.Objects)
                scene.sceneObjects.Add(new SceneObjectData
                {
                    id = item.Id,
                    type = item.Kind == MmdSceneService.ObjectKind.Stage ? SceneObjectData.StageType : SceneObjectData.CharacterType,
                    assetPath = MmdSceneDocument.ToStored(sceneFile, item.ModelSource),
                    motionPath = item.MotionSource == null ? null : MmdSceneDocument.ToStored(sceneFile, item.MotionSource),
                    position = item.Placement.localPosition, rotationEuler = item.Placement.localEulerAngles, scale = item.Placement.localScale,
                    visible = item.Visible,
                });
            return scene;
        }

        // ----- New / Open -----
        private void NewScene()
        {
            if (Busy) return;
            Scene.Clear(); Scene.Loop = false; Scene.Speed = 1;
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
        // Parses and validates the whole file before touching the current scene; an
        // unreadable/invalid file keeps the scene. Missing or failing assets are skipped
        // and listed; the rest of the project is still restored.
        private IEnumerator LoadScene(string path)
        {
            if (Busy) yield break;
            Busy = true; Error = ""; message = "장면을 불러오는 중…"; Refresh();
            yield return null;
            SceneFile file;
            try { file = MmdSceneDocument.Read(path); }
            catch (Exception ex)
            {
                Busy = false; Error = "장면 파일을 열 수 없습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; Refresh();
                yield break;
            }
            var problems = new List<string>();
            try
            {
                Scene.Clear(); SelectedId = null;
                var ids = new Dictionary<string, string>();
                foreach (var data in file.sceneObjects)
                {
                    var pmx = MmdSceneDocument.Resolve(path, data.assetPath);
                    if (!File.Exists(pmx)) { problems.Add("모델 파일 없음: " + data.assetPath); continue; }
                    MmdSceneService.SceneObject item;
                    try { item = data.type == SceneObjectData.StageType ? Scene.ImportStage(pmx, data.position) : Scene.Import(pmx, null, data.position); }
                    catch (Exception ex) { problems.Add("모델 불러오기 실패: " + data.assetPath + " (" + ex.Message + ")"); continue; }
                    Scene.SetPlacement(item.Id, data.position, data.rotationEuler, data.scale);
                    if (!data.visible) Scene.SetVisible(item.Id, false);
                    if (!string.IsNullOrEmpty(data.motionPath))
                    {
                        var vmd = MmdSceneDocument.Resolve(path, data.motionPath);
                        if (!File.Exists(vmd)) problems.Add("모션 파일 없음: " + data.motionPath);
                        else
                        {
                            try { Scene.ReplaceMotion(item.Id, vmd); }
                            catch (Exception ex) { problems.Add("모션 불러오기 실패: " + data.motionPath + " (" + ex.Message + ")"); }
                        }
                    }
                    ids[data.id] = item.Id;
                }
                Graphics.Apply(file.graphics);
                View.Set(file.view.pivot, file.view.yaw, file.view.pitch, file.view.distance); View.Apply(Camera.main);
                Scene.Loop = file.timeline.loop; Scene.Speed = file.timeline.speed;
                Scene.Playing = false; Scene.Seek(Math.Min(file.timeline.timeSeconds, Scene.Duration));
                SelectedId = file.selectedId != null && ids.TryGetValue(file.selectedId, out var selected) ? selected : Scene.Objects.FirstOrDefault()?.Id;
                ProjectPath = Path.GetFullPath(path); projectId = file.projectId; Dirty = false;
                message = "장면을 불러왔습니다: " + Path.GetFileName(path);
                LastLoadProblems = problems.ToArray();
                if (problems.Count > 0)
                    ShowDialog("일부 항목을 불러오지 못했습니다. 나머지 장면은 열렸습니다.\n" + string.Join("\n", problems.Take(8)) +
                        (problems.Count > 8 ? "\n외 " + (problems.Count - 8) + "개" : "") + "\n파일을 옮겼다면 다시 추가한 뒤 저장하세요.",
                        null, ("NoticeOk", "확인", () => { }));
            }
            catch (Exception ex) { Error = "장면을 복원하는 중 오류가 발생했습니다: " + ex.Message; }
            finally { Busy = false; Refresh(); }
        }
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
