# EasyDanceStudio 작업 인계

기준일: **2026-11-19 (한국 시간)**. 사용자가 Codex에서 진행하던 작업을 Claude로 이어서 진행하도록 요청하여 작성한 현재 상태 요약이다. 같은 날 Claude가 MVP-03(뷰포트 조작·선택 강조), MVP-04(PMX 배경), MVP-05(뷰포트 클릭 선택·단축키·삭제 확인), MVP-06(카메라·조명·기본 그래픽), MVP-07(`.mmdscene` 저장/복원), MVP-08(밝은 미니멀 UI 재디자인), MVP-09(통합·성능 보고서·알려진 문제), MVP-10(W/E/R 변형 기즈모)을 구현·검증하고 이 문서를 갱신했다. **1차 MVP 기능은 기즈모까지 구현·자동 검증 완료, 사람의 수동 확인·클린 PC·중급 PC 측정이 남았다.**

## 먼저 확인할 상태

- 작업 디렉터리: `E:\workspace\EasyDanceStudio`
- Unity 프로젝트: `MMDStudio/`
- 현재 브랜치: **`v0.5_MVP06`** (원격 `origin/v0.5_MVP06`에 푸시됨). `aa71034`에 v0.1.0-poc 이후 Codex 작업과 MVP-03~06을 체크포인트로 커밋했다. MVP-07~10은 그 다음 커밋으로 같은 브랜치에 올리고 푸시했다. `v0.5_claude_hand_off_261119`와 `codex/physics-validation`은 `b105024`에 남아 있다. 물리 검증 이후 다중 캐릭터 서비스·UI, MVP-03 뷰포트 조작, MVP-04 PMX 배경, MVP-05 뷰포트 선택·단축키, MVP-06 그래픽, MVP-07 장면 저장/복원, MVP-08 UI 재디자인, MVP-09 성능 보고서, MVP-10 기즈모까지 진행했다.
- HEAD: `aa71034` (MVP-03~06 체크포인트). 그 아래 `b105024` — 원격 초기 이력과 첫 소스 체크포인트를 합친 커밋.
- 원격: `https://github.com/njy33900/EasyDanceStudio.git`
- 원격에 저장한 기준 태그: **`v0.1.0-poc`**, 대상 커밋 `f427881`.
- 태그 이후 작업은 `v0.5_MVP06` 브랜치에 커밋되어 있다. 원격 기본 브랜치나 태그만 받으면 최신 작업이 없으므로 이 브랜치를 사용한다.
- 태그는 새로 만들지 않았다. 커밋·푸시는 사용자 요청 시에만 했다.

`git status --short`로 변경 파일부터 확인한다. 기존 변경을 정리한다며 reset/clean하거나 원격 버전으로 덮어쓰지 않는다.

## 사용자 요청과 유지할 정책

- 로컬 LLM/Ollama/로컬 모델 Codex CLI를 호출하거나 위임하지 않는다. 사용자가 이번에 승인한 인계 대상은 Claude이며, 로컬 모델 금지 정책은 그대로다.
- `AGENTS.md`를 확인한다. 그 파일의 현재 cloud Codex 작업 문구는 이전 실행 주체를 나타내며, 이번 사용자의 Claude 인계 요청이 최신 지시다.
- 테스트 모델·모션·텍스처·음원은 **Unity Assets와 Git, 배포물 밖**에 유지한다. 실제 Windows Player의 외부 로딩으로 검증한다.
- **일반 화면의 물리는 OFF**다. 완벽한 MMD 물리 재현을 계속 연구하느라 기본 MVP 작업을 미루지 않기로 했다. `docs/MVP_SCOPE.md`, ADR-014 참고.
- 사용자에게 노란 화면 테두리를 만들었던 데스크톱 캡처 세션은 종료한 상태다. 최근 UI 검증은 Unity 앱 자체 framebuffer 캡처만 사용했다. 같은 종류의 데스크톱 캡처를 다시 켜지 않도록 주의한다.
- 실제 빌드/Player 결과와 미검증 사항을 구분한다. UI 이벤트 자동 검사 성공을 모든 마우스/네이티브 파일 대화상자 수동 검증으로 표현하지 않는다.

## 현재 실행 방법과 최종 빌드 경로

**사용자가 최종 지정한 경로는 루트의 `out/debug/`, `out/release/`다.** 루트 직하 debug/release 및 이전 builds 폴더는 이동 후 없어졌다.

```text
out/debug/MVP-10-Mono/MmdStudioPoC.exe       (현재 UI)
out/debug/MVP-10-IL2CPP/MmdStudioPoC.exe
out/release/MVP-10-Mono/MmdStudioPoC.exe
out/release/MVP-10-IL2CPP/MmdStudioPoC.exe
out/debug|release/MVP-02~08-*/                (이전 UI, 보존)
```

프로필별 하위 폴더를 유지해 이전 POC와 현재 앱이 덮어써지지 않는다. `/out/` 전체는 Git-ignored다. 기존 개발용 빌드 21개를 이동했고 Release Mono도 별도로 빌드했다. Debug는 Unity Development, Release는 BuildOptions.None이다. 새 출력의 `build-info.json`에 구성/백엔드가 기록된다.

저장소 루트에서 PowerShell 7로 실행한다. **이 PC의 Claude 세션 환경에는 `pwsh`가 설치되어 있지 않았다.** MVP-03~09 검증은 같은 Unity 빌드 명령과 Player 인자를 bash로 그대로 실행했다(스크립트 로직 동일, UI 95개 + 재실행 복원 9개 기준 확인, `.ps1`은 Windows PowerShell 구문 검사만). `PocBuild`는 빌드마다 `PocSmoke.unity`를 새 fileID로 다시 저장하므로 빌드 후 Git에서 되돌린다.

```powershell
# 현재 다중 캐릭터 UI. 기본은 Debug/Mono.
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Configuration Release
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Backend IL2CPP

# 새로 빌드하고 실제 UI 검증. -SkipBuild를 추가하면 기존 EXE 재사용.
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Debug -Backend Mono
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Debug -Backend IL2CPP
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Release -Backend Mono

# 1080p 성능 측정(빌드 후). 결과: artifacts/performance/<backend>-<configuration>/
pwsh -NoProfile -File scripts/Invoke-ScenePerformance.ps1 -Configuration Release -Backend Mono
```

**새 UI는 `-sceneUi` 경로다.** EXE만 직접 더블클릭하면 기존 단일 모델 경로가 기본으로 열릴 수 있으므로 위 실행 스크립트를 사용한다. 기존 `Start-MmdPoc.ps1`, `Start-SandronePoc.ps1`는 단일 모델 회귀/미리보기용으로 유지했다. 모든 빌드/실행 스크립트가 `-Configuration Debug|Release`를 받는다.

로그·JSON·이미지·생성 fixture는 `artifacts/`에 있다. Release 검증은 해당 artifacts 디렉터리의 `release/` 하위에 분리한다. 예전 로그의 내부 절대 경로는 증거 보존을 위해 바꾸지 않았다. 상세: `docs/BUILD_OUTPUTS.md`.

## 구현된 기능

### 외부 PMX/VMD 기반

- 실제 Windows Player에서 외부 PMX와 텍스처를 로드하고 VMD를 연결한다.
- direct Bezier/쿼터니언 재생, 기본 본/선별 IK·vertex morph 검증, 기존 단일 모델 경로의 제한된 camera VMD 지원.
- 잘못된 파일/교체 실패 시 기존 모델·모션을 보존하는 회복 경로와 imported resource 수명 관리.
- material/UV 등 일부 morph는 지원하지 않으며 선택 모델 경고에 표시한다. 범용 MMD 호환성 완료는 아니다.

### 물리 진단 및 상태 유지 서비스

- 기존 공식 bake 비교 불일치의 두 요인을 분리했다: 입력 보간 차이와 solver history 초기화 차이. 공식 bake는 12회 근사 보간, direct는 선형 정확 처리/24회 보간이다.
- 같은 입력만 넣으면 Sandrone의 큰 회전 차이가 사라지고, Gene은 같은 입력과 이전 solver history를 함께 유지해야 일치했다. 통제된 6개 사례에서 두 조건을 맞추면 공식 최종 포즈와 보고상 오차 0이다. **direct 보간 정확도를 낮춰 억지로 일치시키지 않는다.**
- `UMTExtensions.asmref`로 pinned UMT.Runtime에 프로젝트 소유 typed adapter를 함께 컴파일했다. 패키지 캐시 수정/복사나 런타임 reflection 없이 상태 유지가 가능하다. 진단 probe에만 reflection 경로가 남아 있다.
- 60Hz 정수 틱, reset/preroll seek, pause/stop/loop, 누적 시간 처리, 단일 소유권과 폐기된 세션 거부를 구현·검증했다.
- 비대칭 append/IK 켜기/끄기 fixture에서 실제 구동 본 및 동적 본 변화와 재현성을 Mono/IL2CPP에서 확인했다.
- 새 상태 유지 서비스로 Gene·Sandrone 각각 Whiplash 전체 약 184.667초를 두 번 재생했다. 모델별 22,162 전체 본 포즈를 샘플링하고 반복 위치 오차 0, 회전 재현 허용 범위 및 finite/100m 상한을 확인했다. 전체 길이 후속 테스트는 Mono이며, 물리 정확성·연속 영상·충돌 품질 보장은 아니다.

### MVP-01: 다중 캐릭터 서비스

- `MmdSceneService`: 안정적인 객체 ID, 모델별 imported owner/모션, 독립 배치와 하나의 double 시간 시계.
- 공통 재생·일시정지·정지·탐색·루프·0.5~2배속. 짧은 모션은 끝 포즈를 유지한다.
- 개별 VMD 교체/실패 복구, 선택 삭제 시 다른 모델 리소스 유지, 전체 정리.
- UMT Start가 수동 모드에서도 첫 계산을 수행해 정지 포즈가 바뀌는 문제가 실제 프레임 테스트에서 발견됐다. 실행 순서 9000의 초기 안정화 패스로 보정하고 회귀 검사했다. UMT의 10000 LateUpdate/SDEF 순서에 의존하므로 버전 변경 시 재검증한다.

### MVP-10: W/E/R 변형 기즈모

- 선택한 객체의 배치 원점(MMD 모델은 발밑)에 월드 축 기즈모. W 이동(화살표), E 회전(링), R 크기(축 + 가운데 균일). 뷰포트 왼쪽 위 모드 바, 마우스를 올린 핸들은 주황색.
- UI Toolkit Painter2D로 뷰포트 위에 그려 항상 보이며 셰이더 의존이 없다. 화면 공간에서 판정·드래그하고 매 단계 `SetPlacement`(검증)를 거쳐 Inspector 숫자와 양방향 동기화, 미저장 표시.
- 텍스트 입력 중 W/E/R 무시, 숨긴 객체·조명/카메라 선택 시 기즈모 없음. 월드 축만(로컬 축·스냅·평면 핸들·실행 취소 없음). 상세: `docs/TRANSFORM_GIZMO.md`.

### MVP-08: 밝은 미니멀 UI 재디자인

- 사용자가 준 참고 레이아웃: 흰 바탕, 상단 제목+프로젝트명과 오른쪽 아이콘(새로/열기/저장/다른 이름/전체 보기), 좌측 장면 탐색기 트리(캐릭터·배경 그룹, 환경 → 조명·카메라, 하단 추가 버튼), 중앙 둥근 회색 뷰포트 카드(빈 장면 안내), 우측 속성(변형·모션·재질·그래픽 섹션), 하단 아이콘 재생 바(`00:00.7 / 00:03.0`), 상태 줄(아이콘 설명·물리 OFF 칩).
- 스타일은 `Resources/StudioTheme.uss`, 아이콘은 `MmdIcon.cs`(Painter2D 벡터). 그래픽 탭 대신 탐색기의 조명/카메라 항목이 해당 섹션을 연다.
- 루트·본문은 반드시 투명해야 한다(칠하면 3D가 가려짐). 뷰포트 여백은 카드의 흰 테두리, 카메라는 카드 내부 영역, 모서리는 생성 마스크로 둥글게 한다.
- 드래그 앤 드롭은 사용자 결정으로 1차 범위에서 제외(ADR-015). 상세: `docs/UI_DESIGN.md`.

### MVP-09: 통합·성능 보고서

- 실제 Player 1920×1080, 2캐릭터(Whiplash 전체 184.7초)+배경+기본 효과, 저장 후 다시 열기, vsync 끔 3구간 측정. 이 PC(RTX 4090/Ryzen 7 9800X3D) Release Mono 평균 565~573 FPS, Release IL2CPP 617~701, 최저 1% low 192 FPS — 30 FPS 목표 충족. 장면 열기 1.8~2.4초.
- 종료 충돌 재발 추적: 변형 검사 20회(Debug/Release Mono) 모두 정상 종료. native inventory 4개 출력 모두 통과. `UMTNativePlugin.dll`만 VC++ 2015–2022 런타임이 필요한데 빌드에 포함되지 않음(클린 PC 위험).
- 측정 중 사용자가 창을 건드린 Release Mono 1회는 제외하고 다시 측정했다. 상세: `docs/PERFORMANCE_REPORT.md`, `docs/KNOWN_ISSUES.md`.

### MVP-07: `.mmdscene` 저장/복원

- 상단에 프로젝트 이름(미저장 시 `*`)과 새로 만들기/열기/저장/다른 이름으로, Ctrl+S. 네이티브 저장 대화상자(`GetSaveFileNameW`) 추가.
- JSON schema 1: 객체(종류·경로·배치·표시), 선택, 뷰, 그래픽 설정, 재생 시간/반복/배속. 장면 파일 기준 상대 경로(다른 드라이브는 절대), 임시 파일 후 원자적 교체.
- 열기는 전체 검증 후에만 기존 장면을 교체한다. 손상·버전 불일치 시 기존 장면 유지. 누락/실패한 PMX·VMD는 건너뛰고 목록 대화상자로 안내.
- 미저장 변경 추적(재생 시간·뷰·선택은 제외). 새로 만들기/열기/창 닫기 시 저장/저장 안 함/취소. `-sceneFile`(`Start-SceneStudio.ps1 -Scene`)로 시작 시 열기.
- 검증 스크립트가 UI 검사 뒤 Player를 다시 띄워 저장 파일 복원을 확인한다. 상세: `docs/SCENE_PERSISTENCE.md`.

### MVP-06: 카메라·조명·기본 그래픽 (그래픽 탭)

- Inspector에 속성/그래픽 탭. 시야각, 시점 저장(최대 16)·복원·삭제, 뷰 초기화; 방향광 색·강도·방향, 그림자 품질(끔/1024/2048/4096); Bloom·강도, Neutral 톤매핑, 노출·대비·채도; 배경색.
- `MmdSceneGraphics` + 직렬화 가능한 `SceneGraphicsSettings`(전체 검증 후 적용, 실패 시 무변경). 다음 `.mmdscene` 저장에 그대로 쓴다.
- 후처리를 켜면 URP 템플릿 기본 프로파일(Vignette, Film Grain 등)이 같이 적용되므로 우선순위 1000 전역 Volume이 모든 컴포넌트를 중립값으로 덮는다. VolumeManager가 첫 렌더 전에는 초기화되지 않아 지연 처리한다(첫 실행 멈춤 원인).
- 노출 +1.5EV 시 실제 렌더 결과 평균 휘도 약 30→53을 세 빌드 모두에서 확인 — 후처리 셰이더가 Player에서 stripping되지 않음.
- UI 코드는 partial `MmdSceneWorkspace.Graphics.cs`로 분리. 상세: `docs/GRAPHICS_CONTROLS.md`.

### MVP-05: 뷰포트 선택·단축키·삭제 확인

- 뷰포트 좌클릭(5px 이내 누름·뗌)으로 가장 가까운 표시 객체 선택, 빈 곳은 선택 해제. 목록과 같은 `SelectedId`. 좌드래그는 아무 동작 없음(향후 gizmo용).
- 피킹: renderer bounds → 현재 포즈로 bake한 메시의 양면 삼각형 판정. 숨긴 객체 제외.
- Space 재생/일시정지, Delete 삭제 확인, Esc 취소. 텍스트·숫자 필드 포커스 중에는 키 무시. 뷰포트 누름은 패널 컨트롤 포커스를 해제한다.
- 삭제 버튼과 Delete 모두 같은 모달 확인(원본 파일 미삭제·실행 취소 미지원 안내, 기본 포커스 취소). 열려 있는 동안 뷰포트·패널 입력 차단.
- 상세: `docs/VIEWPORT_SELECTION.md`.

### MVP-04: PMX 배경

- 상단 **배경 추가**. `MmdSceneService.ObjectKind.Stage`: 모션 없음, 시계 길이 미기여, 재생 중 포즈 고정. UI·서비스 모두 배경 VMD 연결을 거부한다.
- 좌측 목록을 캐릭터/배경으로 묶고 숨김 표시. 배경 선택 시 Inspector 제목·정보가 바뀌고 VMD 연결 버튼이 비활성화된다. 배치·표시·삭제·강조·프레이밍은 공통.
- 실패 문구가 이후 성공 동작 뒤에도 상태 표시줄에 남던 문제를 고쳤다.
- 테스트 배경은 `scripts/New-StageFixture.py`가 생성하는 자체 텍스처 PMX(바닥+기둥 4개, PNG 2개 상대 경로). 실제 외부 배경 PMX는 아직 검증하지 않았다.
- 상세: `docs/STAGE_WORKFLOW.md`.

### MVP-03: 뷰포트 조작과 선택 강조

- `MmdViewportCamera`: pivot/yaw/pitch/distance orbit 카메라. 우클릭 드래그 회전, 휠클릭 또는 Shift+우클릭 드래그 이동, 휠 확대, F/선택 보기/전체 보기 프레이밍(현재 각도 유지).
- 포인터가 카메라 영역 안이고 UI Toolkit pick 결과가 없을 때만 시작. 시작된 드래그는 패널 위로 나가도 유지. 텍스트/Vector3Field 포커스 중에는 휠·F 무시, 뷰포트 누름은 필드 확정만 한다. Busy와 nonfinite 입력 무시.
- 선택한 표시 캐릭터에 주황색 경계 상자와 바닥 링(Sprites/Default LineRenderer). 숨김/선택 없음/전체 삭제 시 꺼진다.
- 왼쪽 클릭은 향후 picking/gizmo용으로 비워 두었다. 카메라가 +Z에서 −Z를 보므로 위치 X가 화면에서 좌우 반대로 보인다(MVP-02부터 동일, 미정리).
- 상세: `docs/VIEWPORT_CONTROLS.md`.

### MVP-02: 실제 다중 캐릭터 UI

- `MmdSceneWorkspace`: 상단 추가/전체 보기, 좌측 캐릭터 목록, 중앙 viewport, 우측 선택 모델 inspector, 하단 공통 재생/상태 바.
- 모델만 먼저 추가한 뒤 VMD를 연결할 수 있다. `SceneObject.Motion`은 null일 수 있으며 정적 모델은 시계 길이에 기여하지 않는다.
- 선택 모델의 위치·회전·크기, 표시/숨김, 모션 연결, 선택 보기, 삭제를 연결했다. 크기는 0.001~1000, nonfinite는 거부한다.
- 숨김은 root 비활성화가 아니라 renderer만 끄므로 native owner를 해제하지 않는다.
- 동기 로딩/대화상자 중 시계를 멈추고 재개 시 정체된 프레임 delta를 건너뛴다. 로딩 자체는 아직 비동기가 아니다.
- 기본 화면의 물리/오디오 OFF. 기존 camera VMD는 새 공통 시계 UI에 아직 통합하지 않았다.

## 최근 검증 결과와 증거

| 범위 | 확인한 결과 | 로컬 증거 |
|---|---|---|
| 다중 캐릭터 씬 서비스 | Mono/IL2CPP 각 26개 통과 | `artifacts/scene/<backend>/scene.json` |
| MVP-10 UI 전체(기즈모 포함) + 저장/복원 | Debug/Release × Mono/IL2CPP 4개 모두 UI 95개 + 재실행 복원 9개 통과 | `artifacts/scene-ui/<backend>/scene-ui.json`, Release는 `release/` |
| MVP-09/10 성능 | 4개 빌드 모두 30 FPS 목표 충족(MVP-10 Release 568~646 FPS, 최악 프레임 6.2ms) | `artifacts/performance/` |
| 종료 충돌 재발 | 20회 모두 정상 종료 | `artifacts/shutdown/` |
| native/DLL/고지 (MVP-08, MVP-10) | 4개 출력 모두 통과 | `artifacts/native/` |
| 실제 UI 이벤트/렌더 (MVP-02) | Debug Mono/IL2CPP 각 23개 통과 | `artifacts/scene-ui/<backend>/scene-ui.json`, `scene-ui.png` |
| Release Mono UI | 23개 통과, Development 꺼짐 | `artifacts/scene-ui/Mono/release/` |
| 최종 out 경로 이동 후 | Debug/Release Mono 각 23개 재통과 | 같은 UI 보고서, 이동 후 `-SkipBuild` 실행 |
| 새 UI 빌드의 기존 기능 회귀 | 씬 26, 복구 13, 모프/IK 21, 물리 lifecycle 20 통과 | `artifacts/scene-ui/Mono/regression/` |
| native/DLL/고지 | 새 출력의 x64/export/고지 검사 통과 | 각 `native-inventory.json` |

MVP-03 뷰포트 검사는 워크스페이스의 프레임 입력 처리 함수에 합성 포인터 상태를 넣어 검사한다. OS/Input System 장치 이벤트 주입이 아니므로 **실제 마우스 조작은 수동 확인이 필요하다.** MVP-08에서 native inventory를 다시 확인했고 Release IL2CPP도 처음 빌드·검증했다. 옛 POC 회귀 묶음(씬 26/복구 13/물리 lifecycle 20)은 MVP-03 이후 다시 돌리지 않았다(변형 검사만 20회 재실행). 화면 이미지는 실제 앱 자체 화면을 캡처해 확인했다. UI 자동 검사는 NavigationSubmit 및 필드 변경 이벤트를 사용하며, 네이티브 파일 대화상자는 결과 처리 함수를 통해 검사했다. 일반 창/비-batch 모드가 필요하다. 숨긴 창이나 batch 모드의 Unity ScreenCapture는 실패했으므로 테스트 스크립트가 잠시 정상 앱 창을 열고 완료 후 닫는다.

### 미해결 종료 충돌 1회

기존 모프 검사에서 검증 21개와 success 보고서 생성 후, 종료 중 Windows access violation **-1073741819**가 한 번 발생했다. 당시 다른 빌드/UI 작업도 진행 중이었다. 단독 재실행은 21개 통과 및 exit 0이었다. 원인은 확정하지 않았고 native 종료 문제를 고쳤다고 주장하지 않는다.

증거: `artifacts/scene-ui/Mono/regression/Deformation-shutdown-failure.json` 및 `.log`. MVP-08 빌드에서 같은 검사를 20회 반복해 모두 정상 종료했다(`artifacts/shutdown/`). 원인은 여전히 미확정이다.

## 주요 코드 위치

모든 경로는 프로젝트 루트 기준이다.

- `MMDStudio/Assets/MMDStudio/Poc/MmdSceneService.cs`: 객체 registry/공통 시계/교체·삭제/배치·표시.
- `MMDStudio/Assets/MMDStudio/Poc/MmdSceneWorkspace.cs`: 새 UI와 바인딩, 파일 선택 결과 처리, 뷰포트 입력 게이팅과 선택 강조.
- `MMDStudio/Assets/MMDStudio/Poc/MmdViewportCamera.cs`: orbit/pan/zoom/frame 카메라 상태.
- `MMDStudio/Assets/MMDStudio/Poc/MmdSceneGraphics.cs`, `MmdSceneWorkspace.Graphics.cs`: 그래픽 상태·Volume과 그래픽 탭 UI.
- `MMDStudio/Assets/MMDStudio/Poc/MmdSceneDocument.cs`, `MmdSceneWorkspace.Project.cs`, `PocSceneRestoreProbe.cs`: 장면 파일 형식·저장/열기·미저장 경고와 재실행 복원 검사.
- `scripts/New-StageFixture.py`: 검증용 자체 배경 PMX/텍스처 생성.
- `MMDStudio/Assets/MMDStudio/Poc/Resources/StudioTheme.uss`, `MmdIcon.cs`: UI 테마와 벡터 아이콘.
- `MMDStudio/Assets/MMDStudio/Poc/MmdSceneWorkspace.Gizmo.cs`: W/E/R 기즈모.
- `MMDStudio/Assets/MMDStudio/Poc/PocScenePerformanceProbe.cs`, `scripts/Invoke-ScenePerformance.ps1`: 성능 측정.
- `MMDStudio/Assets/MMDStudio/Poc/PocSmoke.cs`: legacy/씬/UI/검증 진입 분기.
- `MMDStudio/Assets/MMDStudio/Poc/MmdPocProbe.cs`: 외부 PMX import, camera framing 옵션.
- `MMDStudio/Assets/MMDStudio/Poc/MmdPocMotion.cs`, `MmdDirectBoneTracks.cs`: VMD 평가와 시계/포즈 소유권.
- `MMDStudio/Assets/MMDStudio/Poc/PocAssetLifetime.cs`: imported resource 수명.
- `MMDStudio/Assets/MMDStudio/Poc/MmdPhysicsPlayback.cs`: 60Hz 상태 유지 재생 서비스.
- `MMDStudio/Assets/MMDStudio/UMTExtensions/`: pinned UMT assembly reference와 typed solver adapter.
- `MMDStudio/Assets/MMDStudio/Poc/Editor/PocBuild.cs`: Windows 빌드/Debug·Release/고지 복사.
- `PocSceneProbe.cs`, `PocSceneUiProbe.cs`, `PocPhysics*Probe.cs`: 실제 Player 검증 코드.
- `scripts/`: 빌드·검증·실행과 원본 synthetic fixture 생성. 신규 .cs의 .meta 파일도 함께 보존한다.

## 환경 및 테스트 에셋

- Unity **6000.3.28f1**, URP **17.3.0**.
- Editor: `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe`.
- UnityMMDTools 0.5.1, 고정 커밋 `db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4`.
- Windows x64 Mono/IL2CPP; 검증 스크립트는 PowerShell 7과 Python 3을 사용한다. Unity Hub의 Personal 활성화로 빌드했다. 샌드박스에서 Unity 라이선스 초기화가 멈춘 사례가 있어 정상 권한 빌드로 진행한 이력이 있다.
- Gene: `test/models/Gene/Gene_light.pmx`.
- Sandrone: `test/Sandrone_Runtime/桑多涅_目齿轮可动.pmx`와 함께 준비한 텍스처. 사용자가 받은 원본 편집용 파일 묶음은 `test/Sandrone_V2/`; 런타임 테스트는 준비된 Sandrone_Runtime을 사용한다.
- Whiplash: `test/aespa - Whiplash/aespa - Whiplash/Whiplash [SMOOTH].vmd`.
- 에셋 사용 조건과 출처는 기존 문서/원본 readme에 있다. 테스트 파일을 원격·Assets·앱에 포함하지 않는다.

## 다음 작업: 1차 마감 점검

1. **사람의 수동 확인**: 실제 마우스·키보드(카메라 회전/이동/확대, 클릭 선택, **기즈모 드래그**, 단축키), 네이티브 저장/열기 대화상자, **실제 외부 배경 PMX** (`Start-SceneStudio.ps1`, `-Scene`).
2. **클린 PC/VM**: VC++ 2015–2022 런타임 동봉 방식 결정 후 Release 빌드를 깨끗한 Windows에서 실행.
3. **중급 PC 1회 측정**: `Invoke-ScenePerformance.ps1 -Configuration Release`.
4. 이후 선택 항목: 비동기 로딩, 누락 파일 재연결, 기즈모 로컬 축/스냅, 문자열 리소스 분리.

**v1.0 MVP 기능 구현과 자동 검증은 끝났지만, 위 점검 전에는 1차 완료로 선언하지 않는다.** 두 모델+배경, 독립 모션 공통 재생, 배치/뷰포트/그래픽, 저장 후 재실행 복원, 잘못된 입력 복구가 최종 기본 흐름이다. MP4/고급 타임라인/블렌딩/플러그인은 이번 1차 범위 밖이다.

## 문서 읽는 순서

1. 이 파일과 `AGENTS.md`.
2. `docs/MVP_SCOPE.md`, `docs/SCENE_WORKSPACE_UI.md`, `docs/VIEWPORT_CONTROLS.md`, `docs/STAGE_WORKFLOW.md`, `docs/VIEWPORT_SELECTION.md`, `docs/GRAPHICS_CONTROLS.md`, `docs/SCENE_PERSISTENCE.md`, `docs/UI_DESIGN.md`, `docs/TRANSFORM_GIZMO.md`, `docs/PERFORMANCE_REPORT.md`, `docs/KNOWN_ISSUES.md`, `docs/MULTI_CHARACTER_SCENE.md`.
3. `docs/BUILD_OUTPUTS.md`, `docs/TEST_PLAN.md`, `docs/TASKS.md`의 Next tickets와 최신 항목.
4. 필요한 경우 물리 관련 문서와 `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`.
5. 원본 요구: `MMD_Studio_Spec_v1.1_Codex.md`, `MMD_Studio_Codex_Start_Here.md`.

TASKS 및 다른 문서의 오래된 POC 항목에는 당시의 미지원/실패 상태가 보존되어 있다. 현재 상태는 이 문서 및 최신 후속 검증과 대조한다. 다음 작업자가 구현/검증을 진행하면 이 파일의 기준일·완료 범위·남은 문제도 함께 갱신한다.
