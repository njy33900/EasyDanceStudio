# MMD Studio architecture

## Current scope

Stage 0 only: first establish a reproducible Unity/URP Windows Player build, then prove external PMX/VMD loading in that Player. A rendered cube or imported prefab is not evidence of runtime MMD support.

`MMDStudio/` is the Unity project; `test/` is local-only external test data, excluded from Git and Player assets. `builds/` and `artifacts/` hold local build products and verification evidence.

Planned boundaries: Core (engine-independent DTOs/contracts), Mmd (runtime loader/player adapters), Scene (objects/selection/shared PlaybackClock), Rendering (URP/camera/light), Persistence, UI. Export and Extensions remain deferred.

First executable probe uses a runtime-only component and a separate Editor build entry point. UnityEditor APIs must never enter the runtime assembly.

POC-05 adds PocWorkspace as a stage-0 transactional single-model controller and runtime UI Toolkit document. WindowsRuntimeFilePicker wraps the Windows native open dialog; PocAssetLifetime owns imported Unity resources. Candidate replacement succeeds before retiring the old scene. This probe still uses synchronous import/conversion and a local playback component; it does not yet implement the planned multi-object scene/shared clock boundaries.

POC-06A adds a disposable camera reference adapter. It evaluates UMTDemo's camera behaviour into a private disabled-camera rig and copies its pose/FOV to the visible camera after character playback. Consecutive source-frame cut intervals hold their previous key to prevent baked-curve interpolation from smearing a cut. The clock remains the single character's MmdPocMotion; an independent scene PlaybackClock is still deferred.
