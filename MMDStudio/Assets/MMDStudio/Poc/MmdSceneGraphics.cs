using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MmdStudio.Poc
{
    public enum MmdShadowQuality { Off, Low, Medium, High }
    // H2a-02: sampling quality of the screen effects (SSAO samples/resolution/blur, DOF mode, bloom filtering).
    public enum MmdEffectQuality { Low, Medium, High }

    [Serializable]
    public sealed class SavedView
    {
        public string name;
        public Vector3 pivot;
        public float yaw, pitch, distance, fieldOfView;
        public float roll; // v0.61 (additive): screen tilt
    }

    // MVP-06: camera/light/effects/background state (MMD-108..110). Plain serializable
    // data so the later .mmdscene JSON ticket can store it without Unity object references.
    [Serializable]
    public sealed class SceneGraphicsSettings
    {
        public const int MaxSavedViews = 16;
        public float fieldOfView = 45;
        public Color lightColor = Color.white;
        public float lightIntensity = 2;
        public float lightYaw = -30, lightPitch = 35;
        public MmdShadowQuality shadowQuality = MmdShadowQuality.Medium;
        public bool bloomEnabled;
        public float bloomIntensity = .8f;
        public bool tonemapping;
        public float exposure, contrast, saturation;
        public Color backgroundColor = new Color(.925f, .93f, .94f, 1); // matches the light viewport card
        // H2a-02 (all off by default; older scene files without these fields open with the defaults).
        public bool dofEnabled;
        public string dofTarget = "";      // scene object ID to keep in focus; empty = fixed distance
        public float dofDistance = 4;       // metres, used without a target (or when it is gone)
        public float dofStrength = .5f;     // 0..1 -> aperture f/16..f/1.4 at 70 mm
        public bool ssaoEnabled;
        public float ssaoIntensity = 1, ssaoRadius = .3f;
        public bool vignetteEnabled;
        public float vignetteIntensity = .3f;
        public MmdEffectQuality effectQuality = MmdEffectQuality.Medium;
        // 2026-11-24 (additive): environment (ambient) light, a flat fill from every direction that
        // lit materials (toon with "환경광 받기", PBR backgrounds) pick up. 0 keeps the scene's own.
        public float ambientIntensity;
        public Color ambientColor = new Color(.85f, .9f, 1f);
        // H2a-05: name of the look preset these values came from ("" = none); the values above are authoritative.
        public string lookPreset = "";
        public List<SavedView> savedViews = new List<SavedView>();

        public SceneGraphicsSettings Clone()
        {
            var copy = (SceneGraphicsSettings)MemberwiseClone();
            copy.savedViews = savedViews.Select(view => new SavedView { name = view.name, pivot = view.pivot, yaw = view.yaw, pitch = view.pitch, distance = view.distance, fieldOfView = view.fieldOfView, roll = view.roll }).ToList();
            return copy;
        }

        public void Validate()
        {
            Range(fieldOfView, 10, 120, "시야각(FOV)은 10~120도여야 합니다.");
            Colour(lightColor, "조명 색"); Range(lightIntensity, 0, 8, "조명 강도는 0~8이어야 합니다.");
            Range(lightYaw, -180, 180, "조명 방향(수평)은 -180~180도여야 합니다."); Range(lightPitch, 5, 90, "조명 높이는 5~90도여야 합니다.");
            if (!Enum.IsDefined(typeof(MmdShadowQuality), shadowQuality)) throw new ArgumentOutOfRangeException(nameof(shadowQuality), "그림자 품질 값이 올바르지 않습니다.");
            Range(bloomIntensity, 0, 10, "Bloom 강도는 0~10이어야 합니다.");
            Range(exposure, -5, 5, "노출은 -5~5 EV여야 합니다."); Range(contrast, -100, 100, "대비는 -100~100이어야 합니다."); Range(saturation, -100, 100, "채도는 -100~100이어야 합니다.");
            Colour(backgroundColor, "배경색");
            if (dofTarget == null) dofTarget = "";
            if (lookPreset == null) lookPreset = "";
            if (lookPreset.Length > 64) throw new ArgumentOutOfRangeException(nameof(lookPreset), "룩 프리셋 이름이 올바르지 않습니다.");
            if (dofTarget.Length > 64) throw new ArgumentOutOfRangeException(nameof(dofTarget), "초점 대상 ID가 올바르지 않습니다.");
            Range(dofDistance, .1f, 100, "초점 거리는 0.1~100 m여야 합니다."); Range(dofStrength, 0, 1, "흐림 강도는 0~1이어야 합니다.");
            Range(ssaoIntensity, 0, 4, "SSAO 강도는 0~4여야 합니다."); Range(ssaoRadius, .05f, 2, "SSAO 반경은 0.05~2 m여야 합니다.");
            Range(vignetteIntensity, 0, 1, "비네트 강도는 0~1이어야 합니다.");
            Range(ambientIntensity, 0, 2, "환경광 강도는 0~2여야 합니다."); Colour(ambientColor, "환경광 색");
            if (!Enum.IsDefined(typeof(MmdEffectQuality), effectQuality)) throw new ArgumentOutOfRangeException(nameof(effectQuality), "효과 품질 값이 올바르지 않습니다.");
            if (savedViews == null || savedViews.Count > MaxSavedViews) throw new ArgumentOutOfRangeException(nameof(savedViews), "저장된 시점은 최대 " + MaxSavedViews + "개입니다.");
            foreach (var view in savedViews)
            {
                if (view == null) throw new ArgumentNullException(nameof(savedViews));
                foreach (var value in new[] { view.pivot.x, view.pivot.y, view.pivot.z, view.yaw, view.pitch, view.distance, view.roll })
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(savedViews), "저장된 시점 값이 올바르지 않습니다.");
                Range(view.fieldOfView, 10, 120, "저장된 시점의 시야각이 올바르지 않습니다.");
            }
        }
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

    public sealed class MmdSceneGraphics : MonoBehaviour
    {
        private static readonly int[] ShadowResolutions = { 0, 1024, 2048, 4096 };
        private SceneGraphicsSettings current = new SceneGraphicsSettings();
        private Camera target;
        private VolumeProfile profile;
        internal Light Sun { get; private set; }
        internal Volume Volume { get; private set; }
        internal Bloom Bloom { get; private set; }
        internal ColorAdjustments Colour { get; private set; }
        internal Tonemapping Tonemap { get; private set; }
        internal DepthOfField Dof { get; private set; }
        internal Vignette Vignette { get; private set; }
        // The PC renderer's SSAO feature (null when the pipeline has none).
        internal ScreenSpaceAmbientOcclusion Ssao { get; private set; }
        // H2a-02: distance to the focus target, set every frame by the workspace (null: dofDistance).
        private float? focusOverride;
        internal float FocusDistance => Dof.focusDistance.value;
        public SceneGraphicsSettings Settings => current.Clone();
        internal SceneGraphicsSettings CurrentSettings => current; // read only, no copy

        public void Initialize(Camera camera)
        {
            target = camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            Sun = FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(light => light.type == LightType.Directional);
            if (Sun == null)
            {
                Sun = new GameObject("Directional Light").AddComponent<Light>();
                Sun.type = LightType.Directional;
            }
            // H2a-01: the project's volume defaults are neutral (StudioVolumeSetup; the build fails
            // otherwise), so this profile carries only the app's own settings.
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom = Get<Bloom>(); Colour = Get<ColorAdjustments>(); Tonemap = Get<Tonemapping>();
            Dof = Get<DepthOfField>(); Vignette = Get<Vignette>();
            Ssao = FindSsao();
            Volume = new GameObject("Scene Graphics Volume").AddComponent<Volume>();
            Volume.transform.SetParent(transform, false);
            Volume.isGlobal = true; Volume.priority = 1000; Volume.sharedProfile = profile;
            Apply(current);
        }
        private T Get<T>() where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component)) component = profile.Add<T>(true);
            component.SetAllOverridesTo(true);
            return component;
        }

        // Validates the whole state first, then applies it; invalid input changes nothing.
        public void Apply(SceneGraphicsSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var next = settings.Clone();
            next.Validate();
            target.fieldOfView = next.fieldOfView;
            target.backgroundColor = new Color(next.backgroundColor.r, next.backgroundColor.g, next.backgroundColor.b, 1);
            Sun.color = new Color(next.lightColor.r, next.lightColor.g, next.lightColor.b, 1);
            Sun.intensity = next.lightIntensity;
            Sun.transform.rotation = Quaternion.Euler(next.lightPitch, next.lightYaw, 0);
            Sun.shadows = next.shadowQuality == MmdShadowQuality.Off ? LightShadows.None : next.shadowQuality == MmdShadowQuality.Low ? LightShadows.Hard : LightShadows.Soft;
            Sun.shadowStrength = 1;
            if (next.shadowQuality != MmdShadowQuality.Off && (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) is UniversalRenderPipelineAsset pipeline)
                pipeline.mainLightShadowmapResolution = ShadowResolutions[(int)next.shadowQuality];
            // "Off" is intensity 0 (the default profile underneath is neutral either way).
            Bloom.active = true;
            Bloom.intensity.value = next.bloomEnabled ? next.bloomIntensity : 0;
            Bloom.threshold.value = .9f;
            Tonemap.mode.value = next.tonemapping ? TonemappingMode.Neutral : TonemappingMode.None;
            Colour.postExposure.value = next.exposure;
            Colour.contrast.value = next.contrast;
            Colour.saturation.value = next.saturation;
            Bloom.highQualityFiltering.value = next.effectQuality == MmdEffectQuality.High;
            // DOF: Gaussian (cheap, background only) at low quality, Bokeh otherwise.
            Dof.mode.value = !next.dofEnabled ? DepthOfFieldMode.Off : next.effectQuality == MmdEffectQuality.Low ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Bokeh;
            Dof.focalLength.value = 70; Dof.bladeCount.value = 6; Dof.bladeCurvature.value = 1;
            Dof.aperture.value = 16 * Mathf.Pow(1.4f / 16, next.dofStrength);
            Dof.gaussianMaxRadius.value = Mathf.Lerp(.5f, 1.5f, next.dofStrength);
            Dof.highQualitySampling.value = next.effectQuality != MmdEffectQuality.Low;
            Vignette.intensity.value = next.vignetteEnabled ? next.vignetteIntensity : 0;
            Vignette.smoothness.value = .4f; Vignette.color.value = Color.black; Vignette.rounded.value = false;
            ApplyAmbient(next);
            ApplySsao(next);
            current = next;
            ApplyFocus();
        }

        // Environment light: a flat ambient (colour x intensity) written straight into the ambient
        // probe that shaders sample; intensity 0 puts back what the scene had.
        private bool sceneAmbientKept;
        private AmbientMode sceneAmbientMode;
        private Color sceneAmbientLight;
        private SphericalHarmonicsL2 sceneAmbientProbe;
        internal bool AmbientOverridden { get; private set; }
        private void ApplyAmbient(SceneGraphicsSettings next)
        {
            if (!sceneAmbientKept)
            {
                sceneAmbientKept = true;
                sceneAmbientMode = RenderSettings.ambientMode; sceneAmbientLight = RenderSettings.ambientLight; sceneAmbientProbe = RenderSettings.ambientProbe;
            }
            if (next.ambientIntensity <= 0)
            {
                if (!AmbientOverridden) return;
                RenderSettings.ambientMode = sceneAmbientMode; RenderSettings.ambientLight = sceneAmbientLight; RenderSettings.ambientProbe = sceneAmbientProbe;
                AmbientOverridden = false;
                return;
            }
            var colour = new Color(next.ambientColor.r, next.ambientColor.g, next.ambientColor.b, 1) * next.ambientIntensity;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(colour.linear);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = colour; RenderSettings.ambientProbe = probe;
            AmbientOverridden = true;
        }

        // Focus target distance (camera-space depth); null returns to the fixed distance.
        internal void SetFocusOverride(float? distance)
        {
            if (distance.HasValue && (float.IsNaN(distance.Value) || float.IsInfinity(distance.Value))) distance = null;
            focusOverride = distance.HasValue ? Mathf.Clamp(distance.Value, .1f, 100) : (float?)null;
            ApplyFocus();
        }
        private void ApplyFocus()
        {
            var focus = focusOverride ?? current.dofDistance;
            Dof.focusDistance.value = focus;
            // Gaussian: sharp to just behind the focus, fully blurred a few metres further
            // (closer as the strength rises).
            Dof.gaussianStart.value = focus + .5f;
            Dof.gaussianEnd.value = focus + .5f + Mathf.Lerp(12, 2, current.dofStrength);
        }

        // SSAO is a renderer feature, not a volume component. Its settings class is internal to
        // URP, so the fields the app owns are set by reflection (serialized fields, so they survive
        // IL2CPP stripping); the probe checks the rendered result. The feature stays enabled in the
        // asset (its shaders are kept in the build) and is switched at runtime.
        private static ScreenSpaceAmbientOcclusion FindSsao()
        {
            if (!((QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) is UniversalRenderPipelineAsset pipeline)) return null;
            foreach (var data in pipeline.rendererDataList)
                if (data != null && data.TryGetRendererFeature<ScreenSpaceAmbientOcclusion>(out var feature)) return feature;
            return null;
        }
        private const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        internal object SsaoSettings => Ssao == null ? null : typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Hidden)?.GetValue(Ssao);
        internal static object SsaoField(object settings, string name) => settings?.GetType().GetField(name, Hidden)?.GetValue(settings);
        private void ApplySsao(SceneGraphicsSettings next)
        {
            if (Ssao == null) return;
            var settings = SsaoSettings ?? throw new InvalidOperationException("SSAO 설정에 접근할 수 없습니다(URP 버전 확인 필요).");
            void Set(string name, object value)
            {
                var field = settings.GetType().GetField(name, Hidden) ?? throw new InvalidOperationException("SSAO 설정 항목이 없습니다: " + name);
                field.SetValue(settings, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
            }
            // AOSampleOption and BlurQualityOptions are ordered High 0, Medium 1, Low 2.
            var level = 2 - (int)next.effectQuality;
            Set("Intensity", next.ssaoIntensity); Set("Radius", next.ssaoRadius); Set("DirectLightingStrength", .25f); Set("Falloff", 100f);
            Set("Samples", level); Set("BlurQuality", level); Set("Downsample", next.effectQuality == MmdEffectQuality.Low);
            // The models use lilToon or URP Unlit, which do not sample the SSAO texture: "after
            // opaque" composites the occlusion onto the colour buffer for any shader.
            Set("AfterOpaque", true);
            // Normals reconstructed from depth: works for shaders without a DepthNormals pass.
            Set("Source", 0); Set("NormalSamples", 1);
            Ssao.SetActive(next.ssaoEnabled && next.ssaoIntensity > 0);
        }
        internal static int ShadowResolution(MmdShadowQuality quality) => ShadowResolutions[(int)quality];
        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
            if (AmbientOverridden) { RenderSettings.ambientMode = sceneAmbientMode; RenderSettings.ambientLight = sceneAmbientLight; RenderSettings.ambientProbe = sceneAmbientProbe; }
#if UNITY_EDITOR
            // Play mode in the editor edits the renderer asset itself: put the template values back.
            if (Ssao != null && ssaoOriginal != null) UnityEditor.EditorJsonUtility.FromJsonOverwrite(ssaoOriginal, Ssao);
#endif
        }
#if UNITY_EDITOR
        private string ssaoOriginal;
        private void Awake() { var feature = FindSsao(); if (feature != null) ssaoOriginal = UnityEditor.EditorJsonUtility.ToJson(feature); }
#endif
    }
}
