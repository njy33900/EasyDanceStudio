using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // v0.61: the inspector's 화면 보정 section. The selected character or background gets a colour
    // (R/G/B multiplier) and brightness; "재질별 보정" (collapsed until needed) lists the model's
    // real PMX materials and gives the chosen one its own colour, brightness and gloss. Clicking a
    // model in the viewport chooses the material under the cursor. "보정 전 보기" shows the same
    // frame from the same camera without any correction; every committed change is one undo step;
    // the values are saved in the scene and used by preview and output alike. The PMX and its
    // textures are never changed.
    public sealed partial class MmdSceneWorkspace
    {
        private VisualElement correctionSection, correctionMaterialControls;
        private Label correctionStatus, correctionSupport, correctionProblems, compareChip;
        private Button correctionCompare, correctionResetObject, correctionResetMaterial;
        private Foldout correctionMaterials;
        private DropdownField correctionMaterial;
        private Slider correctionGloss;
        private readonly List<Action<ColorCorrectionData>> correctionBinders = new List<Action<ColorCorrectionData>>();
        private readonly List<Action<MaterialCorrectionData>> materialCorrectionBinders = new List<Action<MaterialCorrectionData>>();
        private readonly List<int> correctionChoiceIndices = new List<int>();
        private string correctionObjectId;
        // PMX material index chosen in the list (or picked in the viewport) for the selected object.
        internal int CorrectionPmx { get; private set; } = -1;

        private void BuildCorrectionSection(VisualElement content)
        {
            correctionSection = Section(content, "CorrectionSection", "화면 보정");
            correctionStatus = Text(correctionSection, "", "eds-section-text"); correctionStatus.name = "CorrectionStatus";
            var row = Element(correctionSection, "eds-row-buttons"); row.style.marginBottom = 6;
            correctionCompare = TextButton(row, "CorrectionCompare", "보정 전 보기", () => Guard(ToggleCorrectionCompare), "eds-button--block");
            correctionCompare.style.marginRight = 6;
            correctionResetObject = TextButton(row, "CorrectionResetObject", "대상 색·밝기 초기화", () => Guard(ResetObjectCorrection), "eds-button--block");
            CorrectionSlider(correctionSection, "CorrectionTintR", "색 R", 0, 1, d => d.tint.r, (d, v) => d.tint.r = v);
            CorrectionSlider(correctionSection, "CorrectionTintG", "색 G", 0, 1, d => d.tint.g, (d, v) => d.tint.g = v);
            CorrectionSlider(correctionSection, "CorrectionTintB", "색 B", 0, 1, d => d.tint.b, (d, v) => d.tint.b = v);
            CorrectionSlider(correctionSection, "CorrectionBrightness", "밝기 (배)", 0, 2, d => d.brightness, (d, v) => d.brightness = v);

            correctionMaterials = new Foldout { name = "CorrectionMaterials", text = "재질별 보정", value = false };
            correctionMaterials.AddToClassList("eds-correction-materials"); correctionSection.Add(correctionMaterials);
            correctionMaterial = new DropdownField("재질", new List<string>(), 0) { name = "CorrectionMaterial" }; correctionMaterials.Add(correctionMaterial);
            correctionMaterial.RegisterValueChangedCallback(evt =>
            {
                var index = correctionMaterial.choices.IndexOf(evt.newValue);
                if (index < 0 || index >= correctionChoiceIndices.Count) return;
                CorrectionPmx = correctionChoiceIndices[index];
                RefreshCorrectionUi();
            });
            correctionSupport = Text(correctionMaterials, "", "eds-note"); correctionSupport.name = "CorrectionSupport";
            correctionMaterialControls = new VisualElement { name = "CorrectionMaterialControls" }; correctionMaterials.Add(correctionMaterialControls);
            MaterialCorrectionSlider(correctionMaterialControls, "CorrectionMaterialR", "색 R", 0, 1, m => m.tint.r, (m, v) => m.tint.r = v);
            MaterialCorrectionSlider(correctionMaterialControls, "CorrectionMaterialG", "색 G", 0, 1, m => m.tint.g, (m, v) => m.tint.g = v);
            MaterialCorrectionSlider(correctionMaterialControls, "CorrectionMaterialB", "색 B", 0, 1, m => m.tint.b, (m, v) => m.tint.b = v);
            MaterialCorrectionSlider(correctionMaterialControls, "CorrectionMaterialBrightness", "밝기 (배)", 0, 2, m => m.brightness, (m, v) => m.brightness = v);
            correctionGloss = MaterialCorrectionSlider(correctionMaterialControls, "CorrectionGloss", "광택", 0, 1, m => m.gloss, (m, v) => m.gloss = v);
            correctionResetMaterial = TextButton(correctionMaterialControls, "CorrectionMaterialReset", "이 재질 보정 초기화", () => Guard(ResetMaterialCorrection), "eds-button--block");
            correctionResetMaterial.style.marginTop = 6;
            correctionProblems = Text(correctionMaterials, "", "eds-correction-problem"); correctionProblems.name = "CorrectionProblems";
            Text(correctionSection, "원본 PMX와 텍스처는 바꾸지 않고 장면에만 저장합니다. 미리보기와 PNG·MP4 출력에 같은 값이 쓰입니다. 뷰포트에서 모델을 클릭하면 그 자리의 재질이 골라집니다.", "eds-note").name = "CorrectionNote";
        }
        private void BuildCompareChip(VisualElement card)
        {
            compareChip = Text(card, "보정 전 화면 — 화면 보정이 꺼져 있습니다", "eds-compare-chip");
            compareChip.name = "CorrectionCompareChip"; compareChip.pickingMode = PickingMode.Ignore;
            compareChip.style.display = DisplayStyle.None;
        }

        private void CorrectionSlider(VisualElement parent, string name, string label, float min, float max, Func<ColorCorrectionData, float> get, Action<ColorCorrectionData, float> set)
        {
            var slider = NewCorrectionSlider(parent, name, label, min, max);
            slider.RegisterValueChangedCallback(evt => ChangeObjectCorrection(d => set(d, evt.newValue)));
            correctionBinders.Add(d => slider.SetValueWithoutNotify(get(d)));
        }
        private Slider MaterialCorrectionSlider(VisualElement parent, string name, string label, float min, float max, Func<MaterialCorrectionData, float> get, Action<MaterialCorrectionData, float> set)
        {
            var slider = NewCorrectionSlider(parent, name, label, min, max);
            slider.RegisterValueChangedCallback(evt => ChangeMaterialCorrection(m => set(m, evt.newValue)));
            materialCorrectionBinders.Add(m => slider.SetValueWithoutNotify(get(m)));
            return slider;
        }
        private static Slider NewCorrectionSlider(VisualElement parent, string name, string label, float min, float max)
        {
            var slider = new Slider(label, min, max) { name = name, showInputField = true }; slider.AddToClassList("eds-graphics-slider");
            var input = slider.Q<TextField>(); if (input != null) input.isDelayed = true;
            parent.Add(slider);
            return slider;
        }

        // ----- Edits (each one undoable; slider drags coalesce) -----
        private void ChangeObjectCorrection(Action<ColorCorrectionData> change) => Guard(() =>
        {
            var item = Selected; if (item == null) return;
            var id = item.Id; var next = item.Correction.Clone(); change(next);
            ShowCorrectionResult();
            Record("대상 색·밝기", id, () => Scene.SetCorrection(id, next), coalesce: true);
        });
        private void ChangeMaterialCorrection(Action<MaterialCorrectionData> change) => Guard(() =>
        {
            var item = Selected; if (item == null || CorrectionPmx < 0) return;
            var styler = item.StylerOrCreate;
            var name = styler.PmxName(CorrectionPmx);
            var id = item.Id; var next = item.Correction.Clone();
            var entry = EntryFor(item, next, CorrectionPmx);
            if (entry == null) { entry = new MaterialCorrectionData { index = CorrectionPmx, name = name }; next.materials.Add(entry); }
            else { entry.index = CorrectionPmx; entry.name = name; } // an entry found by name is re-anchored where the material is now
            change(entry);
            if (entry.Neutral) next.materials.Remove(entry);
            ShowCorrectionResult();
            Record("재질 보정: " + name, id, () => Scene.SetCorrection(id, next), coalesce: true);
        });
        // The entry in `data` that currently applies to this PMX material (data is a copy of the live correction).
        private static MaterialCorrectionData EntryFor(MmdSceneService.SceneObject item, ColorCorrectionData data, int pmx)
        {
            var styler = item.StylerOrCreate;
            return styler.Resolution.ByPmx.TryGetValue(pmx, out var live) ? data.Find(live.index, live.name) : null;
        }
        private void ResetObjectCorrection()
        {
            var item = Selected; if (item == null || item.Correction.ObjectNeutral) return;
            var id = item.Id; var next = item.Correction.Clone(); next.tint = Color.white; next.brightness = 1;
            ShowCorrectionResult();
            Record("대상 색·밝기 초기화", id, () => Scene.SetCorrection(id, next));
            message = "대상 색·밝기를 되돌렸습니다. 재질별 보정은 그대로입니다.";
        }
        // Only the chosen material's entry goes; the object values and other materials stay.
        private void ResetMaterialCorrection()
        {
            var item = Selected; if (item == null || CorrectionPmx < 0) return;
            var id = item.Id; var next = item.Correction.Clone();
            var entry = EntryFor(item, next, CorrectionPmx);
            if (entry == null) return;
            next.materials.Remove(entry);
            ShowCorrectionResult();
            var name = item.StylerOrCreate.PmxName(CorrectionPmx);
            Record("재질 보정 초기화: " + name, id, () => Scene.SetCorrection(id, next));
            message = "'" + name + "' 재질 보정을 되돌렸습니다.";
        }
        // Editing while comparing goes back to the corrected view, so the change is visible.
        private void ShowCorrectionResult() { if (!Scene.CorrectionsShown) Scene.ShowCorrections(true); }
        internal void ToggleCorrectionCompare()
        {
            Scene.ShowCorrections(!Scene.CorrectionsShown);
            message = Scene.CorrectionsShown ? "화면 보정을 다시 켰습니다." : "보정 전 화면입니다. 같은 카메라·같은 프레임에서 화면 보정만 껐습니다.";
        }
        // Opening a scene or relinking a model. Entries that do not match the model are kept and listed.
        private void ApplyStoredCorrection(string id, ColorCorrectionData data, List<string> problems)
        {
            try
            {
                Scene.SetCorrection(id, data);
                var item = Scene.Objects.First(o => o.Id == id);
                var unmatched = item.Styler?.Resolution.Problems.Count ?? 0;
                if (unmatched > 0)
                {
                    var text = System.IO.Path.GetFileNameWithoutExtension(item.ModelSource) + ": 재질 보정 " + unmatched + "개를 적용하지 않았습니다(재질을 확실히 찾을 수 없음). 속성의 '재질별 보정'에서 이유를 볼 수 있습니다";
                    if (problems != null) problems.Add(text); else Debug.LogWarning(text);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                var text = "화면 보정을 적용하지 못했습니다: " + ex.Message;
                if (problems != null) problems.Add(text); else Debug.LogWarning(text);
            }
        }
        // Viewport click: the material under the cursor becomes the chosen one.
        private void PickCorrectionMaterial(MmdSceneService.SceneObject item, int pmx)
        {
            if (pmx < 0 || item == null || item.Id != SelectedId) return;
            correctionObjectId = item.Id; CorrectionPmx = pmx;
            if (correctionMaterials != null && correctionMaterials.value) message = "재질 선택: " + item.StylerOrCreate.PmxName(pmx);
            RefreshCorrectionUi(); UpdateStatus();
        }
        // Probe hook: choose a material of the selected object by PMX index.
        internal void ChooseCorrectionMaterial(int pmx) { CorrectionPmx = pmx; correctionObjectId = SelectedId; RefreshCorrectionUi(); }

        private void RefreshCorrectionUi()
        {
            if (correctionSection == null) return;
            var shown = Scene.CorrectionsShown;
            Show(compareChip, !shown && !Exporting);
            correctionCompare.text = shown ? "보정 전 보기" : "보정 후 보기";
            correctionCompare.EnableInClassList("eds-button--toggled", !shown);
            var item = Selected;
            Show(correctionSection, item != null);
            if (item == null) return;
            var data = item.Correction; var styler = item.StylerOrCreate; var resolution = styler.Resolution;
            foreach (var bind in correctionBinders) bind(data);
            var corrected = data.materials.Count(m => !m.Neutral && !resolution.Problems.ContainsKey(m));
            var unmatched = resolution.Problems.Count;
            correctionStatus.text = data.IsNeutral ? "보정 없음 · 값을 바꾸면 이 " + (item.Kind == MmdSceneService.ObjectKind.Stage ? "배경" : "캐릭터") + "에만 적용됩니다." :
                (shown ? "적용 중" : "꺼짐(보정 전 화면 비교 중)") + " · 대상 색·밝기 " + (data.ObjectNeutral ? "기본" : "변경") + " · 재질 " + corrected + "개" +
                (unmatched > 0 ? " · 적용 못 한 재질 " + unmatched + "개" : "");

            // The model's real PMX materials (those drawn on screen), in PMX order.
            if (correctionObjectId != item.Id)
            {
                correctionObjectId = item.Id;
                CorrectionPmx = resolution.ByPmx.Keys.Where(styler.PmxDrawn).DefaultIfEmpty(-1).Min();
            }
            correctionChoiceIndices.Clear();
            var choices = new List<string>();
            for (var i = 0; i < styler.PmxMaterialCount; i++)
            {
                if (!styler.PmxDrawn(i)) continue;
                var has = resolution.ByPmx.TryGetValue(i, out var e) && !e.Neutral;
                choices.Add(i + " · " + styler.PmxName(i) + (has ? " · 보정됨" : ""));
                correctionChoiceIndices.Add(i);
            }
            if (!correctionChoiceIndices.Contains(CorrectionPmx)) CorrectionPmx = correctionChoiceIndices.Count > 0 ? correctionChoiceIndices[0] : -1;
            correctionMaterial.choices = choices;
            if (CorrectionPmx >= 0) correctionMaterial.SetValueWithoutNotify(choices[correctionChoiceIndices.IndexOf(CorrectionPmx)]);
            else correctionMaterial.SetValueWithoutNotify("");
            Show(correctionMaterialControls, CorrectionPmx >= 0);
            var entry = CorrectionPmx >= 0 && resolution.ByPmx.TryGetValue(CorrectionPmx, out var live) ? live : null;
            foreach (var bind in materialCorrectionBinders) bind(entry ?? new MaterialCorrectionData());
            var style = item.MaterialStyle.style;
            var glossOk = MmdMaterialStyler.GlossSupported(style);
            var stage = item.Kind == MmdSceneService.ObjectKind.Stage;
            correctionSupport.text = CorrectionPmx < 0 ? "이 모델에는 화면에 그려지는 PMX 재질이 없습니다." :
                "지원: 색·밝기 ○ · 광택 " + (glossOk ? "○ (" + (style == MaterialStyleData.Toon ? "툰 하이라이트" : "PBR 매끄러움") + ")" :
                    "× — 원본 스타일은 조명을 받지 않습니다. 재질 스타일을 툰" + (stage ? "·PBR" : "") + "으로 바꾸면 쓸 수 있습니다") +
                "\n상태: " + (entry == null || entry.Neutral ? "보정 없음" : (shown ? "적용 중" : "꺼짐(비교 중)") +
                    (entry.gloss > 0 && !glossOk ? " · 광택 값은 저장되어 있지만 이 스타일에서는 보이지 않습니다" : "") +
                    (resolution.Moved.ContainsKey(entry) ? " · 재질 번호가 바뀌어 같은 이름으로 찾았습니다" : ""));
            correctionGloss.SetEnabled(!Busy && glossOk);
            correctionResetMaterial.SetEnabled(!Busy && entry != null);
            var problems = resolution.Problems.Select(p => "'" + p.Key.name + "' (#" + p.Key.index + "): " + p.Value + " — 다른 재질에 적용하지 않고 장면에 보관합니다.").ToArray();
            correctionProblems.text = problems.Length == 0 ? "" : "적용하지 못한 재질 보정\n" + string.Join("\n", problems);
            Show(correctionProblems, problems.Length > 0);
            correctionResetObject.SetEnabled(!Busy && !data.ObjectNeutral);
            correctionCompare.SetEnabled(!Busy);
            correctionSection.Query<Slider>().ForEach(s => { if (s != correctionGloss) s.SetEnabled(!Busy); });
            correctionMaterial.SetEnabled(!Busy && correctionChoiceIndices.Count > 0);
        }
    }
}
