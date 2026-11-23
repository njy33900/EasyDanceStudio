using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // Inspector graphics sections (MVP-06, restyled in MVP-08): the Camera and Light sections
    // appear when those explorer entries are chosen; the Graphics section is always shown.
    public sealed partial class MmdSceneWorkspace
    {
        public MmdSceneGraphics Graphics { get; private set; }
        private VisualElement cameraSection, lightSection, graphicsSection, savedViewList;
        private DropdownField shadowQuality;
        private readonly List<Action<SceneGraphicsSettings>> graphicsBinders = new List<Action<SceneGraphicsSettings>>();
        private int savedViewCounter;
        private static readonly string[] ShadowLabels = { "끔", "낮음 (1024)", "보통 (2048)", "높음 (4096)" };

        private void BuildGraphicsSections(VisualElement content)
        {
            cameraSection = Section(content, "CameraSection", "카메라");
            GraphicsSlider(cameraSection, "Fov", "시야각", 10, 120, s => s.fieldOfView, (s, v) => s.fieldOfView = v);
            var viewRow = Element(cameraSection, "eds-row-buttons");
            TextButton(viewRow, "SaveView", "현재 시점 저장", () => Guard(SaveView), "eds-button--block").style.marginRight = 6;
            TextButton(viewRow, "ResetView", "뷰 초기화", () => Guard(ResetView), "eds-button--block");
            // v0.61: the screen tilt a camera VMD handed over, and a way to remove it.
            var levelRow = Element(cameraSection, "eds-row-buttons"); levelRow.name = "ViewRollRow"; levelRow.style.marginTop = 6;
            viewRollInfo = Text(levelRow, "", "eds-section-text"); viewRollInfo.name = "ViewRollInfo"; viewRollInfo.style.flexGrow = 1;
            TextButton(levelRow, "LevelView", "수평 맞추기", () => Guard(LevelView));
            savedViewList = new VisualElement { name = "SavedViews" }; savedViewList.style.marginTop = 6; cameraSection.Add(savedViewList);
            BuildCameraMotionControls(cameraSection);
            BuildAutoCameraControls(cameraSection);

            lightSection = Section(content, "LightSection", "조명 · 방향광");
            GraphicsSlider(lightSection, "LightR", "색 R", 0, 1, s => s.lightColor.r, (s, v) => s.lightColor.r = v);
            GraphicsSlider(lightSection, "LightG", "색 G", 0, 1, s => s.lightColor.g, (s, v) => s.lightColor.g = v);
            GraphicsSlider(lightSection, "LightB", "색 B", 0, 1, s => s.lightColor.b, (s, v) => s.lightColor.b = v);
            GraphicsSlider(lightSection, "LightIntensity", "강도", 0, 8, s => s.lightIntensity, (s, v) => s.lightIntensity = v);
            GraphicsSlider(lightSection, "LightYaw", "수평 방향", -180, 180, s => s.lightYaw, (s, v) => s.lightYaw = v);
            GraphicsSlider(lightSection, "LightPitch", "높이", 5, 90, s => s.lightPitch, (s, v) => s.lightPitch = v);
            shadowQuality = new DropdownField("그림자", ShadowLabels.ToList(), 2) { name = "ShadowQuality" }; lightSection.Add(shadowQuality);
            shadowQuality.RegisterValueChangedCallback(evt =>
            {
                var index = Array.IndexOf(ShadowLabels, evt.newValue);
                if (index >= 0) ChangeGraphics(s => s.shadowQuality = (MmdShadowQuality)index);
            });
            graphicsBinders.Add(s => shadowQuality.SetValueWithoutNotify(ShadowLabels[(int)s.shadowQuality]));

            graphicsSection = Section(content, "GraphicsSection", "그래픽");
            GraphicsToggle(graphicsSection, "Bloom", "Bloom", s => s.bloomEnabled, (s, v) => s.bloomEnabled = v);
            GraphicsSlider(graphicsSection, "BloomIntensity", "Bloom 강도", 0, 10, s => s.bloomIntensity, (s, v) => s.bloomIntensity = v);
            GraphicsToggle(graphicsSection, "Tonemapping", "톤매핑 (Neutral)", s => s.tonemapping, (s, v) => s.tonemapping = v);
            GraphicsSlider(graphicsSection, "Exposure", "노출 EV", -5, 5, s => s.exposure, (s, v) => s.exposure = v);
            GraphicsSlider(graphicsSection, "Contrast", "대비", -100, 100, s => s.contrast, (s, v) => s.contrast = v);
            GraphicsSlider(graphicsSection, "Saturation", "채도", -100, 100, s => s.saturation, (s, v) => s.saturation = v);
            GraphicsSlider(graphicsSection, "BackgroundR", "배경 R", 0, 1, s => s.backgroundColor.r, (s, v) => s.backgroundColor.r = v);
            GraphicsSlider(graphicsSection, "BackgroundG", "배경 G", 0, 1, s => s.backgroundColor.g, (s, v) => s.backgroundColor.g = v);
            GraphicsSlider(graphicsSection, "BackgroundB", "배경 B", 0, 1, s => s.backgroundColor.b, (s, v) => s.backgroundColor.b = v);
            // H2a-02: SSAO, depth of field, vignette, effect quality, reset.
            GraphicsToggle(graphicsSection, "Ssao", "SSAO (앰비언트 오클루전)", s => s.ssaoEnabled, (s, v) => s.ssaoEnabled = v);
            GraphicsSlider(graphicsSection, "SsaoIntensity", "SSAO 강도", 0, 4, s => s.ssaoIntensity, (s, v) => s.ssaoIntensity = v);
            GraphicsSlider(graphicsSection, "SsaoRadius", "SSAO 반경 (m)", .05f, 2, s => s.ssaoRadius, (s, v) => s.ssaoRadius = v);
            GraphicsToggle(graphicsSection, "Dof", "피사계 심도 (DOF)", s => s.dofEnabled, (s, v) => s.dofEnabled = v);
            dofTarget = new DropdownField("초점 대상", new List<string> { FocusByDistance }, 0) { name = "DofTarget" }; graphicsSection.Add(dofTarget);
            dofTarget.RegisterValueChangedCallback(evt =>
            {
                var index = dofTarget.choices.IndexOf(evt.newValue);
                var id = index > 0 && index - 1 < dofTargetIds.Count ? dofTargetIds[index - 1] : "";
                ChangeGraphics(s => s.dofTarget = id);
            });
            GraphicsSlider(graphicsSection, "DofDistance", "초점 거리 (m)", .1f, 50, s => s.dofDistance, (s, v) => s.dofDistance = v);
            GraphicsSlider(graphicsSection, "DofStrength", "흐림 강도", 0, 1, s => s.dofStrength, (s, v) => s.dofStrength = v);
            GraphicsToggle(graphicsSection, "Vignette", "비네트", s => s.vignetteEnabled, (s, v) => s.vignetteEnabled = v);
            GraphicsSlider(graphicsSection, "VignetteIntensity", "비네트 강도", 0, 1, s => s.vignetteIntensity, (s, v) => s.vignetteIntensity = v);
            effectQuality = new DropdownField("효과 품질", QualityLabels.ToList(), 1) { name = "EffectQuality" }; graphicsSection.Add(effectQuality);
            effectQuality.RegisterValueChangedCallback(evt =>
            {
                var index = Array.IndexOf(QualityLabels, evt.newValue);
                if (index >= 0) ChangeGraphics(s => s.effectQuality = (MmdEffectQuality)index);
            });
            graphicsBinders.Add(s => effectQuality.SetValueWithoutNotify(QualityLabels[(int)s.effectQuality]));
            var resetRow = Element(graphicsSection, "eds-row-buttons");
            TextButton(resetRow, "ResetEffects", "효과 초기화", () => Guard(ResetEffects), "eds-button--block");
            Text(graphicsSection, "초점 대상을 고르면 그 캐릭터까지의 거리에 초점을 맞춥니다. 툰 셰이더(재질 스타일)는 H2b 예정입니다.", "eds-note").name = "GraphicsNote";
            var lightReset = Element(lightSection, "eds-row-buttons");
            TextButton(lightReset, "ResetLight", "조명 초기화", () => Guard(ResetLight), "eds-button--block");
        }
        private DropdownField dofTarget, effectQuality;
        private readonly List<string> dofTargetIds = new List<string>();
        private const string FocusByDistance = "거리 지정";
        private static readonly string[] QualityLabels = { "낮음", "보통", "높음" };

        // Post-processing back to the defaults; camera, light, background and saved views stay.
        private void ResetEffects()
        {
            var next = Graphics.Settings; var d = new SceneGraphicsSettings();
            next.bloomEnabled = d.bloomEnabled; next.bloomIntensity = d.bloomIntensity; next.tonemapping = d.tonemapping;
            next.exposure = d.exposure; next.contrast = d.contrast; next.saturation = d.saturation;
            next.dofEnabled = d.dofEnabled; next.dofTarget = d.dofTarget; next.dofDistance = d.dofDistance; next.dofStrength = d.dofStrength;
            next.ssaoEnabled = d.ssaoEnabled; next.ssaoIntensity = d.ssaoIntensity; next.ssaoRadius = d.ssaoRadius;
            next.vignetteEnabled = d.vignetteEnabled; next.vignetteIntensity = d.vignetteIntensity; next.effectQuality = d.effectQuality;
            Record("효과 초기화", null, () => Graphics.Apply(next)); message = "효과를 기본값으로 되돌렸습니다.";
        }
        private void ResetLight()
        {
            var next = Graphics.Settings; var d = new SceneGraphicsSettings();
            next.lightColor = d.lightColor; next.lightIntensity = d.lightIntensity; next.lightYaw = d.lightYaw; next.lightPitch = d.lightPitch; next.shadowQuality = d.shadowQuality;
            Record("조명 초기화", null, () => Graphics.Apply(next)); message = "조명을 기본값으로 되돌렸습니다.";
        }

        // Focus target choices: the characters in the scene (a missing target falls back to the distance).
        private void RefreshDofTargets(SceneGraphicsSettings settings)
        {
            dofTargetIds.Clear();
            var choices = new List<string> { FocusByDistance };
            var names = new Dictionary<string, int>();
            foreach (var item in Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(item.ModelSource);
                names[name] = names.TryGetValue(name, out var n) ? n + 1 : 1;
                choices.Add(names[name] > 1 ? name + " (" + names[name] + ")" : name);
                dofTargetIds.Add(item.Id);
            }
            var index = dofTargetIds.IndexOf(settings.dofTarget);
            if (index < 0 && settings.dofTarget.Length > 0) choices[0] = FocusByDistance + " (대상 없음)";
            dofTarget.choices = choices;
            dofTarget.SetValueWithoutNotify(choices[index + 1]);
        }

        // Every frame: keep the focus on the target character (camera-space depth of its bounds centre).
        internal void UpdateDofFocus()
        {
            if (Graphics == null) return;
            var settings = Graphics.CurrentSettings;
            float? focus = null;
            if (settings.dofEnabled && settings.dofTarget.Length > 0)
            {
                var item = Scene.Objects.FirstOrDefault(o => o.Id == settings.dofTarget);
                if (item != null && item.Visible && TryBounds(new[] { item }, out var bounds))
                    focus = Vector3.Dot(bounds.center - Camera.main.transform.position, Camera.main.transform.forward);
            }
            Graphics.SetFocusOverride(focus);
        }
        private void ShowGraphicsSections(InspectorTarget target)
        {
            Show(cameraSection, target == InspectorTarget.Camera);
            Show(lightSection, target == InspectorTarget.Light);
        }
        private void GraphicsSlider(VisualElement parent, string name, string label, float min, float max, Func<SceneGraphicsSettings, float> get, Action<SceneGraphicsSettings, float> set)
        {
            var slider = new Slider(label, min, max) { name = name, showInputField = true }; slider.AddToClassList("eds-graphics-slider");
            var input = slider.Q<TextField>(); if (input != null) input.isDelayed = true;
            parent.Add(slider);
            slider.RegisterValueChangedCallback(evt => ChangeGraphics(s => set(s, evt.newValue)));
            graphicsBinders.Add(s => slider.SetValueWithoutNotify(get(s)));
        }
        private void GraphicsToggle(VisualElement parent, string name, string label, Func<SceneGraphicsSettings, bool> get, Action<SceneGraphicsSettings, bool> set)
        {
            var toggle = new Toggle(label) { name = name };
            parent.Add(toggle);
            toggle.RegisterValueChangedCallback(evt => ChangeGraphics(s => set(s, evt.newValue)));
            graphicsBinders.Add(s => toggle.SetValueWithoutNotify(get(s)));
        }
        // Copy-modify-apply: invalid values are rejected by MmdSceneGraphics.Apply and the
        // controls are restored from the retained state by Refresh.
        private void ChangeGraphics(Action<SceneGraphicsSettings> change)
        {
            Guard(() =>
            {
                Record("그래픽", null, () => { var next = Graphics.Settings; change(next); Graphics.Apply(next); }, coalesce: true);
            });
        }
        // Whole-state apply used by future persistence; refreshes the controls.
        internal void ApplyGraphics(SceneGraphicsSettings settings) => Guard(() => Record("그래픽", null, () => Graphics.Apply(settings)));
        private void SaveView()
        {
            var next = Graphics.Settings;
            if (next.savedViews.Count >= SceneGraphicsSettings.MaxSavedViews) throw new InvalidOperationException("시점은 최대 " + SceneGraphicsSettings.MaxSavedViews + "개까지 저장할 수 있습니다.");
            savedViewCounter = Math.Max(savedViewCounter, next.savedViews.Count) + 1;
            next.savedViews.Add(new SavedView { name = "시점 " + savedViewCounter, pivot = View.Pivot, yaw = View.Yaw, pitch = View.Pitch, distance = View.Distance, fieldOfView = Camera.main.fieldOfView, roll = View.Roll });
            Record("시점 저장", null, () => Graphics.Apply(next)); message = "현재 시점을 저장했습니다.";
        }
        internal void RestoreView(int index)
        {
            var next = Graphics.Settings;
            if (index < 0 || index >= next.savedViews.Count) throw new ArgumentOutOfRangeException(nameof(index), "저장된 시점을 찾을 수 없습니다.");
            var view = next.savedViews[index];
            ReleaseCameraMotion();
            View.Set(view.pivot, view.yaw, view.pitch, view.distance, view.roll);
            next.fieldOfView = view.fieldOfView; Graphics.Apply(next);
            View.Apply(Camera.main); message = view.name + "(으)로 이동했습니다.";
        }
        private void DeleteView(int index)
        {
            var next = Graphics.Settings;
            if (index < 0 || index >= next.savedViews.Count) return;
            next.savedViews.RemoveAt(index); Record("시점 삭제", null, () => Graphics.Apply(next)); message = "저장된 시점을 삭제했습니다.";
        }
        private void ResetView()
        {
            ReleaseCameraMotion();
            View.Reset(); FrameAll(); View.Apply(Camera.main); message = "뷰를 초기화했습니다.";
        }
        private Label viewRollInfo;
        private void LevelView()
        {
            ReleaseCameraMotion();
            View.Level(); View.Apply(Camera.main); message = "화면 기울기를 없앴습니다.";
        }
        // Shown while the manual view has a tilt (a camera VMD handed it over).
        private void RefreshViewRoll()
        {
            if (viewRollInfo == null) return;
            var row = viewRollInfo.parent;
            var tilted = !CameraFollowing && Mathf.Abs(View.Roll) > .05f;
            Show(row, tilted);
            if (tilted) viewRollInfo.text = "화면 기울기 " + View.Roll.ToString("0.0") + "°";
        }
        private void RefreshGraphics()
        {
            if (Graphics == null || graphicsSection == null) return;
            var settings = Graphics.Settings;
            foreach (var bind in graphicsBinders) bind(settings);
            RefreshDofTargets(settings);
            savedViewList.Clear();
            for (var i = 0; i < settings.savedViews.Count; i++)
            {
                var index = i; var row = Element(savedViewList, "eds-row-buttons");
                TextButton(row, "view-" + i, settings.savedViews[i].name, () => Guard(() => RestoreView(index)), "eds-button--block").style.marginRight = 6;
                TextButton(row, "view-delete-" + i, "삭제", () => Guard(() => DeleteView(index)), "eds-button--danger");
            }
            cameraSection.SetEnabled(!Busy); lightSection.SetEnabled(!Busy); graphicsSection.SetEnabled(!Busy);
            RefreshViewRoll();
        }
    }
}
