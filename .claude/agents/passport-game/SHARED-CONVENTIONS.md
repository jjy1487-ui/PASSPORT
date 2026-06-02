# 여권 주세요 — 공통 규약 (Shared Contract)

> 모든 프로그래머 에이전트(data-tools / gameplay / ui / qa)는 **작업 시작 전 이 문서를 먼저 읽는다.**
> 여기 정의된 데이터 스키마·네이밍·통신 규칙이 단일 진실(single source of truth)이다.
> 스키마는 **실제 엑셀 DB(`Downloads/여권_정리_updated.xlsx`, 19개 시트)에서 추출한 확정본**이다.
> 스키마 변경이 필요하면 **data-tools 에이전트에게 먼저 요청**한다.

---

## 0. 게임 한 줄 요약

여권 심사 게임(Papers, Please 스타일). **진실 서류만 저장**하고, 런타임에 `defect_rule`+`fake_value_pool`로
**결함을 주입(변조)** 한 뒤, 플레이어가 `document_requirement`/`rule_book` 규칙에 따라 통과/거절을 판정한다.
14일 × 7슬롯(총 98건) 진행. **점수와 돈은 분리** — 점수는 판정 정확도만, 돈은 상점/아이템용.

## 1. 코드 컨벤션 (기존 BattleScene 코드 계승)

- **네임스페이스 사용 안 함.** 타입은 전역. enum은 사용하는 클래스와 같은 파일, 클래스 위에 둔다.
- **ScriptableObject 데이터**: `[CreateAssetMenu(fileName="New...", menuName="Passport/...")]`,
  필드는 `public`, `[Header("한글 그룹명")]`으로 묶기, 가능하면 `=` 정렬.
- **런타임 상태**: `public T Prop { get; private set; }` 프로퍼티.
- **Manager 싱글톤**: `public static X Instance { get; private set; }` + `Awake` 중복 가드 + 앱 전역이면 `DontDestroyOnLoad`.
- **로직 ↔ UI 통신은 이벤트로만.** Manager/로직 클래스는 UI를 직접 참조하지 않는다.
  `public event Action<...> OnXxxChanged;` 로 노출하고 UI가 구독한다.
- **UI 클래스**: `[SerializeField] private` + `[Header]`, `Start()`에서 구독·`OnDestroy()`에서 해제, 모든 참조 null 체크.
- **주석**: 한글 `/// <summary>`, 섹션 구분 `// ── 제목 ──────`. **로그**: `Debug.LogWarning($"[ClassName] ...")`.
- 표현식 바디 멤버·switch 식 적극 사용.

### 1.1 DB 컬럼명 ↔ C# 필드명 규칙
- 엑셀 컬럼은 **snake_case 영문**(예: `passport_no`, `expiry_date`). C# 필드는 **camelCase**로 매핑(`passportNo`, `expiryDate`).
- 매핑 표는 data-tools가 임포트 툴 한 곳에서 관리한다. 임의 변형 금지.

## 2. 폴더 구조

```
Assets/Scripts/
  Data/         # ScriptableObject 정의 + 런타임 모델 (data-tools 소유)
  Inspection/   # 변조 엔진 + 판정 + 보조검사(xray/지문) (gameplay 소유)
  Manager/      # 일과 진행·점수·돈·상점·엔딩·세이브 (gameplay 소유)
  Dialogue/     # 대사 시퀀스 재생 로직 (gameplay 소유) / 표시는 UI
  UI/           # 검사 데스크·도장·대사창·상점·HUD·패널 (ui 소유)
Assets/Editor/  # xlsx 임포트 툴 (data-tools 소유)
Assets/Tests/   # PlayMode/EditMode 테스트 (qa 소유)
Assets/GameData/ # 임포트된 .asset
```

## 3. 데이터 스키마 계약 (실제 DB 기준, data-tools 소유)

> 컬럼명은 엑셀 row3(영문)이 확정 계약. C#은 camelCase로 매핑.

### 3.1 고객 & 진실 서류 (customer_id로 연결)
- **customer**: `customer_id`, `name_kr`, `name_en`, `nationality`, `gender`, `birth_date`, `age`, `sprite_ref`, `character_type`(문자열: "일반 고객" 등)
- **passport**: `passport_id`, `customer_id`, `passport_no`, `name_en`, `gender`, `birth_date`, `nationality`, `issue_date`, `expiry_date`, `photo_ref`
- **visa**: `visa_id`, `customer_id`, `visa_no`, `visa_type`, `nationality`, `issue_date`, `expiry_date`, `entry_type`, `memo`
- **pcr_test**: `pcr_id`, `customer_id`, `test_no`, `test_date`, `result`, `valid_until`, `lab_name`, `memo`
- **employment_cert**: `employment_id`, `customer_id`, `cert_no`, `company_name`, `job_title`, `issue_date`, `expiry_date`

### 3.2 진행 & 규칙
- **day_schedule**: `schedule_id`, `day`(1~14), `slot`(1~7), `customer_id`, `valid_chance`(슬롯 정상 확률; 1=고정통과)
- **document_requirement**: `req_id`, `document_type`, `day_from`, `day_to`, `applies_to`, `note` (그날 요구 서류)
- **rule_book**: `rule_id`, `day`, `rule_title`, `rule_content`, `related_field` (날짜별 규칙 브리핑)
- **news**: `news_id`, `day`, `news_title`, `news_content`, `icon_ref` (날짜별 뉴스 연출)

### 3.3 변조 (진실 → 결함)
- **defect_rule**: `rule_id`, `character_type`, `context`, `defect_document`, `violation_field`, `normal_example`, `defect_example`, `secondary_check`, `random_valid_chance`, `note`, `corruption_type`(EXPIRE 등), `target_field`
- **fake_value_pool**: `pool_id`, `field`, `fake_value`, `note`

### 3.4 보조 검사 (정답/결함 보강)
- **xray**: `xray_id`, `customer_id`, `result`(적발 등), `detected_item`, `hidden_location`
- **fingerprint**: `fingerprint_id`, `customer_id`, `result`, `match_status`, `matched_person`

### 3.5 대사 (나레이티브)
- **dialogue_case**: `dialogue_case_id`, `schedule_id`, `customer_id`, `case_type`, `character_type`, `document_type`, `doc_valid`, `violation_field`, `game_result`, `reject_count`
- **dialogue_line**: `line_id`, `dialogue_case_id`, `line_order`, `speaker`(캐릭터/검문관 등), `text_kr`

### 3.6 경제 & 엔딩
- **shop**: `shop_item_id`, `item_name`, `category`, `price`, `unlock_day`, `effect_type`(MAGNIFY 등), `effect_value`, `effect`
- **reward**: `reward_id`, `reward_type`, `trigger_type`, `trigger_condition`, `amount`, `related_ending_id`, `memo`
- **ending**: `ending_id`, `ending_type`, `ending_name`, `score_min`, `score_max`, `end_timing`, `trigger_type`, `trigger_condition`
- **score_model**(key-value config): 아래 4장 참조

### 3.7 런타임 모델 (변조 결과, gameplay가 생성) — **필드 딕셔너리 기반**
> 서류의 개별 필드는 **고정 C# 필드로 박지 않고 키-값 딕셔너리로** 다룬다(6장 변경 대응 원칙).
> 그래야 컬럼이 추가/삭제돼도 변조·판정·표시 로직이 안 바뀐다.

- **InspectionCase** — 손님 1명 심사 케이스.
  `int customerId`, `bool isNormal`(슬롯 `valid_chance` 1회 판정), `List<DocumentInstance> documents`(변조 사본),
  `Verdict correctVerdict`, `int rejectCount`(현재 재심사 라운드 0~3), `string currentCaseType`/`gameResult`(대사 선택 키),
  연결 `dialogue_case`/xray/fingerprint 결과. (4-A 대화 라운드 상태머신이 이 값을 갱신)
- **DocumentInstance** — 화면에 보일 서류 1장(진실 복제 + 결함 0~1개).
  - `string documentType` — 문자열("여권"/"비자"/"pcr_test"…), enum으로 박지 않음.
  - `Dictionary<string,string> fields` — **컬럼명(키) → 값**. 진실값을 복제 후 결함 시 한 키만 교체.
  - `Dictionary<string,FieldMeta> meta` — 표시명(한글)·자료형(date/text)·검사대상 여부 등(임포트 시 data-tools가 채움).
  - `bool hasDefect`, `string defectField`(변조된 키), `string corruptionType`.
- enum 중 **안정적인 것만 타입 고정**: `enum Verdict { Approve, Reject }`, 점수/돈 종류.
  `document_type`/`character_type`/필드명은 **문자열 데이터 그대로** 쓴다(변동이 크므로).

## 4. 핵심 게임 규칙 (코드가 반드시 지킬 것)

1. **슬롯 단위 확률 1회.** `day_schedule.valid_chance`로 정상/비정상을 손님 등장 시 **한 번만** 결정.
2. **비정상이면 결함 서류 1개·결함 1개.** `defect_rule`(character_type/context 매칭) + `fake_value_pool`로 `corruption_type`에 따라 `target_field` 한 곳만 변조. 나머지 진실 그대로.
3. **정답 = isNormal ? Approve : Reject.** 보조검사(xray/fingerprint) 적발 건은 거절이 정답.
4. **요구 서류만 판정 반영** (`document_requirement`의 day_from~day_to, applies_to).
5. **점수 ≠ 돈 (분리).**
   - 점수(엔딩용): `JUDGE_CORRECT +10`, `JUDGE_WRONG −15`. 공식 `점수 = 2450 × 정확도 − 1470`.
   - 돈(상점용): `DAILY_BASE +100`, `DETECTION +30`, `PERFECT_DAY +50`, `WARNING −50`.
   - 엔딩은 `ending` 테이블 score_min~score_max 구간으로 결정(70% 정확도 → +245 → 우수 사원 200~299, 100% → 전설의 검문관 500+).
6. **결정론적 재현.** 변조·확률은 `int seed` 주입 가능해야 함(테스트·QA 재현용).

### 4-A. 대화 라운드 & 재심사 루프 ("대화 3회" 조건) — gameplay 소유
> 데이터 근거: `dialogue_case`의 `case_type`/`doc_valid`/`violation_field`/`game_result`/`reject_count`, `dialogue_line`(라운드별 대사).

한 손님의 심사는 **대사 시퀀스 상태머신**으로 진행한다:
1. **입장**(case_type=`입장`) 대사 재생.
2. **심사 라운드**: 플레이어가 서류를 **구분·대조**(여러 서류 교차 확인) 후 통과/거절 판정.
3. 판정 결과로 **다음 `dialogue_case`를 키로 선택**: `(schedule_id, customer_id, case_type, doc_valid, game_result, reject_count)`.
4. **분기**:
   - 판정이 정답(정상 승인 / 정상 거절) → **확정**, 점수·돈 정산 후 다음 손님.
   - **정상 고객을 잘못 거절**(game_result=`잘못 거절`) → 손님 항의 대사 재생, `reject_count += 1`, **다시 심사 라운드로 루프**.
   - 잘못 허가 등 다른 오판 → 해당 `game_result` 케이스로 확정(오판 점수).
5. **최대 3회**: `reject_count`가 **3에 도달하면 강제 종료**(데이터의 reject_count=3 케이스 = 심사관이 정정·통과). 3을 초과하는 루프는 없다.
6. **점수·돈은 "확정" 시점에 1회만** 적용한다(루프 중 중복 정산 금지). 최종 `game_result` 기준.

> 핵심: "대화 3회"는 **잘못 거절 시 재심사 기회가 최대 3라운드**라는 뜻. 이 루프와 reject_count 분기가 구현되지 않으면 대사가 1회로 끝나 조건이 "수행되지 않는" 상태가 된다.

## 5. 에이전트 간 인터페이스 (핸드오프 경계)

```
data-tools ──(ScriptableObject 스키마 + 임포트된 .asset)──▶ gameplay
gameplay   ──(InspectionCase + event OnCaseReady/OnVerdictResolved/OnDayStarted/OnMoneyChanged/OnScoreChanged + 대사 시퀀스 이벤트)──▶ ui
ui         ──(플레이어 입력: SubmitVerdict, UseTool(xray/지문), BuyItem 등)──▶ gameplay
gameplay/data-tools ──(공개 API + seed)──▶ qa (시뮬·검증)
```

- **gameplay는 UI를 참조하지 않는다.** event로만 통지.
- **ui는 게임 규칙을 모른다.** 정답 판정·변조를 UI에 두지 않는다. 표시와 입력 전달만.
- **스키마 필드명 변경은 data-tools만.** 변경 시 이 문서를 갱신하고 통지.

## 6. 스키마 변경 대응 원칙 (데이터는 또 바뀐다)

> 전제: 엑셀 데이터 테이블은 **앞으로도 수정될 수 있다.** 변경이 와도 **data-tools 재임포트 한 번**으로 끝나야 한다.
> 한 컬럼 바뀐다고 gameplay/ui/qa가 줄줄이 깨지면 설계 실패다.

원칙:
1. **변경 흡수는 한 곳 — 임포트 툴.** snake_case 컬럼 → 런타임 키-값 매핑은 임포터만 안다. 컬럼명/추가/삭제는 임포터 + 이 문서만 고친다.
2. **안정 vs 변동 분리.**
   - 안정(잘 안 바뀜) = **타입 고정**: `Verdict`, 진행 루프, 점수/돈 종류, 에이전트 인터페이스(이벤트 시그니처).
   - 변동(자주 바뀜) = **데이터 주도/키-값**: 서류 필드(`DocumentInstance.fields`), `defect_rule`(corruption_type/target_field), `fake_value_pool`, character_type 문자열.
3. **변조·판정은 필드명을 문자열로 다룬다.** `defect_rule.target_field`로 `fields[target_field]`를 교체하고, 판정도 키로 검사 → **새 필드가 생겨도 로직 무수정**으로 흐른다.
4. **손으로 .asset 편집 금지.** 항상 xlsx → 재임포트. 데이터 변경 = 재임포트 1회.
5. **변경 프로토콜**: data-tools가 ① 임포터 매핑 수정 ② 이 문서(3·4장) 갱신 ③ 영향 키/이벤트를 명시해 통지. **이벤트 시그니처가 바뀌면** ui/qa에 반드시 통지(여기가 유일하게 전파되는 지점).
6. **재임포트 안전**: 임포터는 idempotent(같은 입력 → 같은 .asset). 부분 변경도 전체 재생성으로 처리.

## 7. Unity-MCP / 환경 워크플로

- 환경: Windows / PowerShell. 경로는 `/`.
- 스크립트 생성·수정 후 **반드시 `read_console`로 컴파일 에러 확인**, `editor_state.isCompiling`이 false 될 때까지 대기.
- 새 컴포넌트/타입은 컴파일 성공 후에만 사용. 새 씬엔 Camera + Directional Light 포함.

## 8. 완료 기준 (DoD) — 공통

- [ ] 컴파일 통과, 콘솔 Error 0
- [ ] 이 문서의 스키마(실제 DB 컬럼명)·통신 규칙 준수
- [ ] 담당 폴더 밖 수정 시 사유 명시
- [ ] (해당 시) QA가 검증 가능한 public API/seed 제공
