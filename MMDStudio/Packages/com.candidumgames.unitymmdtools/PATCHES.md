# EasyDanceStudio local patches to UnityMMDTools

This package is embedded in EasyDanceStudio (ADR-017, 2026-11-20). It was copied unchanged from
`https://github.com/CandidumGames/UnityMMDTools.git` at commit
`db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4` (package version 0.5.1, MIT, see `LICENSE.md`).
Every local change is listed here and marked in code with `EasyDanceStudio patch #N`.
To take an upstream update, diff the new upstream tree against that base commit and re-apply
these patches by hand.

## #1 — Exception-safe native cleanup in VMD conversion (2026-11-20)

Upstream frees the solver/physics contexts, the index resolver and the per-stage native buffers only
on the success path, so an exception during conversion leaked them.

- `Runtime/VMD/VMDAnimationClipConverter.cs`: `Convert` and `ConvertAsync` release the transform
  solver context, physics context and `IndexResolver` in `finally`.
- `Runtime/VMD/VMDAnimationClipConverter.NonBaked.cs` (`AddBoneCurves`),
  `IKCurves.cs` (`AddIKCurves`), `Morph.cs` (`AddMorphCurves`): buffers are declared up front and
  released in `finally`.
- `Runtime/VMD/VMDAnimationClipConverter.Shared.cs`: `DisposeIfCreated` helpers.

Not yet covered: the baked path (`VMDAnimationClipConverter.Baked.cs`, `bakeIKToFK = true`, used
only by the diagnostic `baked-reference` mode), `AnimationMath.ResolveBakedCurveBones`, and camera
conversion. Behaviour on the success path is unchanged.

Verification (2026-11-20, Release IL2CPP, Gene + full-length Whiplash VMD):
`scripts/bash/run-pmx-staging.sh` alternates rounds of 3 successful conversions and 3 conversions that
throw from inside UMT (after the morph buffers exist), and compares the steady-state growth of Unity
allocated memory (`conversionLeakBytesPerFailure`, limit 64 KiB).

| Build | Leak per failed conversion | Successful conversion |
|---|---|---|
| Original upstream files (negative control) | **16,727,454 bytes** | 13,178 bytes |
| Patch #1 | **407 bytes** | -133 bytes |

A short 3-second fixture VMD could not detect the leak (buffers of a few KB vanish in allocator noise),
so the check uses the full-length motion.
