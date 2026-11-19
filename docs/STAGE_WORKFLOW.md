# MVP-04 — PMX background (stage) workflow

2026-11-19. Covers specification MMD-105 (PMX background, position/rotation/scale) on top of the MVP-03 workspace. GLB backgrounds stay out of scope until a PoC; physics stays OFF.

## Use

Click **배경 추가** in the toolbar and choose a PMX. The stage is added at the origin, selected and framed with everything visible. The left explorer groups items under **캐릭터 (n)** and **배경 (n)**; hidden items show "(숨김)". With a stage selected the inspector title reads **선택한 배경**, the info line shows "배경 · 모션 없음(고정)", **선택 캐릭터 VMD 연결** is disabled, and position/rotation/scale, visibility, framing, highlight and **선택 항목 삭제** work as for characters.

## Service rules

- `MmdSceneService.ObjectKind { Character, Stage }` on each `SceneObject`; `ImportStage(pmx, position)` uses the same external PMX import, owner and failure cleanup as characters.
- A stage never has a motion. `ImportStage` cannot take a VMD and `ReplaceMotion` on a stage throws without changing anything. The UI rejects a VMD pick while a stage is selected and keeps the scene.
- Stages do not contribute to the clock duration and are not sampled. Their bone/transform pose stays unchanged while characters play.
- Characters are placed at 1.5 m steps by character count only, so adding a stage does not shift the next character.
- Removing a stage releases only its owner; character motions and clock duration are unchanged.

Stages are imported with the same loader as characters, so the existing limits apply (synchronous import, unsupported material/UV morphs reported as warnings). Large stages are not optimized; no static batching, LOD or culling changes were made.

## Status message fix

A failed import or rejected VMD previously left "불러오지 못했습니다…" in the normal status message, so it remained visible after the error was cleared by a later successful action. The failure text now lives only in the error (prefixed "불러오지 못했습니다:"), the previous message is restored, and selecting a list item shows "캐릭터 선택: …" or "배경 선택: …".

## Test fixture

No stage PMX is among the local test assets, so `scripts/New-StageFixture.py` generates an original one (no third-party data) outside Assets/Git: a 120×120-unit floor and four box pillars, two double-sided materials with generated PNG textures referenced by relative path (`tex/floor.png`, `tex/pillar.png`), one root bone, no rigid bodies. The UI verification writes it to `artifacts/scene-ui/<backend>/fixtures/` and passes `-pocStagePmx`.

## Actual Player verification

UI builds now go to `out/<configuration>/MVP-04-<backend>/`, leaving MVP-02/03 outputs intact, and require 50 assertions (40 MVP-03 + 10 MVP-04):

- stage import adds a selected static background at the origin without changing duration; 2 materials, 2 loaded textures, 1 bone
- explorer groups and counts, stage inspector title, VMD button disabled, delete enabled
- VMD pick on a selected stage is rejected and both character motions are unchanged
- the service rejects `ReplaceMotion` on a stage
- stage transforms are unchanged while both characters advance on the shared clock
- inspector transform edits change only the stage
- hiding the stage disables its renderers, marks the list entry and hides the highlight
- the selection highlight follows the stage bounds
- selecting a character after the failure clears the stale failure status
- deleting the stage keeps both characters, their motions and the duration

Results (2026-11-19): Debug Mono, Debug IL2CPP and Release Mono each pass all 50 assertions; build-info.json reports Development true/false as expected. Native inventory and legacy regression suites were not rerun; the bash runner was used because PowerShell 7 is not installed in this session. Also recorded in TASKS.md (MVP-04). The Mono application screenshot was inspected: textured checker floor and pillars, both characters standing on the floor, grouped list and the highlight on the selected character.

## Remaining

Real third-party stage PMX files (large meshes, many textures, transparency) have not been tested. Viewport picking, light/graphics controls and JSON save/restore of stages follow.
