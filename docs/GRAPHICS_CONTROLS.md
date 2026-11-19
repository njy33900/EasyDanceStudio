# MVP-06 — Camera, light and basic graphics (Graphics tab)

2026-11-19. Covers specification MMD-108 (free camera, saved views, FOV), MMD-109 (directional light colour, intensity, shadow quality), MMD-110 (Bloom on/intensity, exposure/basic colour adjustment, background colour) and the Inspector Graphics tab of 4.4/4.7. Camera VMD (MMD-116), DOF/SSAO/toon/PBR (second phase) are not included; the tab says so.

## Use

The inspector now has **속성** and **그래픽** tabs. The Graphics tab has:

| Section | Controls |
|---|---|
| 카메라 | 시야각 10–120°; **현재 시점 저장** (up to 16), per-view restore and **삭제**; **뷰 초기화** (default angle, frame all) |
| 조명 (방향광) | colour R/G/B, intensity 0–8, horizontal direction −180–180°, height 5–90°, shadow quality 끔 / 낮음 (1024, hard) / 보통 (2048, soft) / 높음 (4096, soft) |
| 효과 | Bloom on/off and intensity 0–10 (threshold 0.9), Neutral tonemapping on/off, exposure −5–5 EV, contrast and saturation −100–100 |
| 배경색 | R/G/B |

Sliders have delayed numeric fields. A restored view sets the orbit pivot/angle/distance and its FOV.

## Implementation

- `MmdSceneGraphics` owns the state as `SceneGraphicsSettings`, a plain `[Serializable]` class (no Unity object references) including saved views, for the later `.mmdscene` ticket. `Apply` validates the complete state first and changes nothing when any value is invalid, then sets the camera, light, shadow map resolution and volume overrides. The UI uses copy-modify-apply and refreshes controls from the retained state, so rejected values revert.
- The camera's post-processing is enabled. Doing so also applies the URP template's default volume profiles (they contain Vignette, Film Grain, Split Toning, Lift Gamma Gain, Motion Blur and others), so a priority-1000 global runtime volume overrides every volume component with its neutral constructor defaults. Because URP's `VolumeManager` is initialized on the pipeline's first render, Bloom/ColorAdjustments/Tonemapping are created immediately and the remaining neutral overrides are added once the manager is ready.
- Bloom stays active as an override; "off" is intensity 0, so the template's Bloom cannot show through.
- Shadow quality sets the light's shadow type and the active URP asset's main-light shadow map resolution for the session (the asset file is not modified in the Player).
- The UI lives in a partial file, `MmdSceneWorkspace.Graphics.cs`, as a first step toward splitting the workspace (spec 4.11).

Defaults keep the previous look: FOV 45°, light white ×2 at (35°, −30°), background (0.08, 0.10, 0.14), effects neutral, plus medium soft shadows.

## Actual Player verification

UI builds now go to `out/<configuration>/MVP-06-<backend>/` and require 73 assertions (60 + 13):

- defaults applied, camera post-processing on, global priority volume, neutral Vignette/Film Grain overrides present
- the Graphics tab replaces the properties panel
- the FOV slider sets the camera; an invalid whole-state apply (FOV 500 with exposure 1) is rejected without applying the valid part
- light colour/intensity/direction sliders drive the light
- shadow quality maps to light shadows and 4096/off/2048 shadow map resolution
- Bloom/tonemapping/contrast/saturation drive the volume; Bloom off keeps the override active at intensity 0
- **exposure +1.5 EV brightens an actual rendered frame** (camera rendered to a texture and read back): mean luminance about 30 → 53 on Debug Mono, Debug IL2CPP and Release Mono alike, proving the post-processing variants survive Player shader stripping
- background colour sliders set the camera clear colour
- a saved view restores angle, distance and FOV; deleting removes it
- view reset restores the default angle and frames all visible objects
- a whole-state apply is reflected by every control

The probe also saves `scene-ui-graphics.png`. Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass all 73 assertions; build-info.json reports Development true/false as expected. Both Mono screenshots were inspected; the first Graphics-tab image showed light text on light numeric boxes, which was fixed with the dark input style and re-inspected. A first run hung because VolumeManager was not yet initialized at startup; initialization is now deferred as described above. Native inventory and legacy regressions were not rerun; the bash runner was used (no PowerShell 7 here). Also recorded in TASKS.md (MVP-06).

## Limits

The MMD material shader's response to light colour and shadows was only seen in screenshots, not measured. Shadow cost on large stages has not been measured. Bloom visibility depends on scene brightness above the 0.9 threshold. Ambient/environment lighting, more lights, and camera VMD on the shared clock remain.
