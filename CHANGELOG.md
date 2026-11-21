# Changelog

## Unreleased

- Autosave and crash recovery (H1-06): with unsaved changes the scene is written every 2 minutes to a separate recovery file (never the original); a normal save/new/open/quit removes it, and after a crash the next launch offers to restore (original path kept, marked unsaved), discard or decide later. Verified across forced kills (`scripts/bash/run-recovery.sh`).
- Body secondary motion (PH-05, ADR-018): chest/hip spring bones for models that have those bones but no rigid bodies, with detection in the inspector, presets (light/medium/strong), "apply to all characters", a model-terms notice and per-character saving. Spring bakes are deterministic and cheap (full song 4.3 s); in a chest close-up (forced test on Sandrone) the strong preset visibly moves the chest at the largest-swing moment; tuning on a real target model is still to do.
- Physics finish, part 1: compressed bake cache (Sandrone 158 → 71 MB, error at most 0.36 mm), jump check on visible bones only with an inspector warning, faster baking while paused (Sandrone full song in 132 s), and per-part physics strength sliders (hair, clothes, chest, hip, accessories, other; 0-100 %, instant, saved with the scene).
- Experimental per-character physics in the scene (off by default): the inspector's "물리 (실험)" toggle bakes physics ahead of playback, shows progress, re-bakes after motion or placement changes, falls back to off for that character on failure, and is saved per character (optional schema-1 field). Release IL2CPP and Release Mono pass 130 UI and 9 restart assertions.
- Physics bake cache engine (PH-03): stateful 60 Hz physics is computed ahead in per-frame slices and only physics-affected bones are cached and overlaid on the physics-OFF pose. It matches an uninterrupted simulation within 5.4 μm (Gene) and 0.7 μm (Sandrone); without restoring the solver state, the first tick after every bake-slice boundary is wrong (Gene: 91 of 1,201 ticks, up to 8.8 m), so the restore is required; the bake probe now compares every tick. Full-length bake: Gene 7.8 s / 30 MB, Sandrone about 185 s / 158 MB (not yet wired into the scene UI).
- PH-00 physics measurements (`scripts/bash/run-physics-measure.sh`): a 571-rigid-body model costs 11 ms per 60 Hz tick and 20 s to seek to 30 s, so the physics bake cache becomes the default playback path; chest and other secondary-motion bodies are stable, one model's cloak weight bodies are unstable, characters do not collide with each other.
- UnityMMDTools is now an embedded package (`MMDStudio/Packages/com.candidumgames.unitymmdtools`, base commit `db35d9cb`, local changes in `PATCHES.md`). Patch #1 releases VMD conversion native contexts and buffers when conversion throws: a failing conversion leaked 16.7 MB before and 407 bytes after (Release IL2CPP).
- Enhancement spec 0.5 (ADR-017): optional physics covering every PMX rigid body including body secondary motion, look presets, short-form/transparent output, and differentiation features (beat-based auto camera, lip sync, motion fitting).
- Added a repeated cancel/retry scene-open probe (`scripts/bash/run-scene-cycle.sh`): 20 Release IL2CPP cycles keep object counts identical and Unity allocated memory within +1.9 MiB.
- Loading progress callbacks can no longer abort PMX/VMD loading: an exception thrown from one is logged as a warning, so it cannot interrupt UMT's VMD conversion before it frees its native solver contexts.
- When UMT fails part way through building a model, the meshes, materials and avatar it already created are now destroyed instead of leaking; a fault-injection check in the PMX staging probe verifies nothing is left after failures in three late stages.
- Opening a `.mmdscene` no longer clears the current scene first: entries are prepared asynchronously as hidden candidates with a cancel button, and the scene is replaced in one step only when all are ready; cancel or failure keeps the running scene. Debug Mono, Release IL2CPP and Release Mono pass 126 UI and 9 restart assertions (Release worst frame during open about 66–68 ms).

- Split PMX texture, material and rename work across frames (H1-01b): textures and materials are prepared one per frame by a project-side port of the UMT logic and handed to UMT through its public hooks, with cancel checks and cleanup between items; rename lists are cached and renaming uses UMT's async API. Release IL2CPP Sandrone worst frame 489.6 ms → 55.8 ms; a Player probe confirms identical textures, materials, renderers and warnings to the original import for three models.

- Started H1-01 staged UI loading for PMX and VMD, with a visible stage/cancel card and safe-boundary cancellation. Release IL2CPP Player passes 124 UI and 9 restart checks including cancelled PMX/VMD preserving the previous scene. One Sandrone import still has a 490 ms worst frame in UMT texture/material phases; scene-file loading and complete mid-build resource cleanup remain open.
- Added a hide button to each floating card, including the status bar. Hidden cards leave a small edge tab for direct reopening; the View menu remains available. Focus view hides the tabs and keeps only its corner UI restore control.
- Changed the workspace to a full-window 3D viewport with floating white cards. The View menu hides individual cards; H toggles focus view and H/Esc or the corner UI button restores controls. Selection highlight and gizmo disappear in focus view; framing targets the visible safe area. Debug Mono and Release IL2CPP V0 builds pass 114 UI and 9 restart checks. Manual input and 100% DPI checks remain.
- The current V0 Player now opens the scene workspace when double-clicked. Legacy single-model preview and diagnostic probes use explicit flags; a direct Release IL2CPP launch opened the new UI and exited cleanly, while the legacy scene probe still passed 26 checks.

- Fixed data loss when opening scenes with missing files: unresolved models and missing motion paths are now kept in the document, marked in the explorer and inspector, written back unchanged on save (rebased when saved elsewhere) and removed only by explicit delete. Object IDs are stable across reloads. All four builds pass 100 UI and 9 restart assertions (out/<configuration>/R0-02a-<backend>).

- Added a runtime transform gizmo: W/E/R move/rotate/scale in world axes with a viewport mode bar, constant screen size, hover highlight and two-way inspector sync. Builds go to MVP-10-<backend>; all four Debug/Release × Mono/IL2CPP builds pass 95 UI and 9 restart assertions; Release performance re-measured at 568–646 FPS average.

- Redesigned the workspace to a light, minimal layout: top-bar icon actions, scene explorer tree with light and camera entries, rounded viewport card with empty state, stacked inspector sections, icon playback bar and status hints; USS theme and vector icons. Drag and drop is out of first-phase scope. Builds go to MVP-08-<backend>; all four Debug/Release × Mono/IL2CPP builds pass 86 UI and 9 restart assertions.
- Added the first-phase performance report (two characters with full VMD, stage, effects at 1080p: 565–701 FPS average on the development PC, 30 FPS target met) with a known-issues list, 20 clean shutdown-recurrence runs and a native inventory of all outputs.

- Added .mmdscene projects: new/open/save/save-as and Ctrl+S, schema-1 JSON with relative paths and atomic save, validation that keeps the current scene on invalid files, skipped-and-listed missing assets, unsaved-change prompts for new/open/quit and -sceneFile launch. UI builds go to MVP-07-<backend>; Debug Mono/IL2CPP and Release Mono pass 86 UI assertions and a 9-assertion relaunch restore.

- Added the inspector Graphics tab: FOV, saved views and view reset; directional light colour/intensity/direction and shadow quality; Bloom, Neutral tonemapping, exposure/contrast/saturation; background colour. Settings are a validated serializable state for upcoming scene files; post-processing uses a neutral priority volume over the template defaults. UI builds go to MVP-06-<backend>; Debug Mono/IL2CPP and Release Mono pass 73 actual UI Player assertions, including a rendered-frame exposure check.

- Added viewport left-click picking (shared selection with the list, empty click clears), Space play/pause and Delete with a modal delete confirmation (Esc cancels; the inspector delete button uses the same dialog). UI builds go to MVP-05-<backend>; Debug Mono/IL2CPP and Release Mono pass 60 actual UI Player assertions.

- Added PMX backgrounds (MMD-105): 배경 추가, static Stage objects with placement/visibility/deletion and no motion or clock contribution, explorer grouping and stage-aware inspector; VMD on a stage is rejected. Failure text no longer lingers in the status bar. UI builds go to MVP-04-<backend>; Debug Mono/IL2CPP and Release Mono pass 50 actual UI Player assertions with an original generated stage fixture.

- Added viewport camera controls to the scene workspace: right-drag orbit, middle/Shift+right-drag pan, wheel zoom, F framing, gated by UI panel picking and text-field focus; plus a 3D selection highlight that follows the list. New UI builds go to out/<configuration>/MVP-03-<backend>. Debug Mono/IL2CPP and Release Mono pass 40 actual UI Player assertions; physical mouse input is not yet manually checked.

- Moved build outputs to root out/debug/ and out/release/ with profile/backend subdirectories. Build/launch scripts accept -Configuration Debug|Release; Release disables Unity Development mode. Existing outputs were migrated into out/, which is Git-ignored.

- Connected the scene service to a separate multi-character UI Toolkit workspace: model-only imports, per-object VMD/placement/visibility/deletion, camera framing and shared playback controls. Start with scripts/Start-SceneStudio.ps1. Mono and IL2CPP actual UI event checks pass 23 assertions each; full studio controls/persistence remain pending.

- Added an opt-in multi-character scene registry, independent placement/resource ownership, transactional per-object motion replacement and a shared playback clock. Normal production UI integration remains pending.

- Added non-collinear append/IK enabled-versus-disabled controls in separate Mono and IL2CPP Windows Players. Both features measurably affect the kinetic driver and connected dynamic bone; reset/replay checks pass.
- Verified the typed stateful service on two full Whiplash replays each for Gene and Sandrone on Mono, with zero maximum position replay difference and finite/coarse-bound checks. This is reproducibility, not visual/collision accuracy.
- Fixed the next MVP target and scope in docs/MVP_SCOPE.md: multi-character scene registry and shared clock, physics OFF, before broader UI/background/save work.

## v0.1.0-poc — 2026-11-19

Initial source checkpoint for Windows x64 feasibility work. This is not a completed stage-0 gate or the v1.0 MVP.

### Included

- Unity 6000.3.28f1 / URP project and pinned UnityMMDTools dependency.
- External PMX/VMD loading in Windows Player, a single-model UI Toolkit workspace, native Windows file selection and recoverable asset replacement.
- Direct Bezier/quaternion animation, controlled vertex morph/foot-IK checks, and limited camera VMD playback.
- Mono/IL2CPP build and verification scripts, native-library/notice inventory, and original external diagnostic-fixture generators.
- Physics endurance/reference investigations and a project-owned typed stateful solver/60 Hz lifecycle service. Physics is diagnostic/opt-in; the normal preview remains physics OFF.
- Requirements, architecture/decision records, source provenance/notices, and documented verification results/limits.

### Verification recorded before this checkpoint

- POC-06G: six lifecycle cases per backend, 20 assertions each, on actual Mono and IL2CPP Windows Players. Each backend has 179,712 bone-pose comparisons with zero reported difference from its own retained-state diagnostic reference.
- New Mono Player: 13 import/recovery and 21 expression/controlled IK regression assertions pass.
- Both POC-06G outputs pass native x64/required-export/notice inventory. Earlier reference and loader validation are detailed in docs/TASKS.md.
- Runtime test output is intentionally not tracked. Results here describe local verification; fresh clones must supply external assets and reproduce the checks.

### Remaining

- Multi-character scene and background management, object editing, production viewport/graphics controls, and scene save/restore.
- Longer-motion and stronger IK/append/physics validation, scalable/cancellable seeks, broader malformed-input handling and clean-machine deployment validation.
- Audio synchronization and full MVP UI integration.

### Checkout requirements

Open `MMDStudio/` with the pinned Unity Editor and restore UPM dependencies. PowerShell 7/Python 3 are used by verification scripts; Windows build modules, the relevant compiler toolchain and an activated Unity license are required for builds. Test models, motions, textures, builds, logs, research downloads, Python caches and machine-generated Unity folders are excluded from this repository.
