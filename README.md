# MMD Studio

작업 인계는 루트 [HANDOFF.md](HANDOFF.md)를 먼저 확인하세요. 현재 브랜치의 미커밋 구현, 실행 방법, 검증 결과와 다음 작업을 정리했습니다.

Unity 6000.3.28f1 / URP 17.3.0 Windows x64 desktop application, currently at stage 0 feasibility verification.

Snapshot: **v0.1.0-poc**. This is a source checkpoint, not the v1.0 MVP release. Scope, verified behavior and remaining work are recorded in [CHANGELOG.md](CHANGELOG.md). Test models/motions, diagnostic output and built executables are excluded; supply external assets and rebuild locally.

The follow-up branch adds a multi-character workspace: build with `scripts/Invoke-SceneVerification.ps1 -Ui`, then launch `scripts/Start-SceneStudio.ps1`. Add PMX characters and connect each VMD; select objects in the list to edit placement/visibility or remove them. Details and current limits: [SCENE_WORKSPACE_UI.md](docs/SCENE_WORKSPACE_UI.md).

Build outputs are now beneath root `out/debug/` and `out/release/`, separated by profile/backend. All build and launch scripts accept `-Configuration Debug|Release` (default Debug). See [BUILD_OUTPUTS.md](docs/BUILD_OUTPUTS.md) for paths and commands.

Open `MMDStudio/` with the pinned Unity Editor. Original requirements remain in `MMD_Studio_Spec_v1.1_Codex.md` and `MMD_Studio_Codex_Start_Here.md`.

Progress: docs/TASKS.md. Architecture and candidate decisions: docs/ARCHITECTURE.md and docs/DECISIONS.md. Reproducible verification: docs/TEST_PLAN.md.

Run `scripts/Invoke-PocVerification.ps1 -IncludeComparison -IncludeRecovery -IncludeCamera -IncludeDeformation` with PowerShell 7 to rebuild, run regression/recovery/camera/morph/IK checks and compare five playback modes. Local test assets must be present under `test/`, and Python 3 is needed for comparison analysis. Run `scripts/Start-MmdPoc.ps1` to open the Gene/Whiplash preview using verified direct Bezier/quaternion evaluation. Add `-EmptyWorkspace` to choose your own PMX/VMD through Windows dialogs. Alternatively open `out/debug/POC-06C-Mono/MmdStudioPoC.exe` directly. Use `-MotionMode baked-reference`, `sparse`, `sparse-default-ik` or `direct-default-ik` for diagnostic alternatives.

Local test assets, logs, builds and historical scratch files are excluded from Git. POC-05 adds a single-model runtime UI Toolkit workspace, native file selection, recoverable replacement and tracked resource cleanup. Seven regression cases and 13 recovery checks passed; five playback modes were rerun with 1,310 selected-FK comparisons. Actual PMX/VMD dialog loading and playback/pause were checked. Import/conversion is still synchronous; physics/audio are off. See docs/VMD_REFERENCE_REVIEW.md and docs/TASKS.md for limits and results. Full stage-0/MVP completion is not claimed. All ongoing work is performed directly by cloud Codex; local-model use is prohibited for this project.

POC-06A adds camera VMD connection/detach synchronized to character time. Select PMX, character VMD, then camera VMD. Or add `-CameraVmd 'test\aespa - Whiplash\aespa - Whiplash\Camera [SMOOTH].vmd'` to the preview launcher. The tested camera passes 326 selected key/fractional checks after correcting cut smearing in the reference curve path. Camera projection switches/positive distance are rejected; merged XYZ interpolation and 60 Hz approximation remain limits. Gene is partly outside the camera at 15 sec; automatic framing is pending.

POC-06B verifies visible Gene blink/mouth vertex morphs and controlled left-foot IK in the Player, fixes expression weights surviving motion replacement, and warns about unsupported morph types. The final full run passed 16 expected execution outcomes, including 21 deformation assertions. See docs/DEFORMATION_SUPPORT.md for the coverage matrix; generic morph/IK compatibility remains open.

POC-06C adds a tested IL2CPP Windows build and diagnostic fixed-step Bullet physics for Gene/Sandrone. Add `-Backend IL2CPP -IncludePhysics` to the full verification command. Final runs passed 18 IL2CPP / 10 Mono expected execution outcomes; full-context reset/preroll repeats within each backend. Launchers accept `-Backend IL2CPP` too. Normal UI physics remains OFF; full-motion/reference validation and clean-machine VC runtime deployment remain pending. See docs/PLAYER_PHYSICS_VALIDATION.md.

POC-06D adds full-duration Mono endurance and controlled official-physics-reference diagnostics. Numerical replay is distinct from accuracy: official-reference agreement fails at frame 2, so interactive physics remains disabled. See docs/PHYSICS_ENDURANCE.md for findings and reproduction; this diagnostic build is not the default preview.

POC-06E/F isolate input interpolation and solver-history differences; matched-input/history controls agree with the official converter. POC-06G adds a typed stateful physics adapter and an opt-in 60 Hz lifecycle service. Six cases pass 20 checks each on Mono and IL2CPP. Normal preview physics remains OFF. See [physics context diagnosis](docs/PHYSICS_CONTEXT_DIAGNOSIS.md) and [playback lifecycle](docs/PHYSICS_PLAYBACK_LIFECYCLE.md).



