# Sandrone local test setup

The user supplied test/Sandrone_V2. These files and the prepared copy remain outside Assets/Git/Player output. No original PMX, texture or archive was changed.

## Which PMX to use

| File | Observed contents / status |
| --- | --- |
| 【桑多涅】/桑多涅_目齿轮可动.pmx | Original clothed Marionette character, movable eye-gear version. 66,599 vertices. Its 20 texture paths resolve relative to the PMX directory, but supplied textures were one level above it |
| 【桑多涅】/请柬.pmx | Invitation prop, 144 vertices; not the character |
| Sandrone copy/Base.pmx and Sandrone head.pmx | Apparent split/editing parts; all 5 / 16 texture references are missing at their current locations |
| Sandrone R18 15.07.2026.pmx | Different modified model with a TDA female-base header, 37,683 vertices. Two references missing: Toon/Sk.bmp and Texture/miku_sw.tga. Not used in the successful original-character test |

The archive 【桑多涅】.zip contains only the original character and invitation PMX files, not their textures. Re-extracting that archive alone will not repair the texture layout.

## Prepared local copy

test/Sandrone_Runtime contains a byte-identical copy of the original character PMX, tex/, spa/, skin.bmp, hair.bmp, toon_defo.bmp and the accompanying readme. Textures now resolve beside the PMX. SOURCE.json records the local source and file hashes; it is not a claim about the modified package's external provenance.

Open out/debug/POC-06C-Mono/MmdStudioPoC.exe, use PMX 가져오기 to select the PMX under Sandrone_Runtime, then VMD 연결 to select a character motion such as the existing Whiplash [SMOOTH].vmd. Camera VMD is optional and is selected separately.

For an automatic local preview, PowerShell 7 at repository root:

```powershell
.\scripts\Start-SandronePoc.ps1
```

Pass -Vmd with another character motion if desired. The general Start-MmdPoc.ps1 also accepts -Pmx now. Keep tex/ and spa/ beside the model when moving this prepared directory.

## Actual Windows Player result, 2026-11-19

- External original-character PMX + Whiplash direct mode exited 0, success=true.
- 66,599 vertices, 692 bones, 33 materials, 20/20 textures, 61 blendshapes, 5 IK controllers.
- Motion: 101 bone tracks, 32 morph bindings, 4 IK bindings; 184.667 sec. Pause, loop and repeated seek passed. This is limited diagnostic coverage, not a full visual signoff or generic model compatibility guarantee.
- GPU pose images were inspected. Existing unlit fallback renders the textured character; the original MMD toon/sphere shading is not reproduced exactly.
- Unsupported morph warning: Material 2, Bone 1, UV 1. Live physics/audio are off; hair/skirt physics and special eye behavior remain unverified. One unaddressed controller KeyB02_IK is disabled by the current experimental compatibility policy.
- Evidence: artifacts/sandrone-player.json/.log and sandrone-player-pose-0..5.png. The source hash manifest remains beside the local prepared copy.

The included readme attributes the original model to miHoYo and modification to 观海 (BiliBili: 观海子), permits certain fixes, and states noncommercial use, no redistribution and no adult works. The eye-gear model comment additionally warns about aliasing when viewed from far away. These are supplied-file statements; no external source or permission for the separately modified R18 file was established.

