# -*- coding: utf-8 -*-
"""규정 15 일반화: '성형·지문 확인' → '지문 신원 확인'.
성형 전용 문구를 '거동이 수상하거나 얼굴이 사진과 다른' 일반 지문 신원 확인으로 넓힌다.
엑셀 rule_book 시트 라이브 편집(xlwings/COM, 열려 있어도 편집). idempotent(재실행 안전).
day3.json / GameData.source.json 패치는 별도(Edit)로 수행한다 — '엑셀 먼저' 단계.
"""
import os
import sys
import io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "Tools"))
from xlsx_live_edit import get_book  # noqa: E402

RULE_ID = 15
NEW_TITLE = "지문 신원 확인"
NEW_CONTENT = (
    "거동이 수상하거나 얼굴이 여권 사진과 상이한 입국자는 지문으로 실제 신원을 확인한다.\n"
    "　▸ 지문 신원(성명·생년월일·국적) = 여권과 일치\n"
    "※ 지문 신원이 여권과 상이할 경우 신분 도용·위조로 입국을 불허한다."
)


def col_index(ws, en_name):
    """row3(영문 컬럼명)에서 en_name 컬럼의 1-indexed 위치를 찾는다."""
    for c in range(1, ws.used_range.last_cell.column + 1):
        if (ws.range((3, c)).value or "") == en_name:
            return c
    raise KeyError(f"column {en_name!r} not found in row3")


def main():
    bk, opened = get_book()
    try:
        ws = bk.sheets["rule_book"]
        last = ws.used_range.last_cell.row
        c_id = col_index(ws, "rule_id")
        c_title = col_index(ws, "rule_title")
        c_content = col_index(ws, "rule_content")

        row = None
        for r in range(5, last + 1):
            v = ws.range((r, c_id)).value
            if v is not None and int(v) == RULE_ID:
                row = r
                break
        if row is None:
            print(f"[rule_book] id={RULE_ID} NOT FOUND — 중단")
            return

        old_t = ws.range((row, c_title)).value
        old_c = ws.range((row, c_content)).value
        print(f"[before] row={row} title={old_t!r}")
        print(f"[before] content={old_c!r}")

        if old_t == NEW_TITLE and old_c == NEW_CONTENT:
            print("[rule_book] 이미 새 문구 (skip)")
            return

        ws.range((row, c_title)).value = NEW_TITLE
        ws.range((row, c_content)).value = NEW_CONTENT
        bk.save()
        print(f"[after ] title={NEW_TITLE!r}")
        print(f"[after ] content={NEW_CONTENT!r}")
        print("[saved] data/여권_정리_updated.xlsx")
    finally:
        if opened:
            bk.app.quit()


if __name__ == "__main__":
    main()
