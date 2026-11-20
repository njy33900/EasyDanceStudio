# 작업 인계 — 명세 0.5·UMT 내장 완료, PH-00 물리 조사로 (2026-11-20, Claude)

## 상태

- `E:\workspace\EasyDanceStudio`, 브랜치 `v0.6`. 마지막 커밋 `909a970`(H1-01 전체). 이후 작업은 **미커밋**: 명세 0.5·조사 문서·ADR-017, UMT 내장(`MMDStudio/Packages/com.candidumgames.unitymmdtools/`, 약 58 MB), 패치 #1, 비교 프로브 확장, PH-00 문서 초안. 초기화 금지.
- `REVIEW_FOR_CODEX.md`는 로컬 전용(커밋하지 않음).
- **창이 뜨는 검증은 사용자 허락 후.** 사용자가 게임 중이면 Player 창이 포커스를 가져간다. 긴 동기 작업으로 창이 "응답 없음"이 되지 않게 한다(오늘 Whiplash 20회 변환 시도로 몇 분간 멈춘 창을 강제 종료함).

## 이번에 한 일

1. **조사** `docs/RESEARCH_COMPETITIVE_EDGE.md`: 기존 MMD 도구 대비 빈자리 = "MMD 자산 그대로 + 설정 없이 보기 좋은 영상 + 작업 안 잃음".
2. **명세 0.5** `docs/ENHANCEMENT_SPEC_v0.5.md`, **ADR-017** 확정: 대상(입문~중급·숏폼), 물리 선택형 ON(ADR-014 개정, **PMX의 모든 강체 — 가슴·엉덩이 등 신체 2차 움직임 포함 — 를 MMD 기준으로**, 부위별 강도), 순서 H1 → PH → H2a → H2b → H3 → D, D 분석은 로컬 신호 처리만, UMT 내장.
3. **UMT 내장:** PackageCache → `MMDStudio/Packages/com.candidumgames.unitymmdtools`, manifest Git 의존성 제거, lock `embedded`. 변경 목록 `PATCHES.md`.
4. **패치 #1:** VMD 변환 Native 컨텍스트·버퍼 `finally` 해제(정상 sparse 경로). Release IL2CPP 실패 1회당 누수 **16.7 MB(원본, 음성 대조) → 407 B(패치)**. 측정: `run-pmx-staging.sh`의 `conversionLeakBytesPerFailure`(전체 길이 VMD 필요 — 3초 고정 VMD로는 대조군도 0이라 무효였음).
5. **PH-00 초안** `docs/PH00_PHYSICS_INTEGRATION.md`: 기존 상태형 물리 서비스 코드 확인, 연결 설계, MMD와 다를 수 있는 점(모델 간 충돌 — UMT는 모델별 물리 월드), 측정 계획 M1~M5.

## 검증 (Release IL2CPP, 패치 적용 빌드)

UI 126/126, 복원 9/9, 비교 프로브 3모델 동일, 진행 콜백 예외 VMD 로드 성공, 후반 실패 3지점 남은 객체 0, 변환 실패 누수 407 B/회. Release Mono는 내장 전환 직후 UI 126/126·복원 9/9(패치 #1 측정은 IL2CPP만).

## 다음

1. 커밋(사용자 요청 시). 58 MB 추가가 들어간다. `.gitattributes`에 `*.sh text eol=lf` 추가 제안 미처리.
2. **PH-00 측정 완료**(`docs/PH00_PHYSICS_INTEGRATION.md` 4~5절, `run-physics-measure.sh`): Sandrone 한 틱 11 ms·30초 시킹 20초 → 굽기 캐시가 재생 기본 경로(명세 PH-03 갱신). 다음 작업: **PH-03 굽기 캐시 설계·구현**(프레임 분할 백그라운드 계산, 캐시 크기 결정), Gene 망토 추 강체 불안정의 화면 확인·원인 조사, 엉덩이 강체 있는 모델 확보, 배경 강체 충돌 측정.
3. H1 남은 검증: Debug IL2CPP, 중급 PC. 패치 #1의 굽기 경로·카메라 변환 미적용.
