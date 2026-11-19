# POC-06F — runtime / converter context and identical-input controls

Date: 2026-11-19. Controlled three-second diagnostic comparison; production physics remains OFF.

## Finding

Two differences explain the observed reference disagreement **in this controlled test**:

1. The runtime direct evaluator and official converter generate slightly different interpolated inputs. At frame 1, the maximum input-position difference is approximately 0.000007327879 m. The converter's private `EvaluateBezier` uses a 12-iteration bisection, starting at the target X, without the runtime evaluator's exact-linear shortcut. Runtime `MmdDirectBoneTracks.Weight` returns X exactly for diagonal controls and otherwise uses 24 iterations. Therefore equal VMD keys/time do not imply identical floating-point input samples.
2. The runtime resets bone solver state before each solve; the official bake retains physical state and replaces only selected input/validity fields for other bones. The earlier state-preservation experiment retained history but still used the different runtime samples.

With converter-generated inputs **and** bake-style solver-history retention, all six tested cases match the official converter/demo's physics-controlled local poses across 91 frames with maximum reported position/rotation errors of zero. This identifies mismatched comparison conditions; it does not establish that matching the converter's approximate Bezier evaluator is more accurate than the runtime evaluator, or that general physical behavior is correct.

## Method and controls

`PocPhysicsContextProbe` creates a fresh standalone transform/physics context using the pinned package's actual internal initialization functions. It compares the imported runtime context after ResetToBindPose with the new converter context:

- Bone initial/configuration data, parent/constraint flags and indices.
- Pre-/post-physics ordering, IK controller/link data and lookup tables.
- Rigid-body simulation definitions and sorted kinetic/simulated body lists.
- Root parent matrix, constraint/IK enable flags, physics seed and ground setting.
- Physics-controlled bone selection from the package helper versus the existing probe selection.

Integer/bool/enum fields are compared exactly; floating-point configuration fields use an absolute 0.000001 tolerance per scalar. All six cases have zero differences under those criteria and identical physical selection. This is not a bitwise comparison of all initial native Bullet memory.

The diagnostic calls the converter's real resolver, compact source-track builder, key sample fill/interpolation and ApplyCompactBoneSamples functions through reflection. It splits the existing solver into pre-physics, physics and post-physics phases. It retains the converter's original field-update behavior. The fixture contains no IK-toggle keys; the context defaults enable all IK controllers, matching this converter case. Other motions with toggle keys are explicitly rejected by this diagnostic.

The standalone traced output is checked against the full unchanged `VMDAnimationClipConverter.Convert` and UMTDemo graph evaluation on all 91 frames. That control reports zero maximum position/rotation errors for all six cases. Runtime phase splitting is separately replay-checked against the untouched public solve, also with zero reported errors. Only after those controls are valid are intermediate snapshots used as evidence.

Two additional runtime experiments are evaluated against the same official output:

- **Same inputs:** inject the converter's sampled local position/rotation for nonphysical bones after runtime sampling/reset; keep runtime's ordinary reset policy.
- **Same inputs and history:** additionally retain complete previous state for physical bones, and other bones' history while replacing nonphysical input position/rotation and clearing their two validity flags, matching the bake's update pattern. The retained history comes from the runtime experiment's own previous output, not injected official physical output.

The managed input arrays retain all converter states for inspection, but injection reads only nonphysical input position/rotation. It never overwrites runtime dynamic bodies with converter-simulated positions/rotations. Both experiments use a newly reset runtime/native context. Diagnostic hooks are removed in finally blocks; standalone native allocations/contexts are disposed.

## Results

All rows use 30 Hz, seed 0, zero warmup, frames 0–90, the same synthetic center translation/torso yaw motion, and the existing 1 mm / 0.1-degree reference criteria.

| Case | Original runtime max position / rotation | Same inputs max position / rotation | Same inputs + history max position / rotation |
|---|---|---|---|
| Free body | 0.0000244894 m / 0 deg | 0 / 0 | 0 / 0 |
| Joint | 0.0000436578 m / 0.888227 deg | 0 / 0 | 0 / 0 |
| Joint + append | 0.0000436578 m / 0.888227 deg | 0 / 0 | 0 / 0 |
| Joint + IK | 0.0000436578 m / 0.888227 deg | 0 / 0 | 0 / 0 |
| Gene | 1.142695 m / 10.985384 deg | 1.125919580 m / 10.867586 deg | 0 / 0 |
| Sandrone | 0.003742395 m / 100.094276 deg | 0 / 0 | 0 / 0 |

For Gene, same-input-only still first diverges at frame 2. For Sandrone and all synthetic cases, same-input-only meets parity. Same-input-plus-history has no divergent frame in any tested case. The baseline `parityPassed=false` remains recorded for the five original failing cases; experimental success is not substituted for normal runtime behavior.

The original runtime and converter frame 0 sampled/solved poses match. At frame 1 the small input discrepancy is present before physics. At frame 2 the reset/retained physical state also differs before physics; original post-physics local discrepancies reach approximately 0.489514 m / 4.088418 degrees for Gene and 0.000630004 m / 4.580761 degrees for Sandrone. Raw snapshots include unsolved world matrices: a large world-matrix difference immediately after reset can describe invalid/stale cache state, not a visible bone error. Interpret it together with solved validity flags and subsequent phases.

## Reproduction and evidence

```powershell
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -ContextCompare -IncludeRealModels
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -SkipBuild -ContextCompare -IncludeRealModels
```

- Separate successful Mono Windows build: `builds/POC-06F-Mono/MmdStudioPoC.exe`.
- Final reports: `artifacts/physics-context/traced/<case>/report.json` and matching Player logs.
- Summary: `artifacts/physics-context/verification.json`, including separate baseline parity, standalone parity and both identical-input experiment results.
- Initial context-only run: `initial-context-trace/`; first identical-input run preserved in `first-identical-input-run/` before repeating.
- Original PMX fixtures/hash manifest remain outside Assets/Git under `artifacts/physics-context/fixtures/`.

The script rejects stale reports, execution failure, non-equivalent runtime tracing, standalone/converter mismatch, unexpected configuration/selection differences, and failure of the matched-input/history control. It preserves ordinary baseline mismatch as diagnostic data. No plugin/model code was copied into the package; no desktop capture was used.

Final repetition completed successfully: all six newly generated JSON reports were structurally identical to their preserved first identical-input runs, including phase snapshots, baseline errors and both experimental results. This is repeatability on the same PC/backend/build, not a cross-platform determinism guarantee.

## Consequence for implementation

Keep precise direct VMD interpolation and official-converter emulation as distinct policies. Do not silently reduce interpolation precision merely to force trajectory agreement. Pointwise physical reference comparisons must use identical sampled inputs, initialization, step sequence and solver-history policy.

Before enabling interactive physics, implement a supported runtime state-ownership/seek policy without relying on diagnostic reflection, then validate reset/preroll, loop, pause/resume and clip replacement. Test non-collinear IK/append fixtures that demonstrate actual feature excitation, then longer motions. Current results cover one controlled motion and this pinned package on Mono; independent MMD accuracy, full-motion visual correctness and IL2CPP parity remain open.
