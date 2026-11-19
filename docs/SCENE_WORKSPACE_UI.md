# MVP-02 — Multi-character workspace UI

2026-11-19. The separate scene workspace now connects actual UI Toolkit controls to MmdSceneService. Physics stays OFF; legacy single-model launchers remain available.

## Run

```powershell
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1
# or
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Backend IL2CPP
```

If the selected build is absent, first run `scripts/Invoke-SceneVerification.ps1 -Ui -Backend Mono` (or IL2CPP). The new launcher uses the separate `out/debug/MVP-02-<backend>/MmdStudioPoC.exe` with `-sceneUi`. The executable's legacy default route is retained; use this launcher for the new workspace. Optional `-Pmx` and `-Vmd` preload one external character/motion.

Click **캐릭터 추가**, choose a PMX, then **선택 캐릭터 VMD 연결** for that character. Add further characters and select them in the left list. The right inspector edits the selected object's position/rotation/scale, visibility and deletion. **전체 보기** and **선택 보기** frame the camera. The bottom bar controls shared play/pause/stop/seek/loop/speed; shorter motions hold their end pose.

The viewport rectangle excludes the toolbar, sidebars and playback/status area. Panels are opaque so old UI text cannot accumulate outside the camera's viewport clear. Explicit runtime composite-field layout keeps numeric values readable and editable. Numeric fields use delayed commits; invalid/nonfinite placement and scales outside 0.001–1000 are rejected and fields revert to the retained values. Import warnings, including unsupported morph types, are shown for the selected character.

## Service integration

- Model-only import is allowed: a character can exist without a motion and does not contribute to clock duration until VMD is connected. Static models are skipped by the shared sampler.
- First-motion failure preserves the static model; later replacement failure restores the previous motion pose.
- Visibility toggles renderer state, retaining active native/model owners so hidden animated characters can still be sampled safely.
- Placement edits are per-object and validated before assignment. Object selection uses stable scene IDs rather than list indices; deleting an object selects a remaining object or clears selection.
- The picker/import path pauses playback and skips the next automatic delta on resumption so synchronous loading/dialog time is not injected into scene time. Import/conversion remains synchronous after a rendered loading message.
- The existing native PMX/VMD dialog adapter is reused. This ticket's automated test supplies file results through the UI's result handler; it does not manually operate native dialogs or claim fresh mouse-driven dialog coverage.

## Actual Player verification

```powershell
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Backend Mono
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Backend IL2CPP
```

Both actual Windows Players pass 23 assertions. Tests dispatch UI Toolkit NavigationSubmit events to list/play/pause/stop/frame/delete buttons and change the actual vector/toggle/slider fields, verifying the resulting service/model state. The import handler is exercised with two external PMX files, distinct generated VMDs, bad files and cancellation.

Coverage includes empty/static model state, failed first VMD, independent motions and list entries, selection/inspector binding, selected-only transforms, invalid scale rollback, visibility across a real frame, live hidden ownership, shared timeline/playback/loop/speed, viewport bounds, bad import/cancellation, UI screenshot creation, selected removal/surviving playback and last-object clearing. This is actual UI event routing, not a claim that every mouse/keyboard interaction has been manually tested.

Both 1280×720 application screenshots were visually inspected: two models, object list, numeric fields, playback/status and selection controls are visible. Initial hidden/batch runs passed functional controls but failed Unity backbuffer capture. Final UI checks run without batch mode in a normal temporary application window, capture the application's own framebuffer, and close automatically. No desktop-capture/computer-use session was opened. The initial failed report is preserved as `scene-ui-batch-failure.json` in the Mono evidence directory.

Final Mono regression runs on the new build pass the original scene service (26), import/recovery (13), morph/controlled IK (21) and physics lifecycle (20) assertions. One first morph run produced a success report then exited with Windows access violation code -1073741819 during shutdown while other build/UI work was running. Its report/log are preserved as `Deformation-shutdown-failure.*`. The isolated rerun passes all 21 assertions and exits 0; cause is not established and no native shutdown fix is claimed. Track recurrence before release.

Both new build outputs pass native x64/export/notice inventory. Reports/images/debug/generated test assets remain outside Unity Assets and Git under `artifacts/scene-ui/<backend>` and `out/debug/MVP-02-<backend>`.

## Remaining

This is the first functional scene/inspector/playback layout, not the complete specified studio. Viewport orbit/pan/zoom and selection highlight were added in MVP-03 (VIEWPORT_CONTROLS.md); PMX backgrounds were added in MVP-04 (STAGE_WORKFLOW.md); the UI now builds to MVP-04-<backend>. Viewport picking and gizmos, camera/light/effects controls, JSON save/restore, asynchronous loading/cancellation and clean-machine distribution remain. Existing camera-VMD diagnostics are retained on the legacy preview but not yet connected to this shared-clock UI. Normal UI physics remains OFF.
