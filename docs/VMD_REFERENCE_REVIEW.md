# VMD 플레이어 참조 분석 — 2026-11-18

## 결론

지금 사용하는 로더의 공식 **UMTDemo를 Unity 통합의 첫 참조**로 삼고, **babylon-mmd와 saba를 독립적인 보간·IK·시킹 대조 구현**으로 삼는다. 엔진을 교체하거나 자체 재생기를 계속 확장하기 전에, 동일한 Gene/Whiplash 입력으로 공식 데모 방식과 현재 PoC를 비교한다.

현재 PoC의 비베이크 회전 단위 오류는 수정되어 원본 VMD 키 12개와 회전 오차 0도로 일치했고, 0/15/30초 이미지에서 포즈 변화가 확인됐다. 이것은 키 시각의 FK 회전 및 제한된 재생 검증이다. 중간 프레임 보간, 원본 MMD 대비 전체 포즈, 물리의 정확성까지 검증한 것은 아니다.

## 확인한 소스와 용도

| 구현 | 고정 리비전 / 라이선스 메타데이터 | 직접 확인한 내용 | 이 프로젝트에서의 용도 |
|---|---|---|---|
| CandidumGames/UMTDemo | db14f40f844357714d5c6c10c1fbc59d758911ae / MIT | 공식 데모는 VMD를 IK 베이크한 7채널 위치·쿼터니언 곡선으로 변환한다. 재생 시 쿼터니언을 정규화하고 부호 연속성을 보정한다. PlayableDirector로 재생/시킹한다. 물리 OFF + 베이크된 모션일 때 중복 본 솔버를 끈다. | 가장 먼저 재현할 Unity 기준 경로. 데모 UI 전체를 가져오지 않고 변환·평가·시킹 계약을 참조한다. |
| noname0310/babylon-mmd | 6ee0f5d1e2fd293901b916d618655b64ceddc2db / MIT | 바인딩 실패를 경고하고 누락된 트랙을 건너뛴다. 회전은 Bezier 가중치로 Quaternion Slerp, 위치는 축별 보간. 큰 시간 이동은 물리 초기화 대상으로 표시한다. | 소수 프레임 및 보간·누락 본·물리 시킹 정책의 독립 대조 기준. TypeScript/Babylon 코드를 Unity 패키지처럼 직접 넣는 방식은 아니다. |
| benikabocha/saba | 29b8efa8b31c8e746f9a88020fb0ad9dcdcf3332 / MIT | VMD Bezier와 quaternion slerp, IK 토글 및 모프 평가를 분리한다. SyncPhysics는 단계적으로 포즈를 적용하고 물리 전/후 본 업데이트 사이에 1/30초 물리를 실행한다. | 보간 및 물리 전/후 처리 순서·프리롤 검증용. C++/렌더러 통합은 별도 작업이므로 당장 엔진에 채택하지 않는다. |
| x3bits/libmmd-for-unity | 6abd1c919af2a1e5fe306d37b2e462ed258f6b7f / BSD-3-Clause | 이름으로 본·모프를 매핑하고 SeekTime으로 포즈를 전달하는 Unity C# 구조. 확인한 MmdMotion 경로는 회전에 Quaternion.Lerp를 사용한다. | C# 어댑터 분리 구조 참고. Slerp를 쓰는 독립 구현과 회전 보간 방식이 달라 정확성의 단독 기준으로 사용하지 않는다. |

라이선스는 저장소 및 각 소스/의존성의 별도 고지를 확인해야 한다. 위 표는 공개 메타데이터/소스 조사이며, 전체 의존성의 제품 배포 승인 판단은 아니다. 소스는 artifacts/loader-review에 검토용으로만 내려받았으며, 새 참조 프로젝트 코드를 Assets에 복사하지 않았다.

## 실제로 발견한 차이

### 1. 현재 오류의 원인과 검증 범위

고정 UMT 0.5.1의 `BuildSparseBoneKeyframes`는 `math.Euler(..., ZXY)` 결과를 그대로 저장한다. 실제 단위는 라디안이다. 반면 clip-data 설명의 `localEulerAnglesRaw` 이름만 보고 Unity Quaternion.Euler에 넣으면 도 단위로 해석되어 회전이 약 57배 작아진다. 초기 PoC가 이 오류를 가졌다.

어댑터에서 라디안→도로 바꿨으며, 원본 VMD의 양쪽 팔·팔꿈치 쿼터니언을 0/450/900프레임에서 대조하는 검사를 추가했다. 최종 보고에서 12개 검사가 오차 0도를 기록했다. 검증 이미지의 로컬 회전 변화 본 수도 초기 11개에서 44개로 늘었다. 단순 시간 진행이나 월드 위치 변화만으로는 팔 회전 오류를 잡지 못했다.

### 2. 공식 데모가 피하는 경로

UMTDemo `MMDShowcaseController.LoadMotionAsync`는 `bakeIKToFK=true`로 변환한다. `VMDPlayable`은 쿼터니언의 네 채널을 평가·정규화하고, 인접 키의 q/-q 부호 연속성을 보정한다. 현재 PoC는 sparse 6채널 Euler 곡선 + 명시적 IK 풀이이므로 공식 데모와 같은 경로가 아니다. 공식 데모의 베이크/쿼터니언 경로를 별도 비교 모드로 재현하는 것이 다음 순서다.

### 3. 누락 본과 IK 정책

현재 Gene/Whiplash 조합에서 VMD 본 트랙 77개가 바인딩됐고, 없는 본 이름 96개·모프 이름 37개가 기록됐다. VMD에 모델 전용 또는 0 가중치 트랙이 포함될 수 있으므로 이 수만으로 실제 오차의 크기를 판단하지 않는다. Whiplash 규칙에 요구된 준표준 본의 누락과 실제 움직이는 필수 본을 따로 확인해야 한다.

현재 PoC에는 VMD가 언급하지 않는 Gene IK 컨트롤러 2개를 끄는 실험 정책이 들어 있다. 이는 원본 키 회전 단위 문제와 별개이며 일반 모델 정책으로 검증되지 않았다. 공식 데모 및 동일 입력 비교 전에 이를 보편적인 기본값으로 확정하지 않는다.

### 4. 물리와 시간 이동

공식 UMT 데모는 seek 후 물리를 초기화하고, 생물리와 베이크된 물리가 중복 적용되지 않게 분리한다. 또한 런타임 객체 생성 시 manager의 OnEnable이 의존 필드 연결보다 먼저 실행될 수 있다는 주의가 코드에 있다. 따라서 native DLL이 빌드에 있다는 사실만으로 물리를 켜면 안 된다.

babylon-mmd는 큰 seek에 물리 초기화를 예약하며, saba는 고정 스텝으로 목표 포즈에 단계적으로 접근한다. 현재 PoC의 물리 OFF 반복 시킹 성공을 실시간 물리의 결정적 재현으로 확대 해석하지 않는다.

## 다음 작업 순서

1. UMTDemo 방식의 IK 베이크 + 쿼터니언 재생을 독립 비교 모드로 만든다. 같은 입력·카메라·0/15/30초 및 소수 프레임을 비교하고 변환 시간·메모리도 측정한다. 베이크된 포즈를 다시 IK로 풀지 않는다.
2. Babylon/saba의 Bezier + Slerp 결과를 소수의 본·시각에서 수치 대조한다. 키 시각뿐 아니라 키 사이, 회전 경계 및 역방향 seek를 포함한다. 한 엔진의 Euler 곡선 출력만 기준값으로 삼지 않는다.
3. 누락된 필수 준표준 본·모프, 모델별 IK 기본값을 분리하여 지원표를 작성한다. 이 대조가 끝나기 전 물리나 전체 UI를 확장하지 않는다.

## POC-03 실제 비교 결과 추가

공식 데모의 원본 재생기를 실제 Unity Player에 포함하여 세 모드를 실행했다. 기존 방식과 공식 베이크 방식은 동일하지 않았다. 보조 본을 포함한 최대 월드 위치 차이는 약 12.9cm, 최대 로컬 회전 차이는 약 26.5도였다. 원래 모델 IK 기본값을 모두 유지한 비교 모드는 일부 클로크 관련 본에서 최대 약 2.02m 차이를 보였다. 미확인 IK 억제 정책 또는 기본값 유지 중 하나만으로 정확성을 단정할 수 없다.

**정정:** 당시 보고한 약 0.580도/0.608도는 검사 v1이 물리 토글 바이트를 회전 보간 제어점으로 읽은 오류가 섞인 값이므로 철회한다. Babylon의 실제 VMD 로더/자료구조까지 확인하여 기준 계산을 수정했다. 별도로 측정한 sparse/베이크 간 실제 포즈 차이는 여전히 유효하다.

공식 모드의 변환은 단일 측정에서 약 0.473초, 변환 후 Unity 할당량 증가는 약 46.9MiB였다. 원본 소스 그대로 재현 가능한 경로를 확보했으므로 미리보기 기본값으로 선택했다. 이는 잠정 기준이며 보간·모델별 솔버 차이 해결과 전체 MMD 호환성 검증은 남았다. 수치와 증거는 artifacts/vmd-comparison.md/.json 및 TASKS.md의 POC-03 항목에 있다.

## POC-04 후속 결과

원본 UMT 베이크는 `Rotatable`이 없는 회전 상속 본의 출력 곡선을 생략한다. Gene의 오른쪽 어깨 연결 본과 클로크 보조 본 4개가 해당한다. 오른쪽 어깨 상속 관계를 직접 확인하니 런타임은 일치하지만 베이크에서는 최대 약 26.54도 차이가 났다. 따라서 공식 데모 결과도 모든 본에 대한 정답으로 취급하지 않는다.

직접 Bezier·쿼터니언 평가 모드를 추가하고, Babylon의 실제 보간 블록 선택과 대조했다. 모션 전체 구간의 256개 비순차 시각과 6개 캡처 시각에서 선택 본 5개를 검사한 총 1,310개 비교에서 최대 회전 차이는 약 0.00000854도였다. 같은 검사에서 기존 Euler 경로는 최대 약 3.576도 차이가 났다. 미리보기 기본값은 직접 평가로 변경했으며, 공식 데모는 비교 모드로 보존했다. 지원되지 않는 본·물리·전체 모델 호환성까지 통과했다는 의미는 아니다.

보간 기준 정정의 근거:

- [Babylon 실제 VMD 로더: 회전 블록과 물리 토글](https://github.com/noname0310/babylon-mmd/blob/6ee0f5d1e2fd293901b916d618655b64ceddc2db/src/Loader/vmdLoader.ts)
- [Babylon VMD 자료구조: 물리 토글이 포함된 보간 바이트 배치](https://github.com/noname0310/babylon-mmd/blob/6ee0f5d1e2fd293901b916d618655b64ceddc2db/src/Loader/Parser/vmdObject.ts)
- [UMT 베이크 채널 선택](https://github.com/CandidumGames/UnityMMDTools/blob/db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4/Runtime/VMD/VMDAnimationClipConverter.Baked.cs)

## 원문

- [UMTDemo VMDPlayable](https://github.com/CandidumGames/UMTDemo/blob/db14f40f844357714d5c6c10c1fbc59d758911ae/Assets/Scripts/VMDPlayable.cs)
- [UMTDemo controller](https://github.com/CandidumGames/UMTDemo/blob/db14f40f844357714d5c6c10c1fbc59d758911ae/Assets/Scripts/MMDShowcaseController.cs)
- [UMTDemo timeline](https://github.com/CandidumGames/UMTDemo/blob/db14f40f844357714d5c6c10c1fbc59d758911ae/Assets/Scripts/VMDTimelinePlayer.cs)
- [UMT sparse converter](https://github.com/CandidumGames/UnityMMDTools/blob/db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4/Runtime/VMD/VMDAnimationClipConverter.Math.cs)
- [babylon-mmd evaluator](https://github.com/noname0310/babylon-mmd/blob/6ee0f5d1e2fd293901b916d618655b64ceddc2db/src/Runtime/Animation/mmdRuntimeModelAnimation.pure.ts)
- [babylon-mmd runtime](https://github.com/noname0310/babylon-mmd/blob/6ee0f5d1e2fd293901b916d618655b64ceddc2db/src/Runtime/mmdRuntime.ts)
- [saba VMDAnimation](https://github.com/benikabocha/saba/blob/29b8efa8b31c8e746f9a88020fb0ad9dcdcf3332/src/Saba/Model/MMD/VMDAnimation.cpp)
- [libmmd MotionPlayer](https://github.com/x3bits/libmmd-for-unity/blob/6abd1c919af2a1e5fe306d37b2e462ed258f6b7f/Assets/LibMmd/Motion/MotionPlayer.cs)
- [libmmd MmdMotion](https://github.com/x3bits/libmmd-for-unity/blob/6abd1c919af2a1e5fe306d37b2e462ed258f6b7f/Assets/LibMmd/Motion/MmdMotion.cs)
# KK / HS plugin follow-up

2026-11-19: MMDD developer sources, an archived KK VMDPlay snapshot, Timeline synchronization and a DynamicBone stabilization plugin were reviewed. Findings, pinned revisions and applicability limits are in [KK_HS_VMD_REFERENCE_REVIEW.md](KK_HS_VMD_REFERENCE_REVIEW.md). No plugin code was copied into Assets, and the Bullet reference mismatch remains unresolved.

