# MVP-08 — Light, minimal workspace design

2026-11-19. Redesign of the scene workspace to the agreed reference layout (spec 4.4/4.6): a white base, modern and simple, with fewer amateur-looking details. Behaviour and control names are unchanged; the 86 UI and 9 restart assertions still pass on every build.

## Layout

| Area | Contents |
|---|---|
| Top bar | "Easy Dance Studio", project file name (`*` when unsaved) on the left; icon buttons on the right: 새로 만들기, 열기, 저장, 다른 이름으로 저장, 전체 보기 |
| Scene explorer (left) | "장면" tree: 캐릭터 (n) and 배경 (n) groups with person/floor icons, then 환경 → 조명 and 카메라; 캐릭터 추가 / 배경 추가 at the bottom |
| 3D viewport (centre) | Rounded light-grey card; an empty-state cube icon and prompt when the scene is empty; a single-line controls hint |
| Inspector (right) | 속성 header with the selection, then stacked sections: 변형 (position/rotation/scale, 표시, 선택 보기, 삭제), 모션 (file, length, VMD 연결), 재질 (material/texture counts and unsupported morph warnings; editing is second phase), and 그래픽 (Bloom, tonemapping, exposure, contrast, saturation, background) |
| Explorer → 조명 / 카메라 | Replace the object sections with 조명 · 방향광 (colour, intensity, direction, shadow quality) or 카메라 (FOV, saved views, view reset); 그래픽 stays visible |
| Playback bar | Skip-to-start, play/pause, stop icons; timeline; `00:00.7 / 00:03.0`; 반복; 속도 slider with value |
| Status bar | Status or error text; hovering an icon button shows its description there; a "물리 OFF" chip |

Dialogs (delete confirmation, unsaved changes, load notices) are white cards over a soft dimmed overlay, with the primary or destructive action highlighted and focus on the safe choice.

The previous Properties/Graphics tabs were removed: light and camera are now scene explorer entries as in spec 4.7, and the inspector target (`Target`: object, light or camera) decides which sections are shown.

## Visual system

Defined in `Assets/MMDStudio/Poc/Resources/StudioTheme.uss` (spec 4.11 asks for USS): surface white, canvas #F4F4F5, borders #E4E4E7/#EEEEF0, text #18181B, muted #71717A, accent #2563EB, danger #DC2626. Buttons have 7 px radii and light borders; inputs are white with a blue focus border; sliders use a thin track with a ring handle; scrollbars are thin without arrows. The 3D default background is a light grey matching the card, and the selection highlight uses the accent blue.

Icons are drawn as vector line icons with UI Toolkit's `Painter2D` (`MmdIcon.cs`), because no icon font is available in the Player.

## Implementation notes

- The root and body must stay transparent: anything painted there covers the 3D camera. The viewport gutter is the card's own white border, the camera renders into the card's content rectangle, and four generated corner masks (4× supersampled) round the corners. Masks and the hint ignore picking, so they never block viewport input.
- Fields show values rounded to four decimals (e.g. 20.00001 → 20) without changing the transform until edited.
- First screenshots exposed three real problems that were fixed before verification: the white root covering the 3D view, clipped text in numeric fields and dropdowns, and explorer icons overlapping names.

## Not done

Drag and drop of files was dropped from the first phase by decision (2026-11-19). There is no settings screen (P1), so the reference's gear icon is not shown rather than offered without function. Panel resizing/docking and localisation resources remain open; strings are still in code.

Verification (2026-11-19): Debug Mono, Debug IL2CPP, Release Mono and Release IL2CPP (first Release IL2CPP build) each pass 86 UI and 9 restart-restore assertions; the main, light-inspector, delete-dialog and restored-scene screenshots were inspected on Debug and Release builds. Outputs: out/<configuration>/MVP-08-<backend>.
