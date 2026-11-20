# H1-00 — PMX/VMD 로딩 분리 및 취소 경계 조사

기준: `docs/ENHANCEMENT_SPEC_v0.4.md` H1-00, UnityMMDTools 고정 커밋 `db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4` (`MMDStudio/Packages/packages-lock.json`). 2026-11-19 코드 조사. 패키지와 실행 코드는 이 조사에서 수정하지 않았다.

## 현재 Player 경로

| 작업 | 현재 호출 | 결과 |
|---|---|---|
| PMX 추가 | `MmdSceneWorkspace.ApplyPickerResult` → `MmdSceneService.Import` → `MmdPocProbe.Load` → `PMXImporter.Import(path, options)` | 코루틴이 한 프레임 양보한 뒤 파싱·텍스처·Unity 객체 생성·검사를 한 번에 수행한다. 그 구간의 화면 갱신/취소 입력은 막힌다. |
| VMD 연결/교체 | `ApplyPickerResult` → `MmdSceneService.ReplaceMotion` → `MmdPocMotion.Load` → `VMDReader.Read` → `VMDAnimationClipConverter.Convert` → 직접 트랙/바인딩 | 파일 읽기, 파싱, 변환이 동기식이다. `direct-default-ik` 모드에서도 변환기를 호출한다. 교체 후보는 성공 후에만 기존 모션을 대체한다. |
| 장면 열기 | `MmdSceneWorkspace.Project.LoadScene` → 문서 읽기 → `Scene.Clear` → 항목별 동기식 `Import`/`ReplaceMotion` | 문서 검증 실패는 원래 장면을 보존한다. 문서가 유효하면 기존 장면을 먼저 지우고 각 항목을 순서대로 복원한다. H1-01에서 이 경로의 장시간 정지도 다뤄야 한다. |

기존 성능 측정(`docs/PERFORMANCE_REPORT.md`)의 Release IL2CPP 집계는 Gene PMX 0.57초, Sandrone PMX 1.02초, VMD 두 개 0.48초, 장면 열기 1.79초다. 이 수치는 **전체 호출 시간**이며 어느 내부 단계가 느린지나 UI 프레임 정지 길이는 알려주지 않는다.

## 고정 패키지에서 확인한 경계

| 단계 | 패키지 근거 | 메인 스레드/취소 판단 |
|---|---|---|
| 파일 사전 검사·바이트 읽기 | 앱의 `MmdPocProbe.Load` 및 `MmdPocMotion.Load` | 순수 파일 작업은 별도 스레드/비동기 I/O 후보. 크기·서명 검사는 현재 각각 64 MiB PMX, 32 MiB VMD 제한을 적용한다. |
| PMX 파싱 | `Runtime/PMX/PMXReader.cs`의 `Read`(23행), `ReadAsync`(52행) | 두 경로 모두 `ScriptableObject.CreateInstance<PMXModel>`을 호출한다. **UMT 파서를 통째로 `Task.Run`에 넣을 수 없다.** `ReadAsync`는 메인 스레드에서 헤더·정점·면·재질·뼈·모프 등 구간 사이에 `UMTFrameBudget.YieldIfNeeded`로 프레임을 양보한다. 구간 내부의 긴 루프에는 취소 토큰이 없다. |
| PMX 이름 변경/객체 생성 | `Runtime/PMX/PMXImporter.cs`의 `Import`(89행), `BuildUnityObjectsAsync`(257행) | `Import`의 토큰 검사는 파싱 전·후와 이름 변경 후뿐이다. 이어지는 `BuildUnityObjects`는 동기식이며 토큰을 받지 않는다. `BuildUnityObjectsAsync`는 텍스처/재질/뼈/메시/런타임 등 큰 단계 **사이**에만 양보한다. Unity 객체 생성은 메인 스레드에 남겨야 한다. 이 API 자체에 취소 토큰과 중간 생성 자원 반환이 없다. |
| VMD 파싱 | `Runtime/VMD/VMDReader.cs`의 `Read`(48행), `ReadAsync`(115·135행) | `ScriptableObject.CreateInstance<VMDAnimation>`이 있으므로 그대로 작업 스레드로 옮길 수 없다. `ReadAsync`는 바이트 읽기 Task를 받을 수 있고 프레임 배열을 읽는 중 메인 스레드에서 양보하지만 취소 토큰은 없다. |
| VMD 변환 | `Runtime/VMD/VMDAnimationClipConverter.cs`의 `Convert`(70행), `ConvertAsync`(150행) | `ConvertAsync`는 경로 생성/솔버 준비/뼈·모프 단계에서 양보하며 진행 콜백을 받는다. 하지만 취소 토큰이 없고 일부 희소 곡선·모프 변환은 한 호출 안에서 끝난다. Native 솔버와 resolver의 정리는 정상 완료 경로에 있으며, 예외나 콜백에서 임의로 던지는 방식은 정리 보장이 불분명하다. |
| 프레임 양보 | `Runtime/Shared/UMTTiming.cs`의 `UMTFrameBudget.YieldIfNeeded`(182행) | Unity 6에서 `Awaitable.NextFrameAsync()`를 사용한다. **비동기 API는 백그라운드 작업이 아니라 메인 스레드의 프레임 분할**이다. 양보 지점 사이의 단계가 예산을 넘으면 그 프레임은 여전히 멈춘다. |

## H1-01 구현 범위 결정

1. 앱 전용 로딩 조정자를 둔다. 사전 검사 → 파일 읽기 → PMX/VMD 파싱 → Unity 생성/변환 → 검사 → 장면 반영을 명시적인 단계로 표시한다. 측정할 수 없는 단계에는 백분율을 표시하지 않는다. `Busy`·재생 일시정지·기존 장면 유지·오류 표시를 한 곳에서 관리한다.
2. 첫 단계에서는 고정 패키지를 수정하지 않고 `ReadAsync`, `BuildUnityObjectsAsync`, `ConvertAsync`를 **메인 스레드에서** 연결해 화면이 단계 사이에 갱신되는지 Player에서 측정한다. 파일 바이트 읽기만 비동기 I/O로 분리한다. 실제 긴 구간의 프레임 시간과 메모리를 측정해 16 ms/33 ms를 넘는 단계를 기록한다.
3. **즉시 취소를 약속할 수 있는 구간은 파일 선택 전, 비동기 파일 읽기 중, 파싱·생성·변환 시작 전의 앱 경계다.** UMT 비동기 메서드가 진행 중일 때 받은 취소 요청은 메서드가 안전하게 반환한 뒤 반영한다. PMX 빌드/변환 중에는 부분 결과 자원에 접근할 수 없으므로 임의 예외를 던져 중단하지 않는다. UI에는 실제 경계에 맞춰 `취소 요청됨`을 표시한다.
4. 기존 `PocAssetLifetime`은 **완성된** `PMXImportResult`의 자원만 추적한다. UMT가 객체 생성 중 예외를 던져 결과를 반환하지 않으면 텍스처·재질·메시 등의 정리 경로가 보장되지 않는다. 취소/실패의 완전한 자원 소유권을 H1-01 수용 조건으로 삼으려면 패키지에 정리 가능한 단계 결과와 `try/finally`/취소 지점을 추가하거나, 프로젝트 소유의 단계별 빌더를 만들어야 한다. 현재 패키지 API만으로 중간 단계 **즉시** 취소와 완전한 정리를 보장한다고 주장하지 않는다.
5. VMD 변환의 Native 메모리도 예외 안전성이 확인되기 전에는 콜백에서 취소 예외를 던지지 않는다. 패키지 변경이 필요하면 고정 커밋에 대한 좁은 패치로 기록하고 Mono/IL2CPP 양쪽에서 실패·취소·재시도·종료를 반복 검증한다.
6. 장면 열기는 문서 검증 후 현재 장면을 지우는 구조다. 여러 PMX/VMD 로드 중 취소·실패 시 원래 장면을 보존하려면 후보 장면을 별도로 조립하고 성공 시 교체하는 트랜잭션이 필요하다. 파일 하나 추가/모션 교체의 후보 반영과 구분해서 설계한다.

## H1-01 첫 검증 항목

- Release IL2CPP Windows Player에서 큰 PMX와 VMD의 단계 메시지, 프레임 갱신, 취소 요청 후 기존 장면 유지, 실패 후 재시도, 자원 수/메모리, 정상 종료를 기록한다. 테스트 파일은 `test/` 등 Unity `Assets/`·Git 밖에 둔다.
- 64 MiB/32 MiB 제한과 손상 파일을 유지한 상태에서 유효·무효·취소를 반복한다. 진행률은 분모가 신뢰되는 VMD 변환 프레임 콜백에만 쓸 수 있는지 별도 판단한다.
- 이 문서는 **코드 조사 결과**다. 비동기 경로의 Player 실행 성능이나 취소 안전성을 이미 검증했다는 뜻은 아니다.
