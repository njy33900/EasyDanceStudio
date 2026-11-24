using System;
using System.Collections.Generic;
using System.Linq;
using UMT;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmdStudio.Poc
{
    // H2b-01: per-object material style, a scene override that never touches the PMX or its
    // textures. "" keeps the imported materials (URP Unlit fallback); "toon" (characters and
    // backgrounds) uses EasyDanceStudio/Toon; "pbr" (backgrounds) uses EasyDanceStudio/StageLit.
    [Serializable]
    public sealed class MaterialStyleData
    {
        public const string Original = "", Toon = "toon", Pbr = "pbr";
        public string style = Original;
        public bool shadow = true;          // 명암 경계
        public float shadowBorder = 0;      // -1..1, where light turns to shadow (N.L)
        public float shadowStrength = 1;    // 0..1, how dark the shadow side is
        public bool rim = true;
        public float rimStrength = .3f;     // 0..1
        public bool outline = true;         // only where the PMX material draws an edge
        public float outlineWidth = 1;      // 0..3 x the PMX edge size
        // 2026-11-24 (additive): how wide the light-to-shadow gradient is (0.01 hard step .. 0.5 soft,
        // the default keeps the earlier look) and how much environment light fills the shadow side.
        public float shadowSoftness = .04f;
        public float ambient;               // 0..1
        public float smoothness = .3f, metallic = 0; // pbr

        public MaterialStyleData Clone() => (MaterialStyleData)MemberwiseClone();
        public void Validate()
        {
            if (style == null) style = Original;
            if (style != Original && style != Toon && style != Pbr) throw new ArgumentOutOfRangeException(nameof(style), "재질 스타일 값이 올바르지 않습니다: " + style);
            Range(shadowBorder, -1, 1, "명암 경계 위치는 -1~1이어야 합니다."); Range(shadowStrength, 0, 1, "명암 강도는 0~1이어야 합니다.");
            Range(rimStrength, 0, 1, "림 라이트 강도는 0~1이어야 합니다."); Range(outlineWidth, 0, 3, "윤곽선 두께는 0~3배여야 합니다.");
            Range(shadowSoftness, .01f, .5f, "명암 부드러움은 0.01~0.5여야 합니다."); Range(ambient, 0, 1, "환경광 받기는 0~1이어야 합니다.");
            Range(smoothness, 0, 1, "매끄러움은 0~1이어야 합니다."); Range(metallic, 0, 1, "금속성은 0~1이어야 합니다.");
        }
        private static void Range(float value, float min, float max, string message)
        {
            if (float.IsNaN(value) || value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value), message);
        }
        public bool Same(MaterialStyleData other) => other != null && JsonUtility.ToJson(this) == JsonUtility.ToJson(other);
    }

    internal sealed class MmdMaterialStyler : IDisposable
    {
        private sealed class Slot
        {
            public Renderer Renderer;
            public Material[] Originals;
            public int[] PmxIndices;
            public Material[] Toon, Pbr;
        }
        private static Shader toonShader, pbrShader;
        private readonly PMXImportResult model;
        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<Material> created = new List<Material>();
        public string Current { get; private set; } = MaterialStyleData.Original;
        public int MaterialCount { get; }
        public int TransparentCount { get; private set; }
        public int EdgeCount { get; private set; }
        public int SphereCount { get; private set; }
        public int ToonMapCount { get; private set; }
        public int UnsupportedCount { get; private set; }
        internal IEnumerable<Material> StyledMaterials => created;

        public static Shader ToonShader => toonShader != null ? toonShader : toonShader = Resources.Load<Shader>("Shaders/EdsToon");
        public static Shader PbrShader => pbrShader != null ? pbrShader : pbrShader = Resources.Load<Shader>("Shaders/EdsStageLit");

        public MmdMaterialStyler(PMXImportResult model)
        {
            this.model = model;
            foreach (var mesh in model.meshes)
                if (mesh.renderer != null) slots.Add(new Slot { Renderer = mesh.renderer, Originals = mesh.renderer.sharedMaterials, PmxIndices = mesh.materialIndices ?? new int[0] });
            foreach (var slot in slots) foreach (var index in slot.PmxIndices) if (index >= 0) drawn.Add(index);
            MaterialCount = slots.Sum(s => s.Originals.Length);
            var pmx = model.model?.materials ?? new PMXMaterial[0];
            foreach (var slot in slots)
                for (var i = 0; i < slot.Originals.Length; i++)
                {
                    var index = i < slot.PmxIndices.Length ? slot.PmxIndices[i] : -1;
                    if (index < 0 || index >= pmx.Length || slot.Originals[i] == null) { UnsupportedCount++; continue; }
                    var material = pmx[index];
                    if (IsTransparent(slot.Originals[i])) TransparentCount++;
                    if ((material.drawingFlags & PMXMaterial.DrawingFlags.DrawEdge) != 0) EdgeCount++;
                    if (material.sphereTextureMode != PMXMaterial.SphereTextureMode.None && material.sphereTextureIndex >= 0) SphereCount++;
                    if (ToonMap(material) != null) ToonMapCount++;
                }
        }

        // Fallbacks and limits in the user's words (empty when everything maps).
        public string[] Warnings(string style)
        {
            var list = new List<string>();
            if (style == MaterialStyleData.Original) return list.ToArray();
            if (UnsupportedCount > 0) list.Add("PMX 재질과 연결되지 않은 " + UnsupportedCount + "개 재질은 원본 그대로 둡니다");
            if (SphereCount > 0) list.Add("스피어 맵(" + SphereCount + "개 재질)은 이 스타일에서 표시하지 않습니다");
            if (style == MaterialStyleData.Toon && ToonMapCount < MaterialCount - UnsupportedCount)
                list.Add("모델 툰 텍스처가 없는 " + (MaterialCount - UnsupportedCount - ToonMapCount) + "개 재질은 기본 그림자 색을 씁니다");
            if (TransparentCount > 0) list.Add("반투명 " + TransparentCount + "개 재질은 원래 그리기 순서를 유지합니다");
            return list.ToArray();
        }

        public void Apply(MaterialStyleData data)
        {
            data.Validate();
            if (data.style != MaterialStyleData.Original && (ToonShader == null || PbrShader == null))
                throw new InvalidOperationException("재질 스타일 셰이더를 찾을 수 없습니다(빌드에 포함되지 않음). 원본 재질을 유지합니다.");
            foreach (var slot in slots)
            {
                if (data.style == MaterialStyleData.Original) { slot.Renderer.sharedMaterials = slot.Originals; continue; }
                var toon = data.style == MaterialStyleData.Toon;
                var set = toon ? slot.Toon ?? (slot.Toon = Build(slot, true)) : slot.Pbr ?? (slot.Pbr = Build(slot, false));
                for (var i = 0; i < set.Length; i++)
                    if (set[i] != slot.Originals[i]) Configure(set[i], Pmx(slot, i), data, toon);
                slot.Renderer.sharedMaterials = set;
            }
            Current = data.style;
            RefreshCorrection(); // the corrected colour starts from the new material's own colour
        }

        // ----- v0.61 screen correction (see MmdMaterialCorrection.cs) -----
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), GlossId = Shader.PropertyToID("_Gloss"), SmoothnessId = Shader.PropertyToID("_Smoothness");
        private readonly HashSet<int> drawn = new HashSet<int>();
        private ColorCorrectionData correction = new ColorCorrectionData();
        private bool correctionShown = true;
        private MaterialPropertyBlock block;
        internal CorrectionResolution Resolution { get; private set; } = new CorrectionResolution();
        // Material slots that currently carry a correction block (0 when nothing is corrected or shown).
        internal int CorrectedSlots { get; private set; }
        public int PmxMaterialCount => model.model?.materials?.Length ?? 0;
        // The material's name as written in the PMX (Japanese name; the importer's ASCII name when empty).
        public string PmxName(int index)
        {
            var pmx = model.model?.materials;
            if (pmx == null || index < 0 || index >= pmx.Length) return "";
            var name = pmx[index].originalName.ToString();
            return string.IsNullOrEmpty(name) ? pmx[index].renamedName.ToString() : name;
        }
        public bool PmxDrawn(int index) => drawn.Contains(index);
        // PMX material of a renderer's sub-mesh (picking), -1 when it maps to none.
        public int PmxIndexOf(Renderer renderer, int subMesh)
        {
            foreach (var slot in slots)
                if (slot.Renderer == renderer) return subMesh >= 0 && subMesh < slot.PmxIndices.Length ? slot.PmxIndices[subMesh] : -1;
            return -1;
        }
        // What this style can take: every style has a colour; gloss needs a lit style.
        public static bool GlossSupported(string style) => style == MaterialStyleData.Toon || style == MaterialStyleData.Pbr;

        public void ApplyCorrection(ColorCorrectionData data, bool shown)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            correction = data.Clone(); correctionShown = shown;
            RefreshCorrection();
        }
        public void ShowCorrection(bool shown)
        {
            if (correctionShown == shown) return;
            correctionShown = shown; RefreshCorrection();
        }

        // Matches stored entries to this model's PMX materials. An entry applies when the material
        // at its index still has its name, or (the model was edited) when exactly one material has
        // that name. Otherwise it is kept but applied to nothing, with the reason.
        internal static CorrectionResolution ResolveCorrections(ColorCorrectionData data, int count, Func<int, string> nameOf)
        {
            var result = new CorrectionResolution();
            var names = new Dictionary<string, List<int>>();
            for (var i = 0; i < count; i++)
            {
                var name = nameOf(i);
                if (!names.TryGetValue(name, out var list)) names[name] = list = new List<int>();
                list.Add(i);
            }
            // Exact matches (same index, same name) first, so they win over name-only matches.
            var pending = new List<MaterialCorrectionData>();
            foreach (var entry in data.materials)
            {
                if (entry.index >= 0 && entry.index < count && nameOf(entry.index) == entry.name && !result.ByPmx.ContainsKey(entry.index)) result.ByPmx[entry.index] = entry;
                else pending.Add(entry);
            }
            foreach (var entry in pending)
            {
                if (!names.TryGetValue(entry.name, out var same) || same.Count == 0) { result.Problems[entry] = "모델에 이 이름의 재질이 없습니다(모델이 바뀌었을 수 있음)"; continue; }
                if (same.Count > 1) { result.Problems[entry] = "같은 이름의 재질이 " + same.Count + "개이고 번호도 달라 어느 재질인지 알 수 없습니다"; continue; }
                if (result.ByPmx.ContainsKey(same[0])) { result.Problems[entry] = "다른 보정 항목과 같은 재질을 가리킵니다"; continue; }
                result.ByPmx[same[0]] = entry; result.Moved[entry] = same[0];
            }
            return result;
        }

        private void RefreshCorrection()
        {
            Resolution = ResolveCorrections(correction, PmxMaterialCount, PmxName);
            CorrectedSlots = 0;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var slot in slots)
            {
                if (slot.Renderer == null) continue;
                var current = slot.Renderer.sharedMaterials;
                for (var i = 0; i < current.Length; i++)
                {
                    var material = current[i];
                    var pmx = i < slot.PmxIndices.Length ? slot.PmxIndices[i] : -1;
                    MaterialCorrectionData entry = null;
                    if (pmx >= 0) Resolution.ByPmx.TryGetValue(pmx, out entry);
                    var objectPart = !correction.ObjectNeutral; var materialPart = entry != null && !entry.Neutral;
                    // Materials not linked to a PMX material are never corrected (they cannot be identified).
                    if (!correctionShown || material == null || pmx < 0 || (!objectPart && !materialPart)) { slot.Renderer.SetPropertyBlock(null, i); continue; }
                    block.Clear();
                    if (material.HasProperty(BaseColorId))
                    {
                        var colour = material.GetColor(BaseColorId);
                        var k = correction.brightness * (entry?.brightness ?? 1);
                        colour.r *= correction.tint.r * (entry?.tint.r ?? 1) * k;
                        colour.g *= correction.tint.g * (entry?.tint.g ?? 1) * k;
                        colour.b *= correction.tint.b * (entry?.tint.b ?? 1) * k;
                        block.SetColor(BaseColorId, colour);
                    }
                    if (entry != null && entry.gloss > 0)
                    {
                        if (material.shader == ToonShader) block.SetFloat(GlossId, entry.gloss);
                        else if (material.shader == PbrShader) block.SetFloat(SmoothnessId, Mathf.Lerp(material.GetFloat(SmoothnessId), 1, entry.gloss));
                    }
                    if (block.isEmpty) { slot.Renderer.SetPropertyBlock(null, i); continue; }
                    slot.Renderer.SetPropertyBlock(block, i); CorrectedSlots++;
                }
            }
        }
        private void ClearCorrectionBlocks()
        {
            foreach (var slot in slots)
                if (slot.Renderer != null) for (var i = 0; i < slot.Renderer.sharedMaterials.Length; i++) slot.Renderer.SetPropertyBlock(null, i);
            CorrectedSlots = 0;
        }

        private PMXMaterial? Pmx(Slot slot, int i)
        {
            var pmx = model.model?.materials;
            var index = i < slot.PmxIndices.Length ? slot.PmxIndices[i] : -1;
            return pmx != null && index >= 0 && index < pmx.Length ? pmx[index] : (PMXMaterial?)null;
        }

        private Material[] Build(Slot slot, bool toon)
        {
            var set = new Material[slot.Originals.Length];
            for (var i = 0; i < set.Length; i++)
            {
                var original = slot.Originals[i];
                var pmx = Pmx(slot, i);
                if (original == null || pmx == null) { set[i] = original; continue; } // unsupported: keep the original
                var material = new Material(toon ? ToonShader : PbrShader) { name = original.name + (toon ? " (Toon)" : " (PBR)") };
                created.Add(material);
                var texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : null;
                if (texture == null && original.HasProperty("_MainTex")) texture = original.GetTexture("_MainTex");
                if (texture != null) material.SetTexture("_BaseMap", texture);
                var p = pmx.Value;
                // MMD's lit colour under its default light (0.6): ambient + diffuse x 0.6.
                material.SetColor("_BaseColor", new Color(Mathf.Clamp01(p.ambient.x + p.diffuse.r * .6f), Mathf.Clamp01(p.ambient.y + p.diffuse.g * .6f),
                    Mathf.Clamp01(p.ambient.z + p.diffuse.b * .6f), p.diffuse.a));
                material.SetFloat("_Cull", original.HasProperty("_Cull") ? original.GetFloat("_Cull") : (float)CullMode.Back);
                if (IsTransparent(original))
                {
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", original.HasProperty("_ZWrite") ? original.GetFloat("_ZWrite") : 0);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetFloat("_Surface", 1);
                }
                material.renderQueue = original.renderQueue; // keeps UMT's draw order (transparent sorting)
                if (toon)
                {
                    // The model's toon ramp: its darker end is the shadow colour, the other the lit
                    // colour (whichever way the image is flipped). Unreadable ramps use the defaults.
                    var ramp = ToonMap(p);
                    if (ramp != null && ramp.isReadable)
                    {
                        Color a = ramp.GetPixelBilinear(.5f, .04f), b = ramp.GetPixelBilinear(.5f, .96f);
                        var aDarker = a.grayscale < b.grayscale;
                        material.SetColor("_ShadowTint", aDarker ? a : b); material.SetColor("_LitTint", aDarker ? b : a);
                    }
                    material.SetColor("_OutlineColor", p.edgeColor);
                    // v0.61 gloss: the highlight size follows the PMX specular power (MMD's 反射強度).
                    material.SetFloat("_GlossPower", p.specularStrength > 0 ? Mathf.Clamp(p.specularStrength, 8, 128) : 32);
                }
                set[i] = material;
            }
            return set;
        }

        private static void Configure(Material material, PMXMaterial? pmx, MaterialStyleData data, bool toon)
        {
            if (!toon) { material.SetFloat("_Smoothness", data.smoothness); material.SetFloat("_Metallic", data.metallic); return; }
            material.SetFloat("_ShadowStrength", data.shadow ? data.shadowStrength : 0);
            material.SetFloat("_ShadowBorder", data.shadowBorder);
            material.SetFloat("_ShadowSoftness", data.shadowSoftness);
            material.SetFloat("_Ambient", data.ambient);
            material.SetFloat("_RimStrength", data.rim ? data.rimStrength : 0);
            var edge = pmx.HasValue && (pmx.Value.drawingFlags & PMXMaterial.DrawingFlags.DrawEdge) != 0 && pmx.Value.edgeSize > 0;
            var outline = data.outline && edge && data.outlineWidth > 0;
            material.SetFloat("_OutlineWidth", outline ? pmx.Value.edgeSize * data.outlineWidth : 0);
        }

        private Texture2D ToonMap(PMXMaterial material)
        {
            if (material.toonTextureReferenceFlag) return null; // MMD's shared toon01-10 are not shipped with models
            var index = material.toonTextureIndex;
            return index >= 0 && index < model.texturesByIndex.Length ? model.texturesByIndex[index] : null;
        }

        private static bool IsTransparent(Material material) =>
            material.renderQueue >= (int)RenderQueue.Transparent || material.GetTag("RenderType", false) == "Transparent" ||
            (material.HasProperty("_Surface") && material.GetFloat("_Surface") > .5f);

        // While the application quits, everything is torn down anyway: touching renderers that are
        // being destroyed (or destroying materials they still reference) is not needed.
        private static bool quitting;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void WatchQuit() { quitting = false; Application.quitting += () => quitting = true; }

        public void Dispose()
        {
            if (quitting) { created.Clear(); return; }
            ClearCorrectionBlocks();
            foreach (var slot in slots) if (slot.Renderer != null) slot.Renderer.sharedMaterials = slot.Originals;
            foreach (var material in created) if (material != null) UnityEngine.Object.Destroy(material);
            created.Clear();
            foreach (var slot in slots) { slot.Toon = null; slot.Pbr = null; }
            Current = MaterialStyleData.Original;
        }
    }
}
