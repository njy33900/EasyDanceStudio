// H1-01b: frame-sliced texture and material stages for the pinned UMT PMX importer.
//
// The texture decoding, alpha-coverage inputs and material setup below are ported from
// UnityMMDTools (https://github.com/CandidumGames/UnityMMDTools) at commit
// db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4, Runtime/PMX/PMXTextureLoader.cs and
// Runtime/PMX/PMXMaterialBuilder.cs. Copyright (c) 2025-present Candidum Games, MIT License
// (see the package's LICENSE.md). Changes: work is split per texture/material with frame
// yields and cancellation checks, file bytes are read off the main thread, decoded texture
// pixels are reused for alpha detection instead of decoding the files a second time, and the
// per-material debug log is omitted. The resulting textures and materials are handed to
// PMXImporter through the public loadTextures/materialOverrides options, so the rest of the
// package build is unchanged.
using B83.Image.BMP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UMT;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MmdStudio.Poc
{
    internal sealed class MmdStagedPmxBuild : IDisposable
    {
        public Texture2D[] TexturesByIndex { get; private set; }
        public Dictionary<string, Material> MaterialOverrides { get; } = new Dictionary<string, Material>();
        public List<string> Warnings { get; } = new List<string>();
        public int SharedNameMaterials { get; private set; }
        public double TextureMilliseconds { get; private set; }
        public double MaterialMilliseconds { get; private set; }
        public double LongestTextureStepMilliseconds { get; private set; }
        public double LongestMaterialStepMilliseconds { get; private set; }
        // Created objects stay owned here until TransferTo hands them to the import result.
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public static async Task<MmdStagedPmxBuild> RunAsync(PMXModel model, PMXImportOptions options, UMTFrameBudget budget,
            CancellationToken cancellationToken, Action<string> stage)
        {
            var build = new MmdStagedPmxBuild();
            try
            {
                var pixels = await build.LoadTexturesAsync(model, options, budget, cancellationToken, stage);
                await build.BuildMaterialsAsync(model, options, pixels, budget, cancellationToken, stage);
                return build;
            }
            catch
            {
                build.Dispose();
                throw;
            }
        }

        // Hand the prepared stages to the package build.
        public void Apply(PMXImportOptions options)
        {
            var textures = TexturesByIndex;
            options.loadTextures = (model, _) => textures;
            options.materialOverrides = MaterialOverrides;
        }

        // After a successful package build: texture warnings come first, as the package's own
        // texture stage records them before any later stage, and ownership moves to the result.
        public void TransferTo(PMXImportResult result)
        {
            var insertAt = 0;
            foreach (var warning in Warnings)
                if (!result.warnings.Contains(warning)) result.warnings.Insert(insertAt++, warning);
            owned.Clear();
        }

        public void Dispose()
        {
            foreach (var item in owned) PMXUtilities.DestroyRuntimeObject(item);
            owned.Clear();
        }

        private void Warn(string message)
        {
            if (Warnings.Contains(message)) return;
            Warnings.Add(message);
            Debug.LogWarning("[PMX Import] " + message);
        }

        // ----- Textures (PMXTextureLoader.Load + the source pixels PMXMaterialBuilder samples) -----
        private async Task<PMXMaterialBuilder.SourcePixels[]> LoadTexturesAsync(PMXModel model, PMXImportOptions options, UMTFrameBudget budget,
            CancellationToken cancellationToken, Action<string> stage)
        {
            var total = Stopwatch.StartNew();
            var count = model.texturePaths.Length;
            TexturesByIndex = new Texture2D[count];
            var pixelsByIndex = new PMXMaterialBuilder.SourcePixels[count];
            var textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var pixelsByPath = new Dictionary<string, PMXMaterialBuilder.SourcePixels>(StringComparer.OrdinalIgnoreCase);
            var baseDirectory = !string.IsNullOrEmpty(options.textureBaseDirectory) ? options.textureBaseDirectory : Path.GetDirectoryName(options.sourcePath);
            // Like SourcePixelCache, only main textures are sampled for alpha detection; other
            // textures (e.g. sphere maps) are decoded for rendering only.
            var mainTextures = new HashSet<int>(model.materials.Select(material => material.textureIndex));
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stage?.Invoke("텍스처 불러오는 중 (" + (i + 1) + "/" + count + ")…");
                var texturePath = model.texturePaths[i].ToString();
                if (string.IsNullOrWhiteSpace(texturePath)) continue;
                var fullPath = ResolveTexturePath(texturePath, baseDirectory);
                if (string.IsNullOrEmpty(fullPath)) { Warn("Texture was not found: " + texturePath); continue; }
                if (textures.TryGetValue(fullPath, out var cached))
                {
                    TexturesByIndex[i] = cached;
                    if (mainTextures.Contains(i))
                    {
                        if (!pixelsByPath.TryGetValue(fullPath, out var known) || !known.IsValid)
                        {
                            DecodeTexture(fullPath, File.ReadAllBytes(fullPath), true, out known, discardTexture: true);
                            pixelsByPath[fullPath] = known;
                        }
                        pixelsByIndex[i] = known;
                    }
                    continue;
                }
                var bytes = await Task.Run(() => File.ReadAllBytes(fullPath), cancellationToken);
                var step = Stopwatch.StartNew();
                var texture = DecodeTexture(fullPath, bytes, mainTextures.Contains(i), out var sourcePixels);
                pixelsByIndex[i] = sourcePixels;
                if (texture == null) Warn("Texture could not be decoded: " + fullPath);
                else
                {
                    texture.name = PMXUtilities.SanitizeFileName(Path.GetFileNameWithoutExtension(fullPath), i);
                    owned.Add(texture);
                    textures[fullPath] = texture; pixelsByPath[fullPath] = sourcePixels;
                    TexturesByIndex[i] = texture;
                }
                LongestTextureStepMilliseconds = Math.Max(LongestTextureStepMilliseconds, step.Elapsed.TotalMilliseconds);
                await budget.YieldIfNeeded();
            }
            TextureMilliseconds = total.Elapsed.TotalMilliseconds;
            options.timingCallback?.Invoke("Load Textures (staged)", total.Elapsed);
            return pixelsByIndex;
        }

        // Same decoders as the package. Texture: PMXTextureLoader.DecodeTexture. Pixels: what
        // PMXMaterialBuilder.SourcePixelCache would decode from the same file.
        private static Texture2D DecodeTexture(string fullPath, byte[] bytes, bool wantPixels, out PMXMaterialBuilder.SourcePixels pixels, bool discardTexture = false)
        {
            pixels = default;
            var extension = Path.GetExtension(fullPath);
            if (string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase))
            {
                Texture2D texture = null;
                if (!discardTexture) using (var stream = new MemoryStream(bytes, false)) texture = UMT.ThirdParty.TGALoader.LoadTGA(stream);
                if (wantPixels) using (var stream = new MemoryStream(bytes, false))
                {
                    var colours = UMT.ThirdParty.TGALoader.LoadTGA(stream, out var width, out var height, out _);
                    pixels = new PMXMaterialBuilder.SourcePixels(colours, width, height);
                }
                return texture;
            }
            if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase))
            {
                var texture = discardTexture ? null : new BMPLoader().LoadBMP(bytes)?.ToTexture2D();
                if (wantPixels)
                {
                    // SourcePixelCache would throw on an undecodable main BMP; that file simply
                    // has no usable pixels here (treated as opaque) instead of failing the load.
                    var bmp = new BMPLoader().LoadBMP(bytes);
                    if (bmp != null)
                    {
                        var width = bmp.info.absWidth; var height = bmp.info.absHeight;
                        if (bmp.info.height < 0) FlipRowsInPlace(bmp.imageData, width, height);
                        pixels = new PMXMaterialBuilder.SourcePixels(bmp.imageData, width, height);
                    }
                }
                return texture;
            }
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var ok = ImageConversion.LoadImage(decoded, bytes);
            // SourcePixelCache reads the pixels of its own throwaway LoadImage texture even when
            // decoding fails, so the alpha detection input is taken the same way here.
            if (wantPixels) pixels = new PMXMaterialBuilder.SourcePixels(decoded.GetPixels32(), decoded.width, decoded.height);
            if (ok && !discardTexture) return decoded;
            UnityEngine.Object.Destroy(decoded);
            return null;
        }

        private static void FlipRowsInPlace(Color32[] pixels, int width, int height)
        {
            for (var y = 0; y < height / 2; ++y)
            {
                int top = y * width, bottom = (height - 1 - y) * width;
                for (var x = 0; x < width; ++x) (pixels[top + x], pixels[bottom + x]) = (pixels[bottom + x], pixels[top + x]);
            }
        }

        private static string ResolveTexturePath(string texturePath, string baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory)) return null;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, texturePath),
                Path.Combine(baseDirectory, texturePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)),
            };
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (!visited.Add(candidate)) continue;
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath)) return fullPath;
            }
            var fileName = Path.GetFileName(texturePath);
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (var file in Directory.GetFiles(baseDirectory, "*", SearchOption.AllDirectories))
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        // ----- Materials (PMXMaterialBuilder.Build, one material per step) -----
        private async Task BuildMaterialsAsync(PMXModel model, PMXImportOptions options, PMXMaterialBuilder.SourcePixels[] pixelsByIndex,
            UMTFrameBudget budget, CancellationToken cancellationToken, Action<string> stage)
        {
            var total = Stopwatch.StartNew();
            var count = model.materials.Length;
            var names = new string[count];
            for (var i = 0; i < count; i++) names[i] = PMXUtilities.SanitizeFileName(model.materials[i].renamedName.ToString(), i);
            // materialOverrides is keyed by name: materials sharing a name are left to the package
            // builder so each still gets its own material.
            var shared = new HashSet<string>(names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key));
            var indicesOffset = 0;
            for (var i = 0; i < count; i++)
            {
                var pmxMaterial = model.materials[i];
                var faceIndexStart = indicesOffset; var faceIndexCount = pmxMaterial.faceIndexCount;
                indicesOffset += faceIndexCount;
                if (shared.Contains(names[i])) { SharedNameMaterials++; continue; }
                cancellationToken.ThrowIfCancellationRequested();
                stage?.Invoke("재질 만드는 중 (" + (i + 1) + "/" + count + ")…");
                var step = Stopwatch.StartNew();
                var mainTexture = GetTexture(TexturesByIndex, pmxMaterial.textureIndex);
                var sphereTexture = GetTexture(TexturesByIndex, pmxMaterial.sphereTextureIndex);
                var mainPixels = pmxMaterial.textureIndex >= 0 && pmxMaterial.textureIndex < pixelsByIndex.Length ? pixelsByIndex[pmxMaterial.textureIndex] : default;
                var alpha = PMXMaterialBuilder.DetectAlphaCoverage(mainPixels, model, faceIndexStart, faceIndexCount, options.alphaDetectionThreshold);
                var shouldBeTransparent = alpha.isBelowAlphaCoverageThreshold || alpha.anyPixelFullyTransparent || pmxMaterial.diffuse.a < 1.0f;
                var alphaCoverage = Mathf.Min(alpha.alphaCoverage, pmxMaterial.diffuse.a);
                var anyPixelOpaque = alpha.anyPixelOpaque && !(pmxMaterial.diffuse.a < 1.0f);
                var transparentWithZWrite = anyPixelOpaque || alphaCoverage >= options.alphaCoverageZWriteThreshold;
                var keepEdge = !alpha.isBelowAlphaCoverageThreshold && !(pmxMaterial.diffuse.a < 1.0f);
                var material = BuildMaterial(pmxMaterial, mainTexture, sphereTexture, names[i], shouldBeTransparent, transparentWithZWrite, keepEdge);
                if (material != null)
                {
                    material.renderQueue = shouldBeTransparent ? 3000 + i : 2500 + i;
                    owned.Add(material);
                    MaterialOverrides[names[i]] = material;
                }
                LongestMaterialStepMilliseconds = Math.Max(LongestMaterialStepMilliseconds, step.Elapsed.TotalMilliseconds);
                await budget.YieldIfNeeded();
            }
            MaterialMilliseconds = total.Elapsed.TotalMilliseconds;
            options.timingCallback?.Invoke("Build Materials (staged)", total.Elapsed);
        }

        private static Texture2D GetTexture(Texture2D[] texturesByIndex, int textureIndex) =>
            textureIndex >= 0 && textureIndex < texturesByIndex.Length ? texturesByIndex[textureIndex] : null;

        // ----- Port of PMXMaterialBuilder's private material setup -----
        private const string LilToonMultiShaderName = "_lil/lilToonMulti";
        private const string LilToonMultiOutlineShaderName = "Hidden/lilToonMultiOutline";
        private const string UrpUnlitFallbackShaderName = "Universal Render Pipeline/Unlit";
        private const string BuiltInUnlitShaderName = "Unlit/Texture";
        private const string BuiltInUnlitTransparentShaderName = "Unlit/Transparent";
        private const string UrpSurfaceTypeTransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";
        private const string LilToonRequireUV2Keyword = "_REQUIRE_UV2";
        private const string UnityUIClipRectKeyword = "UNITY_UI_CLIP_RECT";
        private const float OutlineWidthScale = 0.0025f;
        private const float LilToonOutlineWidthUnit = 0.01f;
        private static readonly int BaseColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseMapProperty = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTextureProperty = Shader.PropertyToID("_MainTex");
        private static readonly int ShadowColorProperty = Shader.PropertyToID("_ShadowColor");
        private static readonly int ShadowReceiveProperty = Shader.PropertyToID("_ShadowReceive");
        private static readonly int ShadowBorderProperty = Shader.PropertyToID("_ShadowBorder");
        private static readonly int LilShadowCasterBiasProperty = Shader.PropertyToID("_lilShadowCasterBias");
        private static readonly int Shadow2ndColorProperty = Shader.PropertyToID("_Shadow2ndColor");
        private static readonly int ShadowBorderColorProperty = Shader.PropertyToID("_ShadowBorderColor");
        private static readonly int ShadowColorTextureProperty = Shader.PropertyToID("_ShadowColorTex");
        private static readonly int UseShadowProperty = Shader.PropertyToID("_UseShadow");
        private static readonly int DoubleSidedProperty = Shader.PropertyToID("_DoubleSided");
        private static readonly int CullModeProperty = Shader.PropertyToID("_Cull");
        private static readonly int UseOutlineProperty = Shader.PropertyToID("_UseOutline");
        private static readonly int OutlineWidthProperty = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineColorProperty = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineEnableLightingProperty = Shader.PropertyToID("_OutlineEnableLighting");
        private static readonly int MatcapTextureProperty = Shader.PropertyToID("_MatcapTex");
        private static readonly int MatcapColorProperty = Shader.PropertyToID("_MatcapColor");
        private static readonly int LilToonMatcapTextureProperty = Shader.PropertyToID("_MatCapTex");
        private static readonly int LilToonMatcapColorProperty = Shader.PropertyToID("_MatCapColor");
        private static readonly int UseMatcapProperty = Shader.PropertyToID("_UseMatCap");
        private static readonly int LilToonTransparentModeProperty = Shader.PropertyToID("_TransparentMode");
        private static readonly int ShadingToonyProperty = Shader.PropertyToID("_ShadingToonyFactor");
        private static readonly int ShadingShiftProperty = Shader.PropertyToID("_ShadingShiftFactor");
        private static readonly int GiEqualizationProperty = Shader.PropertyToID("_GiEqualization");
        private static readonly int EditModeProperty = Shader.PropertyToID("_M_EditMode");
        private static readonly int UrpSurfaceProperty = Shader.PropertyToID("_Surface");
        private static readonly int UrpBlendProperty = Shader.PropertyToID("_Blend");
        private static readonly int UrpSrcBlendProperty = Shader.PropertyToID("_SrcBlend");
        private static readonly int UrpDstBlendProperty = Shader.PropertyToID("_DstBlend");
        private static readonly int UrpSrcBlendAlphaProperty = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int UrpDstBlendAlphaProperty = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int UrpZWriteProperty = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaToMaskProperty = Shader.PropertyToID("_AlphaToMask");
        private static readonly int OutlineSrcBlendProperty = Shader.PropertyToID("_OutlineSrcBlend");
        private static readonly int OutlineDstBlendProperty = Shader.PropertyToID("_OutlineDstBlend");
        private static readonly int OutlineSrcBlendAlphaProperty = Shader.PropertyToID("_OutlineSrcBlendAlpha");
        private static readonly int OutlineDstBlendAlphaProperty = Shader.PropertyToID("_OutlineDstBlendAlpha");
        private static readonly int OutlineZWriteProperty = Shader.PropertyToID("_OutlineZWrite");
        private static readonly int OutlineAlphaToMaskProperty = Shader.PropertyToID("_OutlineAlphaToMask");
        private static readonly string[] FaceHeuristics = { "face", "skin", "body", "head", "kao", "kawa", "hifu", "hada", "lian" };
        private static readonly string[] EyeHeuristics = { "eye", "iris", "hitomi", "yan" };
        private enum ShaderKind { LilToon, Urp, BuiltIn }

        private static bool Heuristic(PMXMaterial material, string[] heuristics)
        {
            var lower = material.renamedName.ToString().ToLowerInvariant();
            foreach (var heuristic in heuristics) if (lower.Contains(heuristic)) return true;
            return false;
        }

        private static Material BuildMaterial(PMXMaterial pmxMaterial, Texture2D mainTexture, Texture2D sphereTexture, string materialName,
            bool transparent, bool transparentWithZWrite, bool keepEdge)
        {
            var drawsEdge = (pmxMaterial.drawingFlags & PMXMaterial.DrawingFlags.DrawEdge) != 0 && (!transparent || keepEdge);
            var shader = GetShader(transparent, transparentWithZWrite, drawsEdge, out var shaderKind);
            if (shader == null)
            {
                Debug.LogError("No compatible PMX material shader was found.");
                return null;
            }
            var material = new Material(shader) { name = materialName };
            SetColor(material, BaseColorProperty, pmxMaterial.diffuse);
            if (mainTexture != null)
            {
                SetTexture(material, MainTextureProperty, mainTexture);
                SetTexture(material, BaseMapProperty, mainTexture);
            }
            var isDoubleSided = (pmxMaterial.drawingFlags & PMXMaterial.DrawingFlags.DoubleSided) != 0;
            SetFloat(material, CullModeProperty, isDoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (shaderKind == ShaderKind.LilToon) ConfigureLilToonShading(material, pmxMaterial, mainTexture, sphereTexture, isDoubleSided, drawsEdge);
            if (transparent)
            {
                switch (shaderKind)
                {
                    case ShaderKind.LilToon: ConfigureLilToonTransparent(material, transparentWithZWrite); break;
                    case ShaderKind.Urp: ConfigureUrpTransparent(material, transparentWithZWrite); break;
                    case ShaderKind.BuiltIn: ConfigureBuiltInTransparent(material, transparentWithZWrite); break;
                }
            }
            return material;
        }

        private static void ConfigureLilToonShading(Material material, PMXMaterial pmxMaterial, Texture2D mainTexture, Texture2D sphereTexture, bool isDoubleSided, bool drawsEdge)
        {
            var ambientColor = new Color(pmxMaterial.ambient.x, pmxMaterial.ambient.y, pmxMaterial.ambient.z, 1.0f);
            var borderColor = new Color((pmxMaterial.diffuse.r + pmxMaterial.ambient.x) * 0.5f, (pmxMaterial.diffuse.g + pmxMaterial.ambient.y) * 0.5f,
                (pmxMaterial.diffuse.b + pmxMaterial.ambient.z) * 0.5f, 1.0f);
            var isEye = Heuristic(pmxMaterial, EyeHeuristics);
            var isFace = Heuristic(pmxMaterial, FaceHeuristics);
            SetColor(material, ShadowColorProperty, ambientColor);
            SetFloat(material, ShadowReceiveProperty, 1.0f);
            SetFloat(material, LilShadowCasterBiasProperty, isFace || isEye ? 0.05f : 0.0f);
            SetFloat(material, ShadowBorderProperty, isFace ? 0.3f : isEye ? 0.1f : 0.5f);
            SetColor(material, Shadow2ndColorProperty, new Color(ambientColor.r, ambientColor.g, ambientColor.b, 0.0f));
            SetColor(material, ShadowBorderColorProperty, borderColor);
            SetFloat(material, UseShadowProperty, 1.0f);
            SetFloat(material, ShadingToonyProperty, 0.95f);
            SetFloat(material, ShadingShiftProperty, -0.05f);
            SetFloat(material, GiEqualizationProperty, 0.0f);
            SetFloat(material, EditModeProperty, 1.0f);
            if (mainTexture != null) SetTexture(material, ShadowColorTextureProperty, mainTexture);
            if (pmxMaterial.sphereTextureMode != PMXMaterial.SphereTextureMode.None && sphereTexture != null)
            {
                SetTexture(material, MatcapTextureProperty, sphereTexture);
                SetColor(material, MatcapColorProperty, Color.white);
                SetTexture(material, LilToonMatcapTextureProperty, sphereTexture);
                SetColor(material, LilToonMatcapColorProperty, Color.white);
                SetFloat(material, UseMatcapProperty, 1.0f);
            }
            SetFloat(material, DoubleSidedProperty, isDoubleSided ? 1.0f : 0.0f);
            SetFloat(material, UseOutlineProperty, drawsEdge ? 1.0f : 0.0f);
            SetFloat(material, OutlineWidthProperty, drawsEdge ? pmxMaterial.edgeSize * OutlineWidthScale / LilToonOutlineWidthUnit : 0.0f);
            SetColor(material, OutlineColorProperty, pmxMaterial.edgeColor);
            SetFloat(material, OutlineEnableLightingProperty, 0.0f);
            SetKeyword(material, LilToonRequireUV2Keyword, true);
        }

        private static void ConfigureLilToonTransparent(Material material, bool transparentWithZWrite)
        {
            SetFloat(material, LilToonTransparentModeProperty, 2.0f);
            SetFloat(material, UrpSrcBlendProperty, (float)BlendMode.One);
            SetFloat(material, UrpDstBlendProperty, (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, UrpSrcBlendAlphaProperty, (float)BlendMode.One);
            SetFloat(material, UrpDstBlendAlphaProperty, (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, AlphaToMaskProperty, 0.0f);
            SetFloat(material, OutlineSrcBlendProperty, (float)BlendMode.SrcAlpha);
            SetFloat(material, OutlineDstBlendProperty, (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, OutlineSrcBlendAlphaProperty, (float)BlendMode.One);
            SetFloat(material, OutlineDstBlendAlphaProperty, (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, OutlineZWriteProperty, transparentWithZWrite ? 1.0f : 0.0f);
            SetFloat(material, OutlineAlphaToMaskProperty, 0.0f);
            SetFloat(material, UrpSurfaceProperty, 1.0f);
            SetFloat(material, UrpBlendProperty, 0.0f);
            SetFloat(material, UrpZWriteProperty, transparentWithZWrite ? 1.0f : 0.0f);
            SetKeyword(material, UnityUIClipRectKeyword, true);
        }

        private static void ConfigureUrpTransparent(Material material, bool transparentWithZWrite)
        {
            if (transparentWithZWrite) return;
            SetFloat(material, UrpSurfaceProperty, 1.0f);
            SetFloat(material, UrpBlendProperty, 0.0f);
            SetFloat(material, UrpSrcBlendProperty, (float)BlendMode.SrcAlpha);
            SetFloat(material, UrpDstBlendProperty, (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            SetKeyword(material, UrpSurfaceTypeTransparentKeyword, true);
        }

        private static void ConfigureBuiltInTransparent(Material material, bool transparentWithZWrite)
        {
            if (transparentWithZWrite) return;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        private static Shader GetShader(bool transparent, bool transparentWithZWrite, bool drawsEdge, out ShaderKind shaderKind)
        {
            var lilToon = Shader.Find(drawsEdge ? LilToonMultiOutlineShaderName : LilToonMultiShaderName);
            if (lilToon != null) { shaderKind = ShaderKind.LilToon; return lilToon; }
            var urp = Shader.Find(UrpUnlitFallbackShaderName);
            if (urp != null) { shaderKind = ShaderKind.Urp; return urp; }
            shaderKind = ShaderKind.BuiltIn;
            return Shader.Find(transparent && !transparentWithZWrite ? BuiltInUnlitTransparentShaderName : BuiltInUnlitShaderName);
        }

        private static void SetColor(Material material, int property, Color value) { if (material.HasProperty(property)) material.SetColor(property, value); }
        private static void SetFloat(Material material, int property, float value) { if (material.HasProperty(property)) material.SetFloat(property, value); }
        private static void SetTexture(Material material, int property, Texture value) { if (material.HasProperty(property)) material.SetTexture(property, value); }
        private static void SetKeyword(Material material, string keywordName, bool enabled) => material.SetKeyword(new LocalKeyword(material.shader, keywordName), enabled);
    }
}
