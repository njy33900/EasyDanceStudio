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
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocSecondPmx"), true);
            yield return ui.ApplyPickerResult(bMotion, false);
            var b = ui.Scene.Objects.LastOrDefault();
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
                Check("viewport_excludes_sidebars", Camera.main.rect.x > .1f && Camera.main.rect.width < .8f && Camera.main.rect.height < .9f);
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
                Check("selection_after_failure_clears_stale_failure_status", ui.Error.Length == 0 && !statusText.Contains("불러오지") && statusText.Contains(Path.GetFileNameWithoutExtension(a.ModelSource)));
                Activate(ui.Root, "FrameAll");
                PickingChecks(ui, a, b, stage);
                GraphicsChecks(ui);
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
                Activate(ui.Root, "InspectorTabGraphics");
                // Visual evidence of the Graphics tab (not an extra assertion).
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path), "scene-ui-graphics.png"));
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.2f);
            try
            {
                Activate(ui.Root, "InspectorTabProperties");
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
                Finish(path, report, null);
            }
            catch (Exception ex) { Finish(path, report, ex.ToString()); }
        }
        private static bool Near(Vector3 x, Vector3 y, float tolerance = 1e-3f) => (x - y).magnitude <= tolerance;
        private static bool Near(float x, float y, float tolerance = 1e-3f) => Math.Abs(x - y) <= tolerance;
        private static bool CameraMatchesView(MmdSceneWorkspace ui) =>
            Near(Camera.main.transform.position, ui.View.Position) && Quaternion.Angle(Camera.main.transform.rotation, ui.View.Rotation) < .01f;
        private static void Frame(MmdSceneWorkspace ui, Vector2 screen, Vector2 delta, float wheel = 0, bool orbit = false, bool pan = false, bool frameKey = false, bool left = false) =>
            ui.ProcessViewportInput(screen, delta, wheel, orbit, pan, frameKey, left);
        private static void Click(MmdSceneWorkspace ui, Vector2 screen)
        {
            Frame(ui, screen, Vector2.zero, left: true);
            Frame(ui, screen, Vector2.zero);
        }
        private static void Key(MmdSceneWorkspace ui, bool play = false, bool delete = false, bool cancel = false) =>
            ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, false, play, delete, cancel);
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

            Activate(ui.Root, "InspectorTabGraphics");
            Check("graphics_tab_replaces_properties_panel", ui.GraphicsTabVisible && ui.Root.Q("GraphicsPanel").style.display.value == DisplayStyle.Flex &&
                ui.Root.Q("PropertiesPanel").style.display.value == DisplayStyle.None);

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
                Near(view.Pivot, all.center) && CameraMatchesView(ui));

            // Presentation defaults for the screenshots: default light/background, Bloom and tonemapping on.
            ui.ApplyGraphics(new SceneGraphicsSettings { bloomEnabled = true, tonemapping = true });
            Check("controls_reflect_whole_state_apply", ui.Error.Length == 0 && ui.Root.Q<Toggle>("Bloom").value && ui.Root.Q<Toggle>("Tonemapping").value &&
                Near(ui.Root.Q<Slider>("Exposure").value, 0) && Near(ui.Root.Q<Slider>("Fov").value, defaults.fieldOfView) &&
                Near(ui.Root.Q<Slider>("LightIntensity").value, defaults.lightIntensity) && ui.Root.Q<DropdownField>("ShadowQuality").value == "보통 (2048)");
            Activate(ui.Root, "InspectorTabProperties");
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
                    if (screen.z > 0 && ui.PointerInViewport(candidate) && !RayTouchesBounds(candidate, characters)) floor = candidate;
                }
            if (floor == null) throw new InvalidOperationException("No unobstructed stage floor point in the viewport");
            Click(ui, floor.Value);
            Check("viewport_click_on_floor_selects_stage", ui.SelectedId == stage.Id && ui.Root.Q<Label>("SelectionTitle").text == "선택한 배경");

            var sidebar = new Vector2(rect.xMin * .5f, rect.center.y);
            Click(ui, sidebar);
            Check("left_click_over_panel_does_not_pick", ui.SelectedId == stage.Id);

            Vector2? empty = null;
            foreach (var corner in new[] { new Vector2(rect.xMin + 8, rect.yMax - 8), new Vector2(rect.xMax - 8, rect.yMax - 8), new Vector2(rect.center.x, rect.yMax - 8),
                                           new Vector2(rect.xMin + 8, rect.yMin + 30), new Vector2(rect.xMax - 8, rect.yMin + 30) })
                if (empty == null && ui.PointerInViewport(corner) && !RayTouchesBounds(corner, ui.Scene.Objects)) empty = corner;
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
            var sidebar = new Vector2(rect.xMin * .5f, rect.center.y);
            var toolbar = new Vector2(rect.center.x, Screen.height - 4);
            Check("viewport_pick_distinguishes_panels_and_viewport",
                ui.PointerOverUi(sidebar) && ui.PointerOverUi(toolbar) && !ui.PointerOverUi(centre) && !ui.PointerOverUi(topEdge) &&
                ui.PointerInViewport(centre) && !ui.PointerInViewport(sidebar));

            MmdSceneWorkspace.TryBounds(ui.Scene.Objects.Where(item => item.Visible), out var all);
            Check("frame_all_targets_visible_bounds_and_applies_camera",
                Near(view.Pivot, all.center) && view.Distance > all.extents.magnitude && CameraMatchesView(ui) &&
                Vector3.Angle(Camera.main.transform.forward, all.center - Camera.main.transform.position) < .1f);

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
            Check("frame_shortcut_frames_selected_character", Near(view.Pivot, bBounds.center) && Near(view.Pitch, pitchBeforeKey) && CameraMatchesView(ui));
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
