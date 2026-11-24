# 작업 인계 — 그래픽 레퍼런스 반영, 최소 창 크기, v0.6 검토 1·13·17번 (2026-11-24, Claude)

## 상태

- `E:\workspace\EasyDanceStudio`, 브랜치 `v0.6`, 모두 커밋·푸시됨(마지막 `ec5d923` 이후 이 문서 커밋). `REVIEW_FOR_CODEX*.md`만 로컬 미커밋(의도).
- 앞 단계: [2026-11-24_window-fullscreen_to_review.md](2026-11-24_window-fullscreen_to_review.md).
- 기준선: **UI 220, 복원 14, 형식 7, 반복 열기, 복구 4개, Player 종료 정상, 추적 파일 불변** — Release·Debug × IL2CPP·Mono 네 구성 모두.

## 사용자 요청

"https://youtu.be/yBnfBtK8DrI 그래픽 레퍼런스, 남은거 진행해".

## 레퍼런스 분석 (BMJ, 【MMD】Robin ❤ Rainbow A【Honkai: Star Rail】, 3:23)

내장 브라우저로 1080p 프레임을 떠서 봤다(20 s, 62 s, 150 s, 상반신·다리 확대).
- 고정 와이드 샷, 밝은 낮 실내(창 너머 하늘), 사실적인 무대 소품.
- 하이키 노출, 하늘빛 환경광이 그림자 쪽을 받쳐 줌, 부드러운 접지 그림자.
- 캐릭터: 딱 끊기는 셀 명암이 아닌 부드러운 그라데이션, 윤곽선 거의 없음, 광택 소재(라텍스·스타킹)에 밝은 하이라이트.
- 약한 Bloom, 대비·채도 약간 낮음.

## 바꾼 것

| 커밋 | 내용 |
|---|---|
| `e99597b` | 툰 '명암 부드러움'·'환경광 받기', 조명 '환경광', 룩 프리셋 **밝은 스튜디오**(캐릭터 툰 값까지 주는 첫 프리셋), 최소 창 크기 960×540(`MmdWindowLimits.cs`), 검토 1번 PNG 시퀀스 전부/전무, 13번 렌더러 캐시 |
| `ec5d923` | 자동 카메라 VMD 해시 보고, 검토 17번 백엔드 간 비교 결과 기록 |

- 기본값은 모두 기존 룩(기존 장면·기준 이미지 변화 없음). 새 필드는 schema 2 선택 필드(`material.shadowSoftness/ambient`, `graphics.ambientIntensity/ambientColor`, 사용자 룩 프리셋 `characterToonValues/characterToon`).
- 기준 이미지: `artifacts/scene-ui/IL2CPP/release/look-3.png`(멀리), `look-3-characters.png`(캐릭터 가까이).

## 알게 된 것

- **최소 창 크기**: `WM_GETMINMAXINFO`만으로는 Unity `SetResolution`이 막히지 않았다(첫 측정). `WM_WINDOWPOSCHANGING`도 일반 창(굵은 테두리, 최대화 아님)일 때 고정하고, Win32로 직접 검사(창이 보이는 Player 창인지, 최소 추적 크기 982×596, `SetWindowPos` 400×300 → 960×540, `SetResolution` 400×300 → 960×540).
- **비등방성 필터링**: 강제해도 효과 없음 → 되돌림. 런타임 텍스처(`MmdStagedPmxBuild`, UMT `PMXTextureLoader`)는 **밉맵 없이** 만들어진다. 가까운 바닥이 흐린 것은 픽스처의 저해상도 텍스처 확대였다.
- **검토 17번**: 같은 백엔드는 바이트 동일, IL2CPP↔Mono는 위치·회전 값만 최대 4.6e-5 MMD 단위(약 4 µm) 차이 — 키·컷·곡선·시야각은 같음. 바이트 동일은 보장하지 않음.
- 측정 장면에서 카메라가 멀면 렌더 비교 검사가 의미 없다(캐릭터 몇 픽셀). 검사는 대상에 맞춘 뒤 잰다.

## 다음 후보

1. ~~런타임 텍스처 밉맵~~ — 완료(사용자 승인 후 같은 날). 모든 런타임 텍스처에 밉맵·트라이리니어·비등방성 8, 'UMT와 동일' 비교는 0단계 픽셀로, 로딩 텍스처 단계 +11~17%·최악 프레임 +4 ms, 반짝임 지표 약 1.7% 감소(테스트 텍스처가 단순해 작음). 수치는 TASKS 첫 항목. 네 구성 UI 221.
2. 실제 배경 PMX(사용 허가 포함)가 오면 레퍼런스 비교 이미지를 실제 무대로 다시 만든다.
3. 나머지 v0.6 검토 항목(네이티브 플러그인 COM·Abort, SSAO 리플렉션, 툰 크래시 근본 원인 등)과 v0.61 결정 대기(감도·회전 중심·roll·schema·광택·색조)는 사용자 판단.
