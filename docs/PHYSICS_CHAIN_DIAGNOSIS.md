# Minimal-chain physics diagnosis — POC-06E

Date: 2026-11-19. Status: diagnostic execution passed; reference parity remains unresolved. No physics change is enabled in normal playback.

Follow-up: [PHYSICS_CONTEXT_DIAGNOSIS.md](PHYSICS_CONTEXT_DIAGNOSIS.md) identifies input-sampling and state-history differences. Matched-input/history diagnostic controls now agree with the official output; ordinary runtime behavior described below remains unchanged.

## Reproduction

Run from the repository root using PowerShell 7:

```powershell
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -IncludeRealModels
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -SkipBuild -IncludeRealModels -RetainSolverState
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -SkipBuild -IncludeRealModels -RetainAllSolverState
```

The script builds a separate Mono Windows Player in `out/debug/POC-06E-Mono`, generates original external PMX files under `artifacts/physics-chain/fixtures`, and runs the actual EXE. It does not use a local model, install a game plugin, or capture the desktop. Existing POC-06C interactive launchers remain unchanged with physics OFF. An initial sandboxed Unity build stalled at license initialization; its exact build process was stopped and the ordinary-permission build succeeded. The initial log is preserved as `poc-build-sandbox.log`.

## What is measured

Four original PMX 2.0 fixtures have five bones, a bone-driven rigid body and a simulated rigid body, and one plain triangle: free body, one locked-translation joint, joint plus append rotation, and joint plus IK. No third-party model/texture data is included. The generator and a hash manifest make these files reproducible outside Unity Assets/Git.

The existing three-second center translation/torso yaw fixture is used at 30 Hz, seed 0, zero warmup. Physics-controlled local bone poses are compared over 91 frames with the pinned official converter plus unchanged UMTDemo evaluator. Tolerances remain 1 mm / 0.1 degrees. This is an internal-package comparison, not independent MMD-engine validation.

The phase tracer uses reflection to call the pinned package's existing sample/reset, pre-physics solve, physics wrapper, post-physics solve, flush and rigid-body visualization update. It does not patch/copy the package. Frames 0–2 include every bone's input/solved local pose and validity flags, plus accumulator values at five phase boundaries. Across all 91 frames, its output on physics-controlled bones is compared with the untouched public `SolveWithPhysics` path after a fresh reset. The tracer is accepted only when this replay agrees within 0.000001 m / 0.001 degrees.

Counts identify C# solver/phase-wrapper calls, **not native Bullet substeps**. The test runs synchronously without yielding inside the simulation loop; it does not establish absence of double stepping in every future interactive scheduling configuration. Reflection diagnostics target this pinned Mono build; IL2CPP support is not claimed.

## Results

All six traced cases completed. Each had 91 solve, pre-physics, physics-wrapper and post-physics calls. All physics-controlled replay poses matched the public path with reported maximum position/rotation differences of zero.

| Fixture/model | First divergent frame | Maximum local position error (m) | Maximum local rotation error (degrees) | Reference parity |
|---|---:|---:|---:|---|
| Free body | none | 0.0000244894 | 0 | pass |
| Joint | 86 | 0.0000436578 | 0.888227 | fail |
| Joint + append | 86 | 0.0000436578 | 0.888227 | fail |
| Joint + IK | 86 | 0.0000436578 | 0.888227 | fail |
| Gene | 2 | 1.142695 | 10.985384 | fail |
| Sandrone | 2 | 0.003742395 | 100.094276 | fail |

The append/IK variants produced the same physical-pose error as the joint-only fixture. This does not isolate append/IK as innocent: the current vertical chain/yaw stimulus and physical overwrite do not prove meaningful excitation of those extra features. A non-collinear chain and independent target/rotation stimulus are needed before making that inference. The joint-only case demonstrates that a later rotational disagreement can occur even without those features; it does not reproduce the complex models' early-frame failure.

## Internal-state experiments

The official bake's `ApplyCompactBoneSamples` skips physics-controlled bones and replaces only input position/rotation and two validity flags for other bones. The runtime public path calls `ResetTransformsInternal` every step. Earlier transform-position/rotation preservation did not preserve the complete internal `BoneSolverState`.

Two explicit CLI-only experiments followed verified baseline tracing:

1. Retain complete previous `BoneSolverState` for physics-controlled bones after runtime sampling/reset.
2. Additionally retain other bones' history while replacing their input local position/rotation and clearing `hasSolvedTransform`/`solvedByIK`, following the bake's field-update pattern.

Both experiments completed for all six cases and produced the same reference-error maxima as each other. Gene improved to 0.319127738 m / 8.602312 degrees, but still first diverged at frame 2. Sandrone and the four synthetic cases did not improve. Therefore internal state retention affects Gene but is not a complete explanation or a validated production fix. Experiment reference comparisons use the experimental pose arrays; `phaseTrace.equivalent` still describes the separately verified **unmodified baseline** tracer, not equivalence of the experimental behavior.

Evidence: `artifacts/physics-chain/{verification,retain-state-verification,retain-all-state-verification}.json` and `{traced,retain-state,retain-all-state}/<case>/report.json`, matching Player logs, fixture manifest, and the successful build log. The final baseline was rerun with the final POC-06E build. Execution success and trace equivalence are separate from reference parity.

## Next isolation

1. Use a non-collinear chain and separately animated IK target/append driver; prove each feature changes a pre-physics pose before interpreting comparisons.
2. Compare imported runtime solver configuration/initial state with fresh converter solver context, including parent/constraint/IK configuration and physical-bone selection. Capture the official bake phase states if needed rather than inferring its intermediate poses from final curves.
3. Check whether remaining small input-sampling differences can cause the joint fixture's late rotation error using identical integer-frame inputs. Keep this separate from the complex models' frame-2 discrepancy.
4. Preserve explicit reset/preroll semantics. Do not expose interactive physics until the reference discrepancy has an explained, tested resolution.
