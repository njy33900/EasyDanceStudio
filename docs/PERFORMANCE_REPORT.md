# Integration and performance report — first phase (MVP-09)

2026-11-19. Spec 8.2 requires "sample-scene performance report and known-issues list" (issues: KNOWN_ISSUES.md); NFR-03 asks for 1080p FPS, memory and load times on a reference PC with a fixed sample scene, NFR-04 sets a 30 FPS MVP target for two characters, one background and basic effects.

## Method

`scripts/Invoke-ScenePerformance.ps1 -Configuration Release|Debug -Backend Mono|IL2CPP` (`-pocScenePerfReport`) runs the actual Player in a 1920×1080 window and, through the normal workspace code paths:

1. imports Gene and Sandrone, connects the full-length Whiplash VMD (184.7 s) to both, imports the generated stage;
2. applies basic effects (Bloom on, Neutral tonemapping, medium soft shadows), saves a `.mmdscene` and reopens it;
3. plays on the shared clock with loop from 30 s and measures three passes with vsync off and no frame cap: effects + medium shadows (3 s warm-up, 20 s), effects off + shadows off (2 s, 15 s), effects + high shadows (2 s, 15 s).

Each pass records frames, average FPS, median/p95/p99/worst frame time, 1 % low FPS and gen-0 GC count; the run records import/save/open times and Unity allocated/reserved/Mono heap memory. Physics is OFF as in the product. Reports: `artifacts/performance/<backend>-<configuration>/performance.json` (+ `.log`, `perf-scene.mmdscene`).

## Reference PC

AMD Ryzen 7 9800X3D (16 threads), NVIDIA GeForce RTX 4090, Direct3D 12, Windows 11, Unity 6000.3.28f1 / URP 17.3.0. This is a high-end machine; a mid-range "reference PC" in the NFR-03 sense has not been measured.

## Results (1920×1080, vsync off)

Release rows were re-measured on the MVP-10 builds (with the gizmo); Debug rows are from MVP-08.

Average FPS / 1 % low FPS / p99 ms / worst ms:

| Build | Effects + medium shadows | Effects off | Effects + high shadows |
|---|---|---|---|
| **Release Mono** | 568 / 235 / 3.3 / 6.1 | 573 / 263 / 3.5 / 5.9 | 571 / 268 / 3.1 / 6.1 |
| **Release IL2CPP** | 571 / 222 / 3.8 / 6.1 | 646 / 224 / 3.8 / 6.2 | 570 / 234 / 3.8 / 6.1 |
| Debug Mono | 562 / 210 / 3.7 / 16.2 | 569 / 256 / 3.4 / 5.5 | 568 / 261 / 3.2 / 5.5 |
| Debug IL2CPP | 567 / 206 / 3.7 / 43.8 | 593 / 250 / 3.8 / 5.8 | 574 / 269 / 3.6 / 5.8 |

All passes on all builds meet the **30 FPS target** by a wide margin (lowest 1 % low: 206 FPS). Medians are 1.2–1.5 ms. The MVP-10 Release runs have no frame above 6.2 ms; earlier MVP-08 runs showed isolated single frames up to 44 ms (Debug) with no pattern by build or setting; gen-0 collections are 1–3 per pass.

One Release Mono run was disturbed by interaction with the measurement window (a 1.6 s stall and a different distribution); it was excluded and repeated undisturbed. The table shows the repeat.

### Load, save and memory

| Build | Gene import | Sandrone import | Both VMD | Stage | Save | Open scene | Allocated / reserved / Mono heap |
|---|---|---|---|---|---|---|---|
| Release Mono | 0.75 s | 1.10 s | 0.94 s | 0.02 s | 0.010 s | 2.32 s | 432 / 654 / 254 MB |
| Release IL2CPP | 0.57 s | 1.02 s | 0.48 s | 0.02 s | 0.007 s | 1.79 s | 470 / 686 / 265 MB |

Imports are synchronous (see KNOWN_ISSUES.md). The process working set is not reported by Unity's runtime (`Environment.WorkingSet` returns 0) and is omitted.

## Stability checks run with this report

- **Shutdown crash recurrence:** the legacy morph/IK deformation probe that once ended with access violation -1073741819 was run 10 times on Debug Mono and 10 times on Release Mono (MVP-08 builds): 20/20 exit 0 with passing reports. The original crash has not recurred; its cause remains unknown (`artifacts/shutdown/`).
- **Native inventory:** all four MVP-08 and MVP-10 outputs pass x64 / required exports / notices (`artifacts/native/`). Only `UMTNativePlugin.dll` needs the VC++ 2015–2022 runtime, which the build does not ship (KNOWN_ISSUES.md, clean-machine item).
- **Functional regression:** every MVP-10 build passes 95 UI and 9 restart-restore assertions.

## Conclusion

On the development PC the reference scene runs about 19–21× above the 30 FPS target in Release builds, with load times of 1–2.4 s. The remaining performance risk is lower-spec hardware, which needs one measurement on a mid-range PC (same script, Release build) before release.
