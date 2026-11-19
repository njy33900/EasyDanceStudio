# KK / HS 계열 VMD 플러그인 소스 검토

조사일: 2026-11-19. 공개 개발자 페이지와 고정 커밋의 소스 검토 결과다. 게임이나 플러그인을 설치·실행한 결과가 아니며, 현재 Bullet 물리 오차를 해결했다는 의미도 아니다.

## 찾은 원본과 검토 범위

| 대상 | 출처 / 고정 커밋 | 확인한 범위 |
|---|---|---|
| MMD Director (MMDD) | [Countd360 개발자 배포 글](https://www.patreon.com/c360plugins/posts/mmd-director-pv-55604378) | 개발자가 HS2/AI/KK/KKS Studio 지원을 명시. VNGE와 게임별 API/pose editor 등에 의존한다. HS1 지원 근거로 해석하지 않는다. |
| CharaAnime | [개발자 GitHub](https://github.com/countd360/CharaAnime/tree/7a03fe74c4dcba05175ac6f1a0832e179a87a8b6) | MMDD의 지원 라이브러리/DLL 부분. MMDD 전체 VMD 플레이어 소스라고 주장하지 않는다. 포즈 동기화와 IK 상태를 검토했다. |
| KK_VMDPlay | [공개 소스 스냅샷](https://github.com/Vesperindustrial/KKVMDPlay/tree/e6a17931c5aebddea5e401284e7e6c21498355fc) | 2020년 보관된 구버전 소스. 원 개발자 저장소 또는 최신 배포본과 동일하다는 근거는 확보하지 못했다. 캐릭터 본 매핑, 발 IK, 재생 상태를 검토했다. |
| TimelineVMDSync | [개발자 GitHub](https://github.com/Njaecha/TimelineVMDSync/tree/57246ebd617e2d2dcb90aef7adf9eb7a6d88f077) | Timeline과 VMDPlay/MMDD 사이의 시각·재생 상태 동기화 구현. |
| MmdDynamicBoneStabilizer (KKS) | [개발자 소스](https://github.com/DevoLutioner/SomaDynamics-KKS/tree/e453fdae4870c59460aca755ed1a0414d75ace3c/MmdDynamicBoneStabilizer) | MMDD 포즈 적용 이후 DynamicBone/Ver02를 실행하는 패치와 Unity Cloth 초기화. 같은 개발자의 [KK 저장소](https://github.com/DevoLutioner/MmdDynamicBoneStabilizer)도 확인했지만, 아래 상세 코드 분석은 고정한 KKS 소스 기준이다. |

검토용 파일 23개의 URL·SHA-256·커밋은 `artifacts/plugin-review/download-manifest.json`, 저장소 메타데이터는 각 하위 폴더의 `SOURCE.json`에 있다. 소스는 Assets 밖에 보관했다. 명확한 재사용 라이선스를 확보하지 못했으므로 공개 열람 가능성을 코드 복사·재배포 허가로 해석하지 않는다. 이번 작업은 구조 분석이며 런타임 코드에 복사하지 않았다.

## 현재 문제에 가장 유용한 힌트

### 1. 최종 포즈 적용 이후에 물리를 실행하고 자동 중복 실행을 차단한다

[Stabilizer Plugin.cs](https://github.com/DevoLutioner/SomaDynamics-KKS/blob/e453fdae4870c59460aca755ed1a0414d75ace3c/MmdDynamicBoneStabilizer/Plugin.cs)의 `MmdPoseLateUpdatePatch`는 `MmddPoseController.LateUpdate`의 마지막 postfix에서 `RunAfterMmdPose`를 호출한다. 활성 MMDD 캐릭터 소유 DynamicBone의 원래 자동 LateUpdate는 `AllowAutomaticLateUpdate`로 차단하고, 수동 패스에서 원래 구현을 실행한다.

참고할 원칙은 **최종 애니메이션 포즈 → 물리 실행 주체 하나 → 물리 결과 반영**이다. 현재 PoC도 자동 manager 진행을 끄고 수동 고정 스텝을 사용한다. 따라서 이 소스만으로 중복 실행이 현재 원인이라고 단정할 수 없다. 다음 진단에서 각 시뮬레이션 스텝의 포즈 쓰기/solve 호출 횟수와 순서를 계측해야 한다.

DynamicBone의 렌더 프레임 단위 처리나 `m_UpdateRate=0` 설정을 Bullet에 그대로 옮기지 않는다. 오프라인 검증에서는 한 Unity 렌더 프레임 안에 여러 고정 시뮬레이션 스텝을 실행할 수 있으므로 `Time.frameCount` 기준 중복 차단은 부적합하다.

### 2. 탐색·모션 교체는 시각 변경과 물리 상태 초기화를 함께 다룬다

동일 소스의 `ResetOnMotionAnchorJump`는 이전 앵커 위치/회전과 현재 값을 비교하고, 불연속을 감지하면 `ResetAll`과 `ClearClothMotion`을 호출한다. 후자는 Unity Cloth의 `ClearTransformMotion()`을 사용한다. 물리 머리카락/치마의 흔들림 자체를 탐색으로 오인하지 않도록 움직임 기준 앵커를 분리한다.

우리 프로젝트에는 명시적인 seek/stop/loop/clip-replace 이벤트를 우선 사용하고, 네이티브 물리·본 캐시를 초기화한 뒤 고정 스텝으로 목표 시각까지 재구성하는 편이 더 직접적이다. Gene의 큰 보조 본 이동량을 무조건 seek 감지 기준으로 삼으면 실제 진단 문제를 숨길 수 있다. 이 초기화 원칙은 이미 확인한 ResetToBindPose 재생 재현성 결과와 부합하지만, 공식 베이크와의 프레임 2 오차를 해결했다는 증거는 아니다.

### 3. IK와 추가 변형도 이전 프레임 상태를 가질 수 있다

[CharaAnime CCDIKSolver.cs](https://github.com/countd360/CharaAnime/blob/7a03fe74c4dcba05175ac6f1a0832e179a87a8b6/CharaAnime/Core/CCDIKSolver.cs)는 `lastFrameQ`/`lastFrameWeight`로 이전 체인 회전을 섞고 `StoreLastQ`로 보관한다. 따라서 단순히 같은 VMD 시각을 지정하는 것만으로 모든 구현의 같은 포즈가 보장되지는 않는다. 이는 해당 IK 구현의 관찰이며, 현재 UMT가 같은 평활화를 사용한다는 주장은 아니다.

최소 체인 진단은 IK·append 없는 경우부터 시작하고, append 및 IK를 하나씩 추가해 이전 상태가 처음 개입하는 단계를 찾아야 한다. cold start, 연속 재생, reset 후 재생을 구분한다.

## 그 밖의 참고점과 적용 제한

- [MmddPoseController.cs](https://github.com/countd360/CharaAnime/blob/7a03fe74c4dcba05175ac6f1a0832e179a87a8b6/CharaAnime/Core/MmddPoseController.cs)의 `DoBoneSync`는 본 동기화와 위치·회전·크기 modifier 뒤 `PostBoneSync`를 호출한다. 반면 파일 하단의 IK 실행 순서 Harmony 코드는 `#if false` 안에 있으므로 활성 동작의 근거로 쓰지 않는다.
- [구 KK VMDAnimationController.cs](https://github.com/Vesperindustrial/KKVMDPlay/blob/e6a17931c5aebddea5e401284e7e6c21498355fc/KKVMDPlayPlugin/VMDAnimationController.cs)는 별도 VMD 본 계층과 게임 캐릭터 본 사이의 매핑/기준 회전/신체 비율 보정을 사용한다. 실제 포즈 전사는 중첩 `AutoTrack.LateUpdate`에서 발 IK·시선 처리 후 수행한다. 이는 게임 전용 리타기팅이며 PMX 원본 골격을 직접 재생하는 우리 로더와 구분해야 한다. NaN 시 hips를 10000 좌표로 보내는 우회는 채택하지 않는다. `DefaultCharaAnimOverride`의 일부 메서드는 빈 구현 수준이므로 이름만 보고 기본 Animator 차단이 완성돼 있다고 판단하지 않는다.
- [TimelineVMDSync.cs](https://github.com/Njaecha/TimelineVMDSync/blob/57246ebd617e2d2dcb90aef7adf9eb7a6d88f077/src/TimelineVMDSync/TimelineVMDSync.cs)의 `SyncPlayers`는 Timeline 초를 MMDD 프레임으로 변환하고, 정지된 구 VMDPlay에서 위치 지정이 가능하도록 Play/Pause를 거친다. 유용한 호환성 사례지만 물리 seek 재구성을 입증하는 코드는 아니다. 초·VMD 30fps·재생 배속·물리 dt를 별도로 명시할 필요가 있다.
- Stabilizer가 다루는 DynamicBone/Ver02/Unity Cloth의 감쇠·충돌체 튜닝값은 PMX Bullet 강체/조인트의 기준값이 아니다. MMDD 전체나 HS1 원본 VMD 플러그인의 완전한 공개 소스는 이번 조사에서 확보하지 못했다.

## 다음 진단

1. 최소 강체 체인에서 frame 0/1/2의 FK 전후, IK/append 전후, Bullet 직전/직후, 후처리 이후 포즈와 스텝 번호를 기록한다. 각 스텝의 네이티브 solve 횟수를 확인한다.
2. IK/append 없는 모델부터 공식 베이크와 비교하고, 기능을 하나씩 추가해 차이가 시작되는 단계를 고립한다. 계수 조절보다 실행 순서·상태 소유권을 먼저 확인한다.
3. 네이티브 바디 상태, IK/본 캐시, 시각 초기화를 구분해 reset/replay 대조를 수행한다. 기존 Gene/Sandrone의 `parityPassed=false`는 그대로 유지하며, 원인이 확인되기 전에는 일반 UI 물리를 켜지 않는다.
