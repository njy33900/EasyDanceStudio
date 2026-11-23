# 작업 인계 — 창 크기 조절·전체 화면, 사용자 마우스 확인, 빌드 보강 (2026-11-24, Claude)

## 상태

- `E:\workspace\EasyDanceStudio`, 브랜치 `v0.6`, 모두 커밋·푸시됨. `REVIEW_FOR_CODEX*.md`만 로컬 미커밋(의도).
- 앞 단계: [2026-11-23_v0.61-autorun_to_review.md](2026-11-23_v0.61-autorun_to_review.md)(v0.61 후 자율 실행).
- 현재 기준선: **UI 217, 복원 14, 형식 7, 반복 열기, 복구 4개, Player 종료 정상, 추적 파일 불변** — Release IL2CPP·Release Mono·Debug IL2CPP·Debug Mono 네 구성 모두.
- 앱 실행: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-SceneStudio.ps1 [-Scene <파일>]`(기본 Release IL2CPP, 마지막 창 크기·모드로 열림).

## 커밋

| 커밋 | 내용 |
|---|---|
| `da32f0f` | 창 크기 조절(`resizableWindow`), F11·상단 '전체 화면' 버튼(대화상자·로딩 중에도), Esc로 나가기, UI 배율 Shrink(넓고 낮은 창에서도 카드가 화면 안), 실행 스크립트가 Windows PowerShell 5.1에서 동작·크기 강제 안 함 |
| `caa1ae2` | 최대화 창을 전체 화면으로 오판하던 버그 수정(실제 마우스로 발견), 실패한 빌드가 `ProjectSettings.asset`을 중간 상태로 남기지 않게, 실행 중인 Player가 있으면 빌드 거부 |
| (이 문서 커밋) | 실행 스크립트 기본값 Release IL2CPP, 실행 명령 문서 갱신 |

## 알게 된 것

- **빌드 exit 198의 다른 원인**: 라이선스가 끊긴 게 아니라 **Unity Hub가 꺼져 있으면** 배치 빌드가 액세스 토큰을 못 받는다. Hub는 Microsoft Store 앱(`UnityTechnologies.UnityHub`)이라 Program Files에 없다. Hub를 열면(로그인 유지 상태) 바로 해결.
- **Windows의 최대화 창은 `FullScreenMode.MaximizedWindow`**로 보고된다. "Windowed가 아니면 전체 화면"으로 판단하면 안 된다. 이제 `FullScreenWindow`·`ExclusiveFullScreen`만 전체 화면.
- **실행 중인 Player는 출력 폴더 파일을 잠근다**(`global-metadata.dat` "사용자가 매핑한 구역이 열려 있는 상태"). 이때 IL2CPP 빌드가 실패하고, 예전에는 `ProjectSettings.asset`에 IL2CPP 백엔드·Input System preloaded asset이 남았다. `PocBuild`가 실패 시 되돌려 저장하고 `run-scene-ui.sh`가 같은 경로 Player가 있으면 빌드하지 않는다.
- `git checkout`으로 되돌린 Unity 설정 파일을 Unity가 LF로 다시 쓰면 내용이 같아도 git 상태가 "수정됨"으로 보일 수 있다(`git add`로 색인만 갱신하면 사라짐). verify-all의 "tracked files"가 이 경우 FAIL로 보이니 해시(`git hash-object` = HEAD blob)로 확인한다.
- 컴퓨터 사용 도구(화면 제어)로는 오른쪽·가운데 버튼 드래그를 할 수 없다(왼쪽 누르기/떼기만). 우드래그·Shift+우드래그는 Input System 가상 장치 검사와 사용자 확인으로 덮는다.

## 사용자 확인

- 2026-11-24 사용자가 실행 중인 앱에서 **"마우스 조작 내가 부탁한 대로야"**로 v0.61 마우스 조작을 확인했다.

## 남은 것 (사용자)

1. v0.61 **결정 대기**: 우드래그 감도, 휠 뒤 회전 중심, roll 유지 정책, 보정 필드 schema 2 유지, 광택 하이라이트 모양, 색조·채도 조절 필요 여부.
2. 실제 배경 PMX(사용 허가 포함) — 지금 무대는 합성 픽스처.
3. v0.6 Codex 검토(`REVIEW_FOR_CODEX_v0.6.md`) 결과에서 고칠 항목 선택.
4. 아주 작은 창에서는 UI가 비율대로 작아진다(Shrink). 최소 창 크기가 필요하면 Windows 네이티브 처리(WM_GETMINMAXINFO)가 필요하다 — 요청 시 진행.
