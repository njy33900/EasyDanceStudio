# MVP-07 — `.mmdscene` save, open and new

2026-11-19. Covers MMD-112 (JSON save/load of model/motion paths, placement, camera, light, effects), spec 4.10 flows (save/reopen, missing assets, invalid file, unsaved changes on new/open/close) and 5.2 data handling. This completes the "save, relaunch, reload restores" part of the first-phase done criteria (8.2) for the current feature set.

## Use

The toolbar now starts with the project name (with `*` when there are unsaved changes) and **새로 만들기**, **열기**, **저장**, **다른 이름으로**. Ctrl+S saves (ignored while a text/numeric field has focus). Saving a new scene opens a native Windows save dialog with overwrite confirmation; opening uses a `.mmdscene` filter.

`scripts/Start-SceneStudio.ps1 -Scene <file.mmdscene>` opens a scene at launch (`-sceneFile`).

## File contents (`schemaVersion` 1)

`application` ("EasyDanceStudio"), `projectId`, `sceneObjects` (id, `MmdCharacter`/`Stage`, `assetPath`, `motionPath`, position, rotationEuler, scale, visible), `selectedId`, `view` (pivot, yaw, pitch, distance), `graphics` (the MVP-06 `SceneGraphicsSettings`, including saved views) and `timeline` (time, loop, speed). It is a reference project: PMX/VMD/textures are never copied. Paths are relative to the scene file (forward slashes) when on the same volume, absolute otherwise. No Unity object references are serialized.

## Rules

- **Save** writes to a hidden temporary file in the same folder and replaces the target atomically; the temporary file is removed on failure.
- **Open** reads and fully validates first (extension, 4 MiB limit, JSON, application id, schema version, unique ids, types, `.pmx`/`.vmd` paths, finite transforms, scale 0.001–1000, view, timeline speed 0.5–2, graphics ranges). An invalid file or unsupported version shows an error and **keeps the current scene**.
- Missing or failing PMX/VMD files are **skipped and listed** in a dialog; the rest is restored (a character whose motion is missing is restored without motion). Reconnecting a moved file is a later ticket; the dialog says to re-add and save.
- Restored scenes are paused at the saved time (clamped to the duration), with the saved selection, view, loop, speed and graphics.
- **Unsaved changes**: adding/removing objects, connecting motion, placement, visibility, loop, speed and graphics edits (including saved views) mark the scene dirty; playback time, the orbit view and selection do not. **새로 만들기**, **열기** and closing the window ask **저장 / 저장 안 함 / 취소** (Esc cancels; focus starts on 취소). Choosing 저장 on an untitled scene opens the save dialog, and cancelling it cancels the pending action.
- **New** clears objects, resets graphics to defaults, view, loop and speed, and forgets the file path.

## Implementation

- `MmdSceneDocument.cs`: data contract, validation, atomic write and path conversion.
- `MmdSceneWorkspace.Project.cs`: commands, dirty tracking, the generic modal dialog and `Application.wantsToQuit` handling.
- `WindowsRuntimeFilePicker`: shared open/save dialog code with `GetSaveFileNameW`.
- `PocSceneRestoreProbe.cs` and the `-pocSceneRestoreReport` route for the restart check.

## Actual Player verification

UI builds now go to `out/<configuration>/MVP-07-<backend>/`. `Invoke-SceneVerification.ps1 -Ui` requires 86 UI assertions (73 + 13), then launches **a second Player process** that opens the saved file and requires 9 restore assertions.

UI run (13 new):

- unsaved state shown in the title; save writes a valid file with relative paths matching the live objects, selection, graphics and timeline; no temporary files remain
- edits mark the scene dirty
- open with unsaved changes asks; 취소 keeps the edited scene
- 저장 안 함 reopens exactly the saved placement, rotation, scale, motions, stage, graphics, selection and duration
- a copy with a missing stage and a missing motion opens with both skipped and listed, the rest restored
- a corrupt file and a `schemaVersion` 99 file are rejected with the scene kept
- Ctrl+S saves to the current file; a quit request with unsaved changes asks, then is allowed once saved
- 새로 만들기 with unsaved changes asks; 취소 keeps the scene; at the end 저장 안 함 resets objects, path, dirty state and graphics

Restart run (9): fresh process empty; file opened without errors or problems; all objects in order; kind, resolved paths, motion, placement, visibility per object; graphics identical; view; paused timeline; motions sampled at the restored time; screenshot `scene-restore.png`.

Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass 86 UI and 9 restart assertions (3 objects restored); build-info.json reports Development true/false as expected. The saved file was inspected (relative `../` paths, schema 1) and the restart screenshot shows the restored title, stage selection, placement, loop/2× speed, 2.0 s position and graphics. The first build failed on a duplicate probe variable, fixed before these runs. Native inventory and legacy regressions were not rerun; the bash runner was used (no PowerShell 7 here; the updated .ps1 files were parse-checked with Windows PowerShell). Also recorded in TASKS.md (MVP-07).

## Limits

Native save/open dialogs are exercised only through the result handlers, not by clicking them. Loading is still synchronous after the loading message. Missing-file reconnection, recent files and auto-save are not implemented. Schema migration exists only as a version check (version 1).
