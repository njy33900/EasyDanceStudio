# 작업 인계 — H1-01b 텍스처·재질 단계 분할 (구현됨, 미빌드) · Unity 라이선스로 차단 (2026-11-20, Claude)

## 상태

- `E:\workspace\EasyDanceStudio`, 브랜치 `v0.6`, HEAD `015177a`. R0-02a, V0, H1-01 첫 구현(Codex)과 이 문서의 H1-01b 코드가 모두 **미커밋**. 초기화 금지.
- **차단:** 2026-11-20 Unity 빌드가 `No valid Unity Editor license found. Please activate your license.`(종료 코드 198, `Access token is unavailable`)로 실패했다. 샌드박스 밖 실행도 동일. 사용자가 Unity Hub에 로그인/Personal 라이선스를 갱신해야 한다. 그 전까지 빌드·Player 검증 불가.
- 따라서 아래 H1-01b 코드는 **컴파일·실행 검증 전**이다.

## 이번에 한 일 (H1-01b, 코드만)

목표: Sandrone PMX 로딩의 최악 프레임(489.6ms; UMT 텍스처 332ms, 재질 455ms가 각각 한 프레임)을 항목 단위로 나눈다. 고정 UMT 패키지는 수정하지 않았다.

- 조사 결과: UMT `PMXImportOptions.loadTextures`(텍스처 배열 제공)와 `materialOverrides`(이름별 재질 제공) 공개 훅이 있다. 재질 단계 비용 대부분은 알파 감지용으로 **같은 이미지 파일을 다시 디코딩**(`SourcePixelCache`)하는 것이다. `DetectAlphaCoverage`와 `SourcePixels`는 public, 재질 구성(`BuildMaterial`/`GetShader`/`Configure*`)은 private.
- 신규 `MMDStudio/Assets/MMDStudio/Poc/MmdStagedPmxBuild.cs`: UMT(MIT) `PMXTextureLoader`/`PMXMaterialBuilder` 로직을 포팅(출처·라이선스 주석 포함)해
  - 텍스처를 **한 장씩** 디코딩(파일 바이트는 작업 스레드, 디코딩은 메인 스레드), 항목 사이 `UMTFrameBudget(8)` 양보와 취소 검사, `텍스처 불러오는 중 (i/n)…` 단계 표시
  - 디코딩한 픽셀을 **메인 텍스처에 한해** 알파 감지에 재사용(재디코딩 제거)
  - 재질을 **한 개씩** 생성, `재질 만드는 중 (i/n)…`, 같은 이름을 공유하는 재질은 UMT 원래 빌더에 남김(`materialOverrides`가 이름 키라서)
  - 결과를 `Apply(options)`로 UMT에 넘기고, 성공 시 `TransferTo(result)`로 소유권 이전(경고는 패키지와 같은 순서로 앞에 삽입). 실패·취소 시 `Dispose`가 만든 텍스처·재질을 파기 → 이 단계의 부분 자원 정리 공백도 해소
  - 패키지와 의도적으로 다른 점: 재질별 `Debug.Log` 생략, 메인 텍스처로 쓰인 BMP가 손상되면 패키지는 예외로 실패하지만 여기선 불투명으로 처리
- `MmdPocProbe.LoadAsync`: 이름 정리 뒤 `MmdStagedPmxBuild.RunAsync` → `Apply` → `BuildUnityObjectsAsync` → `TransferTo`. 단계 기록에 최장 텍스처/재질 단계 시간 추가.
- 신규 `PocPmxStagingProbe.cs` + `PocSmoke.cs` 경로 `-pocPmxStagingReport <json>`: Gene·Sandrone·자체 배경 PMX를 **기존 동기 UMT 경로(기준)와 단계 경로로 두 번씩** 불러와 경고, 텍스처(이름·크기·형식·mip·픽셀 해시), 재질(셰이더·렌더 큐·키워드·RenderType·모든 셰이더 속성), 렌더러 재질 순서를 비교하고 단계 경로의 최악 프레임을 기록.
- 신규 `scripts/bash/run-pmx-staging.sh <cfg> <backend>`: 위 프로브 실행·요약(빌드 출력 `out/<cfg>/V0-<backend>`).

## 라이선스 복구 후 할 일

1. `bash scripts/bash/run-scene-ui.sh debug Mono` — 빌드 + 기존 UI 124 + 복원 9 회귀(컴파일 오류 있으면 먼저 수정). 빌드 뒤 `git checkout -- MMDStudio/Assets/MMDStudio/Poc/PocSmoke.unity`.
2. `bash scripts/bash/run-pmx-staging.sh debug Mono` — 3개 모델 `identical=True` 확인, 다르면 mismatch 줄로 원인 수정. 최장 텍스처/재질 단계와 최악 프레임 기록.
3. Release IL2CPP로 같은 두 단계 → `docs/H1_LOADING_STAGE1.md` 갱신(H1-01b 결과), TASKS/KNOWN_ISSUES/handoff 갱신.
4. 남은 H1-01: UMT `BuildUnityObjectsAsync`의 뼈대·메시 이후 단계 실패 시 부분 자원, VMD 변환 Native 예외 경로, `.mmdscene` 열기 후보 장면 트랜잭션, 반복 취소·재시도 자원/메모리, Mono·중급 PC.

## 참고

- 큰 텍스처 한 장(예: 4096² PNG)의 디코딩은 여전히 한 프레임이다(Unity `LoadImage`는 메인 스레드 전용).
- 이전 인계: [2026-11-19_H1-01a_to_H1-01b.md](2026-11-19_H1-01a_to_H1-01b.md)(Codex).
