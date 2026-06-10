# -*- coding: utf-8 -*-
"""
materialize_clones_to_xlsx.py — 클론 캐릭터를 엑셀 소스에 '정식 진실 행'으로 편입

배경:
  day_schedule(98슬롯=14일x7)가 37명을 재사용 → 같은 인물이 한 플레이에 최대 6번 등장.
  지금까지는 build_days 뒤 diversify_customers 가 '빌드된 dayN.json'의 중복 인물을 클론으로
  교체해 해결했다(런타임 후처리). 이 스크립트는 그 클론을 '엑셀 소스(진실)'에 정식으로 추가해
  엑셀이 98명(슬롯당 1명) 고유 캐릭터의 단일 소스가 되게 한다.

핵심 nuance (반드시 진실 레벨에서 복제):
  build_days 가 진실 서류에 defect_rule 로 결함을 '주입'하고 대사도 '조립'한다. 따라서 이미 가공된
  dayN.json 을 역매핑하면 안 된다(이중 결함). 여기서는 source(진실) 레벨에서만 복제하고,
  build_days 가 클론도 원본과 동일한 규칙으로 결함/대사를 처리하게 둔다.

복제 규칙:
  - day_schedule 를 (day, slot) 오름차순(=build_days/diversify 의 전역 등장 순서)으로 순회.
  - 각 customer_id 의 '첫 전역 등장' 슬롯은 원본 유지, '2번째 이상' 슬롯마다 클론 1명 생성.
  - 클론은 새 customer_id(1001~) + 국적/스타일에 맞는 새 이름(name_kr/name_en).
    sprite_ref / photo_ref(얼굴/사진)는 원본 재활용(여권사진 대조 관계 보존).
  - 원본이 가진 문서만 복제: passport(필수), visa/pcr_test/employment_cert/xray/fingerprint(있으면).
    문서의 이름 칸(name_en 등 여권 영문이름)은 새 이름으로, 그 외 진실값/결함값(alt_name 등 위조 신원)은 원본 유지.
  - day_schedule 해당 슬롯의 customer_id(+customer_name) 를 클론으로 갱신.

대사 시트:
  엑셀에 dialogue_case/dialogue_line 시트는 없다(build_days 가 make_dialogue_cases + branch 주입으로
  customer_id 키 기반 조립). 따라서 클론은 대사 행이 불필요(FK 무결성 영향 없음).

idempotent:
  이미 클론(customer_id>=1001)이 customer 시트에 있으면 아무 것도 추가하지 않는다(재실행 안전).
  클론 customer_id / 이름 배정은 source 데이터만으로 결정론적(같은 xlsx -> 같은 클론).

편집 방법:
  data/여권_정리_updated.xlsx 가 Excel 에서 열려 있어도 xlwings(COM)로 attach 해 편집한다.
  편집 전 타임스탬프 백업 사본을 만든다.

실행:
  python Tools/DataImport/materialize_clones_to_xlsx.py
  (cwd 무관 — __file__ 로 repo 루트 재계산)
"""
import os
import sys
import json
import shutil
import datetime

# ── 경로 ─────────────────────────────────────────────────────
_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", ".."))
XLSX = os.path.join(_REPO, "data", "여권_정리_updated.xlsx")
SOURCE_JSON = os.path.join(_REPO, "Assets", "GameData", "_source", "GameData.source.json")

CLONE_ID_START = 1001

# ── 클론 이름 풀 (diversify_customers.POOLS 와 동일 어휘 — 동일 결과 보장) ──
POOLS = {
    "korean": [
        ("김도윤", "KIM DOYUN"), ("이서준", "LEE SEOJUN"), ("박하준", "PARK HAJUN"), ("최예준", "CHOI YEJUN"),
        ("정시우", "JEONG SIWOO"), ("강주원", "KANG JUWON"), ("조지호", "JO JIHO"), ("윤건우", "YOON GEONWOO"),
        ("장우진", "JANG WOOJIN"), ("임선우", "IM SEONWOO"), ("한현우", "HAN HYUNWOO"), ("오은우", "OH EUNWOO"),
        ("서지안", "SEO JIAN"), ("신유준", "SHIN YUJUN"), ("권민재", "KWON MINJAE"), ("황지환", "HWANG JIHWAN"),
        ("안준영", "AN JUNYEONG"), ("송재윤", "SONG JAEYUN"), ("전도현", "JEON DOHYUN"), ("홍성민", "HONG SEONGMIN"),
        ("김서아", "KIM SEOAH"), ("이지우", "LEE JIWOO"), ("박하윤", "PARK HAYUN"), ("최수아", "CHOI SUAH"),
        ("정다은", "JEONG DAEUN"), ("강예린", "KANG YERIN"), ("조유진", "JO YUJIN"), ("윤채원", "YOON CHAEWON"),
        ("장소율", "JANG SOYUL"), ("임가은", "IM GAEUN"), ("한지아", "HAN JIA"), ("오나은", "OH NAEUN"),
        ("서하린", "SEO HARIN"), ("신예은", "SHIN YEEUN"), ("권서윤", "KWON SEOYUN"), ("황민서", "HWANG MINSEO"),
        ("배은서", "BAE EUNSEO"), ("문지유", "MOON JIYU"), ("양수빈", "YANG SUBIN"), ("백지원", "BAEK JIWON"),
        ("노태경", "NOH TAEKYUNG"), ("유준호", "YOO JUNHO"), ("심재훈", "SIM JAEHOON"), ("구본혁", "KOO BONHYUK"),
        ("남승현", "NAM SEUNGHYUN"),
    ],
    "western": [
        ("데이비드 스미스", "DAVID SMITH"), ("토머스 무어", "THOMAS MOORE"), ("다니엘 테일러", "DANIEL TAYLOR"),
        ("매튜 앤더슨", "MATTHEW ANDERSON"), ("크리스 토머스", "CHRIS THOMAS"), ("앤드류 화이트", "ANDREW WHITE"),
        ("에밀리 클락", "EMILY CLARK"), ("올리비아 루이스", "OLIVIA LEWIS"), ("소피아 워커", "SOPHIA WALKER"),
        ("에마 홀", "EMMA HALL"), ("그레이스 영", "GRACE YOUNG"), ("한나 킹", "HANNAH KING"),
        ("라이언 그린", "RYAN GREEN"), ("케빈 베이커", "KEVIN BAKER"),
    ],
    "chinese": [
        ("리 강", "LI GANG"), ("왕 팡", "WANG FANG"), ("장 민", "ZHANG MIN"), ("류 옌", "LIU YAN"),
        ("첸 하오", "CHEN HAO"), ("자오 친", "ZHAO QIN"), ("황 레이", "HUANG LEI"), ("우 팅", "WU TING"),
        ("쉬 펑", "XU FENG"), ("선 메이", "SUN MEI"), ("주 빈", "ZHU BIN"), ("후 쥔", "HU JUN"),
        ("궈 신", "GUO XIN"), ("린 타오", "LIN TAO"), ("허 룽", "HE LONG"), ("가오 윈", "GAO YUN"),
    ],
    "japanese": [
        ("사토 유토", "SATO YUTO"), ("스즈키 소라", "SUZUKI SORA"), ("다카하시 리쿠", "TAKAHASHI RIKU"),
        ("와타나베 하나", "WATANABE HANA"), ("이토 메이", "ITO MEI"), ("야마다 카이", "YAMADA KAI"),
        ("나카무라 츠바사", "NAKAMURA TSUBASA"), ("고바야시 사쿠라", "KOBAYASHI SAKURA"),
        ("가토 다이키", "KATO DAIKI"), ("요시다 미오", "YOSHIDA MIO"), ("야마구치 하루", "YAMAGUCHI HARU"),
        ("마츠모토 리오", "MATSUMOTO RIO"),
    ],
}


def culture_of(nationality):
    n = nationality or ""
    if "KOR" in n:
        return "korean"
    if "USA" in n.upper():
        return "western"
    if "CHN" in n:
        return "chinese"
    if "JPN" in n:
        return "japanese"
    return "korean"


# ── source.json 로드 ─────────────────────────────────────────
def load_source():
    with open(SOURCE_JSON, encoding="utf-8") as f:
        return json.load(f)["sheets"]


def index_by_cid(sheet_rows):
    """customer_id -> row (1:1 가정 시트용). 같은 cid 가 여러 행이면 마지막 채택."""
    return {r["customer_id"]: r for r in sheet_rows}


# ── 클론 계획 수립 (전역 등장 순서 = build_days/diversify 와 동일) ──
def plan_clones(sheets):
    """day_schedule 를 (day, slot) 순으로 순회. 2번째 이상 등장 슬롯마다 클론 배정.

    반환:
      clones: list of dict {
        cid(클론 id), origin_cid(원본 id), name_kr, name_en, day, slot, schedule_id }
      schedule_updates: list of (schedule_id, new_cid, new_name_kr)  # 갱신할 슬롯
    """
    schedule = sheets["day_schedule"]["rows"]
    customers = index_by_cid(sheets["customer"]["rows"])

    # 기존 이름 충돌 회피용 사용 집합
    existing_kr = set(r.get("name_kr") for r in sheets["customer"]["rows"])
    existing_en = set(r.get("name_en") for r in sheets["customer"]["rows"])

    cursor = {k: 0 for k in POOLS}

    def next_name(cult):
        pool = POOLS.get(cult) or POOLS["korean"]
        while cursor[cult] < len(pool):
            kr, en = pool[cursor[cult]]
            cursor[cult] += 1
            if kr in existing_kr or en in existing_en:
                continue
            existing_kr.add(kr)
            existing_en.add(en)
            return kr, en
        raise RuntimeError("이름 풀 소진: '%s' — POOLS 에 이름을 더 추가하세요." % cult)

    # (day, slot) 오름차순 정렬
    ordered = sorted(schedule, key=lambda r: (int(r["day"]), int(r["slot"])))

    seen = set()
    next_id = CLONE_ID_START
    clones = []
    schedule_updates = []
    for s in ordered:
        cid = s["customer_id"]
        if cid not in seen:
            seen.add(cid)
            continue  # 첫 전역 등장 → 원본 유지
        # 2번째 이상 등장 → 클론
        origin = customers[cid]
        cult = culture_of(origin.get("nationality"))
        new_kr, new_en = next_name(cult)
        clone_cid = str(next_id)
        clones.append({
            "cid": clone_cid,
            "origin_cid": cid,
            "name_kr": new_kr,
            "name_en": new_en,
            "day": int(s["day"]),
            "slot": int(s["slot"]),
            "schedule_id": s["schedule_id"],
        })
        schedule_updates.append((s["schedule_id"], clone_cid, new_kr))
        seen.add(clone_cid)
        next_id += 1
    return clones, schedule_updates


# ── 클론 행 빌더 (시트별, 진실 복제 + 이름만 새 이름) ──────────
def _mrz_name(name_en):
    """'KIM DOYUN' -> 'KIM<<DOYUN' (여권 MRZ식). build_days.clean_name 이 다시 공백으로 정규화하므로
    표기 일관성만 위한 것. 토큰이 1개면 그대로."""
    parts = (name_en or "").split()
    return "<<".join(parts) if len(parts) >= 2 else (name_en or "")


def build_customer_row(clone, origin):
    return {
        "customer_id": clone["cid"],
        "name_kr": clone["name_kr"],
        "name_en": clone["name_en"],
        "nationality": origin.get("nationality"),
        "gender": origin.get("gender"),
        "birth_date": origin.get("birth_date"),
        "age": origin.get("age"),
        "sprite_ref": origin.get("sprite_ref"),       # 얼굴 이미지 재활용
        "character_type": origin.get("character_type"),
    }


def build_passport_row(clone, origin_pp):
    r = dict(origin_pp)
    r["passport_id"] = clone["cid"]           # 클론 1:1 → cid 를 PK 로 (>37, 충돌 없음)
    r["customer_id"] = clone["cid"]
    r["name_en"] = _mrz_name(clone["name_en"])  # 여권 영문이름 = 새 이름(MRZ식 표기 유지)
    r["photo_ref"] = origin_pp.get("photo_ref")  # 여권 사진 = 원본 재활용
    return r


def build_visa_row(clone, origin_visa):
    r = dict(origin_visa)
    r["visa_id"] = clone["cid"]
    r["customer_id"] = clone["cid"]
    # visa 시트에 이름 칸 없음(build_days 가 여권에서 조인). 진실값 그대로.
    return r


def build_pcr_row(clone, origin_pcr):
    r = dict(origin_pcr)
    r["pcr_id"] = clone["cid"]
    r["customer_id"] = clone["cid"]
    return r


def build_emp_row(clone, origin_emp):
    r = dict(origin_emp)
    r["employment_id"] = clone["cid"]
    r["customer_id"] = clone["cid"]
    # employment 시트에 이름 칸 없음(build_days 가 여권에서 조인). 진실값 그대로.
    return r


def build_xray_row(clone, origin_xray):
    r = dict(origin_xray)
    r["xray_id"] = clone["cid"]
    r["customer_id"] = clone["cid"]
    # customer_name(FK 표시) 은 새 한글 이름으로. 적발물/위치 등 결함값은 원본 유지.
    if "customer_name" in r:
        r["customer_name"] = clone["name_kr"]
    return r


def build_fingerprint_row(clone, origin_fp):
    r = dict(origin_fp)
    r["fingerprint_id"] = clone["cid"]
    r["customer_id"] = clone["cid"]
    # alt_name/alt_birth/alt_nationality(위장 신원 = 결함값), criminal_record/wanted_no(수배 정보)는 원본 유지.
    # 본인(성형) 케이스의 표시 이름은 build_days 가 여권 영문이름에서 가져오므로 여기 손댈 칸 없음.
    return r


# ── 엑셀 쓰기 (xlwings/COM, 열린 파일에도 attach) ─────────────
# 시트별 컬럼 순서(엑셀 row3 영문 헤더 순서와 동일). 클론 dict 를 이 순서로 펼쳐 append.
SHEET_COLUMNS = {
    "customer": ["customer_id", "name_kr", "name_en", "nationality", "gender",
                 "birth_date", "age", "sprite_ref", "character_type"],
    "passport": ["passport_id", "customer_id", "passport_no", "name_en", "gender",
                 "birth_date", "nationality", "issue_date", "expiry_date", "photo_ref"],
    "visa": ["visa_id", "customer_id", "visa_no", "visa_type", "nationality",
             "issue_date", "expiry_date", "entry_type", "memo"],
    "pcr_test": ["pcr_id", "customer_id", "test_no", "test_date", "result",
                 "valid_until", "lab_name", "memo"],
    "employment_cert": ["employment_id", "customer_id", "cert_no", "company_name",
                         "job_title", "hire_date", "issue_date"],
    "xray": ["xray_id", "customer_id", "result", "detected_item", "hidden_location",
             "customer_name"],
    "fingerprint": ["fingerprint_id", "customer_id", "mode", "alt_name", "alt_birth",
                    "alt_nationality", "criminal_record", "wanted_no"],
}
HEADER_ROWS = 4  # row1 PK/FK, row2 type, row3 eng, row4 kr


def _to_cell(v):
    """엑셀 셀 값으로. 정수 문자열은 int 로(엑셀 표기 일관). 빈칸/None 은 None."""
    if v is None:
        return None
    s = str(v)
    if s == "":
        return None
    # 순수 정수면 int 로 (예: '1001' -> 1001). 음수/소수/날짜형은 문자열 유지.
    if s.isdigit():
        return int(s)
    return s


def _last_data_row(ws, col_index_1based):
    """헤더 아래에서 해당 PK 컬럼의 마지막 데이터 행 번호(1-based). 데이터 없으면 HEADER_ROWS."""
    last = HEADER_ROWS
    r = HEADER_ROWS + 1
    # ws.cells 로 한 컬럼 훑기 (used range 끝까지)
    used_last = ws.api.UsedRange.Rows.Count + ws.api.UsedRange.Row - 1
    for rr in range(HEADER_ROWS + 1, used_last + 1):
        v = ws.range((rr, col_index_1based)).value
        if v is not None and str(v).strip() != "":
            last = rr
    return last


def write_to_xlsx(rows_by_sheet, schedule_updates):
    """rows_by_sheet: {sheet_name: [clone dict, ...]}, schedule_updates: [(schedule_id, cid, name_kr)].
    xlwings 로 append/갱신. 백업은 호출 전에 수행."""
    import xlwings as xw

    # 열려 있으면 attach, 아니면 invisible open
    base = os.path.basename(XLSX).lower()
    bk = None
    opened = False
    for app in xw.apps:
        for b in app.books:
            try:
                if os.path.basename(b.fullname).lower() == base:
                    bk = b
                    break
            except Exception:
                pass
        if bk:
            break
    if bk is None:
        app = xw.App(visible=False)
        app.display_alerts = False
        bk = app.books.open(XLSX)
        opened = True

    try:
        # 1) 신규 클론 행 append (시트별)
        for sheet_name, clone_rows in rows_by_sheet.items():
            if not clone_rows:
                continue
            ws = bk.sheets[sheet_name]
            cols = SHEET_COLUMNS[sheet_name]
            # PK 컬럼(첫 컬럼)으로 마지막 데이터 행 찾기
            last = _last_data_row(ws, 1)
            for i, row in enumerate(clone_rows):
                target = last + 1 + i
                values = [_to_cell(row.get(k)) for k in cols]
                ws.range((target, 1)).value = values

        # 2) day_schedule 슬롯 갱신: customer_id(col4) + customer_name(col6)
        ws = bk.sheets["day_schedule"]
        sched_cols = ["schedule_id", "day", "slot", "customer_id", "valid_chance", "customer_name"]
        cid_col = sched_cols.index("customer_id") + 1   # 4
        name_col = sched_cols.index("customer_name") + 1  # 6
        sid_col = 1
        used_last = ws.api.UsedRange.Rows.Count + ws.api.UsedRange.Row - 1
        # schedule_id -> (cid, name) 맵
        upd = {str(sid): (cid, nm) for sid, cid, nm in schedule_updates}
        applied = 0
        for rr in range(HEADER_ROWS + 1, used_last + 1):
            sid = ws.range((rr, sid_col)).value
            if sid is None:
                continue
            sid_key = str(int(sid)) if isinstance(sid, float) and sid.is_integer() else str(sid)
            if sid_key in upd:
                cid, nm = upd[sid_key]
                ws.range((rr, cid_col)).value = _to_cell(cid)
                ws.range((rr, name_col)).value = nm
                applied += 1

        bk.save()
        return applied
    finally:
        if opened:
            bk.app.quit()


def main():
    sheets = load_source()

    # idempotent 가드: 이미 클론(customer_id>=1001)이 customer 시트에 있으면 중단(중복 추가 금지)
    existing_clone = [r for r in sheets["customer"]["rows"]
                      if str(r.get("customer_id", "")).isdigit() and int(r["customer_id"]) >= CLONE_ID_START]
    if existing_clone:
        msg = ("[materialize_clones] 이미 클론 %d명이 customer 시트에 존재 → 추가 안 함(idempotent). "
               "재생성하려면 클론 행을 먼저 제거하세요." % len(existing_clone))
        print(msg)
        return 0

    clones, schedule_updates = plan_clones(sheets)

    # 인덱스(원본 문서 보유 여부 판별)
    pp_idx = index_by_cid(sheets["passport"]["rows"])
    visa_idx = index_by_cid(sheets["visa"]["rows"])
    pcr_idx = index_by_cid(sheets["pcr_test"]["rows"])
    emp_idx = index_by_cid(sheets["employment_cert"]["rows"])
    xray_idx = index_by_cid(sheets["xray"]["rows"])
    fp_idx = index_by_cid(sheets["fingerprint"]["rows"])
    cust_idx = index_by_cid(sheets["customer"]["rows"])

    rows_by_sheet = {k: [] for k in SHEET_COLUMNS}
    counts = {k: 0 for k in SHEET_COLUMNS}

    for cl in clones:
        oc = cl["origin_cid"]
        origin = cust_idx[oc]
        rows_by_sheet["customer"].append(build_customer_row(cl, origin))
        counts["customer"] += 1
        # passport (원본이 가지면; 거의 전원 보유)
        if oc in pp_idx:
            rows_by_sheet["passport"].append(build_passport_row(cl, pp_idx[oc]))
            counts["passport"] += 1
        if oc in visa_idx:
            rows_by_sheet["visa"].append(build_visa_row(cl, visa_idx[oc]))
            counts["visa"] += 1
        if oc in pcr_idx:
            rows_by_sheet["pcr_test"].append(build_pcr_row(cl, pcr_idx[oc]))
            counts["pcr_test"] += 1
        if oc in emp_idx:
            rows_by_sheet["employment_cert"].append(build_emp_row(cl, emp_idx[oc]))
            counts["employment_cert"] += 1
        if oc in xray_idx:
            rows_by_sheet["xray"].append(build_xray_row(cl, xray_idx[oc]))
            counts["xray"] += 1
        if oc in fp_idx:
            rows_by_sheet["fingerprint"].append(build_fingerprint_row(cl, fp_idx[oc]))
            counts["fingerprint"] += 1

    # 백업 (타임스탬프)
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup = XLSX + ".bak_clones_%s" % ts
    shutil.copy2(XLSX, backup)

    applied = write_to_xlsx(rows_by_sheet, schedule_updates)

    # 결과 리포트(stdout 한글 안전 위해 숫자 위주)
    print("[materialize_clones] 클론 %d명 생성" % len(clones))
    for k in SHEET_COLUMNS:
        print("  +%-16s %d행" % (k, counts[k]))
    print("  day_schedule 갱신 슬롯: %d (기대 %d)" % (applied, len(schedule_updates)))
    print("  백업: %s" % os.path.basename(backup))
    return 0


if __name__ == "__main__":
    sys.exit(main())
