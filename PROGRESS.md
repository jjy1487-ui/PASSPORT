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

## data-tools 단계 (코드 작성 완료, Unity 검증 보류) — 2026-06-02
> xlsx 수집 방식 **확정(사용자 승인)**: Python → 중간 JSON → C# 임포터 (NPOI/EPPlus DLL 아님).
- `Tools/DataImport/xlsx_to_json.py` — 19시트 → 단일 `Assets/GameData/_source/GameData.source.json`.
  시트별 헤더 레이아웃 3종(relational row3=영문 / flat row1=영문 / config 한글헤더) 자동 감지, snake_case 키 보존, 한글 label·자료형은 `columns` meta로 보존. idempotent.
- `Tools/DataImport/validate_data.py` — 무결성 검증 → `_validation_report.md`. **오류 0건**(98건 채움, FK 유효, 확률 0~1).
- `Assets/Scripts/Data/` — `DataCore.cs`(Verdict, FieldMeta, DataRow 키-값, DataTableAsset 베이스), `DataTables.cs`(19테이블 SO), `GameDatabase.cs`(단일 진입점), `RuntimeModels.cs`(DocumentInstance/InspectionCase 형 정의, 생성은 gameplay).
- `Assets/Editor/` — `PassportDataImporter.cs`(메뉴 `Tools/Passport/Import Data`, JSON→.asset 19개+GameDatabase, idempotent), `MiniJson.cs`(의존성 없는 파서).
- 기존 `Assets/Scripts/Inspection/` day1.json 프로토타입은 **건드리지 않음**(gameplay/ui 소유). SO 타입명은 `~Table`로 분리해 충돌 회피.

### gameplay가 반드시 반영할 데이터 특수 케이스
- defect_rule rule_id=6 `random_valid_chance="고정(통과1/거절0)"` 비수치 → float 파싱 실패 시 "고정 케이스" 특수 처리.
- defect_rule 일부 `corruption_type`/`target_field` 복수값(`A | B`, `result / lab_name`) → 구분자(`|`,`/`) 파싱 규칙 정해 하나 선택("결함 1개" 규약 4.2와 정합).

## 1~14일차 구현 (baked JSON 방식, 컴파일 검증 완료) — 2026-06-02
> 실제 게임 런타임은 **사전제작 per-day JSON**(`Resources/GameData/dayN.json`, 스키마=`Day1Data`) 기반.
> data-tools의 SO 파이프라인(진실+런타임변조)과는 별개 경로 — SO는 원천 데이터 저장소로만 유지, 이 씬엔 미사용.
- **대화 처리 결정(사용자 승인)**: 2~14일은 서류·결함·진행 자동생성, **대화는 최소 플레이가능 공통 라인(입장/승인/거절/오거부1·2·3/오허가)**만. 각 라인에 `[TODO 대사]` 표식. 대사 완성은 후속(xlsx dialogue 시트 보강 후 재생성 권장).
- `Tools/DataImport/build_days.py` — `GameData.source.json` → `day2~14.json` 13개 생성(idempotent). day1.json은 수작업 완성본이라 미변경.
  - 확률 슬롯(0<valid_chance<1)은 **고정 시드 20260602**로 결정론적 resolve. ==1 정상 / ==0 비정상.
  - 비정상 슬롯마다 defect_rule(character_type 매칭)로 **결함 정확히 1개** 주입(복수 target_field는 시드로 1개 선택). 요구서류는 document_requirement 기준(여권 전일 / 비자 day3+ 외국인 / PCR day5~7 검역 / 취업증빙 day8~14 {취업체류자,장기체류자}).
- 런타임 일반화(gameplay): `GameDataLoader.Load(int day)`, `ImmigrationManager`가 현재 일차 보유 + `BeginDay`로 1→…→14 진행(골드 일자간 누적), 14일 종료 시 "전체 일정 종료"(엔딩 TODO). `InspectionController.OnDayCompleted` 이벤트로 통지. 심사/대화 3회 루프 로직은 미변경.
- **컴파일 검증 완료**: Unity 연결됨, 전체 프로젝트 Error 0(SO 파이프라인 포함).

### 남은 TODO (1~14일 관련)
- 2~14일 대화 본문 작성(현재 placeholder + `[TODO 대사]`).
- 14일 종료 → 점수 정산(2450×정확도−1470) + `ending` 테이블 구간 분기 → 엔딩 화면. 현재는 종료 로그만.
- 점수≠돈: 컨트롤러는 아직 골드만 ±. 점수 누적/엔딩 연동 미구현.
- 플레이 검증: ImmigrationScene에서 1→14 진행 실제 플레이테스트(미실시).

## 문서 대조 + 대화 요청 시스템 (스크립트 작성, 씬 와이어링 보류) — 2026-06-02
> 사용자 결정: 대조=**필드 단위 클릭 + 보조 도구**(판정 무관, 일치/불일치 표시만) / 대화=**플레이어 요청형 버튼**(선형 데이터) / 고객 초상=**이름만**(국적·성별·유형 숨김 — 대조의 전제).
- `CustomerView.cs` — 초상 영역 이름만 표시(`_infoText` 숨김). **완료.**
- 신규(`Assets/Scripts/Inspection/`): `DocumentFieldView`(클릭 필드 행), `CrossCheckController`(카드 간 두 필드 선택+정규화 비교, 표시 전용), `CrossCheckResultView`(일치/불일치 뱃지), `DialogueRequestButton`('대화/심문' 버튼).
- `DocumentCardView`/`DocumentView` — 필드 행 생성·노출(미연결 시 기존 한 덩어리 폴백). `InspectionController` — 훅만 추가(`OnRequestableChanged`/`CanRequestDialogue`/`RequestDialogue`). **판정·오거부 3회 루프·점수 로직 불변(검증 완료).**
- **씬 와이어링·플레이 검증 완료(2026-06-02, Unity 연결됨)**: FieldRow 프리팹·FieldContainer·CrossCheckResultPanel(닫기 X 우상단)·CrossCheckController·DialogueRequestButton 배치+바인딩, ImmigrationScene 저장. 절차 기록은 `WIRING_GUIDE_crosscheck_dialogue.md`.
  - 컴파일 Error 0. Play(day7=베트남 검역, 서류 3장) 검증: 서류가 필드 행으로 렌더(여권9/비자7/PCR6), CustomerView 이름만(국적·성별 숨김), 대조 **국적 VNM↔VNM=일치 / 만료일 2031↔2027=불일치** 정상, 결과 패널·뱃지 동작, 판정·점수 영향 없음. 런타임 에러 0.
  - 참고: Canvas가 ScreenSpaceOverlay라 카메라 직접 스크린샷엔 UI 미포함(기능 무관). **씬 startDay가 현재 7**(디버그 점프 상태) — 정식 플레이는 1로.
- TODO: UITheme/도구바 도입 시 색·도구 토글 연결. 대조 '근거 필수' 심화는 후속. day2~14 대화 본문(현 placeholder).

## 대조 보완 — 폰트/3상태/점선깜빡임 (완료·검증) — 2026-06-02
> 사용자 피드백 반영: ① 한글 폰트 깨짐 ② 결과 3-상태(관련없음 추가) ③ 점선 연결+깜빡임.
- **폰트**: 신규 TMP 텍스트 6곳(FieldRow Label/Value, 결과텍스트/뱃지/X, 대화버튼)을 `Assets/Fonts/MalgunGothic Dynamic SDF`로 교체(LiberationSans는 한글 두부 깨짐). 재시작 후에도 유지 확인.
- **3-상태**: `CrossCheckController`에 `enum CrossCheckResult { Unrelated, Match, Mismatch }`. 관련성=**라벨(표시명) 일치 여부**(예 국적↔국적=관련). 관련+값동일=일치, 관련+값다름=불일치, 라벨다름=관련없음. 판정/점수 무영향(보조).
- **연출**: `CrossCheckConnectorView`(신규) — 선택한 두 필드 행을 **점선(`Assets/Images/dash_dot.png` 타일)** 으로 잇고 중점에 결과 글씨를 **깜빡이며**(코루틴 alpha 1↔0.2) 표시. 색: 일치#2E8B57/불일치#C0392B/관련없음#7A828C. 구 상단 `CrossCheckResultPanel`은 비활성(인플레이스 연출로 대체).
- **검증(Play)**: day7 다중서류로 국적=일치(녹)/만료일=불일치(빨강)/여권번호↔검사일=관련없음(회색), 점선 폭=필드간 거리, 컴파일 Error 0. startDay=1 저장.
- 한계: 관련성은 **라벨 기반**(의미 기반 MRZ↔여권번호 등은 필드 키 필요 → 데이터 재생성 대상, 후속).
- ⚠️ 교훈: 씬이 열린 상태에서 `.unity` 파일 외부 직접편집 → "외부 변경 reload" 모달로 에디터 멈춤 발생. **씬 변경은 반드시 에디터 API(mcp-unity/execute_code)로만.**

## 대조 이종소스 일반화 (완료·검증) — 2026-06-02
> 대조를 서류 내부 → **서류·캐릭터·뉴스·음성대화·규정 간**으로 일반화. 관련성=**속성 키(attribute key)** 일치. (xray·지문은 추후.)
- **데이터(1단계)**: `FieldEntry.key`(속성 키=컬럼 키) 추가, `Claim{attr,value,label}` 신설, `NewsData.claims[]`·`DialogueLineData.claim`·`RuleData.attr` 추가. day2~14 재생성 + day1 in-place 패치(대사 보존). 속성 키 어휘·매핑은 `build_days.py` 한 곳 + SHARED-CONVENTIONS 3.8절. 필드 1230개 key 채움, 뉴스 claim 3건/대화 claim 44건(다수 value="").
- **UI(2단계)**: `ICrossCheckSelectable`(SourceType/AttributeKey/Value/DisplayLabel/Rect/OnSelected) + `ICrossCheckProvider`. DocumentFieldView 구현화. CrossCheckController가 _documentView + _extraProviders(CustomerView·NewsPopup·RulebookPopup·DialogueLogPopup)에서 selectable 수집. 신규 `CrossCheckItemView`(비서류 항목 위젯). 캐릭터=이름(nameEn)·얼굴(spriteRef) selectable.
- **4-상태**: attr 다름→관련없음(회), 같고 양쪽 값有 일치/불일치(녹/빨), 같고 한쪽 값""→**관련 있음**(파랑, 규정/값없는 claim). CrossCheckConnectorView에 Related 추가.
- **씬은 Day1SceneBuilder가 단일 소스**: `Tools/Inspection/Build Day1 Scene`가 필드 행 템플릿·연결선·컨트롤러·모든 provider·한글폰트(FindFont→malgun)까지 전부 생성. (이전엔 필드 행이 빌더 밖 MCP 수작업이라 재빌드 시 회귀 → **빌더에 필드 행 생성 추가**로 해결.)
- **검증(Play, day7 다중서류)**: 캐릭터 이름↔여권 이름(MRZ NGUYEN<<VAN)=일치, 여권국적↔비자국적=일치(서류간 회귀), 캐릭터 이름↔여권 국적=관련없음. 직접 Show: GO 활성·깜빡임 코루틴 RUNNING·라벨 정상. 필드 행 9개 생성, 컴파일 Error 0, startDay=1.
- 한계: 관련성=라벨/키 기반. day2~14 대화 claim은 placeholder라 적음(입장 대사 미태깅). 뉴스 단체관광 등 단서 없는 건 claim 없음.

## MRZ 삭제 + xray·지문 대조 (완료·검증) — 2026-06-02
- **MRZ 완전 삭제**: 여권 MRZ1/MRZ2 필드 제거(day1~14, 잔존 0), 영문이름 `A<<B`→`A B` 정리(`<` 0건). 속성 어휘에서 `mrz` 제거. day1 손님6 단서를 MRZ→여권번호 형식위반으로 전환.
- **xray·지문 = 대조 소스 + 검사기 버튼**(사용자 승인): `ScanData{type,result,detail,extra,claim}`, `CustomerData.xray/.fingerprint`(11~14일차 6건). 지문 claim attr=`name`(matched_person)→여권 이름과 대조 시 불일치(위장 신원 적발), xray claim attr=`contraband`(detected_item)→뉴스 단서와. 어휘에 `contraband` 추가.
- UI: `ScanResultPanel`(ICrossCheckProvider, kind=Xray/Fingerprint) + `ScanRequestButton`(검사기 버튼, HasData시만 활성). InspectionController에 `CurrentXray/CurrentFingerprint`/`OnCustomerChanged` 접근자(판정 로직 무변경). Day1SceneBuilder에 버튼2+패널2 생성·바인딩, _extraProviders 6개(+scan panel 2).
- **빌더에 필드 행 템플릿 생성도 추가**(이전 회귀 수정) → 이제 `Build Day1 Scene` 한 번으로 필드 행·연결선·검사기·모든 provider 전부 재현. 폰트 FindFont(malgun).
- **버그 수정**: JsonUtility가 null 중첩객체를 빈 인스턴스로 역직렬화 → `ScanResultPanel.HasData`가 항상 true였음. 빈 ScanData를 "없음"으로 취급하도록 수정.
- **검증(Play)**: day11 손님10 지문 신원("성형 전 지명수배자…")↔여권 이름("YOON SEORIN")=**불일치**(위장 적발). HasData: day11=지문만/day13=xray만/day1=둘다없음 정확. 컴파일 Error 0, startDay=1.

## 스캔 = 대조로 잠금 해제 (완료·검증) — 2026-06-02
> xray/지문을 "항상 쓰는 버튼"→**"대조로 의심 입증 시 잠금 해제"**로 전환. 트리거 소스=뉴스+규정(사용자 승인).
- **데이터**: `Claim.unlocksScan`("xray"|"fingerprint"|"") 추가. 생성기가 검사 보유 손님 날짜에 트리거 뉴스 주입 — claim {attr,value(그 손님 실제 필드값),unlocksScan,label}. 각 트리거는 그 검사 가진 손님 1명에게만 매칭(거짓 해제 0): day11 passport_no=KO1011170→지문, day12 nationality=USA→지문, day13 nationality=PAK→xray, day14 KO1010053→xray+지문(손님9)/SYR→xray(손님34).
- **UI**: `ICrossCheckSelectable.UnlocksScan` 추가(서류/캐릭터=""; CrossCheckItemView=claim값, NewsPopup이 전달). `CrossCheckController.OnScanUnlocked(string)` — Compare 결과 Match/Related이고 한쪽 UnlocksScan!="" + 다른쪽이 손님 소스면 발행. `ScanRequestButton` 게이팅을 `HasData && _unlocked`로, OnScanUnlocked로 해제·OnCustomerChanged로 재잠금. Day1SceneBuilder가 버튼에 _crossCheck 바인딩.
- **스캔 버튼은 트리거 전 완전히 숨김(출력 X)**: `ScanRequestButton.UpdateInteractable`이 잠금 시 CanvasGroup alpha=0+interactable/blocksRaycasts=false(흐리게 아님), 해제 시 alpha=1로 **출력**. GameObject는 활성 유지(이벤트 구독 보장).
- **검증(Play, day13 손님11)**: 초기 xray 버튼 alpha=0(안 보임) → 뉴스 PAK단서↔여권국적 PAK 대조 Match → alpha=1(출력)·interactable=True → 손님 교체 시 alpha=0 재숨김. 컴파일 Error 0, startDay=1.
- 메모: 권위 규약은 `C:\Users\chris\.claude\agents\passport-game\SHARED-CONVENTIONS.md`(3.8 포함). 프로젝트 로컬 사본은 구버전 — 추후 동기화 정리 필요.

## 오늘 날짜 UI + 날짜 대조 (완료·검증) — 2026-06-02
> 만료일/발급일 판단 기준이 되는 "오늘 날짜"를 좌측하단 작은 UI로 표시 + 클릭 대조 소스화.
- **오늘 날짜 = 하루씩 증가**(런타임 계산, 데이터 불요): `InspectionController.CurrentDate` = `2026-06-01 + (day-1)`. day1=2026-06-01 … day14=2026-06-14.
- **TodayDateView**(좌측하단, 작게): "오늘: yyyy-MM-dd" 표시 + `ICrossCheckProvider`(selectable: SourceType="오늘", attr="today", value=날짜). OnCustomerChanged로 갱신. Day1SceneBuilder가 생성·바인딩, _extraProviders 7개(+today).
- **날짜 인식 대조**(CrossCheckController.`TryEvaluateDate`): 한쪽이 "오늘"이고 다른쪽 attr이 날짜키면 문자열 일치 대신 날짜 비교 — 만료류(expiry_date/valid_until): 오늘>날짜→"만료됨"(빨강)/else "유효"(녹); 발급류(issue_date/test_date/birth_date): 오늘<날짜→"발급일 오류(미래)"/else "발급 완료". `CrossCheckConnectorView.Show`에 overrideText 추가(색=enum, 라벨=문구). 판정/점수 무영향.
- **검증(Play)**: 좌측하단 "오늘: 2026-06-0X"(일차별), 오늘↔여권만료 2031=유효, 오늘↔비자만료 2023=만료됨, 오늘↔발급일=발급 완료. 컴파일 Error 0.
- **(갱신) 대조 멘트 통일**: 만료됨/유효/발급완료 특수 문구 제거 → **일치/불일치/관련없음**으로 통일(문서·필드 안 가림). 날짜 규칙: 만료류는 오늘≤날짜→일치(유효범위 내)/지나면 불일치; 발급류는 날짜≤오늘→일치/미래면 불일치; 날짜 아닌 필드→관련 없음. 검증: 여권만료2031=일치, 비자만료2023=불일치, 발급일과거=일치, 여권번호=관련없음.

## 미해결 / 다음 단계
0. **⚠️ Unity 인스턴스 주의**: mcp-unity에 연결된 인스턴스가 **`D:/My project`(무관 프로젝트)** 뿐이었음. 우리 프로젝트는 Unity에 안 열려 있어 **컴파일·Play 검증 불가**. → 이 프로젝트를 Unity에서 열어 브리지 연결 후 검증. (디스크 코드/데이터는 정상. 14일 데이터 경로는 python으로 전수 검증: 98손님, 대화 도달성·서류 불변식 문제 0건.)
1. (진행 중) GitHub 기본 브랜치를 `재영`으로 변경 — Settings→Branches에서 수동.
2. **(선택) data-tools SO 검증**: 메뉴 `Tools/Passport/Import Data` 1회 실행 → `Assets/GameData/*.asset` 19개 + `GameDatabase.asset` 생성 확인. (14일 JSON 경로와는 독립 — 당장 게임 동작엔 불필요)
3. 구현 순서(의존 방향): **data-tools(코드 완료) → gameplay → ui → qa**
   - gameplay: `GameDatabase` SO 하나 참조 → 변조 엔진(seed 결정론, `defect_rule.target_field`로 `fields[key]` 1곳 교체) → 판정 → **대화 3회 루프** → 점수/돈 정산. (위 특수 케이스 반영)
   - ui: UI-CONVENTIONS 기준으로 검사 데스크 + 재심사 루프 표시.
   - qa: 규칙·결정론·점수공식·14일 페이싱 검증.

## 새 세션 사용법
> 예: "PROGRESS.md 읽고, Unity 연결됐으면 passport-data-tools부터 시작해."
- 에이전트는 `.claude/agents/`에 있으므로 이름으로 바로 호출 가능.
- 절대경로 대신 `.claude/agents/passport-game/*.md` 상대경로로 규약 참조.
