using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H2a-05: look presets in the inspector ("룩 프리셋", always shown). Choosing one applies its
    // light/effects/background in one undoable step; the scene remembers the preset's name
    // (graphics.lookPreset) next to the actual values, so a preset changed later does not change
    // a saved scene. Editing any look value after applying shows "(수정됨)"; "다시 적용" returns
    // to the preset. User presets are kept in the app settings folder (look-presets.json).
    public sealed partial class MmdSceneWorkspace
    {
        [Serializable] private sealed class LookPresetFile
        {
            public int version = 1;
            public List<LookPreset> presets = new List<LookPreset>();
        }
        private VisualElement lookSection;
        private DropdownField lookPreset;
        private Label lookState;
        private TextField lookName;
        private Button lookReapply, lookDelete, lookSave;
        private readonly List<LookPreset> userLooks = new List<LookPreset>();
        private const string NoLook = "(사용자 지정)";
        // null keeps user presets in memory only (automated probes, unless they pass -lookPresetFile).
        internal string LookPresetFilePath { get; set; }
        internal IReadOnlyList<LookPreset> UserLookPresets => userLooks;
        internal IEnumerable<LookPreset> AllLookPresets => MmdLookPresets.BuiltIn.Concat(userLooks);

        private void BuildLookSection(VisualElement content)
        {
            lookSection = Section(content, "LookSection", "룩 프리셋");
            lookPreset = new DropdownField("프리셋", new List<string> { NoLook }, 0) { name = "LookPreset" }; lookSection.Add(lookPreset);
            lookPreset.RegisterValueChangedCallback(evt => Guard(() => { if (evt.newValue != NoLook) ApplyLook(evt.newValue); }));
            lookState = Text(lookSection, "", "eds-note"); lookState.name = "LookState";
            var row = Element(lookSection, "eds-row-buttons");
            lookReapply = TextButton(row, "LookReapply", "다시 적용", () => Guard(() => ApplyLook(Graphics.Settings.lookPreset)), "eds-button--block");
            lookReapply.style.marginRight = 6;
            lookDelete = TextButton(row, "LookDelete", "프리셋 삭제", () => Guard(DeleteLook), "eds-button--block", "eds-button--danger");
            lookName = new TextField("새 이름") { name = "LookName", maxLength = MmdLookPresets.MaxNameLength }; lookSection.Add(lookName);
            lookSave = TextButton(lookSection, "LookSave", "현재 값을 프리셋으로 저장", () => Guard(() => SaveLook(lookName.value)), "eds-button--block");
            Text(lookSection, "조명·그림자·후처리·배경을 한 번에 바꿉니다(카메라·초점 대상·효과 품질은 그대로). 장면에는 프리셋 이름과 실제 값이 함께 저장됩니다.", "eds-note");
        }

        private void ConfigureLooks()
        {
            var explicitFile = PocArguments.Value("-lookPresetFile");
            var automated = Environment.GetCommandLineArgs().Any(a => a.StartsWith("-poc", StringComparison.OrdinalIgnoreCase));
            LookPresetFilePath = explicitFile != null ? Path.GetFullPath(explicitFile)
                : automated ? null : Path.Combine(Application.persistentDataPath, "look-presets.json");
            LoadLooks();
            RefreshLook();
        }
        internal void LoadLooks()
        {
            userLooks.Clear();
            if (LookPresetFilePath == null || !File.Exists(LookPresetFilePath)) return;
            try
            {
                var data = JsonUtility.FromJson<LookPresetFile>(File.ReadAllText(LookPresetFilePath));
                foreach (var preset in data?.presets ?? new List<LookPreset>())
                {
                    if (preset?.values == null || userLooks.Count >= MmdLookPresets.MaxUserPresets) continue;
                    try
                    {
                        var name = MmdLookPresets.CheckName(preset.name, userLooks);
                        preset.values.Validate();
                        if (userLooks.Any(p => p.name == name)) continue;
                        preset.name = name; userLooks.Add(preset);
                    }
                    catch (ArgumentException ex) { Debug.LogWarning("Look preset skipped: " + ex.Message); }
                }
            }
            catch (Exception ex) { Debug.LogWarning("Look presets unreadable: " + ex.Message); }
        }
        private void SaveLooks()
        {
            if (LookPresetFilePath == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LookPresetFilePath));
            var temporary = LookPresetFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(new LookPresetFile { presets = userLooks }, true), new System.Text.UTF8Encoding(false));
                if (File.Exists(LookPresetFilePath)) File.Replace(temporary, LookPresetFilePath, null); else File.Move(temporary, LookPresetFilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal void ApplyLook(string name)
        {
            var preset = AllLookPresets.FirstOrDefault(p => p.name == name) ?? throw new ArgumentException("룩 프리셋을 찾을 수 없습니다: " + name);
            var next = Graphics.Settings;
            MmdLookPresets.CopyLook(preset.values, next);
            next.lookPreset = preset.name;
            // Depth of field without a focus target: keep the first character in focus.
            if (next.dofEnabled && next.dofTarget.Length == 0)
                next.dofTarget = Scene.Objects.FirstOrDefault(o => o.Kind == MmdSceneService.ObjectKind.Character)?.Id ?? "";
            Record("룩 프리셋", null, () => Graphics.Apply(next));
            message = "룩 프리셋 적용: " + preset.name;
        }

        internal void SaveLook(string rawName)
        {
            var name = MmdLookPresets.CheckName(rawName, userLooks);
            var existing = userLooks.FirstOrDefault(p => p.name == name);
            if (existing == null && userLooks.Count >= MmdLookPresets.MaxUserPresets) throw new InvalidOperationException("사용자 프리셋은 최대 " + MmdLookPresets.MaxUserPresets + "개입니다.");
            var values = new SceneGraphicsSettings();
            MmdLookPresets.CopyLook(Graphics.Settings, values);
            if (existing != null) existing.values = values; else userLooks.Add(new LookPreset { name = name, values = values });
            SaveLooks();
            var next = Graphics.Settings; next.lookPreset = name;
            Record("룩 프리셋", null, () => Graphics.Apply(next));
            lookName.SetValueWithoutNotify("");
            message = (existing != null ? "룩 프리셋을 덮어썼습니다: " : "룩 프리셋을 저장했습니다: ") + name;
        }

        private void DeleteLook()
        {
            var name = Graphics.Settings.lookPreset;
            var preset = userLooks.FirstOrDefault(p => p.name == name) ?? throw new InvalidOperationException("기본 프리셋은 삭제할 수 없습니다.");
            userLooks.Remove(preset);
            SaveLooks();
            // The scene keeps its values; it is no longer tied to a preset.
            var next = Graphics.Settings; next.lookPreset = "";
            Record("룩 프리셋", null, () => Graphics.Apply(next));
            message = "룩 프리셋을 삭제했습니다: " + name + " · 장면의 값은 그대로입니다.";
        }

        // "적용됨" / "(수정됨)" / the preset no longer exists.
        internal string LookStateText
        {
            get
            {
                var settings = Graphics.CurrentSettings;
                if (settings.lookPreset.Length == 0) return "프리셋 없이 직접 설정한 값입니다.";
                var preset = AllLookPresets.FirstOrDefault(p => p.name == settings.lookPreset);
                if (preset == null) return settings.lookPreset + " (프리셋이 없어 장면의 값만 사용)";
                return MmdLookPresets.SameLook(preset.values, settings) ? settings.lookPreset + " 적용됨" : settings.lookPreset + " (수정됨)";
            }
        }

        private void RefreshLook()
        {
            if (lookSection == null) return;
            var settings = Graphics.CurrentSettings;
            var choices = new List<string> { NoLook };
            choices.AddRange(MmdLookPresets.BuiltIn.Select(p => p.name));
            choices.AddRange(userLooks.Select(p => p.name));
            lookPreset.choices = choices;
            lookPreset.SetValueWithoutNotify(choices.Contains(settings.lookPreset) ? settings.lookPreset : NoLook);
            lookState.text = LookStateText;
            var known = AllLookPresets.Any(p => p.name == settings.lookPreset);
            lookReapply.SetEnabled(!Busy && known && !MmdLookPresets.SameLook(AllLookPresets.First(p => p.name == settings.lookPreset).values, settings));
            lookDelete.SetEnabled(!Busy && userLooks.Any(p => p.name == settings.lookPreset));
            lookSection.SetEnabled(!Busy);
        }
    }
}
