# 여권 주세요 — 진행 현황 & 다음 단계 (핸드오프)

> 새 세션은 이 문서를 먼저 읽고 이어서 작업한다. 상세 규칙은 `.claude/agents/passport-game/` 문서 참조.

## 프로젝트 개요
- 장르: 여권 심사 게임(Papers, Please 스타일). 14일 × 7슬롯(98건) 심사.
- 핵심: **진실 서류만 저장 → 런타임 변조(defect_rule+fake_value_pool) → 플레이어가 통과/거절 판정**.
- 위치: `C:\Users\chris\Documents\produc_build_reecture` (Unity **6000.4.6f1**)
- 저장소: `github.com/jjy1487-ui/produc_build_reecture`, 작업 브랜치 **`재영`** (모든 작업이 여기 있음. `메인`은 빈 껍데기)

## 데이터 (완료)
- 원본: `C:\Users\chris\Downloads\여권_정리_updated.xlsx` (**19시트** 관계형 DB)
  - customer / passport / visa / pcr_test / employment_cert / day_schedule / document_requirement /
    rule_book / news / defect_rule / fake_value_pool / xray / fingerprint / dialogue_case / dialogue_line /
    shop / reward / ending / score_model
- 엑셀 구조: row1=PK/FK, row2=자료형, **row3=영문 컬럼명(매핑 기준)**, row4=한글, row5~=데이터
- 설계 보조 md: `Downloads\여권주세요_날짜별_방문고객_랜덤정리.md`

## 만든 산출물 (서브에이전트 + 규약) — 커밋 `84ab6a3`
- `.claude/agents/passport-game/SHARED-CONVENTIONS.md` — **단일 계약**: 19시트 스키마, 게임 규칙, 키-값 변경 대응, 대화 3회 루프
- `.claude/agents/passport-game/UI-CONVENTIONS.md` — UI 일관성 규약 + 데이터→UI 요소 매핑(10장)
- `.claude/agents/passport-data-tools.md` — 데이터/툴 (스키마 소유자, xlsx 임포터)
- `.claude/agents/passport-gameplay.md` — 변조·판정·진행·경제·대화 라운드 상태머신
- `.claude/agents/passport-ui.md` — 검사 데스크·재심사 루프 UI·상점·정산
- `.claude/agents/passport-qa.md` — 규칙·결정론·점수공식·페이싱 검증

## 핵심 설계 결정 (요약)
- **직무별** 프로그래머 에이전트 4종(data-tools/gameplay/ui/qa). 기획·아트는 코드 산출물 아니라 제외.
- **변경 강건**: 서류 필드는 고정 C# 멤버 금지, `DocumentInstance.fields`(키-값)로. 데이터 바뀌면 임포터만 수정 → 재임포트.
- **점수 ≠ 돈**: 점수=판정만(JUDGE_CORRECT+10/WRONG−15, 공식 2450×정확도−1470, 엔딩 점수구간). 돈=DAILY_BASE/DETECTION/PERFECT_DAY/WARNING(상점용).
- **대화 3회 재심사 루프**(SHARED-CONVENTIONS 4-A): 정상 고객을 잘못 거절 시 손님 항의 → reject_count++ → 재심사, **최대 3회**(3 도달 시 강제 정정 통과). 점수·돈은 "확정" 시 1회만. ← **"수행 안 되던" 핵심, 구현 필요.**
- UI: PC 가로 1920×1080. 닫기=X·우상단, 통과=좌/거절=우 고정. 색·간격은 UITheme(SO). 단축키 통과 F·거절 J·도구 1/2/3.

## 미해결 / 다음 단계
1. (진행 중) GitHub 기본 브랜치를 `재영`으로 변경 — Settings→Branches에서 수동.
2. **Unity로 이 프로젝트 열고 mcp-unity 브리지 연결** (구현 검증에 필요. 현재 연결 인스턴스 0).
3. 구현 순서(의존 방향): **data-tools → gameplay → ui → qa**
   - data-tools: 19시트 → ScriptableObject(키-값) + Editor 임포터(`Tools/Passport/Import Data`), idempotent.
   - gameplay: 변조 엔진(seed 결정론) → 판정 → **대화 3회 루프** → 점수/돈 정산.
   - ui: UI-CONVENTIONS 기준으로 검사 데스크 + 재심사 루프 표시.
   - qa: 규칙·결정론·점수공식·14일 페이싱 검증.

## 새 세션 사용법
> 예: "PROGRESS.md 읽고, Unity 연결됐으면 passport-data-tools부터 시작해."
- 에이전트는 `.claude/agents/`에 있으므로 이름으로 바로 호출 가능.
- 절대경로 대신 `.claude/agents/passport-game/*.md` 상대경로로 규약 참조.
