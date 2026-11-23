using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // v0.61: screen correction of one scene object, a scene override like the material style
    // (the PMX and its textures are never changed). The object's colour and brightness apply to
    // all its materials; a per-material entry adds its own colour, brightness and gloss on top.
    // Applied at draw time through per-material property blocks, so preview and output (PNG, MP4)
    // draw the same values, and "보정 전 보기" can switch all of it off without touching the data.
    [Serializable]
    public sealed class ColorCorrectionData
    {
        public const int MaxMaterials = 1024;
        public Color tint = Color.white;  // RGB multiplier, 0..1 per channel
        public float brightness = 1;      // 0..2
        public List<MaterialCorrectionData> materials = new List<MaterialCorrectionData>();

        public ColorCorrectionData Clone() => JsonUtility.FromJson<ColorCorrectionData>(JsonUtility.ToJson(this));
        public bool ObjectNeutral => tint.r == 1 && tint.g == 1 && tint.b == 1 && brightness == 1;
        public bool IsNeutral => ObjectNeutral && materials.Count == 0;
        public void Validate()
        {
            Colour(tint, "대상 색"); Range(brightness, 0, 2, "대상 밝기는 0~2배여야 합니다.");
            if (materials == null) materials = new List<MaterialCorrectionData>();
            if (materials.Count > MaxMaterials) throw new ArgumentOutOfRangeException(nameof(materials), "재질 보정은 최대 " + MaxMaterials + "개입니다.");
            var keys = new HashSet<string>();
            foreach (var entry in materials)
            {
                if (entry == null) throw new ArgumentNullException(nameof(materials), "재질 보정 항목이 비어 있습니다.");
                if (entry.name == null) entry.name = "";
                if (entry.index < 0 || entry.index > 65535 || entry.name.Length > 128) throw new ArgumentOutOfRangeException(nameof(materials), "재질 보정의 재질 번호나 이름이 올바르지 않습니다.");
                if (!keys.Add(entry.index + "\n" + entry.name)) throw new ArgumentException("같은 재질에 대한 보정이 두 번 들어 있습니다: " + entry.name, nameof(materials));
                Colour(entry.tint, "재질 색"); Range(entry.brightness, 0, 2, "재질 밝기는 0~2배여야 합니다."); Range(entry.gloss, 0, 1, "광택은 0~1이어야 합니다.");
            }
        }
        // The entry made for this PMX material (as listed in the model), or null.
        public MaterialCorrectionData Find(int index, string name) => materials.FirstOrDefault(m => m.index == index && m.name == name);
        private static void Range(float value, float min, float max, string message)
        {
            if (float.IsNaN(value) || value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value), message);
        }
        private static void Colour(Color value, string name)
        {
            foreach (var channel in new[] { value.r, value.g, value.b })
                if (float.IsNaN(channel) || channel < 0 || channel > 1) throw new ArgumentOutOfRangeException(nameof(value), name + " 값은 0~1이어야 합니다.");
        }
    }

    [Serializable]
    public sealed class MaterialCorrectionData
    {
        // Which PMX material: its index and name in the model when the correction was made. Both
        // must still match, or the name must be unique in the model, for it to apply (see
        // MmdMaterialStyler.ResolveCorrections); otherwise it is kept but not applied.
        public int index;
        public string name = "";
        public Color tint = Color.white;  // RGB multiplier
        public float brightness = 1;      // 0..2
        public float gloss;               // 0..1: toon highlight / PBR smoothness toward 1; 0 = the style's own
        public bool Neutral => tint.r == 1 && tint.g == 1 && tint.b == 1 && brightness == 1 && gloss == 0;
    }

    // Outcome of matching the stored entries against the loaded model.
    internal sealed class CorrectionResolution
    {
        public readonly Dictionary<int, MaterialCorrectionData> ByPmx = new Dictionary<int, MaterialCorrectionData>();
        public readonly Dictionary<MaterialCorrectionData, string> Problems = new Dictionary<MaterialCorrectionData, string>();
        public readonly Dictionary<MaterialCorrectionData, int> Moved = new Dictionary<MaterialCorrectionData, int>();
    }
}
