# 연구 — 기존 MMD 도구 대비 강점 (2026-11-20, Claude)

목적: 출시 전 단계에서 "왜 MMD 대신 EasyDanceStudio를 쓰는가"에 답할 기능을 찾는다. 현재 명세(ENHANCEMENT_SPEC_v0.4)와 겹치는 것과 새로 필요한 것을 나눈다.

출처 표기: **[웹]** 이번 조사에서 확인한 내용, **[지식]** 조사로 확인하지 못한 일반 지식·추정(검증 필요).

## 1. 경쟁 도구 현황

| 도구 | 강점 | 약점 |
|---|---|---|
| MikuMikuDance | 표준·생태계(모델·모션·이펙트 배포물 대부분이 MMD 기준) | 마지막 버전 9.32(2019-12), 소스 비공개 **[웹]**. MME 마지막 버전 0.37(2014), 배포처 불안정 **[웹]**. 이펙트·물리 조정이 어렵고, 영상은 AVI 출력 후 별도 인코딩 **[지식]** |
| MikuMikuMoving | MMD 대체로 새 UI·독자 형식 **[웹]** | 갱신 정체, 생태계는 MMD 의존 **[지식]** |
| nanoem | MMD 호환 오픈소스, macOS 중심 **[웹]** | 모델 로드 충돌·발 떨림·저장 실패 같은 문제가 FAQ 항목에 있음 **[웹]** |
| Blender + mmd_tools | 최고 품질·자유도, Blender 4.2 LTS 지원 **[웹]** | 학습 곡선 큼. 렌더가 MMD보다 훨씬 느리다는 평 **[웹]**. 재질 변환 시 MMD 그림자·스페큘러 손상 **[웹]** |
| Unity/웹 라이브러리(UMT, babylon-mmd 등) | 엔진 품질 **[웹]** | 개발자용. 일반 사용자 앱이 아님 |
| 영상→모션 도구(XR Animator, Akira, OpenMMD 등) | 웹캠·영상에서 VMD 생성 **[웹]** | 별도 도구. 오디오→립싱크 VMD 도구는 이번 조사에서 찾지 못함 **[웹]** |

**빈자리:** "MMD 자산을 그대로 쓰면서, 설치·설정 없이 바로 보기 좋은 영상을 뽑는 현대적인 앱"이 비어 있다. MMD는 정체됐고 Blender는 어렵고 엔진 도구는 개발자용이다.

## 2. 사용자가 막히는 지점 (가설 — 검증 필요)

1. **보기 좋게 만들기:** MME/ray-mmd 설치·조합이 어렵다 **[지식]**.
2. **물리:** 머리카락·치마 관통, 첫 프레임 튐, 모델마다 다른 결과 **[지식]**.
3. **모션이 모델에 안 맞음:** 준표준 본 누락, 체형 차이로 발 미끄러짐·손 관통 **[지식]**.
4. **영상 출력:** AVI → 인코더 → 오디오 맞추기의 여러 단계 **[지식]**.
5. **카메라·표정:** 카메라 VMD와 립싱크가 없으면 영상이 밋밋하다. 직접 만들기는 어렵다 **[지식]**.
6. **안정성:** 32비트 메모리 한계, 충돌 시 작업 손실 **[지식]**.

설문·커뮤니티(니코니코·bilibili·Reddit·DeviantArt) 확인으로 순위를 검증하는 것을 권한다.

## 3. 강점 후보와 우선순위

### A. 반드시 메울 것 (없으면 MMD보다 못함)

| 후보 | 현재 명세 | 메모 |
|---|---|---|
| **물리 켜기(안정)** | 0.5 PH 묶음 | 춤 영상에서 머리·치마·가슴·엉덩이 등 모델에 리깅된 2차 움직임이 없으면 바로 이탈. 모델의 전 강체를 MMD 기준으로 계산해야 한다. 예열(pre-roll), 리셋, 결정적 결과, 관통 완화가 필요. 가장 큰 공백 |
| 오디오 + MP4 직접 출력 | H2a-03, H3-02/03 | 이미 계획됨. "한 번에 MP4"가 MMD 대비 체감 강점 |
| 보기 좋은 기본값(룩 프리셋) | H2a-02, H2b-01 | MME 설정 없이 프리셋 하나로 툰·림·윤곽선·DOF·블룸 |

### B. 차별화 (MMD에 없거나 어려움)

| 후보 | 효과 | 난이도 |
|---|---|---|
| **박자 기반 자동 카메라** | 오디오 BPM·구간 분석 → 컷·줌·궤도 카메라 자동 생성, 사용자가 수정 | 중. 오디오 분석 + 카메라 템플릿. 카메라 VMD 없는 모션이 많아 효과 큼 |
| **오디오 → 립싱크** | 보컬에서 모음(あいうえお) 모프 키 생성 | 중~상. 조사에서 기존 도구를 찾지 못한 빈자리 |
| **모션 맞춤(리타깃 보정)** | 준표준 본 자동 보완, 체형 차이 보정(발 고정·손 관통 완화) | 상. MMD 사용자에게 매우 흔한 고통 |
| 숏폼 출력 프리셋 | 세로 9:16, 30/60fps, 길이 제한, 투명 배경 PNG 시퀀스 | 하. H3 확장으로 저비용 |
| 물리 굽기 캐시 | 물리 결과를 저장해 재생·출력이 매번 같음 | 중. 물리 ON 이후 |
| 실시간 미리보기 = 최종 출력 | 미리보기와 출력이 같은 렌더러 | 구조상 이미 유리 |

### C. 나중 (생태계 확장)

- **VRM 지원**(VRoid 사용자층) — UMT 지원 범위 확인 필요 **[지식]**.
- 영상·웹캠 모션 캡처 결과 가져오기(BVH/VMD) — 외부 도구와 연동만.
- 다국어(한·영·일) UI.

### D. 이미 확보 중인 신뢰성 강점

트랜잭션 장면 열기, 누락 참조 보존, 자동 저장·복구(H1-06), Undo(H1-03), 64비트 Unity 기반. "작업을 잃지 않는다"는 메시지로 묶을 수 있다.

## 4. 제안 순서

1. **물리 ON**(A) — `ENHANCEMENT_SPEC_v0.5.md` PH 묶음으로 반영(2026-11-20).
2. 오디오 → MP4 출력(H2a-03, H3) + 숏폼 프리셋.
3. 룩 프리셋(H2a/H2b).
4. 박자 기반 자동 카메라 → 립싱크 → 모션 맞춤.

1~3이 "MMD만큼 되면서 더 쉽다", 4가 "MMD에 없는 것"이다.

## 5. 결정·검증이 필요한 것

- 물리 OFF 정책을 언제 바꿀지(ADR 필요). UMT 물리의 안정성·결정성 조사부터.
- 립싱크·자동 카메라를 로컬 처리로 할지(AGENTS.md 정책상 외부 AI·로컬 모델 사용 제한과의 관계 확인).
- 대상 사용자: MMD 기존 사용자 vs 입문자/숏폼 제작자. 기능 우선순위가 달라진다.

## 출처

- [MikuMikuDance — Wikipedia](https://wikipedia.com/wiki/MikuMikuDance)
- [MikuMikuMoving alternatives — AlternativeTo](https://alternativeto.net/software/mikumikumoving)
- [nanoem manual](https://nanoem.readthedocs.io/), [nanoem GitHub](https://github.com/hkrn/nanoem)
- [MikuMikuEffect — scrapbox](https://scrapbox.io/motoso/MikuMikuEffect)
- [blender_mmd_tools](https://github.com/MMD-Blender/blender_mmd_tools), [MMD 모델 Blender 가져오기 — All3DP](https://all3dp.com/2/blender-mmd-import-models/)
- [MMD effect to Blender — Blender Artists](https://blenderartists.org/t/can-i-import-convert-mikumikudances-effect-to-blender/1195786)
- [XR Animator (SystemAnimatorOnline)](https://github.com/ButzYung/SystemAnimatorOnline), [Akira — Babylon.js forum](https://forum.babylonjs.com/t/akira-motion-capture-for-mmd-blender/56903), [OpenMMD](https://gitee.com/toolkit/OpenMMD)
- [babylon-mmd 물리 굽기](https://noname0310.github.io/babylon-mmd/docs/deep-usage/bake-physics-animation)
- [EasyMMD — Steam](https://store.steampowered.com/app/2344030)
