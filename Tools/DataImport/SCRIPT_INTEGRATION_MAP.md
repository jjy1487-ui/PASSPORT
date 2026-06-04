# 방문객 스크립트(260604) ↔ 기존 데이터 시스템 통합 매핑 (data-tools 소유)

> 소스 대본: `Downloads/dayeon_data/방문객_스크립트_전체_260604.xlsx` (15시트)
> 타깃 런타임: `Assets/Resources/GameData/{branch_dialogue,rejection_templates,interrogation}.json`
> 빌더: `Tools/DataImport2/build_aux_data.py` (대사·거부템플릿) + `Tools/DataImport/build_interrogation.py` (취조 Q&A, 신규)
> 규약: SHARED-CONVENTIONS.md 3.5 / 3.6 / 3.9 / 4-A 절.
>
> **결론 요약:** 이 대본은 신규 데이터가 아니라 **기존 콘텐츠 레이어의 갱신본**이다. 대사·거부템플릿
> 임포트 인프라(`build_aux_data.py`)가 이미 이 엑셀을 1차 소스로 쓰고 있고, 현재 산출 JSON과 **0-diff(idempotent)**
> 로 일치한다. 따라서 대사·거부는 **재실행만으로 손실 없이 최신화**된다. 유일한 신규 작업은 **취조 Q&A 시트의
> JSON화(4-A 데이터)** 와 본 매핑 문서·규약 동기화다. 비가역/손실 결정 없음.

---

## 0. 소스 파일 주의 (중요)

| 경로 | 시트 | 상태 |
|---|---|---|
| `Downloads/방문객_스크립트_전체_260604.xlsx` | 15 | **구버전**(65,549B). 진상/연예인·정치인 톤 미패치. |
| `Downloads/dayeon_data/방문객_스크립트_전체_260604.xlsx` | 15 | **최신 단일 소스**(67,500B). 빌더가 읽는 파일. 정치인 화법 패치 반영. |

- 두 파일은 **진상 고객 2행 / 연예인·정치인 ★ 16행**만 다르다. dayeon_data 쪽이 정치인 톤을 일반 손님과 차별화한
  최신본이며, `Tools/DataImport/patch_politician_tone.py` 의 결과로 보인다. **dayeon_data 버전이 정본.**
- `build_aux_data.py` 의 `SCRIPT_XLSX` 가 dayeon_data 경로로 고정되어 있어 정본을 읽는다(변경 불필요).

## 1. 캐릭터 시트 ↔ character_type 매핑

대본 13개 캐릭터 시트는 `branch_dialogue_map.py:TYPE_MAP` 으로 day JSON 의 `characterType` 과 연결된다(이미 구현·검증됨).

| 대본 시트(branch characterType) | day JSON characterType | 비고 |
|---|---|---|
| 일반 고객 | 일반 고객 | |
| 진상 고객 | 진상 고객 | |
| 외국인 관광객 | 외국인 관광객 | `[영어]` 언어 변형 포함 |
| 전염병 환자 | 검역 대상자(PCR) | defect_variant 1-A/1-B/1-C/1-D |
| 외국인 장기체류자 (취업) | 취업체류자 | |
| 외국인 장기체류자 (대학) | 장기체류자 | |
| 성형 수술 고객 (일반) | 성형 의심 고객 | 분기 A~H |
| 성형 수술 고객 (범죄자) | 범죄자(성형수술) | 분기 A~G |
| 범죄자 | 범죄자(외국 도피자) / 범죄자(국내 유입자) | `branchLabel` 의 `[외국 도피자]`/`[국내 유입자]` 키워드로 세분(`CRIMINAL_BRANCH_KEY`) |
| 테러범 | 테러범 | |
| 연예인·정치인 ★ | 특수(연예인)★ / 특수(정치인)★ | `══ 연예인 ★ ══`/`══ 정치인 ★ ══` 구획(`section`)으로 분리(`SECTION_FILTER`) |
| 사이비 신도 | (특수) — day JSON 미배치 | `══ N회차 ══` 구획. visit_round 1/2/3 |
| 꼬마·현자 ★ | 특수(현자)★ + 꼬마 | `══ 꼬마 ★ ══`/`══ 현자 ★ ══` 구획 |

> 매핑은 단일 지점(`TYPE_MAP`/`SECTION_FILTER`/`CRIMINAL_BRANCH_KEY`)에서만 관리한다(규약 6장).

## 2. 분기(▶/══) ↔ branch_key 카탈로그 매핑

대본의 분기 헤더는 `branch_dialogue.json` 에 `branchLabel`(원문)+`gameResult`(정규화)로 보존된다.
판정 확정 시 점수/금액 조회용 `branch_key`(BRANCH_CATALOG.md, 41종)는 **게임플레이가 상태머신 결과로 결정**하며,
data-tools 는 대사 라벨 → branch_key 매핑 후보만 명시한다(아래). **신규 branch_key 0종** — 41종으로 전부 커버됨.

| 대본 분기 라벨(요지) | gameResult | branch_key (기존) |
|---|---|---|
| 서류 정상 → 입국 (정답) / 즉시 입국 | 정상 승인 | `approve_correct` / `approve_immediate` |
| 서류 정상 → 거부 (오판) | 잘못 거절 | `reject_wrong` |
| 서류 불량 → 거부 (정답) | 정상 거절 | `reject_correct` |
| 서류 불량 → 입국 (오판) | 잘못 허가 | `approve_wrong` |
| N회 거절 후 입국 / 강제 입국 (연예/정치) | 정상 승인 | `approve_after_reject_1/2/3` |
| 성형: 지문/마스크 미요청 → 입국 (오판) | 잘못 허가 | `approve_wrong` (defect_variant=지문/마스크 미요청) |
| 성형: 몽타주 일치 + 범인 아님 → 입국 (정답) | 정상 승인 | `approve_correct` (우수 사원 호칭) |
| 성형범죄자: 몽타주+X-ray+거부 (최선) | 정상 거절 | `detect_montage_xray_reject` |
| 성형범죄자/범죄자: 몽타주 미인식 → 단순 거부 | 정상 거절 | `reject_lucky` |
| 성형범죄자/범죄자: 제안 응함+금괴/마약+조기엔딩 | (조기엔딩) | `corrupt_accept_gold` / `corrupt_accept_drugs` |
| 범죄자: 몽타주 인식 → 거부 (정답) | 정상 거절 | `detect_montage_reject` |
| 테러범 10분기 (상담/신고/제압/X-ray) | 혼합 | `terror_*` 10종 |
| 사이비 신도 회차별 문답 조합 | (회차) | `cult_*` 8종 (visit_round로 구분) |
| 연예인/정치인 마스크·대리·논란 | 정상 승인/누적 | `mask_request_turn_*` / `proxy_self_request` / `scandal_third_turn` |
| 현자 가치관 3단 조합 → 입국+아이템 | 정상 승인 | `approve_sage_item` (defect_variant=가치관조합) |
| 꼬마 발판 제공/미제공 → 입국 | 정상 승인 | `approve_with_step` / `approve_no_step` |

> 라벨 텍스트 → branch_key 의 기계 매핑은 게임플레이 상태머신 소관(본 표는 설계 근거). data-tools 의
> `character_score`/`character_payout` 는 이미 동일 branch_key 어휘로 채워져 있다(규약 3.9).

## 3. 대사/액션 라인 → dialogue 스키마 변환 규칙

### 3.1 화자(speaker) 매핑 — `build_aux_data.py:SPEAKER_MAP`
| 대본 캐릭터 값 | JSON speaker | day JSON 표시(규약 3.5) |
|---|---|---|
| 방문객 | `캐릭터` | 빌드 시 손님 실제 이름(`customer.name_kr`)으로 치환(`build_days.localize_speakers`) |
| 검문관(플레이어) | `심사관` | 불변(`심사관`) |
| [시스템] | `시스템` | 연출/액션 라인 (아래 3.2) |

### 3.2 액션 라인(`[UI 등장]`/`[모션]`/`[시스템]`/`[사전 이벤트]`) 처리 — **결정**
- **결정: 액션 라인은 별도 필드로 분리하지 않고, `line.text` 에 마커 원문 그대로 보존한다.**
- 근거:
  1. 기존 파이프라인(`branch_dialogue_map._first_quote`)이 **발화 라인만 `「...」` 인용문으로 추출**하고
     액션 라인(인용문 없음)은 자동 무시한다. 즉 대사 주입 시 액션 라인은 자연히 걸러지므로 별도 필드가 불필요하다.
  2. `speaker=시스템` + 마커 텍스트로 **연출 메타가 라인 단위로 식별 가능**하다(UI/연출이 `speaker=="시스템"` 또는
     `text.startswith("[")` 로 분기). 별도 스키마 컬럼을 만들면 변경 흡수 지점이 늘어 규약 6장에 역행.
  3. 손실 0: 마커 원문(`[UI 등장]`, `[모션] 서류 드롭`, `[사전 이벤트] ... 신문 기사`)이 그대로 남아
     연출 구현 시 참조 가능.
- 핸드오프: 액션 라인을 **실제 연출/모션/UI 트리거로 해석**하는 것은 gameplay/ui 소관(아래 6장).

### 3.3 언어 변형(`[영어]`) 처리 — **결정**
- **결정: 한국어 발화만 런타임 대사로 채택하고, 영어 변형은 라이브러리(JSON)에 보존하되 주입에서 첫 언어만 사용.**
- 근거: `branch_dialogue_map.index_branch` 가 `[영어]`/`[일본어]`/`[중국어]` 언어 그룹 헤더를 카운트하여
  **두 번째 언어 그룹부터 무시**(`lang_markers >= 2`)하고, 인용문 안의 `원문\n(번역)` 은 첫 줄만 취한다.
  외국인 관광객은 영어 원문이 1번째 그룹이라 영어 발화가 채택된다(설계 의도대로).
- 손실 없음: 추가 언어 변형 전체가 `branch_dialogue.json` 에 보존되어, 다국어 UI 확장 시 재사용 가능.

### 3.4 데이터 흐름 (idempotent)
```
대본 xlsx ─ build_aux_data.py ─▶ branch_dialogue.json (13 types, section/branchLabel/gameResult/lines)
build_days.py ─▶ dayN.json 케이스(placeholder)
branch_dialogue_map.py ─▶ 케이스에 「...」 발화 주입 (구조 불변, text만 교체)
authored_lines.py ─▶ 남은 [TODO] 슬롯 폴백 (day1 톤)
```
- 현재 `branch_dialogue.json`/`rejection_templates.json` 은 정본 엑셀과 **0-diff** → 재빌드해도 변화 없음(검증 완료).

## 4. 취조 질문 스크립트 → 4-A 대화 라운드 매핑 — **신규 데이터**

대본 `취조 질문 스크립트` 시트(오류유형 4종 × 답변유형 3종 = 12행)는 **현재 어떤 JSON에도 변환되지 않았다**(빌더 skip).
4-A 대화 라운드(취조 Q&A, 재심사 루프)의 데이터 근거가 되므로 **신규 JSON `interrogation.json`** 으로 임포트한다.

| 취조 시트 컬럼 | interrogation.json 필드 | 비고 |
|---|---|---|
| 오류유형 | `errorType` | inspection_notice.error_type 어휘로 정규화(정보불일치/사진불일치/기간오류→기간만료/위조) |
| (오류유형 괄호 항목) | `violationField` | `name`/`face`/`expiry_date`/`passport_no` (속성 키 어휘, 규약 3.8) |
| 질문 대상 | `target` | 방문객 |
| 검문관 질문 (5단계) | `inspectorQuestion` | 「...」 발화 |
| 답변 유형 | `answerType` | `시인`/`부인`/`회피` (4-A 분기 키) |
| 방문객 답변 (5-1단계) | `visitorAnswer` | 「...」 발화 |
| 결과 판정 | `outcome` | 거부 근거 확보 / 추가 검토 필요 / 의심 강화 |
| 비고 | `note` | |

- **inspection_notice 와의 정합:** 취조 오류유형 4종(정보불일치/사진불일치/기간만료/위조)은
  inspection_notice 의 error_type·violation_field 와 1:1 정렬된다. 취조에서 거부 근거 확보 시
  해당 inspection_notice 행이 발부될 citation 후보가 된다.
- **4-A 상태머신 연결(게임플레이 소관):** 취조는 심사 라운드 내 **선택적 서브 시퀀스**다.
  `answerType` 3분기(시인=거부 근거 확보 / 부인=추가 검토 필요 / 회피=의심 강화)가 라운드 전이 신호.
  data-tools 는 Q&A 텍스트와 분기 키만 제공하고, 라운드 루프·reject_count 갱신은 gameplay 가 구현.

## 5. 거부 대사 템플릿 → inspection_notice 매핑

대본 `거부 대사 템플릿`(4행: 정보불일치/사진불일치/기간오류/위조)은 `build_aux_data.build_rejection_templates`
가 `rejection_templates.json` 으로 변환(이미 idempotent). 메인 DB `inspection_notice`(9행) 와의 관계:

| 거부 템플릿(problemType) | inspection_notice error_type | violation_field |
|---|---|---|
| 정보불일치 (이름/성별/생년월일/국적) | 정보불일치 | name |
| 사진불일치 (사진) | 사진불일치 | face |
| 기간오류 (발급일/만료일) | 기간만료 | expiry_date |
| 위조 (여권번호) | 위조 | passport_no |

- 거부 템플릿은 **거절 시 손님에게 들려줄 대사 A(단호)/B(정중)** 이고, inspection_notice 는 **발부하는 고지서(citation) 본문**이다.
  두 소스는 error_type 으로 조인된다. 거부 템플릿 4종은 inspection_notice 9종의 **부분집합**(여권 4종)이며,
  나머지 5종(비자 정보불일치/PCR 검사부적합/서류누락/위험물반입/신원위조)은 고지서만 있고 음성 거부 대사는 공통 템플릿 사용.
- **손실 없음:** rejection_templates.json 은 음성 대사 전용으로 유지하고, inspection_notice 는 메인 DB 그대로(불변).

## 6. 순수 서류결함 모델 밖 특수 메커닉 — gameplay/ui 핸드오프 목록

아래는 데이터(대사/분기/점수)는 준비됐으나 **상태머신/UI 확장이 필요**한 항목이다. 이번 작업 범위(데이터)에서 제외하고 목록화만 한다.

| 캐릭터 | 특수 메커닉 | 필요한 확장 | 데이터 근거 |
|---|---|---|---|
| 성형 의심/범죄자 | 지문 일치/불일치 요청, 마스크 벗기 요청, 몽타주 대조 | 지문·몽타주 비교 서브액션, 마스크 토글 | `fingerprint` 시트, branch A~H, `detect_montage_*` |
| 성형범죄자 | X-ray 밀반입 적발 | X-ray 검사 잠금해제(이미 SCAN_TRIGGERS 인프라 존재) | `xray` 시트, `detect_montage_xray_reject` |
| 테러범 | 상담 마스터 분기(달램/설교/신고/제압), 시한폭탄 타이머 | 다단 대화 상태머신 + 타이머 이벤트 | 테러범 10분기 `terror_*` |
| 사이비 신도 | 1/2/3 회차 누적 + 문답 조합(예/아니오), 세뇌 엔딩 | visit_round 카운터 + 조합 평가 + 조기엔딩 #13/#14 | 사이비 3구획, `cult_*` |
| 꼬마 | 발판 제공 선택(카운터 빈 화면 → 발판) | UI 발판 토글 + 아이템 드롭 | 꼬마 2분기, `approve_with_step`/`approve_no_step` |
| 현자 | 가치관 3단 문답(8조합) → 마법 아이템 1:1 | 문답 누적 + 아이템 매핑 + 누적 8회 호칭 | 현자 문답, `approve_sage_item` |
| 연예인/정치인 | 마스크 벗기 요청 누적(3턴) → 인성 논란 다음날 이벤트, 매니저/보좌관 대리 | 턴 카운터 + 다음날 이벤트 트리거 | `mask_request_turn_*`/`scandal_third_turn`/`proxy_self_request` |
| 범죄자/성형범죄자 | 뇌물 제안 응답 → 금괴/마약 수령 → 조기엔딩 #11/#12 | 뇌물 분기 UI + 조기엔딩 트리거 | `corrupt_accept_*`, early_ending #11/#12 |
| 취조(공통) | 취조 Q&A 서브 시퀀스(시인/부인/회피) → 라운드 전이 | 4-A 라운드 내 취조 분기 소비 | `interrogation.json`(신규) |

> 점수/금액/호칭/아이템/조기엔딩 트리거 값은 `character_score`/`character_payout`(메인 DB) + BRANCH_CATALOG.md 에 이미 존재.
> 게임플레이는 상태머신으로 branch_key 를 산출 → 그 키로 점수/금액 행을 조회한다.

## 7. 충돌/손실 평가

| 항목 | 평가 |
|---|---|
| 기존 dialogue 덮어쓰기 | **덮어쓰기 없음.** branch_dialogue.json 은 이미 이 엑셀에서 생성된 idempotent 산출물(0-diff). 재빌드해도 동일. |
| 메인 DB dialogue_case/dialogue_line(43/103행) | **불변.** 대본 콘텐츠는 `branch_dialogue.json`(런타임 라이브러리)으로만 흐르고 메인 DB 시트는 손대지 않는다. |
| 점수 페이싱 곡선 / 엔딩 밴드 | **영향 없음.** 점수는 character_score(메인 DB) 기반, 본 작업은 대사/취조 콘텐츠만 추가. ENDING_SCORE_BANDS 불변. |
| 취조 JSON 신규 | **순수 추가.** 기존 파일 변경 없음, 손실 0. |
| 비가역 결정 | **없음.** 모든 산출물 idempotent, 타임스탬프 백업 후 재생성 가능. |

## 8. 산출/수정 파일

- **신규**: `Tools/DataImport/build_interrogation.py` → `Assets/Resources/GameData/interrogation.json`
- **재실행(idempotent, 0-diff 확인)**: `Tools/DataImport2/build_aux_data.py` → `branch_dialogue.json`, `rejection_templates.json`
- **문서**: 본 파일 + SHARED-CONVENTIONS.md 3.5/4-A 절 동기화
