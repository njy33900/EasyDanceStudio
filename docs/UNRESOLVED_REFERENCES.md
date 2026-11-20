# R0-02a — Preserving unresolved references

2026-11-19. First ticket of `docs/ENHANCEMENT_SPEC_v0.4.md`. Fixes a data-loss defect in the MVP-07 scene loader: entries whose PMX was missing or failed to load were dropped from the scene, and a character whose VMD was missing lost its motion path, so saving afterwards silently removed those references from the `.mmdscene`.

## Behaviour now

- **Missing or failing model** (character or stage): the entry stays in the document unchanged (ID, type, stored path strings, placement, visibility, motion path) as an *unresolved* entry. It has no 3D object, does not affect the clock, picking or gizmo, and is listed in the scene explorer under its group as `name (누락)` in amber.
- **Missing or failing motion** on a loaded character: the character loads without motion and keeps the stored motion path; the explorer shows `name (모션 누락)` and the 모션 section says the VMD is missing and that connecting another VMD replaces it.
- **Saving** writes entries in the original document order (live and unresolved interleaved as read), then objects added since. Saving into the **same folder** writes unresolved path strings verbatim. Saving **elsewhere** ("다른 이름으로") rebases relative paths so they still point at the same missing file; absolute paths stay as they are.
- **Object IDs are stable**: reopening a file keeps its IDs (previously new IDs were generated), which the explorer, selection and later reconnect/undo rely on.
- **Removing** an unresolved entry requires selecting it and confirming "장면에서 항목 삭제" (or Delete); only that entry is removed. The confirmation says the reference will disappear from the file when saved.
- The load notice now tells the user that missing items are marked and are not lost on save. Reconnecting to a new path is H1-02.

## Implementation

- `MmdSceneService.Import(pmx, position, kind, id)` accepts an explicit, unique ID.
- `MmdSceneWorkspace.Project.cs`: document order, unresolved entries (copied `SceneObjectData` + source scene path + reason), unresolved motions per live object, `Capture` merging, `Rebase` on save-elsewhere, `RemoveUnresolved`.
- `MmdSceneWorkspace.cs`: explorer rows `unresolved-<id>`, inspector 누락된 파일 section (`MissingSection`, `MissingRemove`), delete flow and Delete key for unresolved entries; connecting a VMD clears the missing-motion record.

## Actual Player verification

UI builds go to `out/<configuration>/R0-02a-<backend>/`; the UI probe now requires 100 assertions (95 + 5) plus the 9 restart-restore assertions. Using a copy of the saved scene with the stage path and the first character's motion path pointing at a nonexistent folder:

- `missing_entries_kept_and_marked_with_stable_ids`: one unresolved entry and one missing motion; explorer rows `(누락)` / `(모션 누락)`; stage group still counts the entry; live character IDs equal the file's IDs
- `save_keeps_unresolved_entries_verbatim`: after editing another character and saving in place, the entry order is unchanged, the stage entry is byte-identical as JSON, the missing motion path is unchanged and the edit is saved
- `save_elsewhere_rebases_unresolved_paths`: saving into a subfolder changes the stored relative strings but they resolve to the same files
- `reopen_keeps_ids_and_unresolved_entries`: reopening shows the same two problems, entries and IDs
- `explicit_delete_removes_only_that_unresolved_entry`: the inspector shows the missing-file section without a gizmo; delete asks for confirmation; after saving only that entry is gone and the other missing motion path remains

The probe also saves `scene-ui-missing.png` (inspected: amber explorer rows, missing-file inspector section, disabled gizmo modes). Results (2026-11-19): Debug Mono, Debug IL2CPP, Release Mono and Release IL2CPP each pass 100 UI and 9 restart-restore assertions. Recorded in TASKS.md (R0-02a).
