# Changelog

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
