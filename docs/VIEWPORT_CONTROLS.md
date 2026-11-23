# MVP-03 — Viewport camera controls and selection highlight

2026-11-19. Extends the MVP-02 scene workspace (SCENE_WORKSPACE_UI.md). Physics stays OFF; the single-model legacy preview is unchanged.

## Controls (v0.61, 2026-11-23)

| Input | Effect |
|---|---|
| Left click (press and release within 5 px) | Pick the object under the cursor (empty space clears the selection); also chooses the PMX material under the cursor for 화면 보정 |
| Left drag (beyond 5 px) | Orbit around the pivot (0.3°/px, pitch clamped to ±89°). The movement held back while it could still be a click is applied on the first orbit frame; the release does not pick |
| Right drag | Dolly: pointer up comes closer, down goes back (distance × e^(−4·dy/viewport height), 0.05–500). Horizontal movement is ignored; the field of view is not touched |
| Middle drag, or Shift + right drag | Pan pivot and camera together; the point under the cursor follows it |
| Mouse wheel | Zoom toward the model surface under the cursor (the camera moves along the line to that point, so it stays at the same screen position; the pivot then sits on the view axis at the point's depth). Without a surface: toward the pivot (×0.88 per notch) |
| F | Frame the selected character, or all visible characters when nothing is selected (a new pivot) |
| F11 / top bar 전체 화면 | Full screen (borderless, at the resolution of the window's monitor) and back to the window size it had; works with dialogs open and while loading. Esc also leaves full screen once no dialog or focus view takes it. Unity's Alt+Enter works too |
| 전체 보기 / 선택 보기 buttons | Same framing as before, now through the orbit camera |

Until v0.6 the right drag orbited and the left drag did nothing. Framing keeps the current viewing angle and fits the bounding sphere into the smaller of the vertical and horizontal fields of view. A left press on a gizmo handle drags the gizmo instead. Only one button drives at a time: presses while another drag or click is in progress are ignored.

The view also has a roll (screen tilt). It is only set when a camera VMD hands over to the manual view; orbiting keeps it, 카메라 → "수평 맞추기" and 뷰 초기화 clear it, and it is saved with the view and saved views (`view.roll`, `savedViews[].roll`, additive in schema 2).

A short hint is shown at the bottom of the viewport.

## Input gating

- A drag or wheel zoom starts only when the pointer is inside the camera's viewport rectangle and UI Toolkit picking at that point returns nothing. The toolbar, list, inspector and playback/status panels are opaque, pickable panels, so input over them never reaches the 3D camera. The viewport element and its hint ignore picking.
- Once a drag started in the viewport, it continues while the pointer crosses a panel, until the button is released.
- While a text or composite numeric field (Vector3Field and its FloatFields) has keyboard focus, wheel zoom and the F shortcut are ignored. A press in the viewport only blurs that field — which commits its delayed value — and does not rotate the camera in that press.
- During a synchronous import or output (Busy), while a dialog or the delete confirmation is shown, and when the window loses focus, viewport input is ignored and any drag ends. A button still held when focus returns does not resume the drag.
- Camera moves are sent only with a non-zero amount, so a press and release without movement never moves the camera. With a camera VMD followed, the motion is released on the first real orbit/pan/dolly/zoom (or F, 전체 보기, 뷰 초기화, a saved view), not on a press, a click that selects, or a sideways right drag. The hand-over keeps position, direction, roll and the lens: the VMD's view angle becomes the camera FOV setting (clamped to 10–120°), a view state that undo of other graphics edits does not revert.
- Nonfinite deltas or wheel values are ignored; wheel notches are clamped.
- The wheel's surface query is the same baked-mesh ray test as picking; the UI probe reports its cost (`wheel_over_surface_zooms_toward_point_under_cursor`).

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

This checks the gating and camera logic in the actual Player. Since 2026-11-23 (v0.61) the UI probe also feeds a virtual Input System mouse and keyboard with state events and lets the workspace read them in its own Update (`device_level_mouse_and_keys_follow_v061_rules`: click, left-drag orbit, right-drag dolly, Shift+right pan, one 120-unit wheel notch, F). OS-level mouse events (Windows → Input System backend) are still not injected; physical mouse feel needs a manual check.

Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass all 40 assertions; build-info.json reports Development true/false as expected. The Mono application screenshot shows the highlight box and ground ring on the selected character and the controls hint. Native inventory and the legacy regression suites were not rerun for this ticket. PowerShell 7 was not installed in this session, so the script's build/run steps were executed with an equivalent bash runner. Also recorded in TASKS.md (MVP-03).

## Remaining

Viewport picking (click a character to select it), transform gizmos, undo, camera VMD on the shared clock, and saving the view with the scene. Background workflow, lighting/graphics and JSON persistence follow.

MVP-04 note: the UI Player now builds to MVP-04-<backend> and the probe requires 50 assertions; all viewport checks above remain part of it (STAGE_WORKFLOW.md).
