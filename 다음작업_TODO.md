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

## 1. [핵심] day JSON 재빌드 — 설계를 실제 게임에 반영
엑셀엔 있지만 day JSON엔 없는 것: **장기체류자 3명 추가(9-7 자오레이 / 11-7 천징 / 13-4 제시카), 범죄자 마약/밀수 개명, 윤서린 고정** 등.
- 방법 A: `Tools/DataImport/_run_full_rebuild.py` (엑셀→day JSON 전체 재생성). 엑셀 닫혀 있어야 함.
- 방법 B: 새 손님만 수동으로 day JSON에 서류 생성.
- **새 장기체류자 서류 필요**: 여권+비자+취업증빙. → 천징=**회사명 위조**, 제시카=**입사일 논리오류(입사일>발급일)** 결함. 자오레이=정상.
- ⚠️ **코드 상수 주의**: `Assets/Scripts/Manager/BranchKeyResolver.cs`의 `CriminalForeign="범죄자(외국 도피자)"`, `CriminalDomestic="범죄자(국내 유입자)"`가 아직 **옛이름**. 엑셀(customer/score/payout/defect_rule)은 **마약/밀수 범죄자로 개명**됨. 재빌드로 day JSON이 새이름을 쓰면 코드 상수와 불일치 → AdvancedBranchPanel(범죄자 몽타주/뇌물)·점수 매칭 깨짐. → **코드 상수도 같이 개명**하거나, **day JSON은 옛이름 유지**할지 먼저 결정.

## 2. UIPreview 씬 생성 (Unity Edit Mode 필요)
- `Assets/Scripts/Debug/UIPreviewController.cs`(완성)를 쓸 씬 만들기: CoreRig + EventSystem + 컨트롤러.
- **레이어 3종 세트** 적용: ①풀스크린 모달 백드롭(RaycastTarget) ②팝업별 Canvas+OverrideSorting+sortingOrder ③CanvasGroup(열때 blocksRaycasts). → 드래그 시 활성 팝업만 잡히게.

## 3. 지문판독기 3단계 팝업 구현
- 스캔중 → DB조회 → 지문대조 → 일치/불일치 분기. **이미지+글씨**(드래그 X, 버튼/자동 트리거).
- **자립형(데이터 주입식)** 으로 만들면 UIPreview 씬에서 단독 재생 가능.
- 기존 `ScanData`/`CrossCheckController`/`ScanResultPanel` 재사용.

## 4. (선택) 기존 팝업 8종 레이어 레트로핏
- News 등 CoreRig 내 8개 팝업에 모달 백드롭 + Canvas 분리 (현재 단일 Canvas+박스라 바깥 클릭이 뒤로 샘).

## 반영/되돌리기 메모
- 변경 인게임 반영: **Unity Play 정지 → Assets > Refresh** (EndingTable.asset, day JSON).
- 엔딩 "항상 우수사원" **되돌리기(실력제 복원)**: `git`에서 커밋 `5266ab6` 이전의 `Assets/GameData/EndingTable.asset` 복원 (직전 밴드: 전설 500~600 / 청렴 400~499 / 만인귀감 300~399 / 우수사원 200~299 / 평범 100~199 / …).
