# POC-06H — feature excitation and real-motion stateful playback

2026-11-19; follow-up branch `codex/physics-validation` from the pushed v0.1.0-poc baseline. Normal UI remains physics OFF.

## Feature controls

The original four vertical-chain PMX fixtures are unchanged byte-for-byte. Two additional original six-bone fixtures use a non-collinear chain, an independently placed IK goal, a kinetic driver and one connected dynamic body. Generated PMX/VMD files remain outside Unity Assets and Git.

For each fixture the actual Windows Player runs the typed stateful service to one second with the relevant feature enabled and disabled. Append is toggled through constraint solving; IK through IK solving. The driver is attached only to a kinetic body, so a dynamic-body result cannot overwrite its final pose. Both cases must change the driver rotation by at least one degree and the dynamic bone's world position by at least 0.0001 m. A third reset/preroll trial re-enables the feature and must reproduce the first full-bone pose within 0.00001 m.

| Backend | Feature | Driver rotation difference | Dynamic bone position difference | Reset/replay position error |
|---|---|---:|---:|---:|
| Mono | append | 2.864896 degrees | 0.001241411 m | 0 |
| Mono | IK | 29.469551 degrees | 0.111447200 m | 0 |
| IL2CPP | append | 2.864896 degrees | 0.001241411 m | 0 |
| IL2CPP | IK | 29.469551 degrees | 0.111447193 m | 0 |

All four cases pass three assertions each. These controls establish meaningful feature excitation and its effect on the physical chain, addressing the previous vertical-chain stimulus limitation. They do not establish arbitrary IK target accuracy, angle-limit correctness, append order compatibility on every model, or cross-backend trajectory equality.

## Real-motion replay

The additional real-motion mode loads the supplied external Whiplash VMD into the typed stateful service, preserving precise direct input interpolation. It runs every 60 Hz tick twice with fresh reset/session ownership, storing and comparing every bone's world position and local rotation. Positions must reproduce within 0.00001 m and rotations within 0.05 degrees. Every position/rotation must be finite; the coarse position magnitude bound is 100 m.

This differs from POC-06D: it exercises the new stateful playback service and retained history rather than the old public solve/reset-per-step path. Reproducibility is a distinct claim from visual/physical accuracy; no continuous visual or collision signoff is implied. Per-trial progress is logged every 600 ticks.

Gene and Sandrone Mono each completed both full 184.667-second trials: 11,080 steps plus initial seed per trial, all 11,081 repeated poses compared, maximum position replay error 0. Maximum world-position magnitudes were 8.383716 m (Gene) and 1.910295 m (Sandrone). Rotation replay satisfies the 0.05-degree limit. This totals 22,162 sampled full-bone poses per model; IL2CPP real-motion endurance is not covered by this ticket.

## Reproduction

```powershell
# Build and verify the two feature fixtures on each backend.
pwsh -NoProfile -File scripts/Invoke-PhysicsFeatureVerification.ps1 -Backend Mono
pwsh -NoProfile -File scripts/Invoke-PhysicsFeatureVerification.ps1 -Backend IL2CPP

# Reuse the Mono build and additionally replay both real models twice in full.
pwsh -NoProfile -File scripts/Invoke-PhysicsFeatureVerification.ps1 -Backend Mono -SkipBuild -RealMotion -Seconds full
```

Default `-Seconds 30` bounds the real-motion test duration; `full` selects the loaded motion's complete duration. Outputs are isolated under `out/debug/POC-06H-<backend>` and `artifacts/physics-features/<backend>`. Each case has a report and matching Player log; verification.json is updated after each passed case. Both builds succeeded and pass native x64/export/notice inventory.

## Scope after this ticket

The bounded numerical physics investigation is sufficient to proceed with the next scene/shared-clock ticket, described in MVP_SCOPE.md. Keep physics OFF in that first multi-character ticket. Cancellable/scalable seek, running-model retirement, exception recovery, longer IL2CPP coverage and visual/collision review remain separate prerequisites to user-facing physics integration.
