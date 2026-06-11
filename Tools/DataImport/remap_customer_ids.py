# -*- coding: utf-8 -*-
"""
remap_customer_ids.py — 연결 시트의 customer_id FK 재번호 (data-tools 소유)

배경: 사용자가 메인 엑셀 `data/여권_정리_updated.xlsx` 의 customer 시트에서만
클론 customer_id(1001~1061)를 38~98 연속번호로 손수 재번호함(원본 1~37 불변).
그런데 연결 시트와 day_schedule 은 아직 옛 클론 id(1001+)를 참조 → FK 깨짐.

확정 매핑: 새 id = 옛 id − 963 (클론 1001→38 … 1061→98). 원본 1~37 불변.
매핑 JSON: Tools/_id_map.json (키=옛id 문자열, 값=새id). 본 스크립트는 이 JSON 을 사용한다.

치환 대상 시트(customer_id 컬럼만 −963):
  passport, visa, pcr_test, employment_cert, fingerprint, day_schedule
  (xray 는 1001+ 행이 없지만 포함해도 안전 — 매핑 없는 값은 불변)
customer 시트는 건드리지 않는다(사용자가 이미 1~98 로 재번호).
sprite_ref/photo_ref(얼굴)·customer_name(표시명) 은 손대지 않는다 —
day_schedule/xray 의 customer_name 은 이미 클론 새 이름과 정합(검증됨).

idempotent: 1001+(또는 매핑 키에 있는) 값만 치환하므로, 이미 1~98 이면 변경 0.
엑셀이 열려 있어도 xlwings(COM)로 attach 편집한다(사용자에게 닫으라 하지 않음).

사용:
    python Tools/DataImport/remap_customer_ids.py
    python Tools/DataImport/remap_customer_ids.py --dry-run   # 변경 미리보기만
"""
import os
import sys
import json
import shutil
import datetime

# 콘솔이 cp949여도 안전하게 출력 (− 등 비-cp949 문자 대비)
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", ".."))
sys.path.insert(0, os.path.join(_REPO, "Tools"))

from xlsx_live_edit import get_book, SRC  # noqa: E402

MAP_JSON = os.path.join(_REPO, "Tools", "_id_map.json")

# 시트명 -> customer_id 컬럼 인덱스(1-based). 데이터는 row5+.
CUSTOMER_ID_COL = {
    "passport": 2,
    "visa": 2,
    "pcr_test": 2,
    "employment_cert": 2,
    "fingerprint": 2,
    "day_schedule": 4,
    "xray": 2,
}

HEADER_ROWS = 4  # LAYOUT_A: row1~4 헤더, 데이터 row5+


def load_map():
    with open(MAP_JSON, "r", encoding="utf-8") as f:
        raw = json.load(f)
    # 키(옛id 문자열) -> 값(새id int)
    return {int(k): int(v) for k, v in raw.items()}


def backup(path):
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    dst = f"{path}.bak_remap_{ts}"
    shutil.copy2(path, dst)
    return dst


def remap(dry_run=False):
    id_map = load_map()
    bk, opened = get_book()
    report = []
    total_changed = 0
    try:
        sheet_names = {s.name for s in bk.sheets}
        for sheet, col in CUSTOMER_ID_COL.items():
            if sheet not in sheet_names:
                report.append(f"  {sheet}: (시트 없음, 건너뜀)")
                continue
            ws = bk.sheets[sheet]
            last = ws.used_range.last_cell.row
            if last < HEADER_ROWS + 1:
                report.append(f"  {sheet}: (데이터 없음)")
                continue
            rng = ws.range((HEADER_ROWS + 1, col), (last, col))
            vals = rng.value
            if not isinstance(vals, list):
                vals = [vals]
            changed = 0
            new_vals = []
            for v in vals:
                if v is None:
                    new_vals.append([None])
                    continue
                cid = int(v)
                if cid in id_map:
                    new_vals.append([id_map[cid]])
                    changed += 1
                else:
                    new_vals.append([cid])
            if changed and not dry_run:
                rng.value = new_vals
            total_changed += changed
            report.append(f"  {sheet}: col{col} rows={len(vals)} changed={changed}")
        if not dry_run:
            bk.save()
    finally:
        if opened:
            bk.app.quit()
    return total_changed, report


def main():
    dry_run = "--dry-run" in sys.argv
    if not dry_run:
        bk_path = backup(SRC)
        print(f"[backup] {bk_path}")
    total, report = remap(dry_run=dry_run)
    print(("[dry-run] " if dry_run else "[remap] ") + f"customer_id FK −963 치환 ({SRC})")
    print("\n".join(report))
    print(f"[total] changed rows = {total}")


if __name__ == "__main__":
    main()
