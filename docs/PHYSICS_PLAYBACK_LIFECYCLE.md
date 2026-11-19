# POC-06G — Stateful physics playback service

2026-11-19. Opt-in runtime service and actual Windows Player lifecycle tests. Normal UI remains physics OFF and existing launchers continue to use POC-06C.

## Implementation

`Assets/MMDStudio/UMTExtensions/UMTExtensions.asmref` compiles the project-owned `MmdStatefulPhysicsSolver` into pinned UMT.Runtime (assembly GUID `771f49a373744d64a9b8aca7b36272be`). The adapter uses typed internal context/solve APIs without reflection, vendor-copying the package, or changing Library/PackageCache. It deliberately depends on UMT revision `db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4`; internal API changes require revalidation. This is a project integration, not an upstream public API guarantee.

Reset rebuilds manager/native caches, resets native physics, and initializes solver states from bind-pose configuration. For each step the adapter updates nonphysical input positions/rotations and their validity flags, retaining physical bone state and other solver history. It synchronizes IK toggles, runs the existing pre-physics/Bullet/post-physics solve once, then flushes bone and rigid-body visualization transforms. It allocates no native contexts of its own; the imported model manager retains native-resource ownership. Precise direct VMD interpolation remains unchanged.

`MmdPhysicsPlayback` owns integer 60 Hz ticks and is separate from the normal UI clock:

- Seek clamps to the motion duration and floors the requested time to a 60 Hz tick. It resets/reseeds at time zero, then prerolls every tick. This costs time proportional to the target time; no checkpoint cache or asynchronous cancellation is implemented.
- Pause preserves fractional pending time and ignores elapsed time supplied while paused. Stop pauses and reconstructs tick zero.
- Loops reset/reseed rather than retaining previous-cycle velocity. For the tested three-second clip, playback uses ticks 0–179 and wraps to zero at tick 180. Nonloop playback reaches tick 180, then pauses on further advancement. Non-grid durations use the implementation's explicit floor/ceil tick boundaries and require broader validation.
- A call processes at most 240 ticks. Excess elapsed time remains pending for subsequent calls, including an Advance(0) drain. This service does not promise that heavy models can maintain real-time playback.
- One physics session owns the motion. A second session, ordinary external pose sampling, or a competing ordinary playback clock is rejected. Motion Load changes the clip revision and retires old sessions. Dispose releases ownership and prevents further advancement.
- The service is currently exercised through diagnostic CLI probes, not exposed by a physics button. Existing diagnostic fixed-step/reference paths remain available separately.

## Validation

The lifecycle probe generates an original three-second VMD outside Assets/Git and loads each external PMX. A retained-state reflection diagnostic generates the baseline at 60 Hz using the **same precise direct inputs**. All 181 poses and all bones are compared with the new typed adapter; positions use a 0.00001 m limit and rotations a 0.05-degree limit. Further lifecycle comparisons increase the total to 192 full-bone poses per case. This checks adapter/lifecycle behavior, not independent MMD physical fidelity. The earlier official-converter matched-input controls remain documented separately in PHYSICS_CONTEXT_DIAGNOSIS.md.

Twenty assertions per case cover adapter replay, one solve per tick plus the zero-time seed, pause, duplicate-owner and external overwrite rejection, split elapsed-time chunks, pause with a pending half-tick, fractional/backward/repeated seeks, invalid seek, stop, two loop resets, stall budget/pending-time drain, natural nonloop end, competing clock rejection, clip replacement with a changed motion, and disposed-session rejection.

Mono and IL2CPP final results: all six cases pass in each backend (free body, joint, joint+append, joint+IK, Gene and Sandrone). Each backend passes 120 assertions and 179,712 bone-pose comparisons, with maximum reported position/rotation differences of zero against its own retained-state diagnostic reference. Per-case comparisons: 960 for each five-bone fixture, 43,008 for Gene, 132,864 for Sandrone. No equality of Mono trajectories with IL2CPP trajectories is claimed. These synthetic append/IK cases retain the excitation limitations described in PHYSICS_CHAIN_DIAGNOSIS.md.

Additional regression on the new Mono Player: external import/recovery/resource replacement passes 13 assertions; expression/controlled foot IK passes 21 assertions. Those tests exercise normal physics-OFF behavior. Model retirement during a running physics solve, longer motions, arbitrary IK/append excitation, lifecycle after mid-step exceptions, and visual/collision accuracy are not covered by this ticket.

Both output inventories pass x64 architecture, required native create/destroy/step exports (13 exports total), and six copied notices. Test fixtures are confirmed Git-ignored. These are same-PC checks, not a clean-machine deployment audit.

## Reproduction

```powershell
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -Lifecycle -IncludeRealModels -Backend Mono
pwsh -NoProfile -File scripts/Invoke-PhysicsChainVerification.ps1 -Lifecycle -IncludeRealModels -Backend IL2CPP
```

Add `-SkipBuild` to repeat the corresponding Player. Outputs are isolated under `builds/POC-06G-<backend>` and `artifacts/physics-lifecycle/<backend>`. The script rejects stale reports, wrong backends, process/report failure and fewer than 20 assertions. Per-model reports/logs are under `traced/<case>`; the summary is `verification.json`. Fixtures remain outside Unity Assets/Git. No desktop capture or local LLM was used.

## Next gate

Validate stronger non-collinear IK/append inputs and the stateful service on longer real motions, with explicit visual/collision review. Add scalable seek cancellation/checkpoints and model retirement/exception handling before wiring this service into the production UI. Preserve a distinct official-input emulation mode for reference comparisons rather than weakening the direct interpolator's precision.
