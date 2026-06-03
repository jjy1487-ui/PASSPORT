# -*- coding: utf-8 -*-
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

FONT = "Arial"
HDR_FILL = PatternFill("solid", start_color="2F5496")
HDR_FONT = Font(name=FONT, bold=True, color="FFFFFF", size=11)
GRP_FILL = PatternFill("solid", start_color="D9E1F2")
TITLE_FONT = Font(name=FONT, bold=True, size=14, color="1F3864")
BODY = Font(name=FONT, size=10)
BOLD = Font(name=FONT, size=10, bold=True)
WRAP = Alignment(wrap_text=True, vertical="top")
TOPL = Alignment(vertical="top", horizontal="left")
thin = Side(style="thin", color="BFBFBF")
BORDER = Border(left=thin, right=thin, top=thin, bottom=thin)

wb = Workbook()

def style_header(ws, ncol, row=1):
    for c in range(1, ncol + 1):
        cell = ws.cell(row=row, column=c)
        cell.fill = HDR_FILL; cell.font = HDR_FONT
        cell.alignment = Alignment(vertical="center", horizontal="left")
        cell.border = BORDER
    ws.row_dimensions[row].height = 22
    ws.freeze_panes = ws.cell(row=row + 1, column=1)

def put_rows(ws, rows, start=2):
    for r, row in enumerate(rows, start):
        for c, val in enumerate(row, 1):
            cell = ws.cell(row=r, column=c, value=val)
            cell.font = BODY; cell.alignment = WRAP; cell.border = BORDER

# ───────────────────────── 시트 1: 개요 ─────────────────────────
ws = wb.active; ws.title = "0.개요"
ws.sheet_view.showGridLines = False
ws["A1"] = "여권 주세요 — 데이터 테이블 설명서"; ws["A1"].font = TITLE_FONT
lines = [
    "",
    "■ 데이터 흐름 (파이프라인)  —  사람은 [1] 엑셀만 고친다!",
    "   [1] 엑셀(여권_정리.xlsx, 시트 21장)   ← 사람이 편집하는 원본",
    "        │ 임포트 툴이 변환",
    "   [2] GameData.source.json              ← 엑셀을 떠놓은 중간 파일(자동 생성)",
    "        │ 임포터가 시트별 변환",
    "   [3] *.asset 테이블 21개 + GameDatabase.asset  ← 유니티가 읽는 데이터",
    "        │ 일자별 가공",
    "   [4] Resources/GameData/day1~14.json   ← 게임 실행 중 실제로 읽는 파일",
    "",
    "★ 규칙: day1.json 등 [2][3][4]는 직접 고치지 말 것 (임포트 때 덮어써짐).",
    "",
    "■ 시트 안내",
    "   0.개요          이 페이지",
    "   1.테이블요약     21개 테이블 한 줄 요약 + 그룹",
    "   2.데이터사전     모든 테이블의 모든 컬럼 설명 (가장 상세)",
    "   3.연결관계       테이블을 잇는 열쇠(ID) 설명",
    "   4.역할·빠른찾기  누가 무엇을 편집 / 자주 하는 작업 / 합치기 메모",
    "",
    "■ 테이블이 많아 보이는 이유",
    "   '정규화'로 정보를 쪼개 두었기 때문. 손님 1명 정보가 customer/passport/visa/대사로",
    "   흩어져 있지만 전부 customer_id 하나로 꿰여 있음. 이 열쇠 개념만 잡으면 쉬움.",
]
for i, t in enumerate(lines, 3):
    ws.cell(row=i, column=1, value=t).font = BODY if not t.startswith("■") else BOLD
ws.column_dimensions["A"].width = 95

# ───────────────────────── 시트 2: 테이블 요약 ─────────────────────────
ws = wb.create_sheet("1.테이블요약")
ws.append(["그룹", "테이블(영문)", "테이블(한글)", "행수", "한 줄 요약"])
style_header(ws, 5)
summary = [
    ["① 손님", "customer", "손님", 37, "누가 오는가 (사람 기본 신상). 모든 것의 출발점"],
    ["② 진행", "day_schedule", "일자 배치", 98, "며칠 몇 번째 슬롯에 누가 오는가 (등장 배치표)"],
    ["③ 서류", "passport", "여권", 37, "여권 내용 (번호/이름/국적/발급·만료일 등)"],
    ["③ 서류", "visa", "비자", 19, "비자 내용 (종류/국적/유효기간/입국횟수)"],
    ["③ 서류", "pcr_test", "PCR 검사서", 19, "PCR 검사서 (결과/검사일/유효기간/검사소)"],
    ["③ 서류", "employment_cert", "재직증명서", 5, "재직증명서 (회사/직책/발급·만료일)"],
    ["④ 검사·정보", "xray", "X-ray 검사", 3, "X-ray 검사 결과 (적발물/은닉 위치) — 대조 단서"],
    ["④ 검사·정보", "fingerprint", "지문 검사", 3, "지문 검사 결과 (일치 상태/인물) — 대조 단서"],
    ["④ 검사·정보", "news", "뉴스", 5, "일자별 뉴스 — 본문에서 단서 도출"],
    ["④ 검사·정보", "rule_book", "규정집", 6, "일자별 규정 — 판단 근거"],
    ["⑤ 대사", "dialogue_case", "대사 상황", 38, "대사 '상황'(머리): 유형/서류/위반항목/판정/정오"],
    ["⑤ 대사", "dialogue_line", "대사 줄", 93, "대사 '줄'(몸): 화자/화자ID/순서/대사 내용"],
    ["⑥ 결함생성", "defect_rule", "결함 규칙", 15, "위조 서류를 어떻게 만들지 규칙"],
    ["⑥ 결함생성", "fake_value_pool", "가짜값 풀", 13, "변조에 넣을 가짜 값 후보 모음"],
    ["⑥ 결함생성", "document_requirement", "서류 요구", 4, "기간별 어떤 서류가 필수인지"],
    ["⑦ 점수·경제", "score_model", "점수 설정", 12, "점수 계산용 설정값(환경설정)"],
    ["⑦ 점수·경제", "character_score", "캐릭터 점수", 83, "유형·분기별 점수/호칭"],
    ["⑦ 점수·경제", "character_payout", "캐릭터 보상", 62, "유형·분기별 돈(보상). character_score와 쌍둥이"],
    ["⑦ 점수·경제", "ending", "엔딩", 23, "점수 구간/트리거별 엔딩"],
    ["⑦ 점수·경제", "shop", "상점", 8, "상점 아이템 (가격/해금일/효과)"],
    ["⑦ 점수·경제", "reward", "보상", 7, "조건 달성 보상 (트리거/금액)"],
]
put_rows(ws, summary)
for w, col in zip([14, 20, 14, 7, 66], "ABCDE"):
    ws.column_dimensions[col].width = w
# 그룹 셀 연하게 + 한글명 굵게
for r in range(2, len(summary) + 2):
    ws.cell(row=r, column=1).fill = GRP_FILL
    ws.cell(row=r, column=3).font = BOLD

# ───────────────────────── 시트 3: 데이터 사전 ─────────────────────────
ws = wb.create_sheet("2.데이터사전")
ws.append(["테이블", "컬럼 키", "설명 / 역할", "연결(FK)"])
style_header(ws, 4)
# (table, col, desc, fk)
D = [
 ("customer","customer_id","손님 고유 번호 (PK)","→ 모든 손님 데이터의 열쇠"),
 ("customer","name_kr","한글 이름",""),
 ("customer","name_en","영문 이름",""),
 ("customer","nationality","국적",""),
 ("customer","gender","성별",""),
 ("customer","birth_date","생년월일",""),
 ("customer","age","나이",""),
 ("customer","sprite_ref","캐릭터 이미지 키",""),
 ("customer","character_type","손님 유형(일반/관광객 등)","→ 점수·결함 테이블"),

 ("day_schedule","schedule_id","슬롯 고유 번호 (PK)","→ dialogue_case"),
 ("day_schedule","day","며칠(1~14)",""),
 ("day_schedule","slot","그날 몇 번째 손님",""),
 ("day_schedule","customer_id","어떤 손님","→ customer"),
 ("day_schedule","valid_chance","서류가 정상일 확률",""),

 ("passport","passport_id","여권 행 번호 (PK)",""),
 ("passport","customer_id","주인 손님","→ customer"),
 ("passport","passport_no","여권 번호",""),
 ("passport","name_en","영문 이름(대조 대상)",""),
 ("passport","gender","성별",""),
 ("passport","birth_date","생년월일",""),
 ("passport","nationality","국적",""),
 ("passport","issue_date","발급일",""),
 ("passport","expiry_date","만료일(오늘과 대조)",""),
 ("passport","photo_ref","사진 키",""),

 ("visa","visa_id","비자 행 번호 (PK)",""),
 ("visa","customer_id","주인 손님","→ customer"),
 ("visa","visa_no","비자 번호",""),
 ("visa","visa_type","비자 종류",""),
 ("visa","nationality","국적",""),
 ("visa","issue_date","발급일",""),
 ("visa","expiry_date","만료일",""),
 ("visa","entry_type","입국 횟수(단수/복수)",""),
 ("visa","memo","비고",""),

 ("pcr_test","pcr_id","PCR 행 번호 (PK)",""),
 ("pcr_test","customer_id","주인 손님","→ customer"),
 ("pcr_test","test_no","검사 번호",""),
 ("pcr_test","test_date","검사일",""),
 ("pcr_test","result","검사 결과(음성/양성)",""),
 ("pcr_test","valid_until","유효 기한(오늘과 대조)",""),
 ("pcr_test","lab_name","검사 기관",""),
 ("pcr_test","memo","비고",""),

 ("employment_cert","employment_id","재직증명 행 번호 (PK)",""),
 ("employment_cert","customer_id","주인 손님","→ customer"),
 ("employment_cert","cert_no","증명서 번호",""),
 ("employment_cert","company_name","회사명",""),
 ("employment_cert","job_title","직책",""),
 ("employment_cert","issue_date","발급일",""),
 ("employment_cert","expiry_date","만료일",""),

 ("xray","xray_id","X-ray 행 번호 (PK)",""),
 ("xray","customer_id","주인 손님","→ customer"),
 ("xray","result","검사 결과",""),
 ("xray","detected_item","적발물",""),
 ("xray","hidden_location","은닉 위치",""),

 ("fingerprint","fingerprint_id","지문 행 번호 (PK)",""),
 ("fingerprint","customer_id","주인 손님","→ customer"),
 ("fingerprint","result","검사 결과",""),
 ("fingerprint","match_status","일치 상태",""),
 ("fingerprint","matched_person","일치 인물/사건",""),

 ("news","news_id","뉴스 번호 (PK)",""),
 ("news","day","며칠",""),
 ("news","news_title","뉴스 제목",""),
 ("news","news_content","뉴스 본문(단서 도출)",""),
 ("news","icon_ref","아이콘 키",""),

 ("rule_book","rule_id","규정 번호 (PK)",""),
 ("rule_book","day","적용 시작일",""),
 ("rule_book","rule_title","규정 제목",""),
 ("rule_book","rule_content","규정 본문",""),
 ("rule_book","related_field","관련 항목",""),

 ("dialogue_case","dialogue_case_id","대화 상황 번호 (PK)","→ dialogue_line"),
 ("dialogue_case","schedule_id","어느 슬롯의 대사","→ day_schedule"),
 ("dialogue_case","customer_id","어느 손님","→ customer"),
 ("dialogue_case","character_type","손님 유형(특수★ 구분)","→ customer"),
 ("dialogue_case","case_type","상황 종류(입장/일반 심사/여권 없음/입국 도장 없음)",""),
 ("dialogue_case","document_type","어떤 서류 관련(여권/비자/-, 항목별 대사 키)",""),
 ("dialogue_case","violation_field","위반 항목(만료일/이름/없음…, 항목별 대사 키)",""),
 ("dialogue_case","verdict","플레이어 도장: 입국 허가 / 입국 거부 / -",""),
 ("dialogue_case","result","판정 정오: 정답 / 오답 / -",""),
 ("dialogue_case","reject_count","오거부 단계(1~3, 특수 캐릭터 전용)",""),

 ("dialogue_line","line_id","대사 줄 번호 (PK)",""),
 ("dialogue_line","dialogue_case_id","어느 상황에 속하나","→ dialogue_case"),
 ("dialogue_line","line_order","대사 순서",""),
 ("dialogue_line","speaker","화자(심사관 / 캐릭터 이름)",""),
 ("dialogue_line","speaker_id","화자 캐릭터 ID(손님=ID, 심사관=빈칸)","→ customer"),
 ("dialogue_line","text_kr","대사 내용(한글)",""),

 ("defect_rule","rule_id","규칙 번호",""),
 ("defect_rule","character_type","적용 손님 유형","→ customer"),
 ("defect_rule","context","상황",""),
 ("defect_rule","defect_document","변조할 서류",""),
 ("defect_rule","violation_field","위반 항목",""),
 ("defect_rule","corruption_type","변조 방식",""),
 ("defect_rule","target_field","대상 필드",""),
 ("defect_rule","secondary_check","이 결함을 잡는 보조검사와 결과 (지문/X-ray, 없으면 -)","→ xray/fingerprint"),
 ("defect_rule","random_valid_chance","정상 확률",""),
 ("defect_rule","note","비고",""),

 ("fake_value_pool","pool_id","번호",""),
 ("fake_value_pool","field","어느 항목용 가짜 값",""),
 ("fake_value_pool","fake_value","가짜 값",""),
 ("fake_value_pool","note","비고",""),

 ("document_requirement","req_id","번호",""),
 ("document_requirement","document_type","서류 종류",""),
 ("document_requirement","day_from","적용 시작일",""),
 ("document_requirement","day_to","적용 종료일",""),
 ("document_requirement","applies_to","적용 대상",""),
 ("document_requirement","note","비고",""),

 ("score_model","item","설정 항목",""),
 ("score_model","value","값",""),
 ("score_model","desc","설명",""),

 ("character_score","score_id","행 번호 (PK)",""),
 ("character_score","character_type","손님 유형","→ customer"),
 ("character_score","visit_round","방문 회차",""),
 ("character_score","doc_state","서류 상태",""),
 ("character_score","defect_variant","결함 세부",""),
 ("character_score","branch_key","분기 키","↔ character_payout"),
 ("character_score","score","점수",""),
 ("character_score","score_range","점수 구간",""),
 ("character_score","title","호칭",""),
 ("character_score","event_id","이벤트",""),
 ("character_score","note","비고",""),

 ("character_payout","payout_id","행 번호 (PK)",""),
 ("character_payout","character_type","손님 유형","→ customer"),
 ("character_payout","visit_round","방문 회차",""),
 ("character_payout","doc_state","서류 상태",""),
 ("character_payout","defect_variant","결함 세부",""),
 ("character_payout","branch_key","분기 키","↔ character_score"),
 ("character_payout","base_tier","기본 금액",""),
 ("character_payout","bounty","현상금",""),
 ("character_payout","payout","지급 금액",""),
 ("character_payout","formula","계산식",""),
 ("character_payout","item_drop","아이템 드롭",""),
 ("character_payout","early_ending","조기 엔딩",""),
 ("character_payout","appears_round1","1회차 등장",""),
 ("character_payout","note","비고",""),

 ("ending","ending_id","엔딩 번호 (PK)",""),
 ("ending","ending_type","엔딩 분류",""),
 ("ending","ending_name","엔딩명",""),
 ("ending","score_min","점수 하한",""),
 ("ending","score_max","점수 상한",""),
 ("ending","end_timing","발동 시점",""),
 ("ending","trigger_type","트리거 타입",""),
 ("ending","trigger_condition","발동 조건",""),

 ("shop","shop_item_id","상점 아이템 번호 (PK)",""),
 ("shop","item_name","아이템명",""),
 ("shop","category","분류",""),
 ("shop","price","가격",""),
 ("shop","unlock_day","해금일",""),
 ("shop","effect_type","효과 타입",""),
 ("shop","effect_value","효과 수치",""),
 ("shop","effect","효과 설명",""),

 ("reward","reward_id","보상 번호 (PK)",""),
 ("reward","reward_type","보상 종류",""),
 ("reward","trigger_type","트리거 타입",""),
 ("reward","trigger_condition","발동 조건",""),
 ("reward","amount","금액",""),
 ("reward","related_ending_id","연관 엔딩","→ ending"),
 ("reward","memo","비고",""),
]
KR_NAME = {
 "customer":"손님","day_schedule":"일자 배치","passport":"여권","visa":"비자",
 "pcr_test":"PCR 검사서","employment_cert":"재직증명서","xray":"X-ray 검사",
 "fingerprint":"지문 검사","news":"뉴스","rule_book":"규정집",
 "dialogue_case":"대사 상황","dialogue_line":"대사 줄","defect_rule":"결함 규칙",
 "fake_value_pool":"가짜값 풀","document_requirement":"서류 요구","score_model":"점수 설정",
 "character_score":"캐릭터 점수","character_payout":"캐릭터 보상","ending":"엔딩",
 "shop":"상점","reward":"보상",
}
# 컬럼 키/설명/연결만 먼저 채운다(A열은 병합으로 처리)
for r, row in enumerate(D, 2):
    for c, val in enumerate(row[1:], 2):  # B,C,D
        cell = ws.cell(row=r, column=c, value=val)
        cell.font = BODY; cell.alignment = WRAP; cell.border = BORDER
    ws.cell(row=r, column=1).border = BORDER
for w, col in zip([22, 22, 50, 24], "ABCD"):
    ws.column_dimensions[col].width = w

# 테이블 블록: A열 세로 병합 + 한글명 + 블록 음영(번갈아)
SHADE = PatternFill("solid", start_color="EAF0FB")
GVAL = Alignment(horizontal="center", vertical="center", wrap_text=True)
# 같은 테이블이 연속하는 구간 [start_row, end_row] 목록 만들기
blocks = []
bstart = 2
for r in range(2, len(D) + 2):
    name = D[r - 2][0]
    nxt = D[r - 1][0] if (r - 1) < len(D) else None
    if name != nxt:
        blocks.append((bstart, r, name))
        bstart = r + 1
for bi, (s, e, tname) in enumerate(blocks):
    label = f"{tname}\n({KR_NAME.get(tname, tname)})"
    if e > s:
        ws.merge_cells(start_row=s, start_column=1, end_row=e, end_column=1)
    cell = ws.cell(row=s, column=1, value=label)
    cell.font = BOLD; cell.alignment = GVAL; cell.border = BORDER
    if bi % 2 == 1:  # 한 칸 건너 음영
        for rr in range(s, e + 1):
            for cc in range(1, 5):
                ws.cell(row=rr, column=cc).fill = SHADE

# ───────────────────────── 시트 4: 연결 관계 ─────────────────────────
ws = wb.create_sheet("3.연결관계")
ws.sheet_view.showGridLines = False
ws["A1"] = "테이블을 잇는 열쇠(ID)"; ws["A1"].font = TITLE_FONT
rel = [
 "",
 "테이블들은 같은 ID 값으로 서로 연결됩니다. 아래 4개만 알면 됩니다.",
 "",
 "● customer_id  — 손님 1명을 가리킴 (가장 중요)",
 "    customer → passport / visa / pcr_test / employment_cert / xray / fingerprint",
 "             → day_schedule / dialogue_case / defect_rule / 점수표",
 "",
 "● schedule_id  — '며칠 몇 번째 손님' 한 슬롯",
 "    day_schedule → dialogue_case",
 "",
 "● dialogue_case_id  — 대사 상황 1개",
 "    dialogue_case → dialogue_line (대사 줄들)",
 "",
 "● character_type  — 손님 유형",
 "    customer → character_score / character_payout / defect_rule",
 "",
 "──────────── 관계도 ────────────",
 "customer (손님)",
 "   ├─ passport / visa / pcr_test / employment_cert   (customer_id)",
 "   ├─ xray / fingerprint                              (customer_id)",
 "   ├─ day_schedule ─schedule_id─ dialogue_case ─dialogue_case_id─ dialogue_line",
 "   └─ character_type ─ character_score / character_payout / defect_rule",
]
for i, t in enumerate(rel, 3):
    ws.cell(row=i, column=1, value=t).font = BODY
ws.column_dimensions["A"].width = 90

# ───────────────────────── 시트 5: 역할·빠른찾기 ─────────────────────────
ws = wb.create_sheet("4.역할·빠른찾기")
ws.append(["■ 누가 어떤 테이블을 편집하나", ""])
ws.cell(row=1, column=1).font = BOLD
role = [
 ["시나리오/대사 작가", "dialogue_case, dialogue_line, news, rule_book"],
 ["레벨/케이스 기획", "customer, day_schedule, passport/visa/pcr_test/employment_cert, defect_rule, fake_value_pool, document_requirement"],
 ["시스템/밸런스 기획", "score_model, character_score, character_payout, ending, shop, reward"],
 ["프로그래머", "엑셀은 거의 안 건드림 (임포터/런타임 코드 담당)"],
]
r = 2
for role_name, tables in role:
    ws.cell(row=r, column=1, value=role_name).font = BOLD
    ws.cell(row=r, column=2, value=tables).font = BODY
    ws.cell(row=r, column=2).alignment = WRAP
    r += 1

r += 1
ws.cell(row=r, column=1, value="■ 자주 하는 작업 → 어디를 고치나").font = BOLD; r += 1
tasks = [
 ["새 손님 추가", "customer 행 추가 → 서류(passport 등) 추가 → day_schedule 슬롯 추가 → 대사 추가"],
 ["대사 수정", "dialogue_line 의 text_kr"],
 ["위반(불일치) 항목 변경", "해당 서류 값 + dialogue_case 의 violation_field"],
 ["점수/보상 조정", "character_score(점수) / character_payout(돈)"],
 ["엔딩 조건", "ending"],
]
for task, where in tasks:
    ws.cell(row=r, column=1, value=task).font = BOLD
    ws.cell(row=r, column=2, value=where).font = BODY
    ws.cell(row=r, column=2).alignment = WRAP
    r += 1

r += 1
ws.cell(row=r, column=1, value="■ 합치기 검토 메모").font = BOLD; r += 1
merge = [
 ["✅ 합치기 좋음", "xray + fingerprint", "구조 거의 동일, 런타임은 이미 ScanData 하나로 사용 중"],
 ["🔶 신중히", "passport/visa/pcr_test/employment_cert", "런타임은 공통 서류지만 컬럼이 달라 빈칸↑ 가독성↓"],
 ["❌ 합치지 말 것", "dialogue_case + dialogue_line", "1:다 관계라 분리가 정답"],
 ["🔶 분리 유지 권장", "character_score + character_payout", "열쇠 같지만 점수/돈으로 성격 다름"],
]
ws.cell(row=r, column=1, value="판단"); ws.cell(row=r, column=2, value="대상"); ws.cell(row=r, column=3, value="이유")
for c in range(1,4): ws.cell(row=r, column=c).font = HDR_FONT; ws.cell(row=r, column=c).fill = HDR_FILL
r += 1
for verdict, target, reason in merge:
    ws.cell(row=r, column=1, value=verdict).font = BODY
    ws.cell(row=r, column=2, value=target).font = BODY
    ws.cell(row=r, column=3, value=reason).font = BODY
    ws.cell(row=r, column=3).alignment = WRAP
    r += 1
for w, col in zip([20, 40, 55], "ABC"):
    ws.column_dimensions[col].width = w

import os
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "테이블_설명서.xlsx")
wb.save(out)
print("saved", out)
