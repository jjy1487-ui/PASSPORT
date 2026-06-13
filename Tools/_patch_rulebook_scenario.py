# -*- coding: utf-8 -*-
"""규정집 시나리오 정리 패치 (rule_book 시트):
  C) 금지물품(rule_id=4) day 1 -> 11
  D) PCR 방역지시(id5)+PCR검사서(id7) -> id7 하나로 통합, id5 행 삭제 (endDay=7 유지)

엑셀이 열려 있어도 xlwings(COM)로 그 인스턴스에 붙어 편집한다.
idempotent: 이미 적용돼 있으면 변경 없음.
"""
import os, sys
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_live_edit import get_book

UNIFIED_PCR = (
    "검역대상자는 PCR 검사서를 제출해야 한다.\n"
    "검사 결과가 양성이거나 유효 기간이 지났거나, 검사서 정보가 여권과 일치하지 않으면 입국을 거부한다.\n"
    "검사서의 이름, 국적, 검사번호, 검사일, 결과, 유효기간을 확인한다."
)

bk, opened = get_book()
try:
    ws = bk.sheets["rule_book"]
    # 데이터 행 스캔 (row 5부터), rule_id->row 매핑
    last = ws.range("A" + str(ws.cells.last_cell.row)).end("up").row
    rid_row = {}
    for r in range(5, last + 1):
        rid = ws.range((r, 1)).value
        if rid is None:
            continue
        rid_row[int(rid)] = r
    print("rule_id -> row:", rid_row)

    # ── C) rule_id=4 day -> 11 ──
    if 4 in rid_row:
        r4 = rid_row[4]
        cur = ws.range((r4, 2)).value
        if int(cur) != 11:
            ws.range((r4, 2)).value = 11
            print(f"[C] rule_id=4 day {cur} -> 11 (row {r4})")
        else:
            print("[C] rule_id=4 already day=11 (noop)")
    else:
        print("[C] rule_id=4 not found!")

    # ── D) rule_id=7 content 통합 + endDay=7 유지 ──
    if 7 in rid_row:
        r7 = rid_row[7]
        ws.range((r7, 4)).value = UNIFIED_PCR        # content
        ws.range((r7, 2)).value = 5                  # day 유지
        ws.range((r7, 6)).value = 7                  # end_day 유지
        print(f"[D] rule_id=7 content unified, day=5, end_day=7 (row {r7})")
    else:
        print("[D] rule_id=7 not found!")

    # ── D) rule_id=5 행 삭제 ──
    if 5 in rid_row:
        r5 = rid_row[5]
        ws.range(f"{r5}:{r5}").api.Delete()
        print(f"[D] rule_id=5 row deleted (row {r5})")
    else:
        print("[D] rule_id=5 already removed (noop)")

    bk.save()
    print("saved.")
finally:
    if opened:
        bk.app.quit()
