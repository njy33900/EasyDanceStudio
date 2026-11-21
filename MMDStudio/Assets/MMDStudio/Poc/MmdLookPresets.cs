using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H2a-05: a look preset is the look part of the graphics settings (light, shadows, post
    // processing, background); camera, saved views, the DOF focus target/distance and the effect
    // quality stay with the scene. Material styles join when H2b lands.
    [Serializable]
    public sealed class LookPreset
    {
        public string name;
        public SceneGraphicsSettings values = new SceneGraphicsSettings();
        [NonSerialized] public bool builtIn;
    }

    public static class MmdLookPresets
    {
        public const int MaxUserPresets = 32, MaxNameLength = 32;

        // Proposed defaults; the reference images are to be confirmed with the user (decision pending).
        public static readonly LookPreset[] BuiltIn =
        {
            new LookPreset { name = "밝은 무대", builtIn = true, values = new SceneGraphicsSettings
            {
                lightColor = new Color(1, .98f, .95f), lightIntensity = 2.4f, lightYaw = -30, lightPitch = 45, shadowQuality = MmdShadowQuality.Medium,
                bloomEnabled = true, bloomIntensity = .6f, tonemapping = true, exposure = .2f, contrast = 10, saturation = 5,
                backgroundColor = new Color(.93f, .94f, .96f), ssaoEnabled = true, ssaoIntensity = .8f, ssaoRadius = .3f,
                vignetteEnabled = false, vignetteIntensity = .2f, dofEnabled = false, dofStrength = .3f,
            } },
            new LookPreset { name = "저녁 실외", builtIn = true, values = new SceneGraphicsSettings
            {
                lightColor = new Color(1, .72f, .5f), lightIntensity = 1.8f, lightYaw = 60, lightPitch = 12, shadowQuality = MmdShadowQuality.High,
                bloomEnabled = true, bloomIntensity = 1.2f, tonemapping = true, exposure = 0, contrast = 15, saturation = 10,
                backgroundColor = new Color(.55f, .42f, .5f), ssaoEnabled = true, ssaoIntensity = .7f, ssaoRadius = .4f,
                vignetteEnabled = true, vignetteIntensity = .25f, dofEnabled = true, dofStrength = .35f,
            } },
            new LookPreset { name = "어두운 클럽", builtIn = true, values = new SceneGraphicsSettings
            {
                lightColor = new Color(.75f, .55f, 1), lightIntensity = 2.2f, lightYaw = 150, lightPitch = 60, shadowQuality = MmdShadowQuality.Medium,
                bloomEnabled = true, bloomIntensity = 2.5f, tonemapping = true, exposure = -.3f, contrast = 25, saturation = 20,
                backgroundColor = new Color(.04f, .03f, .08f), ssaoEnabled = true, ssaoIntensity = 1.2f, ssaoRadius = .3f,
                vignetteEnabled = true, vignetteIntensity = .45f, dofEnabled = true, dofStrength = .5f,
            } },
        };

        // Copies the look fields of `preset` onto `target` (everything else is kept).
        public static void CopyLook(SceneGraphicsSettings preset, SceneGraphicsSettings target)
        {
            target.lightColor = preset.lightColor; target.lightIntensity = preset.lightIntensity;
            target.lightYaw = preset.lightYaw; target.lightPitch = preset.lightPitch; target.shadowQuality = preset.shadowQuality;
            target.bloomEnabled = preset.bloomEnabled; target.bloomIntensity = preset.bloomIntensity; target.tonemapping = preset.tonemapping;
            target.exposure = preset.exposure; target.contrast = preset.contrast; target.saturation = preset.saturation;
            target.backgroundColor = preset.backgroundColor;
            target.dofEnabled = preset.dofEnabled; target.dofStrength = preset.dofStrength;
            target.ssaoEnabled = preset.ssaoEnabled; target.ssaoIntensity = preset.ssaoIntensity; target.ssaoRadius = preset.ssaoRadius;
            target.vignetteEnabled = preset.vignetteEnabled; target.vignetteIntensity = preset.vignetteIntensity;
        }

        // True when the look fields of `current` equal the preset's (slider rounding tolerated).
        public static bool SameLook(SceneGraphicsSettings preset, SceneGraphicsSettings current)
        {
            bool F(float a, float b) => Math.Abs(a - b) < 1e-4f;
            bool C(Color a, Color b) => F(a.r, b.r) && F(a.g, b.g) && F(a.b, b.b);
            return C(preset.lightColor, current.lightColor) && F(preset.lightIntensity, current.lightIntensity) &&
                F(preset.lightYaw, current.lightYaw) && F(preset.lightPitch, current.lightPitch) && preset.shadowQuality == current.shadowQuality &&
                preset.bloomEnabled == current.bloomEnabled && F(preset.bloomIntensity, current.bloomIntensity) && preset.tonemapping == current.tonemapping &&
                F(preset.exposure, current.exposure) && F(preset.contrast, current.contrast) && F(preset.saturation, current.saturation) &&
                C(preset.backgroundColor, current.backgroundColor) &&
                preset.dofEnabled == current.dofEnabled && F(preset.dofStrength, current.dofStrength) &&
                preset.ssaoEnabled == current.ssaoEnabled && F(preset.ssaoIntensity, current.ssaoIntensity) && F(preset.ssaoRadius, current.ssaoRadius) &&
                preset.vignetteEnabled == current.vignetteEnabled && F(preset.vignetteIntensity, current.vignetteIntensity);
        }

        public static string CheckName(string name, IEnumerable<LookPreset> existingUser)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) throw new ArgumentException("프리셋 이름을 입력하세요.");
            if (trimmed.Length > MaxNameLength) throw new ArgumentException("프리셋 이름은 " + MaxNameLength + "자 이하여야 합니다.");
            if (BuiltIn.Any(p => p.name == trimmed)) throw new ArgumentException("기본 프리셋과 같은 이름은 쓸 수 없습니다: " + trimmed);
            return trimmed;
        }
    }
}
