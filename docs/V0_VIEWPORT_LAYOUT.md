# V0 — 창 전체 뷰포트와 숨길 수 있는 UI

기준: `docs/ENHANCEMENT_SPEC_v0.4.md` V0-01~06, ADR-016. 2026-11-19 구현. 이전 R0-02a의 미커밋 누락 참조 보존 변경 위에서 작업했으며 커밋/푸시는 하지 않았다.

## 동작

- 3D 카메라는 앱 창 클라이언트 영역 전체를 렌더링한다(`Camera.rect = (0,0,1,1)`). 상단 바, 탐색기, 속성 창, 재생 바, 상태 줄은 뷰포트 위 흰 카드로 뜬다. 기존 고정 뷰포트 카드의 흰 여백·코너 마스크는 제거했다.
- **V0 실행 파일을 더블클릭하면 이 장면 UI가 바로 열린다.** 예전 단일 모델 진단 화면은 명시적인 `-legacyWorkspace` 옵션 또는 진단 프로브 옵션으로만 연다. 유니티 에디터와 PowerShell은 일반 실행에 필요하지 않다.
- 각 카드(상단 바·탐색기·속성 창·재생 바·상태 바·기즈모 모드 바)의 `−` 버튼으로 그 카드만 숨긴다. 숨긴 자리의 가장자리에 작은 탭이 남아, 해당 탭으로 바로 다시 연다. 상단 바는 우측 상단 `도구 모음 ▾` 탭으로 복원한다. 상단 **보기** 메뉴에서도 여섯 카드를 각각 표시하거나 숨길 수 있다.
- **집중 보기** 버튼 또는 `H`는 모든 조작 카드와 접힌 창 탭을 숨긴다. `H`, 대화상자가 없는 상태의 `Esc`, 우측 상단 `UI` 버튼으로 복원한다. 집중 보기에서는 선택 강조 테두리와 변형 기즈모도 숨겨 장면만 보인다. 표시 상태는 실행 중에만 유지하고 재실행하면 기본 표시 상태로 돌아온다. `.mmdscene`에는 기록하지 않는다.
- 보이는 카드 위 입력은 UI에 전달되고 숨긴 카드 자리의 입력은 3D 카메라·선택으로 전달된다. 패널을 숨기는 동안 진행 중인 포인터 드래그를 종료한다. 파일·삭제·미저장 확인 대화상자는 여전히 최상위에 있으며 `Esc`가 먼저 대화상자에 적용된다. 오류/로딩 메시지는 집중 보기의 작은 알림으로 표시된다.
- `F`, 전체/선택 보기와 자동 프레이밍은 현재 보이는 카드 사이의 안전 사각형에 대상 경계가 들어가도록 거리와 피벗을 계산한다. 카드 표시를 바꾸는 것만으로 카메라가 움직이지 않는다. 보기 메뉴가 열려 있으면 메뉴도 안전 영역에서 제외한다.

## 구현 위치

- `MmdSceneWorkspace.ViewportLayout.cs`: UI 표시 상태, 보기 메뉴·복원 버튼, 입력 안전 사각형, 집중 보기 알림.
- `MmdSceneWorkspace.cs`: 전체 창 카메라, 레이어 순서, `H`/`Esc` 입력, 선택 강조 숨김.
- `MmdViewportCamera.cs`: `FrameInRect`로 현재 안전 사각형에 프레이밍.
- `MmdSceneWorkspace.Gizmo.cs`, `StudioTheme.uss`: 기즈모 표시 상태, 떠 있는 카드 레이아웃.
- `PocSceneUiProbe.cs`: 기존 검사와 V0 표시·입력·프레이밍·리사이즈 검사.

## 실제 Windows Player 검증

```powershell
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Debug -Backend Mono
pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Release -Backend IL2CPP
pwsh -NoProfile -File scripts/Start-SceneStudio.ps1 -Configuration Release -Backend IL2CPP
```

가장 쉬운 실행: 탐색기에서 `out/release/V0-IL2CPP/MmdStudioPoC.exe`를 더블클릭한다. 같은 폴더의 `MmdStudioPoC_Data/` 등 Unity 빌드 파일을 EXE와 함께 둔다. 실행 후 **캐릭터 추가**로 외부 PMX를, 캐릭터 선택 후 **VMD 연결**로 모션을 지정한다.

두 구성 모두 **UI 114/114 + 별도 프로세스 복원 9/9**, 프로세스 정상 종료. 1280×720에서 전체 창 카메라·카드 입력 경계·개별 숨김·집중 보기·`H`/`Esc` 복원·기즈모/선택 강조 숨김·안전 영역 프레이밍을 확인했다. Player 내부에서 1600×900으로 창을 바꾸고 전체 카메라·패널/뷰포트 입력 경계를 검사한 뒤 1280×720으로 돌렸다. 스크린샷은 앱 자체 `ScreenCapture`로 생성했으며 데스크톱 캡처를 사용하지 않았다.

기본 실행 수정 후 Release IL2CPP를 재빌드해 UI 114/114·복원 9/9를 다시 통과했다. `-sceneUi` 없이 EXE를 열어 새 UI 시작 로그, 실제 창 생성, 정상 종료(exit 0)를 확인했다. 별도 `-pocSceneReport`로 예전 진단 경로 26/26도 통과했다.

개별 카드 숨김 버튼 추가 후 Release IL2CPP를 다시 빌드해 **UI 116/116·복원 9/9**를 통과했다. 각 카드의 `−` 버튼 존재, 재생 바·상태 바·기즈모 바의 버튼 숨김과 보기 메뉴 복원, 탐색기·속성 창·상단 바의 버튼 숨김을 Player에서 검사했다. 스크린샷 `scene-ui.png`에서 여섯 버튼의 배치를 확인했다. 선택 후 상태 메시지를 확인하는 기존 검사는 호버 도움말이 상태 메시지를 대체할 수 있으므로, 오류가 지워졌는지와 선택 정보에 파일명이 표시되는지를 확인하도록 고쳤다.

접힌 창 탭 추가 후 Release IL2CPP를 다시 빌드해 **UI 120/120·복원 9/9**를 통과했다. 접힌 카드의 탭 표시와 탭 클릭 복원, 집중 보기에서 탭이 숨겨지는 동작을 검사했다. `artifacts/scene-ui/IL2CPP/release/scene-ui-collapsed.png`는 탐색기를 접은 상태의 실제 Player 프레임이다.

증거: `artifacts/scene-ui/Mono/scene-ui.json`, `scene-ui.png`, `scene-ui-focus.png`, `scene-restore.json`; Release IL2CPP는 `artifacts/scene-ui/IL2CPP/release/` 아래 동일 이름. 빌드: `out/debug/V0-Mono/`, `out/release/V0-IL2CPP/`.

## 아직 사람에게 필요한 확인

- 자동 입력은 `ProcessViewportInput`에 합성 프레임을 넣는다. 실제 마우스·키보드의 `ReadDevices` 경로, `H`/`Esc`, 보기 메뉴 클릭, 기즈모 드래그는 사람이 Release Player에서 확인해야 한다(R0-01).
- 현재 Windows 시스템 DPI를 DPI 인식 스레드에서 읽으면 **144 DPI(150%)**였다. 이 PC의 1280×720/1600×900 Player 스크린샷과 내부 클릭 경계는 확인했지만, 100% 배율 PC의 실제 글자·버튼 크기와 고DPI 다중 모니터 이동은 미검증이다. 시스템 배율을 강제로 바꾸지 않았다.
- 모델·배경의 외부 실사용 품질과 클린 PC 실행은 R0-01/R0-03 범위다. 출시 후보 전체 성능은 별도 측정이 필요하다.
