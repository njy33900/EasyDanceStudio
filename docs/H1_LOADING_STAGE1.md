# H1-01 첫 구현 — 단일 PMX/VMD 단계별 로딩

기준: `docs/ENHANCEMENT_SPEC_v0.4.md` H1-01, 선행 조사 `H1_LOADING_INVESTIGATION.md`. 2026-11-19. **H1-01 전체 완료 아님.**

## 구현

- UI의 **캐릭터/배경 추가**는 `MmdPocProbe.LoadAsync` → `MmdSceneService.ImportAsync`를 사용한다. 파일 바이트 읽기는 작업 스레드의 순수 I/O, PMX 파싱과 Unity 객체 생성은 UMT의 `ReadAsync`/`BuildUnityObjectsAsync`를 Unity 메인 스레드에서 프레임 사이에 양보하며 수행한다. 기존 동기 `Import`는 장면 복원과 진단용으로 유지했다.
- UI의 **VMD 연결/교체**는 `MmdPocMotion.LoadAsync` → `MmdSceneService.ReplaceMotionAsync`를 사용한다. 파일 읽기, UMT `VMDReader.ReadAsync`, `VMDAnimationClipConverter.ConvertAsync`, 최종 바인딩을 단계별로 표시한다. 기존 모션은 후보가 완성될 때까지 유지한다.
- 로딩 중 작은 카드에 현재 단계와 **취소** 버튼을 표시한다. 취소 요청은 안전한 경계에서 반영한다. UMT의 파싱/객체 생성/변환 단계 자체에는 취소 토큰이 없어서 현재 단계를 강제로 중단하지 않으며, 버튼에는 `취소 요청됨 · 현재 단계가 끝나면 중단합니다.`라고 표시한다. 임의의 진행률 %는 쓰지 않는다. 집중 보기에서도 로딩 카드는 보인다.
- PMX 소유자 `PocAssetLifetime`이 완성된 `PMXModel`도 추적해 소유자 해제 시 함께 파기한다. 취소로 반환된 후보 PMX/모션은 장면에 반영하지 않는다. `OnDestroy`는 진행 중 취소를 요청한다.

## 실제 Windows Player 검증

명령: `pwsh -NoProfile -File scripts/Invoke-SceneVerification.ps1 -Ui -Configuration Release -Backend IL2CPP`.

- Release IL2CPP `out/release/V0-IL2CPP/`: **UI 124/124**, 별도 프로세스 복원 **9/9**, 종료 코드 0. 테스트 PMX/VMD는 `test/`와 `artifacts/`에 있으며 Unity `Assets/`·Git 밖이다.
- 추가 Sandrone PMX를 로딩 중 취소: 기존 두 캐릭터 ID, 선택/장면 수, 오류 없음, 로딩 카드 종료 확인. 20MB Whiplash VMD를 기존 캐릭터에 연결 중 취소: 기존 `MmdPocMotion` 객체와 경로 보존 확인.
- 같은 검증에서 **유효 PMX 2개, 유효 VMD 2개, 실패 PMX/VMD, 배경 PMX, 저장·재실행** 경로가 통과했다. 실물 마우스 클릭이 아니라 Player 내부 UI Toolkit submit 이벤트다.
- 최종 유효 Sandrone PMX 한 건의 로딩 계측: 전체 **1.043초**, 최악 프레임 **489.6ms**, 관찰 프레임 8개. UMT 단계 시간: 텍스처 332.0ms, 재질 455.0ms, 뼈대 1.0ms, 메시/렌더러 15.0ms, 런타임 9.0ms. 직전 측정은 1.019초/475.3ms였다. 결과: `artifacts/scene-ui/IL2CPP/release/scene-ui.json`의 `pmxLoad*`와 `pmxStageTimings`. 이는 고사양 개발 PC의 두 번 측정이며 다른 PC/FPS 보증이 아니다.

## 남은 작업과 위험

1. **응답성 미달:** UMT `BuildUnityObjectsAsync`가 텍스처·재질 단계를 각각 한 호출로 수행해 최종 측정에서 332/455ms 정지가 남는다. 프레임 예산 8ms는 단계 사이에서만 적용된다. 패키지의 좁은 패치 또는 프로젝트 소유 단계별 빌더로 텍스처/재질을 나눠야 한다. 그 전까지 H1-01의 응답성 수용 기준을 통과했다고 표시하지 않는다.
2. **중간 실패 자원 정리 미보증:** UMT가 객체 생성 중 예외를 던지면 `BuildUnityObjectsAsync`는 부분 `PMXImportResult`를 반환하지 않는다. 컨테이너는 파기하지만 그 전에 만든 텍스처/재질/메시는 완전한 추적·파기 검증이 없다. VMD 파싱/변환 내부 Native 자원의 예외 경로도 패키지 `try/finally` 보강이 필요하다. 취소 콜백에서 예외를 던져 강제 중단하지 않는다.
3. **장면 열기:** `.mmdscene` 복원은 아직 동기식이고, 검증 후 기존 장면을 먼저 지운다. 후보 장면 트랜잭션과 항목별 단계 표시·취소는 별도 구현해야 한다.
4. **추가 검증:** PMX 손상/누락 텍스처, VMD 변환 실패, 취소·재시도 반복의 자원 수·메모리, 유효한 대용량 VMD 완료, 실제 클릭, Mono/다른 IL2CPP 구성, 중급 PC 계측이 남았다.

## H1-01b — 텍스처·재질·이름 정리 분할 (2026-11-20, Claude)

**결과:** Release IL2CPP UI 프로브의 Sandrone PMX 추가 최악 프레임 **489.6ms → 55.8ms**, 전체 1.043초 → 0.795초. 고정 UMT 패키지는 수정하지 않았다.

### 원인과 변경
- 재질 단계(455ms)의 대부분은 알파 감지를 위해 **같은 이미지 파일을 다시 디코딩**하는 것(`PMXMaterialBuilder.SourcePixelCache`)이었다. 텍스처 단계(332ms)는 한 호출 안의 반복문이었다.
- 신규 `MmdStagedPmxBuild.cs`: UMT(MIT) `PMXTextureLoader`/`PMXMaterialBuilder` 로직을 포팅(출처·라이선스 주석). 텍스처를 한 장씩(파일 바이트는 작업 스레드), 재질을 한 개씩 만들며 `UMTFrameBudget(8)`로 양보하고 항목 사이에 취소를 확인한다. 디코딩한 픽셀을 메인 텍스처에 한해 알파 감지에 재사용한다. 결과는 UMT 공개 훅 `PMXImportOptions.loadTextures`/`materialOverrides`로 넘기고, 이름을 공유하는 재질은 UMT 빌더에 남긴다(현재 모델 3종은 0개). 실패·취소 시 이 단계가 만든 텍스처·재질을 파기하고, 성공 시 소유권을 결과로 넘긴다(경고는 패키지와 같은 순서).
- 최악 프레임 위치를 계측해 보니 다음 병목은 **동기식 이름 정리**(`LoadRenameLists`+`Rename`, 150~270ms)였다. 이름 목록 JSON은 한 번만 작업 스레드에서 파싱해 캐시하고, UMT의 `RenameAsync`(같은 함수, 범주 사이 양보)로 바꿨다.
- 단계 표시에 `텍스처 불러오는 중 (i/n)…`, `재질 만드는 중 (i/n)…`을 추가했다(실제 개수 기반).
- 패키지와 의도적으로 다른 점: 재질별 `Debug.Log` 생략. 메인 텍스처로 쓰인 BMP가 손상되면 패키지는 예외로 실패하지만 여기서는 불투명으로 처리한다.

### 동일성·응답성 검증 (실제 Windows Player)
`-pocPmxStagingReport`(`PocPmxStagingProbe.cs`, `scripts/bash/run-pmx-staging.sh`)가 Gene·Sandrone·자체 배경 PMX를 **기존 동기 UMT 경로와 단계 경로로 한 번씩** 불러와 경고, 텍스처(이름·크기·형식·mip·픽셀 해시), 재질(셰이더·렌더 큐·키워드·RenderType·모든 셰이더 속성), 렌더러별 재질 순서를 비교한다.

| 구성 | Gene | Sandrone | 배경 |
|---|---|---|---|
| Release IL2CPP | 동일, 최악 71.1ms | 동일, 최악 56.0ms | 동일, 17.5ms |
| Release Mono | 동일, 91.0ms | 동일, 54.9ms | 동일, 17.5ms |
| Debug Mono | 동일, 94.2ms | 동일, 61.2ms | 동일, 17.5ms |

UI 프로브 124/124, 재실행 복원 9/9(세 구성). Sandrone 단계: 텍스처 합계 442ms(최장 1장 51ms), 재질 합계 80ms(최장 4.3ms). 증거: `artifacts/pmx-staging/<backend>-<configuration>/pmx-staging.json`.

### 남은 응답성 한계
- 큰 텍스처 1장 디코딩(약 50ms)은 Unity `LoadImage`가 메인 스레드 전용이라 한 프레임이다.
- Gene의 이름 정리 중 한 범주(형태소 기반 로마자 변환, Kawazu)가 최대 71~94ms. UMT private 코드 안이라 더 나누려면 패키지 패치가 필요하다. 첫 실행 때 Kawazu 사전 준비 비용은 이번 측정에 포함되지 않았다(기준 경로가 먼저 실행됨).
- 고사양 개발 PC 측정이다. 중급 PC 측정과 H1-01의 나머지(UMT 뼈대·메시 이후 단계 실패 시 부분 자원, VMD Native 예외 경로, `.mmdscene` 후보 장면 트랜잭션, 반복 취소·재시도 자원/메모리)는 남아 있다.

## H1-01c — 트랜잭션 장면 열기 (2026-11-20, Claude · Debug Mono / Release IL2CPP / Release Mono 검증)

**변경:** `.mmdscene` 열기가 더 이상 기존 장면을 먼저 지우지 않는다.
- `MmdSceneService.PrepareAsync`: 항목 하나(PMX + VMD)를 H1-01b 단계 로더로 비동기 준비해 별도 `Scene Candidates` 노드 아래, 렌더러를 끈 채, 장면 목록·시계 밖에 둔다. 모션 실패는 항목 실패가 아니라 `MotionError`(R0-02a 미해결 모션)로 남긴다.
- `CommitScene`: 후보 전체의 ID를 먼저 검증한 뒤 현재 장면을 비우고 후보를 한 번에 편입(배치·표시 상태 유지, 모션 시계 획득). `Discard`: 후보 모션·소유자 해제.
- `MmdSceneWorkspace.Project.LoadScene`: 문서 검증 → 항목별 `PrepareAsync`(로딩 카드에 `장면 불러오는 중 (i/n) · 단계`와 취소 버튼) → 전부 준비되면 `CommitScene` 후 미해결 항목·그래픽·뷰·타임라인·선택·경로 적용. **취소** 또는 커밋 실패 시 후보를 모두 버리고 기존 장면·객체 인스턴스·파일 경로·재생 상태를 유지한다. 파일 없음/항목 로드 실패는 기존처럼 미해결 항목.

**검증 (Debug Mono, 실제 Player):** UI 프로브 **126/126**(+2), 재실행 복원 9/9.
- `scene_open_cancel_keeps_running_scene_and_releases_candidates`: 같은 장면을 열다가 두 번째 항목 단계에서 취소 → 장면 객체가 **같은 인스턴스**, 경로·오류 없음, 후보 노드 비어 있음, 취소 전후 `PocAssetLifetime` 수 동일, 로딩 카드 숨김, 상태 표시줄 취소 안내.
- `async_scene_open_replaces_scene_in_one_step`: 이어서 끝까지 열기 → 같은 ID의 **새 인스턴스**로 교체, 모션 연결, 후보 노드 비어 있음, 자원 수 동일, 렌더러 표시.
- 측정(Debug Mono, 캐릭터 2 + 배경): 장면 열기 **2.74초 동안 최악 프레임 128.2ms**, 100프레임 갱신. 이전 동기 경로는 1.8~2.4초 동안 창 전체가 멈췄다.
- 진단: 직전 열기에서 해제된 소유자는 `Destroy`가 프레임 끝까지 지연되므로 자원 수 비교는 한 프레임 뒤에 한다(누수 아님 확인).

**Release 검증 (2026-11-20):** Release IL2CPP UI 126/126·복원 9/9, 장면 열기 **1.98초·최악 66.1ms**(94프레임). Release Mono UI 126/126·복원 9/9, **2.04초·최악 67.6ms**(95프레임). 그래픽 휘도 두 구성 일치.

**느린 프레임 단계 계측 (2026-11-20, Release IL2CPP):** UI 프로브 보고서에 `sceneOpenSlowestFrames`(가장 느린 8프레임과 그 프레임 전후 로딩 단계)를 추가했다. 장면 열기 1.98초·95프레임 중:
- 68.2 / 46.9ms — 첫 캐릭터의 `PMX 이름 정리`(UMT `RenameAsync` 내부 한 범주, Kawazu 비공개 코드라 더 나눌 수 없음. H1-01b와 같은 원인).
- 56.2 / 54.6 / 51.4 / 44.9 / 41.4ms — 두 번째 캐릭터의 큰 텍스처 한 장씩의 디코드(`ImageConversion.LoadImage`는 메인 스레드 전용).
- 45.7ms — 배경의 마지막 단계와 `CommitScene`(기존 장면 해제·편입·첫 포즈)이 한 프레임.

결론: 이제 남은 느린 프레임은 모두 **단일 원자 작업**(이름 정리 한 범주, 텍스처 한 장 디코드, 교체 한 번)이고 70ms 미만이다. 더 줄이려면 UMT 포크(이름 정리 분할) 또는 워커 스레드 이미지 디코더 도입이 필요하므로 H1-01 범위에서는 코드 변경 없이 기록만 한다. Debug Mono의 128ms는 같은 작업이 디버그 빌드에서 느려진 것으로 본다.

**남은 것:** Debug IL2CPP·중급 PC, 커밋 중 예외의 부분 상태(현재 `Clear` 이후 예외는 이론상 가능 — ID 검증을 앞에 둬 위험을 줄임), 최악 128ms 프레임의 단계 계측, 반복 취소·재시도 메모리.

## H1-01 — UMT 후반 단계 실패 시 자원 정리 (2026-11-20, Claude · Release IL2CPP 검증)

**문제:** `PMXImporter.BuildUnityObjectsAsync`가 중간에 예외를 던지면 결과 객체를 돌려주지 않는다. 그때까지 UMT가 만든 메시·재질·아바타는 `PocAssetLifetime`에 등록되지 못하고, 컨테이너 GameObject를 지워도 에셋 객체는 남는다.

**변경 (`MmdPocProbe.LoadAsync`):** 결과 없이 실패하면 `ReleaseUnreturnedBuild`가 컨테이너 아래 렌더러의 `sharedMesh`·`sharedMaterials`, `MeshFilter.sharedMesh`, `Animator.avatar`를 모아 파괴한 뒤 기존처럼 컨테이너와 파싱 모델을 해제한다. 텍스처는 H1-01b 단계 빌드가 소유·해제하고, UMTResources 툰 텍스처는 공유 에셋이므로 건드리지 않는다. 메시 빌더 자체가 목록 중간에 실패해 렌더러에 아직 붙지 않은 메시는 공개 API로 닿을 수 없다(UMT 한계, 기록만).

**검증:** 프로브 전용 훅 `MmdPocProbe.FailAfterStageForTest`가 UMT 타이밍 콜백에서 지정 단계 직후 예외를 던진다. `run-pmx-staging.sh`가 비교 검사 뒤 Gene으로 세 단계 실패를 주입하고 `Mesh/Material/Texture2D/Avatar/GameObject/ScriptableObject` 수를 전후 비교한다(`lateFailures`).

| 실패 주입 지점 | 예외 전파 | 남은 객체 |
|---|---|---|
| Build Materials 직후 | 예 | 모두 0 |
| Build Meshes and Renderers 직후 | 예 | 모두 0 |
| Build Runtime 직후 | 예 | 모두 0 |

Release IL2CPP: 비교 프로브 3모델 동일 유지, UI 126/126·복원 9/9. 정리 코드를 끈 상태에서 이 검사가 누수를 잡는지(음성 대조)는 따로 돌리지 않았다.

## H1-01 — VMD Native 예외 경로 (2026-11-20, Claude · Release IL2CPP 검증)

**조사:** 고정 커밋 UMT의 `VMDAnimationClipConverter.ConvertAsync`/`Convert`는 `MMDTransformManager` 솔버 컨텍스트, (물리 굽기 시) `MMDPhysicsManager` 컨텍스트, `Allocator.Persistent` `IndexResolver`를 만든 뒤 **성공 경로 끝에서만** 해제한다(`try/finally` 없음). 변환 중 예외가 나면 이 Native 메모리는 회수되지 않는다. 지역 변수라 밖에서 해제할 방법이 없다. `VMDReader`에는 Native 할당이 없다.

**우리 쪽 원인 제거:** 변환 중 UMT로 예외가 들어갈 수 있는 유일한 우리 코드는 진행 콜백이다. `MmdPocMotion.ReportSafely`로 VMD 로드의 모든 진행 보고(변환 진행 콜백 포함)와 PMX 빌드 타이밍 콜백의 UI 보고를 감싸 예외를 경고 로그로만 남긴다. 취소는 이미 콜백에서 던지지 않고 변환이 끝난 뒤 확인한다.

**검증:** `run-pmx-staging.sh`에 Whiplash VMD를 넘기면 항상 예외를 던지는 진행 콜백으로 Gene + VMD를 불러온다. Release IL2CPP: 콜백 11회 호출(모두 예외), 모션 정상 로드. 처음 실행에서 감싸기 전의 첫 보고(`VMD 파일 읽는 중`)가 로드를 중단시키는 것을 이 검사가 잡아 모든 보고를 감쌌다. UI 126/126·복원 9/9, 비교 3모델 동일, 후반 실패 주입 3지점 남은 객체 0 유지.

**남은 한계:** UMT 변환 코드 자체의 버그·손상 데이터로 생긴 예외는 여전히 Native 컨텍스트를 남긴다. 해결하려면 UMT를 프로젝트에 내장(embedded package)해 `try/finally`를 넣거나 업스트림에 수정을 제안해야 한다. 고정 커밋 정책을 바꾸는 결정이므로 사용자 확인 사항으로 둔다.

## H1-01 — 반복 취소·재시도 자원/메모리 (2026-11-20, Claude · Release IL2CPP 검증)

**프로브:** `PocSceneCycleProbe`(`-pocSceneCycleReport <json> -sceneFile <scene> -pocCycles N`), 실행 `bash scripts/bash/run-scene-cycle.sh <cfg> <backend> [N]`(먼저 `run-scene-ui.sh`가 저장한 장면 사용: Gene+Whiplash, Sandrone+Whiplash, 배경). 한 Player 프로세스에서 장면을 연 뒤 N회 반복: 열기 시작 → 지정 항목·단계에서 취소(`PMX 파일`, `이름 정리`, `텍스처`, `재질`, `VMD`를 항목별로 순환) → 끝까지 다시 열기. 매 회 `UnloadUnusedAssets`+GC 후 Mesh/Material/Texture2D/GameObject/ScriptableObject/`PocAssetLifetime` 수, 후보 수, Mono/Unity 할당·예약 메모리를 기록한다. 통과 기준: 모든 취소가 기존 장면 유지, 모든 재열기 성공, 객체 수 매 회 동일, Unity 할당 증가 64 MiB 미만.

**결과 (Release IL2CPP, 20회):** 통과.
- 객체 수 20회 모두 동일(Mesh 37, Material 127, Texture2D 154, GameObject 2593, ScriptableObject 120, 소유자 3, 후보 0). 취소 20/20 기존 장면 유지, 재열기 20/20 성공(각 약 1.97~2.01초).
- Unity 할당 메모리 402.7 → 404.6 MiB(+1.9 MiB, 10회 이후 변화 없음). Mono 사용량 78~81 MiB 범위(재질 단계 취소 직후 회차만 일시적으로 약 97 MiB, 다음 회 복귀). Unity 예약 메모리는 542 → 598 MiB로 계단식 증가 — 할당기 풀이 반환하지 않고 재사용하는 양으로 보이며 할당량은 늘지 않았다. 더 긴 반복에서 계속 오르는지는 미확인.
- 처음 설계에서 Gene의 `재질` 단계는 13개 재질이 한 프레임 안에 끝나 취소 지점으로 잡을 수 없어 Sandrone에서만 겨냥한다. VMD 취소는 `VMD 파일 읽는 중`에서 잡혔고 곡선 변환 도중 취소는 이 실행에서 겨냥되지 않았다(변환은 취소 검사가 변환 뒤에 있음).
