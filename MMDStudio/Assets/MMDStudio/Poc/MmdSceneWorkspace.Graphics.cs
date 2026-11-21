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
            savedViewList = new VisualElement { name = "SavedViews" }; savedViewList.style.marginTop = 6; cameraSection.Add(savedViewList);

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
            Text(graphicsSection, "DOF · SSAO · 툰 셰이더는 2차 예정입니다.", "eds-note").name = "GraphicsNote";
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
            next.savedViews.Add(new SavedView { name = "시점 " + savedViewCounter, pivot = View.Pivot, yaw = View.Yaw, pitch = View.Pitch, distance = View.Distance, fieldOfView = Camera.main.fieldOfView });
            Record("시점 저장", null, () => Graphics.Apply(next)); message = "현재 시점을 저장했습니다.";
        }
        internal void RestoreView(int index)
        {
            var next = Graphics.Settings;
            if (index < 0 || index >= next.savedViews.Count) throw new ArgumentOutOfRangeException(nameof(index), "저장된 시점을 찾을 수 없습니다.");
            var view = next.savedViews[index];
            View.Set(view.pivot, view.yaw, view.pitch, view.distance);
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
            View.Reset(); FrameAll(); View.Apply(Camera.main); message = "뷰를 초기화했습니다.";
        }
        private void RefreshGraphics()
        {
            if (Graphics == null || graphicsSection == null) return;
            var settings = Graphics.Settings;
            foreach (var bind in graphicsBinders) bind(settings);
            savedViewList.Clear();
            for (var i = 0; i < settings.savedViews.Count; i++)
            {
                var index = i; var row = Element(savedViewList, "eds-row-buttons");
                TextButton(row, "view-" + i, settings.savedViews[i].name, () => Guard(() => RestoreView(index)), "eds-button--block").style.marginRight = 6;
                TextButton(row, "view-delete-" + i, "삭제", () => Guard(() => DeleteView(index)), "eds-button--danger");
            }
            cameraSection.SetEnabled(!Busy); lightSection.SetEnabled(!Busy); graphicsSection.SetEnabled(!Busy);
        }
    }
}
