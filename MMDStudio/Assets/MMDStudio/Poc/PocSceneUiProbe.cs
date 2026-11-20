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
            public float physicsPoseDifferenceM;
            public int pmxLoadFrames;
            public string[] pmxStageTimings;
        }
        private readonly List<string> checks = new List<string>();
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
                Frame(ui, new Vector2(Screen.width * .5f, Screen.height * .5f), Vector2.zero, orbit: true);
                Frame(ui, new Vector2(Screen.width * .5f + 30, Screen.height * .5f), new Vector2(30, 0), orbit: true);
                Frame(ui, new Vector2(Screen.width * .5f + 30, Screen.height * .5f), Vector2.zero);
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
                if (!saved.sceneObjects.First(o => o.id == a.Id).physics || saved.sceneObjects.First(o => o.id == b.Id).physics)
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
            // Back to the main scene file for the remaining checks.
            yield return ui.OpenFrom(sceneFile);
            try
            {
                a = Find(ui, MmdSceneService.ObjectKind.Character, aModel); b = Find(ui, MmdSceneService.ObjectKind.Character, bModel);
                stage = Find(ui, MmdSceneService.ObjectKind.Stage, stageModel);
                if (ui.Error.Length > 0 || a == null || a.PhysicsEnabled) throw new InvalidOperationException("main scene reopen failed: " + ui.Error);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(Path.Combine(project, "corrupt.mmdscene"));
            try
            {
                Check("corrupt_file_rejected_and_scene_kept", ui.Error.Contains("열 수 없습니다") && ui.Scene.Objects.Select(item => item.Id).SequenceEqual(ids) &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(sceneFile), StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(Path.Combine(project, "future.mmdscene"), File.ReadAllText(sceneFile).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));
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
                Finish(path, report, null);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); }
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
        private static void Frame(MmdSceneWorkspace ui, Vector2 screen, Vector2 delta, float wheel = 0, bool orbit = false, bool pan = false, bool frameKey = false, bool left = false) =>
            ui.ProcessViewportInput(screen, delta, wheel, orbit, pan, frameKey, left);
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
            Check("graphics_defaults_applied_with_neutral_post_processing", graphics.NeutralOverridesReady && camera.GetUniversalAdditionalCameraData().renderPostProcessing &&
                graphics.Volume.isGlobal && graphics.Volume.priority >= 1000 && Near(camera.fieldOfView, defaults.fieldOfView) &&
                Near(graphics.Sun.intensity, defaults.lightIntensity) && graphics.Sun.shadows == LightShadows.Soft && Near(camera.backgroundColor, defaults.backgroundColor) &&
                graphics.Bloom.active && Near(graphics.Bloom.intensity.value, 0) && graphics.Tonemap.mode.value == TonemappingMode.None &&
                graphics.Volume.sharedProfile.TryGet<Vignette>(out var vignette) && vignette.intensity.overrideState && vignette.intensity.value == 0 &&
                graphics.Volume.sharedProfile.TryGet<FilmGrain>(out var grain) && grain.intensity.overrideState && grain.intensity.value == 0);

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

            ui.Root.Q<Slider>("BackgroundR").value = .3f; ui.Root.Q<Slider>("BackgroundG").value = .2f; ui.Root.Q<Slider>("BackgroundB").value = .1f;
            Check("background_colour_sliders_set_camera_clear", Near(camera.backgroundColor, new Color(.3f, .2f, .1f)) && camera.clearFlags == CameraClearFlags.SolidColor);

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

            // Presentation defaults for the screenshots: default light/background, Bloom and tonemapping on.
            ui.ApplyGraphics(new SceneGraphicsSettings { bloomEnabled = true, tonemapping = true });
            Check("controls_reflect_whole_state_apply", ui.Error.Length == 0 && ui.Root.Q<Toggle>("Bloom").value && ui.Root.Q<Toggle>("Tonemapping").value &&
                Near(ui.Root.Q<Slider>("Exposure").value, 0) && Near(ui.Root.Q<Slider>("Fov").value, defaults.fieldOfView) &&
                Near(ui.Root.Q<Slider>("LightIntensity").value, defaults.lightIntensity) && ui.Root.Q<DropdownField>("ShadowQuality").value == "보통 (2048)");
            if (previousSelection != null) ui.Select(previousSelection);
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

            var before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            Frame(ui, aPoint, Vector2.zero, left: true);
            Frame(ui, aPoint + new Vector2(12, 0), new Vector2(12, 0), left: true);
            Frame(ui, aPoint + new Vector2(24, 0), new Vector2(12, 0), left: true);
            Frame(ui, aPoint + new Vector2(24, 0), Vector2.zero);
            Check("viewport_left_drag_neither_picks_nor_moves_camera", ui.SelectedId == b.Id && (view.Yaw, view.Pitch, view.Distance, view.Pivot) == before);

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
            Frame(ui, centre, Vector2.zero, orbit: true);
            Frame(ui, centre, new Vector2(40, 0), orbit: true);
            Frame(ui, centre, Vector2.zero);
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

            var yaw = view.Yaw; var pitch = view.Pitch; var distance = view.Distance; var pivot = view.Pivot;
            Frame(ui, centre, Vector2.zero, orbit: true);                 // press
            Frame(ui, centre + new Vector2(40, 0), new Vector2(40, -20), orbit: true);
            Check("orbit_drag_in_viewport_rotates_around_fixed_pivot",
                Near(view.Yaw, Mathf.Repeat(yaw + 40 * MmdViewportCamera.DegreesPerPixel, 360)) && Near(view.Pitch, pitch + 20 * MmdViewportCamera.DegreesPerPixel) &&
                Near(view.Distance, distance) && Near(view.Pivot, pivot) && Near((Camera.main.transform.position - pivot).magnitude, distance) && CameraMatchesView(ui));
            // Continue the same drag across the left panel: capture keeps orbiting.
            yaw = view.Yaw;
            Frame(ui, sidebar, new Vector2(10, 0), orbit: true);
            Check("started_drag_continues_over_panel", Near(view.Yaw, Mathf.Repeat(yaw + 10 * MmdViewportCamera.DegreesPerPixel, 360)));
            Frame(ui, sidebar, Vector2.zero);                               // release

            pivot = view.Pivot; var cameraBefore = Camera.main.transform.position; yaw = view.Yaw; pitch = view.Pitch;
            Frame(ui, centre, Vector2.zero, pan: true);
            Frame(ui, centre + new Vector2(30, 15), new Vector2(30, 15), pan: true);
            Frame(ui, centre, Vector2.zero);
            var moved = view.Pivot - pivot;
            Check("pan_drag_moves_pivot_and_camera_together",
                moved.magnitude > 1e-4f && Near(Camera.main.transform.position - cameraBefore, moved) && Near(view.Yaw, yaw) && Near(view.Pitch, pitch) &&
                Vector3.Dot(moved, Camera.main.transform.right) < 0 && Vector3.Dot(moved, Camera.main.transform.up) < 0);

            distance = view.Distance;
            Frame(ui, centre, Vector2.zero, wheel: 2);
            Check("wheel_in_viewport_zooms_toward_pivot", Near(view.Distance, distance * MmdViewportCamera.ZoomPerNotch * MmdViewportCamera.ZoomPerNotch) && CameraMatchesView(ui));

            var before = (view.Yaw, view.Pitch, view.Distance, view.Pivot);
            Frame(ui, sidebar, Vector2.zero, orbit: true);
            Frame(ui, sidebar, new Vector2(50, 50), orbit: true);
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
            Frame(ui, centre, Vector2.zero, orbit: true);
            Frame(ui, centre, new Vector2(30, 0), orbit: true);
            Frame(ui, centre, Vector2.zero);
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
            Frame(ui, centre, Vector2.zero, orbit: true);
            Frame(ui, centre, new Vector2(float.PositiveInfinity, 0), orbit: true);
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
