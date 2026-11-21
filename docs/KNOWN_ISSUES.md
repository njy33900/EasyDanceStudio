# Known issues and limits — first phase (v1.0 MVP candidate)

2026-11-19. Spec 8.2 requires a known-issues list next to the performance report (PERFORMANCE_REPORT.md). Items are grouped by impact. "Verified" means checked by the automated Player probes on Debug/Release × Mono/IL2CPP unless stated.

## Not yet checked by a person

1. **Physical mouse and keyboard input.** All viewport, gizmo, shortcut and dialog checks feed synthetic per-frame input into the app's own handler; real device input through the Input System was not exercised automatically.
   V0 adds H/Esc focus view and a View menu; these also need actual input confirmation. The current development PC reports 150% system DPI and passed 1280×720/1600×900 internal layout/picking checks; 100% DPI and moving the window between monitors remain unchecked.
2. **Native file dialogs.** Open/save dialogs are exercised through their result handlers; clicking through the Windows dialogs has not been re-checked since MVP-02.
3. **Real third-party stage PMX.** Only the generated synthetic stage was tested (2 textures, 100 vertices). Large meshes, many/missing textures, transparency and stage-specific materials are unverified.
4. **Clean Windows machine.** Every run used the development PC. The native inventory (2026-11-19, all four MVP-08 outputs pass x64/exports/notices) shows that only `UMTNativePlugin.dll` needs the Visual C++ 2015–2022 runtime (`VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, UCRT); it is **not shipped** with the build. On a machine without that redistributable the plugin may fail to load. With physics OFF it may never be loaded, but that is unconfirmed. Release packaging should bundle the redistributable (or app-local runtime DLLs under Microsoft's redistribution terms) and be checked on a clean VM.
5. **Other models/motions.** Compatibility is shown for Gene, Sandrone and Whiplash plus synthetic fixtures only.

## Functional limits (by decision or not yet implemented)

- **Physics is OFF** in the workspace (ADR-014). Hair/cloth do not simulate; the status bar shows "물리 OFF".
- The **transform gizmo** (MVP-10) works in world axes only: no local-space mode, plane handles or snapping.
- Undo/redo covers edits, add/remove and VMD/WAV connection (H1-03, 200 commands); file loading itself is not recorded.
- **Material and UV morphs** are not played; they are listed as warnings in the inspector's 재질 section.
- **Light colour/intensity affect only the toon and PBR material styles** (H2b-01). The "원본" style keeps the imported URP Unlit fallback materials (lilToon is not in the project), which ignore the light. Built-in look presets switch characters to toon.
- **Toon style limits** (H2b-01): sphere maps (matcap) are not shown; only the main directional light is used; MMD's shared toon01-10 ramps are not shipped with models, so those materials use a default shadow colour. Visual defaults await the user's reference images (decision 8).
- **Camera VMD** (H2a-04): no model tracking (a PMM feature, not in VMD) and no orthographic projection (shown in perspective, reported). Manual navigation releases the motion; "카메라 모션 따라가기" resumes it.
- **Audio (H2a-03, 2026-11-21): WAV only** (PCM 8/16/24/32-bit, float 32/64-bit; compressed WAV, OGG and MP3 are rejected with a message). At speeds other than 1x the pitch changes with the speed (decision 11; Unity has no built-in pitch-preserving stretch) and the playback bar shows "음높이 변경". Preview sync: the scene clock is the master and the audio is re-positioned only past 80 ms drift; measured steady drift is within one audio buffer (max 34 ms, mean +13 ms reported read-ahead, 0 resyncs, Release IL2CPP, 44.1 kHz). The audible output latency of the device is not compensated in preview; offline output (H3) syncs by samples.
- **No settings screen** (P1), **no drag and drop** (dropped by decision). Recent scenes exist since H1-07.
- UI PMX/VMD imports now show stages and accept a cancel request between safe boundaries. Texture, material and rename stages are now split (H1-01b): the Release IL2CPP Sandrone PMX import takes 0.795 s with a 55.8 ms worst frame (was 1.043 s / 489.6 ms). Remaining single-frame costs: one large texture decode (~50 ms) and one rename category (up to ~94 ms for Gene). The `.mmdscene` open path is now a cancellable transaction that keeps the current scene until the new one is fully prepared (H1-01c; Release IL2CPP 1.98 s / 66 ms worst frame, Release Mono 2.04 s / 68 ms, Debug Mono 2.74 s / 128 ms). H1-01 is only partly implemented; see `H1_LOADING_STAGE1.md`.
- If pinned UMT fails inside Unity object creation, its async builder does not return partial resources, so full mesh/material/texture cleanup is not yet proven. Malformed-input and repeated cancel/retry resource tests remain.
- Missing files in a scene are kept as unresolved entries (R0-02a) and survive saving; **re-linking** them to a new path is not implemented yet (H1-02).
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

## UMT embedded (2026-11-20)

- UnityMMDTools is embedded and patched (ADR-017). Upstream updates must be merged by hand using `PATCHES.md`. The repository grows by about 58 MB (Kawazu dictionary `sys.bytes` is 47 MB).
- VMD conversion still leaks native buffers on exceptions in the baked path (`bakeIKToFK`, diagnostic only) and in camera conversion; the normal sparse path is patched.
