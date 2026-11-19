# MVP-01 — multi-character scene and shared clock

2026-11-19. Runtime service and separate Windows Player verification; production scene UI/background/persistence remain subsequent tickets. Physics stays OFF.

## Implementation

`MmdSceneService` owns a registry of stable object IDs, separate imported-model/resource owners, per-object motion components and one double-precision scene time. Model import does not replace another model or reframe the camera. Placement belongs to each model's container transform. All registered motion components are sampled from scene time in direct-default-IK mode and have their ordinary automatic clocks disabled.

The scene clock supports automatic Player-frame advancement using unscaled time, manual advancement for verification, pause/stop/seek/loop, and speed 0.5–2.0. Scene duration is the longest registered motion. Shorter clips hold their final pose while scene time continues; the whole scene wraps at its longest duration. Empty scenes stop and reset time. Removal/replacement clamps current time to the remaining duration.

Scene clock ownership prevents ordinary component sampling, competing physics ownership and direct Load on owned motion components. Per-object ReplaceMotion creates and samples a candidate before retiring the old component; a failed candidate restores the previous pose at scene time. Stable ID and model-resource owner survive successful motion replacement. Candidate model import failure releases only candidate resources. Selected removal and Clear retire the appropriate imported owners.

The existing importer now has a `frameCamera` option; the legacy single-model workspace retains its default behavior. Scene imports use false and do not modify camera/cube state. Existing UI/launchers remain the single-model preview; the new scene service is not yet connected to production controls.

## First-frame pause correction

An actual Player-frame test initially failed after the immediate numeric checks passed: a paused pose changed on the first yielded frame. Pinned UMT Start performs an initial solve even when transformEnabled is false, so already-sampled scene poses can be processed again. The failure is preserved in `artifacts/scene/Mono/scene-paused-frame-failure.json`.

The scene service runs at execution order 9000 and reasserts source-sampled poses during Update/LateUpdate through the frame after model import. This runs after the initialization callbacks and before pinned UMT's order-10000 LateUpdate/SDEF reconciliation. Ordinary paused frames do not continuously resample after that short settling period. The added paused-frame regression now passes on Mono and IL2CPP. The adapter depends on pinned UMT scheduling and must be revalidated on upgrades; arbitrary imports initiated after these callbacks need review when wiring UI.

## Verification

The actual EXE imports external Gene and Sandrone simultaneously. Gene uses the supplied full Whiplash VMD; Sandrone uses an original three-second controlled VMD generated outside Assets/Git. Both roots are placed independently. The GPU image shows two textured characters with different poses, and the Mono image was visually inspected.

Twenty-six assertions cover:

- Distinct stable IDs/resource owners, independent VMD sources, preserved first pose on second import, camera preservation, disabled physical/component clocks.
- Shared advancement, pause, shorter-clip hold, repeated seek, independent placement, speed, loop and stop.
- Bad PMX import, successful PMX followed by bad VMD, failed VMD replacement/pose rollback, external pose/clip-write rejection and successful per-object replacement while the other object survives.
- Paused poses surviving an actual Player frame, GPU capture, selected resource retirement, surviving playback, ID-preserving changed motion, actual frame-driven clock progression and final resource cleanup.

The final Mono run passes all 26 assertions with reported maximum repeated-seek world-position error 0. Its measured automatic progression over the realtime wait was approximately 0.173 seconds; this is a liveness check, not a frame-rate benchmark. Distinct object IDs are stable for each object's lifetime, not yet serialized across application restarts.

The final IL2CPP run also passes all 26 assertions, with repeated-seek position error 0 and approximately 0.168 seconds of automatic progression. Both GPU images were inspected and show two textured characters. On the final new Mono Player, the existing single-model recovery (13), morph/controlled IK (21) and stateful physics lifecycle (20) assertions pass. Both output native x64/export/notice inventories pass. These are same-PC results; no cross-backend physical equality or clean-machine distribution claim is made.

## Reproduction

```powershell
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Backend Mono
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Backend IL2CPP
```

Add `-SkipBuild` to reuse the corresponding Player. Outputs: `out/debug/MVP-01-<backend>` and `artifacts/scene/<backend>/scene.json`, `scene.log`, `two-models.png` and build log. The script checks process success, report freshness/backend and at least 26 passed assertions. All test assets/images/builds remain ignored and external to Unity Assets.

## Remaining

Connect the service to Scene Explorer selection and per-object controls, then PMX background and scene save/restore. Imports/conversion are synchronous, physics is OFF, and the service does not yet provide asynchronous cancellation, unexpected solver-exception pose rollback, persistence, undo or a performance guarantee for multiple heavy models. This ticket establishes two-character service behavior; it does not complete the v1.0 MVP or a clean-machine distribution audit.
