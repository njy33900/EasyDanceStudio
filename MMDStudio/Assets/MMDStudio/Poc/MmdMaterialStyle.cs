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
        public float smoothness = .3f, metallic = 0; // pbr

        public MaterialStyleData Clone() => (MaterialStyleData)MemberwiseClone();
        public void Validate()
        {
            if (style == null) style = Original;
            if (style != Original && style != Toon && style != Pbr) throw new ArgumentOutOfRangeException(nameof(style), "재질 스타일 값이 올바르지 않습니다: " + style);
            Range(shadowBorder, -1, 1, "명암 경계 위치는 -1~1이어야 합니다."); Range(shadowStrength, 0, 1, "명암 강도는 0~1이어야 합니다.");
            Range(rimStrength, 0, 1, "림 라이트 강도는 0~1이어야 합니다."); Range(outlineWidth, 0, 3, "윤곽선 두께는 0~3배여야 합니다.");
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
                if (mesh.renderer != null) slots.Add(new Slot { Renderer = mesh.renderer, Originals = mesh.renderer.sharedMaterials, PmxIndices = mesh.materialIndices });
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
            foreach (var slot in slots) if (slot.Renderer != null) slot.Renderer.sharedMaterials = slot.Originals;
            foreach (var material in created) if (material != null) UnityEngine.Object.Destroy(material);
            created.Clear();
            foreach (var slot in slots) { slot.Toon = null; slot.Pbr = null; }
            Current = MaterialStyleData.Original;
        }
    }
}
