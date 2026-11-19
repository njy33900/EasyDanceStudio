# MVP-05 — Viewport picking, shortcuts and delete confirmation

2026-11-19. Covers the selection and shortcut parts of specification 4.2/4.8 and the destructive-delete confirmation of 4.12 on top of MVP-03/04. Transform gizmos (W/E/R), Ctrl+S and undo are not implemented.

## Behaviour

| Input | Effect |
|---|---|
| Left click in the viewport | Select the nearest visible character or stage under the cursor; empty space clears the selection |
| Left drag | Nothing (no pick, no camera move); reserved for future gizmos |
| Space | Play/pause the shared clock (only when a motion exists) |
| Delete | Open the delete confirmation for the selection |
| Esc | Cancel the delete confirmation |

- A click is a left press and release inside the viewport within 5 px (`ClickSlopPixels`). Presses over panels never pick.
- Viewport and list selection share one state (`SelectedId`): inspector, list highlight, 3D highlight and the status line ("캐릭터 선택: …" / "배경 선택: …" / "선택을 해제했습니다.") all update.
- A viewport press releases focus from any panel control, so keys typed afterwards are not routed to a previously clicked button. A press while a text/numeric field has focus only commits that field.
- Space, Delete, F and wheel are ignored while a text/numeric field has focus (spec 4.8 #7).
- **선택 항목 삭제** and Delete both open the same modal confirmation naming the item, stating that the source file is not deleted and that undo is not yet available. Focus starts on **취소 (Esc)**. While it is open, its full-screen overlay blocks panels and the viewport, and only Esc (or the dialog buttons) is handled.

## Picking implementation

`MmdSceneWorkspace.PickAt` tests each visible object's skinned renderers: first the renderer's world bounds against the camera ray, then the triangles of the mesh baked at its current pose (`BakeMesh(useScale: false)` with the renderer's `localToWorldMatrix`), using double-sided Möller–Trumbore. The nearest hit wins, so a character in front of the stage is chosen over the floor behind it. Picking therefore follows animation and placement/scale. Hidden objects are skipped.

Each click bakes only the renderers whose bounds the ray crosses. This is per click, not per frame; very dense stages may make a click take noticeably longer, which has not been measured.

## Actual Player verification

UI builds now go to `out/<configuration>/MVP-05-<backend>/` and require 60 assertions (50 + 10):

- clicking a point projected from character B's upper-body bone (Unity's transform, independent of the picking code) selects B; title, highlight and inspector follow
- a left drag across character A neither picks nor moves the camera
- clicking an unobstructed floor point (rays checked against character bounds) selects the stage
- a left click over a side panel does not pick
- clicking an empty viewport corner clears the selection and disables selection controls
- Space toggles shared playback even with a list button focused
- Space and Delete are ignored while a numeric field is focused
- Delete opens the confirmation naming the stage without removing anything
- while it is open, orbit, wheel, click and Space do nothing and the overlay reports the viewport as UI
- Esc closes it and keeps the object selected
- later deletions (Delete key for the stage, the button for characters) go through **삭제** in the dialog

The probe also saves `scene-ui-confirm.png` showing the open dialog. Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass all 60 assertions; build-info.json reports Development true/false as expected. Both Mono screenshots were inspected (selection status line, longer controls hint fits; dimmed dialog with name, notice and focused 취소). Native inventory and legacy regressions were not rerun; the bash runner was used (no PowerShell 7 here). Also recorded in TASKS.md (MVP-05).

As with MVP-03, the probe drives the per-frame input handler with synthetic pointer/key states; physical mouse and keyboard use still needs a manual check.
