# D-04 VRM 지원 — 조사 (P2)

2026-11-22, Claude. 명세 0.5 D-04: VRoid 등 VRM 모델 사용자층을 위해 다음을 조사하고, 별도 티켓으로 만들지 정한다.

- 가져오기 가능성(라이선스 메타 표시 포함)
- VMD를 적용할 때의 본 매핑
- 재질(MToon) 처리
- 배포 조건

이 문서는 공개된 형식과 라이브러리의 구조를 바탕으로 한 검토다. 실제 VRM 파일로 시험하지는 않았다(시험 자산 없음).

## 1. 가져오기

- VRM은 glTF 2.0 확장이다. 0.x(`VRM`)와 1.0(`VRMC_vrm`)이 공존한다.
- **UniVRM**(MIT, vrm-c)은 **런타임 가져오기**를 지원한다. glTF 파싱, 메시·스키닝, 표정, 스프링 본까지 포함하며 IL2CPP Player에서도 쓰인다.
- 앱 구조상 위치: `MmdSceneService`의 객체 종류(Character/Stage)는 그대로 두고, "로더"만 PMX/VRM으로 갈라지게 한다. 장면 파일은 `assetPath`의 확장자(.vrm)로 구분한다(schema 2 추가 필드 또는 schema 3).
- **라이선스 메타 표시(필수)**: VRM은 아바타 사용 조건을 메타로 담는다.
  - 0.x: allowedUser, violent/sexual, commercial, otherPermissionUrl, license.
  - 1.0: avatarPermission, allowExcessivelyViolent/SexualUsage, commercialUsage, creditNotation, allowRedistribution, modification.
  - 불러올 때 이 내용을 보여 주고, 사용자가 확인한 뒤 장면에 넣는다.
  - 출력(H3)에 크레딧 표기가 필요하면 안내한다.
  - PH-05의 "모델 이용 규약 확인 안내"와 같은 위치·방식으로 맞춘다.

## 2. VMD 적용 — 본 매핑

VRM은 Unity Humanoid 본 체계이고, VMD는 MMD 일본어 본 이름 + MMD 기준 자세다.

| MMD | VRM Humanoid | 비고 |
|---|---|---|
| センター·グルーブ | hips(위치) | 이동량은 다리 길이 비율로 맞춤(D-03 방법 A와 같은 문제) |
| 下半身 / 上半身 / 上半身2 | hips 회전 / spine / chest(upperChest) | |
| 首 / 頭 | neck / head | |
| 肩 / 腕 / ひじ / 手首 | shoulder / upperArm / lowerArm / hand | **기준 자세 차이**: MMD는 A자세(팔 약 35~40° 아래), VRM은 T자세 → 본별 회전 오프셋 필요 |
| 腕捩·手捩 | (없음) | upperArm/lowerArm 회전에 합침 |
| 足 / ひざ / 足首 / つま先 | upperLeg / lowerLeg / foot / toes | |
| 足ＩＫ·つま先ＩＫ | (없음) | IK 해석 필요: 자체 2본 IK 또는 Unity Animator IK |
| 指 본 | 손가락 humanoid 본 | 이름표로 매핑 |
| 両目·目 | leftEye/rightEye | |

- 회전 변환: MMD 좌표(왼손, Z 반전)를 Unity로 바꾼 뒤(UMT 규약), 각 본의 기준 자세 차이(MMD 휴지 → VRM 휴지)를 보정 회전으로 곱한다.
- 손 방향·손가락 축은 모델마다 달라서 "MMD 자세로 맞추기" 보정 단계가 필요하다.
- 표정: あ→aa, い→ih, う→ou, え→ee, お→oh, まばたき→blink, 笑い→happy 등은 이름표로 매핑한다. 없는 표정은 D-03 방식으로 보고한다.
- 물리: VRM은 SpringBone으로 머리·옷을 흔든다(UniVRM 내장). PMX 강체 물리(PH)와는 별도 경로이고, 출력 결정성(같은 결과)을 위해 굽기 캐시 방식으로 맞춰야 한다(PH-05와 같은 방식).

## 3. 재질 — MToon

- VRM 재질은 대부분 MToon(툰)이다. UniVRM은 Built-in/URP용 MToon 셰이더를 제공한다(1.0 계열의 URP 지원).
- 선택지는 두 가지다.
  - (i) UniVRM의 URP MToon을 그대로 쓴다. Player 포함과 variant 수를 확인하고, H2b-01 툰과 시각적으로 비교한다.
  - (ii) MToon 속성(명암 경계·그림자 색·림·아웃라인)을 H2b-01 툰 셰이더로 옮긴다.
- 앱 안에서 일관된 룩(룩 프리셋, 조명 반응)을 원하면 (ii)가 낫다. 원작 재현이 우선이면 (i)이 낫다. 시각 판단이 필요하므로 결정 대기다.

## 4. 배포 조건

- UniVRM: MIT. glTF 관련 코드도 MIT 계열이다. `ThirdPartyNotices`에 고지를 추가한다.
- 크기: 패키지 수 MB 수준(UMT 내장 58 MB와 비교하면 작음).
- 런타임 가져오기는 Player에서 셰이더·텍스처 생성을 하므로, H1-01처럼 단계 분할·취소·실패 정리가 필요하다.

## 5. 권고

- **별도 티켓으로 만든다(P2 → 필요 시 P1)**. 1차 범위: VRM 1.0과 0.x 런타임 가져오기, 메타 표시와 확인, Humanoid 기준 VMD 적용(FK + 다리 2본 IK, A/T 자세 보정), 표정 매핑, MToon은 (ii) 변환.
- 수용 기준(안):
  - VRoid 기본 모델 2종(사용 허가 확인)에서 Whiplash VMD를 적용한 전후 이미지.
  - 손·발 위치 오차(같은 VMD의 PMX 모델 대비, 다리 길이 정규화).
  - 메타 표시가 출력 전에 보인다.
  - 장면 저장과 재열기.

## 결정 대기

- VRM 티켓 착수 시점과 범위.
- MToon 처리 방식((i) 원본 셰이더 vs (ii) 앱 툰으로 변환).
- 시험용 VRM 모델 확보(사용 허가 포함, 사용자 제공).
