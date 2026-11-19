# Codex 시작 지시문 — MMD Studio

> 작성일: 2026-11-18  
> 본문 명세서: `MMD_Studio_Spec_v1.1_Codex.md`  
> 이 지시문은 **바로 구현을 시작할 때 Codex에 입력할 프롬프트**입니다. 저장소 루트에 명세서와 함께 두거나 `docs/SPEC.md`로 복사해 전달하십시오.

## Codex에 전달할 작업 지시

당신은 Unity 6.3 LTS / C# / URP / UI Toolkit 기반의 Windows x64 독립 실행형 애플리케이션 `MMD Studio`를 구현하는 개발 에이전트입니다.

먼저 `MMD_Studio_Spec_v1.1_Codex.md` 문서를 읽어 요구사항을 파악하십시오. 이 문서는 원본 v1.0 전체 명세에 UI 상세 설계를 추가한 통합 문서입니다. 특히 다음 부분에 주목하십시오.

- `§1~3`: 목표, 기술 스택, 0~3차 단계별 기능 범위
- `§4.1~4.14`: 메인 UI 와이어프레임, 크기 기준, 패널별 동작, 선택/입력 규칙, 상태, UI 수용 기준
- `§5~8`: 핵심 아키텍처, 실제 MMD 로더 조건, 테스트와 완료 정의
- `§10~12, 부록 A`: 작업 티켓, 토큰 절약, 위험 관리, 선행 게이트

### 이번 실행에서 할 일 (우선 0단계만 수행)

1. 현 저장소 상태를 조사하고 Unity/UPM/URP 버전, 기존 소스, 로더 후보, Windows Player 빌드 가능 여부를 보고합니다. 없는 환경이나 파일을 있다고 가정하지 않습니다.
2. 원본 명세의 `0단계 Windows Player PoC`를 최우선 작업으로 계획합니다.
3. MMD 로더 후보가 **Unity Editor의 Import 기능만 지원하는지, Windows Player에서 사용자가 선택한 외부 PMX/VMD를 런타임에 읽을 수 있는지** 소스/문서/실험으로 확인합니다. 추측으로 성공 처리하지 않습니다.
4. 기능을 한 번에 전부 작성하지 말고, 1~4시간 수준의 작업 티켓으로 나눕니다. 이번 실행에서 가장 작은 검증 가능한 티켓부터 구현합니다.
5. `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/TASKS.md`, `docs/TEST_PLAN.md`가 없다면 초기 내용을 생성합니다. 실제 결과만 기록합니다.
6. 검증 가능한 범위에서 빌드/테스트를 실행하고 변경 파일, 실행 명령, 결과, 실패 원인을 보고합니다.
7. Windows Player 테스트가 어려운 환경이면 막힌 이유와 재현 가능한 수동 검증 절차를 제시하고, 테스트 성공을 주장하지 않습니다.

### UI 구현 단계에 들어갈 때의 지시 (0단계 이후)

- `§4.4`의 상단 툴바 / 왼쪽 Scene Explorer / 중앙 3D Viewport / 오른쪽 Inspector·Graphics / 하단 Playback Bar / 상태바를 기준으로 구현합니다.
- 3D 렌더 영역은 Unity Camera/URP, 런타임 UI는 UI Toolkit으로 구성합니다.
- `SelectionService`, `PlaybackService`, `SceneService`, `GraphicsService`, `ImportService`를 UI와 인터페이스로 분리합니다.
- UI와 3D 입력이 동시에 처리되는 버그를 막고, 텍스트 입력 중 전역 단축키를 비활성화합니다.
- Unity EditorWindow나 AssetDatabase, UnityEditor.Handles에 의존하는 플레이어 UI는 사용하지 않습니다.
- UI Mock은 허용하되 실제 PMX/VMD 로딩 성공으로 보고하지 않습니다.
- **1차에서 편집 타임라인, 모션 블렌딩, 플러그인, MP4 출력, 자유 도킹 시스템을 구현하지 않습니다.**

### 산출물 보고 형식

```text
[요약] 이번에 구현/검증한 범위
[환경] Unity 버전 / Windows Player 빌드 지원 / 제약
[변경] 새 파일·수정 파일
[검증] 실행한 빌드/테스트 명령, 결과, Player 실기기 검증 여부
[미해결] 오류와 재현 방법
[다음 작업] 작은 단위의 후속 티켓 3개 이내
```

**지시 원칙:** 간결하게 설계하고 구현하되, 실제 작동 근거 없이 '구현 완료'라고 말하지 마십시오. 기존 명세 요구를 임의로 축소하거나 2·3차 기능을 앞당기지 마십시오.
