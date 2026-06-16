# -*- coding: utf-8 -*-
"""대사_스크립트.xlsx Day12·13·14 — 각 손님 '인삿말' 블록 뒤 수하물 검사 2줄
(캐릭터별 심사관 질문 + 손님 반응)을 보장/갱신한다. _baggage_responses 의 문구 사용. 멱등.
열려 있어도 xlwings COM 라이브 편집. 실행 전 .bak 백업.
"""
import os
import sys
import io
import shutil

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Tools"))
sys.path.insert(0, HERE)
from xlsx_live_edit import get_book  # noqa: E402
from _baggage_responses import inspector_for, response_for, BAGGAGE_MARKER  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(ROOT, "data", "대사_스크립트.xlsx")

COL_BRANCH = 6   # 분기
COL_SPEAKER = 7  # 화자
COL_TEXT = 8     # 대사
BAGGAGE_BRANCH = "수하물 검사(X-ray)"


def sync_sheet(ws):
    name_count = {}
    updated = inserted = 0
    r = 3
    while r <= ws.used_range.last_cell.row:
        if (ws.range((r, COL_BRANCH)).value or "") == "인삿말":
            name = ws.range((r, COL_SPEAKER)).value or "손님"
            occ = name_count.get(name, 0)
            name_count[name] = occ + 1
            insp = inspector_for(name)
            resp = response_for(name, alt=(occ >= 1))

            nxt_branch = ws.range((r + 1, COL_BRANCH)).value or ""
            nxt_text = ws.range((r + 1, COL_TEXT)).value or ""
            has_block = nxt_branch == BAGGAGE_BRANCH or (BAGGAGE_MARKER in nxt_text and (ws.range((r + 1, COL_SPEAKER)).value or "") == "심사관")
            if has_block:
                ch = False
                if (ws.range((r + 1, COL_TEXT)).value or "") != insp:
                    # 분기 라벨은 기존 것 보존(Day11 '수하물 검사' 등) — 텍스트만 갱신
                    ws.range((r + 1, COL_SPEAKER)).value = "심사관"
                    ws.range((r + 1, COL_TEXT)).value = insp
                    ch = True
                if (ws.range((r + 2, COL_TEXT)).value or "") != resp:
                    ws.range((r + 2, COL_SPEAKER)).value = name
                    ws.range((r + 2, COL_TEXT)).value = resp
                    ch = True
                if ch:
                    updated += 1
                r += 3
                continue
            else:
                ws.range(f"{r + 1}:{r + 2}").api.Insert()
                ws.range((r + 1, COL_BRANCH)).value = BAGGAGE_BRANCH
                ws.range((r + 1, COL_SPEAKER)).value = "심사관"
                ws.range((r + 1, COL_TEXT)).value = insp
                ws.range((r + 2, COL_SPEAKER)).value = name
                ws.range((r + 2, COL_TEXT)).value = resp
                inserted += 1
                r += 3
                continue
        r += 1
    return updated, inserted


def main():
    if os.path.exists(SRC):
        bak = SRC.replace(".xlsx", ".bak_baggage.xlsx")
        if not os.path.exists(bak):
            try:
                shutil.copyfile(SRC, bak)
                print(f"[backup] {os.path.basename(bak)}")
            except Exception as e:
                print(f"[backup] 실패(무시): {e}")

    bk, opened = get_book(SRC)
    try:
        for sn in ("Day11", "Day12", "Day13", "Day14"):
            ws = bk.sheets[sn]
            u, i = sync_sheet(ws)
            print(f"{sn}: 갱신 {u} / 신규삽입 {i}")
        bk.save()
        print("[saved] data/대사_스크립트.xlsx")
    finally:
        if opened:
            bk.app.quit()


if __name__ == "__main__":
    main()
