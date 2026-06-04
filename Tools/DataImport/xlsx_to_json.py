# -*- coding: utf-8 -*-
"""
xlsx_to_json.py — 「여권 주세요」 데이터 변환기 (data-tools 소유)

원본: <repo>/data/여권_정리_updated.xlsx (21시트, 단일 소스, 저장소 안)
출력: Assets/GameData/_source/GameData.source.json (단일 중간 JSON)

규약(SHARED-CONVENTIONS) 6장: 변경 흡수는 한 곳. 헤더 행 감지 / snake->camel 매핑 / 한글 meta는
전부 이 변환기에만 둔다. idempotent (같은 xlsx -> 같은 JSON).

콘솔이 cp949라 한글이 stdout에서 깨진다 -> print로 검증하지 말고 결과 JSON을 Read로 확인할 것.

사용:
    python xlsx_to_json.py [입력.xlsx] [출력.json]
인자 없으면 기본 경로 사용.
"""
import sys
import os
import json
import re

try:
    import openpyxl
except ImportError:
    sys.stderr.write("openpyxl 필요: pip install openpyxl\n")
    sys.exit(1)

# ── 기본 경로 (저장소 상대, PC 독립) ───────────────────────
# 이 스크립트 = <repo>/Tools/DataImport/xlsx_to_json.py → repo 루트는 두 단계 위.
_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", ".."))
# 단일 소스(저장소 안). 팀원이 clone해도 동일 경로로 동작한다.
DEFAULT_XLSX = os.path.join(_REPO, "data", "여권_정리_updated.xlsx")
DEFAULT_OUT = os.path.join(_REPO, "Assets", "GameData", "_source", "GameData.source.json")

# 2번째 소스(EXTRA): 팀원 별도 파일(character_payout/character_score 원본).
# 단일 소스(메인 엑셀)에 이미 두 시트가 편입돼 있어 보통은 사용되지 않는다(메인 우선).
# 메인에 시트가 없을 때만 폴백으로 시도하며, 파일이 없으면 경고만 내고 정상 진행한다.
# 환경변수 PASSPORT_EXTRA_XLSX로 경로를 줄 수 있고, 없으면 폴백 비활성(팀원 기본).
EXTRA_XLSX = os.environ.get("PASSPORT_EXTRA_XLSX", "")
EXTRA_PAYOUT_SHEET = "캐릭터별 분기별 지급 금액표(1회차기준)"
EXTRA_SCORE_SHEET = "캐릭터별 분기점 점수표(단순화)"

# ── 시트 헤더 레이아웃 분류 ─────────────────────────────────
# A: row1=PK/FK, row2=type, row3=영문컬럼, row4=한글, row5+=data
# B: row1=영문컬럼(한글 괄호), row2+=data  (type/한글행 없음)
# C: row1=한글헤더(항목/값/설명) key-value config
LAYOUT_A = "relational"   # 헤더 4행 + 데이터
LAYOUT_B = "flat"         # 헤더 1행 + 데이터
LAYOUT_C = "config"       # score_model key-value

SHEET_LAYOUT = {
    "customer": LAYOUT_A,
    "day_schedule": LAYOUT_A,
    "passport": LAYOUT_A,
    "visa": LAYOUT_A,
    "pcr_test": LAYOUT_A,
    "employment_cert": LAYOUT_A,
    "dialogue_case": LAYOUT_A,
    "dialogue_line": LAYOUT_A,
    "news": LAYOUT_A,
    "rule_book": LAYOUT_A,
    "xray": LAYOUT_A,
    "fingerprint": LAYOUT_A,
    "ending": LAYOUT_A,
    "shop": LAYOUT_A,
    "reward": LAYOUT_A,
    "defect_rule": LAYOUT_B,
    "fake_value_pool": LAYOUT_B,
    "document_requirement": LAYOUT_B,
    "score_model": LAYOUT_C,
    # 심사 오류 고지서(거절 시 발부하는 citation). rejection_templates/defect_rule/rule_book 과 정합.
    "inspection_notice": LAYOUT_A,
    # 세분화 규정집(정적 매뉴얼). 탭/항목/내용 구조. rule_book(날짜 브리핑)과 별개 개념.
    "rule_section": LAYOUT_A,
    # 캐릭터 분기표는 이제 메인 엑셀에 관계형(4행 헤더)으로 편입됨(append_character_sheets.py).
    # 메인에 있으면 이걸 1차 소스로 읽고, 없을 때만 EXTRA_XLSX 폴백.
    "character_payout": LAYOUT_A,
    "character_score": LAYOUT_A,
}


def clean_eng_key(raw):
    """영문 컬럼명만 추출. 'context(적용상황)' -> 'context'. 공백/None 제거."""
    if raw is None:
        return None
    s = str(raw).strip()
    if not s:
        return None
    # 괄호(한글 또는 영문) 앞의 영문 식별자만
    m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)", s)
    if m:
        return m.group(1)
    return s  # 영문 식별자가 아니면 원문 (config 등)


def norm_cell(v):
    """셀 값 정규화. 날짜/숫자/None 처리. 모든 값은 문자열로 보존(fields 키-값 규약)."""
    if v is None:
        return None
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float):
        # 정수 float -> 정수 문자열 (1.0 -> "1")
        if v.is_integer():
            return str(int(v))
        return repr(v)
    if isinstance(v, int):
        return str(v)
    # datetime
    try:
        import datetime
        if isinstance(v, (datetime.datetime, datetime.date)):
            return v.strftime("%Y-%m-%d")
    except Exception:
        pass
    return str(v).strip()


def read_layout_a(ws):
    """관계형: row1 pk/fk, row2 type, row3 eng, row4 kr, row5+ data."""
    grid = list(ws.iter_rows(values_only=True))
    if len(grid) < 4:
        return {"columns": [], "rows": []}
    pkfk_row = grid[0]
    type_row = grid[1]
    eng_row = grid[2]
    kr_row = grid[3]

    columns = []
    for ci, raw in enumerate(eng_row):
        key = clean_eng_key(raw)
        if key is None:
            continue
        columns.append({
            "key": key,
            "type": norm_cell(type_row[ci]) if ci < len(type_row) else None,
            "label": norm_cell(kr_row[ci]) if ci < len(kr_row) else None,
            "pkfk": norm_cell(pkfk_row[ci]) if ci < len(pkfk_row) else None,
            "col": ci,
        })

    rows = []
    for r in grid[4:]:
        if r is None:
            continue
        # 완전 빈 행 스킵
        if all(c is None or (isinstance(c, str) and c.strip() == "") for c in r):
            continue
        obj = {}
        for col in columns:
            ci = col["col"]
            obj[col["key"]] = norm_cell(r[ci]) if ci < len(r) else None
        rows.append(obj)
    return {"columns": [{"key": c["key"], "type": c["type"], "label": c["label"]} for c in columns],
            "rows": rows}


def read_layout_b(ws):
    """플랫: row1 영문컬럼(한글괄호), row2+ data. label은 괄호 안 한글 추출."""
    grid = list(ws.iter_rows(values_only=True))
    if len(grid) < 1:
        return {"columns": [], "rows": []}
    head = grid[0]
    columns = []
    for ci, raw in enumerate(head):
        key = clean_eng_key(raw)
        if key is None:
            continue
        label = None
        if raw is not None:
            m = re.search(r"\(([^)]*)\)", str(raw))
            if m:
                label = m.group(1).strip()
        columns.append({"key": key, "type": None, "label": label, "col": ci})

    rows = []
    for r in grid[1:]:
        if r is None:
            continue
        if all(c is None or (isinstance(c, str) and c.strip() == "") for c in r):
            continue
        obj = {}
        for col in columns:
            ci = col["col"]
            obj[col["key"]] = norm_cell(r[ci]) if ci < len(r) else None
        rows.append(obj)
    return {"columns": [{"key": c["key"], "type": c["type"], "label": c["label"]} for c in columns],
            "rows": rows}


def read_layout_c(ws):
    """score_model: row1=한글헤더(항목/값/설명), row2+ key-value. -> entries[{item,value,desc}]."""
    grid = list(ws.iter_rows(values_only=True))
    if len(grid) < 1:
        return {"columns": [], "rows": []}
    head = grid[0]  # 항목, 값, 설명
    keys = ["item", "value", "desc"]
    rows = []
    for r in grid[1:]:
        if r is None:
            continue
        if all(c is None or (isinstance(c, str) and c.strip() == "") for c in r):
            continue
        obj = {}
        for i, k in enumerate(keys):
            obj[k] = norm_cell(r[i]) if i < len(r) else None
        rows.append(obj)
    cols = [{"key": "item", "label": norm_cell(head[0]) if len(head) > 0 else "항목"},
            {"key": "value", "label": norm_cell(head[1]) if len(head) > 1 else "값"},
            {"key": "desc", "label": norm_cell(head[2]) if len(head) > 2 else "설명"}]
    return {"columns": cols, "rows": rows}


# ── 엔딩 점수밴드 오버라이드(밸런스 옵션1, 260602) ─────────────
#  캐릭터별 점수표(character_score)가 만드는 실제 누적 점수 분포에 맞춰
#  엔딩 점수구간(ending_id 1~10) 경계를 재산정한다. 캐릭터별 점수 값 자체는 불변.
#  근거 곡선(14일 98손님, 앞에서부터 정답): 0%=-800,50%=-207,60%=-74,70%=82,80%=230,90%=402,100%=577.
#  앵커: 70%→우수 사원, 100%→전설(상단 개방), 손익분기 0→나쁘진 않았어, 최저≈-800(하단 개방).
#  ending_id 11~23(조기/누적/히든, score_min/max=null)은 건드리지 않는다.
#  idempotent: xlsx 어떤 값이 와도 1~10은 이 표로 덮어쓴다(재생성에도 보존).
ENDING_SCORE_BANDS = {
    "1":  ("540",     "100000"),   # 전설의 검문관 (상단 개방)
    "2":  ("400",     "539"),      # 청렴한 검문관
    "3":  ("250",     "399"),      # 만인의 귀감
    "4":  ("80",      "249"),      # 우수 사원        (70%≈82)
    "5":  ("1",       "79"),       # 평범한 검문관
    "6":  ("-120",    "0"),        # 나쁘진 않았어    (손익분기 0)
    "7":  ("-280",    "-121"),     # 미숙한 직원
    "8":  ("-440",    "-281"),     # 진로 고민
    "9":  ("-640",    "-441"),     # 넌 해고야
    "10": ("-100000", "-641"),     # 형사 처벌        (하단 개방)
}

def apply_ending_band_override(result, warnings):
    """ending 시트 rows 중 ending_id 1~10의 score_min/score_max만 재산정.
    11~23(null 구간)은 손대지 않는다. 시트/행이 없으면 경고만 남기고 스킵."""
    sheet = result["sheets"].get("ending")
    if sheet is None:
        warnings.append("ending 밴드 오버라이드 스킵: ending 시트 없음")
        return
    applied = 0
    for r in sheet.get("rows", []):
        eid = str(r.get("ending_id"))
        if eid in ENDING_SCORE_BANDS:
            mn, mx = ENDING_SCORE_BANDS[eid]
            r["score_min"] = mn
            r["score_max"] = mx
            applied += 1
    if applied != len(ENDING_SCORE_BANDS):
        warnings.append("ending 밴드 오버라이드: 적용 %d/%d (일부 ending_id 행 없음)"
                        % (applied, len(ENDING_SCORE_BANDS)))


def main():
    xlsx = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_XLSX
    out = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT

    if not os.path.exists(xlsx):
        sys.stderr.write("입력 파일 없음: %s\n" % xlsx)
        sys.exit(2)

    wb = openpyxl.load_workbook(xlsx, data_only=True)
    result = {
        "source_file": os.path.basename(xlsx),
        "sheets": {},
    }

    reader = {LAYOUT_A: read_layout_a, LAYOUT_B: read_layout_b, LAYOUT_C: read_layout_c}
    warnings = []

    for ws in wb.worksheets:
        title = ws.title
        layout = SHEET_LAYOUT.get(title)
        if layout is None:
            warnings.append("알 수 없는 시트(스킵): %s" % title)
            continue
        data = reader[layout](ws)
        data["layout"] = layout
        result["sheets"][title] = data

    # 기대 시트 누락 체크 (character_* 는 EXTRA 폴백 가능하므로 여기선 제외)
    for expected in SHEET_LAYOUT:
        if expected in ("character_payout", "character_score"):
            continue
        if expected not in result["sheets"]:
            warnings.append("기대 시트 누락: %s" % expected)

    # ── 캐릭터 점수표/금액표: 메인 우선, EXTRA_XLSX 폴백 ──────────────
    # 정책(260602): 메인 엑셀에 character_payout/character_score 시트가 편입됨 → 그걸 1차 소스로 사용(위 루프에서 이미 읽음).
    # 메인에 없을 때만 팀원 별도 파일(EXTRA_XLSX)에서 분기 정규화로 보충(이중소스 충돌 방지: 메인이 단일 진실).
    have_main_payout = "character_payout" in result["sheets"]
    have_main_score = "character_score" in result["sheets"]
    if have_main_payout:
        warnings.append("character_payout: 메인 엑셀 1차 소스 사용(EXTRA 무시)")
    if have_main_score:
        warnings.append("character_score: 메인 엑셀 1차 소스 사용(EXTRA 무시)")

    extra = sys.argv[3] if len(sys.argv) > 3 else EXTRA_XLSX
    if (not have_main_payout or not have_main_score) and extra and os.path.exists(extra):
        try:
            import branch_normalize as bn
            ewb = openpyxl.load_workbook(extra, data_only=True)
            extra_titles = set(ws.title for ws in ewb.worksheets)
            if not have_main_payout:  # 메인에 없을 때만 폴백
                if EXTRA_PAYOUT_SHEET in extra_titles:
                    rows = list(ewb[EXTRA_PAYOUT_SHEET].iter_rows(values_only=True))
                    rows = [[norm_cell(c) for c in r] for r in rows]
                    result["sheets"]["character_payout"] = bn.build_payout(rows)
                    warnings.append("character_payout: EXTRA 폴백 사용(메인 시트 없음)")
                else:
                    warnings.append("extra 금액표 시트 누락: %s" % EXTRA_PAYOUT_SHEET)
            if not have_main_score:  # 메인에 없을 때만 폴백
                if EXTRA_SCORE_SHEET in extra_titles:
                    rows = list(ewb[EXTRA_SCORE_SHEET].iter_rows(values_only=True))
                    rows = [[norm_cell(c) for c in r] for r in rows]
                    result["sheets"]["character_score"] = bn.build_score(rows)
                    warnings.append("character_score: EXTRA 폴백 사용(메인 시트 없음)")
                else:
                    warnings.append("extra 점수표 시트 누락: %s" % EXTRA_SCORE_SHEET)
        except Exception as e:
            warnings.append("extra xlsx 처리 실패: %s" % e)
    elif not have_main_payout or not have_main_score:
        warnings.append("character_* 메인 시트 누락 + EXTRA 폴백 비활성(PASSPORT_EXTRA_XLSX 미설정): extra=%r" % extra)

    # 엔딩 점수밴드 재산정(밸런스 옵션1) — 시트 읽은 뒤, 출력 전. idempotent.
    apply_ending_band_override(result, warnings)

    result["warnings"] = warnings

    os.makedirs(os.path.dirname(out), exist_ok=True)
    # 키 순서 안정화(idempotent): rows는 입력 순서 유지, 시트는 SHEET_LAYOUT 순서로 재정렬.
    # 2번째 소스의 신규 시트(character_payout/character_score)는 19시트 뒤에 고정 순서로 붙인다.
    EXTRA_ORDER = ["character_payout", "character_score"]
    ordered = {}
    for name in SHEET_LAYOUT:
        if name in result["sheets"]:
            ordered[name] = result["sheets"][name]
    for name in EXTRA_ORDER:
        if name in result["sheets"]:
            ordered[name] = result["sheets"][name]
    result["sheets"] = ordered

    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)

    # 요약을 별도 UTF-8 파일로 (stdout 한글 깨짐 방지)
    summary = []
    for name, d in result["sheets"].items():
        summary.append("%-22s layout=%-10s cols=%d rows=%d" %
                       (name, d["layout"], len(d["columns"]), len(d["rows"])))
    if warnings:
        summary.append("--- warnings ---")
        summary.extend(warnings)
    summ_path = os.path.join(os.path.dirname(out), "_import_summary.txt")
    with open(summ_path, "w", encoding="utf-8") as f:
        f.write("\n".join(summary))

    sys.stdout.write("OK sheets=%d out=%s\n" % (len(result["sheets"]), out))


if __name__ == "__main__":
    main()
