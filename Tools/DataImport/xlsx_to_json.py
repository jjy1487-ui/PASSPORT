# -*- coding: utf-8 -*-
"""
xlsx_to_json.py — 「여권 주세요」 데이터 변환기 (data-tools 소유)

원본: Downloads/여권_정리_updated.xlsx (19시트)
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

# ── 기본 경로 ──────────────────────────────────────────────
DEFAULT_XLSX = r"C:\Users\chris\Downloads\여권_정리_updated.xlsx"
DEFAULT_OUT = r"C:\Users\chris\Documents\produc_build_reecture\Assets\GameData\_source\GameData.source.json"

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

    # 기대 시트 누락 체크
    for expected in SHEET_LAYOUT:
        if expected not in result["sheets"]:
            warnings.append("기대 시트 누락: %s" % expected)

    result["warnings"] = warnings

    os.makedirs(os.path.dirname(out), exist_ok=True)
    # 키 순서 안정화(idempotent): rows는 입력 순서 유지, 시트는 SHEET_LAYOUT 순서로 재정렬
    ordered = {}
    for name in SHEET_LAYOUT:
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
