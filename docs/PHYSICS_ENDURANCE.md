# Full-duration physics and official-reference findings

POC-06D keeps normal interactive playback physics OFF. It adds diagnostics, not a live-physics release feature. The stable launchers still select POC-06C.

## Reproduction

```powershell
.\scripts\Invoke-PocVerification.ps1 -Backend Mono -PhysicsReference
.\scripts\Invoke-PocVerification.ps1 -Backend Mono -PhysicsEndurance -SkipBuild
```

Both use builds/POC-06D-Mono/MmdStudioPoC.exe. Reference evidence is under artifacts/physics-reference/Mono; endurance evidence is under artifacts/physics-endurance/Mono. Omit -SkipBuild when sources change. Tests use external Gene and the prepared Sandrone_Runtime; no original source assets are changed or bundled. IL2CPP full-duration coverage is not claimed by this ticket.

## What the endurance result means

Each model runs the complete 184.667-second Whiplash twice at 1/60-sec steps, using full-context ResetToBindPose before each trial. There are 11,080 step transitions / 11,081 sampled poses per trial. Every bone's position and rotation must remain finite; positions must remain within a coarse 100 m bound. The second trial compares every sampled pose with the first, with a maximum 0.001 m tolerance. This is within-backend reproducibility, not proof of correct MMD physics.

Nine GPU checkpoints per model cover 0,15,30,60,90,120,150,180 and the final time. The probe yields before capture so SDEF reconciliation can run. The report includes maximum world-position magnitude, largest one-step displacement, and the largest displacement among bones directly weighted by PMX vertices. Directly unweighted helpers can still affect constraints/collisions; this distinction does not prove their behavior harmless.

The initial checkpoint-only version completed both trials for both models, with zero checkpoint replay differences. Gene's initial wall time was 20.56 sec and Sandrone's 254.96 sec on this PC. These are single diagnostic runs including image capture; no performance benchmark or UI latency claim is made. A second run strengthens replay comparison to all 11,081 poses and adds direct-weight statistics. See TASKS.md for final measurements.

The final strengthened run completed successfully for both models: two trials and 11,081 replay-pose comparisons each, maximum difference 0. Gene measured 15.874 sec simulation / 17.542 sec wall; Sandrone 250.713 / 253.260 sec. Maximum world magnitudes were 9.531709 / 1.910295 m. Sandrone's maximum single-step displacement was 0.221922 m on a directly unweighted helper; its directly weighted maximum was 0.176418 m. These are measurements of this diagnostic, not accuracy/performance acceptance.

Gene's largest one-step displacement is about 13.93 m on 前クローク2_2錘, which is not directly assigned vertex weight. The largest directly weighted-bone displacement is about 0.2076 m on 右中指３. The coarse finite/bounded test alone must not be presented as full physical stability or visual correctness. Further constraint/reference review is required.

## Actual official-reference comparison

PocPhysicsReferenceProbe writes a small controlled VMD with center translation and upper-body rotation over three seconds. It compares the current runtime direct-default-ik path with pinned UMT Convert(bakeIKToFK=true,bakePhysicsToFK=true), evaluated by the actual unmodified UMTDemo VMDModelPlayableBehaviour. Both use 30 Hz, seed 0 and zero warmup; there are no morph tracks or IK toggles. It compares physics-controlled bones' local positions/rotations over 91 poses, rather than treating nonphysical channels omitted by the reference bake as an oracle.

| Model | Bone-pose comparisons | Max local position difference | Max local rotation difference | First threshold failure |
| --- | ---: | ---: | ---: | ---: |
| Gene | 5,642 | 1.142695 m | 10.985384 degrees | frame 2 |
| Sandrone | 48,503 | 0.003742 m | 100.094276 degrees | frame 2 |

Frame 0 matches exactly; frame 1 maximum position differences are about 0.000011876 m / 0.000007260 m, with zero reported rotation differences. At frame 2 both exceed the declared 0.001 m / 0.1-degree agreement criterion. The reports include per-frame maxima and worst-bone names. Reference `success=true` means the comparison executed; `parityPassed=false` is the actual agreement result. Script execution success must not be read as parity acceptance.

This reference shares UMT's underlying solver and is not a second independent engine. Even agreement would not establish whole-engine MMD accuracy. Here agreement fails; therefore interactive physics remains disabled.

## Source review and bounded investigation

- Pinned UMT MMDTransformManager.TransformBonesWithPhysics explicitly performs pre-physics bone processing, physics, then after-physics bone processing. The adapter uses this public route once per diagnostic step.
- Cached pinned Saba VMDAnimation.SyncPhysics applies animation/morphs, pre-physics nodes, 1/30-sec physics, then post-physics nodes. It transitions into a target pose over several frames; this is distinct from replaying a complete motion from zero.
- Cached Babylon runtime separates beforePhysics and afterPhysics. These are source-order observations; neither external engine was executed in this ticket.
- UMT's baked path can warm up physics (default five seconds); the controlled comparison explicitly sets zero to match the runtime test's starting policy. Its baked sampler skips assigning new FK to physics-controlled bones, while the public runtime route also resamples/reset-caches each solve.
- An adapter experiment preserving the previous physical local pose and skipping FK writes did not reduce the controlled comparison maxima. It was reverted. The cause is not established; no package source was modified and no fix is claimed. Original comparison evidence is preserved as before-preserve-*.json.

Next investigation should isolate the frame-1-to-2 solver/cache/constraint state on a minimal rigid-body chain and distinguish directly weighted output from helper/contact effects. Full-duration repeatability alone is insufficient to expose physics controls in the UI.
