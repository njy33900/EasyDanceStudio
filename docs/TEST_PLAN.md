# Stage 0 verification

Build location convention: root out/debug/ or out/release/, selected with `-Configuration Debug|Release` (default Debug) on every build/launch script. See BUILD_OUTPUTS.md. Release verification evidence is placed in the corresponding artifacts directory's release/ subfolder.

## MVP-02 multi-character UI

Run `scripts/Invoke-SceneVerification.ps1 -Ui -Backend Mono` or IL2CPP; add `-SkipBuild` to reuse. Each backend passes 23 assertions using actual UI Toolkit submit/change events in the separate MVP-02 Player. The test briefly opens a normal non-batch application window to capture its own UI framebuffer and then closes. No desktop capture is used. Launch the user workspace with `scripts/Start-SceneStudio.ps1`. See SCENE_WORKSPACE_UI.md for exact coverage, layout verification and the preserved one-off morph-regression shutdown failure.

## MVP-01 multi-character scene

Run `scripts/Invoke-SceneVerification.ps1 -Backend Mono` or `-Backend IL2CPP`; add `-SkipBuild` to reuse. Separate actual Windows Players exercise Gene/Whiplash and Sandrone/a generated distinct VMD simultaneously, shared clocks/placement, failure recovery and resource retirement. Each backend passes 26 assertions, including a paused actual-frame regression. Reports/GPU images are isolated in artifacts/scene/<backend>. See MULTI_CHARACTER_SCENE.md. This is the scene service gate, not production multi-object UI completion.

## Stateful feature and real-motion follow-up

Run `scripts/Invoke-PhysicsFeatureVerification.ps1 -Backend Mono` (or IL2CPP) to build the separate POC-06H Player and test actual non-collinear append/IK excitation. Add `-RealMotion -Seconds full` on Mono for two complete stateful Whiplash replays per external model; `-SkipBuild` reuses the selected build. Reports are isolated in artifacts/physics-features/<backend>. Detailed thresholds and limitations: PHYSICS_FEATURE_VALIDATION.md. These checks do not enable normal UI physics or establish independent MMD physical fidelity.

## Automated verification (PowerShell 7)

```powershell
.\scripts\Invoke-PocVerification.ps1 -IncludeComparison -IncludeRecovery -IncludeCamera -IncludeDeformation
```

This builds the current POC-06C Mono Player, runs seven regression cases and four extra motion modes, then compares actual pose dumps. Python 3 is needed. The five modes are sparse, sparse-default-ik, baked-reference, direct and direct-default-ik. Full pose parity against the reference bake is not assumed: its non-Rotatable constraint channels are incomplete. Independent direct-FK and controlled shoulder-constraint acceptance must pass; see TASKS.md.

Use -IncludeVmd for the seven regression cases only, -IncludePmx for PMX-only checks, and no include flags for the cube baseline. After a matching build and passing regression run, -SkipBuild -OnlyComparison runs five motion modes without repeating negative controls. It writes artifacts/poc-comparison-verification.json plus vmd-comparison.md/.json. This also enforces --require-direct-accuracy: at least 1,000 selected source-FK samples, maximum rotation error <=0.05 degrees and controlled Gene shoulder-inheritance error <=0.05 degrees. Other reported path differences are findings, not equality assertions.

The GPU probe uses RenderPipeline.StandardRequest for a real offscreen camera render and writes a PNG beside its JSON. Source assets are never imported into the Unity project. Failure probes deliberately exit 1 and write success=false; the script requires those failures. Reports must be newer than the process start time.

Add -IncludeRecovery to exercise 13 in-process workspace checks: external loading, reassignment from animated pose, invalid VMD/PMX rollback, truncated PMX cleanup, cancellation, combined model/motion transaction rollback, three model replacements and clear. Require artifacts/poc-recovery.json success=true with every check passed. Cleanup assertions cover the successfully tracked Unity resources and controlled parse failures, not arbitrary native-converter failures.

For interactive verification, run scripts/Start-MmdPoc.ps1 -EmptyWorkspace, select Gene_light.pmx through PMX 가져오기, then Whiplash [SMOOTH].vmd through VMD 연결. Check motion advancement, pause, timeline and stop. Choose artifacts/recovery-invalid.vmd to check the visible error and preservation of the previous pose. Cancel a picker and verify no replacement. Development builds may also show Unity's development console for logged import exceptions.

## Camera reference probe

After a matching current backend build, run `scripts/Invoke-PocVerification.ps1 -SkipBuild -OnlyCamera`. It uses the external Camera [SMOOTH].vmd, verifies ten runtime lifecycle/render/sync assertions and runs an independent raw-camera oracle over 66 selected source keys and 260 additional fractional times. Acceptance is <=0.001 m position, <=0.05 degrees forward/up basis and <=0.01 degrees FOV. Source-key and fractional maxima are reported separately in artifacts/camera-comparison.json. Generated copies test explicit rejection of projection-off/positive-distance keys. Original assets are never modified.

Inspect camera-pose-0/15/30.png and the runtime UI separately. Current images show Gene partly clipped at 15 sec; camera coordinates are preserved and auto-framing is not implemented. The oracle uses a declared shortest-Euler camera rotation policy; it does not execute another engine. UMT merges XYZ movement controls and bakes at 60 Hz, so the controlled equal-XYZ sample does not establish general camera fidelity. Consecutive-frame cuts must hold the previous pose until the next key rather than blend between baked samples. Physics/audio remain off. Camera duration clamps to its last pose when the character clock runs longer.

## Controlled morph / IK probe

Run `scripts/Invoke-PocVerification.ps1 -OnlyDeformation` to build and test only this ticket, or add `-SkipBuild` after a matching current backend build. The probe creates known-name CP932 VMD2 fixtures under artifacts, loads them in the actual Windows Player against the external Gene model, and writes artifacts/poc-deformation.json. Require success=true and all 21 checks completed. The cases cover seven weight sample times for blink/mouth, baked mesh deformation, GPU pixel differences, bone-only replacement clearing prior expression, and foot IK on/off/on/repeated seeking. Inspect deform-neutral.png, deform-active.png and deform-ik-on/off.png as well.

Thresholds: expected blendshape weights within 0.001 of 0..100; >20 moved vertices and >0.0001 m maximum mesh displacement; >20 pixels with RGB delta sum >30; controlled disabled IK target distance >0.02 m and enabled distance <20% of disabled; repeated target position within 0.00001 m. The supplied target displaces the left-foot controller by (+0.08,+0.04,0) m and is reachable. No original source asset is modified. Gene Material/UV morphs are warned as unsupported; Group and cross-model IK need separate coverage. See DEFORMATION_SUPPORT.md.

## Backend and fixed-step physics verification

```powershell
.\scripts\Invoke-PocVerification.ps1 -Backend IL2CPP -IncludeComparison -IncludeRecovery -IncludeCamera -IncludeDeformation -IncludePhysics
.\scripts\Invoke-PocVerification.ps1 -Backend Mono -IncludeVmd -IncludeRecovery -IncludePhysics
```

Use `-SkipBuild -OnlyPhysics -Backend IL2CPP` after a matching build for physics alone. Gene is required; the prepared Sandrone_Runtime model is also tested when present. Mono evidence stays in artifacts; IL2CPP evidence stays in artifacts/il2cpp. Physics failures preserve the passed core summary and both models' reports. The full IL2CPP run has 18 expected execution outcomes with both local models present; Mono's shorter regression has 10.

Physics checks require eight assertions per model: rigid bodies/joints, disabled automatic advancement, physics changing the solved pose, full-context reset/replay repeatability within 0.001 m, reset/preroll seek repeatability within 0.001 m, stable pause, GPU images and model-owner release. All 360 fixed steps also check finite positions/rotations and a coarse 100 m bound. These do not prove accurate collisions or full-duration visual correctness. Inspect poc-physics-gene/sandrone-on/off.png and see PLAYER_PHYSICS_VALIDATION.md. UI playback remains physics OFF.

Run the read-only inventory separately for either build:

```powershell
python scripts/inspect_player_native.py --player out/debug/POC-06C-IL2CPP --report artifacts/il2cpp/native-inventory.json
```

Require x64 files, required exports and copied notices. This development PC has the required VC runtimes; clean-machine distribution is not verified. UI Toolkit/native dialogs need an interactive check because the batch suite skips UI construction. The final IL2CPP interactive test loaded Sandrone/Whiplash through Windows dialogs and paused successfully.

## Physics endurance and reference diagnostics

Use `-Backend Mono -PhysicsReference` to build POC-06D and execute controlled comparisons to the official physics bake/demo evaluator. Reports must have success=true for execution; inspect parityPassed separately. Current reference agreement fails for both local models. This command is diagnostic and does not require parityPassed=true.

Then run `-Backend Mono -PhysicsEndurance -SkipBuild` for two full-duration trials per model. Require success=true, trials=2, stepsPerTrial=11080, comparedPoses=11081, completedSteps=22162, maximumReplayError<=0.001 m, and all nine checkpoint PNGs. Inspect large step displacement statistics separately; finite/<100 m bounds do not establish visual/constraint correctness. Evidence is separated under artifacts/physics-reference and artifacts/physics-endurance. See PHYSICS_ENDURANCE.md for the exact limits and findings.

## Direct build example

PowerShell, repository root. Unity must not already have this project open.

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe'
& $unity -batchmode -nographics -quit -projectPath "$PWD\MMDStudio" -executeMethod MmdStudio.Poc.PocBuild.BuildWindows -pocOutput "$PWD\debug\POC-06C-Mono\MmdStudioPoC.exe" -logFile "$PWD\artifacts\poc-build.log"
```

Require build exit code 0, POC_BUILD_SUCCESS log, and actual EXE.

## Player smoke

```powershell
$player = Start-Process "$PWD\debug\POC-06C-Mono\MmdStudioPoC.exe" -ArgumentList '-batchmode', '-screen-width', '640', '-screen-height', '360', '-pocSmokeReport', "$PWD\artifacts\poc-player.json", '-logFile', "$PWD\artifacts\poc-player.log" -PassThru -WindowStyle Hidden
$player.WaitForExit()
$player.ExitCode
Get-Content artifacts\poc-player.json
```

Do not use -nographics for a positive Player check: the probe requires URP initialization. Require exit 0 and JSON success=true, Unity version and render pipeline/device details. PNG inspection supplements the coarse nonempty/magenta pixel checks; these are not a general image quality metric. Running the EXE normally opens the empty UI Toolkit workspace. Choose PMX then VMD through the native dialogs. With -pocSmokeReport the diagnostic probe renders, reports and auto-exits.

## Remaining MMD gate

External PMX/VMD probes include six captured poses, original-key quaternion checks, one-second real-time progression, pause/loop/repeated seek, and 256 nonsequential times over the full duration. Five selected FK channels produce 1,310 comparisons to a separate raw-VMD Bezier/Slerp oracle. This is selected numeric coverage, not a full continuous-play visual signoff. Oracle v2 follows Babylon's actual rotation block layout, excludes first-block physics flags and includes a synthetic layout regression check. Earlier ~0.6-degree oracle-v1 claims are withdrawn. Runtime shoulder inheritance is checked directly because the official bake omits that helper's output. Check visible morphs, generic IK, broader failure recovery, physics and native distribution before accepting stage 0.

Known limits: controlled Gene/Whiplash inputs only; synchronous import/conversion can block the main thread; exhaustive malformed-data/texture-path validation and partial-import resource cleanup are not production-ready. Live physics is explicitly disabled. Controlled vertex morph/left-foot IK checks above do not establish general morph/IK correctness. Windows Mono and IL2CPP Players have both been built and tested; controlled fixed-step physics is described in PLAYER_PHYSICS_VALIDATION.md. No audio sync; limited camera VMD playback is described above. See VMD_REFERENCE_REVIEW.md for source-based comparison findings.




