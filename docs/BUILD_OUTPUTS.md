# Build output directories

2026-11-19. All build and launch scripts now use configuration directories beneath repository-root out/:

```text
EasyDanceStudio/
  out/
    debug/
      V0-Mono/MmdStudioPoC.exe        (current workspace UI)
      R0-02a-Mono/MmdStudioPoC.exe
      R0-02a-IL2CPP/MmdStudioPoC.exe
      MVP-10-Mono/MmdStudioPoC.exe
      MVP-10-IL2CPP/MmdStudioPoC.exe
      MVP-08-Mono/MmdStudioPoC.exe
      MVP-08-IL2CPP/MmdStudioPoC.exe
      MVP-07-Mono/MmdStudioPoC.exe
      MVP-07-IL2CPP/MmdStudioPoC.exe
      MVP-06-Mono/MmdStudioPoC.exe
      MVP-06-IL2CPP/MmdStudioPoC.exe
      MVP-05-Mono/MmdStudioPoC.exe
      MVP-05-IL2CPP/MmdStudioPoC.exe
      MVP-04-Mono/MmdStudioPoC.exe
      MVP-04-IL2CPP/MmdStudioPoC.exe
      MVP-03-Mono/MmdStudioPoC.exe
      MVP-03-IL2CPP/MmdStudioPoC.exe
      MVP-02-Mono/MmdStudioPoC.exe
      MVP-02-IL2CPP/MmdStudioPoC.exe
      POC-06C-Mono/...
    release/
      V0-IL2CPP/MmdStudioPoC.exe     (current release candidate)
      R0-02a-Mono/MmdStudioPoC.exe
      R0-02a-IL2CPP/MmdStudioPoC.exe
      MVP-10-Mono/MmdStudioPoC.exe
      MVP-10-IL2CPP/MmdStudioPoC.exe
      MVP-08-Mono/MmdStudioPoC.exe
      MVP-08-IL2CPP/MmdStudioPoC.exe   (first Release IL2CPP build)
      MVP-07-Mono/MmdStudioPoC.exe
      MVP-06-Mono/MmdStudioPoC.exe
      MVP-05-Mono/MmdStudioPoC.exe
      MVP-04-Mono/MmdStudioPoC.exe
      MVP-03-Mono/MmdStudioPoC.exe
      MVP-02-Mono/MmdStudioPoC.exe
```

Profile/backend subdirectories keep diagnostic and current-app outputs from overwriting each other. The complete out/ directory is Git-ignored. Existing build profiles were migrated from builds/, then the interim root debug/release layout, into out/debug and out/release after validating absolute paths and checking running builds. The former root directories were removed by moving them intact. Historical documents use the migrated paths; original diagnostic log/report contents were not rewritten.

## Configuration

Every build/verification/launch script accepts `-Configuration Debug` (default) or `-Configuration Release`, independently of `-Backend Mono|IL2CPP` where supported.

- Debug: Unity Development build, under out/debug/.
- Release: BuildOptions.None, under out/release/.
- New builds include build-info.json beside the EXE, recording configuration/backend/Development status. Migrated historical builds predate that manifest.
- Verification evidence remains under artifacts/. Release verification adds a release/ subdirectory to the corresponding artifact directory so it cannot overwrite Debug reports. Preview launchers likewise separate Release logs.

```powershell
# Build and verify the current workspace.
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Debug
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Release

# Launch either output.
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Configuration Debug
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Configuration Release
```

Use `-Backend IL2CPP` for the corresponding backend. Other scripts (POC, physics chain/features, legacy preview and Sandrone preview) use the same configuration option and root convention. Build configuration selection does not change supported product features or implicitly enable physics.

## Verification

Debug and Release MVP-02 Mono builds were created and verified before the final out/ migration. After moving them intact, each again passed 23 actual UI Player assertions using -SkipBuild from out/debug and out/release. Their build-info.json files report Development true/false respectively; the inspected Release application image has no Development Build watermark. Both outputs pass native x64/export/notice inventory. All PowerShell scripts parse successfully, out/ is confirmed ignored, and the old builds/, root debug/ and root release/ directories are absent. Existing IL2CPP Development outputs were migrated; a new IL2CPP Release build was not required or run for this path change.

MVP-03 (2026-11-19): Invoke-SceneVerification.ps1 -Ui and Start-SceneStudio.ps1 now use MVP-03-<backend>, leaving MVP-02 outputs intact. New Debug Mono, Debug IL2CPP and Release Mono builds pass 40 UI assertions each; see VIEWPORT_CONTROLS.md.

MVP-04 (2026-11-19): the UI verification and launcher now use MVP-04-<backend>. Debug Mono, Debug IL2CPP and Release Mono pass 50 UI assertions each; see STAGE_WORKFLOW.md. Note that PocBuild regenerates Assets/MMDStudio/Poc/PocSmoke.unity on every build with new file IDs; restore it from Git afterwards unless the scene content intentionally changed.

MVP-05 (2026-11-19): the UI verification and launcher now use MVP-05-<backend>; Debug Mono, Debug IL2CPP and Release Mono pass 60 UI assertions each (VIEWPORT_SELECTION.md).

MVP-06 (2026-11-19): the UI verification and launcher now use MVP-06-<backend>; Debug Mono, Debug IL2CPP and Release Mono pass 73 UI assertions each (GRAPHICS_CONTROLS.md).

MVP-07 (2026-11-19): the UI verification and launcher now use MVP-07-<backend>; the UI verification also relaunches the Player to check scene restore. Debug Mono, Debug IL2CPP and Release Mono pass 86 + 9 assertions each (SCENE_PERSISTENCE.md).

MVP-08/09 (2026-11-19): UI verification, the launcher and Invoke-ScenePerformance.ps1 use MVP-08-<backend>. All four Debug/Release × Mono/IL2CPP outputs were built and pass 86 UI + 9 restart assertions and the native inventory (PERFORMANCE_REPORT.md).

MVP-10 (2026-11-19): UI verification, the launcher and Invoke-ScenePerformance.ps1 use MVP-10-<backend>; all four outputs pass 95 UI + 9 restart assertions and the native inventory (TRANSFORM_GIZMO.md).

R0-02a (2026-11-19): UI verification, the launcher and Invoke-ScenePerformance.ps1 use R0-02a-<backend>; all four outputs pass 100 UI + 9 restart assertions (UNRESOLVED_REFERENCES.md).
