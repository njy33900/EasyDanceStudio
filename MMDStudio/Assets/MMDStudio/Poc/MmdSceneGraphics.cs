using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MmdStudio.Poc
{
    public enum MmdShadowQuality { Off, Low, Medium, High }

    [Serializable]
    public sealed class SavedView
    {
        public string name;
        public Vector3 pivot;
        public float yaw, pitch, distance, fieldOfView;
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
        public List<SavedView> savedViews = new List<SavedView>();

        public SceneGraphicsSettings Clone()
        {
            var copy = (SceneGraphicsSettings)MemberwiseClone();
            copy.savedViews = savedViews.Select(view => new SavedView { name = view.name, pivot = view.pivot, yaw = view.yaw, pitch = view.pitch, distance = view.distance, fieldOfView = view.fieldOfView }).ToList();
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
            if (savedViews == null || savedViews.Count > MaxSavedViews) throw new ArgumentOutOfRangeException(nameof(savedViews), "저장된 시점은 최대 " + MaxSavedViews + "개입니다.");
            foreach (var view in savedViews)
            {
                if (view == null) throw new ArgumentNullException(nameof(savedViews));
                foreach (var value in new[] { view.pivot.x, view.pivot.y, view.pivot.z, view.yaw, view.pitch, view.distance })
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
        public SceneGraphicsSettings Settings => current.Clone();

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
            // Post-processing also applies the pipeline/global default profiles (template
            // samples include vignette, film grain, split toning...). Override every known
            // volume component with its neutral constructor defaults so the app owns the look.
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom = Get<Bloom>(); Colour = Get<ColorAdjustments>(); Tonemap = Get<Tonemapping>();
            EnsureNeutralOverrides();
            Volume = new GameObject("Scene Graphics Volume").AddComponent<Volume>();
            Volume.transform.SetParent(transform, false);
            Volume.isGlobal = true; Volume.priority = 1000; Volume.sharedProfile = profile;
            Apply(current);
        }
        internal bool NeutralOverridesReady { get; private set; }
        // VolumeManager is initialized by the pipeline on its first render, which can be
        // after Initialize; until then only Bloom/ColorAdjustments/Tonemapping exist.
        private void EnsureNeutralOverrides()
        {
            if (NeutralOverridesReady || profile == null || !VolumeManager.instance.isInitialized) return;
            foreach (var type in VolumeManager.instance.baseComponentTypeArray)
                if (!type.IsAbstract && !profile.Has(type)) profile.Add(type, true);
            NeutralOverridesReady = true;
        }
        private void LateUpdate() { EnsureNeutralOverrides(); }
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
            // Keep the component active: an inactive override would let the template's
            // default-profile Bloom underneath show through. "Off" is intensity 0.
            Bloom.active = true;
            Bloom.intensity.value = next.bloomEnabled ? next.bloomIntensity : 0;
            Bloom.threshold.value = .9f;
            Tonemap.mode.value = next.tonemapping ? TonemappingMode.Neutral : TonemappingMode.None;
            Colour.postExposure.value = next.exposure;
            Colour.contrast.value = next.contrast;
            Colour.saturation.value = next.saturation;
            current = next;
        }
        internal static int ShadowResolution(MmdShadowQuality quality) => ShadowResolutions[(int)quality];
        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
        }
    }
}
