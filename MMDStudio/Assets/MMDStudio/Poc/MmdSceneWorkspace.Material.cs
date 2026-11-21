using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H2b-01: the inspector's 재질 section. Style per object (원본 / 툰, plus PBR for backgrounds),
    // toon controls (명암 경계 on/position/strength, 림 라이트 on/strength, 윤곽선 on/width) and PBR
    // controls (smoothness, metallic). Every change is one undoable edit stored in the scene file;
    // the PMX and its textures are never changed. Limits and fallbacks are listed under the controls.
    public sealed partial class MmdSceneWorkspace
    {
        private DropdownField materialStyle;
        private VisualElement toonControls, pbrControls;
        private Label materialWarnings;
        private Button materialApplyAll;
        private readonly List<Action<MaterialStyleData>> materialBinders = new List<Action<MaterialStyleData>>();
        private static readonly string[] CharacterStyleLabels = { "원본", "툰" }, StageStyleLabels = { "원본", "툰", "PBR" };
        private static string StyleLabel(string style) => style == MaterialStyleData.Toon ? "툰" : style == MaterialStyleData.Pbr ? "PBR" : "원본";
        private static string StyleId(string label) => label == "툰" ? MaterialStyleData.Toon : label == "PBR" ? MaterialStyleData.Pbr : MaterialStyleData.Original;

        private void BuildMaterialControls(VisualElement section)
        {
            materialStyle = new DropdownField("스타일", CharacterStyleLabels.ToList(), 0) { name = "MaterialStyle" }; section.Add(materialStyle);
            materialStyle.RegisterValueChangedCallback(evt => ChangeMaterial(d => d.style = StyleId(evt.newValue), "재질 스타일"));
            toonControls = new VisualElement { name = "ToonControls" }; section.Add(toonControls);
            MaterialToggle(toonControls, "ToonShadow", "명암 경계", d => d.shadow, (d, v) => d.shadow = v);
            MaterialSlider(toonControls, "ToonShadowBorder", "경계 위치", -1, 1, d => d.shadowBorder, (d, v) => d.shadowBorder = v);
            MaterialSlider(toonControls, "ToonShadowStrength", "그림자 진하기", 0, 1, d => d.shadowStrength, (d, v) => d.shadowStrength = v);
            MaterialToggle(toonControls, "ToonRim", "림 라이트", d => d.rim, (d, v) => d.rim = v);
            MaterialSlider(toonControls, "ToonRimStrength", "림 라이트 강도", 0, 1, d => d.rimStrength, (d, v) => d.rimStrength = v);
            MaterialToggle(toonControls, "ToonOutline", "윤곽선", d => d.outline, (d, v) => d.outline = v);
            MaterialSlider(toonControls, "ToonOutlineWidth", "윤곽선 두께 (배)", 0, 3, d => d.outlineWidth, (d, v) => d.outlineWidth = v);
            pbrControls = new VisualElement { name = "PbrControls" }; section.Add(pbrControls);
            MaterialSlider(pbrControls, "PbrSmoothness", "매끄러움", 0, 1, d => d.smoothness, (d, v) => d.smoothness = v);
            MaterialSlider(pbrControls, "PbrMetallic", "금속성", 0, 1, d => d.metallic, (d, v) => d.metallic = v);
            materialWarnings = Text(section, "", "eds-note"); materialWarnings.name = "MaterialWarnings";
            materialApplyAll = TextButton(section, "MaterialApplyAll", "모든 캐릭터에 이 툰 설정 적용", () => Guard(ApplyToonToAll), "eds-button--block");
            Text(section, "원본 PMX와 텍스처는 바꾸지 않고 장면에만 저장합니다. 툰은 조명 방향·색·그림자를 받습니다.", "eds-note").name = "MaterialNote";
        }

        private void MaterialToggle(VisualElement parent, string name, string label, Func<MaterialStyleData, bool> get, Action<MaterialStyleData, bool> set)
        {
            var toggle = new Toggle(label) { name = name }; parent.Add(toggle);
            toggle.RegisterValueChangedCallback(evt => ChangeMaterial(d => set(d, evt.newValue), "재질"));
            materialBinders.Add(d => toggle.SetValueWithoutNotify(get(d)));
        }
        private void MaterialSlider(VisualElement parent, string name, string label, float min, float max, Func<MaterialStyleData, float> get, Action<MaterialStyleData, float> set)
        {
            var slider = new Slider(label, min, max) { name = name, showInputField = true }; slider.AddToClassList("eds-graphics-slider");
            var input = slider.Q<TextField>(); if (input != null) input.isDelayed = true;
            parent.Add(slider);
            slider.RegisterValueChangedCallback(evt => ChangeMaterial(d => set(d, evt.newValue), "재질"));
            materialBinders.Add(d => slider.SetValueWithoutNotify(get(d)));
        }

        private void ChangeMaterial(Action<MaterialStyleData> change, string label) => Guard(() =>
        {
            if (Selected == null) return;
            var id = SelectedId;
            var next = Selected.MaterialStyle.Clone(); change(next);
            Record(label, id, () => Scene.SetMaterialStyle(id, next), coalesce: label != "재질 스타일");
            if (label == "재질 스타일") message = "재질 스타일: " + StyleLabel(next.style);
        });

        private void ApplyToonToAll()
        {
            var source = Selected != null && Selected.MaterialStyle.style == MaterialStyleData.Toon ? Selected.MaterialStyle : new MaterialStyleData { style = MaterialStyleData.Toon };
            var count = 0;
            Record("툰 일괄 적용", null, () =>
            {
                foreach (var item in Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character).ToArray())
                {
                    var next = source.Clone(); next.style = MaterialStyleData.Toon;
                    Scene.SetMaterialStyle(item.Id, next); count++;
                }
            });
            message = "캐릭터 " + count + "명에게 툰 재질을 적용했습니다.";
        }

        // Opening a scene or relinking: a style that cannot be applied leaves the original materials.
        private void ApplyStoredMaterial(string id, MaterialStyleData data, List<string> problems)
        {
            try { Scene.SetMaterialStyle(id, data); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                var text = "재질 스타일을 적용하지 못해 원본 재질을 씁니다: " + ex.Message;
                if (problems != null) problems.Add(text); else Debug.LogWarning(text);
            }
        }

        private void RefreshMaterial()
        {
            if (materialStyle == null) return;
            var selected = Selected;
            if (selected == null) return;
            var stage = selected.Kind == MmdSceneService.ObjectKind.Stage;
            var data = selected.MaterialStyle;
            materialStyle.choices = (stage ? StageStyleLabels : CharacterStyleLabels).ToList();
            materialStyle.SetValueWithoutNotify(StyleLabel(data.style));
            foreach (var bind in materialBinders) bind(data);
            Show(toonControls, data.style == MaterialStyleData.Toon);
            Show(pbrControls, data.style == MaterialStyleData.Pbr);
            var warnings = selected.MaterialWarnings;
            materialWarnings.text = warnings.Length == 0 ? "" : "참고: " + string.Join(" · ", warnings);
            Show(materialWarnings, warnings.Length > 0);
            Show(materialApplyAll, !stage && data.style == MaterialStyleData.Toon);
            materialStyle.SetEnabled(!Busy); toonControls.SetEnabled(!Busy); pbrControls.SetEnabled(!Busy); materialApplyAll.SetEnabled(!Busy);
        }
    }
}
