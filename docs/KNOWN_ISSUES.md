# Known issues and limits — first phase (v1.0 MVP candidate)

2026-11-19. Spec 8.2 requires a known-issues list next to the performance report (PERFORMANCE_REPORT.md). Items are grouped by impact. "Verified" means checked by the automated Player probes on Debug/Release × Mono/IL2CPP unless stated.

## Not yet checked by a person

1. **Physical mouse and keyboard input.** All viewport, gizmo, shortcut and dialog checks feed synthetic per-frame input into the app's own handler; real device input through the Input System was not exercised automatically.
2. **Native file dialogs.** Open/save dialogs are exercised through their result handlers; clicking through the Windows dialogs has not been re-checked since MVP-02.
3. **Real third-party stage PMX.** Only the generated synthetic stage was tested (2 textures, 100 vertices). Large meshes, many/missing textures, transparency and stage-specific materials are unverified.
4. **Clean Windows machine.** Every run used the development PC. The native inventory (2026-11-19, all four MVP-08 outputs pass x64/exports/notices) shows that only `UMTNativePlugin.dll` needs the Visual C++ 2015–2022 runtime (`VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, UCRT); it is **not shipped** with the build. On a machine without that redistributable the plugin may fail to load. With physics OFF it may never be loaded, but that is unconfirmed. Release packaging should bundle the redistributable (or app-local runtime DLLs under Microsoft's redistribution terms) and be checked on a clean VM.
5. **Other models/motions.** Compatibility is shown for Gene, Sandrone and Whiplash plus synthetic fixtures only.

## Functional limits (by decision or not yet implemented)

- **Physics is OFF** in the workspace (ADR-014). Hair/cloth do not simulate; the status bar shows "물리 OFF".
- The **transform gizmo** (MVP-10) works in world axes only: no local-space mode, plane handles or snapping.
- **No undo/redo** (third phase); deletion asks for confirmation instead.
- **Material and UV morphs** are not played; they are listed as warnings in the inspector's 재질 section.
- **Camera VMD** is not connected to the shared clock (legacy preview only, P1).
- **No audio** (P1), **no recent files or settings screen** (P1), **no drag and drop** (dropped by decision).
- Loading is **synchronous**: the window shows a loading message but does not respond during a PMX/VMD import (about 0.6–1.1 s each for the test models; scene open about 1.8–2.4 s).
- Missing files in a scene are skipped and listed; **re-linking** them requires re-adding and saving.
- Clicking a dense model bakes its mesh for picking; per-click cost on very dense stages is unmeasured.
- Strings are in code (Korean); localisation resources are not separated yet.

## Stability

- **Shutdown access violation (-1073741819), seen once** at the end of a legacy morph/IK regression run on 2026-11-19 while other builds were running; that run's checks had already passed. Repeating the same probe 20 times (10 Debug Mono, 10 Release Mono, MVP-08 builds) gave 20/20 clean exits. The cause is not established and no fix is claimed; keep watching in release testing.
- The performance measurements were taken on a high-end PC only (RTX 4090 / Ryzen 7 9800X3D); a mid-range PC has not been measured.
- Occasional single-frame hitches (up to about 45 ms, one case of 1.6 s when the window was disturbed during measurement) appear in the performance passes; averages and 1 % lows stay far above target on the test PC.

## Rendering

- The UI is designed for 1280×720 and above with ScaleWithScreenSize; very small windows are not handled specially.
- Neutral tonemapping darkens the light-grey background; this is expected tonemapping behaviour.
- Bloom only shows on pixels brighter than the 0.9 threshold.

## Process

- PowerShell 7 was not installed in the Claude session, so verification used an equivalent bash runner; the `.ps1` scripts were parse-checked with Windows PowerShell, not executed.
- The legacy native inventory/notice check and the older POC regression suites were not rerun after MVP-03.
