# MVP-10 — Runtime transform gizmo (W / E / R)

2026-11-19. Covers spec 4.2 (W/E/R) and 4.8 #4–5 (a self-made gizmo that works in the built Player; dragging it updates the transform and stays in sync with the inspector's numeric fields), completing MMD-107 (select, move/rotate/scale, show/hide, delete). Chosen by the user for the first phase.

## Use

Select a character or stage. The gizmo appears at the object's placement origin (feet for MMD models), in world axes: X red, Y green, Z blue; the hovered or dragged handle turns amber.

| Mode | Key | Handles | Drag |
|---|---|---|---|
| 이동 (Move) | W | three arrows | moves along the axis; the object follows the pointer's projection on that axis |
| 회전 (Rotate) | E | three rings | rotates about the axis by the angle swept around the gizmo centre (several turns accumulate) |
| 크기 (Scale) | R | three square-ended axes + centre square | axis: scales one component; centre: uniform scale (right/down grows) |

The mode bar at the viewport's top-left (이동 W / 회전 E / 크기 R) shows and switches the mode; hovering a mode shows its key in the status bar. W/E/R are ignored while a text/numeric field has focus and when Ctrl is held. The gizmo is hidden for hidden objects and while Light or Camera is selected in the explorer.

A left press on a handle starts a gizmo drag instead of picking; elsewhere left click still selects. Once started, the drag continues while the pointer crosses panels until release. Right/middle-button camera controls are unaffected.

## Behaviour and rules

- Every drag step goes through `MmdSceneService.SetPlacement`, so the usual validation applies (finite values, scale 0.001–1000; scale is also clamped while dragging). Values are computed from the drag start, so there is no drift.
- The inspector's position/rotation/scale fields update during the drag, and numeric edits move the gizmo, so both paths edit the same transform (spec 4.8 #5). Gizmo edits mark the scene unsaved.
- The gizmo keeps a constant on-screen size (about 90 px axes) at any zoom.
- No undo yet (third phase); an unwanted change can be typed back in the inspector.

## Implementation

`MmdSceneWorkspace.Gizmo.cs`. The gizmo is drawn with UI Toolkit `Painter2D` on a non-picking overlay inside the viewport card, so it is always on top of the 3D scene, antialiased, and needs no extra shader in the Player. Hit-testing uses the projected arrow segments, ring polylines (64 segments) and centre square in screen space (9 px tolerance scaled with resolution). Move maps pointer travel onto the axis' screen projection; rotate accumulates the signed screen angle around the projected centre and flips the sign when the axis faces away from the camera.

## Actual Player verification

UI builds now go to `out/<configuration>/MVP-10-<backend>/` and require 95 UI assertions (86 + 9) plus the 9 restart-restore assertions. New checks, driven through the same per-frame viewport handler as the other probe input:

- the gizmo appears at the selected object's origin (compared with Unity's own projection) in move mode with the mode button active
- dragging the X arrow 40 px moves only X, the object's projected origin lands within 3 px of the pointer path, selection is unchanged, the scene is marked unsaved and the Position field matches
- dragging the Y arrow changes only the height
- E switches to rotate; dragging along the Y ring rotates about the world up axis by more than 5° and the Rotation field matches
- R switches to scale; the centre handle scales all three components by the same ratio; the Y axis handle scales only Y
- typing a position in the inspector moves the gizmo origin to the new projected position
- W/E/R are ignored while a numeric field has focus and work again after it loses focus
- no gizmo for a hidden object or while the light is selected

Earlier floor/empty-space click checks now pick points away from gizmo handles. The probe also saves `scene-ui-gizmo.png` (rotate mode). Results (2026-11-19): Debug Mono, Debug IL2CPP, Release Mono and Release IL2CPP each pass 95 UI and 9 restart-restore assertions and the native inventory; move and rotate gizmo screenshots were inspected. The first run failed one later persistence check because the gizmo checks left a different object selected than the one saved; the probe now restores the selection (no product change). Also recorded in TASKS.md (MVP-10).

## Limits

Physical mouse dragging has not been checked by a person (same as other viewport input). No local-space mode, plane handles, snapping or undo.
