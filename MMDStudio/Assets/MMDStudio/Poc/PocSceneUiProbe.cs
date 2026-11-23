using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    public sealed class PocSceneUiProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, screenshot;
            public string[] checks;
            public float pmxLoadSeconds, pmxWorstFrameMs;
            public float sceneOpenSeconds, sceneOpenWorstFrameMs;
            public int sceneOpenFrames;
            public string[] sceneOpenSlowestFrames;
            public string[] orphanLifetimes;
            public int physicsBakeFrames;
            public float physicsPoseDifferenceM, physicsStrengthZeroDifferenceM, springPoseDifferenceM, springZeroDifferenceM;
            public int springBakeFrames;
            public int pmxLoadFrames;
            public string[] pmxStageTimings;
        }
        // Answers the workspace's file dialogs without a native window; records the last request.
        private sealed class ScriptedDialogs : IStudioFileDialogs
        {
            public string Next, Asked;
            private string Answer(string request) { Asked = request; var answer = Next; Next = null; return answer; }
            public string OpenModel() => Answer("OpenModel");
            public string OpenMotion() => Answer("OpenMotion");
            public string OpenScene() => Answer("OpenScene");
            public string OpenAudio() => Answer("OpenAudio");
            public string OpenCameraMotion() => Answer("OpenCameraMotion");
            public string SaveOutput(string suggestedName, string extension) => Answer("SaveOutput:" + extension);
            public string SaveScene(string suggestedName) => Answer("SaveScene:" + suggestedName);
            public string SaveText(string suggestedName) => Answer("SaveText");
        }
        // Records what the input router asks for; the viewport covers everything unless told otherwise.
        private sealed class FakeViewportTarget : IViewportInputTarget
        {
            public readonly List<string> Log = new List<string>();
            public bool InViewport = true;
            public bool CameraReady => true;
            public bool DialogOpen { get; set; }
            public void CancelDialog() => Log.Add("cancelDialog");
            public bool ConfirmOpen => false;
            public void CancelConfirm() => Log.Add("cancelConfirm");
            public bool TextFocused => false;
            public bool FocusView => false;
            public void SetFocusView(bool focused) => Log.Add("focus " + focused);
            public bool Busy => false;
            public bool PointerInViewport(Vector2 screen) => InViewport;
            public void ReleaseFocus() { }
            public bool GizmoUnderPointer;
            public bool BeginGizmoDrag(Vector2 screen) { if (GizmoUnderPointer) Log.Add("gizmoBegin"); return GizmoUnderPointer; }
            public void UpdateGizmoDrag(Vector2 screen) => Log.Add("gizmoDrag");
            public void EndGizmoDrag() => Log.Add("gizmoEnd");
            public void UpdateGizmoHover(Vector2 screen, bool overViewport) { }
            public void ClickAt(Vector2 screen) => Log.Add("click " + screen);
            public void Orbit(Vector2 delta) => Log.Add("orbit " + delta.x.ToString("0") + "," + delta.y.ToString("0"));
            public void Pan(Vector2 delta) => Log.Add("pan");
            public void Dolly(float pixels) => Log.Add("dolly " + pixels.ToString("0"));
            public void Zoom(float notches, Vector2 screen) => Log.Add("zoom");
            public void ApplyView() { }
            public void FrameSelection() => Log.Add("frame");
            public void TogglePlay() => Log.Add("play");
            public void RequestDelete() => Log.Add("delete");
            public void Save() => Log.Add("save");
            public void Undo() => Log.Add("undo");
            public void Redo() => Log.Add("redo");
            public void SetGizmo(int key) => Log.Add("gizmo " + key);
        }
        private readonly List<string> checks = new List<string>();
        private string reportDirectory;
        private void Check(string name, bool passed)
        { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        private static void Activate(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("UI button unavailable: " + name);
            button.Focus();
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }
        public IEnumerator Run(MmdSceneWorkspace ui, string path)
        {
            var report = new Report();
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            reportDirectory = Path.GetDirectoryName(path);
            var aMotion = Path.Combine(Path.GetDirectoryName(path), "first.vmd");
            var bMotion = Path.Combine(Path.GetDirectoryName(path), "second.vmd");
            var invalid = Path.Combine(Path.GetDirectoryName(path), "invalid.pmx");
            var badVmd = Path.Combine(Path.GetDirectoryName(path), "invalid.vmd");
            PocPhysicsReferenceProbe.WriteFixture(aMotion); PocPhysicsReferenceProbe.WriteFixture(bMotion, .5f);
            File.WriteAllText(invalid, "invalid PMX"); File.WriteAllBytes(badVmd, new byte[64]);
            ui.Scene.AutomaticClock = false;
            ui.DeviceInput = false; // A real mouse over the test window must not move the camera.
            ui.SuppressQuitPrompt = true; // Application.Quit at the end must not be blocked by unsaved changes.
            yield return null;
            try { Check("empty_scene_has_no_selection_highlight", !ui.HighlightVisible); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            try { Check("empty_controls_initialized", ui.Scene.Objects.Count == 0 && !ui.Root.Q<Button>("ConnectMotion").enabledSelf && !ui.Root.Q<Button>("PlayPause").enabledSelf); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocPmx"), true);
            var a = ui.Scene.Objects.FirstOrDefault();
            try { Check("model_only_import_selects_static_character", a != null && ui.SelectedId == a.Id && a.Motion == null && !ui.Root.Q<Button>("PlayPause").enabledSelf); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(badVmd, false);
            try { Check("failed_first_vmd_preserves_static_model", ui.Error.Length > 0 && a.Motion == null && ui.Scene.Objects.Count == 1); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(aMotion, false);
            try { Check("first_vmd_enables_shared_playback", a.Motion != null && ui.Root.Q<Button>("PlayPause").enabledSelf && ui.Error.Length == 0); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            var pmxStart = Time.realtimeSinceStartup;
            StartCoroutine(ui.ApplyPickerResult(PocArguments.Value("-pocSecondPmx"), true));
            while (ui.Busy)
            {
                yield return null;
                report.pmxLoadFrames++;
                report.pmxWorstFrameMs = Mathf.Max(report.pmxWorstFrameMs, Time.unscaledDeltaTime * 1000);
            }
            report.pmxLoadSeconds = Time.realtimeSinceStartup - pmxStart;
            report.pmxStageTimings = ui.Scene.Objects.LastOrDefault()?.Summary.loadStageTimings;
            yield return ui.ApplyPickerResult(bMotion, false);
            var b = ui.Scene.Objects.LastOrDefault();
            var cancelledImport = StartCoroutine(ui.ApplyPickerResult(PocArguments.Value("-pocSecondPmx"), true));
            for (var frame = 0; frame < 30 && ui.Root.Q<VisualElement>("LoadingCard").resolvedStyle.display != DisplayStyle.Flex; frame++)
                yield return null;
            try
            {
                Check("pmx_loading_shows_stage_and_cancel", ui.Busy &&
                    ui.Root.Q<Label>("LoadingLabel").text.Contains("PMX") &&
                    ui.Root.Q<Button>("CancelLoading").enabledInHierarchy);
                Activate(ui.Root, "CancelLoading");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return cancelledImport;
            yield return null; // Deferred Unity destruction of any prepared candidate.
            try
            {
                Check("cancelled_pmx_preserves_scene", !ui.Busy && ui.Error.Length == 0 && ui.Scene.Objects.Count == 2 &&
                    ui.Scene.Objects[0].Id == a.Id && ui.Scene.Objects[1].Id == b.Id && ui.SelectedId == b.Id &&
                    ui.Root.Q<VisualElement>("LoadingCard").resolvedStyle.display == DisplayStyle.None);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            ui.Select(a.Id);
            var priorMotion = a.Motion;
            var priorMotionSource = a.MotionSource;
            var cancelledMotion = StartCoroutine(ui.ApplyPickerResult(PocArguments.Value("-pocVmd"), MmdSceneWorkspace.PickKind.Motion));
            for (var frame = 0; frame < 30 && ui.Root.Q<VisualElement>("LoadingCard").resolvedStyle.display != DisplayStyle.Flex; frame++)
                yield return null;
            try
            {
                Check("vmd_loading_shows_stage_and_cancel", ui.Busy &&
                    ui.Root.Q<Label>("LoadingLabel").text.Contains("VMD") &&
                    ui.Root.Q<Button>("CancelLoading").enabledInHierarchy);
                Activate(ui.Root, "CancelLoading");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return cancelledMotion;
            yield return null;
            try
            {
                Check("cancelled_vmd_keeps_previous_motion", !ui.Busy && ui.Error.Length == 0 &&
                    ReferenceEquals(a.Motion, priorMotion) && a.MotionSource == priorMotionSource &&
                    ui.Scene.Objects.Count == 2 && ui.SelectedId == a.Id);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            try
            {
                Check("second_character_and_independent_vmd_visible_in_list", ui.Scene.Objects.Count == 2 && a.MotionSource != b.MotionSource && ui.Root.Q<Button>("object-"+a.Id) != null && ui.Root.Q<Button>("object-"+b.Id) != null);
                Activate(ui.Root, "object-" + a.Id);
                Check("list_submit_selects_character_and_inspector", ui.SelectedId == a.Id && ui.Root.Q<Label>("SelectionInfo").text.Contains(Path.GetFileName(a.ModelSource)));
                var originalB = b.Placement.localPosition;
                ui.Root.Q<Vector3Field>("Position").value = new Vector3(-1, 0, .2f);
                ui.Root.Q<Vector3Field>("Rotation").value = new Vector3(0, 20, 0);
                ui.Root.Q<Vector3Field>("Scale").value = new Vector3(.9f, .9f, .9f);
                Check("inspector_changes_only_selected_transform", a.Placement.localPosition == new Vector3(-1, 0, .2f) && Math.Abs(a.Placement.localEulerAngles.y-20) < .001f && a.Placement.localScale == new Vector3(.9f,.9f,.9f) && b.Placement.localPosition == originalB);
                ui.Root.Q<Vector3Field>("Scale").value = new Vector3(-1, 1, 1);
                Check("invalid_scale_is_rejected_and_field_restored", ui.Error.Length > 0 && a.Placement.localScale.x == .9f && ui.Root.Q<Vector3Field>("Scale").value.x == .9f);
                ui.Root.Q<Toggle>("Visible").value = false;
                ui.Scene.Seek(.5);
                Check("hidden_character_keeps_live_owner_and_clock", !a.Visible && a.Owner.gameObject.activeSelf && a.Motion.TimeSeconds == .5f);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            try
            {
                Check("hidden_renderers_stay_hidden_across_frame", a.Model.root.GetComponentsInChildren<Renderer>().All(renderer=>!renderer.enabled));
                ui.Root.Q<Toggle>("Visible").value = true;
                Check("visibility_restored", a.Visible && a.Model.root.GetComponentsInChildren<Renderer>().All(renderer=>renderer.enabled));
                ui.Root.Q<Slider>("Timeline").value = .5f;
                Check("timeline_widget_seeks_both", ui.Scene.Seconds == 1.5 && a.Motion.TimeSeconds == 1.5f && b.Motion.TimeSeconds == 1.5f);
                ui.Root.Q<Toggle>("Loop").value = true; ui.Root.Q<Slider>("Speed").value = 2;
                Check("loop_and_speed_widgets_update_clock", ui.Scene.Loop && ui.Scene.Speed == 2);
                Activate(ui.Root, "PlayPause"); ui.Scene.Advance(.25);
                Check("play_submit_advances_shared_clock", ui.Scene.Playing && ui.Scene.Seconds == 2 && a.Motion.TimeSeconds == 2 && b.Motion.TimeSeconds == 2);
                Activate(ui.Root, "PlayPause"); var paused = ui.Scene.Seconds; ui.Scene.Advance(.5);
                Check("pause_submit_holds_clock", !ui.Scene.Playing && ui.Scene.Seconds == paused);
                Activate(ui.Root, "Stop"); Check("stop_submit_resets_both", ui.Scene.Seconds == 0 && a.Motion.TimeSeconds == 0 && b.Motion.TimeSeconds == 0);
                Activate(ui.Root, "FrameAll");
                Check("viewport_fills_window", Camera.main.rect == new Rect(0, 0, 1, 1));
                ViewportChecks(ui, a, b);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            string deviceError = null;
            yield return DeviceInputChecks(ui, a, b, error => deviceError = error);
            if (deviceError != null) { Finish(path, report, deviceError); yield break; }
            yield return ui.ApplyPickerResult(invalid, true);
            try { Check("bad_import_reports_error_without_losing_selection", ui.Error.Length > 0 && ui.Scene.Objects.Count == 2 && ui.SelectedId == a.Id); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(null, true);
            try
            {
                Check("picker_cancel_is_non_destructive", ui.Error.Length == 0 && ui.Scene.Objects.Count == 2 && !ui.Busy);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            var duration = ui.Scene.Duration;
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocStagePmx"), MmdSceneWorkspace.PickKind.Stage);
            var stage = ui.Scene.Objects.FirstOrDefault(item => item.Kind == MmdSceneService.ObjectKind.Stage);
            try
            {
                Check("stage_import_adds_selected_static_background", stage != null && ui.Error.Length == 0 && ui.Scene.Objects.Count == 3 && ui.SelectedId == stage.Id &&
                    stage.Motion == null && stage.MotionSource == null && stage.Placement.localPosition == Vector3.zero && ui.Scene.Duration == duration &&
                    stage.Summary.materials == 2 && stage.Summary.loadedTextures == 2 && stage.Summary.bones == 1);
                Check("explorer_groups_characters_and_stage", ui.Root.Q<Label>("CharacterGroup").text.Contains("(2)") && ui.Root.Q<Label>("StageGroup").text.Contains("(1)") &&
                    ui.Root.Q<Button>("object-" + stage.Id) != null && ui.Root.Q<Label>("SelectionTitle").text == "선택한 배경" &&
                    !ui.Root.Q<Button>("ConnectMotion").enabledSelf && ui.Root.Q<Button>("RemoveModel").enabledSelf);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            var aSource = a.MotionSource; var bSource = b.MotionSource;
            yield return ui.ApplyPickerResult(aMotion, MmdSceneWorkspace.PickKind.Motion);
            try
            {
                Check("motion_pick_on_stage_is_rejected_without_changes", ui.Error.Length > 0 && stage.Motion == null && a.MotionSource == aSource && b.MotionSource == bSource && ui.Scene.Duration == duration);
                var threw = false;
                try { ui.Scene.ReplaceMotion(stage.Id, aMotion); } catch (InvalidOperationException) { threw = true; }
                Check("service_rejects_stage_motion", threw && stage.Motion == null);
                var parts = stage.Model.root.GetComponentsInChildren<Transform>();
                var poses = parts.Select(part => (part.localPosition, part.localRotation)).ToArray();
                ui.Scene.Seek(1); ui.Scene.Playing = true; ui.Scene.Advance(.5); ui.Scene.Playing = false;
                Check("stage_stays_static_while_characters_play", ui.Scene.Seconds > 1 && a.Motion.TimeSeconds == (float)ui.Scene.Seconds && b.Motion.TimeSeconds == (float)ui.Scene.Seconds &&
                    parts.Select(part => (part.localPosition, part.localRotation)).SequenceEqual(poses));
                ui.Root.Q<Vector3Field>("Position").value = new Vector3(0, -.05f, .5f);
                ui.Root.Q<Vector3Field>("Scale").value = new Vector3(1.2f, 1.2f, 1.2f);
                Check("stage_inspector_transform_applies_to_stage_only", stage.Placement.localPosition == new Vector3(0, -.05f, .5f) && stage.Placement.localScale == new Vector3(1.2f, 1.2f, 1.2f) &&
                    a.Placement.localPosition == new Vector3(-1, 0, .2f) && a.Placement.localScale == new Vector3(.9f, .9f, .9f));
                ui.Root.Q<Toggle>("Visible").value = false;
                Check("hidden_stage_marked_in_list_and_highlight_hidden", !stage.Visible && stage.Model.root.GetComponentsInChildren<Renderer>().All(renderer => !renderer.enabled) &&
                    ui.Root.Q<Button>("object-" + stage.Id).text.Contains("(숨김)") && !ui.HighlightVisible);
                ui.Root.Q<Toggle>("Visible").value = true;
                MmdSceneWorkspace.TryBounds(new[] { stage }, out var stageBounds);
                Check("stage_selection_highlight_follows_stage", ui.HighlightVisible && Near(ui.HighlightBounds.center, stageBounds.center) && Near(ui.HighlightBounds.size, stageBounds.size));
                Activate(ui.Root, "object-" + a.Id);
                var statusText = ui.Root.Q<Label>("Status").text;
                Check("selection_after_failure_clears_stale_failure_status", ui.Error.Length == 0 && !statusText.Contains("불러오지") &&
                    ui.Root.Q<Label>("SelectionInfo").text.Contains(Path.GetFileName(a.ModelSource)));
                Activate(ui.Root, "FrameAll");
                PickingChecks(ui, a, b, stage);
                GizmoChecks(ui, a);
                GraphicsChecks(ui);
                MaterialChecks(ui, a, b, stage, reportDirectory);
                CorrectionChecks(ui, a, b, stage, reportDirectory);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // V0 layout uses the actual UI panel for hit testing; allow layout to settle
            // between display changes before querying the panel's pick tree.
            try
            {
                Check("floating_ui_starts_visible_over_full_camera", Camera.main.rect == new Rect(0, 0, 1, 1) &&
                    ui.PanelVisible("TopBar") && ui.PanelVisible("Explorer") && ui.PanelVisible("Inspector") &&
                    ui.PanelVisible("PlaybackBar") && ui.PanelVisible("StatusBar") && ui.PanelVisible("GizmoModes"));
                Check("each_floating_card_has_a_hide_button", new[] { "TopBar", "Explorer", "Inspector", "PlaybackBar", "StatusBar", "GizmoModes" }
                    .All(name => ui.Root.Q<Button>("Hide" + name) != null));
                Activate(ui.Root, "HidePlaybackBar");
                Activate(ui.Root, "HideStatusBar");
                Activate(ui.Root, "HideGizmoModes");
                var cardsHidden = !ui.PanelVisible("PlaybackBar") && !ui.PanelVisible("StatusBar") && !ui.PanelVisible("GizmoModes");
                Check("hidden_cards_leave_edge_tabs", cardsHidden && new[] { "PlaybackBar", "StatusBar", "GizmoModes" }
                    .All(name => ui.Root.Q<Button>("Show" + name).resolvedStyle.display == DisplayStyle.Flex));
                Activate(ui.Root, "ShowPlaybackBar");
                Activate(ui.Root, "ShowStatusBar");
                Activate(ui.Root, "ShowGizmoModes");
                Check("edge_tabs_restore_individual_cards", ui.PanelVisible("PlaybackBar") && ui.PanelVisible("StatusBar") &&
                    ui.PanelVisible("GizmoModes"));
                Activate(ui.Root, "HideStatusBar");
                Activate(ui.Root, "ViewMenuToggle");
                Activate(ui.Root, "ToggleStatusBar");
                Activate(ui.Root, "ViewMenuToggle");
                Check("view_menu_also_restores_hidden_card", ui.PanelVisible("StatusBar"));
                Activate(ui.Root, "HideExplorer");
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-collapsed.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.25f);
            try
            {
                Check("collapsed_tab_screenshot_created", File.Exists(Path.Combine(Path.GetDirectoryName(path), "scene-ui-collapsed.png")));
                Check("hidden_explorer_releases_its_pixels_to_3d", !ui.PanelVisible("Explorer") && ui.PointerInViewport(new Vector2(64, Screen.height * .5f)));
                Check("hidden_explorer_leaves_reopen_tab", ui.Root.Q<Button>("ShowExplorer").resolvedStyle.display == DisplayStyle.Flex);
                Activate(ui.Root, "ShowExplorer");
                Activate(ui.Root, "HideTopBar");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            try
            {
                Check("hidden_topbar_has_restore_control", !ui.PanelVisible("TopBar") &&
                    ui.PointerInViewport(new Vector2(Screen.width * .5f, Screen.height - 38)) &&
                    ui.Root.Q<Button>("RestoreControls").resolvedStyle.display == DisplayStyle.Flex);
                Activate(ui.Root, "RestoreControls");
                Check("restore_control_reopens_topbar", ui.PanelVisible("TopBar"));
                Activate(ui.Root, "HideInspector");
                Activate(ui.Root, "ViewMenuToggle");
                Check("inspector_button_hides_individual_card", !ui.PanelVisible("Inspector") &&
                    ui.Root.Q<Button>("ShowInspector").resolvedStyle.display == DisplayStyle.Flex);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            try
            {
                Check("hidden_inspector_releases_its_pixels_to_3d", ui.PointerInViewport(new Vector2(Screen.width - 70, Screen.height * .5f)));
                Activate(ui.Root, "ToggleInspector");
                Activate(ui.Root, "ViewMenuToggle");
                var cameraBeforeFocus = Camera.main.transform.position;
                Key(ui, focus: true);
                Check("h_focus_hides_all_control_cards", ui.FocusView && !ui.PanelVisible("TopBar") && !ui.PanelVisible("Explorer") &&
                    !ui.PanelVisible("Inspector") && !ui.PanelVisible("PlaybackBar") && !ui.PanelVisible("GizmoModes") &&
                    new[] { "Explorer", "Inspector", "PlaybackBar", "StatusBar", "GizmoModes" }
                        .All(name => ui.Root.Q<Button>("Show" + name).resolvedStyle.display == DisplayStyle.None) &&
                    ui.Root.Q<Button>("RestoreControls").resolvedStyle.display == DisplayStyle.Flex &&
                    !ui.HighlightVisible && !ui.GizmoVisible && Near(Camera.main.transform.position, cameraBeforeFocus));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            try
            {
                Check("focus_view_accepts_3d_input_across_window", ui.PointerInViewport(new Vector2(64, Screen.height * .5f)) &&
                    ui.PointerInViewport(new Vector2(Screen.width - 70, Screen.height * .5f)) &&
                    ui.PointerInViewport(new Vector2(Screen.width * .5f, Screen.height - 38)));
                var before = ui.View.Yaw;
                LeftDrag(ui, new Vector2(Screen.width * .5f, Screen.height * .5f), new Vector2(Screen.width * .5f + 30, Screen.height * .5f));
                Check("focus_view_orbit_still_works", !Near(ui.View.Yaw, before));
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-focus.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            // ScreenCapture writes after rendering; keep focus view active until the file exists.
            yield return new WaitForSecondsRealtime(.3f);
            try
            {
                Check("focus_view_screenshot_created", File.Exists(Path.Combine(Path.GetDirectoryName(path), "scene-ui-focus.png")));
                Key(ui, cancel: true);
                Check("escape_restores_controls_without_dialog", !ui.FocusView && ui.PanelVisible("TopBar") && ui.PanelVisible("Explorer"));
                Key(ui, focus: true); Key(ui, focus: true);
                Check("h_toggles_focus_view_back", !ui.FocusView && ui.PanelVisible("Inspector") && ui.PanelVisible("PlaybackBar"));
                Screen.SetResolution(1600, 900, false);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.4f);
            try
            {
                Check("resized_window_keeps_full_camera_and_panel_input", Screen.width == 1600 && Screen.height == 900 &&
                    Camera.main.rect == new Rect(0, 0, 1, 1) &&
                    ui.PointerOverUi(new Vector2(64, Screen.height * .5f)) &&
                    ui.PointerInViewport(new Vector2(Screen.width * .5f, Screen.height * .5f)));
                Screen.SetResolution(1280, 720, false);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.4f);

            // ----- MVP-07 project save/open/new -----
            var project = Path.Combine(Path.GetDirectoryName(path), "project");
            Directory.CreateDirectory(project);
            foreach (var old in Directory.GetFiles(project)) File.Delete(old);
            var sceneFile = Path.Combine(project, "scene.mmdscene");
            var aModel = a.ModelSource; var bModel = b.ModelSource; var stageModel = stage.ModelSource;
            string[] ids = null;
            SceneObjectData stageEntry = null, firstEntry = null; string[] characterIds = null;
            try
            {
                Check("unsaved_changes_marked_in_title", ui.Dirty && ui.ProjectPath == null && ui.Root.Q<Label>("ProjectTitle").text.EndsWith(" *"));
                ui.SaveTo(sceneFile);
                var saved = MmdSceneDocument.Read(sceneFile);
                Check("save_writes_valid_scene_with_relative_paths", !ui.Dirty && ui.Root.Q<Label>("ProjectTitle").text == "scene.mmdscene" &&
                    saved.sceneObjects.Count == 3 && saved.sceneObjects.All(item => !Path.IsPathRooted(item.assetPath) && item.assetPath.StartsWith("../")) &&
                    saved.sceneObjects.Count(item => item.type == SceneObjectData.StageType && string.IsNullOrEmpty(item.motionPath)) == 1 &&
                    saved.sceneObjects.Where(item => item.type == SceneObjectData.CharacterType).All(item => !string.IsNullOrEmpty(item.motionPath)) &&
                    saved.sceneObjects.Select(item => MmdSceneDocument.Resolve(sceneFile, item.assetPath)).SequenceEqual(ui.Scene.Objects.Select(item => item.ModelSource), StringComparer.OrdinalIgnoreCase) &&
                    saved.selectedId == ui.SelectedId && JsonUtility.ToJson(saved.graphics) == JsonUtility.ToJson(ui.Graphics.Settings) &&
                    saved.timeline.loop == ui.Scene.Loop && saved.timeline.speed == ui.Scene.Speed);
                Check("atomic_save_leaves_no_temporary_files", Directory.GetFiles(project).Length == 1);
                Activate(ui.Root, "object-" + a.Id);
                ui.Root.Q<Vector3Field>("Position").value = new Vector3(-2, 0, 0);
                ui.Root.Q<Slider>("Exposure").value = 1;
                Check("edits_mark_scene_dirty", ui.Dirty && ui.Root.Q<Label>("ProjectTitle").text == "scene.mmdscene *");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(sceneFile);
            try
            {
                var asked = ui.DialogVisible && ui.Root.Q<Button>("UnsavedDiscard") != null && ui.Root.Q<Button>("UnsavedSave") != null;
                Activate(ui.Root, "UnsavedCancel");
                Check("open_with_unsaved_changes_asks_and_cancel_keeps_scene", asked && !ui.DialogVisible && ui.Dirty && ui.Scene.Objects.Contains(a) &&
                    a.Placement.localPosition == new Vector3(-2, 0, 0) && Near(ui.Graphics.Settings.exposure, 1));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(sceneFile);
            try { Activate(ui.Root, "UnsavedDiscard"); }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                Check("discard_reopens_saved_state_exactly", !ui.Dirty && ui.Error.Length == 0 && ui.LastLoadProblems.Length == 0 && ui.Scene.Objects.Count == 3 &&
                    a != null && b != null && stage != null && a.Placement.localPosition == new Vector3(-1, 0, .2f) &&
                    Quaternion.Angle(a.Placement.localRotation, Quaternion.Euler(0, 20, 0)) < .01f && a.Placement.localScale == new Vector3(.9f, .9f, .9f) &&
                    string.Equals(a.MotionSource, aSource, StringComparison.OrdinalIgnoreCase) && string.Equals(b.MotionSource, bSource, StringComparison.OrdinalIgnoreCase) &&
                    stage.Placement.localPosition == new Vector3(0, -.05f, .5f) && stage.Placement.localScale == new Vector3(1.2f, 1.2f, 1.2f) && stage.Motion == null &&
                    Near(ui.Graphics.Settings.exposure, 0) && ui.Graphics.Settings.bloomEnabled && ui.Graphics.Settings.tonemapping && ui.SelectedId == stage.Id &&
                    ui.Scene.Duration == duration);
                var broken = MmdSceneDocument.Read(sceneFile);
                broken.sceneObjects.First(item => item.type == SceneObjectData.StageType).assetPath = "../nowhere/stage.pmx";
                broken.sceneObjects.First(item => item.type == SceneObjectData.CharacterType).motionPath = "../nowhere/motion.vmd";
                MmdSceneDocument.Write(Path.Combine(project, "missing.mmdscene"), broken);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(project, "missing.mmdscene"));
            try
            {
                var first = ui.Scene.Objects.FirstOrDefault();
                Check("missing_assets_skipped_listed_rest_restored", ui.DialogVisible && ui.LastLoadProblems.Length == 2 && ui.DialogText.Contains("nowhere") &&
                    ui.Scene.Objects.Count == 2 && ui.Scene.Objects.All(item => item.Kind == MmdSceneService.ObjectKind.Character) &&
                    first != null && first.Motion == null && ui.Scene.Objects[1].Motion != null);
                Activate(ui.Root, "NoticeOk");

                // R0-02a: unresolved entries stay in the document.
                var missingPath = Path.Combine(project, "missing.mmdscene");
                var original = MmdSceneDocument.Read(missingPath);
                stageEntry = original.sceneObjects.First(item => item.type == SceneObjectData.StageType);
                firstEntry = original.sceneObjects.First(item => item.type == SceneObjectData.CharacterType);
                characterIds = original.sceneObjects.Where(item => item.type == SceneObjectData.CharacterType).Select(item => item.id).ToArray();
                var missingRow = ui.Root.Q<Button>("unresolved-" + stageEntry.id);
                Check("missing_entries_kept_and_marked_with_stable_ids", ui.UnresolvedCount == 1 && ui.UnresolvedMotionCount == 1 &&
                    missingRow != null && missingRow.text.Contains("(누락)") && ui.Root.Q<Button>("object-" + firstEntry.id).text.Contains("(모션 누락)") &&
                    ui.Root.Q<Label>("StageGroup").text.Contains("(1)") && ui.Scene.Objects.Select(item => item.Id).SequenceEqual(characterIds));
                Activate(ui.Root, "object-" + characterIds[1]);
                ui.Root.Q<Vector3Field>("Position").value = new Vector3(2, 0, 0);
                ui.SaveTo(missingPath);
                var saved = MmdSceneDocument.Read(missingPath);
                var savedStage = saved.sceneObjects.FirstOrDefault(item => item.id == stageEntry.id);
                Check("save_keeps_unresolved_entries_verbatim", saved.sceneObjects.Select(item => item.id).SequenceEqual(original.sceneObjects.Select(item => item.id)) &&
                    savedStage != null && JsonUtility.ToJson(savedStage) == JsonUtility.ToJson(stageEntry) &&
                    saved.sceneObjects.First(item => item.id == firstEntry.id).motionPath == firstEntry.motionPath &&
                    saved.sceneObjects.First(item => item.id == characterIds[1]).position == new Vector3(2, 0, 0));
                var movedDirectory = Path.Combine(project, "moved"); Directory.CreateDirectory(movedDirectory);
                var moved = Path.Combine(movedDirectory, "moved.mmdscene");
                ui.SaveTo(moved);
                var relocated = MmdSceneDocument.Read(moved);
                var movedStage = relocated.sceneObjects.First(item => item.id == stageEntry.id);
                var movedFirst = relocated.sceneObjects.First(item => item.id == firstEntry.id);
                Check("save_elsewhere_rebases_unresolved_paths", movedStage.assetPath != stageEntry.assetPath &&
                    string.Equals(MmdSceneDocument.Resolve(moved, movedStage.assetPath), MmdSceneDocument.Resolve(missingPath, stageEntry.assetPath), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(MmdSceneDocument.Resolve(moved, movedFirst.motionPath), MmdSceneDocument.Resolve(missingPath, firstEntry.motionPath), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(project, "missing.mmdscene"));
            try
            {
                var reopened = ui.DialogVisible && ui.LastLoadProblems.Length == 2 && ui.UnresolvedCount == 1 && ui.UnresolvedMotionCount == 1 &&
                    ui.Scene.Objects.Select(item => item.Id).SequenceEqual(characterIds);
                Activate(ui.Root, "NoticeOk");
                Check("reopen_keeps_ids_and_unresolved_entries", reopened);
                Activate(ui.Root, "unresolved-" + stageEntry.id);
                var inspected = ui.Root.Q<Label>("SelectionTitle").text == "누락된 배경" && ui.Root.Q("MissingSection").style.display.value != DisplayStyle.None &&
                    ui.Root.Q<Label>("MissingInfo").text.Contains(stageEntry.assetPath) && !ui.GizmoVisible;
                if (!inspected) throw new InvalidOperationException("unresolved_entry_inspector");
                // Visual evidence of an unresolved entry (not an extra assertion).
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-missing.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            // H1-02: relink the missing background to the real PMX; same ID/placement, undoable.
            {
                var missingButton = ui.Root.Q<Button>("MissingRelink");
                if (missingButton == null || !missingButton.enabledInHierarchy) { Finish(path, report, "MissingRelink button unavailable"); yield break; }
            }
            yield return ui.RelinkMissing(stageEntry.id, stageModel);
            try
            {
                var relinked = ui.Scene.Objects.FirstOrDefault(item => item.Id == stageEntry.id);
                var linked = relinked != null && relinked.Kind == MmdSceneService.ObjectKind.Stage && ui.Error.Length == 0 && ui.UnresolvedCount == 0 &&
                    ui.UnresolvedMotionCount == 1 && relinked.Placement.localPosition == stageEntry.position && relinked.Placement.localScale == stageEntry.scale &&
                    relinked.Visible == stageEntry.visible && ui.SelectedId == stageEntry.id && ui.Dirty &&
                    string.Equals(relinked.ModelSource, Path.GetFullPath(stageModel), StringComparison.OrdinalIgnoreCase) && ui.UndoLabel == "파일 다시 연결";
                UndoKey();
                var undone = ui.UnresolvedCount == 1 && ui.Scene.Objects.All(item => item.Id != stageEntry.id) && !ui.Dirty;
                RedoKey();
                var redone = ui.UnresolvedCount == 0 && ui.Scene.Objects.Any(item => item.Id == stageEntry.id);
                UndoKey(); // back to the missing state for the delete check below
                Check("relink_missing_pmx_keeps_id_and_placement_and_undoes", linked && undone && redone && ui.UnresolvedCount == 1);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            try
            {
                var inspected = true;
                Activate(ui.Root, "MissingRemove");
                if (!ui.ConfirmVisible) throw new InvalidOperationException("Unresolved delete did not ask for confirmation");
                Activate(ui.Root, "ConfirmRemove");
                var removed = ui.UnresolvedCount == 0 && ui.Dirty && ui.Root.Q<Button>("unresolved-" + stageEntry.id) == null;
                ui.SaveTo(Path.Combine(project, "missing.mmdscene"));
                var afterDelete = MmdSceneDocument.Read(Path.Combine(project, "missing.mmdscene"));
                Check("explicit_delete_removes_only_that_unresolved_entry", inspected && removed && afterDelete.sceneObjects.Count == 2 &&
                    afterDelete.sceneObjects.All(item => item.id != stageEntry.id) && afterDelete.sceneObjects.First(item => item.id == firstEntry.id).motionPath == firstEntry.motionPath);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(sceneFile);
            try
            {
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                if (a == null || b == null || stage == null || ui.DialogVisible) throw new InvalidOperationException("clean reopen failed");
                ids = ui.Scene.Objects.Select(item => item.Id).ToArray();
                File.WriteAllText(Path.Combine(project, "corrupt.mmdscene"), "{ not json");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // H1-07: recent scenes — newest first, missing files marked and removable, list persisted
            // as paths only, capped at 10, and a row reopens its scene.
            var recentFile = Path.Combine(Path.GetDirectoryName(path), "recent.json");
            bool recentOk = false;
            try
            {
                bool Same(string x, string y) => string.Equals(Path.GetFullPath(x), Path.GetFullPath(y), StringComparison.OrdinalIgnoreCase);
                var tracked = ui.RecentScenes.Count > 0 && Same(ui.RecentScenes[0], sceneFile) &&
                    ui.RecentScenes.Any(item => Same(item, Path.Combine(project, "missing.mmdscene"))) &&
                    ui.RecentScenes.Select(item => Path.GetFullPath(item).ToLowerInvariant()).Distinct().Count() == ui.RecentScenes.Count;
                if (File.Exists(recentFile)) File.Delete(recentFile);
                ui.RecentFilePath = recentFile;
                for (var i = 0; i < 12; i++) ui.RememberRecent(Path.Combine(project, "fake-" + i + ".mmdscene"));
                var capped = ui.RecentScenes.Count == 10 && ui.RecentScenes[0].EndsWith("fake-11.mmdscene");
                ui.ForgetMissingRecent();
                var ghost = Path.Combine(project, "ghost.mmdscene");
                ui.RememberRecent(ghost); ui.RememberRecent(sceneFile);
                var stored = File.ReadAllText(recentFile);
                var pathsOnly = stored.Contains("scene.mmdscene") && !stored.Contains("sceneObjects");
                Activate(ui.Root, "RecentScenes");
                var open0 = ui.Root.Q<Button>("RecentOpen0"); var open1 = ui.Root.Q<Button>("RecentOpen1");
                var shown = ui.DialogVisible && open0 != null && open0.text.StartsWith("scene.mmdscene") && open0.enabledSelf &&
                    open1 != null && open1.text.Contains("(파일 없음)") && !open1.enabledSelf && ui.Root.Q<Button>("RecentForgetMissing").enabledSelf;
                Activate(ui.Root, "RecentRemove1");
                var removed = ui.DialogVisible && ui.RecentScenes.Count == 1 && ui.Root.Q<Button>("RecentOpen1") == null;
                ui.LoadRecent();
                var persisted = ui.RecentScenes.Count == 1 && Same(ui.RecentScenes[0], sceneFile);
                ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, cancelKey: true);
                var closed = !ui.DialogVisible;
                ui.RememberRecent(ghost); ui.RememberRecent(sceneFile);
                Activate(ui.Root, "RecentScenes");
                Activate(ui.Root, "RecentForgetMissing");
                var tidied = ui.RecentScenes.Count == 1 && ui.DialogVisible && !ui.Root.Q<Button>("RecentForgetMissing").enabledSelf;
                recentOk = tracked && capped && pathsOnly && shown && removed && persisted && closed && tidied;
                if (!recentOk) throw new InvalidOperationException("recent scenes: tracked=" + tracked + " capped=" + capped + " pathsOnly=" + pathsOnly +
                    " shown=" + shown + " removed=" + removed + " persisted=" + persisted + " closed=" + closed + " tidied=" + tidied);
                Activate(ui.Root, "RecentOpen0");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                Check("recent_scenes_listed_marked_removed_and_reopened", recentOk && !ui.DialogVisible && ui.Error.Length == 0 &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(sceneFile), StringComparison.OrdinalIgnoreCase) &&
                    ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids) && a != null && b != null && stage != null);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            var diagnosticsOk = false; var diagnosticsDetail = "";
            // H1-08: diagnostics dialog — version/backend/OS/GPU/recent warnings, user folder masked,
            // exported text equals what was shown, log folder button present (not pressed: it opens Explorer).
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Debug.LogWarning("diagnostics probe marker " + Path.Combine(profile, "marker.pmx"));
                Activate(ui.Root, "Diagnostics");
                var body = ui.Root.Q<Label>("DiagnosticsBody");
                var text = body?.text ?? "";
#if ENABLE_IL2CPP
                var backendShown = text.Contains("IL2CPP");
#else
                var backendShown = text.Contains("Mono");
#endif
                var shown = ui.DialogVisible && body != null && body.selection.isSelectable && backendShown && text.Contains(SystemInfo.operatingSystem) &&
                    text.Contains(SystemInfo.graphicsDeviceName) && text.Contains("diagnostics probe marker %USERPROFILE%") &&
                    text.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0 && !text.Contains(Environment.MachineName + " ") &&
                    text.Contains("캐릭터 2 · 배경 1") && text.Contains("scene.mmdscene") && text.Contains("로그 파일:") &&
                    ui.Root.Q<Button>("DiagnosticsExport") != null && ui.Root.Q<Button>("DiagnosticsOpenLog") != null;
                var exported = Path.Combine(Path.GetDirectoryName(path), "diagnostics.txt");
                if (File.Exists(exported)) File.Delete(exported);
                var wrote = ui.ExportDiagnostics(exported) && File.ReadAllText(exported).Replace("\r\n", "\n") == text.Replace("\r\n", "\n");
                diagnosticsOk = shown && wrote;
                diagnosticsDetail = diagnosticsOk ? "" : " [shown=" + shown + " wrote=" + wrote + "]";
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-diagnostics.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return null; yield return null; // the screenshot is taken at the end of the frame
            try
            {
                ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, cancelKey: true);
                Check("diagnostics_shown_masked_and_exported" + diagnosticsDetail, diagnosticsOk && !ui.DialogVisible && ui.Error.Length == 0);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);

            // H1-04: the input router on its own, against a recording fake (no UI, scene or camera).
            try
            {
                var fake = new FakeViewportTarget(); var router = new ViewportInputRouter();
                var last = Vector2.zero;
                // One frame at `at`; the delta is the movement since the previous frame.
                ViewportInputFrame F(Vector2 at, bool left = false, bool right = false, bool pan = false, bool cancel = false, bool undo = false, bool focusLost = false)
                {
                    var frame = new ViewportInputFrame { Screen = at, Delta = at - last, LeftHeld = left, DollyHeld = right, PanHeld = pan, CancelKey = cancel, UndoKey = undo, FocusLost = focusLost };
                    last = at; return frame;
                }
                void Run(params ViewportInputFrame[] frames) { foreach (var frame in frames) router.Route(frame, fake); }
                var p = new Vector2(100, 100);
                // A click within the slop picks once and moves nothing.
                last = p; Run(F(p, left: true), F(p + new Vector2(3, 2), left: true), F(p + new Vector2(3, 2)));
                var clicked = fake.Log.Count(entry => entry.StartsWith("click")) == 1 && !fake.Log.Any(entry => entry.StartsWith("orbit"));
                // A left drag beyond the slop orbits (catching up the held-back 6 px), and its release does not pick.
                fake.Log.Clear(); last = p;
                Run(F(p, left: true), F(p + new Vector2(2, 0), left: true), F(p + new Vector2(6, 0), left: true), F(p + new Vector2(9, 0), left: true), F(p + new Vector2(9, 0)));
                var leftOrbit = fake.Log.SequenceEqual(new[] { "orbit 6,0", "orbit 3,0" });
                // Right drag dollies on its vertical movement only (horizontal is ignored); Shift+right/middle pans.
                fake.Log.Clear(); last = p;
                Run(F(p, right: true), F(p + new Vector2(20, 0), right: true), F(p + new Vector2(20, 15), right: true), F(p + new Vector2(20, 15)));
                var dolly = fake.Log.SequenceEqual(new[] { "dolly 15" });
                fake.Log.Clear(); last = p;
                Run(F(p, pan: true), F(p + new Vector2(4, 4), pan: true), F(p + new Vector2(4, 4)));
                var panned = fake.Log.SequenceEqual(new[] { "pan" });
                // Press and release without movement: no camera call at all (a camera VMD stays).
                fake.Log.Clear(); last = p;
                Run(F(p, right: true), F(p, right: true), F(p), F(p, pan: true), F(p, pan: true), F(p));
                var still = fake.Log.Count == 0;
                // A left press on a gizmo handle drags the gizmo, never the camera.
                fake.Log.Clear(); last = p; fake.GizmoUnderPointer = true;
                Run(F(p, left: true), F(p + new Vector2(20, 0), left: true), F(p + new Vector2(20, 0)));
                var gizmo = fake.Log.SequenceEqual(new[] { "gizmoBegin", "gizmoDrag", "gizmoEnd" });
                fake.GizmoUnderPointer = false;
                // Only one button drives: a right press during a left orbit is ignored.
                fake.Log.Clear(); last = p;
                Run(F(p, left: true), F(p + new Vector2(10, 0), left: true), F(p + new Vector2(10, 10), left: true, right: true), F(p + new Vector2(10, 10)));
                var oneButton = !fake.Log.Any(entry => entry.StartsWith("dolly"));
                // Focus loss ends a drag; the still-held button does not resume it on return.
                fake.Log.Clear(); last = p;
                Run(F(p, left: true), F(p + new Vector2(10, 0), left: true), F(p + new Vector2(20, 0), left: true, focusLost: true), F(p + new Vector2(30, 0), left: true), F(p + new Vector2(30, 0)));
                var focusEnds = fake.Log.SequenceEqual(new[] { "orbit 10,0" });
                // A started drag continues over panels; a press over a panel starts nothing.
                fake.Log.Clear(); last = p;
                router.Route(F(p, left: true), fake); fake.InViewport = false;
                Run(F(p + new Vector2(10, 0), left: true), F(p + new Vector2(14, 0), left: true), F(p + new Vector2(14, 0)), F(p + new Vector2(14, 0), right: true), F(p + new Vector2(14, 20), right: true), F(p + new Vector2(14, 20)));
                var acrossPanels = fake.Log.SequenceEqual(new[] { "orbit 10,0", "orbit 4,0" });
                fake.InViewport = true;
                // A dialog blocks everything but Esc and ends a drag.
                fake.Log.Clear(); last = p;
                router.Route(F(p, left: true), fake); router.Route(F(p + new Vector2(10, 0), left: true), fake);
                fake.DialogOpen = true;
                Run(F(p + new Vector2(20, 0), left: true, undo: true), F(p + new Vector2(20, 0), left: true, cancel: true));
                fake.DialogOpen = false;
                Run(F(p + new Vector2(30, 0), left: true), F(p + new Vector2(30, 0)));
                var dialogBlocks = fake.Log.SequenceEqual(new[] { "orbit 10,0", "cancelDialog" });
                // Outside the viewport a press starts nothing; keys still work.
                fake.Log.Clear(); fake.InViewport = false; last = new Vector2(5, 5);
                Run(F(new Vector2(5, 5), left: true, undo: true), F(new Vector2(5, 5)));
                var outside = fake.Log.Contains("undo") && !fake.Log.Any(entry => entry.StartsWith("click"));
                fake.InViewport = true;
                var all = clicked && leftOrbit && dolly && panned && still && gizmo && oneButton && focusEnds && acrossPanels && dialogBlocks && outside;
                Check("input_router_rules_without_ui" + (all ? "" : " [click=" + clicked + " leftOrbit=" + leftOrbit + " dolly=" + dolly + " pan=" + panned + " still=" + still +
                    " gizmo=" + gizmo + " oneButton=" + oneButton + " focus=" + focusEnds + " panels=" + acrossPanels + " dialog=" + dialogBlocks + " outside=" + outside + "]"), all);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // H1-04: the toolbar's file buttons through the dialog seam (scripted answers instead of
            // the native dialog): save as, cancelled open, open.
            var dialogs = new ScriptedDialogs(); var nativeDialogs = ui.FileDialogs; ui.FileDialogs = dialogs;
            var saveAsCopy = Path.Combine(project, "save-as-copy.mmdscene");
            bool savedAs = false, openCancelled = false;
            try
            {
                if (File.Exists(saveAsCopy)) File.Delete(saveAsCopy);
                dialogs.Next = saveAsCopy; Activate(ui.Root, "SaveSceneAs");
                savedAs = string.Equals(ui.ProjectPath, Path.GetFullPath(saveAsCopy), StringComparison.OrdinalIgnoreCase) && File.Exists(saveAsCopy) &&
                    !ui.Dirty && dialogs.Asked == "SaveScene:scene.mmdscene";
                dialogs.Next = null; Activate(ui.Root, "OpenScene");
                openCancelled = dialogs.Asked == "OpenScene" && !ui.Busy && ui.Error.Length == 0 &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(saveAsCopy), StringComparison.OrdinalIgnoreCase);
                dialogs.Next = sceneFile; Activate(ui.Root, "OpenScene");
            }
            catch (Exception ex) { ui.FileDialogs = nativeDialogs; Finish(path, report, ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                ui.FileDialogs = nativeDialogs;
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                Check("toolbar_file_buttons_use_dialog_seam" + (savedAs && openCancelled ? "" : " [savedAs=" + savedAs + " cancelled=" + openCancelled + "]"),
                    savedAs && openCancelled && ui.Error.Length == 0 && a != null && b != null && stage != null &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(sceneFile), StringComparison.OrdinalIgnoreCase) &&
                    ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // H1-01c: cancel a scene open during its second entry; the running scene must be untouched.
            yield return null; // let owners released by the previous open finish their deferred Destroy
            var beforeObjects = ui.Scene.Objects.ToArray(); var beforePath = ui.ProjectPath;
            var lifetimesBefore = UnityEngine.Object.FindObjectsByType<PocAssetLifetime>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            // Lifetimes not owned by a scene object (diagnostic for leaked import containers).
            report.orphanLifetimes = UnityEngine.Object.FindObjectsByType<PocAssetLifetime>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(owner => ui.Scene.Objects.All(item => item.Owner != owner))
                .Select(owner => owner.name + " (parent " + (owner.transform.parent == null ? "none" : owner.transform.parent.name) + ", active " + owner.gameObject.activeInHierarchy + ")").ToArray();
            ui.StartCoroutine(ui.OpenFrom(sceneFile));
            var waited = 0f;
            while (!(ui.Busy && ui.Root.Q<Label>("LoadingLabel").text.Contains("(2/")) && waited < 60) { waited += Time.unscaledDeltaTime; yield return null; }
            try
            {
                if (!ui.Busy || !ui.Root.Q<Button>("CancelLoading").enabledInHierarchy) throw new InvalidOperationException("scene open did not reach its second entry with a cancel button");
                Activate(ui.Root, "CancelLoading");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            while (ui.Busy) yield return null;
            yield return null; // Destroy is deferred to the end of the frame
            try
            {
                var sameObjects = ui.Scene.Objects.SequenceEqual(beforeObjects); var samePath = ui.ProjectPath == beforePath;
                var lifetimes = UnityEngine.Object.FindObjectsByType<PocAssetLifetime>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var statusText = ui.Root.Q<Label>("Status").text; var cardHidden = ui.Root.Q("LoadingCard").style.display.value == DisplayStyle.None;
                var cancelOk = sameObjects && samePath && ui.Error.Length == 0 && ui.Scene.CandidateCount == 0 && lifetimes == lifetimesBefore &&
                    statusText.Contains("취소") && cardHidden;
                Check("scene_open_cancel_keeps_running_scene_and_releases_candidates" + (cancelOk ? "" : " [sameObjects=" + sameObjects + " samePath=" + samePath +
                    " error='" + ui.Error + "' candidates=" + ui.Scene.CandidateCount + " lifetimes=" + lifetimes + " before=" + lifetimesBefore + " objects=" + ui.Scene.Objects.Count +
                    " status='" + statusText + "' cardHidden=" + cardHidden + "]"), cancelOk);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            // Full asynchronous open after the cancel: replaces the scene in one step.
            ui.StartCoroutine(ui.OpenFrom(sceneFile));
            var openLog = new List<(float ms, string stage)>();
            var loadingLabel = ui.Root.Q<Label>("LoadingLabel");
            var frameStart = Time.realtimeSinceStartup;
            do
            {
                var stageBefore = ui.Busy ? loadingLabel.text : "(idle)";
                yield return null;
                var now = Time.realtimeSinceStartup; var ms = (now - frameStart) * 1000; frameStart = now;
                var stageAfter = ui.Busy ? loadingLabel.text : "(committed)";
                openLog.Add((ms, stageBefore == stageAfter ? stageAfter : stageBefore + " -> " + stageAfter));
                report.sceneOpenWorstFrameMs = Mathf.Max(report.sceneOpenWorstFrameMs, ms); report.sceneOpenFrames++;
            }
            while (ui.Busy);
            report.sceneOpenSlowestFrames = openLog.OrderByDescending(entry => entry.ms).Take(8)
                .Select(entry => entry.ms.ToString("0.0") + " ms: " + entry.stage).ToArray();
            yield return null; // the replaced scene's owners are destroyed at the end of the frame
            try
            {
                report.sceneOpenSeconds = ui.SceneOpenSeconds;
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                Check("async_scene_open_replaces_scene_in_one_step", a != null && b != null && stage != null && ui.Error.Length == 0 && !ui.DialogVisible &&
                    ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids) && ui.Scene.Objects.All(item => !beforeObjects.Contains(item)) &&
                    ui.Scene.CandidateCount == 0 && a.Motion != null && b.Motion != null && ui.Scene.Duration == duration &&
                    UnityEngine.Object.FindObjectsByType<PocAssetLifetime>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == lifetimesBefore &&
                    a.Model.root.GetComponentsInChildren<Renderer>().All(renderer => renderer.enabled));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // PH-03: experimental per-character physics (bake cache) through the inspector toggle.
            var physicsFile = Path.Combine(project, "physics.mmdscene");
            Vector3[] offPose = null, onPose = null;
            try
            {
                Activate(ui.Root, "object-" + stage.Id);
                var stageBlocked = !ui.Root.Q<Toggle>("Physics").enabledSelf;
                Activate(ui.Root, "object-" + a.Id);
                ui.Scene.Seek(2); offPose = a.Motion.PosePositions();
                var enabledForCharacter = ui.Root.Q<Toggle>("Physics").enabledSelf;
                ui.Root.Q<Toggle>("Physics").value = true;
                Check("physics_toggle_only_for_characters_with_motion", stageBlocked && enabledForCharacter && a.PhysicsEnabled && !b.PhysicsEnabled && ui.Dirty);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            for (var i = 0; i < 3000 && !a.PhysicsReady; i++) { ui.Scene.BakePhysicsSlice(); report.physicsBakeFrames++; yield return null; }
            try
            {
                ui.RefreshPhysicsInfo();
                ui.Scene.Seek(2); onPose = a.Motion.PosePositions();
                ui.Scene.Seek(.5); ui.Scene.Seek(2);
                var again = a.Motion.PosePositions();
                report.physicsPoseDifferenceM = offPose.Zip(onPose, (x, y) => Vector3.Distance(x, y)).Max();
                Check("physics_bake_completes_changes_pose_and_repeats", a.PhysicsReady && ui.Root.Q<Label>("PhysicsInfo").text.Contains("준비 완료") &&
                    report.physicsPoseDifferenceM > 1e-3f && onPose.Zip(again, (x, y) => Vector3.Distance(x, y)).Max() == 0);
                // PH-04: every part at 0 % shows the physics-OFF pose; back at 100 % the baked pose.
                ui.RefreshPhysicsInfo();
                var parts = a.PhysicsParts;
                foreach (var part in parts) ui.Root.Q<Slider>("PhysicsStrength-" + part).value = 0;
                ui.Scene.Seek(2); var zero = a.Motion.PosePositions();
                foreach (var part in parts) ui.Root.Q<Slider>("PhysicsStrength-" + part).value = 100;
                ui.Scene.Seek(2); var full = a.Motion.PosePositions();
                report.physicsStrengthZeroDifferenceM = offPose.Zip(zero, (x, y) => Vector3.Distance(x, y)).Max();
                Check("physics_part_strength_blends_without_rebake", parts.Length > 0 && a.PhysicsReady && report.physicsStrengthZeroDifferenceM < 1e-3f &&
                    onPose.Zip(full, (x, y) => Vector3.Distance(x, y)).Max() == 0);
                ui.Scene.SetPlacement(a.Id, a.Placement.localPosition + new Vector3(.1f, 0, 0), a.Placement.localEulerAngles, a.Placement.localScale);
                if (a.PhysicsReady) throw new InvalidOperationException("placement change did not invalidate the physics bake");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            for (var i = 0; i < 3000 && !a.PhysicsReady; i++) { ui.Scene.BakePhysicsSlice(); yield return null; }
            try
            {
                Check("physics_rebakes_after_placement_change", a.PhysicsReady);
                if (!ui.SaveTo(physicsFile)) throw new InvalidOperationException("physics scene save failed: " + ui.Error);
                var saved = MmdSceneDocument.Read(physicsFile);
                if (!saved.sceneObjects.First(o => o.id == a.Id).physics.enabled || saved.sceneObjects.First(o => o.id == b.Id).physics.enabled)
                    throw new InvalidOperationException("physics flag not saved per character");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(physicsFile);
            try
            {
                var a2 = Find(ui, MmdSceneService.ObjectKind.Character, aModel); var b2 = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                Activate(ui.Root, "object-" + a2.Id);
                var restored = a2.PhysicsEnabled && !b2.PhysicsEnabled && ui.Root.Q<Toggle>("Physics").value;
                ui.Root.Q<Toggle>("Physics").value = false;
                Check("physics_flag_saved_reopened_and_turned_off", restored && !a2.PhysicsEnabled && !a2.PhysicsReady && ui.Error.Length == 0);
                if (!ui.SaveTo(physicsFile)) throw new InvalidOperationException("physics scene save failed: " + ui.Error); // no unsaved prompt on reopen
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            // PH-05: chest/hip spring bones. Neither test model has chest/hip bones without rigid
            // bodies (Gene has none, Sandrone's have rigid bodies), so the probe forces Sandrone's
            // rigid-body chest roots to take a spring; its physics stays off here.
            MmdSceneService.SceneObject springTarget = null; Vector3[] springOff = null;
            try
            {
                springTarget = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                var gene = Find(ui, MmdSceneService.ObjectKind.Character, aModel);
                Activate(ui.Root, "object-" + gene.Id);
                var geneBlocked = !ui.Root.Q<DropdownField>("SpringPreset").enabledSelf && ui.Root.Q<Label>("SpringInfo").text.Contains("없어");
                springTarget.SpringForceForTest = true;
                Activate(ui.Root, "object-" + springTarget.Id);
                ui.Scene.Seek(2); springOff = springTarget.Motion.PosePositions();
                ui.Root.Q<DropdownField>("SpringPreset").value = "중";
                Check("spring_offered_only_where_bones_allow", geneBlocked && springTarget.SpringPreset == "medium" && ui.Dirty);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            for (var i = 0; i < 3000 && !springTarget.SpringReady; i++) { ui.Scene.BakePhysicsSlice(); report.springBakeFrames++; yield return null; }
            try
            {
                ui.Scene.Seek(2); var springOn = springTarget.Motion.PosePositions();
                ui.Scene.Seek(.5); ui.Scene.Seek(2); var springAgain = springTarget.Motion.PosePositions();
                report.springPoseDifferenceM = springOff.Zip(springOn, (x, y) => Vector3.Distance(x, y)).Max();
                ui.RefreshPhysicsInfo();
                ui.Root.Q<Slider>("PhysicsStrength-chest").value = 0;
                ui.Scene.Seek(2); var springZero = springTarget.Motion.PosePositions();
                ui.Root.Q<Slider>("PhysicsStrength-chest").value = 100;
                report.springZeroDifferenceM = springOff.Zip(springZero, (x, y) => Vector3.Distance(x, y)).Max();
                Check("spring_bake_moves_chest_repeatably_and_blends", springTarget.SpringReady && ui.Root.Q<Label>("SpringInfo").text.Contains("준비 완료") &&
                    report.springPoseDifferenceM > 1e-4f && springOn.Zip(springAgain, (x, y) => Vector3.Distance(x, y)).Max() == 0 && report.springZeroDifferenceM < 1e-4f);
                if (!ui.SaveTo(physicsFile)) throw new InvalidOperationException("spring scene save failed: " + ui.Error);
                var saved = MmdSceneDocument.Read(physicsFile);
                Check("spring_preset_saved_per_character", saved.sceneObjects.First(o => o.id == springTarget.Id).physics.spring == "medium" &&
                    saved.sceneObjects.Where(o => o.id != springTarget.Id).All(o => string.IsNullOrEmpty(o.physics.spring)));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            // Back to the main scene file for the remaining checks.
            yield return ui.OpenFrom(sceneFile);
            try
            {
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                if (ui.Error.Length > 0 || a == null || a.PhysicsEnabled) throw new InvalidOperationException("main scene reopen failed: " + ui.Error);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }

            // H1-03: undo/redo through Ctrl+Z / Ctrl+Y (viewport shortcuts).
            void UndoKey() => ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, undoKey: true);
            void RedoKey() => ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, redoKey: true);
            MmdPocMotion motionBefore = null; string motionSourceBefore = null;
            try
            {
                var startPosition = a.Placement.localPosition;
                Activate(ui.Root, "object-" + a.Id);
                ui.Root.Q<Vector3Field>("Position").value = startPosition + new Vector3(1, 0, 0);
                Activate(ui.Root, "object-" + stage.Id);
                ui.Root.Q<Toggle>("Visible").value = false;
                var recorded = ui.UndoCount == 2 && ui.Dirty;
                UndoKey();
                var visibleBack = stage.Visible && a.Placement.localPosition == startPosition + new Vector3(1, 0, 0);
                UndoKey();
                var allBack = a.Placement.localPosition == startPosition && !ui.Dirty && ui.RedoCount == 2;
                RedoKey(); RedoKey();
                var redone = !stage.Visible && a.Placement.localPosition == startPosition + new Vector3(1, 0, 0) && ui.Dirty;
                UndoKey(); UndoKey();
                Check("undo_redo_placement_and_visibility_back_to_saved", recorded && visibleBack && allBack && redone &&
                    stage.Visible && a.Placement.localPosition == startPosition && !ui.Dirty);

                Activate(ui.Root, "object-" + stage.Id);
                Activate(ui.Root, "RemoveModel");
                Activate(ui.Root, "ConfirmRemove");
                var removedNow = ui.Scene.Objects.Count == 2 && !ui.Scene.Objects.Contains(stage);
                UndoKey();
                var restored = ui.Scene.Objects.Count == 3 && ui.Scene.Objects.Contains(stage) && stage.Model.root != null &&
                    stage.Model.root.GetComponentsInChildren<Renderer>().All(r => r.enabled) && ui.SelectedId == stage.Id && !ui.Dirty;
                RedoKey();
                var removedAgain = !ui.Scene.Objects.Contains(stage);
                UndoKey();
                Check("undo_remove_restores_the_same_object_instantly", removedNow && restored && removedAgain && ui.Scene.Objects.Contains(stage) && !ui.Dirty);

                Activate(ui.Root, "object-" + a.Id);
                motionBefore = a.Motion; motionSourceBefore = a.MotionSource;
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(bMotion, false);
            try
            {
                var connected = a.Motion != motionBefore && a.MotionSource == Path.GetFullPath(bMotion) && ui.UndoLabel == "VMD 연결";
                UndoKey();
                var previousBack = a.Motion == motionBefore && a.MotionSource == motionSourceBefore && a.Motion.enabled && !ui.Dirty;
                RedoKey();
                var nextBack = a.MotionSource == Path.GetFullPath(bMotion);
                UndoKey();
                Check("undo_vmd_connection_restores_previous_motion", connected && previousBack && nextBack && a.Motion == motionBefore && !ui.Dirty);

                // History limit: 204 loop toggles keep only the last 200 commands.
                var loopBefore = ui.Scene.Loop;
                for (var i = 0; i < MmdSceneWorkspace.UndoLimit + 4; i++) ui.Root.Q<Toggle>("Loop").value = !ui.Scene.Loop;
                var capped = ui.UndoCount == MmdSceneWorkspace.UndoLimit;
                for (var i = 0; i < MmdSceneWorkspace.UndoLimit; i++) UndoKey();
                Check("undo_history_keeps_last_200_commands", capped && ui.UndoCount == 0 && ui.RedoCount == MmdSceneWorkspace.UndoLimit &&
                    ui.Scene.Loop == loopBefore && ui.Dirty); // the saved state fell off the history, so it stays unsaved
                if (!ui.SaveTo(sceneFile)) throw new InvalidOperationException("save after undo checks failed: " + ui.Error);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(project, "corrupt.mmdscene"));
            try
            {
                Check("corrupt_file_rejected_and_scene_kept", ui.Error.Contains("열 수 없습니다") && ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids) &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(sceneFile), StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(Path.Combine(project, "future.mmdscene"), File.ReadAllText(sceneFile).Replace("\"schemaVersion\": " + SceneFile.CurrentSchemaVersion, "\"schemaVersion\": 99"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(project, "future.mmdscene"));
            try
            {
                Check("unsupported_schema_version_rejected_and_scene_kept", ui.Error.Contains("버전") && ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids));
                ui.Root.Q<Slider>("Exposure").value = .5f;
                var dirty = ui.Dirty;
                Key(ui, save: true);
                Check("ctrl_s_saves_to_current_file", dirty && !ui.Dirty && ui.Error.Length == 0 && Near(MmdSceneDocument.Read(sceneFile).graphics.exposure, .5f));
                ui.Root.Q<Slider>("Exposure").value = 0;
                var blocked = !ui.HandleQuitRequest() && ui.DialogVisible;
                Activate(ui.Root, "UnsavedCancel");
                Key(ui, save: true);
                Check("quit_with_unsaved_changes_asks_then_allows_when_saved", blocked && !ui.DialogVisible && !ui.Dirty && ui.HandleQuitRequest());
                ui.Root.Q<Toggle>("Loop").value = !ui.Scene.Loop; ui.Root.Q<Toggle>("Loop").value = !ui.Scene.Loop;
                Activate(ui.Root, "NewScene");
                var newAsked = ui.DialogVisible;
                Activate(ui.Root, "UnsavedCancel");
                Check("new_scene_prompt_cancel_keeps_scene", newAsked && ui.Scene.Objects.Count == 3 && ui.Dirty);
                Key(ui, save: true);
                Activate(ui.Root, "object-" + a.Id);
                ui.Root.Q<Slider>("Timeline").value = .25f;
                Activate(ui.Root, "FrameAll");
                report.screenshot = Path.Combine(Path.GetDirectoryName(path), "scene-ui.png");
                ScreenCapture.CaptureScreenshot(report.screenshot);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            try
            {
                Check("application_ui_screenshot_created", File.Exists(report.screenshot));
                Key(ui, gizmo: 2);
                // Visual evidence of the rotate gizmo (not an extra assertion).
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-gizmo.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            try
            {
                Key(ui, gizmo: 1);
                Activate(ui.Root, "explorer-light");
                // Visual evidence of the light/graphics inspector (not an extra assertion).
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-graphics.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            try
            {
                Activate(ui.Root, "object-" + stage.Id);
                Key(ui, delete: true);
                // Visual evidence of the confirmation dialog (not an extra assertion).
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-confirm.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            try
            {
                Activate(ui.Root, "ConfirmRemove");
                Check("stage_delete_keeps_characters_and_motions", ui.Scene.Objects.Count == 2 && ui.Scene.Objects.All(item => item.Kind == MmdSceneService.ObjectKind.Character) &&
                    a.Motion != null && b.Motion != null && ui.Scene.Duration == duration && ui.SelectedId == a.Id && ui.Root.Q<Label>("StageGroup").text.Contains("(0)"));
                RemoveSelected(ui);
                Check("delete_submit_selects_remaining_character", ui.Scene.Objects.Count == 1 && ui.SelectedId == b.Id && ui.Root.Q<Button>("object-"+a.Id) == null);
                Activate(ui.Root, "PlayPause"); ui.Scene.Advance(.1);
                Check("remaining_character_plays_from_ui", b.Motion.TimeSeconds > .75f);
                RemoveSelected(ui);
                Check("last_delete_clears_selection_and_disables_controls", ui.Scene.Objects.Count == 0 && ui.SelectedId == null && !ui.Root.Q<Button>("ConnectMotion").enabledSelf && !ui.Root.Q<Button>("PlayPause").enabledSelf);
                Check("last_delete_hides_selection_highlight", !ui.HighlightVisible);
                Activate(ui.Root, "NewScene");
                var asked = ui.DialogVisible;
                Activate(ui.Root, "UnsavedDiscard");
                Check("new_scene_discard_resets_everything", asked && ui.Scene.Objects.Count == 0 && ui.ProjectPath == null && !ui.Dirty &&
                    JsonUtility.ToJson(ui.Graphics.Settings) == JsonUtility.ToJson(new SceneGraphicsSettings()) && ui.Root.Q<Label>("ProjectTitle").text == "새 장면");
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            string audioError = null;
            yield return AudioChecks(ui, path, sceneFile, error => audioError = error);
            if (audioError != null) { Finish(path, report, audioError); yield break; }
            string cameraError = null;
            yield return CameraMotionChecks(ui, path, sceneFile, error => cameraError = error);
            if (cameraError != null) { Finish(path, report, cameraError); yield break; }
            string outputError = null;
            yield return OutputChecks(ui, path, sceneFile, error => outputError = error);
            if (outputError != null) { Finish(path, report, outputError); yield break; }
            string correctionError = null;
            yield return CorrectionOutputChecks(ui, path, error => correctionError = error);
            if (correctionError != null) { Finish(path, report, correctionError); yield break; }
            yield return CorrectionScreenshots(ui, path, error => correctionError = error);
            if (correctionError != null) { Finish(path, report, correctionError); yield break; }
            string autoError = null;
            yield return AutoCameraChecks(ui, path, error => autoError = error);
            if (autoError != null) { Finish(path, report, autoError); yield break; }
            string fitError = null;
            yield return MotionFitChecks(ui, path, error => fitError = error);
            Finish(path, report, fitError);
        }

        // ----- H2a-03 audio -----
        // Synthetic WAVs only (written next to the report, outside the repository). The song is
        // quiet (amplitude 0.1) and plays for about two seconds in total.
        private static float[] Tone(int rate, double seconds)
        {
            var frames = (int)Math.Round(rate * seconds);
            var samples = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                samples[2 * i] = .1f * Mathf.Sin(2 * Mathf.PI * 440 * i / rate);
                samples[2 * i + 1] = .1f * Mathf.Sin(2 * Mathf.PI * 660 * i / rate);
            }
            return samples;
        }
        private IEnumerator AudioChecks(MmdSceneWorkspace ui, string path, string sceneFile, Action<string> fail)
        {
            var dir = Path.Combine(Path.GetDirectoryName(path), "audio");
            var song = Path.Combine(dir, "song.wav");
            const int rate = 44100; const double length = 3.0;
            var dialogs = new ScriptedDialogs(); var native = ui.FileDialogs;
            MmdAudioTrack track = null; AudioSource source = null;
            yield return null; // Destroy is deferred to the end of the frame
            try
            {
                // H2b-01: the new, empty scene holds no toon/PBR materials (released with their objects).
                Check("styled_materials_released_with_their_objects [" + StyledMaterialCount() + " left]", StyledMaterialCount() == 0);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var pattern = new[] { -1f, -.5f, 0, .25f, .5f, .99f };
                bool Roundtrip(string name, int channels, int frequency, int bits, bool extensible, float tolerance)
                {
                    var file = Path.Combine(dir, name);
                    var samples = Enumerable.Range(0, 600 * channels).Select(i => pattern[i % pattern.Length]).ToArray();
                    MmdWavReader.Write(file, samples, channels, frequency, bits, extensible);
                    var data = MmdWavReader.Read(file);
                    return data.Channels == channels && data.Frequency == frequency && data.Frames == 600 && data.BitsPerSample == bits &&
                        data.Samples.Select((value, i) => Math.Abs(value - samples[i])).Max() <= tolerance;
                }
                var formats = new[]
                {
                    Roundtrip("pcm16.wav", 2, 44100, 16, false, 1e-4f), Roundtrip("pcm24.wav", 1, 48000, 24, false, 1e-6f),
                    Roundtrip("pcm8.wav", 1, 22050, 8, false, .01f), Roundtrip("float.wav", 2, 48000, 32, true, 0),
                    Roundtrip("ext16.wav", 2, 96000, 16, true, 1e-4f),
                };
                File.WriteAllText(Path.Combine(dir, "text.wav"), "not a wav file at all");
                var adpcm = Path.Combine(dir, "adpcm.wav");
                MmdWavReader.Write(adpcm, new float[100], 1, 22050);
                var bytes = File.ReadAllBytes(adpcm); bytes[20] = 2; File.WriteAllBytes(adpcm, bytes); // format code 2 = MS ADPCM
                var pcm16 = File.ReadAllBytes(Path.Combine(dir, "pcm16.wav"));
                File.WriteAllBytes(Path.Combine(dir, "truncated.wav"), pcm16.Take(30).ToArray());
                string Rejects(string name) { try { MmdWavReader.Read(Path.Combine(dir, name)); return null; } catch (InvalidDataException ex) { return ex.Message; } }
                var messages = new[] { "text.wav", "adpcm.wav", "truncated.wav" }.Select(Rejects).ToArray();
                // A data chunk shorter than its header says (streamed writer, cut copy) decodes what is there.
                File.WriteAllBytes(Path.Combine(dir, "cut.wav"), pcm16.Take(44 + 400).ToArray());
                var cutFrames = MmdWavReader.Read(Path.Combine(dir, "cut.wav")).Frames;
                Check("audio_wav_decoder_formats_and_rejections" + (formats.All(x => x) && messages.All(m => m != null) ? "" :
                    " [formats=" + string.Join(",", formats) + " messages=" + string.Join(" | ", messages) + "]"),
                    formats.All(x => x) && messages.All(m => m != null) && messages[1].Contains("압축") && cutFrames == 100);

                MmdWavReader.Write(song, Tone(rate, length), 2, rate);
                ui.FileDialogs = dialogs;
                Activate(ui.Root, "explorer-audio");
                if (ui.Target != MmdSceneWorkspace.InspectorTarget.Audio || ui.Root.Q("AudioSection").style.display.value != DisplayStyle.Flex)
                    throw new InvalidOperationException("audio inspector not shown");
                dialogs.Next = song; Activate(ui.Root, "AudioConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                track = ui.Scene.Audio; source = ui.Scene.AudioSourceForTest;
                Check("audio_connected_from_inspector_sets_scene_length", dialogs.Asked == "OpenAudio" && ui.Error.Length == 0 && track != null &&
                    track.Frames == (long)(rate * length) && track.Frequency == rate && track.Channels == 2 && Math.Abs(ui.Scene.Duration - length) < 1e-9 &&
                    ui.Root.Q<Button>("PlayPause").enabledSelf && ui.Root.Q<Button>("explorer-audio").text.Contains("song") &&
                    ui.Root.Q<Label>("AudioInfo").text.Contains("44100 Hz") && ui.Dirty && ui.UndoLabel == "오디오 연결");
                ui.Scene.Seek(1.2345);
                var expected = (long)Math.Round(1.2345 * rate);
                Check("audio_seek_sets_sample_position", source != null && source.clip == track.Clip && source.timeSamples == expected &&
                    ui.Scene.AudioTargetSample == expected && !source.isPlaying);
                ui.Scene.Seek(.5); ui.Scene.AutomaticClock = true; ui.Scene.Playing = true;
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            var drifts = new List<double>(); var output = new float[1024]; var peak = 0f;
            var resyncsBefore = ui.Scene.AudioResyncs; var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 1.2f)
            {
                yield return null;
                if (!source.isPlaying) continue;
                drifts.Add((source.timeSamples - ui.Scene.AudioTargetSample) / (double)rate);
                AudioListener.GetOutputData(output, 0);
                peak = Math.Max(peak, output.Max(Math.Abs));
            }
            ui.Scene.Playing = false; ui.Scene.AutomaticClock = false;
            try
            {
                var maxDrift = drifts.Count == 0 ? 1 : drifts.Max(Math.Abs);
                var resyncs = ui.Scene.AudioResyncs - resyncsBefore;
                // Steady state: after the first 10 frames (start latency settles); signed mean = constant offset.
                var steady = drifts.Skip(10).ToList();
                var steadyMax = steady.Count == 0 ? 1 : steady.Max(Math.Abs); var mean = steady.Count == 0 ? 0 : steady.Average();
                Check("audio_plays_in_sync_with_scene_clock [frames " + drifts.Count + ", max drift " + (maxDrift * 1000).ToString("0.0") + " ms, steady max " +
                    (steadyMax * 1000).ToString("0.0") + " ms, mean " + (mean * 1000).ToString("+0.0;-0.0") + " ms, resyncs " + resyncs +
                    ", peak " + peak.ToString("0.000") + "]", drifts.Count > 20 && maxDrift < MmdSceneService.ResyncSeconds && resyncs <= 1 && peak > .02f &&
                    ui.Scene.Seconds > 1.4 && !source.isPlaying);
                ui.Root.Q<Slider>("Speed").value = 1.5f;
                var shifted = Math.Abs(source.pitch - 1.5f) < 1e-6f && ui.AudioPitchNoticeVisible;
                ui.Root.Q<Slider>("Speed").value = 1;
                Check("speed_change_shifts_audio_pitch_and_is_shown", shifted && Math.Abs(source.pitch - 1) < 1e-6f && !ui.AudioPitchNoticeVisible);
                ui.Root.Q<Toggle>("Loop").value = true;
                ui.Scene.Seek(length - .3); ui.Scene.AutomaticClock = true; ui.Scene.Playing = true;
            }
            catch (Exception ex) { ui.Scene.AutomaticClock = false; ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            started = Time.realtimeSinceStartup;
            var wrapped = false;
            while (Time.realtimeSinceStartup - started < 2 && !wrapped)
            {
                yield return null;
                wrapped = ui.Scene.Seconds > .05 && ui.Scene.Seconds < 1;
            }
            yield return null; yield return null;
            var wrapOk = wrapped && ui.Scene.Playing && source.isPlaying && ui.Scene.Seconds < 1 &&
                Math.Abs(source.timeSamples - ui.Scene.AudioTargetSample) < MmdSceneService.ResyncSeconds * rate;
            ui.Root.Q<Toggle>("Loop").value = false;
            ui.Scene.Seek(length - .2);
            started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 1.5f && ui.Scene.Playing) yield return null;
            yield return null;
            ui.Scene.AutomaticClock = false;
            try
            {
                var stopped = !ui.Scene.Playing && !source.isPlaying && Math.Abs(ui.Scene.Seconds - length) < 1e-9;
                Check("loop_restarts_audio_and_end_stops_it" + (wrapOk && stopped ? "" : " [wrapped=" + wrapOk + " stopped=" + stopped + "]"), wrapOk && stopped);
                ui.Root.Q<Slider>("AudioVolume").value = 40;
                ui.Root.Q<Toggle>("AudioMute").value = true;
                var applied = Math.Abs(source.volume - .4f) < 1e-6f && source.mute && ui.Scene.AudioVolume == .4f;
                ui.Undo(); var unmuted = !source.mute && ui.Scene.AudioVolume == .4f;
                ui.Undo(); var full = ui.Scene.AudioVolume == 1 && Math.Abs(source.volume - 1) < 1e-6f;
                ui.Redo(); ui.Redo();
                Activate(ui.Root, "AudioRemove");
                var removed = ui.Scene.Audio == null && ui.Scene.Duration == 0 && !source.isPlaying && !ui.Root.Q<Button>("PlayPause").enabledSelf &&
                    ui.Root.Q<Button>("explorer-audio").text.Contains("없음");
                ui.Undo(); var back = ui.Scene.Audio == track && !track.Disposed && Math.Abs(ui.Scene.Duration - length) < 1e-9;
                ui.Redo(); var again = ui.Scene.Audio == null;
                ui.Undo();
                Check("audio_volume_mute_and_remove_are_undoable" + (applied && unmuted && full && removed && back && again ? "" :
                    " [applied=" + applied + " unmuted=" + unmuted + " full=" + full + " removed=" + removed + " back=" + back + " again=" + again + "]"),
                    applied && unmuted && full && removed && back && again && ui.Scene.Audio == track && ui.Scene.AudioVolume == .4f && ui.Scene.AudioMuted);
                if (!ui.SaveTo(Path.Combine(dir, "audio.mmdscene"))) throw new InvalidOperationException("audio scene save failed: " + ui.Error);
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            var audioScene = Path.Combine(dir, "audio.mmdscene");
            yield return ui.OpenFrom(audioScene);
            var missingScene = Path.Combine(dir, "missing-audio.mmdscene");
            try
            {
                var stored = MmdSceneDocument.Read(audioScene).audio;
                var reopened = ui.Scene.Audio;
                Check("audio_saved_relative_and_reopened", stored.path == "song.wav" && stored.volume == .4f && stored.muted && reopened != null && reopened != track &&
                    track.Disposed && reopened.Frames == track.Frames && ui.Scene.AudioVolume == .4f && ui.Scene.AudioMuted && !ui.Dirty &&
                    ui.Error.Length == 0 && ui.LastLoadProblems.Length == 0);
                var file = MmdSceneDocument.Read(audioScene);
                file.audio.path = "gone/missing.wav";
                MmdSceneDocument.Write(missingScene, file);
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return ui.OpenFrom(missingScene);
            try
            {
                var listed = ui.DialogVisible && ui.LastLoadProblems.Any(p => p.Contains("missing.wav")) && ui.Scene.Audio == null &&
                    ui.UnresolvedAudioPath == "gone/missing.wav" && ui.Root.Q<Button>("explorer-audio").text.Contains("누락");
                Activate(ui.Root, "NoticeOk");
                var sub = Path.Combine(dir, "sub"); Directory.CreateDirectory(sub);
                if (!ui.SaveTo(Path.Combine(sub, "moved.mmdscene"))) throw new InvalidOperationException("moved save failed: " + ui.Error);
                var kept = MmdSceneDocument.Read(Path.Combine(sub, "moved.mmdscene")).audio.path == "../gone/missing.wav";
                if (!listed || !kept) throw new InvalidOperationException("missing_audio_kept_as_reference [listed=" + listed + " kept=" + kept + " problems=" + string.Join(" | ", ui.LastLoadProblems) + "]");
                Activate(ui.Root, "explorer-audio");
                dialogs.Next = song; Activate(ui.Root, "AudioConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                var replaced = ui.Scene.Audio != null && ui.UnresolvedAudioPath == null;
                ui.Undo(); var restoredMissing = ui.Scene.Audio == null && ui.UnresolvedAudioPath == "../gone/missing.wav";
                ui.Redo();
                Check("missing_audio_kept_as_reference_and_replaceable", replaced && restoredMissing && ui.Scene.Audio != null);
                dialogs.Next = Path.Combine(dir, "adpcm.wav"); Activate(ui.Root, "AudioConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            var current = ui.Scene.Audio;
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                Check("unsupported_wav_rejected_and_current_audio_kept", ui.Error.Contains("압축") && ui.Scene.Audio == current && !current.Disposed);
                if (!ui.SaveTo(Path.Combine(dir, "sub", "moved.mmdscene"))) throw new InvalidOperationException("save before reopen failed: " + ui.Error);
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            // The main scene gets the song for the relaunch check (PocSceneRestoreProbe).
            yield return ui.OpenFrom(sceneFile);
            var motionDuration = ui.Scene.Duration;
            try
            {
                Activate(ui.Root, "explorer-audio");
                dialogs.Next = song; Activate(ui.Root, "AudioConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                ui.FileDialogs = native;
                ui.Root.Q<Slider>("AudioVolume").value = 80;
                if (!ui.SaveTo(sceneFile)) throw new InvalidOperationException("scene save with audio failed: " + ui.Error);
                var saved = MmdSceneDocument.Read(sceneFile);
                Check("scene_with_audio_saved_for_relaunch", saved.audio.path == "../audio/song.wav" && saved.audio.volume == .8f && !saved.audio.muted &&
                    saved.sceneObjects.Count == 3 && Math.Abs(ui.Scene.Duration - Math.Max(motionDuration, length)) < 1e-9);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }

        // ----- H2a-04 camera VMD -----
        private static MmdCameraKey CameraKey(uint frame, Vector3 target, Vector3 rotation, float distance, float angle, bool orthographic = false, Vector4? y = null)
        {
            var key = new MmdCameraKey { Frame = frame, Target = target, Rotation = rotation, Distance = distance, ViewAngle = angle, Orthographic = orthographic };
            for (var c = 0; c < 6; c++) key.Curves[c] = MmdCameraMotion.LinearCurve;
            if (y.HasValue) key.Curves[1] = y.Value;
            return key;
        }
        private static bool SamePose(Camera camera, MmdCameraMotion.Pose pose, float tolerance = 1e-4f) =>
            (camera.transform.position - pose.Position).magnitude < tolerance && Quaternion.Angle(camera.transform.rotation, pose.Rotation) < .01f &&
            Math.Abs(camera.fieldOfView - pose.FieldOfView) < 1e-3f;
        private IEnumerator CameraMotionChecks(MmdSceneWorkspace ui, string path, string sceneFile, Action<string> fail)
        {
            var dir = Path.Combine(Path.GetDirectoryName(path), "camera");
            var fixture = Path.Combine(dir, "camera.vmd");
            var camera = Camera.main;
            var dialogs = new ScriptedDialogs(); var native = ui.FileDialogs;
            MmdCameraMotion loaded;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                // Y eases in (control points pulled to the end) while X and Z are linear: separate curves.
                var easeIn = new Vector4(1, 0, 1, 0);
                MmdCameraMotion.Write(fixture, new[]
                {
                    CameraKey(0, new Vector3(0, 10, 0), Vector3.zero, -30, 30),
                    CameraKey(30, new Vector3(10, 20, -5), new Vector3(.2f, 1, 0), -20, 45, y: easeIn),
                    CameraKey(31, new Vector3(0, 5, 0), new Vector3(0, Mathf.PI, 0), -10, 30),          // hard cut
                    // v0.61: a screen tilt (roll) toward the last key, handed over to the manual view.
                    CameraKey(60, new Vector3(0, 5, 0), new Vector3(0, Mathf.PI, .3f), -15, 30, orthographic: true),
                }, lightKeys: 2);
                loaded = MmdCameraMotion.Load(fixture);
                var start = loaded.Evaluate(0);
                var startOk = (start.Position - new Vector3(0, .8f, 2.4f)).magnitude < 1e-4f && Vector3.Angle(start.Rotation * Vector3.forward, Vector3.back) < .01f && Math.Abs(start.FieldOfView - 30) < 1e-4f;
                var mid = loaded.Evaluate(.5);
                var xProgress = -mid.Target.x / (10 * MmdCameraMotion.MmdUnit); var yProgress = (mid.Target.y / MmdCameraMotion.MmdUnit - 10) / 10;
                var separate = Math.Abs(xProgress - .5f) < 2e-3f && yProgress < .3f && yProgress > 0;
                var cutHold = loaded.Evaluate(30.5 / 30); var atKey1 = loaded.Evaluate(1); var atKey2 = loaded.Evaluate(31.0 / 30);
                var cut = (cutHold.Position - atKey1.Position).magnitude < 1e-5f && (atKey2.Target - new Vector3(0, .4f, 0)).magnitude < 1e-5f;
                var warnings = string.Join(" | ", loaded.Warnings);
                Check("camera_vmd_reader_exact_per_axis_curves_and_cuts [x " + xProgress.ToString("0.000") + " y " + yProgress.ToString("0.000") + " warnings: " + warnings + "]",
                    startOk && separate && cut && loaded.Keys.Count == 4 && Math.Abs(loaded.DurationSeconds - 2) < 1e-9 && warnings.Contains("직교") && warnings.Contains("조명"));

                // Same keys as UMT's reference path can take (shared XYZ curve, no orthographic
                // key): identical at its 60 Hz samples.
                var reference = Path.Combine(dir, "camera-umt.vmd");
                var rotEase = new Vector4(.6f, 0, .4f, 1);
                var keys = new[]
                {
                    CameraKey(0, new Vector3(0, 10, 0), Vector3.zero, -30, 30),
                    CameraKey(30, new Vector3(10, 20, -5), new Vector3(.2f, 1, .1f), -20, 45),
                    CameraKey(31, new Vector3(0, 5, 0), new Vector3(0, 3, 0), -10, 30),
                    CameraKey(90, new Vector3(-4, 12, 6), new Vector3(-.3f, -2.5f, 0), -40, 60),
                };
                keys[3].Curves[3] = rotEase; keys[1].Curves[4] = rotEase;
                MmdCameraMotion.Write(reference, keys);
                var ours = MmdCameraMotion.Load(reference);
                float worstPosition = 0, worstAngle = 0, worstFov = 0;
                var sample = new GameObject("Camera Reference Sample").AddComponent<Camera>(); sample.enabled = false;
                using (var umt = new DisposableCamera(MmdPocCamera.Load(reference)))
                {
                    // (At the exact 60 Hz sample times the two agree within 0.001 mm on IL2CPP; 0.1 ms
                    // later the reference adds its linear blend between samples, about 1 mm here.)
                    // 0.1 ms after each 60 Hz sample: the reference path picks keys by float time,
                    // which lands on either side of a cut exactly at its boundary (Mono evaluates
                    // float maths in double precision, IL2CPP does not).
                    for (var i = 0; i <= 179; i++)
                    {
                        var t = i / 60.0 + 1e-4;
                        umt.Value.Sample(sample, (float)t);
                        var pose = ours.Evaluate(t);
                        worstPosition = Math.Max(worstPosition, (sample.transform.position - pose.Position).magnitude);
                        worstAngle = Math.Max(worstAngle, Quaternion.Angle(sample.transform.rotation, pose.Rotation));
                        worstFov = Math.Max(worstFov, Math.Abs(sample.fieldOfView - pose.FieldOfView));
                    }
                }
                // The real Whiplash camera VMD (if present beside the dance VMD): every key time must
                // match UMT; between keys UMT merges the X/Y/Z curves, so only the deviation is reported.
                var realDetail = "real file not found";
                var realOk = true;
                var realPath = Path.Combine(Path.GetDirectoryName(PocArguments.Value("-pocVmd") ?? ""), "Camera [SMOOTH].vmd");
                if (File.Exists(realPath))
                try
                {
                    var real = MmdCameraMotion.Load(realPath);
                    float keyWorst = 0, between = 0;
                    using (var umt = new DisposableCamera(MmdPocCamera.Load(realPath)))
                    {
                        foreach (var key in real.Keys)
                        {
                            umt.Value.Sample(sample, (float)(key.Frame / 30.0 + 1e-4));
                            keyWorst = Math.Max(keyWorst, (sample.transform.position - real.Evaluate(key.Frame / 30.0 + 1e-4).Position).magnitude);
                        }
                        for (var i = 0; i < real.DurationSeconds * 60; i += 7)
                        {
                            umt.Value.Sample(sample, (float)(i / 60.0 + 1e-4));
                            between = Math.Max(between, (sample.transform.position - real.Evaluate(i / 60.0 + 1e-4).Position).magnitude);
                        }
                    }
                    realOk = keyWorst < 1e-3f;
                    realDetail = "real " + real.Keys.Count + " keys, at keys " + (keyWorst * 1000).ToString("0.000") + " mm, between keys (60 Hz, every 7th) max " + (between * 1000).ToString("0.0") + " mm";
                }
                catch (InvalidDataException ex) { realDetail = "real file not comparable with the UMT reference path: " + ex.Message; }
                Destroy(sample.gameObject);
                Check("camera_vmd_matches_umt_reference [synthetic 60 Hz: " + (worstPosition * 1000).ToString("0.000") + " mm, " + worstAngle.ToString("0.000") + " deg, fov " +
                    worstFov.ToString("0.0000") + "; " + realDetail + "]", worstPosition < 2e-3f && worstAngle < .05f && worstFov < 2e-2f && realOk);

                Activate(ui.Root, "explorer-camera");
                ui.FileDialogs = dialogs;
                dialogs.Next = fixture; Activate(ui.Root, "CameraMotionConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            Vector3 beforeRelease = default;
            try
            {
                var motion = ui.Scene.CameraMotion;
                ui.Scene.Seek(1.0); ui.ApplyCameraMotion();
                Check("camera_vmd_connected_follows_scene_clock", dialogs.Asked == "OpenCameraMotion" && ui.Error.Length == 0 && motion != null && ui.CameraFollowing &&
                    SamePose(camera, motion.Evaluate(1.0)) && ui.Root.Q<Button>("explorer-camera").text.Contains("따라가는 중") && ui.UndoLabel == "카메라 VMD 연결" &&
                    ui.Root.Q<Label>("CameraMotionInfo").text.Contains("직교") && ui.Scene.Duration >= motion.DurationSeconds);
                ui.Scene.Playing = true; ui.Scene.Advance(.25); ui.Scene.Playing = false; ui.ApplyCameraMotion();
                var played = SamePose(camera, motion.Evaluate(ui.Scene.Seconds)) && Math.Abs(ui.Scene.Seconds - (1 + .25 * ui.Scene.Speed)) < 1e-9; // this scene plays at 2x
                // v0.61: only real movement releases the motion. Pressing and releasing (a click that
                // selects or clears), a right press, or a sideways right drag (no dolly) keep following.
                ui.Scene.Seek(1.5); ui.ApplyCameraMotion();
                var pose = motion.Evaluate(1.5);
                var centre = new Vector2(Screen.width * .5f, Screen.height * .5f);
                Click(ui, centre); ui.ApplyCameraMotion();
                Frame(ui, centre, Vector2.zero, dolly: true); Frame(ui, centre + new Vector2(30, 0), new Vector2(30, 0), dolly: true); Frame(ui, centre + new Vector2(30, 0), Vector2.zero);
                Frame(ui, centre, Vector2.zero, pan: true); Frame(ui, centre, Vector2.zero);
                ui.ApplyCameraMotion();
                var stillFollowing = ui.CameraFollowing && SamePose(camera, pose);
                Activate(ui.Root, "explorer-camera");
                // The hand-over itself: the first manual frame is the motion's frame, tilt and lens included.
                var vmdFov = pose.FieldOfView; var vmdRoll = Vector3.SignedAngle(Quaternion.LookRotation(pose.Rotation * Vector3.forward) * Vector3.up, pose.Rotation * Vector3.up, pose.Rotation * Vector3.forward);
                ui.Root.Q<Toggle>("CameraFollow").value = false;
                var handOver = !ui.CameraFollowing && (camera.transform.position - pose.Position).magnitude < 1e-4f && Quaternion.Angle(camera.transform.rotation, pose.Rotation) < .01f &&
                    Math.Abs(camera.fieldOfView - vmdFov) < 1e-3f && Math.Abs(ui.Graphics.Settings.fieldOfView - vmdFov) < 1e-3f && Math.Abs(Mathf.DeltaAngle(ui.View.Roll, vmdRoll)) < .01f &&
                    Math.Abs(vmdRoll) > 1 && CameraMatchesView(ui);
                ui.Root.Q<Toggle>("CameraFollow").value = true; ui.ApplyCameraMotion();
                // Then a real left drag: released on its first movement, continuing from the motion's pose.
                beforeRelease = camera.transform.position;
                var grab = centre;
                Frame(ui, grab, Vector2.zero, left: true);
                Frame(ui, grab + new Vector2(6, 0), new Vector2(6, 0), left: true);  // beyond the click slop: orbit 6 px
                var orbitExpected = Quaternion.Euler(-Mathf.Asin(Mathf.Clamp((pose.Rotation * Vector3.forward).y, -1, 1)) * Mathf.Rad2Deg,
                    Mathf.Atan2((pose.Rotation * Vector3.forward).x, (pose.Rotation * Vector3.forward).z) * Mathf.Rad2Deg + 6 * MmdViewportCamera.DegreesPerPixel, vmdRoll);
                var continuous = !ui.CameraFollowing && Quaternion.Angle(camera.transform.rotation, orbitExpected) < .01f && Math.Abs(camera.fieldOfView - vmdFov) < 1e-3f &&
                    (camera.transform.position - beforeRelease).magnitude < Mathf.Abs(pose.Distance) * 6 * MmdViewportCamera.DegreesPerPixel * Mathf.Deg2Rad * 1.01f + 1e-4f;
                Frame(ui, grab + new Vector2(36, 0), new Vector2(30, 0), left: true); Frame(ui, grab + new Vector2(36, 0), Vector2.zero);
                var released = !ui.CameraFollowing && ui.Scene.CameraMotion == motion && !ui.Root.Q<Toggle>("CameraFollow").value &&
                    Math.Abs(camera.fieldOfView - ui.Graphics.Settings.fieldOfView) < 1e-4f && ui.Error.Length == 0;
                var manual = camera.transform.position;
                ui.Scene.Seek(1.25); ui.ApplyCameraMotion();
                var stays = (camera.transform.position - manual).magnitude < 1e-6f;
                ui.Root.Q<Toggle>("CameraFollow").value = true;
                var back = ui.CameraFollowing && SamePose(camera, motion.Evaluate(1.25));
                Check("manual_navigation_releases_camera_vmd_and_follow_resumes [played=" + played + " kept=" + stillFollowing + " handOver=" + handOver + " (fov " + vmdFov.ToString("0.0") +
                    ", roll " + vmdRoll.ToString("0.0") + ") continuous=" + continuous + " released=" + released + " stays=" + stays + " back=" + back + "]",
                    played && stillFollowing && handOver && continuous && released && stays && back);
                // Rejections keep the track: a dance VMD without camera keys, a non-VMD file.
                dialogs.Next = Path.Combine(Path.GetDirectoryName(path), "first.vmd"); Activate(ui.Root, "CameraMotionConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            var kept = ui.Scene.CameraMotion;
            yield return null;
            while (ui.Busy) yield return null;
            string danceError;
            try
            {
                danceError = ui.Error;
                dialogs.Next = Path.Combine(Path.GetDirectoryName(path), "invalid.vmd"); Activate(ui.Root, "CameraMotionConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                Check("camera_vmd_rejections_keep_track [" + danceError + " | " + ui.Error + "]", danceError.Contains("카메라 키가 없습니다") && ui.Error.Contains("VMD") &&
                    ui.Scene.CameraMotion == kept && ui.CameraFollowing);
                // Undo/redo of the connection.
                ui.Undo();
                var undone = ui.Scene.CameraMotion == null && !ui.CameraFollowing;
                ui.Redo();
                Check("camera_vmd_connect_undo_redo", undone && ui.Scene.CameraMotion == kept && ui.CameraFollowing);
                if (!ui.SaveTo(Path.Combine(dir, "camera.mmdscene"))) throw new InvalidOperationException("camera scene save failed: " + ui.Error);
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            var cameraScene = Path.Combine(dir, "camera.mmdscene");
            yield return ui.OpenFrom(cameraScene);
            var missingScene = Path.Combine(dir, "missing-camera.mmdscene");
            try
            {
                var stored = MmdSceneDocument.Read(cameraScene).cameraMotion;
                var reopened = ui.Scene.CameraMotion;
                var ok = stored.path == "camera.vmd" && stored.follow && reopened != null && reopened != kept && ui.CameraFollowing && ui.Error.Length == 0 &&
                    !ui.Dirty && ui.LastLoadProblems.Length == 0;
                var file = MmdSceneDocument.Read(cameraScene); file.cameraMotion.path = "gone/camera.vmd";
                MmdSceneDocument.Write(missingScene, file);
                if (!ok) throw new InvalidOperationException("camera_vmd_saved_and_reopened [stored=" + stored.path + " follow=" + stored.follow + " reopened=" + (reopened != null) + " following=" + ui.CameraFollowing + " error=" + ui.Error + "]");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return ui.OpenFrom(missingScene);
            try
            {
                var listed = ui.DialogVisible && ui.LastLoadProblems.Any(p => p.Contains("gone/camera.vmd")) && ui.Scene.CameraMotion == null && !ui.CameraFollowing &&
                    ui.UnresolvedCameraPath == "gone/camera.vmd" && ui.Root.Q<Button>("explorer-camera").text.Contains("누락");
                Activate(ui.Root, "NoticeOk");
                if (!ui.SaveTo(Path.Combine(dir, "sub-missing.mmdscene"))) throw new InvalidOperationException("missing camera save failed: " + ui.Error);
                var keptPath = MmdSceneDocument.Read(Path.Combine(dir, "sub-missing.mmdscene")).cameraMotion.path == "gone/camera.vmd";
                Check("camera_vmd_saved_reopened_and_missing_kept [listed=" + listed + " kept=" + keptPath + "]", listed && keptPath);
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            // The main scene keeps the camera VMD, released (the relaunch check compares the
            // manual view and the graphics FOV with the camera).
            yield return ui.OpenFrom(sceneFile);
            try
            {
                Activate(ui.Root, "explorer-camera");
                dialogs.Next = fixture; Activate(ui.Root, "CameraMotionConnect");
            }
            catch (Exception ex) { ui.FileDialogs = native; fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                ui.FileDialogs = native;
                ui.Root.Q<Toggle>("CameraFollow").value = false;
                if (!ui.SaveTo(sceneFile)) throw new InvalidOperationException("scene save with camera failed: " + ui.Error);
                var saved = MmdSceneDocument.Read(sceneFile);
                Check("scene_with_camera_vmd_saved_for_relaunch", saved.cameraMotion.path == "../camera/camera.vmd" && !saved.cameraMotion.follow &&
                    saved.audio.path == "../audio/song.wav" && saved.sceneObjects.Count == 3);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }
        private sealed class DisposableCamera : IDisposable
        {
            public readonly MmdPocCamera Value;
            public DisposableCamera(MmdPocCamera value) { Value = value; }
            public void Dispose() => Value?.Dispose();
        }
        // ----- H2b-01 material styles -----
        private static int StyledMaterialCount() => Resources.FindObjectsOfTypeAll<Material>()
            .Count(m => m != null && m.shader != null && (m.shader.name == "EasyDanceStudio/Toon" || m.shader.name == "EasyDanceStudio/StageLit"));
        private static Material[] MaterialsOf(MmdSceneService.SceneObject item) =>
            item.Model.root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
        private void MaterialChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a, MmdSceneService.SceneObject b, MmdSceneService.SceneObject stage, string directory)
        {
            var camera = Camera.main; var view = ui.View;
            var savedView = (view.Pivot, view.Yaw, view.Pitch, view.Distance);
            var savedGraphics = ui.Graphics.Settings; var savedSelection = ui.SelectedId;
            var light = ui.Graphics.Settings; light.backgroundColor = new Color(.5f, .55f, .6f); light.lightIntensity = 2; light.lightYaw = -30; light.lightPitch = 35;
            // SSAO's blue noise moves between renders; measure without screen effects.
            light.ssaoEnabled = false; light.dofEnabled = false; light.vignetteEnabled = false; light.bloomEnabled = false;
            ui.ApplyGraphics(light);
            // The look preset checks left the characters in toon (presets carry a character style).
            foreach (var item in new[] { a, b }) ui.Scene.SetMaterialStyle(item.Id, new MaterialStyleData());
            Activate(ui.Root, "object-" + a.Id);
            var originals = MaterialsOf(a);
            var dropdown = ui.Root.Q<DropdownField>("MaterialStyle");
            var characterChoices = !dropdown.choices.Contains("PBR") && dropdown.choices.Contains("툰");
            dropdown.value = "툰";
            var toonMaterials = MaterialsOf(a);
            var toonCount = toonMaterials.Count(m => m.shader.name == "EasyDanceStudio/Toon");
            var replaced = toonCount > 0 && toonCount == originals.Length - a.Styler.UnsupportedCount && a.MaterialStyle.style == MaterialStyleData.Toon &&
                ui.Root.Q("ToonControls").style.display.value == DisplayStyle.Flex;
            dropdown.value = "원본";
            var restored = MaterialsOf(a).SequenceEqual(originals);
            var restoreDiff = string.Join(", ", MaterialsOf(a).Zip(originals, (x, y) => x == y ? "" : (x == null ? "null" : x.name + "#" + x.GetInstanceID()) + "!=" + (y == null ? "null" : y.name + "#" + y.GetInstanceID()))
                .Where(t => t.Length > 0).Take(3)) + " counts " + MaterialsOf(a).Length + "/" + originals.Length;
            ui.Undo();
            var undoToon = MaterialsOf(a).SequenceEqual(toonMaterials);
            ui.Undo();
            var undoOriginal = MaterialsOf(a).SequenceEqual(originals) && a.MaterialStyle.style == MaterialStyleData.Original;
            ui.Redo();
            Check("toon_style_replaces_materials_and_original_restores [toon " + toonCount + "/" + originals.Length + " choices=" + characterChoices + " replaced=" + replaced +
                " restored=" + restored + " undo=" + undoToon + "/" + undoOriginal + " diff: " + restoreDiff + "]", characterChoices && replaced && restored && undoToon && undoOriginal &&
                a.MaterialStyle.style == MaterialStyleData.Toon);

            // The toon shader is in the Player and takes the light: dimming or turning the light
            // changes the character (the unlit original does not).
            ui.FrameIds(a.Id);
            Activate(ui.Root, "explorer-light"); // no selection highlight in the measured frames
            float Changed(Color32[] x, Color32[] y) =>
                x.Zip(y, (p, q) => Math.Abs((p.r + p.g + p.b) - (q.r + q.g + q.b)) >= 9 ? 1 : 0).Sum() / (float)x.Length;
            var toonBright = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-light-bright.png"));
            ui.Root.Q<Slider>("LightYaw").value = 150;
            var toonTurned = RenderedPixels(camera);
            ui.Root.Q<Slider>("LightYaw").value = -30;
            ui.Root.Q<Slider>("LightIntensity").value = .6f;
            var toonDim = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-light-dim.png"));
            var keepToon = a.MaterialStyle.Clone();
            ui.Scene.SetMaterialStyle(a.Id, new MaterialStyleData());
            var plainDim = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-plain-dim.png"));
            ui.Root.Q<Slider>("LightIntensity").value = 2;
            var plainBright = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-plain-bright.png"));
            ui.Scene.SetMaterialStyle(a.Id, keepToon);
            Activate(ui.Root, "object-" + a.Id);
            var dimmed = Changed(toonBright, toonDim); var turned = Changed(toonBright, toonTurned); var plain = Changed(plainBright, plainDim);
            Check("toon_shader_in_player_responds_to_light [pixels changed: toon dim " + (dimmed * 100).ToString("0.0") + "%, turned " + (turned * 100).ToString("0.0") +
                "%, original " + (plain * 100).ToString("0.00") + "%]", dimmed > .01f && turned > .005f && plain < .001f);

            // Controls drive the material values; the outline pass switches off.
            ui.Root.Q<Slider>("ToonShadowStrength").value = .3f;
            ui.Root.Q<Slider>("ToonOutlineWidth").value = 2;
            ui.Root.Q<Toggle>("ToonRim").value = false;
            var toon = MaterialsOf(a).Where(m => m.shader.name == "EasyDanceStudio/Toon").ToArray();
            var pmx = a.Model.model.materials;
            var edgeMaterials = toon.Count(m => m.GetFloat("_OutlineWidth") > 0);
            var values = toon.All(m => Math.Abs(m.GetFloat("_ShadowStrength") - .3f) < 1e-5f && m.GetFloat("_RimStrength") == 0) && edgeMaterials > 0 &&
                true;
            var outlined = RenderedSharpness(camera);
            ui.Root.Q<Toggle>("ToonOutline").value = false;
            var noOutline = RenderedSharpness(camera);
            var passOff = MaterialsOf(a).Where(m => m.shader.name == "EasyDanceStudio/Toon").All(m => m.GetFloat("_OutlineWidth") == 0);
            ui.Root.Q<Toggle>("ToonOutline").value = true; ui.Root.Q<Toggle>("ToonRim").value = true;
            ui.Root.Q<Slider>("ToonShadowStrength").value = 1; ui.Root.Q<Slider>("ToonOutlineWidth").value = 1;
            Check("toon_controls_drive_material_values_and_outline_pass [edge materials " + edgeMaterials + "/" + toon.Length + ", sharpness outline " + outlined.ToString("0.00") +
                " none " + noOutline.ToString("0.00") + "]", values && passOff && Math.Abs(outlined - noOutline) > .01f);

            // PBR is offered for backgrounds only.
            Activate(ui.Root, "object-" + stage.Id);
            var stageChoices = ui.Root.Q<DropdownField>("MaterialStyle").choices.Contains("PBR");
            ui.Root.Q<DropdownField>("MaterialStyle").value = "PBR";
            ui.Root.Q<Slider>("PbrSmoothness").value = .8f;
            var stageMaterials = MaterialsOf(stage).Where(m => m.shader.name == "EasyDanceStudio/StageLit").ToArray();
            ui.FrameIds(stage.Id);
            Activate(ui.Root, "explorer-light");
            var pbr = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-pbr-bright.png"));
            ui.Root.Q<Slider>("LightIntensity").value = .5f;
            var pbrDim = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "material-pbr-dim.png"));
            ui.Root.Q<Slider>("LightIntensity").value = 2;
            Activate(ui.Root, "object-" + stage.Id);
            var pbrChanged = Changed(pbr, pbrDim);
            var rejected = false;
            try { ui.Scene.SetMaterialStyle(a.Id, new MaterialStyleData { style = MaterialStyleData.Pbr }); } catch (InvalidOperationException) { rejected = true; }
            Check("pbr_style_for_backgrounds_only_and_lit [" + stageMaterials.Length + " materials, pixels changed by the light " + (pbrChanged * 100).ToString("0.0") + "%]", stageChoices &&
                stageMaterials.Length > 0 && stageMaterials.All(m => Math.Abs(m.GetFloat("_Smoothness") - .8f) < 1e-5f) && pbrChanged > .01f && rejected && a.MaterialStyle.style == MaterialStyleData.Toon);

            // Before/after on two different models (skin, hair, transparent parts), saved for review.
            var details = new List<string>();
            var differ = true;
            foreach (var item in new[] { a, b })
            {
                var name = Path.GetFileNameWithoutExtension(item.ModelSource);
                ui.FrameIds(item.Id);
                var keep = item.MaterialStyle.Clone();
                ui.Scene.SetMaterialStyle(item.Id, new MaterialStyleData());
                var before = RenderedLuminance(camera);
                SaveRendered(camera, Path.Combine(directory, "material-" + (item == a ? "a" : "b") + "-original.png"));
                ui.Scene.SetMaterialStyle(item.Id, new MaterialStyleData { style = MaterialStyleData.Toon });
                var after = RenderedLuminance(camera);
                SaveRendered(camera, Path.Combine(directory, "material-" + (item == a ? "a" : "b") + "-toon.png"));
                ui.Scene.SetMaterialStyle(item.Id, keep);
                var styler = item.Styler;
                differ &= Math.Abs(after - before) > .5f;
                details.Add(name + ": 재질 " + styler.MaterialCount + " · 반투명 " + styler.TransparentCount + " · 윤곽선 " + styler.EdgeCount + " · 툰 텍스처 " +
                    styler.ToonMapCount + " · 스피어 " + styler.SphereCount + " · 미지원 " + styler.UnsupportedCount + " · 휘도 " + before.ToString("0.0") + "→" + after.ToString("0.0"));
            }
            Debug.Log("[material-probe] " + string.Join(" | ", details));
            Check("two_models_before_after_images [" + string.Join(" | ", details) + "]", differ &&
                new[] { "a-original", "a-toon", "b-original", "b-toon" }.All(n => File.Exists(Path.Combine(directory, "material-" + n + ".png"))) &&
                ui.Root.Q<Label>("MaterialWarnings") != null);

            // Leave Gene in toon and the stage in PBR: saved with the scene, checked on relaunch.
            view.Set(savedView.Pivot, savedView.Yaw, savedView.Pitch, savedView.Distance); view.Apply(camera);
            ui.ApplyGraphics(savedGraphics);
            if (savedSelection != null) ui.Select(savedSelection);
        }
        // ----- v0.61 screen correction -----
        private static int MaxChannelDiff(Color32 x, Color32 y) => Math.Max(Math.Abs(x.r - y.r), Math.Max(Math.Abs(x.g - y.g), Math.Abs(x.b - y.b)));
        // Pixels whose colour changed (any channel by more than 2), optionally only where `mask` is true.
        private static int PixelsChanged(Color32[] x, Color32[] y, bool[] mask = null)
        {
            var count = 0;
            for (var i = 0; i < x.Length; i++) if ((mask == null || mask[i]) && MaxChannelDiff(x[i], y[i]) > 2) count++;
            return count;
        }
        private static double Psnr(Color32[] x, Color32[] y)
        {
            double sum = 0;
            for (var i = 0; i < x.Length; i++) sum += Sq(x[i].r - y[i].r) + Sq(x[i].g - y[i].g) + Sq(x[i].b - y[i].b);
            return 10 * Math.Log10(255.0 * 255 / Math.Max(1e-9, sum / (x.Length * 3.0)));
        }
        // Where one PMX material of the object is on screen: the pixels that change when its slots
        // draw a flat magenta material instead (independent of the correction code under test).
        private static bool[] RegionOf(MmdSceneService.SceneObject item, int pmx, Camera camera, Color32[] reference)
        {
            var marker = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Probe Region Marker" };
            marker.SetColor("_BaseColor", new Color(1, 0, 1, 1));
            var swapped = new List<(Renderer renderer, Material[] set)>();
            try
            {
                foreach (var mesh in item.Model.meshes)
                {
                    if (mesh.renderer == null || mesh.materialIndices == null) continue;
                    var set = mesh.renderer.sharedMaterials; var copy = (Material[])set.Clone(); var any = false;
                    for (var i = 0; i < copy.Length && i < mesh.materialIndices.Length; i++) if (mesh.materialIndices[i] == pmx) { copy[i] = marker; any = true; }
                    if (any) { swapped.Add((mesh.renderer, set)); mesh.renderer.sharedMaterials = copy; }
                }
                var pixels = RenderedPixels(camera);
                return reference.Zip(pixels, (x, y) => MaxChannelDiff(x, y) > 2).ToArray();
            }
            finally
            {
                foreach (var (renderer, set) in swapped) renderer.sharedMaterials = set;
                Destroy(marker);
            }
        }
        // Material slots of an object that carry a correction block, as "pmx" indices (one entry per slot).
        private static List<int> BlockedSlots(MmdSceneService.SceneObject item, Func<MaterialPropertyBlock, bool> test = null)
        {
            var list = new List<int>(); var block = new MaterialPropertyBlock();
            foreach (var mesh in item.Model.meshes)
            {
                if (mesh.renderer == null) continue;
                for (var i = 0; i < mesh.renderer.sharedMaterials.Length; i++)
                {
                    mesh.renderer.GetPropertyBlock(block, i);
                    if (!block.isEmpty && (test == null || test(block))) list.Add(mesh.materialIndices != null && i < mesh.materialIndices.Length ? mesh.materialIndices[i] : -1);
                }
            }
            return list;
        }
        private readonly Dictionary<string, string> expectedCorrections = new Dictionary<string, string>();
        private void CorrectionChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a, MmdSceneService.SceneObject b, MmdSceneService.SceneObject stage, string directory)
        {
            var camera = Camera.main; var view = ui.View;
            var savedView = (view.Pivot, view.Yaw, view.Pitch, view.Distance);
            var savedGraphics = ui.Graphics.Settings; var savedSelection = ui.SelectedId;
            // Deterministic frames: no SSAO noise, no bloom spill across materials.
            var plain = ui.Graphics.Settings; plain.ssaoEnabled = false; plain.dofEnabled = false; plain.vignetteEnabled = false; plain.bloomEnabled = false;
            ui.ApplyGraphics(plain);

            // The matching rules on their own: exact, moved (unique name), ambiguous and missing names.
            var names = new[] { "skin", "cloth", "cloth", "hair" };
            var rules = new ColorCorrectionData();
            foreach (var (index, name) in new[] { (0, "skin"), (5, "hair"), (9, "cloth"), (1, "eyes"), (2, "cloth") }) rules.materials.Add(new MaterialCorrectionData { index = index, name = name });
            var r = MmdMaterialStyler.ResolveCorrections(rules, names.Length, i => names[i]);
            Check("correction_material_matching_rules [" + string.Join(" | ", r.Problems.Values) + "]", r.ByPmx.Count == 3 && r.ByPmx[0] == rules.materials[0] &&
                r.ByPmx[3] == rules.materials[1] && r.Moved.ContainsKey(rules.materials[1]) && r.ByPmx[2] == rules.materials[4] &&
                r.Problems.ContainsKey(rules.materials[2]) && r.Problems.ContainsKey(rules.materials[3]));

            // A click on the character selects it and chooses the material under the cursor.
            ui.FrameIds(a.Id);
            Activate(ui.Root, "object-" + a.Id);
            MmdSceneWorkspace.TryBounds(new[] { a }, out var aBounds);
            Vector2? point = null;
            foreach (var world in new[] { BodyPoint(a), aBounds.center, aBounds.center + Vector3.up * .3f, aBounds.center - Vector3.up * .3f })
            {
                var screen = Camera.main.WorldToScreenPoint(world); var candidate = new Vector2(screen.x, screen.y);
                if (point == null && screen.z > 0 && ui.GizmoHandleAt(candidate) < 0 && ui.RaycastScene(candidate).Item == a) point = candidate;
            }
            if (point == null) throw new InvalidOperationException("No point on the first character for the material pick");
            var hit = ui.RaycastScene(point.Value);
            Click(ui, point.Value);
            var styler = a.Styler;
            var dropdown = ui.Root.Q<DropdownField>("CorrectionMaterial");
            var drawn = Enumerable.Range(0, styler.PmxMaterialCount).Count(styler.PmxDrawn);
            Check("viewport_click_picks_object_and_material_under_cursor [#" + hit.PmxMaterial + " " + styler.PmxName(hit.PmxMaterial) + ", list " + dropdown.choices.Count + "/" + styler.PmxMaterialCount + " PMX]",
                ui.SelectedId == a.Id && hit.PmxMaterial >= 0 && ui.CorrectionPmx == hit.PmxMaterial && dropdown.value.StartsWith(hit.PmxMaterial + " · ") &&
                dropdown.choices.Count == drawn && drawn > 1 && !ui.Root.Q<Foldout>("CorrectionMaterials").value && ui.Root.Q("CorrectionSection").style.display.value != DisplayStyle.None);
            ui.Root.Q<Foldout>("CorrectionMaterials").value = true; // opened when needed

            // Frames without the selection highlight; the material choice survives reselection.
            Color32[] Shot() { var keep = ui.SelectedId; Activate(ui.Root, "explorer-light"); var pixels = RenderedPixels(camera); if (keep != null) Activate(ui.Root, "object-" + keep); return pixels; }
            Activate(ui.Root, "explorer-light");
            var neutral = RenderedPixels(camera);
            SaveRendered(camera, Path.Combine(directory, "correction-before.png"));
            // The test material: the one with the most well-lit pixels on screen (a black material cannot show a multiplier).
            int test = -1, best = 0; bool[] region = null;
            for (var i = 0; i < styler.PmxMaterialCount; i++)
            {
                if (!styler.PmxDrawn(i)) continue;
                var candidate = RegionOf(a, i, camera, neutral);
                var score = 0;
                for (var p = 0; p < candidate.Length; p++) if (candidate[p] && neutral[p].r + neutral[p].g + neutral[p].b > 240) score++;
                if (score > best) { best = score; test = i; region = candidate; }
            }
            if (test < 0) throw new InvalidOperationException("No lit PMX material of the first character is on screen");
            var outside = region.Select(v => !v).ToArray();
            Activate(ui.Root, "object-" + a.Id);
            var testName = styler.PmxName(test);
            dropdown.value = dropdown.choices.First(c => c.StartsWith(test + " · "));
            var chosen = ui.CorrectionPmx == test;

            // One material: colour (coalesced into one undo step), only its own pixels change.
            ui.Root.Q<Slider>("CorrectionMaterialG").value = .7f; ui.Root.Q<Slider>("CorrectionMaterialB").value = .7f;
            var tinted = Shot();
            var inside = PixelsChanged(neutral, tinted); var leaked = PixelsChanged(neutral, tinted, outside);
            var slots = BlockedSlots(a);
            var onlyThere = slots.Count > 0 && slots.All(p => p == test) && BlockedSlots(b).Count == 0 && BlockedSlots(stage).Count == 0;
            var oneStep = ui.UndoLabel == "재질 보정: " + testName && a.Correction.materials.Count == 1 && a.Correction.materials[0].index == test && a.Correction.materials[0].name == testName;
            Check("one_material_correction_changes_only_that_material [#" + test + " " + testName + ": region " + region.Count(v => v) + " px, changed " + inside + ", outside " + leaked +
                ", slots " + slots.Count + "]", chosen && inside > 20 && leaked == 0 && onlyThere && oneStep && dropdown.value.EndsWith("보정됨"));
            SaveRendered(camera, Path.Combine(directory, "correction-material.png"));

            // Gloss: the toon highlight, on that material only.
            var glossSlider = ui.Root.Q<Slider>("CorrectionGloss");
            var glossOffered = glossSlider.enabledSelf && ui.Root.Q<Label>("CorrectionSupport").text.Contains("툰 하이라이트");
            glossSlider.value = 1;
            var glossy = Shot();
            var glossChanged = PixelsChanged(tinted, glossy); var glossLeaked = PixelsChanged(tinted, glossy, outside);
            var glossBlocks = BlockedSlots(a, block => block.GetFloat("_Gloss") > .99f);
            Check("gloss_adds_highlight_on_that_material_only [changed " + glossChanged + " px, outside " + glossLeaked + "]",
                glossOffered && glossChanged > 0 && glossLeaked == 0 && glossBlocks.Count > 0 && glossBlocks.All(p => p == test));

            // The object's colour and brightness: all of its materials, nothing else in the scene.
            ui.Root.Q<Slider>("CorrectionBrightness").value = .6f;
            var dim = Shot();
            var objectChanged = PixelsChanged(glossy, dim);
            Check("object_brightness_changes_whole_object_only [changed " + objectChanged + " px vs one material " + inside + "]",
                objectChanged > inside && BlockedSlots(a).Count > slots.Count && BlockedSlots(b).Count == 0 && BlockedSlots(stage).Count == 0 &&
                ui.Root.Q<Label>("CorrectionStatus").text.Contains("적용 중"));
            SaveRendered(camera, Path.Combine(directory, "correction-after.png"));

            // One button: the same frame from the same camera without any correction, and back.
            var pose = (camera.transform.position, camera.transform.rotation, camera.fieldOfView, ui.Scene.Seconds);
            Activate(ui.Root, "CorrectionCompare");
            var off = !ui.Scene.CorrectionsShown && ui.Root.Q<Label>("CorrectionCompareChip").style.display.value != DisplayStyle.None &&
                ui.Root.Q<Button>("CorrectionCompare").text == "보정 후 보기" && ui.Root.Q<Label>("CorrectionStatus").text.Contains("꺼짐");
            var beforeShot = Shot();
            Activate(ui.Root, "CorrectionCompare");
            var afterShot = Shot();
            var samePose = (camera.transform.position, camera.transform.rotation, camera.fieldOfView, ui.Scene.Seconds) == pose;
            Check("compare_button_shows_before_and_after_in_place [before vs neutral " + PixelsChanged(neutral, beforeShot) + " px, after vs corrected " + PixelsChanged(dim, afterShot) + " px]",
                off && PixelsChanged(neutral, beforeShot) == 0 && PixelsChanged(dim, afterShot) == 0 && ui.Scene.CorrectionsShown && samePose &&
                ui.Root.Q<Label>("CorrectionCompareChip").style.display.value == DisplayStyle.None);

            // Reset only the chosen material; the object values stay. Undo and redo.
            var withMaterial = JsonUtility.ToJson(a.Correction);
            Activate(ui.Root, "CorrectionMaterialReset");
            var reset = a.Correction.materials.Count == 0 && Near(a.Correction.brightness, .6f) && BlockedSlots(a, block => block.HasFloat("_Gloss")).Count == 0;
            ui.Undo();
            var undone = JsonUtility.ToJson(a.Correction) == withMaterial;
            ui.Redo(); var redone = a.Correction.materials.Count == 0; ui.Undo();
            Check("reset_one_material_keeps_object_values_and_undoes [reset=" + reset + " undo=" + undone + " redo=" + redone + "]",
                reset && undone && redone && JsonUtility.ToJson(a.Correction) == withMaterial && PixelsChanged(dim, Shot()) == 0);

            // An entry that matches no material of this model is kept, applied nowhere, and explained.
            var kept = a.Correction.Clone();
            var bogus = a.Correction.Clone(); bogus.materials.Add(new MaterialCorrectionData { index = 4000, name = "存在しない材質", tint = Color.black });
            ui.Scene.SetCorrection(a.Id, bogus); Activate(ui.Root, "object-" + a.Id);
            var problems = ui.Root.Q<Label>("CorrectionProblems");
            var listed = problems.style.display.value != DisplayStyle.None && problems.text.Contains("存在しない材質") && problems.text.Contains("적용하지 않고") &&
                ui.Root.Q<Label>("CorrectionStatus").text.Contains("적용 못 한 재질 1개");
            var untouched = PixelsChanged(dim, Shot()) == 0;
            var stored = ui.Capture(Path.Combine(directory, "probe.mmdscene")).sceneObjects.First(o => o.id == a.Id).correction.materials.Any(m => m.name == "存在しない材質");
            ui.Scene.SetCorrection(a.Id, kept); Activate(ui.Root, "object-" + a.Id);
            Check("unmatched_material_correction_kept_applied_nowhere_and_explained [listed=" + listed + " untouched=" + untouched + " stored=" + stored + "]", listed && untouched && stored);

            // Gloss needs a lit style: offered as unsupported on the original (unlit) style.
            Activate(ui.Root, "object-" + b.Id);
            var unlit = b.MaterialStyle.style == MaterialStyleData.Original && !ui.Root.Q<Slider>("CorrectionGloss").enabledSelf &&
                ui.Root.Q<Label>("CorrectionSupport").text.Contains("광택 ×");
            Check("gloss_marked_unsupported_on_original_style", unlit);

            // Leave corrections for the save/relaunch checks: the first character (object + one
            // material with gloss) and the stage (object brightness). The second character stays neutral.
            Activate(ui.Root, "object-" + a.Id);
            ui.Root.Q<Slider>("CorrectionBrightness").value = 1.1f; ui.Root.Q<Slider>("CorrectionTintB").value = .9f;
            ui.Root.Q<Slider>("CorrectionMaterialG").value = .85f; ui.Root.Q<Slider>("CorrectionMaterialB").value = .85f; ui.Root.Q<Slider>("CorrectionGloss").value = .5f;
            Activate(ui.Root, "object-" + stage.Id);
            ui.Root.Q<Slider>("CorrectionBrightness").value = .85f;
            expectedCorrections.Clear();
            foreach (var item in new[] { a, b, stage }) expectedCorrections[item.Id] = JsonUtility.ToJson(item.Correction);
            correctionTestMaterial = test;
            Check("corrections_left_for_relaunch [" + string.Join(" | ", new[] { a, stage }.Select(o => Path.GetFileNameWithoutExtension(o.ModelSource) + ": " + JsonUtility.ToJson(o.Correction))) + "]",
                !a.Correction.IsNeutral && a.Correction.materials.Count == 1 && !stage.Correction.ObjectNeutral && b.Correction.IsNeutral && BlockedSlots(stage).Count > 0);

            view.Set(savedView.Pivot, savedView.Yaw, savedView.Pitch, savedView.Distance); view.Apply(camera);
            ui.ApplyGraphics(savedGraphics);
            if (savedSelection != null) ui.Select(savedSelection);
        }
        private int correctionTestMaterial = -1;

        // The corrected character after save/reopen: the same values, and the preview and the PNG
        // output of the same frame agree, before and after the correction.
        private IEnumerator CorrectionOutputChecks(MmdSceneWorkspace ui, string path, Action<string> fail)
        {
            var dir = Path.Combine(Path.GetDirectoryName(path), "correction");
            var small = new OutputData { preset = OutputData.Custom, width = 640, height = 360, fps = 30 };
            var camera = Camera.main;
            var savedGraphics = ui.Graphics.Settings; var savedSelection = ui.SelectedId;
            var savedView = (ui.View.Pivot, ui.View.Yaw, ui.View.Pitch, ui.View.Distance, ui.View.Roll);
            MmdSceneService.SceneObject item = null; ColorCorrectionData keep = null;
            var keepAll = new Dictionary<string, ColorCorrectionData>();
            Color32[] previewAfter = null, previewBefore = null;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var restored = ui.Scene.Objects.All(o => expectedCorrections.TryGetValue(o.Id, out var json) && json == JsonUtility.ToJson(o.Correction));
                item = ui.Scene.Objects.FirstOrDefault(o => o.Kind == MmdSceneService.ObjectKind.Character && !o.Correction.IsNeutral);
                Check("corrections_survive_save_and_reopen [" + ui.Scene.Objects.Count + " objects]", restored && item != null && item.Styler != null && item.Styler.CorrectedSlots > 0 &&
                    item.Styler.Resolution.Problems.Count == 0);
                keep = item.Correction.Clone();
                var plain = ui.Graphics.Settings; plain.ssaoEnabled = false; plain.dofEnabled = false; plain.vignetteEnabled = false; plain.bloomEnabled = false;
                ui.Graphics.Apply(plain);
                if (ui.CameraFollowing) ui.SetCameraFollow(false);
                ui.FrameIds(item.Id); ui.SelectTarget(MmdSceneWorkspace.InspectorTarget.Light);
                ui.Scene.Playing = false; ui.Scene.Seek(.75);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            // Output finishes the physics bake before rendering; the preview must show the same pose.
            for (var guard = 0; ui.Scene.PhysicsBaking && guard < 20000; guard++) { ui.Scene.BakePhysicsSlice(); yield return null; }
            yield return null; // the skinned pose of this time is in place after a LateUpdate
            try
            {
                ui.Scene.Seek(.75);
                previewAfter = RenderedPixels(camera, small.width, small.height);
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, Path.Combine(dir, "output-after.png"));
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                ui.Scene.ShowCorrections(false);
                previewBefore = RenderedPixels(camera, small.width, small.height);
                ui.Scene.ShowCorrections(true);
                // Output always draws the corrections, so the "before" output is made without any
                // (the preview's "보정 전 보기" turns off every object's correction, the stage's too).
                foreach (var o in ui.Scene.Objects) { keepAll[o.Id] = o.Correction.Clone(); ui.Scene.SetCorrection(o.Id, new ColorCorrectionData()); }
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, Path.Combine(dir, "output-before.png"));
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                foreach (var entry in keepAll) ui.Scene.SetCorrection(entry.Key, entry.Value);
                var outputAfter = LoadPng(Path.Combine(dir, "output-after.png")); var outputBefore = LoadPng(Path.Combine(dir, "output-before.png"));
                var after = outputAfter.GetPixels32(); var before = outputBefore.GetPixels32();
                SavePixels(previewAfter, small.width, small.height, Path.Combine(dir, "preview-after.png"));
                SavePixels(previewBefore, small.width, small.height, Path.Combine(dir, "preview-before.png"));
                var matchAfter = Psnr(previewAfter, after); var matchBefore = Psnr(previewBefore, before);
                var changed = PixelsChanged(before, after);
                var environment = SystemInfo.graphicsDeviceName + " · " + Screen.width + "x" + Screen.height + " · " + Path.GetFileNameWithoutExtension(item.ModelSource) +
                    " #" + correctionTestMaterial + " · " + small.width + "x" + small.height;
                Debug.Log("[correction-probe] " + environment + " · preview/output after " + matchAfter.ToString("0.0") + " dB, before " + matchBefore.ToString("0.0") + " dB, changed " + changed);
                Check("preview_and_output_use_the_same_correction [after " + matchAfter.ToString("0.0") + " dB, before " + matchBefore.ToString("0.0") + " dB, corrected pixels " + changed +
                    "; " + environment + "]", matchAfter > 40 && matchBefore > 40 && changed > 50 && ui.Error.Length == 0 && JsonUtility.ToJson(item.Correction) == JsonUtility.ToJson(keep));
                Destroy(outputAfter); Destroy(outputBefore);
                ui.Graphics.Apply(savedGraphics);
                ui.View.Set(savedView.Pivot, savedView.Yaw, savedView.Pitch, savedView.Distance, savedView.Roll); ui.View.Apply(camera);
                if (savedSelection != null && ui.Scene.Objects.Any(o => o.Id == savedSelection)) ui.Select(savedSelection);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }
        private static Color32[] RenderedPixels(Camera camera, int width, int height)
        {
            var texture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousRect = camera.rect; var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previousActive; camera.targetTexture = previousTarget; camera.rect = previousRect;
                RenderTexture.ReleaseTemporary(texture); Destroy(pixels);
            }
        }
        private static void SavePixels(Color32[] pixels, int width, int height, string file)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(file, texture.EncodeToPNG()); }
            finally { Destroy(texture); }
        }

        // The inspector's 화면 보정 section on the corrected character, after and before (for review).
        private IEnumerator CorrectionScreenshots(MmdSceneWorkspace ui, string path, Action<string> fail)
        {
            var dir = Path.GetDirectoryName(path);
            var after = Path.Combine(dir, "scene-ui-correction.png"); var before = Path.Combine(dir, "scene-ui-correction-before.png");
            var previousTarget = ui.Target; var previousId = ui.SelectedId;
            var previousView = (ui.View.Pivot, ui.View.Yaw, ui.View.Pitch, ui.View.Distance, ui.View.Roll);
            try
            {
                foreach (var file in new[] { after, before }) if (File.Exists(file)) File.Delete(file);
                var item = ui.Scene.Objects.FirstOrDefault(o => o.Kind == MmdSceneService.ObjectKind.Character && o.Correction.materials.Count > 0);
                if (item == null) throw new InvalidOperationException("No character with a material correction for the screenshot");
                ui.FrameIds(item.Id); ui.Select(item.Id);
                ui.Root.Q<Foldout>("CorrectionMaterials").value = true;
                if (correctionTestMaterial >= 0) ui.ChooseCorrectionMaterial(correctionTestMaterial);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null; yield return null; // layout, then scroll the section into view
            try
            {
                // The section's top (status, compare button) at the top of the inspector.
                var scroll = ui.Root.Q<ScrollView>("InspectorContent");
                scroll.scrollOffset = new Vector2(0, ui.Root.Q("CorrectionSection").layout.y);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            ScreenCapture.CaptureScreenshot(after);
            yield return new WaitForSecondsRealtime(.3f);
            try { Activate(ui.Root, "CorrectionCompare"); } // the button (it also refreshes the inspector and the chip)
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            ScreenCapture.CaptureScreenshot(before);
            yield return new WaitForSecondsRealtime(.3f);
            try
            {
                var chipShown = ui.Root.Q<Label>("CorrectionCompareChip").resolvedStyle.display == DisplayStyle.Flex;
                Activate(ui.Root, "CorrectionCompare");
                Check("correction_inspector_screenshots_created", File.Exists(after) && File.Exists(before) && chipShown && ui.Scene.CorrectionsShown);
                ui.View.Set(previousView.Pivot, previousView.Yaw, previousView.Pitch, previousView.Distance, previousView.Roll); ui.View.Apply(Camera.main);
                if (previousTarget != MmdSceneWorkspace.InspectorTarget.Object) ui.SelectTarget(previousTarget);
                else if (previousId != null && ui.Scene.Objects.Any(o => o.Id == previousId)) ui.Select(previousId);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }

        // ----- v0.61 mouse through the Input System device layer -----
        // The other viewport checks call the router directly. Here a virtual mouse and keyboard get
        // Input System state events and the workspace reads them in its own Update (ReadDevices),
        // one frame per step: button bits, position and delta, wheel units and Shift.
        private IEnumerator DeviceInputChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a, MmdSceneService.SceneObject b, Action<string> fail)
        {
            // Without focus the workspace ignores device input by design (a drag ends on focus loss).
            if (!Application.isFocused) { checks.Add("device_input_checks_skipped_window_not_focused"); yield break; }
            UnityEngine.InputSystem.Mouse mouse = null; UnityEngine.InputSystem.Keyboard keyboard = null;
            var state = new UnityEngine.InputSystem.LowLevel.MouseState();
            var view = ui.View; var at = Vector2.zero;
            void Send(Vector2 position, bool left = false, bool right = false, float scroll = 0)
            {
                state.delta = position - at; state.position = position; at = position; state.buttons = 0;
                if (left) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
                if (right) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
                state.scroll = new Vector2(0, scroll);
                mouse.MakeCurrent(); keyboard.MakeCurrent();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
            }
            void Keys(params UnityEngine.InputSystem.Key[] held)
            {
                keyboard.MakeCurrent();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(held));
            }
            var rect = Camera.main.pixelRect;
            Vector2 aPoint = default, bPoint = default, centre = default, empty = default;
            var results = new List<string>(); var ok = true;
            void Result(string name, bool passed) { results.Add(name + "=" + passed); ok &= passed; }
            try
            {
                mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("ProbeMouse");
                keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("ProbeKeyboard");
                Activate(ui.Root, "FrameAll");
                Activate(ui.Root, "object-" + a.Id);
                Vector2 On(MmdSceneService.SceneObject item)
                {
                    MmdSceneWorkspace.TryBounds(new[] { item }, out var bounds);
                    foreach (var world in new[] { BodyPoint(item), bounds.center, bounds.center + Vector3.up * .3f })
                    {
                        var screen = Camera.main.WorldToScreenPoint(world); var point = new Vector2(screen.x, screen.y);
                        if (screen.z > 0 && ui.RaycastScene(point).Item == item && ui.GizmoHandleAt(point) < 0) return point;
                    }
                    throw new InvalidOperationException("No free point on " + Path.GetFileNameWithoutExtension(item.ModelSource));
                }
                bPoint = On(b); aPoint = On(a);
                centre = rect.center; for (var i = 0; i < 10 && ui.GizmoHandleAt(centre) >= 0; i++) centre += new Vector2(0, 40);
                var corner = new[] { new Vector2(rect.xMin + 8, rect.yMax - 8), new Vector2(rect.xMax - 8, rect.yMax - 8), new Vector2(rect.xMin + 8, rect.yMin + 30) }
                    .Where(p => ui.PointerInViewport(p) && ui.RaycastScene(p).Item == null).ToArray();
                if (corner.Length == 0) throw new InvalidOperationException("No empty viewport point for the device wheel step");
                empty = corner[0];
                at = bPoint; Send(bPoint); Keys();
                ui.DeviceInput = true;
            }
            catch (Exception ex) { ui.DeviceInput = false; fail(ex.ToString()); yield break; }
            yield return null;
            // 1. A click (press and release on the same spot) selects the character under the cursor.
            Send(bPoint, left: true); yield return null;
            Send(bPoint); yield return null;
            Result("click_selects", ui.SelectedId == b.Id);
            // 2. A left drag that starts on the other character orbits, catching up past the slop; the selection stays.
            var before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            if (ui.GizmoHandleAt(aPoint) >= 0) { ui.DeviceInput = false; fail("device drag would start on the gizmo"); yield break; }
            Send(aPoint); yield return null;
            Send(aPoint, left: true); yield return null;
            Send(aPoint + new Vector2(3, 0), left: true); yield return null;
            var still = (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before;
            Send(aPoint + new Vector2(12, 0), left: true); yield return null;
            Send(aPoint + new Vector2(24, 0), left: true); yield return null;
            Send(aPoint + new Vector2(24, 0)); yield return null;
            Result("left_drag_orbits_keeping_selection", still && Near(view.Yaw, Mathf.Repeat(before.Yaw + 24 * MmdViewportCamera.DegreesPerPixel, 360), .01f) &&
                Near(view.Distance, before.Distance) && ui.SelectedId == b.Id);
            // 3. Right drag up comes closer by the dolly rate; the pivot and the lens stay.
            var distance = view.Distance; var pivot = view.Pivot; var fov = Camera.main.fieldOfView;
            Send(centre); yield return null;
            Send(centre, right: true); yield return null;
            Send(centre + new Vector2(0, 60), right: true); yield return null;
            Send(centre + new Vector2(0, 60)); yield return null;
            Result("right_drag_dollies", Near(view.Distance, distance * Mathf.Exp(-60f / Camera.main.pixelHeight * MmdViewportCamera.DollyPerViewport)) &&
                Near(view.Pivot, pivot) && Camera.main.fieldOfView == fov);
            // 4. Shift + right drag pans: pivot and camera move together, distance and angles stay.
            distance = view.Distance; pivot = view.Pivot; var yaw = view.Yaw;
            Keys(UnityEngine.InputSystem.Key.LeftShift); Send(centre); yield return null;
            Send(centre, right: true); yield return null;
            Send(centre + new Vector2(30, 15), right: true); yield return null;
            Send(centre + new Vector2(30, 15)); Keys(); yield return null;
            Result("shift_right_drag_pans", (view.Pivot - pivot).magnitude > 1e-4f && Near(view.Distance, distance) && Near(view.Yaw, yaw));
            // 5. One wheel notch (Windows reports 120 units) over empty space: toward the pivot.
            distance = view.Distance; pivot = view.Pivot;
            Send(empty); yield return null;
            Send(empty, scroll: 120); yield return null;
            Send(empty); yield return null;
            Result("wheel_notch_zooms", Near(view.Distance, distance * MmdViewportCamera.ZoomPerNotch) && Near(view.Pivot, pivot) && !ui.LastZoomHitSurface);
            // 6. F frames the selected character.
            Keys(UnityEngine.InputSystem.Key.F); yield return null;
            Keys(); yield return null;
            MmdSceneWorkspace.TryBounds(new[] { b }, out var bBounds);
            Result("f_frames_selection", ui.SafeViewportRect().Contains((Vector2)Camera.main.WorldToScreenPoint(bBounds.center)) && CameraMatchesView(ui));
            try
            {
                ui.DeviceInput = false;
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                Activate(ui.Root, "FrameAll");
                Activate(ui.Root, "object-" + a.Id); // the next check expects the first character selected
                Check("device_level_mouse_and_keys_follow_v061_rules [" + string.Join(" ", results) + "]", ok);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }

        // ----- H3 output -----
        private static double Sq(int value) => value * (double)value;
        private static Texture2D LoadPng(string file)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(file));
            return texture;
        }
        private static bool NoTemporaryFiles(string directory) =>
            !Directory.GetFileSystemEntries(directory).Any(entry => Path.GetFileName(entry).StartsWith(".") && entry.EndsWith(".tmp"));
        private IEnumerator OutputChecks(MmdSceneWorkspace ui, string path, string sceneFile, Action<string> fail)
        {
            var dir = Path.Combine(Path.GetDirectoryName(path), "output");
            var small = new OutputData { preset = OutputData.Custom, width = 640, height = 360, fps = 30, guides = true };
            var savedGraphics = ui.Graphics.Settings;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                // 1. Integer frame timing.
                var t = MmdOutputTiming.TicksPerSecond;
                var long30 = MmdOutputTiming.FrameCount(0, MmdOutputTiming.ToTicks(1800), 60);
                var last = MmdOutputTiming.FrameSeconds(MmdOutputTiming.ToTicks(1.25), 599, 60);
                Check("output_frame_timing_uses_integer_ticks", MmdOutputTiming.FrameCount(0, MmdOutputTiming.ToTicks(10), 60) == 600 &&
                    MmdOutputTiming.FrameCount(MmdOutputTiming.ToTicks(.5), MmdOutputTiming.ToTicks(1), 30) == 15 &&
                    MmdOutputTiming.FrameCount(0, t / 3, 60) == 20 && MmdOutputTiming.FrameCount(0, t / 3 + 1, 60) == 21 && long30 == 108000 &&
                    Math.Abs(MmdOutputTiming.FrameSeconds(0, 107999, 60) - 107999 / 60.0) < 1e-9 && Math.Abs(last - (1.25 + 599 / 60.0)) < 1e-12 &&
                    MmdOutputTiming.AudioSample(0, 1, 60, 44100) == 735 && MmdOutputTiming.AudioSample(MmdOutputTiming.ToTicks(.5), 0, 30, 48000) == 24000);
                // Deterministic frames for the comparisons: no SSAO blue noise.
                var plain = ui.Graphics.Settings; plain.ssaoEnabled = false; ui.Graphics.Apply(plain);
                ui.Scene.Seek(.75);
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, Path.Combine(dir, "frame-a.png"));
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            Screen.SetResolution(1600, 900, false);
            for (var i = 0; i < 6; i++) yield return null;
            try { ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, Path.Combine(dir, "frame-b.png")); }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            Screen.SetResolution(1280, 720, false);
            for (var i = 0; i < 6; i++) yield return null;
            byte[] readOnlyBytes = null; var readOnly = Path.Combine(dir, "locked.png");
            try
            {
                var a = LoadPng(Path.Combine(dir, "frame-a.png")); var b = LoadPng(Path.Combine(dir, "frame-b.png"));
                var pa = a.GetPixels32(); var pb = b.GetPixels32();
                var maxDiff = pa.Zip(pb, (x, y) => Math.Max(Math.Abs(x.r - y.r), Math.Max(Math.Abs(x.g - y.g), Math.Abs(x.b - y.b)))).Max();
                var opaque = pa.All(p => p.a == 255);
                Check("png_capture_size_opaque_ui_free_and_window_independent [max channel diff " + maxDiff + "]", a.width == 640 && a.height == 360 && opaque &&
                    maxDiff <= 3 && Math.Abs(ui.Scene.Seconds - .75) < 1e-9 && ui.Error.Length == 0 && NoTemporaryFiles(dir));
                Destroy(a); Destroy(b);
                // A write that cannot complete leaves the existing file as it was.
                File.WriteAllBytes(readOnly, new byte[] { 1, 2, 3 });
                File.SetAttributes(readOnly, FileAttributes.ReadOnly); readOnlyBytes = File.ReadAllBytes(readOnly);
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, readOnly);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            var mp4 = Path.Combine(dir, "clip.mp4");
            try
            {
                var lockedKept = ui.Error.Contains("출력하지 못했습니다") && File.ReadAllBytes(readOnly).SequenceEqual(readOnlyBytes) && NoTemporaryFiles(dir);
                File.SetAttributes(readOnly, FileAttributes.Normal);
                if (!lockedKept) throw new InvalidOperationException("png_write_failure_keeps_existing_file [error='" + ui.Error + "']");
                // 2. MP4: one second at 30 fps, then half a second at 60 fps.
                var summary = ui.OutputSummary(MmdSceneWorkspace.OutputFormat.Mp4, small, 0, 1);
                if (!summary.Contains("프레임 30개")) throw new InvalidOperationException("estimate shown before output: " + summary);
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, small, 0, 1, mp4);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            var mp4b = Path.Combine(dir, "clip60.mp4");
            MmdMediaWriter.ProbeInfo first = default;
            try
            {
                if (ui.Error.Length > 0) throw new InvalidOperationException("mp4 output failed: " + ui.Error);
                first = MmdMediaWriter.Probe(mp4);
                var sixty = small.Clone(); sixty.fps = 60;
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, sixty, .5, 1, mp4b);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            // The encoded frame 15 of the 30 fps clip against a PNG of the same scene time.
            try { ui.Scene.Seek(.5); ui.StartOutput(MmdSceneWorkspace.OutputFormat.Png, small, 0, 0, Path.Combine(dir, "frame-15.png")); }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                ui.Scene.Seek(.75);
                var decoded = MmdMediaWriter.ReadVideoFrame(mp4, 15, 640, 360);
                var reference = LoadPng(Path.Combine(dir, "frame-15.png")).GetPixels32(); // bottom-up, like the decoded frame
                double mse = 0, flipped = 0;
                for (var y = 0; y < 360; y++)
                    for (var x = 0; x < 640; x++)
                    {
                        var r = reference[y * 640 + x]; var d = (y * 640 + x) * 4; var f = ((359 - y) * 640 + x) * 4;
                        mse += Sq(decoded[d + 2] - r.r) + Sq(decoded[d + 1] - r.g) + Sq(decoded[d] - r.b);
                        flipped += Sq(decoded[f + 2] - r.r) + Sq(decoded[f + 1] - r.g) + Sq(decoded[f] - r.b);
                    }
                double Psnr(double total) => 10 * Math.Log10(255.0 * 255 / Math.Max(1e-9, total / (640 * 360 * 3)));
                var psnr = Psnr(mse); var psnrFlipped = Psnr(flipped);
                Check("mp4_frame_matches_rendered_frame [PSNR " + psnr.ToString("0.0") + " dB, upside down " + psnrFlipped.ToString("0.0") + " dB]", psnr > 30 && psnr > psnrFlipped + 3);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            var clicks = Path.Combine(dir, "clicks.wav");
            try
            {
                var second = MmdMediaWriter.Probe(mp4b);
                bool Ok(MmdMediaWriter.ProbeInfo p, int frames, int fps) => p.VideoCodecName == "H264" && p.width == 640 && p.height == 360 && p.videoFrames == frames &&
                    p.fpsNum == fps && p.fpsDen == 1 && Math.Abs(p.videoDuration - frames * 10000000L / fps) <= 10000000L / fps && p.firstVideoTime == 0;
                Check("mp4_frame_count_duration_size_and_codec [30fps: " + first.videoFrames + " frames " + (first.videoDuration / 1e7).ToString("0.000") + " s " + first.VideoCodecName +
                    " audio " + first.AudioCodecName + "; 60fps: " + second.videoFrames + " frames " + (second.videoDuration / 1e7).ToString("0.000") + " s]",
                    Ok(first, 30, 30) && Ok(second, 30, 60) && first.AudioCodecName == "AAC" && NoTemporaryFiles(dir));
                // 3. Audio sync: clicks at known scene times, measured in the decoded MP4 audio.
                const int rate = 48000;
                var samples = new float[rate * 3];
                foreach (var at in new[] { .25, 1.5, 2.75 })
                    for (var i = 0; i < rate / 100; i++) samples[(int)(at * rate) + i] = .8f * Mathf.Sin(2 * Mathf.PI * 1000 * i / rate);
                MmdWavReader.Write(clicks, samples, 1, rate);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(clicks, MmdSceneWorkspace.PickKind.Audio);
            var synced = Path.Combine(dir, "sync.mp4");
            try
            {
                if (ui.Scene.Audio == null || !ui.Scene.Audio.Source.EndsWith("clicks.wav")) throw new InvalidOperationException("click track not connected: " + ui.Error);
                ui.Scene.AudioVolume = 1; ui.Scene.AudioMuted = false;
                var sync = small.Clone(); sync.fps = 60;
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, sync, 0, 3, synced);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            var cancelTarget = Path.Combine(dir, "existing.mp4");
            try
            {
                var info = MmdMediaWriter.Probe(synced);
                var pcm = MmdMediaWriter.ReadAudio(synced, 48000 * 4, out var decodedRate, out var channels);
                var onsets = new List<double>();
                var threshold = (short)(.3f * 32767);
                for (var i = 0; i < pcm.Length / channels; i++)
                    if (Math.Abs(pcm[i * channels]) > threshold && (onsets.Count == 0 || i / (double)decodedRate - onsets[onsets.Count - 1] > .5))
                        onsets.Add(i / (double)decodedRate + info.firstAudioTime / 1e7);
                var expected = new[] { .25, 1.5, 2.75 };
                var errors = onsets.Count == 3 ? onsets.Zip(expected, (o, e) => Math.Abs(o - e)).ToArray() : new[] { 1.0 };
                var frame = 1.0 / 60;
                Check("mp4_audio_sync_within_one_frame [onsets " + string.Join(", ", onsets.Select(o => o.ToString("0.0000"))) + " s, error ms " +
                    string.Join("/", errors.Select(e => (e * 1000).ToString("0.0"))) + ", audio " + (info.audioDuration / 1e7).ToString("0.000") + " s video " + (info.videoDuration / 1e7).ToString("0.000") + " s, " + decodedRate + " Hz]",
                    info.videoFrames == 180 && errors.All(e => e < frame) && Math.Abs(info.audioDuration - info.videoDuration) / 1e7 < frame && info.sampleRate == 48000);
                // 4. Cancel half way: the existing file is untouched and nothing is left behind.
                File.WriteAllBytes(cancelTarget, new byte[] { 9, 8, 7 });
                var longer = small.Clone(); longer.fps = 60;
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, longer, 0, 3, cancelTarget);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            var waited = 0;
            while (ui.Busy && !ui.Root.Q<Label>("LoadingLabel").text.Contains("/180") && waited++ < 600) yield return null;
            try { Activate(ui.Root, "CancelLoading"); }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            while (ui.Busy) yield return null;
            try
            {
                var cancelledOk = ui.LastOutputCancelled && File.ReadAllBytes(cancelTarget).SequenceEqual(new byte[] { 9, 8, 7 }) && NoTemporaryFiles(dir) &&
                    Math.Abs(ui.Scene.Seconds - .75) < 1e-9 && !ui.Busy;
                // Missing folder: reported, nothing written.
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, small, 0, 1, Path.Combine(dir, "missing-folder", "x.mp4"));
                if (!cancelledOk) throw new InvalidOperationException("mp4_cancel_keeps_existing_file [cancelled=" + ui.LastOutputCancelled + " seconds=" + ui.Scene.Seconds + "]");
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                Check("output_cancel_and_failure_leave_existing_files_and_no_temporaries", ui.Error.Contains("출력 폴더가 없습니다") && !Directory.Exists(Path.Combine(dir, "missing-folder")) && NoTemporaryFiles(dir));
                // 5. Portrait guide and framing inside it; landscape hides it.
                var portrait = new OutputData { preset = OutputData.Portrait, fps = 60, guides = true };
                ui.SetOutput(portrait);
                var guide = ui.OutputGuideScreenRect();
                var shown = ui.OutputGuideVisible && Math.Abs(guide.width - Screen.height * 9f / 16) < 1 && Math.Abs(guide.center.x - Screen.width * .5f) < 1;
                Activate(ui.Root, "FrameAll");
                MmdSceneWorkspace.TryBounds(ui.Scene.Objects.Where(o => o.Visible), out var all);
                var inside = BoundsInside(all, Rect.MinMaxRect(guide.xMin, 0, guide.xMax, Screen.height));
                var square = new OutputData { preset = OutputData.Square, guides = true }; ui.SetOutput(square);
                var squareShown = ui.OutputGuideVisible && Math.Abs(ui.OutputGuideScreenRect().width - Screen.height) < 1;
                ui.SetOutput(new OutputData());
                Check("portrait_and_square_guides_and_framing_inside_output [shown=" + shown + " inside=" + inside + " square=" + squareShown + "]",
                    shown && inside && squareShown && !ui.OutputGuideVisible);
                // 6. Transparent PNG sequence: characters only, straight alpha.
                var sequence = new OutputData { preset = OutputData.Custom, width = 480, height = 270, fps = 30 };
                ui.StartOutput(MmdSceneWorkspace.OutputFormat.PngSequence, sequence, .5, .6, Path.Combine(dir, "alpha.png"));
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                var files = Enumerable.Range(0, 3).Select(i => Path.Combine(dir, "alpha_" + i.ToString("D6") + ".png")).ToArray();
                var ok = files.All(File.Exists) && !File.Exists(Path.Combine(dir, "alpha_000003.png")) && NoTemporaryFiles(dir);
                var texture = ok ? LoadPng(files[1]) : null;
                var pixels = texture != null ? texture.GetPixels32() : new Color32[0];
                var clear = pixels.Count(p => p.a == 0); var solid = pixels.Count(p => p.a == 255);
                var corners = texture != null && new[] { pixels[0], pixels[texture.width - 1], pixels[pixels.Length - 1], pixels[pixels.Length - texture.width] }.All(p => p.a == 0);
                var stageBack = ui.Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Stage).All(o => o.Model.root.GetComponentsInChildren<Renderer>(true).All(r => r.enabled == o.Visible));
                Check("transparent_png_sequence_characters_only_with_alpha [" + (texture == null ? "missing" : texture.width + "x" + texture.height) + ", clear " + clear + ", solid " + solid + "]",
                    ok && texture.width == 480 && texture.height == 270 && corners && solid > 50 && clear > pixels.Length / 2 && stageBack &&
                    Camera.main.backgroundColor.a > .99f && Camera.main.allowHDR);
                if (texture != null) Destroy(texture);
                ui.Graphics.Apply(savedGraphics);
                // 7. The toolbar button and dialog (scripted file dialog).
                Activate(ui.Root, "Output");
                var dialogShown = ui.DialogVisible && ui.Root.Q<Label>("OutputSummary").text.Contains("프레임");
                ui.Root.Q<DropdownField>("OutputFormat").value = "PNG 이미지 (현재 프레임)";
                ui.Root.Q<DropdownField>("OutputPreset").value = "정사각 1080×1080";
                if (!dialogShown) throw new InvalidOperationException("output dialog not shown");
                var dialogs = new ScriptedDialogs { Next = Path.Combine(dir, "dialog.png") }; outputDialogs = dialogs;
                ui.FileDialogs = dialogs;
                Activate(ui.Root, "OutputStart");
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                var texture = File.Exists(Path.Combine(dir, "dialog.png")) ? LoadPng(Path.Combine(dir, "dialog.png")) : null;
                Check("output_dialog_asks_for_the_file_and_writes_it", outputDialogs.Asked == "SaveOutput:png" && texture != null && texture.width == 1080 && texture.height == 1080 &&
                    ui.Output.preset == OutputData.Square && !ui.DialogVisible);
                if (texture != null) Destroy(texture);
                // Saved with the scene (checked again on relaunch).
                if (!ui.SaveTo(sceneFile)) throw new InvalidOperationException("save with output settings failed: " + ui.Error);
                Check("output_settings_saved_with_scene", MmdSceneDocument.Read(sceneFile).output.preset == OutputData.Square);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }
        private ScriptedDialogs outputDialogs;
        // ----- D-01 beat-based automatic camera -----
        // Synthetic dance loop at a known tempo: kick on every beat (accented on the downbeat),
        // snare on 2 and 4, hi-hat on the off-beats, low noise. Returns the true beat times.
        private static float[] DanceLoop(int rate, double seconds, double bpm, double offset, int seed, out List<double> truth)
        {
            var random = new System.Random(seed);
            var samples = new float[(int)(rate * seconds)];
            for (var i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() * 2 - 1) * .02f;
            truth = new List<double>();
            var period = 60 / bpm;
            for (var k = 0; offset + k * period < seconds - .3; k++)
            {
                var at = offset + k * period; truth.Add(at);
                var start = (int)(at * rate);
                var accent = k % 4 == 0 ? .9f : .6f;
                for (var i = 0; i < rate * .25 && start + i < samples.Length; i++)
                {
                    var t = i / (double)rate;
                    samples[start + i] += accent * (float)(Math.Sin(2 * Math.PI * (60 - 40 * t) * t) * Math.Exp(-t / .12));
                    if (k % 2 == 1) samples[start + i] += .3f * (float)((random.NextDouble() * 2 - 1) * Math.Exp(-t / .08));
                }
                var hat = (int)((at + period / 2) * rate);
                for (var i = 0; i < rate * .05 && hat + i < samples.Length; i++)
                    samples[hat + i] += .15f * (float)((random.NextDouble() * 2 - 1) * Math.Exp(-i / (rate * .02)));
            }
            for (var i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i], -1, 1);
            return samples;
        }
        private IEnumerator AutoCameraChecks(MmdSceneWorkspace ui, string path, Action<string> fail)
        {
            var dir = Path.Combine(Path.GetDirectoryName(path), "autocam");
            var music = Path.Combine(dir, "dance-128.wav");
            List<double> truth128 = null;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                // 1. Known tempos: BPM, beat phase and bar lines.
                var details = new List<string>(); var ok = true;
                foreach (var (bpm, offset, seed) in new[] { (128.0, .37, 1), (96.0, .12, 2), (150.0, .05, 3) })
                {
                    var samples = DanceLoop(44100, 20, bpm, offset, seed, out var truth);
                    var info = MmdBeatAnalyzer.Analyze(samples, 1, 44100);
                    var phase = info.Beats.Select(b => truth.Min(t => Math.Abs(t - b))).Where(e => e < .25).DefaultIfEmpty(1).Average();
                    var downbeats = truth.Where((t, k) => k % 4 == 0).ToList();
                    // Bar lines must sit on beats; which beat is "1" (downbeat) is reported, not required.
                    var onBeats = info.Bars.Count(x => truth.Any(t => Math.Abs(t - x) < .03)) / (double)Math.Max(1, info.Bars.Length);
                    var barsOk = info.Bars.Count(x => downbeats.Any(d => Math.Abs(d - x) < .03)) / (double)Math.Max(1, info.Bars.Length);
                    ok &= Math.Abs(info.Bpm - bpm) < .5 && phase < .015 && onBeats >= .9;
                    details.Add(bpm + ": " + info.Bpm.ToString("0.00") + " BPM, phase " + (phase * 1000).ToString("0.0") + " ms, bar lines on beats " + (onBeats * 100).ToString("0") +
                        "%, on downbeats " + (barsOk * 100).ToString("0") + "%, confidence " + info.Confidence.ToString("0.0"));
                    if (bpm == 128) { truth128 = truth; MmdWavReader.Write(music, samples, 1, 44100); }
                }
                Check("beat_analysis_on_known_tempos [" + string.Join(" | ", details) + "]", ok);
                // 2. The real song beside the dance VMD (BPM reported for the user to confirm).
                var song = Directory.GetFiles(Path.GetDirectoryName(PocArguments.Value("-pocVmd") ?? ""), "*.wav").FirstOrDefault();
                var songDetail = "no song";
                var songOk = true;
                if (song != null)
                {
                    var wav = MmdWavReader.Read(song);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var info = MmdBeatAnalyzer.Analyze(wav.Samples, wav.Channels, wav.Frequency);
                    songOk = info.Bpm >= 60 && info.Bpm <= 200 && info.Beats.Length > 100;
                    songDetail = Path.GetFileName(song) + ": " + info.Bpm.ToString("0.00") + " BPM, " + info.Beats.Length + " beats, " + info.Bars.Length + " bars, confidence " +
                        info.Confidence.ToString("0.0") + ", " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s for " + wav.DurationSeconds.ToString("0") + " s";
                }
                Check("beat_analysis_on_real_song [" + songDetail + "]", songOk);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(music, MmdSceneWorkspace.PickKind.Audio);
            MmdSceneService.SceneObject target = null;
            try
            {
                target = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character);
                Activate(ui.Root, "explorer-camera");
                var dropdown = ui.Root.Q<DropdownField>("AutoCameraTarget"); dropdown.value = dropdown.choices[1];
                ui.Root.Q<DropdownField>("AutoCameraCut").value = "2마디 (보통)";
                ui.Root.Q<IntegerField>("AutoCameraSeed").value = 7;
                ui.Root.Q<Slider>("AutoCameraShake").value = .3f;
                Activate(ui.Root, "AutoCameraGenerate");
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            byte[] first = null; string file = null;
            try
            {
                file = ui.Scene.CameraMotion?.Source;
                var result = ui.LastAutoResult;
                if (ui.Error.Length > 0 || result == null) throw new InvalidOperationException("auto camera failed: " + ui.Error);
                var beatFrames = truth128.Select(t => (long)Math.Round(t * 30)).ToArray();
                var onBeat = result.CutFrames.Count(f => beatFrames.Any(b => Math.Abs(b - f) <= 1)) / (double)Math.Max(1, result.CutFrames.Count);
                Check("auto_camera_cuts_on_beats [" + result.CutFrames.Count + " cuts, " + (onBeat * 100).ToString("0") + "% within one frame of a beat, BPM " + ui.LastBeats.Bpm.ToString("0.00") +
                    ", shots " + string.Join("", result.Shots.Select(s => s.shot.ToString()[0])) + "]", result.CutFrames.Count >= 4 && onBeat >= .95 && ui.CameraFollowing &&
                    file != null && file.EndsWith(".autocam-7.vmd") && ui.UndoLabel == "카메라 VMD 연결" && ui.LastAutoCamera.target == target.Id);
                first = File.ReadAllBytes(file);
                Activate(ui.Root, "AutoCameraGenerate");
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            byte[] again = null;
            try
            {
                again = File.ReadAllBytes(file);
                ui.Root.Q<IntegerField>("AutoCameraSeed").value = 8;
                Activate(ui.Root, "AutoCameraGenerate");
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            try
            {
                var other = File.ReadAllBytes(ui.Scene.CameraMotion.Source);
                var umtReads = false;
                try { using (new DisposableCamera(MmdPocCamera.Load(ui.Scene.CameraMotion.Source))) umtReads = true; } catch (Exception) { umtReads = false; }
                Check("auto_camera_same_seed_same_vmd_and_standard_format [" + first.Length + " bytes]", first.SequenceEqual(again) && !first.SequenceEqual(other) &&
                    ui.Scene.CameraMotion.Source.EndsWith(".autocam-8.vmd") && umtReads);
                // The character's head stays in a 16:9 frame through the whole song.
                var motion = ui.Scene.CameraMotion; var inside = 0; var samples = 0; var seconds = ui.Scene.Seconds;
                for (var t = 0.0; t < Math.Min(ui.Scene.Duration, ui.LastBeats.DurationSeconds) - .1; t += .25)
                {
                    ui.Scene.Seek(t);
                    var points = target.Model.bones.Where(x => x != null).Select(x => x.position).ToArray();
                    var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
                    var head = new Vector3((min.x + max.x) * .5f, max.y - (max.y - min.y) * .06f, (min.z + max.z) * .5f);
                    var pose = motion.Evaluate(t);
                    var local = Quaternion.Inverse(pose.Rotation) * (head - pose.Position);
                    var tanV = Mathf.Tan(pose.FieldOfView * .5f * Mathf.Deg2Rad); var tanH = tanV * 16 / 9f;
                    if (local.z > 0 && Math.Abs(local.x / (local.z * tanH)) <= 1 && Math.Abs(local.y / (local.z * tanV)) <= 1) inside++;
                    samples++;
                }
                ui.Scene.Seek(seconds);
                Check("auto_camera_keeps_the_character_in_frame [head inside " + inside + "/" + samples + "]", samples > 40 && inside >= samples * .98);
                if (!ui.SaveTo(Path.Combine(dir, "autocam.mmdscene"))) throw new InvalidOperationException("auto camera scene save failed: " + ui.Error);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(dir, "autocam.mmdscene"));
            try
            {
                var stored = MmdSceneDocument.Read(Path.Combine(dir, "autocam.mmdscene")).cameraMotion;
                Check("auto_camera_inputs_and_seed_saved_and_restored [stored " + stored.generator.version + "/" + stored.generator.seed + "/" + stored.generator.bpm + "]",
                    stored.generator.version == AutoCameraSettings.Version && stored.generator.seed == 8 && stored.generator.cutBars == 2 &&
                    Math.Abs(stored.generator.bpm - 128) < .5 && stored.generator.audio == "dance-128.wav" && stored.path.EndsWith("scene.autocam-8.vmd") &&
                    ui.LastAutoCamera != null && ui.LastAutoCamera.seed == 8 && ui.Root.Q<IntegerField>("AutoCameraSeed").value == 8 && ui.Scene.CameraMotion != null);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }
        // ----- D-03 motion fit diagnosis -----
        // A small VMD keying semi-standard bones, an unknown bone and three morphs (names in Shift-JIS).
        private static void WriteFitFixture(string path)
        {
            var bones = new[]
            {
                new byte[] { 0x83, 0x4F, 0x83, 0x8B, 0x81, 0x5B, 0x83, 0x75 },             // グルーブ
                new byte[] { 0x8F, 0xE3, 0x94, 0xBC, 0x90, 0x67, 0x32 },                   // 上半身2
                new byte[] { 0x8D, 0xB6, 0x98, 0x72, 0x9D, 0x80 },                         // 左腕捩
                new byte[] { 0x83, 0x5A, 0x83, 0x93, 0x83, 0x5E, 0x81, 0x5B },             // センター
                new byte[] { 0x91, 0xB6, 0x8D, 0xDD, 0x82, 0xB5, 0x82, 0xC8, 0x82, 0xA2, 0x8D, 0x9C }, // 存在しない骨
            };
            var morphs = new[]
            {
                new byte[] { 0x82, 0xA0 },                                                 // あ
                new byte[] { 0x82, 0xDC, 0x82, 0xCE, 0x82, 0xBD, 0x82, 0xAB },             // まばたき
                new byte[] { 0x82, 0xC8, 0x82, 0xA2, 0x83, 0x82, 0x81, 0x5B, 0x83, 0x74 }, // ないモーフ
            };
            byte[] Name(byte[] name) { var fixedName = new byte[15]; Array.Copy(name, fixedName, name.Length); return fixedName; }
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("Vocaloid Motion Data 0002".PadRight(30, '\0')));
                writer.Write(new byte[20]);
                writer.Write((uint)(bones.Length * 2));
                foreach (var bone in bones)
                    foreach (var frame in new uint[] { 0, 30 })
                    {
                        writer.Write(Name(bone)); writer.Write(frame);
                        writer.Write(0f); writer.Write(frame == 0 ? 0f : .5f); writer.Write(0f);
                        writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
                        var interpolation = new byte[64];
                        for (var i = 0; i < 64; i++) interpolation[i] = (byte)(i % 16 < 8 ? 20 : 107);
                        writer.Write(interpolation);
                    }
                writer.Write((uint)(morphs.Length * 2));
                foreach (var morph in morphs)
                    foreach (var frame in new uint[] { 0, 30 }) { writer.Write(Name(morph)); writer.Write(frame); writer.Write(frame == 0 ? 0f : 1f); }
                writer.Write(0u); writer.Write(0u); writer.Write(0u);
            }
        }
        private IEnumerator MotionFitChecks(MmdSceneWorkspace ui, string path, Action<string> fail)
        {
            var fixture = Path.Combine(Path.GetDirectoryName(path), "fit-fixture.vmd");
            MmdSceneService.SceneObject target = null;
            try
            {
                WriteFitFixture(fixture);
                target = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character);
                ui.Select(target.Id);
            }
            catch (Exception ex) { fail(ex.ToString()); yield break; }
            yield return ui.ApplyPickerResult(fixture, MmdSceneWorkspace.PickKind.Motion);
            try
            {
                var modelBones = new HashSet<string>(target.Model.model.bones.Select(b => b.originalName.ToString()));
                var modelMorphs = new HashSet<string>(target.Model.model.morphs.Select(m => m.originalName.ToString()));
                var text = ui.Root.Q<Label>("MotionFitInfo").text;
                var expectedBones = new[] { "グルーブ", "上半身2", "左腕捩", "センター", "存在しない骨" }.Where(b => !modelBones.Contains(b)).ToArray();
                var semiShown = expectedBones.Where(b => b != "存在しない骨" && b != "センター").All(b => text.Contains(b));
                var symptoms = (!expectedBones.Contains("グルーブ") || text.Contains("상하 움직임")) && (!expectedBones.Contains("左腕捩") || text.Contains("팔 비틀림"));
                var unknown = text.Contains("그 밖의 본") && text.Contains("存在しない骨");
                var morphCount = new[] { "あ", "まばたき", "ないモーフ" }.Count(m => !modelMorphs.Contains(m));
                var morphs = text.Contains("모프 " + morphCount + "개 없음");
                var summary = target.Motion.Summary;
                Check("motion_fit_diagnosis_in_user_words [missing bones " + string.Join("/", summary.missingBones) + ", morphs " + string.Join("/", summary.missingMorphs) + "]",
                    semiShown && symptoms && unknown && morphs && summary.missingBones.SequenceEqual(expectedBones.OrderBy(b => b, StringComparer.Ordinal)));
                // The real dance motion against both test models (reported for docs/D03_MOTION_FIT.md).
                var vmd = PocArguments.Value("-pocVmd");
                var details = new List<string>();
                if (vmd != null && File.Exists(vmd))
                {
                    UMT.VMDAnimation animation;
                    using (var stream = File.OpenRead(vmd)) animation = UMT.VMDReader.Read(stream);
                    try
                    {
                        var used = new HashSet<string>(animation.boneFrames.Select(f => f.boneName.ToString()));
                        var usedMorphs = new HashSet<string>(animation.morphFrames.Select(f => f.morphName.ToString()));
                        foreach (var item in ui.Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character))
                        {
                            var bones = item.Model.model.bones.Select(b => b.originalName.ToString()).ToArray();
                            var report = MmdMotionFit.Diagnose(used.Where(b => !bones.Contains(b)).OrderBy(b => b, StringComparer.Ordinal).ToArray(),
                                usedMorphs.Where(m => !item.Model.model.morphs.Any(x => x.originalName.ToString() == m)).ToArray(), bones);
                            details.Add(Path.GetFileNameWithoutExtension(item.ModelSource) + ": motion bones " + used.Count + ", missing " + report.Bones.Count +
                                " (" + string.Join(",", report.Bones.Select(b => b.Name)) + "), morphs missing " + report.MissingMorphs + "/" + usedMorphs.Count +
                                ", model lacks semi-standard " + string.Join(",", report.ModelLacksSemiStandard));
                        }
                    }
                    finally { Destroy(animation); }
                }
                Debug.Log("[motion-fit] " + string.Join(" | ", details));
                Check("motion_fit_report_for_real_motion [" + string.Join(" | ", details) + "]", details.Count >= 1 || vmd == null);
            }
            catch (Exception ex) { fail(ex.ToString()); }
        }
        // ----- H2a-05 look presets -----
        private static void SaveRendered(Camera camera, string file)
        {
            const int width = 960, height = 540;
            var texture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousRect = camera.rect; var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                File.WriteAllBytes(file, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive; camera.targetTexture = previousTarget; camera.rect = previousRect;
                RenderTexture.ReleaseTemporary(texture); Destroy(pixels);
            }
        }
        private void LookChecks(MmdSceneWorkspace ui, string reportDirectory)
        {
            var graphics = ui.Graphics; var camera = Camera.main;
            var dropdown = ui.Root.Q<DropdownField>("LookPreset");
            var state = ui.Root.Q<Label>("LookState");
            var bright = MmdLookPresets.BuiltIn[0];
            var before = JsonUtility.ToJson(graphics.Settings);
            var characters = ui.Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character).ToArray();
            var stylesBefore = characters.Select(o => o.MaterialStyle.style).ToArray();
            var fov = camera.fieldOfView; var quality = graphics.Settings.effectQuality;
            var listed = MmdLookPresets.BuiltIn.Length >= 3 && MmdLookPresets.BuiltIn.All(p => dropdown.choices.Contains(p.name));
            dropdown.value = bright.name;
            var applied = MmdLookPresets.SameLook(bright.values, graphics.Settings) && graphics.Settings.lookPreset == bright.name &&
                state.text.Contains("적용됨") && Math.Abs(camera.fieldOfView - fov) < 1e-4f && graphics.Settings.effectQuality == quality &&
                Math.Abs(graphics.Sun.intensity - bright.values.lightIntensity) < 1e-4f && graphics.Ssao.isActive == bright.values.ssaoEnabled &&
                characters.All(o => o.MaterialStyle.style == MaterialStyleData.Toon); // H2b-01: presets give characters the toon style
            ui.Undo();
            var undone = JsonUtility.ToJson(graphics.Settings) == before && characters.Select(o => o.MaterialStyle.style).SequenceEqual(stylesBefore);
            ui.Redo();
            Check("look_presets_listed_and_applied_in_one_undo_step [listed=" + listed + " applied=" + applied + " undone=" + undone + "]",
                listed && applied && undone && graphics.Settings.lookPreset == bright.name && ui.UndoLabel == "룩 프리셋");

            ui.Root.Q<Slider>("Exposure").value = 1.3f;
            var modified = state.text.Contains("(수정됨)") && ui.Root.Q<Button>("LookReapply").enabledSelf;
            Activate(ui.Root, "LookReapply");
            Check("editing_a_look_value_marks_modified_and_reapply_restores", modified && MmdLookPresets.SameLook(bright.values, graphics.Settings) &&
                state.text.Contains("적용됨") && !ui.Root.Q<Button>("LookReapply").enabledSelf);

            // User presets: saved to the settings file, built-in names and blank names rejected, deleted.
            var presetFile = Path.Combine(reportDirectory, "look-presets.json");
            if (File.Exists(presetFile)) File.Delete(presetFile);
            ui.LookPresetFilePath = presetFile; ui.LoadLooks();
            ui.Root.Q<Slider>("Saturation").value = -40;
            var name = ui.Root.Q<TextField>("LookName");
            name.value = "  " + bright.name + " "; Activate(ui.Root, "LookSave");
            var builtInRejected = ui.Error.Contains("기본 프리셋") && ui.UserLookPresets.Count == 0;
            name.value = "   "; Activate(ui.Root, "LookSave");
            var blankRejected = ui.Error.Contains("입력") && ui.UserLookPresets.Count == 0;
            name.value = "내 무대"; Activate(ui.Root, "LookSave");
            var saved = ui.UserLookPresets.Count == 1 && graphics.Settings.lookPreset == "내 무대" && File.Exists(presetFile) &&
                File.ReadAllText(presetFile).Contains("내 무대") && dropdown.choices.Contains("내 무대") && state.text.Contains("적용됨");
            ui.LoadLooks();
            var reloaded = ui.UserLookPresets.Count == 1 && Math.Abs(ui.UserLookPresets[0].values.saturation + 40) < 1e-4f;
            // A scene keeps its values when the preset changes later.
            var sceneValues = graphics.Settings;
            ui.Root.Q<Slider>("Saturation").value = 30;
            name.value = "내 무대"; Activate(ui.Root, "LookSave"); // overwrite the preset with saturation 30
            ui.ApplyGraphics(sceneValues);                        // what opening the scene file applies
            var kept = Math.Abs(graphics.Settings.saturation + 40) < 1e-4f && graphics.Settings.lookPreset == "내 무대" && state.text.Contains("(수정됨)") &&
                Math.Abs(ui.UserLookPresets[0].values.saturation - 30) < 1e-4f;
            dropdown.value = "내 무대";
            Activate(ui.Root, "LookDelete");
            var deleted = ui.UserLookPresets.Count == 0 && graphics.Settings.lookPreset == "" && Math.Abs(graphics.Settings.saturation + 40) < 1e-4f &&
                !File.ReadAllText(presetFile).Contains("내 무대") && !dropdown.choices.Contains("내 무대");
            Check("user_look_presets_saved_overwritten_deleted_and_scene_keeps_values [builtIn=" + builtInRejected + " blank=" + blankRejected + " saved=" + saved +
                " reloaded=" + reloaded + " kept=" + kept + " deleted=" + deleted + "]", builtInRejected && blankRejected && saved && reloaded && kept && deleted);
            ui.LookPresetFilePath = null;

            // Reference frames for each built-in preset (3D only, saved for the user to confirm).
            var luminance = new List<float>();
            for (var i = 0; i < MmdLookPresets.BuiltIn.Length; i++)
            {
                dropdown.value = MmdLookPresets.BuiltIn[i].name;
                ui.UpdateDofFocus();
                SaveRendered(camera, Path.Combine(reportDirectory, "look-" + i + ".png"));
                luminance.Add(RenderedLuminance(camera));
            }
            var distinct = luminance.Zip(luminance.Skip(1), (x, y) => Math.Abs(x - y)).All(d => d > 5);
            Check("look_preset_reference_frames_rendered_and_distinct [" + string.Join(", ", luminance.Select(v => v.ToString("0.0"))) + "]",
                distinct && Enumerable.Range(0, MmdLookPresets.BuiltIn.Length).All(i => File.Exists(Path.Combine(reportDirectory, "look-" + i + ".png"))));
        }
        private static bool Near(Vector3 x, Vector3 y, float tolerance = 1e-3f) => (x - y).magnitude <= tolerance;
        private static bool Near(float x, float y, float tolerance = 1e-3f) => Math.Abs(x - y) <= tolerance;
        private static bool CameraMatchesView(MmdSceneWorkspace ui) =>
            Near(Camera.main.transform.position, ui.View.Position) && Quaternion.Angle(Camera.main.transform.rotation, ui.View.Rotation) < .01f;
        private static bool BoundsInside(Bounds bounds, Rect area)
        {
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
                foreach (var y in new[] { bounds.min.y, bounds.max.y })
                    foreach (var z in new[] { bounds.min.z, bounds.max.z })
                    {
                        var point = Camera.main.WorldToScreenPoint(new Vector3(x, y, z));
                        if (point.z <= 0 || !area.Contains(new Vector2(point.x, point.y))) return false;
                    }
            return true;
        }
        // dolly: right button without Shift (v0.61; it orbited before). Left drag orbits.
        private static void Frame(MmdSceneWorkspace ui, Vector2 screen, Vector2 delta, float wheel = 0, bool dolly = false, bool pan = false, bool frameKey = false, bool left = false, bool focusLost = false) =>
            ui.ProcessViewportInput(screen, delta, wheel, dolly, pan, frameKey, left, focusLost: focusLost);
        private static void Click(MmdSceneWorkspace ui, Vector2 screen)
        {
            Frame(ui, screen, Vector2.zero, left: true);
            Frame(ui, screen, Vector2.zero);
        }
        private static void Key(MmdSceneWorkspace ui, bool play = false, bool delete = false, bool cancel = false, bool save = false, int gizmo = 0, bool focus = false) =>
            ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, false, play, delete, cancel, save, gizmo, focus);
        // Left-button drag through the given screen points (press at the first, release at the last).
        private static void LeftDrag(MmdSceneWorkspace ui, params Vector2[] points)
        {
            Frame(ui, points[0], Vector2.zero, left: true);
            for (var i = 1; i < points.Length; i++) Frame(ui, points[i], points[i] - points[i - 1], left: true);
            Frame(ui, points[points.Length - 1], Vector2.zero);
        }
        private void GizmoChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a)
        {
            var previousSelection = ui.SelectedId;
            Activate(ui.Root, "object-" + a.Id);
            var origin = ui.GizmoOriginScreen();
            Check("gizmo_shown_at_selected_origin_in_move_mode", ui.GizmoVisible && ui.Gizmo == MmdSceneWorkspace.GizmoMode.Move &&
                (origin - ScreenOf(a.Placement.position)).magnitude < .5f && ui.Root.Q<Button>("GizmoMove").ClassListContains("eds-gizmo-button--active"));

            Vector2 Along(int axis, float fraction) { var o = ui.GizmoOriginScreen(); return o + (ui.GizmoAxisEndScreen(axis) - o) * fraction; }
            var start = a.Placement.localPosition; var selected = ui.SelectedId;
            var grab = Along(0, .7f); var direction = (ui.GizmoAxisEndScreen(0) - origin).normalized;
            if (ui.GizmoHandleAt(grab) != 0) throw new InvalidOperationException("X move handle not under its own projection");
            LeftDrag(ui, grab, grab + direction * 20, grab + direction * 40);
            var moved = a.Placement.localPosition;
            Check("gizmo_move_x_follows_pointer_and_syncs_inspector", moved.x > start.x + 1e-4f && Mathf.Abs(moved.y - start.y) < 1e-5f && Mathf.Abs(moved.z - start.z) < 1e-5f &&
                (ScreenOf(a.Placement.position) - (origin + direction * 40)).magnitude < 3 && ui.SelectedId == selected && ui.Dirty &&
                Near(ui.Root.Q<Vector3Field>("Position").value, moved, 1e-3f));

            origin = ui.GizmoOriginScreen(); start = a.Placement.localPosition;
            grab = Along(1, .7f); direction = (ui.GizmoAxisEndScreen(1) - origin).normalized;
            if (ui.GizmoHandleAt(grab) != 1) throw new InvalidOperationException("Y move handle not under its own projection");
            LeftDrag(ui, grab, grab + direction * 25);
            moved = a.Placement.localPosition;
            Check("gizmo_move_y_changes_only_height", moved.y > start.y + 1e-4f && Mathf.Abs(moved.x - start.x) < 1e-5f && Mathf.Abs(moved.z - start.z) < 1e-5f);

            Key(ui, gizmo: 2);
            var ringStart = ui.GizmoRingScreen(1, Mathf.PI * .75f);
            var rotationStart = a.Placement.localRotation;
            var rotateMode = ui.Gizmo == MmdSceneWorkspace.GizmoMode.Rotate && ui.Root.Q<Button>("GizmoRotate").ClassListContains("eds-gizmo-button--active");
            if (ui.GizmoHandleAt(ringStart) != 1) throw new InvalidOperationException("Y ring not under its own projection");
            LeftDrag(ui, ringStart, ui.GizmoRingScreen(1, Mathf.PI * .85f), ui.GizmoRingScreen(1, Mathf.PI * .95f));
            (a.Placement.localRotation * Quaternion.Inverse(rotationStart)).ToAngleAxis(out var angle, out var axis);
            Check("gizmo_rotate_y_ring_turns_about_up_axis", rotateMode && angle > 5 && Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > .999f &&
                Quaternion.Angle(Quaternion.Euler(ui.Root.Q<Vector3Field>("Rotation").value), a.Placement.localRotation) < .05f);

            Key(ui, gizmo: 3);
            var scaleStart = a.Placement.localScale; origin = ui.GizmoOriginScreen();
            if (ui.GizmoHandleAt(origin) != MmdSceneWorkspace.UniformHandle) throw new InvalidOperationException("Uniform scale handle not at origin");
            LeftDrag(ui, origin, origin + new Vector2(15, 15), origin + new Vector2(30, 30));
            var scaled = a.Placement.localScale; var ratio = scaled.x / scaleStart.x;
            Check("gizmo_uniform_scale_keeps_proportions", ui.Gizmo == MmdSceneWorkspace.GizmoMode.Scale && ratio > 1.2f &&
                Mathf.Abs(scaled.y / scaleStart.y - ratio) < 1e-3f && Mathf.Abs(scaled.z / scaleStart.z - ratio) < 1e-3f &&
                Near(ui.Root.Q<Vector3Field>("Scale").value, scaled, 1e-3f));
            scaleStart = a.Placement.localScale; origin = ui.GizmoOriginScreen();
            grab = Along(1, .8f); direction = (ui.GizmoAxisEndScreen(1) - origin).normalized;
            if (ui.GizmoHandleAt(grab) != 1) throw new InvalidOperationException("Y scale handle not under its own projection");
            LeftDrag(ui, grab, grab + direction * 20);
            scaled = a.Placement.localScale;
            Check("gizmo_axis_scale_changes_one_component", scaled.y > scaleStart.y * 1.05f && Mathf.Abs(scaled.x - scaleStart.x) < 1e-5f && Mathf.Abs(scaled.z - scaleStart.z) < 1e-5f);

            ui.Root.Q<Vector3Field>("Position").value = new Vector3(-1.5f, 0, .4f);
            Check("inspector_edit_moves_gizmo", (ui.GizmoOriginScreen() - ScreenOf(a.Placement.position)).magnitude < .5f && a.Placement.localPosition == new Vector3(-1.5f, 0, .4f));

            ui.Root.Q<Vector3Field>("Position").Q<FloatField>().Focus();
            Key(ui, gizmo: 1);
            var stayed = ui.Gizmo == MmdSceneWorkspace.GizmoMode.Scale;
            (ui.Root.panel.focusController.focusedElement as VisualElement)?.Blur();
            Key(ui, gizmo: 1);
            Check("gizmo_keys_switch_mode_except_while_typing", stayed && ui.Gizmo == MmdSceneWorkspace.GizmoMode.Move);

            ui.Root.Q<Toggle>("Visible").value = false;
            var hidden = !ui.GizmoVisible && ui.GizmoHandleAt(ui.GizmoOriginScreen()) < 0;
            ui.Root.Q<Toggle>("Visible").value = true;
            Activate(ui.Root, "explorer-light");
            var light = !ui.GizmoVisible;
            Activate(ui.Root, "object-" + a.Id);
            Check("gizmo_absent_for_hidden_object_and_light", hidden && light && ui.GizmoVisible);

            // Restore the placement the later persistence checks expect.
            ui.Root.Q<Vector3Field>("Position").value = new Vector3(-1, 0, .2f);
            ui.Root.Q<Vector3Field>("Rotation").value = new Vector3(0, 20, 0);
            ui.Root.Q<Vector3Field>("Scale").value = new Vector3(.9f, .9f, .9f);
            if (a.Placement.localPosition != new Vector3(-1, 0, .2f) || a.Placement.localScale != new Vector3(.9f, .9f, .9f))
                throw new InvalidOperationException("Could not restore the placement after gizmo checks");
            if (previousSelection != null) ui.Select(previousSelection);
        }
        private static MmdSceneService.SceneObject Find(MmdSceneWorkspace ui, MmdSceneService.ObjectKind kind, string model) =>
            ui.Scene.Objects.FirstOrDefault(item => item.Kind == kind && string.Equals(item.ModelSource, model, StringComparison.OrdinalIgnoreCase));
        private static void RemoveSelected(MmdSceneWorkspace ui)
        {
            Activate(ui.Root, "RemoveModel");
            if (!ui.ConfirmVisible) throw new InvalidOperationException("Delete confirmation did not open");
            Activate(ui.Root, "ConfirmRemove");
        }
        private static Vector2 ScreenOf(Vector3 world)
        {
            var point = Camera.main.WorldToScreenPoint(world);
            if (point.z <= 0) throw new InvalidOperationException("Point is behind the camera");
            return new Vector2(point.x, point.y);
        }
        // A point inside the character, found from Unity's own bone transforms (independent of the
        // picking code's mesh baking): the upper-body bone, else the renderer bounds centre.
        private static Vector3 BodyPoint(MmdSceneService.SceneObject item)
        {
            var bone = item.Model.root.GetComponentsInChildren<Transform>().FirstOrDefault(part => part.name == "上半身");
            if (bone != null) return bone.position;
            MmdSceneWorkspace.TryBounds(new[] { item }, out var bounds); return bounds.center;
        }
        private static bool RayTouchesBounds(Vector2 screen, IEnumerable<MmdSceneService.SceneObject> items)
        {
            var ray = Camera.main.ScreenPointToRay(screen);
            return items.SelectMany(item => item.Model.root.GetComponentsInChildren<Renderer>()).Any(renderer => renderer.bounds.IntersectRay(ray));
        }
        // URP post-processing components expose IsActive() (IPostProcessComponent); others
        // (e.g. lighting options) are not screen effects.
        private static bool IsActiveEffect(VolumeComponent component)
        {
            var method = component.GetType().GetMethod("IsActive", Type.EmptyTypes);
            return method != null && method.ReturnType == typeof(bool) && (bool)method.Invoke(component, null);
        }
        private static float RenderedLuminance(Camera camera)
        {
            const int width = 160, height = 90;
            var texture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousRect = camera.rect; var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                return (float)pixels.GetPixels32().Average(p => .2126 * p.r + .7152 * p.g + .0722 * p.b);
            }
            finally
            {
                RenderTexture.active = previousActive; camera.targetTexture = previousTarget; camera.rect = previousRect;
                RenderTexture.ReleaseTemporary(texture); Destroy(pixels);
            }
        }
        private static bool Near(Color x, Color y) => Math.Abs(x.r - y.r) < 1e-3f && Math.Abs(x.g - y.g) < 1e-3f && Math.Abs(x.b - y.b) < 1e-3f;
        private void GraphicsChecks(MmdSceneWorkspace ui)
        {
            var graphics = ui.Graphics; var camera = Camera.main; var defaults = new SceneGraphicsSettings();
            var pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            // H2a-01: with the app's defaults, no post-processing effect is active anywhere in the
            // camera's resolved volume stack (project defaults are neutral, nothing overridden).
            RenderedLuminance(camera); // make sure the stack was resolved for this camera
            var activeEffects = VolumeManager.instance.baseComponentTypeArray
                .Select(type => VolumeManager.instance.stack.GetComponent(type))
                .Where(component => component != null && component.active && !(component is ColorCurves) && IsActiveEffect(component))
                .Select(component => component.GetType().Name).ToList();
            // ColorCurves always reports active (it is baked into the colour LUT); neutral means
            // identity curves.
            var curves = VolumeManager.instance.stack.GetComponent<ColorCurves>();
            foreach (var x in new[] { .1f, .5f, .9f })
                if (curves != null && new[] { curves.master.value, curves.red.value, curves.green.value, curves.blue.value }.Any(curve => Math.Abs(curve.Evaluate(x) - x) > 1e-3f))
                { activeEffects.Add("ColorCurves(non-identity)"); break; }
            Check("graphics_defaults_applied_with_neutral_post_processing" + (activeEffects.Count == 0 ? "" : " [active: " + string.Join(", ", activeEffects) + "]"),
                activeEffects.Count == 0 && camera.GetUniversalAdditionalCameraData().renderPostProcessing &&
                graphics.Volume.isGlobal && graphics.Volume.priority >= 1000 && Near(camera.fieldOfView, defaults.fieldOfView) &&
                Near(graphics.Sun.intensity, defaults.lightIntensity) && graphics.Sun.shadows == LightShadows.Soft && Near(camera.backgroundColor, defaults.backgroundColor) &&
                Near(graphics.Bloom.intensity.value, 0) && graphics.Tonemap.mode.value == TonemappingMode.None &&
                graphics.Dof.mode.value == DepthOfFieldMode.Off && Near(graphics.Vignette.intensity.value, 0) &&
                graphics.Ssao != null && !graphics.Ssao.isActive && graphics.Volume.sharedProfile.components.Count == 5);

            var previousSelection = ui.SelectedId;
            bool Shown(string name) => ui.Root.Q(name).style.display.value != DisplayStyle.None;
            Activate(ui.Root, "explorer-light");
            var light = ui.Target == MmdSceneWorkspace.InspectorTarget.Light && ui.SelectedId == null && !ui.HighlightVisible &&
                Shown("LightSection") && !Shown("CameraSection") && !Shown("TransformSection") && Shown("GraphicsSection") &&
                ui.Root.Q<Button>("explorer-light").ClassListContains("eds-row--selected");
            Activate(ui.Root, "explorer-camera");
            Check("explorer_light_and_camera_entries_show_their_sections", light && ui.Target == MmdSceneWorkspace.InspectorTarget.Camera &&
                Shown("CameraSection") && !Shown("LightSection") && ui.Root.Q<Label>("SelectionTitle").text == "카메라");

            ui.Root.Q<Slider>("Fov").value = 60;
            Check("fov_slider_sets_camera_field_of_view", Near(camera.fieldOfView, 60) && Near(graphics.Settings.fieldOfView, 60));
            var invalid = graphics.Settings; invalid.exposure = 1; invalid.fieldOfView = 500;
            var threw = false;
            try { graphics.Apply(invalid); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check("invalid_graphics_settings_rejected_atomically", threw && Near(camera.fieldOfView, 60) && Near(graphics.Colour.postExposure.value, 0) && Near(graphics.Settings.exposure, 0));

            ui.Root.Q<Slider>("LightR").value = .5f; ui.Root.Q<Slider>("LightIntensity").value = 3;
            ui.Root.Q<Slider>("LightYaw").value = 45; ui.Root.Q<Slider>("LightPitch").value = 60;
            Check("light_sliders_set_colour_intensity_and_direction", Near(graphics.Sun.color.r, .5f) && Near(graphics.Sun.color.g, 1) && Near(graphics.Sun.intensity, 3) &&
                Quaternion.Angle(graphics.Sun.transform.rotation, Quaternion.Euler(60, 45, 0)) < .01f);

            var dropdown = ui.Root.Q<DropdownField>("ShadowQuality");
            dropdown.value = "높음 (4096)";
            var high = graphics.Sun.shadows == LightShadows.Soft && pipeline != null && pipeline.mainLightShadowmapResolution == 4096 && graphics.Settings.shadowQuality == MmdShadowQuality.High;
            dropdown.value = "끔";
            var off = graphics.Sun.shadows == LightShadows.None && graphics.Settings.shadowQuality == MmdShadowQuality.Off;
            dropdown.value = "보통 (2048)";
            Check("shadow_quality_dropdown_maps_light_and_shadowmap", high && off && graphics.Sun.shadows == LightShadows.Soft && pipeline.mainLightShadowmapResolution == 2048);

            ui.Root.Q<Toggle>("Bloom").value = true; ui.Root.Q<Slider>("BloomIntensity").value = 2;
            ui.Root.Q<Toggle>("Tonemapping").value = true;
            ui.Root.Q<Slider>("Contrast").value = 20; ui.Root.Q<Slider>("Saturation").value = -30;
            var effectsOn = Near(graphics.Bloom.intensity.value, 2) && graphics.Tonemap.mode.value == TonemappingMode.Neutral &&
                Near(graphics.Colour.contrast.value, 20) && Near(graphics.Colour.saturation.value, -30);
            ui.Root.Q<Toggle>("Bloom").value = false;
            Check("effect_controls_drive_volume_overrides", effectsOn && graphics.Bloom.active && Near(graphics.Bloom.intensity.value, 0) && Near(graphics.Settings.bloomIntensity, 2));

            ui.Root.Q<Toggle>("Tonemapping").value = false; ui.Root.Q<Slider>("Contrast").value = 0; ui.Root.Q<Slider>("Saturation").value = 0;
            ui.Root.Q<Slider>("Exposure").value = 0;
            // A dark clear colour keeps the brighter frame from clipping at white on the light default.
            ui.Root.Q<Slider>("BackgroundR").value = .08f; ui.Root.Q<Slider>("BackgroundG").value = .1f; ui.Root.Q<Slider>("BackgroundB").value = .14f;
            var dark = RenderedLuminance(camera);
            ui.Root.Q<Slider>("Exposure").value = 1.5f;
            var bright = RenderedLuminance(camera);
            Debug.Log("[graphics-probe] luminance exposure0=" + dark + " exposure1.5=" + bright);
            Check("exposure_brightens_actual_rendered_frame", Near(graphics.Colour.postExposure.value, 1.5f) && dark > 1 && bright > dark * 1.3f);
            // H2a-01: the app's effects survive shader variant stripping (visible in the Player's
            // rendered frame, not only in the volume values).
            // Bloom acts on HDR colour before exposure: a white background exceeds its threshold, and
            // exposure -2 keeps the result from clipping. (The frame's brightness is mostly background.)
            ui.Root.Q<Slider>("Exposure").value = -2;
            ui.Root.Q<Slider>("BackgroundR").value = 1; ui.Root.Q<Slider>("BackgroundG").value = 1; ui.Root.Q<Slider>("BackgroundB").value = 1;
            var litPlain = RenderedLuminance(camera);
            ui.Root.Q<Toggle>("Bloom").value = true; ui.Root.Q<Slider>("BloomIntensity").value = 5;
            var bloomed = RenderedLuminance(camera);
            ui.Root.Q<Toggle>("Bloom").value = false; ui.Root.Q<Slider>("BloomIntensity").value = 2;
            ui.Root.Q<Slider>("Exposure").value = 1.5f;
            ui.Root.Q<Slider>("BackgroundR").value = .08f; ui.Root.Q<Slider>("BackgroundG").value = .1f; ui.Root.Q<Slider>("BackgroundB").value = .14f;
            ui.Root.Q<Toggle>("Tonemapping").value = true;
            var toned = RenderedLuminance(camera);
            ui.Root.Q<Toggle>("Tonemapping").value = false;
            var plain = RenderedLuminance(camera);
            Debug.Log("[graphics-probe] luminance plain=" + plain + " tonemapped=" + toned + " white=" + litPlain + " white+bloom5=" + bloomed);
            Check("bloom_and_tonemapping_change_rendered_frame" + " [white=" + litPlain + " bloom=" + bloomed + " plain=" + plain + " toned=" + toned + "]",
                bloomed > litPlain + .5f && Math.Abs(toned - plain) > .5f && Math.Abs(plain - bright) < .5f);

            ui.Root.Q<Slider>("BackgroundR").value = .3f; ui.Root.Q<Slider>("BackgroundG").value = .2f; ui.Root.Q<Slider>("BackgroundB").value = .1f;
            Check("background_colour_sliders_set_camera_clear", Near(camera.backgroundColor, new Color(.3f, .2f, .1f)) && camera.clearFlags == CameraClearFlags.SolidColor);
            EffectChecks(ui);
            LookChecks(ui, reportDirectory);

            var view = ui.View;
            Activate(ui.Root, "SaveView");
            var saved = (view.Pivot, view.Yaw, view.Pitch, view.Distance);
            view.Orbit(new Vector2(120, 40)); view.Apply(camera); ui.Root.Q<Slider>("Fov").value = 30;
            Activate(ui.Root, "view-0");
            Check("saved_view_restores_angle_distance_and_fov", graphics.Settings.savedViews.Count == 1 && Near(view.Pivot, saved.Pivot) && Near(view.Yaw, saved.Yaw) &&
                Near(view.Pitch, saved.Pitch) && Near(view.Distance, saved.Distance) && Near(camera.fieldOfView, 60) && CameraMatchesView(ui));
            Activate(ui.Root, "view-delete-0");
            Check("saved_view_delete_removes_entry", graphics.Settings.savedViews.Count == 0 && ui.Root.Q<Button>("view-0") == null);

            view.Orbit(new Vector2(-80, 30)); view.Apply(camera);
            Activate(ui.Root, "ResetView");
            MmdSceneWorkspace.TryBounds(ui.Scene.Objects.Where(item => item.Visible), out var all);
            Check("view_reset_restores_default_angle_and_frames_all", Near(view.Yaw, MmdViewportCamera.DefaultYaw) && Near(view.Pitch, MmdViewportCamera.DefaultPitch) &&
                ui.SafeViewportRect().Contains((Vector2)camera.WorldToScreenPoint(all.center)) && CameraMatchesView(ui));

            // Presentation defaults for the screenshots: default light/background, Bloom and tonemapping
            // on; H2a-02 SSAO, vignette and DOF on the first character (saved and checked on relaunch).
            var focusCharacter = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character);
            ui.ApplyGraphics(new SceneGraphicsSettings { bloomEnabled = true, tonemapping = true, ssaoEnabled = true, vignetteEnabled = true,
                dofEnabled = true, dofTarget = focusCharacter.Id, dofStrength = .3f });
            Check("controls_reflect_whole_state_apply", ui.Error.Length == 0 && ui.Root.Q<Toggle>("Bloom").value && ui.Root.Q<Toggle>("Tonemapping").value &&
                ui.Root.Q<Toggle>("Ssao").value && ui.Root.Q<Toggle>("Dof").value && ui.Root.Q<DropdownField>("DofTarget").index == 1 &&
                Near(ui.Root.Q<Slider>("Exposure").value, 0) && Near(ui.Root.Q<Slider>("Fov").value, defaults.fieldOfView) &&
                Near(ui.Root.Q<Slider>("LightIntensity").value, defaults.lightIntensity) && ui.Root.Q<DropdownField>("ShadowQuality").value == "보통 (2048)");
            if (previousSelection != null) ui.Select(previousSelection);
        }
        private static Color32[] RenderedPixels(Camera camera)
        {
            const int width = 320, height = 180;
            var texture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousRect = camera.rect; var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previousActive; camera.targetTexture = previousTarget; camera.rect = previousRect;
                RenderTexture.ReleaseTemporary(texture); Destroy(pixels);
            }
        }
        // Edge energy of the rendered frame: mean absolute luminance step between neighbouring pixels.
        private static float RenderedSharpness(Camera camera)
        {
            const int width = 320, height = 180;
            var texture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousRect = camera.rect; var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                var data = pixels.GetPixels32();
                float L(int x, int y) { var p = data[y * width + x]; return .2126f * p.r + .7152f * p.g + .0722f * p.b; }
                double sum = 0;
                for (var y = 0; y < height - 1; y++)
                    for (var x = 0; x < width - 1; x++) sum += Math.Abs(L(x + 1, y) - L(x, y)) + Math.Abs(L(x, y + 1) - L(x, y));
                return (float)(sum / ((width - 1) * (height - 1)));
            }
            finally
            {
                RenderTexture.active = previousActive; camera.targetTexture = previousTarget; camera.rect = previousRect;
                RenderTexture.ReleaseTemporary(texture); Destroy(pixels);
            }
        }
        // H2a-02: SSAO, depth of field, vignette, effect quality and the reset buttons, checked on
        // the rendered Player frame (the effects must survive shader stripping and reflection).
        private void EffectChecks(MmdSceneWorkspace ui)
        {
            var graphics = ui.Graphics; var camera = Camera.main;
            var a = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character);
            var view = ui.View; var savedView = (view.Pivot, view.Yaw, view.Pitch, view.Distance);
            ui.FrameIds(a.Id); // the character fills the frame, so per-pixel effects are measurable
            ui.Root.Q<Slider>("BackgroundR").value = .9f; ui.Root.Q<Slider>("BackgroundG").value = .9f; ui.Root.Q<Slider>("BackgroundB").value = .9f;
            ui.Root.Q<Slider>("Exposure").value = 0;
            var aoOff = RenderedLuminance(camera); var offPixels = RenderedPixels(camera);
            ui.Root.Q<Toggle>("Ssao").value = true; ui.Root.Q<Slider>("SsaoIntensity").value = 4; ui.Root.Q<Slider>("SsaoRadius").value = 1;
            var ssao = graphics.SsaoSettings;
            var aoOn = RenderedLuminance(camera); var onPixels = RenderedPixels(camera);
            // The characters are small in this framing: count the pixels the occlusion darkened.
            int darkened = 0, brightened = 0;
            for (var i = 0; i < offPixels.Length; i++)
            {
                var step = (onPixels[i].r + onPixels[i].g + onPixels[i].b) - (offPixels[i].r + offPixels[i].g + offPixels[i].b);
                if (step <= -9) darkened++; else if (step >= 9) brightened++;
            }
            var darkenedShare = darkened / (float)offPixels.Length;
            var ssaoOn = graphics.Ssao.isActive && Near((float)MmdSceneGraphics.SsaoField(ssao, "Intensity"), 4) && Near((float)MmdSceneGraphics.SsaoField(ssao, "Radius"), 1) &&
                (bool)MmdSceneGraphics.SsaoField(ssao, "AfterOpaque");
            ui.Root.Q<Toggle>("Ssao").value = false;
            var aoBack = RenderedLuminance(camera);
            Check("ssao_controls_drive_renderer_feature_and_darken_frame [off=" + aoOff.ToString("0.00") + " on=" + aoOn.ToString("0.00") + " back=" + aoBack.ToString("0.00") +
                " darkened " + (darkenedShare * 100).ToString("0.00") + "% brightened " + brightened + "]",
                ssaoOn && aoOn < aoOff && darkenedShare > .002f && brightened < darkened / 10 && !graphics.Ssao.isActive && Math.Abs(aoBack - aoOff) < .05f);

            var sharp = RenderedSharpness(camera);
            ui.Root.Q<Toggle>("Dof").value = true; ui.Root.Q<Slider>("DofDistance").value = .3f; ui.Root.Q<Slider>("DofStrength").value = 1;
            ui.UpdateDofFocus();
            var blurred = RenderedSharpness(camera);
            var near = graphics.Dof.mode.value == DepthOfFieldMode.Bokeh && Near(graphics.FocusDistance, .3f);
            var target = ui.Root.Q<DropdownField>("DofTarget");
            target.value = target.choices[1];
            ui.UpdateDofFocus();
            MmdSceneWorkspace.TryBounds(new[] { a }, out var bounds);
            var depth = Vector3.Dot(bounds.center - camera.transform.position, camera.transform.forward);
            var follows = graphics.Settings.dofTarget == a.Id && Near(graphics.FocusDistance, depth, 1e-3f);
            var focused = RenderedSharpness(camera);
            Check("dof_blurs_frame_and_focuses_on_target_character [sharp=" + sharp.ToString("0.00") + " blurred=" + blurred.ToString("0.00") + " focused=" + focused.ToString("0.00") +
                " near=" + near + " follows=" + follows + "]", near && follows && blurred < sharp * .8f && focused > blurred * 1.1f);

            var quality = ui.Root.Q<DropdownField>("EffectQuality");
            quality.value = "낮음";
            var low = graphics.Dof.mode.value == DepthOfFieldMode.Gaussian && (bool)MmdSceneGraphics.SsaoField(ssao, "Downsample") &&
                Convert.ToInt32(MmdSceneGraphics.SsaoField(ssao, "Samples")) == 2 && !graphics.Bloom.highQualityFiltering.value;
            quality.value = "높음";
            var high = graphics.Dof.mode.value == DepthOfFieldMode.Bokeh && !(bool)MmdSceneGraphics.SsaoField(ssao, "Downsample") &&
                Convert.ToInt32(MmdSceneGraphics.SsaoField(ssao, "Samples")) == 0 && graphics.Bloom.highQualityFiltering.value;
            quality.value = "보통";
            ui.Root.Q<Toggle>("Dof").value = false;
            var vignetteOff = RenderedLuminance(camera);
            ui.Root.Q<Toggle>("Vignette").value = true; ui.Root.Q<Slider>("VignetteIntensity").value = 1;
            var vignetteOn = RenderedLuminance(camera);
            Check("quality_preset_and_vignette_apply [vignette " + vignetteOff.ToString("0.0") + "->" + vignetteOn.ToString("0.0") + " low=" + low + " high=" + high + "]",
                low && high && graphics.Settings.effectQuality == MmdEffectQuality.Medium && Near(graphics.Vignette.intensity.value, 1) && vignetteOn < vignetteOff - 2);

            // Reset buttons: effects and light separately, both undoable.
            ui.Root.Q<Toggle>("Ssao").value = true; ui.Root.Q<Slider>("LightIntensity").value = 5;
            var before = JsonUtility.ToJson(graphics.Settings);
            Activate(ui.Root, "ResetEffects");
            var d = new SceneGraphicsSettings(); var e = graphics.Settings;
            var effectsReset = !e.ssaoEnabled && !e.vignetteEnabled && !e.dofEnabled && e.dofTarget == "" && Near(e.vignetteIntensity, d.vignetteIntensity) &&
                Near(e.ssaoIntensity, d.ssaoIntensity) && Near(e.exposure, 0) && Near(e.lightIntensity, 5) && Near(e.backgroundColor, new Color(.9f, .9f, .9f)) && !graphics.Ssao.isActive;
            ui.Undo();
            var undone = JsonUtility.ToJson(graphics.Settings) == before && graphics.Ssao.isActive;
            Activate(ui.Root, "ResetLight");
            var l = graphics.Settings;
            var lightReset = Near(l.lightIntensity, d.lightIntensity) && Near(l.lightYaw, d.lightYaw) && Near(l.lightPitch, d.lightPitch) && l.ssaoEnabled && Near(graphics.Sun.intensity, d.lightIntensity);
            ui.Undo();
            Check("reset_effects_and_light_are_separate_and_undoable [effects=" + effectsReset + " undone=" + undone + " light=" + lightReset + "]",
                effectsReset && undone && lightReset && JsonUtility.ToJson(graphics.Settings) == before);
            ui.Root.Q<Toggle>("Ssao").value = false; ui.Root.Q<Toggle>("Vignette").value = false;
            view.Set(savedView.Pivot, savedView.Yaw, savedView.Pitch, savedView.Distance); view.Apply(camera);
            ui.Root.Q<Slider>("BackgroundR").value = .3f; ui.Root.Q<Slider>("BackgroundG").value = .2f; ui.Root.Q<Slider>("BackgroundB").value = .1f;
        }
        private void PickingChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a, MmdSceneService.SceneObject b, MmdSceneService.SceneObject stage)
        {
            var view = ui.View; var rect = Camera.main.pixelRect;
            var characters = new[] { a, b };
            var bPoint = ScreenOf(BodyPoint(b)); var aPoint = ScreenOf(BodyPoint(a));
            Click(ui, bPoint);
            MmdSceneWorkspace.TryBounds(new[] { b }, out var bBounds);
            Check("viewport_click_selects_character_under_cursor", ui.PointerInViewport(bPoint) && ui.SelectedId == b.Id &&
                ui.Root.Q<Label>("SelectionTitle").text == "선택한 캐릭터" && ui.HighlightVisible && Near(ui.HighlightBounds.center, bBounds.center));

            // v0.61: a left drag that starts on another character orbits; its release does not pick.
            var before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            var leftOrbitYaw = Mathf.Repeat(view.Yaw + 24 * MmdViewportCamera.DegreesPerPixel, 360);
            if (ui.GizmoHandleAt(aPoint) >= 0) throw new InvalidOperationException("The left-drag check would start on the gizmo of the selected character");
            Frame(ui, aPoint, Vector2.zero, left: true);
            Frame(ui, aPoint + new Vector2(3, 0), new Vector2(3, 0), left: true);   // within the click slop: nothing yet
            var heldStill = (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before;
            Frame(ui, aPoint + new Vector2(12, 0), new Vector2(9, 0), left: true);  // beyond: orbit catches up 12 px
            Frame(ui, aPoint + new Vector2(24, 0), new Vector2(12, 0), left: true);
            Frame(ui, aPoint + new Vector2(24, 0), Vector2.zero);
            Check("viewport_left_drag_orbits_without_picking [yaw " + before.Yaw.ToString("0.00") + "->" + view.Yaw.ToString("0.00") + "]", heldStill && ui.SelectedId == b.Id &&
                Near(view.Yaw, leftOrbitYaw) && Near(view.Pitch, before.Pitch) && Near(view.Distance, before.Distance) && Near(view.Pivot, before.Pivot) && CameraMatchesView(ui) &&
                ui.HighlightVisible && Near(ui.HighlightBounds.center, bBounds.center, .05f));
            view.Set(before.Pivot, before.Yaw, before.Pitch, before.Distance); view.Apply(Camera.main);

            Vector2? floor = null;
            foreach (var x in new[] { 0f, -.5f, .5f, -2f, 2f })
                foreach (var z in new[] { 1.5f, 2.5f, .5f, -1f })
                {
                    if (floor != null) continue;
                    var world = stage.Placement.TransformPoint(new Vector3(x, 0, z));
                    var screen = Camera.main.WorldToScreenPoint(world);
                    var candidate = new Vector2(screen.x, screen.y);
                    if (screen.z > 0 && ui.PointerInViewport(candidate) && !RayTouchesBounds(candidate, characters) && ui.GizmoHandleAt(candidate) < 0) floor = candidate;
                }
            if (floor == null) throw new InvalidOperationException("No unobstructed stage floor point in the viewport");
            Click(ui, floor.Value);
            Check("viewport_click_on_floor_selects_stage", ui.SelectedId == stage.Id && ui.Root.Q<Label>("SelectionTitle").text == "선택한 배경");

            var sidebar = new Vector2(64, rect.center.y);
            Click(ui, sidebar);
            Check("left_click_over_panel_does_not_pick", ui.SelectedId == stage.Id);

            Vector2? empty = null;
            foreach (var corner in new[] { new Vector2(rect.xMin + 8, rect.yMax - 8), new Vector2(rect.xMax - 8, rect.yMax - 8), new Vector2(rect.center.x, rect.yMax - 8),
                                           new Vector2(rect.xMin + 8, rect.yMin + 30), new Vector2(rect.xMax - 8, rect.yMin + 30) })
                if (empty == null && ui.PointerInViewport(corner) && !RayTouchesBounds(corner, ui.Scene.Objects) && ui.GizmoHandleAt(corner) < 0) empty = corner;
            if (empty == null) throw new InvalidOperationException("No empty viewport point");
            Click(ui, empty.Value);
            Check("viewport_click_on_empty_space_clears_selection", ui.SelectedId == null && !ui.HighlightVisible &&
                !ui.Root.Q<Button>("ConnectMotion").enabledSelf && !ui.Root.Q<Button>("RemoveModel").enabledSelf);

            Activate(ui.Root, "object-" + a.Id); // leaves focus on a list button, as a mouse click would
            var playing = ui.Scene.Playing;
            Key(ui, play: true);
            var toggled = ui.Scene.Playing != playing;
            Key(ui, play: true);
            Check("space_toggles_shared_playback", toggled && ui.Scene.Playing == playing);

            ui.Root.Q<Vector3Field>("Position").Q<FloatField>().Focus();
            Key(ui, play: true); Key(ui, delete: true);
            Check("space_and_delete_ignored_while_text_field_focused", ui.Scene.Playing == playing && !ui.ConfirmVisible && ui.Scene.Objects.Count == 3);
            (ui.Root.panel.focusController.focusedElement as VisualElement)?.Blur();

            Click(ui, floor.Value);
            Key(ui, delete: true);
            Check("delete_key_opens_confirmation_without_removing", ui.ConfirmVisible && ui.Scene.Objects.Count == 3 &&
                ui.Root.Q<Label>("RemoveConfirmText").text.Contains(Path.GetFileNameWithoutExtension(stage.ModelSource)));

            before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            var centre = rect.center;
            Frame(ui, centre, Vector2.zero, dolly: true);
            Frame(ui, centre + new Vector2(0, 40), new Vector2(0, 40), dolly: true);
            Frame(ui, centre, Vector2.zero);
            LeftDrag(ui, centre, centre + new Vector2(40, 0));
            Frame(ui, centre, Vector2.zero, wheel: 2);
            Click(ui, bPoint);
            Key(ui, play: true);
            Check("confirmation_blocks_viewport_and_shortcuts", ui.ConfirmVisible && ui.PointerOverUi(centre) && (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before &&
                ui.SelectedId == stage.Id && ui.Scene.Playing == playing);

            Key(ui, cancel: true);
            Check("escape_cancels_delete_and_keeps_object", !ui.ConfirmVisible && ui.Scene.Objects.Count == 3 && ui.SelectedId == stage.Id && !ui.PointerOverUi(centre));
        }
        private void ViewportChecks(MmdSceneWorkspace ui, MmdSceneService.SceneObject a, MmdSceneService.SceneObject b)
        {
            var view = ui.View; var rect = Camera.main.pixelRect;
            var centre = rect.center;
            // Inside the camera rect 3 px below the toolbar. With an inverted Y conversion this
            // would map into the bottom playback/status panels and be reported as UI.
            var topEdge = new Vector2(rect.center.x, rect.yMax - 3);
            var sidebar = new Vector2(64, rect.center.y);
            var toolbar = new Vector2(rect.center.x, Screen.height - 38);
            Check("viewport_pick_distinguishes_panels_and_viewport",
                ui.PointerOverUi(sidebar) && ui.PointerOverUi(toolbar) && !ui.PointerOverUi(centre) && !ui.PointerOverUi(topEdge) &&
                ui.PointerInViewport(centre) && !ui.PointerInViewport(sidebar));

            MmdSceneWorkspace.TryBounds(ui.Scene.Objects.Where(item => item.Visible), out var all);
            Check("frame_all_targets_visible_bounds_and_applies_camera",
                view.Distance > all.extents.magnitude && CameraMatchesView(ui) &&
                ui.SafeViewportRect().Contains((Vector2)Camera.main.WorldToScreenPoint(all.center)));
            Check("frame_all_fits_visible_bounds_between_cards", BoundsInside(all, ui.SafeViewportRect()));

            // Left presses must not land on the selected character's gizmo handles (that would move it).
            Vector2 Free(Vector2 point) { for (var i = 0; i < 10 && ui.GizmoHandleAt(point) >= 0; i++) point += new Vector2(0, 40); return point; }
            var yaw = view.Yaw; var pitch = view.Pitch; var distance = view.Distance; var pivot = view.Pivot;
            var selectedBefore = ui.SelectedId;
            centre = Free(centre);
            Frame(ui, centre, Vector2.zero, left: true);                  // press (a click until it moves 5 px)
            Frame(ui, centre + new Vector2(40, -20), new Vector2(40, -20), left: true);
            Check("left_drag_in_viewport_rotates_around_fixed_pivot",
                Near(view.Yaw, Mathf.Repeat(yaw + 40 * MmdViewportCamera.DegreesPerPixel, 360)) && Near(view.Pitch, pitch + 20 * MmdViewportCamera.DegreesPerPixel) &&
                Near(view.Distance, distance) && Near(view.Pivot, pivot) && Near((Camera.main.transform.position - pivot).magnitude, distance) && CameraMatchesView(ui));
            // Continue the same drag across the left panel: capture keeps orbiting.
            yaw = view.Yaw;
            Frame(ui, sidebar, new Vector2(10, 0), left: true);
            Check("started_drag_continues_over_panel", Near(view.Yaw, Mathf.Repeat(yaw + 10 * MmdViewportCamera.DegreesPerPixel, 360)));
            Frame(ui, sidebar, Vector2.zero);                               // release
            Check("orbit_release_keeps_selection", ui.SelectedId == selectedBefore);

            // v0.61 right drag: up comes closer, down goes back; horizontal movement and the lens are untouched.
            var fov = Camera.main.fieldOfView; distance = view.Distance; pivot = view.Pivot; yaw = view.Yaw; pitch = view.Pitch;
            Frame(ui, centre, Vector2.zero, dolly: true);
            Frame(ui, centre + new Vector2(60, 0), new Vector2(60, 0), dolly: true);
            var sideways = Near(view.Distance, distance);
            Frame(ui, centre + new Vector2(60, 60), new Vector2(0, 60), dolly: true);
            var closer = view.Distance; var expected = distance * Mathf.Exp(-60f / Camera.main.pixelHeight * MmdViewportCamera.DollyPerViewport);
            Frame(ui, centre + new Vector2(60, 0), new Vector2(0, -60), dolly: true);
            Frame(ui, centre + new Vector2(60, 0), Vector2.zero);
            Check("right_drag_dollies_vertically_keeping_pivot_and_lens [" + distance.ToString("0.000") + "->" + closer.ToString("0.000") + "->" + view.Distance.ToString("0.000") + "]",
                sideways && Near(closer, expected) && closer < distance && Near(view.Distance, distance) && Near(view.Pivot, pivot) && Near(view.Yaw, yaw) && Near(view.Pitch, pitch) &&
                Camera.main.fieldOfView == fov && CameraMatchesView(ui));

            pivot = view.Pivot; var cameraBefore = Camera.main.transform.position; yaw = view.Yaw; pitch = view.Pitch;
            Frame(ui, centre, Vector2.zero, pan: true);
            Frame(ui, centre + new Vector2(30, 15), new Vector2(30, 15), pan: true);
            Frame(ui, centre, Vector2.zero);
            var moved = view.Pivot - pivot;
            Check("pan_drag_moves_pivot_and_camera_together",
                moved.magnitude > 1e-4f && Near(Camera.main.transform.position - cameraBefore, moved) && Near(view.Yaw, yaw) && Near(view.Pitch, pitch) &&
                Vector3.Dot(moved, Camera.main.transform.right) < 0 && Vector3.Dot(moved, Camera.main.transform.up) < 0);

            // Wheel over empty space: toward the pivot.
            Vector2? empty = null;
            foreach (var corner in new[] { new Vector2(rect.xMin + 8, rect.yMax - 8), new Vector2(rect.xMax - 8, rect.yMax - 8), new Vector2(rect.center.x, rect.yMax - 8),
                                           new Vector2(rect.xMin + 8, rect.yMin + 30), new Vector2(rect.xMax - 8, rect.yMin + 30) })
                if (empty == null && ui.PointerInViewport(corner) && ui.RaycastScene(corner).Item == null) empty = corner;
            if (empty == null) throw new InvalidOperationException("No empty viewport point for the wheel check");
            distance = view.Distance; pivot = view.Pivot;
            Frame(ui, empty.Value, Vector2.zero, wheel: 2);
            Check("wheel_over_empty_space_zooms_toward_pivot", Near(view.Distance, distance * MmdViewportCamera.ZoomPerNotch * MmdViewportCamera.ZoomPerNotch) &&
                Near(view.Pivot, pivot) && !ui.LastZoomHitSurface && CameraMatchesView(ui));

            // Wheel over a character: the surface point under the cursor keeps its screen position,
            // the camera keeps its direction and lens, the pivot moves to the point's depth.
            var bodyScreen = ScreenOf(BodyPoint(b));
            var hit = ui.RaycastScene(bodyScreen);
            if (hit.Item == null) throw new InvalidOperationException("No character surface under the body point");
            var queries = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) ui.RaycastScene(bodyScreen);
            var queryMs = queries.Elapsed.TotalMilliseconds / 20;
            var rotationBefore = Camera.main.transform.rotation; distance = view.Distance; fov = Camera.main.fieldOfView;
            var cameraToHit = (hit.Point - Camera.main.transform.position).magnitude;
            Frame(ui, bodyScreen, Vector2.zero, wheel: 2);
            var drift = ((Vector2)Camera.main.WorldToScreenPoint(hit.Point) - bodyScreen).magnitude;
            var approach = (hit.Point - Camera.main.transform.position).magnitude / cameraToHit;
            var depth = Vector3.Dot(hit.Point - Camera.main.transform.position, Camera.main.transform.forward);
            Check("wheel_over_surface_zooms_toward_point_under_cursor [drift " + drift.ToString("0.000") + " px, approach x" + approach.ToString("0.000") +
                ", surface query " + queryMs.ToString("0.00") + " ms with " + ui.Scene.Objects.Count + " objects]",
                ui.LastZoomHitSurface && drift < .5f && Near(approach, MmdViewportCamera.ZoomPerNotch * MmdViewportCamera.ZoomPerNotch, 1e-3f) &&
                Quaternion.Angle(rotationBefore, Camera.main.transform.rotation) < .01f && Camera.main.fieldOfView == fov && Near(view.Distance, depth, 1e-3f) && CameraMatchesView(ui) &&
                queryMs < 25);
            // F frames the selection again (a new pivot); FrameAll restores the overview for the next checks.
            Activate(ui.Root, "FrameAll");

            // Focus loss (Alt+Tab) ends a drag; the still-held button does not resume it.
            centre = Free(rect.center);
            yaw = view.Yaw;
            Frame(ui, centre, Vector2.zero, left: true);
            Frame(ui, centre + new Vector2(10, 0), new Vector2(10, 0), left: true);
            var orbitedYaw = view.Yaw;
            Frame(ui, centre + new Vector2(20, 0), new Vector2(10, 0), left: true, focusLost: true);
            Frame(ui, centre + new Vector2(40, 0), new Vector2(20, 0), left: true);
            Frame(ui, centre + new Vector2(40, 0), Vector2.zero);
            Check("focus_loss_ends_drag", !Near(orbitedYaw, yaw) && Near(view.Yaw, orbitedYaw) && !ui.PointerDragging && ui.SelectedId == selectedBefore);
            Activate(ui.Root, "FrameAll");

            var before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            LeftDrag(ui, sidebar, sidebar + new Vector2(50, 50));
            Frame(ui, sidebar, Vector2.zero, dolly: true);
            Frame(ui, sidebar + new Vector2(50, 50), new Vector2(50, 50), dolly: true);
            Frame(ui, sidebar, Vector2.zero);
            Frame(ui, toolbar, Vector2.zero, pan: true);
            Frame(ui, toolbar, new Vector2(50, 50), pan: true);
            Frame(ui, toolbar, Vector2.zero);
            Frame(ui, sidebar, Vector2.zero, wheel: 3);
            Check("pointer_over_panels_blocks_orbit_pan_and_zoom", (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before);

            var field = ui.Root.Q<Vector3Field>("Position").Q<FloatField>();
            field.Focus();
            Check("text_field_focus_detected", ui.TextInputFocused());
            Frame(ui, centre, Vector2.zero, wheel: 3, frameKey: true);
            Check("text_focus_blocks_wheel_and_frame_shortcut", (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before);
            LeftDrag(ui, centre, centre + new Vector2(30, 0));
            Check("viewport_press_commits_text_focus_without_rotating", !ui.TextInputFocused() && (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before);

            Activate(ui.Root, "object-" + b.Id);
            MmdSceneWorkspace.TryBounds(new[] { b }, out var bBounds);
            Check("list_selection_moves_3d_highlight", ui.HighlightVisible && Near(ui.HighlightBounds.center, bBounds.center) && Near(ui.HighlightBounds.size, bBounds.size));
            var pitchBeforeKey = view.Pitch;
            Frame(ui, centre, Vector2.zero, frameKey: true);
            Check("frame_shortcut_frames_selected_character", ui.SafeViewportRect().Contains((Vector2)Camera.main.WorldToScreenPoint(bBounds.center)) &&
                Near(view.Pitch, pitchBeforeKey) && CameraMatchesView(ui));
            ui.Root.Q<Toggle>("Visible").value = false;
            Check("hidden_selected_character_hides_highlight", !ui.HighlightVisible);
            ui.Root.Q<Toggle>("Visible").value = true;
            Activate(ui.Root, "object-" + a.Id);
            MmdSceneWorkspace.TryBounds(new[] { a }, out var aBounds);
            Check("reselection_restores_highlight_on_selected", ui.HighlightVisible && Near(ui.HighlightBounds.center, aBounds.center));
            Frame(ui, centre, Vector2.zero, wheel: float.NaN);
            centre = Free(centre);
            Frame(ui, centre, Vector2.zero, left: true);
            Frame(ui, centre + new Vector2(10, 0), new Vector2(10, 0), left: true);
            Frame(ui, centre + new Vector2(10, 0), new Vector2(float.PositiveInfinity, 0), left: true);
            Frame(ui, centre + new Vector2(10, 0), Vector2.zero);
            Frame(ui, centre, Vector2.zero, dolly: true);
            Frame(ui, centre, new Vector2(0, float.NaN), dolly: true);
            Frame(ui, centre, new Vector2(0, 1e9f), dolly: true);
            Frame(ui, centre, Vector2.zero);
            Frame(ui, centre, Vector2.zero, wheel: -1000);
            Check("nonfinite_and_extreme_input_keeps_camera_finite_and_clamped",
                !float.IsNaN(view.Yaw) && !float.IsNaN(view.Position.x) && view.Distance <= MmdViewportCamera.MaxDistance && CameraMatchesView(ui));
        }
        private void Finish(string path, Report report, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.checks = checks.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
