# Windows backend and fixed-step physics probe

POC-06C is a controlled Windows x64 experiment. The normal workspace continues to use physics-OFF sampling. There is no user-facing live-physics switch yet.

## Build and runtime separation

PocBuild accepts -pocBackend mono or il2cpp. The build temporarily selects the requested backend, then restores the previous project setting in a finally block. Invoke-PocVerification.ps1 defaults to Mono; -Backend IL2CPP selects the separate native build. Smoke reports record the compiled scriptingBackend rather than infer it from a folder name.

| Backend | Player directory | Evidence directory |
| --- | --- | --- |
| Mono | builds/POC-06C-Mono | artifacts |
| IL2CPP | builds/POC-06C-IL2CPP | artifacts/il2cpp |

The POC-06B binaries remain intact. Start-MmdPoc.ps1 and Start-SandronePoc.ps1 accept -Backend Mono or IL2CPP. Normal interactive playback remains physics OFF for either backend.

An actual IL2CPP run exposed an optional diagnostic incompatibility: Process.PeakWorkingSet64 throws NotSupportedException under IL2CPP and had prevented VMD loading. This measurement is now explicitly unavailable on IL2CPP. Mono's zero result is also marked unavailable; neither is a valid peak-memory benchmark. Conversion timing and Unity allocation snapshots remain separate measurements.

## Physics experiment and reset policy

PocPhysicsProbe calls the pinned UMT public SolveWithPhysics API through an explicit MmdPocMotion.SampleWithPhysics entry point. Ordinary Sample/Advance retain their previous physics-OFF behavior. Automatic manager transformation and motion advancement are disabled during the probe.

Each trial advances Whiplash from frame 0 for 360 fixed steps at 1/60 sec; the pinned manager internally uses 1/120-sec Bullet substeps. Positions must stay finite and within the coarse 100 m probe bound, and rotations must stay finite. This detects crashes/divergence, not visual accuracy. A second run compares reset/replay poses; two 120-step prerolls separated by another trial compare seeking after reset. Both require <=0.001 m maximum position difference. Pause must not change the pose, renders must pass existing nonempty/missing-shader checks, and retiring the model must release its owner without native failures.

The initial Gene trial using manager.ResetPhysics() reported 0.237479 m reset/replay difference. That method retains the existing native context. The corrected experiment calls the existing ResetToBindPose(), which rebuilds solver caches and recreates physics, before sampling frame 0 and prerolling. This uses public pinned-package APIs; no reference/package source was patched. Original failure evidence is preserved in artifacts/il2cpp/poc-physics-gene-reset-only.json.

This establishes a reproducible controlled reset/preroll strategy, not instant random-access live physics. The strategy is not yet exposed in interactive playback and its cost over long motions has not been profiled. Full-duration review, collision/angle-limit reference parity, multiple-model interaction and different hardware remain open.

## Local native inventory

```powershell
python scripts/inspect_player_native.py --player builds/POC-06C-IL2CPP --report artifacts/il2cpp/native-inventory.json
```

This read-only PE parser checks x64 executable/DLL architecture, hashes, required Bullet create/destroy/step exports and presence of the copied notices. It never loads a DLL. Runtime PMX import builds the native rigid-body context; the physics probe additionally steps it and retires it.

UMTNativePlugin.dll imports VCRUNTIME140.dll, VCRUNTIME140_1.dll and Windows Universal CRT API sets. The current PC has Visual Studio/runtime dependencies installed; a clean Windows installation was not tested. Copied package/UMTDemo notices cover their declared components, including Bullet and IPADIC. This is a local inventory, not complete release/distribution approval. Test PMX/VMD assets are not bundled.

See TASKS.md for measured results and TEST_PLAN.md for reproduction commands.

POC-06D extends this to full-duration Mono trials and an actual official-converter/demo comparison. The controlled reference agreement fails beginning at frame 2 despite reproducible trials. It does not replace the limited POC-06C evidence with a general physics acceptance. See PHYSICS_ENDURANCE.md; interactive physics remains OFF.
