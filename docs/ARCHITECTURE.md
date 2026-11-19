# MMD Studio architecture

MVP-02 adds `MmdSceneWorkspace`, a separate UI Toolkit route (`-sceneUi`), with object selection and inspector controls bound to the scene service. Scene objects may have no motion; static entries do not contribute to scene duration. Model summaries retain support warnings, placement is validated in the service, and hiding only affects renderers. The existing single-model preview remains separate. User launch/coverage: SCENE_WORKSPACE_UI.md.

MVP-01 adds `MmdSceneService`: independent imported owners/stable IDs, transactional per-object motion replacement, and a shared scene clock with component pose ownership. The old single-model `PocWorkspace` remains a regression/preview route; the new registry is not yet wired into production UI. See MULTI_CHARACTER_SCENE.md for behavior, initialization ordering and tested limits.

## Current scope

Stage 0 only: first establish a reproducible Unity/URP Windows Player build, then prove external PMX/VMD loading in that Player. A rendered cube or imported prefab is not evidence of runtime MMD support.

`MMDStudio/` is the Unity project; `test/` is local-only external test data, excluded from Git and Player assets. Root `out/debug/` and `out/release/` hold build products; `artifacts/` holds verification evidence. Profile/backend subdirectories remain separate. Build/launch configuration and migration details: BUILD_OUTPUTS.md.

Planned boundaries: Core (engine-independent DTOs/contracts), Mmd (runtime loader/player adapters), Scene (objects/selection/shared PlaybackClock), Rendering (URP/camera/light), Persistence, UI. Export and Extensions remain deferred.

First executable probe uses a runtime-only component and a separate Editor build entry point. UnityEditor APIs must never enter the runtime assembly.

POC-05 adds PocWorkspace as a stage-0 transactional single-model controller and runtime UI Toolkit document. WindowsRuntimeFilePicker wraps the Windows native open dialog; PocAssetLifetime owns imported Unity resources. Candidate replacement succeeds before retiring the old scene. This probe still uses synchronous import/conversion and a local playback component; it does not yet implement the planned multi-object scene/shared clock boundaries.

POC-06A adds a disposable camera reference adapter. It evaluates UMTDemo's camera behaviour into a private disabled-camera rig and copies its pose/FOV to the visible camera after character playback. Consecutive source-frame cut intervals hold their previous key to prevent baked-curve interpolation from smearing a cut. The clock remains the single character's MmdPocMotion; an independent scene PlaybackClock is still deferred.
