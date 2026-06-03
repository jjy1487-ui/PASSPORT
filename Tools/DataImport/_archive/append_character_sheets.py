# -*- coding: utf-8 -*-
"""
append_character_sheets.py — 메인 DB 엑셀에 character_score / character_payout 두 시트를 편입.
(data-tools 소유. SHARED-CONVENTIONS 관계형 레이아웃 + §3.9 컬럼 계약)

목적:
  팀원 별도 파일(EXTRA_XLSX)에만 있던 캐릭터 분기 점수표/금액표를,
  기존 19시트 메인 DB(`여권_정리_updated.xlsx`)에 같은 형식의 새 2시트로 추가해
  21시트 단일 파일에서 열람/편집 가능하게 한다.

형식(기존 시트와 동일한 관계형 4행 헤더):
  row1 = PK/FK 표기(키 컬럼에만, 나머지는 빈칸)
  row2 = 자료형(int / varchar(N) / bool …)  ← 기존 시트의 varchar(N) 톤 따름
  row3 = 영문 컬럼명(snake_case, 매핑 기준)
  row4 = 한글 라벨
  row5+ = 데이터(이미 정규화된 권위 값)

권위 데이터 소스:
  - 컬럼 정의(type/label/pkfk): Tools/DataImport/branch_normalize.py 의 SCORE_COLS / PAYOUT_COLS
  - 데이터 행: Assets/GameData/_source/GameData.source.json 의 sheets.character_score / character_payout rows
    (branch_normalize 정규화 결과와 1:1. 별도 EXTRA_XLSX 재파싱 없이 동일 값 보장 → idempotent)

idempotent:
  - 실행 전 타임스탬프 백업.
  - 기존 19시트는 절대 수정하지 않는다(읽지도 쓰지도 않음).
  - character_score / character_payout 시트가 이미 있으면 제거 후 재생성(같은 입력 → 같은 결과).
  - 항상 19시트 뒤에 character_payout, character_score 순으로 append → 21시트.

사용:
    python append_character_sheets.py [메인.xlsx] [source.json]
인자 없으면 기본 경로.

콘솔이 cp949라 한글 stdout이 깨진다 → 결과 확인은 생성된 xlsx를 openpyxl로 Read.
"""
import sys
import os
import json
import shutil
import datetime

try:
    import openpyxl
except ImportError:
    sys.stderr.write("openpyxl 필요: pip install openpyxl\n")
    sys.exit(1)

# branch_normalize 의 컬럼 정의(type/label/pkfk)를 단일 소스로 재사용
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import branch_normalize as bn  # noqa: E402

# 저장소 상대경로(PC 독립). 일회성 마이그레이션 스크립트지만 하드코딩 경로 제거.
_REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_XLSX = os.path.join(_REPO, "data", "여권_정리_updated.xlsx")
DEFAULT_SOURCE = os.path.join(_REPO, "Assets", "GameData", "_source", "GameData.source.json")

# 새로 편입할 시트: (시트명, source.json 키, branch_normalize 컬럼정의)
SHEETS = [
    ("character_payout", "character_payout", bn.PAYOUT_COLS),
    ("character_score",  "character_score",  bn.SCORE_COLS),
]

# 자료형 표기: 기존 시트가 varchar(N)을 쓰므로 동일 톤으로 길이 부여.
# (헤더 표시용. 파이프라인은 row3 영문키로 읽으므로 길이값은 비기능.)
TYPE_LEN = {
    "score_id": "int", "payout_id": "int",
    "character_type": "varchar(30)",
    "visit_round": "int",
    "doc_state": "varchar(20)",
    "defect_variant": "varchar(60)",
    "branch_key": "varchar(40)",
    "score": "int", "score_range": "varchar(30)",
    "title": "varchar(30)", "event_id": "varchar(10)",
    "base_tier": "int", "bounty": "int", "payout": "int",
    "formula": "varchar(60)", "item_drop": "varchar(30)",
    "early_ending": "varchar(10)", "appears_round1": "bool",
    "note": "varchar(300)",
}


def sql_type(col):
    """branch_normalize의 bare type을 기존 시트 톤(varchar(N))으로 변환."""
    return TYPE_LEN.get(col["key"], col["type"])


def write_sheet(ws, cols, rows):
    """관계형 4행 헤더 + 데이터. row1 pkfk / row2 type / row3 eng / row4 kr / row5+ data."""
    for ci, col in enumerate(cols, start=1):
        pkfk = (col.get("pkfk") or "").strip()
        ws.cell(row=1, column=ci, value=pkfk if pkfk else None)   # PK/FK는 키 컬럼만
        ws.cell(row=2, column=ci, value=sql_type(col))            # 자료형
        ws.cell(row=3, column=ci, value=col["key"])               # 영문(매핑 기준)
        ws.cell(row=4, column=ci, value=col["label"])             # 한글 라벨
    keys = [c["key"] for c in cols]
    for ri, row in enumerate(rows, start=5):
        for ci, k in enumerate(keys, start=1):
            v = row.get(k)
            ws.cell(row=ri, column=ci, value=v if v not in ("", None) else None)


def main():
    xlsx = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_XLSX
    source = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_SOURCE

    if not os.path.exists(xlsx):
        sys.stderr.write("메인 엑셀 없음: %s\n" % xlsx)
        sys.exit(2)
    if not os.path.exists(source):
        sys.stderr.write("source.json 없음(먼저 xlsx_to_json.py 실행): %s\n" % source)
        sys.exit(2)

    with open(source, "r", encoding="utf-8") as f:
        src = json.load(f)
    src_sheets = src["sheets"]

    # ── 백업(타임스탬프) ──────────────────────────────────────
    ts = datetime.datetime.now().strftime("%y%m%d_%H%M%S")
    base, ext = os.path.splitext(xlsx)
    backup = "%s.backup_%s%s" % (base, ts, ext)
    shutil.copy2(xlsx, backup)

    wb = openpyxl.load_workbook(xlsx)  # data_only 아님: 기존 시트 식 보존
    existing = list(wb.sheetnames)

    # ── idempotent: 이미 있으면 제거 후 재생성 ────────────────
    for sheet_name, _, _ in SHEETS:
        if sheet_name in wb.sheetnames:
            del wb[sheet_name]

    # ── 19시트 뒤에 append (payout, score 순) ─────────────────
    added = []
    for sheet_name, src_key, cols in SHEETS:
        if src_key not in src_sheets:
            sys.stderr.write("source.json에 시트 없음(스킵): %s\n" % src_key)
            continue
        rows = src_sheets[src_key]["rows"]
        ws = wb.create_sheet(title=sheet_name)
        write_sheet(ws, cols, rows)
        added.append((sheet_name, len(rows)))

    wb.save(xlsx)

    # ── 요약(UTF-8 파일, stdout 한글 깨짐 방지) ───────────────
    summ = []
    summ.append("backup: %s" % backup)
    summ.append("main xlsx: %s" % xlsx)
    summ.append("기존 시트 수: %d" % len(existing))
    summ.append("최종 시트 수: %d" % len(wb.sheetnames))
    summ.append("최종 시트 순서: %s" % ", ".join(wb.sheetnames))
    for nm, cnt in added:
        summ.append("추가됨: %s rows=%d" % (nm, cnt))
    summ_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_append_summary.txt")
    with open(summ_path, "w", encoding="utf-8") as f:
        f.write("\n".join(summ))

    sys.stdout.write("OK sheets=%d backup=%s\n" % (len(wb.sheetnames), os.path.basename(backup)))


if __name__ == "__main__":
    main()
