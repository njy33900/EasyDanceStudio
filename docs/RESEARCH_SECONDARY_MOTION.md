# 연구 — 신체 2차 움직임(가슴·엉덩이 등) 구현 방식 (2026-11-20, Claude)

목적: PMX에 가슴·엉덩이 강체가 없는 모델에도 2차 움직임을 일괄 적용하는 기능(가칭 PH-05)의 구현 방식을 정하기 위해 코이카츠 모드, 블렌더, 유니티·언리얼 게임의 방식을 조사했다. 출처 표기: **[웹]** 이번 조사로 확인, **[지식]** 일반 지식·추정.

## 1. 조사 결과

### 코이카츠(Unity 기반)와 모드
- 게임 자체의 몸 흔들림은 Unity의 **DynamicBone(스프링 본)** 계열로 처리한다. 모드는 이를 편집하는 방향이다 **[웹]**.
- [DynamicBoneDistributionEditor](https://github.com/Njaecha/DynamicBoneDistributionEditor) (Njaecha): 캐릭터 몸·옷·장신구의 DynamicBone을 편집한다. 감쇠(damping)·탄성(elasticity)·관성(inertia)·반경(radius)·강성(stiffness)의 기본값과 본별 분포, 중력·외력·끝 오프셋, 고정 축을 조절한다 **[웹]**. → "부위별·본별 파라미터 분포"가 핵심 UX다.
- [ABMX](https://github.com/manlymarco/abmx) (ManlyMarco): 본 크기·위치 수정(체형 커스터마이즈). 물리 자체는 아님 **[웹]**.
- [KKBoneGen](https://booth.pm/ja/items/7998267) (BOOTH, 유료): 본 추가, **수동** 웨이트 페인트(Add/Sub/Smooth/Replace 브러시), 폴리곤 세분화. 자동 웨이트는 설명에 없다 **[웹]**. → 본이 없는 모델에 본을 붙이는 일은 코이카츠 생태계에서도 사람이 직접 칠한다.

### 블렌더
- [Wiggle 2](https://github.com/shteeve3d/blender-wiggle-2) 계열, [Jiggle Physics](https://github.com/naelstrof/blender-jiggle-physics)(naelstrof, verlet 알고리즘, 늘어나는 흔들림·프리셋), [Wiggle Bones](https://extensions.blender.org/add-ons/wiggle-bones/)(Blender 5.0+), Wobbly Wiggler, SpringFX(유료) 등 **본 단위 스프링 시뮬레이션**이 표준이다. 실시간 뷰포트 미리보기 후 **키프레임으로 굽기(bake)**한다 **[웹]**.
- 주의점으로 결과가 체인 길이·프레임레이트에 의존하고, 순서가 뒤섞인 렌더에서 오작동한다는 점이 있다. 그래서 굽기가 필요하다 **[웹]**. 우리 굽기 캐시와 같은 결론이다.
- 강체 기반으로는 [Bonex](https://oimoyu.github.io/blender_bonex_document/en)(PhysX), [Rigidbody Bone](https://github.com/dskjal/Rigidbody-Bone)이 있다. 본에 강체·조인트를 생성하는 도구다 **[웹]**.
- 라이선스: 대부분 GPL. **코드를 가져오면 안 되고 아이디어만 참고**한다 **[지식]**.

### 유니티 게임
- **스프링 본**: DynamicBone(유료 에셋), VRM SpringBone(UniVRM, MIT), [VgoSpringBone](https://github.com/izayoijiichan/vgospringbone), MagicaCloth(스프링 본 + 천). 옷은 Unity 기본 Cloth보다 본 기반이 실용적이라는 평이 있다 **[웹]**.
- 공통 파라미터: 강성, 감쇠, 탄성, 관성, 중력, 충돌 반경, 구·캡슐 콜라이더 **[웹]**.

### 언리얼 게임
- [AnimDynamics](https://dev.epicgames.com/documentation/unreal-engine/animation-blueprint-animdynamics-in-unreal-engine): AnimGraph 안의 가벼운 솔버. 자체 바디로 관성을 근사하고 **충돌 없음**, 선형·각도·평면 제약으로 움직임을 제한한다 **[웹]**.
- [RigidBody 노드](https://dev.epicgames.com/documentation/unreal-engine/animation-blueprint-rigid-body-in-unreal-engine): 피직스 에셋 기반. 캐릭터 몸·월드와 충돌한다. 무겁다 **[웹]**.
- [Kawaii Physics](https://pafuhana1213.github.io/KawaiiPhysics-Portal/en/) (pafuhana1213, **MIT**): 머리카락·치마·가슴용 "가짜 물리"를 노드 하나로 처리한다. 본 거리 제약으로 치마-다리 관통 방지, 애니메이션 본 동기화, 런타임 파라미터, DataAsset 저장을 지원한다 **[웹]**.

## 2. 핵심 결론

1. **신체 2차 움직임은 업계 공통으로 "스프링 본"이다.** 강체 물리(Bullet/PhysX)는 무겁고 폭발·관통 위험이 있다. 그래서 가슴·엉덩이처럼 짧고 부드러운 부위는 스프링(감쇠 진동자, verlet)으로 처리한다. 충돌은 없거나 구·캡슐 정도다.
2. MMD 방식(PMX 강체+조인트, Bullet)은 머리카락·치마 같은 **긴 체인**에 강하다. **짧은 신체 부위**는 스프링 본이 더 싸고 안정적이며 조절이 직관적이다.
3. **본이 없는 모델**에 본을 붙이는 문제는 코이카츠에서도 수동 웨이트 페인트로 푼다. 자동화는 미해결 영역이다.

## 3. 우리 앱 적용안 (PH-05 제안)

**A. 스프링 본 레이어 — 1순위.** 가슴·엉덩이 본은 있는데 강체가 없는 모델에 적용한다(강체가 있어도 사용자가 선택 가능).
- Bullet과 별개인 **우리 소유 스프링 솔버**(verlet 또는 감쇠 스프링)를 둔다. 부모 본의 애니메이션 움직임에 지연·탄성으로 반응하는 오프셋을 계산한다.
- **기존 굽기 캐시 파이프라인을 그대로 탄다**: 같은 60 Hz 틱으로 굽고, 캐시·튐 검사·부위별 강도를 공유한다. 스프링은 결정적이고 매우 싸다(본 몇 개, 틱당 수 μs 예상). Sandrone처럼 이미 무거운 모델에도 부담이 없다.
- 파라미터는 코이카츠·DynamicBone과 같은 강성·감쇠·탄성·중력에 **프리셋(약·중·강)**을 둔다. 100% 초과 강조도 스프링 파라미터로 자연스럽게 된다(강체 섞기로는 불가능했던 것).
- 일괄 적용: "모든 캐릭터에 신체 흔들림 적용" 버튼. 본 탐지는 이름(일본어·중국어·영문)과 정점 가중치로 한다.
- 구현 참고: UniVRM SpringBone·Kawaii Physics(둘 다 MIT)의 알고리즘. 코드를 가져온다면 라이선스 고지.

**B. PMX에 강체·조인트 주입 — 2순위.** 우리 로더가 PMX 파싱 후·UMT 빌드 전 단계를 거치므로 읽은 모델에 강체를 추가할 수 있다. MMD와 같은 물리 계열이라 일관성은 좋지만 무겁고(Bullet) 튜닝이 어렵다. A로 부족한 경우만.

**C. 본 없는 모델 — 조사.** (1) 본 추가 + 자동 웨이트(거리·열 확산 기반)는 품질 위험이 크다. (2) 대안으로 **정점 변형 흔들림**: 기준점(가슴·엉덩이 위치를 상체·하체 본에서 추정) 주변 정점을 거리 감쇠 가중치로 스프링 오프셋만큼 움직인다. 본·웨이트 수정 없이 동작한다. 옷 메시도 같은 월드 공간 변형장을 쓰면 일관되게 움직인다. 구현량은 중간이고, 노멀·SDEF와의 관계를 확인해야 한다.

**공통 주의:** 원본 PMX는 수정하지 않는다(런타임·장면 설정만). 적용 화면에 **모델 이용 규약 확인 안내**를 넣고, 자동 적용은 기본 OFF로 둔다.

## 4. 다음 단계 제안

1. 진단: 모델별 가슴·엉덩이 본 유무, 정점 가중치, 강체 유무를 인스펙터에 표시.
2. A 구현: 스프링 솔버 + 굽기 캐시 통합 + 프리셋 + 일괄 적용 + 규약 안내. 검증은 결정성, 튐 검사, 전후 화면, 비용.
3. C(정점 변형) 프로토타입은 A 이후 별도 조사.

## 출처
- [Njaecha/DynamicBoneDistributionEditor](https://github.com/Njaecha/DynamicBoneDistributionEditor), [manlymarco/abmx](https://github.com/manlymarco/abmx), [KKBoneGen (BOOTH)](https://booth.pm/ja/items/7998267), [bbepis/BepisPlugins](https://github.com/bbepis/BepisPlugins)
- [Wiggle Bones](https://extensions.blender.org/add-ons/wiggle-bones/), [Jiggle Physics](https://extensions.blender.org/add-ons/jiggle-physics/), [naelstrof/blender-jiggle-physics](https://github.com/naelstrof/blender-jiggle-physics), [shteeve3d/blender-wiggle-2](https://github.com/shteeve3d/blender-wiggle-2), [JacquesLucke/JiggleArmature](https://github.com/JacquesLucke/JiggleArmature), [Wobbly Wiggler](https://joseconseco.github.io/wobbly_wiggler_docs), [Bonex](https://oimoyu.github.io/blender_bonex_document/en), [dskjal/Rigidbody-Bone](https://github.com/dskjal/Rigidbody-Bone)
- [izayoijiichan/vgospringbone](https://github.com/izayoijiichan/vgospringbone), [Magica Cloth 포럼](https://discussions.unity.com/t/released-magica-cloth/773425?page=37), [VRM SpringBone 노트](https://scrapbox.io/vr-avatar-creation-fp/VRM), [DAZ 포럼](https://www.daz3d.com/forums/discussion/comment/7745636)
- [Unreal AnimDynamics](https://dev.epicgames.com/documentation/unreal-engine/animation-blueprint-animdynamics-in-unreal-engine), [Unreal RigidBody](https://dev.epicgames.com/documentation/unreal-engine/animation-blueprint-rigid-body-in-unreal-engine), [Kawaii Physics 포털](https://pafuhana1213.github.io/KawaiiPhysics-Portal/en/), [Kawaii Physics 포럼](https://forums.unrealengine.com/t/kawaii-physics-dynamic-bone-physics-to-sway-bones-more-easily-and-more-cutely/128296)

## 5. 진행 (2026-11-20)

A안을 ADR-018로 확정하고 1차 구현했다(`docs/PH00_PHYSICS_INTEGRATION.md` 11절). 테스트 모델 중 대상 모델이 없어 강제 시험으로 계산·UI를 검증했다. 눈에 보이는 효과는 대상 모델로 다시 확인해야 한다.
