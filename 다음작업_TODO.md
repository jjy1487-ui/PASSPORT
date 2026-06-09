# 다음 작업 TODO — 여권주세요 (핸드오프)

> 작성 260609. 직전 커밋 `5266ab6` (브랜치 재영) 기준. 컨텍스트 클리어 후 이 문서부터 보고 이어서 진행.

## 현재 상태 요약
- **설계 데이터(엑셀 `data/여권_정리_updated.xlsx` + day_schedule)는 정리 완료.**
- **단, 게임이 읽는 `day JSON`엔 일부만 반영됨 → 엑셀↔JSON 분기 있음.** (아래 1번이 핵심)
- 엔딩: **"항상 우수사원"으로 고정**(EndingTable.asset에서 우수사원=전 점수범위, 나머지 비활성).
- 위협 인물: 테러범 1(사토) + 마약범(강도식) + 밀수범(존카터) + 성형범죄자(윤서린), 전부 고정 거절.
- 장기체류자: 8~14일 5명(첸리 8·10, 자오레이 9, 천징 11, 제시카 13). 천징/제시카는 거절(회사위조/입사일오류).
- 손님 분산: 김민준 6회·첸웨이 4회 (1-1은 김민준).
- 엑셀 편집은 **xlwings**로(파일 열려도 편집 가능, 단 셀 편집 모드면 hang → 그땐 닫고 openpyxl). 헬퍼: `Tools/xlsx_live_edit.py`.

## 1. [핵심] day JSON 재빌드 — 설계를 실제 게임에 반영  ✅ 완료(260609)
엑셀→day JSON 풀 리빌드 완료. 엑셀(소스)에만 있던 스케줄 재편이 게임에 반영됨.
- **결정**: 범죄자 이름은 **엑셀 새이름으로 통일**(사용자 선택). 외국도피자→**밀수품 범죄자**(존카터,12일), 국내유입자→**마약 범죄자**(강도식,14일). 성형수술=그대로.
- **개명 반영 위치**(전부 일치):
  - 코드: `BranchKeyResolver.cs`(CriminalSmuggler/CriminalDrug), `AdvancedBranchPanel.cs`.
  - 대사툴: `branch_dialogue_map.py`(TYPE_MAP/CRIMINAL_BRANCH_KEY 키), `authored_lines.py` 키.
  - 엑셀: customer/score/payout는 이미 새이름, **ending H15/H26 설명문도 새이름으로 정리**. → source.json/day JSON 옛이름 0개.
  - ※ branch_dialogue.json 내부 조인라벨 `[외국 도피자]/[국내 유입자]`는 **의도적으로 유지**(authored 대사 조인키, 키만 새이름에 매핑).
- **장기체류자 3명 추가**(엑셀에 서류 완비, 리빌드로 자동 생성): 자오레이(9-7)=정상, 천징(11-7)=**회사명 위조**, 제시카(13-4)=**입사일 논리오류**. 천징은 `build_days.py`의 `FORCED_DEFECT`로 회사위조 고정(시드 우연 회피).
- 버그픽스: `_run_full_rebuild.py` 경로 이중화(cwd+상대경로) 수정.
- 검증: validate errors=0, day JSON 옛이름 0, Unity 컴파일 에러 0.
- **인게임 반영 남음**: Unity Play 정지 → Assets > Refresh (day JSON 재로딩). [완료시 이 줄 삭제]
- 잔여 옛이름(무해): 백업본(_backup_*/.bak), QA/문서툴(gen_day_walkthrough·scenario_walkthrough·BRANCH_CATALOG), dead 마이그레이션 `branch_normalize.py`, 생성 리포트(*.txt). 파이프라인·런타임 무관.

## 2. UIPreview 씬 생성 (Unity Edit Mode 필요)
- `Assets/Scripts/Debug/UIPreviewController.cs`(완성)를 쓸 씬 만들기: CoreRig + EventSystem + 컨트롤러.
- **레이어 3종 세트** 적용: ①풀스크린 모달 백드롭(RaycastTarget) ②팝업별 Canvas+OverrideSorting+sortingOrder ③CanvasGroup(열때 blocksRaycasts). → 드래그 시 활성 팝업만 잡히게.

## 3. 지문판독기 3단계 팝업 구현
- 스캔중 → DB조회 → 지문대조 → 일치/불일치 분기. **이미지+글씨**(드래그 X, 버튼/자동 트리거).
- **자립형(데이터 주입식)** 으로 만들면 UIPreview 씬에서 단독 재생 가능.
- 기존 `ScanData`/`CrossCheckController`/`ScanResultPanel` 재사용.

## 4. (선택) 기존 팝업 8종 레이어 레트로핏
- News 등 CoreRig 내 8개 팝업에 모달 백드롭 + Canvas 분리 (현재 단일 Canvas+박스라 바깥 클릭이 뒤로 샘).

## ⚠️ 런타임 .asset 재베이크 필수 (260609 추가)
day JSON만 리빌드하면 안 됨. 런타임은 `Assets/GameData/*.asset`(CustomerTable/CharacterScoreTable/CharacterPayoutTable 등)도 읽으므로, 엑셀 변경 후 **Unity 메뉴 `Tools/Passport/Import Data`** 로 재베이크해야 점수/지급/캐릭터 조인이 맞는다(안 하면 옛 데이터로 조인 깨짐).
- **임포터는 EndingTable.asset도 덮어쓴다** → "항상 우수사원" 오버라이드가 날아감. 임포트 직후 반드시 `git checkout HEAD -- Assets/GameData/EndingTable.asset` 로 복원(이번에 복원함).
- 지문 테이블: 성형범죄자(윤서린)만 1행(얼굴변조→지문ID). 밀수/마약범은 X-ray(contraband)로 적발 → 지문 행 없음이 정상.

## 반영/되돌리기 메모
- 변경 인게임 반영: **Unity Play 정지 → Assets > Refresh** (EndingTable.asset, day JSON).
- 엔딩 "항상 우수사원" **되돌리기(실력제 복원)**: `git`에서 커밋 `5266ab6` 이전의 `Assets/GameData/EndingTable.asset` 복원 (직전 밴드: 전설 500~600 / 청렴 400~499 / 만인귀감 300~399 / 우수사원 200~299 / 평범 100~199 / …).
