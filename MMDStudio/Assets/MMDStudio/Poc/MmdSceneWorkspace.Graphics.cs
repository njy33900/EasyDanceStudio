using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // MVP-06 inspector Graphics tab: camera FOV and saved views, directional light,
    // Bloom/tonemapping/exposure/contrast/saturation and background colour.
    public sealed partial class MmdSceneWorkspace
    {
        public MmdSceneGraphics Graphics { get; private set; }
        private VisualElement propertiesPanel, graphicsPanel, savedViewList;
        private Button propertiesTab, graphicsTab;
        private DropdownField shadowQuality;
        private readonly List<Action<SceneGraphicsSettings>> graphicsBinders = new List<Action<SceneGraphicsSettings>>();
        private int savedViewCounter;
        private static readonly string[] ShadowLabels = { "끔", "낮음 (1024)", "보통 (2048)", "높음 (4096)" };
        internal bool GraphicsTabVisible { get; private set; }

        private void BuildInspectorTabs(VisualElement inspector, VisualElement properties)
        {
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; tabs.style.flexShrink = 0;
            inspector.Insert(0, tabs);
            propertiesTab = Button(tabs, "InspectorTabProperties", "속성", () => ShowGraphicsTab(false));
            graphicsTab = Button(tabs, "InspectorTabGraphics", "그래픽", () => ShowGraphicsTab(true));
            propertiesTab.style.flexGrow = graphicsTab.style.flexGrow = 1;
            propertiesPanel = properties; propertiesPanel.name = "PropertiesPanel";
            graphicsPanel = BuildGraphicsPanel(); inspector.Add(graphicsPanel);
            ShowGraphicsTab(false);
        }
        internal void ShowGraphicsTab(bool graphics)
        {
            GraphicsTabVisible = graphics;
            propertiesPanel.style.display = graphics ? DisplayStyle.None : DisplayStyle.Flex;
            graphicsPanel.style.display = graphics ? DisplayStyle.Flex : DisplayStyle.None;
            var on = new Color(.15f, .3f, .5f); var off = new Color(.17f, .21f, .28f);
            propertiesTab.style.backgroundColor = graphics ? off : on; graphicsTab.style.backgroundColor = graphics ? on : off;
        }

        private VisualElement BuildGraphicsPanel()
        {
            var panel = new ScrollView { name = "GraphicsPanel" }; panel.style.flexGrow = 1;
            Section(panel, "카메라");
            GraphicsSlider(panel, "Fov", "시야각", 10, 120, s => s.fieldOfView, (s, v) => s.fieldOfView = v);
            var viewRow = Row(panel);
            Button(viewRow, "SaveView", "현재 시점 저장", () => Guard(SaveView)).style.flexGrow = 1;
            Button(viewRow, "ResetView", "뷰 초기화", () => Guard(ResetView)).style.flexGrow = 1;
            savedViewList = new VisualElement { name = "SavedViews" }; panel.Add(savedViewList);

            Section(panel, "조명 (방향광)");
            GraphicsSlider(panel, "LightR", "색 R", 0, 1, s => s.lightColor.r, (s, v) => s.lightColor.r = v);
            GraphicsSlider(panel, "LightG", "색 G", 0, 1, s => s.lightColor.g, (s, v) => s.lightColor.g = v);
            GraphicsSlider(panel, "LightB", "색 B", 0, 1, s => s.lightColor.b, (s, v) => s.lightColor.b = v);
            GraphicsSlider(panel, "LightIntensity", "강도", 0, 8, s => s.lightIntensity, (s, v) => s.lightIntensity = v);
            GraphicsSlider(panel, "LightYaw", "수평 방향", -180, 180, s => s.lightYaw, (s, v) => s.lightYaw = v);
            GraphicsSlider(panel, "LightPitch", "높이", 5, 90, s => s.lightPitch, (s, v) => s.lightPitch = v);
            shadowQuality = new DropdownField("그림자", ShadowLabels.ToList(), 2) { name = "ShadowQuality" };
            shadowQuality.labelElement.style.minWidth = shadowQuality.labelElement.style.width = 64; DarkInput(shadowQuality); panel.Add(shadowQuality);
            shadowQuality.RegisterValueChangedCallback(evt =>
            {
                var index = Array.IndexOf(ShadowLabels, evt.newValue);
                if (index >= 0) ChangeGraphics(s => s.shadowQuality = (MmdShadowQuality)index);
            });
            graphicsBinders.Add(s => shadowQuality.SetValueWithoutNotify(ShadowLabels[(int)s.shadowQuality]));

            Section(panel, "효과");
            GraphicsToggle(panel, "Bloom", "Bloom", s => s.bloomEnabled, (s, v) => s.bloomEnabled = v);
            GraphicsSlider(panel, "BloomIntensity", "Bloom 강도", 0, 10, s => s.bloomIntensity, (s, v) => s.bloomIntensity = v);
            GraphicsToggle(panel, "Tonemapping", "톤매핑 (Neutral)", s => s.tonemapping, (s, v) => s.tonemapping = v);
            GraphicsSlider(panel, "Exposure", "노출 EV", -5, 5, s => s.exposure, (s, v) => s.exposure = v);
            GraphicsSlider(panel, "Contrast", "대비", -100, 100, s => s.contrast, (s, v) => s.contrast = v);
            GraphicsSlider(panel, "Saturation", "채도", -100, 100, s => s.saturation, (s, v) => s.saturation = v);

            Section(panel, "배경색");
            GraphicsSlider(panel, "BackgroundR", "R", 0, 1, s => s.backgroundColor.r, (s, v) => s.backgroundColor.r = v);
            GraphicsSlider(panel, "BackgroundG", "G", 0, 1, s => s.backgroundColor.g, (s, v) => s.backgroundColor.g = v);
            GraphicsSlider(panel, "BackgroundB", "B", 0, 1, s => s.backgroundColor.b, (s, v) => s.backgroundColor.b = v);
            var note = new Label("물리 OFF · DOF/SSAO/툰 셰이더는 2차 예정") { name = "GraphicsNote" };
            note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 10; note.style.fontSize = 12; note.style.color = new Color(.65f, .72f, .82f);
            panel.Add(note);
            return panel;
        }
        // Same dark input styling as the inspector's numeric fields (the runtime theme
        // otherwise draws a light box behind the light text).
        private static void DarkInput(VisualElement field)
        {
            var box = field.Q(className: "unity-base-field__input");
            if (box != null) { box.style.backgroundColor = new Color(.14f, .18f, .24f); box.style.color = Color.white; }
            field.Query<TextElement>().ForEach(text => { if (!(field is BaseField<string> f && ReferenceEquals(text, f.labelElement))) text.style.color = Color.white; });
        }
        private static void Section(VisualElement parent, string text)
        {
            var label = new Label(text); label.style.marginTop = 10; label.style.marginBottom = 4; label.style.color = new Color(.65f, .72f, .82f); parent.Add(label);
        }
        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; parent.Add(row); return row;
        }
        private void GraphicsSlider(VisualElement parent, string name, string label, float min, float max, Func<SceneGraphicsSettings, float> get, Action<SceneGraphicsSettings, float> set)
        {
            var slider = new Slider(label, min, max) { name = name, showInputField = true };
            slider.labelElement.style.minWidth = slider.labelElement.style.width = 64;
            var input = slider.Q<TextField>(); if (input != null) { input.style.width = 52; input.isDelayed = true; DarkInput(input); }
            parent.Add(slider);
            slider.RegisterValueChangedCallback(evt => ChangeGraphics(s => set(s, evt.newValue)));
            graphicsBinders.Add(s => slider.SetValueWithoutNotify(get(s)));
        }
        private void GraphicsToggle(VisualElement parent, string name, string label, Func<SceneGraphicsSettings, bool> get, Action<SceneGraphicsSettings, bool> set)
        {
            var toggle = new Toggle(label) { name = name };
            toggle.labelElement.style.minWidth = toggle.labelElement.style.width = 120;
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
                var next = Graphics.Settings; change(next); Graphics.Apply(next);
            });
        }
        // Whole-state apply used by future persistence; refreshes the controls.
        internal void ApplyGraphics(SceneGraphicsSettings settings) => Guard(() => Graphics.Apply(settings));
        private void SaveView()
        {
            var next = Graphics.Settings;
            if (next.savedViews.Count >= SceneGraphicsSettings.MaxSavedViews) throw new InvalidOperationException("시점은 최대 " + SceneGraphicsSettings.MaxSavedViews + "개까지 저장할 수 있습니다.");
            savedViewCounter = Math.Max(savedViewCounter, next.savedViews.Count) + 1;
            next.savedViews.Add(new SavedView { name = "시점 " + savedViewCounter, pivot = View.Pivot, yaw = View.Yaw, pitch = View.Pitch, distance = View.Distance, fieldOfView = Camera.main.fieldOfView });
            Graphics.Apply(next); message = "현재 시점을 저장했습니다.";
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
            next.savedViews.RemoveAt(index); Graphics.Apply(next); message = "저장된 시점을 삭제했습니다.";
        }
        private void ResetView()
        {
            View.Reset(); FrameAll(); View.Apply(Camera.main); message = "뷰를 초기화했습니다.";
        }
        private void RefreshGraphics()
        {
            if (Graphics == null || graphicsPanel == null) return;
            var settings = Graphics.Settings;
            foreach (var bind in graphicsBinders) bind(settings);
            savedViewList.Clear();
            for (var i = 0; i < settings.savedViews.Count; i++)
            {
                var index = i; var row = Row(savedViewList);
                Button(row, "view-" + i, settings.savedViews[i].name, () => Guard(() => RestoreView(index))).style.flexGrow = 1;
                Button(row, "view-delete-" + i, "삭제", () => Guard(() => DeleteView(index)));
            }
            graphicsPanel.SetEnabled(!Busy);
        }
    }
}
