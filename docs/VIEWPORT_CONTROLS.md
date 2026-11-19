# MVP-03 — Viewport camera controls and selection highlight

2026-11-19. Extends the MVP-02 scene workspace (SCENE_WORKSPACE_UI.md). Physics stays OFF; the single-model legacy preview is unchanged.

## Controls

| Input | Effect |
|---|---|
| Right drag | Orbit around the pivot (0.3°/px, pitch clamped to ±89°) |
| Middle drag, or Shift + right drag | Pan pivot and camera together; the point under the cursor follows it |
| Mouse wheel | Zoom toward the pivot (×0.88 per notch, distance 0.05–500) |
| F | Frame the selected character, or all visible characters when nothing is selected |
| 전체 보기 / 선택 보기 buttons | Same framing as before, now through the orbit camera |

Framing keeps the current viewing angle and fits the bounding sphere into the smaller of the vertical and horizontal fields of view. Left click is reserved for future viewport picking/gizmos.

A short hint is shown at the bottom of the viewport.

## Input gating

- A drag or wheel zoom starts only when the pointer is inside the camera's viewport rectangle and UI Toolkit picking at that point returns nothing. The toolbar, list, inspector and playback/status panels are opaque, pickable panels, so input over them never reaches the 3D camera. The viewport element and its hint ignore picking.
- Once a drag started in the viewport, it continues while the pointer crosses a panel, until the button is released.
- While a text or composite numeric field (Vector3Field and its FloatFields) has keyboard focus, wheel zoom and the F shortcut are ignored. A press in the viewport only blurs that field — which commits its delayed value — and does not rotate the camera in that press.
- During a synchronous import (Busy), viewport input is ignored and any drag ends.
- Nonfinite deltas or wheel values are ignored; wheel notches are clamped.

Windows reports 120 wheel units per notch. Values of 10 or more are divided by 120; already normalized values are used as is.

## Selection highlight

The selected visible character gets an orange line box around its combined SkinnedMeshRenderer bounds and a ground ring at its placement under the bounds. It follows list selection, reselection, transforms and playback (updated on each Refresh and in LateUpdate). It is hidden when nothing is selected, when the selected character is hidden, and after the last character is removed. Lines use the built-in Sprites/Default shader, which is always included in the Player; a URP Unlit fallback is used only when that shader is missing. Highlight objects are not part of model roots, so they do not affect framing or visibility checks.

Renderer bounds are the skinned renderer's bounds, not a per-vertex tight fit; the box can be looser than the visible pose.

## Implementation

- `MmdViewportCamera.cs`: pure pivot/yaw/pitch/distance state, orbit/pan/zoom/frame and apply to Camera.main.
- `MmdSceneWorkspace.cs`: `ProcessViewportInput` (gating and drag state), `ReadDevices` (Input System Mouse/Keyboard, active input handler is Input System only), `PointerOverUi`/`PointerInViewport`/`TextInputFocused`, highlight line renderers.

## Actual Player verification

UI verification now writes the Player to `out/<configuration>/MVP-03-<backend>/` so MVP-02 outputs are preserved, and requires 40 assertions. `Start-SceneStudio.ps1` launches MVP-03.

The probe disables device reads so a real mouse over the test window cannot move the camera. It drives the same `ProcessViewportInput` path with synthetic per-frame pointer states and checks: panel/viewport picking (including a point just below the toolbar that would fail if the panel Y conversion were inverted), framing all visible bounds, orbit about a fixed pivot, drag continuation over a panel, pan moving pivot and camera together in the expected directions, wheel zoom, panel blocking of orbit/pan/zoom, focus detection, blocked wheel/F during field focus, viewport press committing focus without rotation, list selection moving the highlight, F framing the selected character, hidden-selection highlight removal, reselection, nonfinite/extreme input, and highlight absence on empty and fully cleared scenes.

This checks the gating and camera logic in the actual Player. It does not inject OS mouse events through the Input System device layer; physical mouse/keyboard behavior in the running app still needs a manual check.

Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass all 40 assertions; build-info.json reports Development true/false as expected. The Mono application screenshot shows the highlight box and ground ring on the selected character and the controls hint. Native inventory and the legacy regression suites were not rerun for this ticket. PowerShell 7 was not installed in this session, so the script's build/run steps were executed with an equivalent bash runner. Also recorded in TASKS.md (MVP-03).

## Remaining

Viewport picking (click a character to select it), transform gizmos, undo, camera VMD on the shared clock, and saving the view with the scene. Background workflow, lighting/graphics and JSON persistence follow.

MVP-04 note: the UI Player now builds to MVP-04-<backend> and the probe requires 50 assertions; all viewport checks above remain part of it (STAGE_WORKFLOW.md).
