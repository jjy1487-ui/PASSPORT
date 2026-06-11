# -*- coding: utf-8 -*-
"""
rebuild_customer_order_sheet.py — 고객순서(유형·이름) 시트 재생성 (data-tools 소유)

목적: 메인 엑셀 `data/여권_정리_updated.xlsx` 의 `고객순서(유형·이름)` 시트(사람이 보는 요약표)를
현재 day_schedule(클론 반영, customer_id 1~98) 에 맞춰 다시 채운다.

시트 구조: row1=헤더(일차 | 요구 서류 | 방1..방7), row2~r15=일차 1~14.
각 칸(방N) 표기 형식: "{유형약칭}·{결과}{기호}({이름})"
  - 유형약칭: customer.character_type -> 약칭 (TYPE2ABBR)
  - 결과: 통과 / 거절 / 랜덤
  - 기호: ★(특수/튜토리얼) · ◆(랜덤)

결과 결정 규칙(기존 시트 철학 = day_schedule.valid_chance 기반):
  이 요약표는 "슬롯 정상 확률(valid_chance)"을 보여준다. day JSON 이 vc 를 한 번
  굴려 확정한 correctResult 가 아니라, 슬롯의 확률 성격(통과/거절/랜덤)을 표기한다.
  - vc == 1.0           -> 통과
  - vc == 0.0           -> 거절
  - 0 < vc < 1.0        -> 랜덤◆
       단, character_type 이 '항상 적발(거절)' 유형(범죄자 계열·테러범)이면
       vc 가 중간값이어도 거절로 표기(기존 시트의 성형범죄자 vc=0.7 -> 거절 관례).
       (다른 적발 유형 — 전염병/밀수/마약 — 은 day_schedule 에서 vc=0.0 이라 자동 거절.)

기호(★) 규칙:
  - character_type 문자열에 '★' 포함(특수 연예인/정치인/현자) -> ★
  - (day1, slot1) 첫 손님(튜토리얼)도 ★  (기존 시트 관례 보존)
  - 결과가 랜덤이면 ◆

요구 서류 열: 기존 시트 값(있으면)을 보존, 없으면 document_requirement 로 계산.
이름은 customer.name_kr (클론 새 이름 반영). day_schedule.customer_name 과 동일.

idempotent: 같은 day_schedule -> 같은 표. 엑셀이 열려 있어도 xlwings 로 attach 편집.
타임스탬프 백업 후 편집.

사용:
    python Tools/DataImport/rebuild_customer_order_sheet.py
    python Tools/DataImport/rebuild_customer_order_sheet.py --dry-run
"""
import os
import sys
import shutil
import datetime

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", ".."))
sys.path.insert(0, os.path.join(_REPO, "Tools"))

from xlsx_live_edit import get_book, SRC  # noqa: E402

SHEET = "고객순서(유형·이름)"

# character_type(풀네임) -> 약칭. 새 유형이 오면 여기만 갱신(변경 흡수 단일 지점).
TYPE2ABBR = {
    "일반 고객": "일반",
    "외국인 관광객": "외관",
    "진상 고객": "진상",
    "성형 수술 고객": "성형",
    "범죄자(성형수술)": "성형범죄자",
    "범죄자(밀수품 범죄자)": "밀수범",
    "범죄자(마약 범죄자)": "마약범",
    "테러범": "테러범",
    "전염병 환자": "전염병",
    "장기체류자": "장기",
    "특수(연예인)★": "연예",
    "특수(정치인)★": "정치",
    "특수(현자)★": "현자",
}


def abbr_for(ctype):
    if ctype in TYPE2ABBR:
        return TYPE2ABBR[ctype]
    # 폴백: 괄호/★ 제거한 앞부분
    base = str(ctype).split("(")[0].replace("★", "").strip()
    return base or str(ctype)


def is_special(ctype):
    return "★" in str(ctype)


def is_always_reject_type(ctype):
    """적발 대상(항상 거절) 유형: 범죄자 계열·테러범. 중간 vc 여도 거절로 표기."""
    s = str(ctype)
    return s.startswith("범죄자") or s == "테러범"


def backup(path):
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    dst = f"{path}.bak_custorder_{ts}"
    shutil.copy2(path, dst)
    return dst


def read_schedule(bk):
    """(day,slot) -> (customer_id, valid_chance). customer id->(name,type) 도 반환."""
    ws = bk.sheets["customer"]
    last = ws.used_range.last_cell.row
    rows = ws.range((5, 1), (last, 9)).value
    cust = {int(r[0]): {"name": r[1], "type": r[8]} for r in rows if r[0] is not None}
    ws = bk.sheets["day_schedule"]
    last = ws.used_range.last_cell.row
    rows = ws.range((5, 1), (last, 5)).value
    sched = {}
    for r in rows:
        if r[3] is None:
            continue
        sched[(int(r[1]), int(r[2]))] = (int(r[3]), r[4])
    return sched, cust


def decide_result_symbol(ctype, valid_chance):
    """(결과, 랜덤여부). 결과='통과'|'거절'|'랜덤'. valid_chance(슬롯 정상확률) 기반."""
    vc = None
    try:
        vc = float(valid_chance) if valid_chance is not None else None
    except (ValueError, TypeError):
        vc = None
    if vc is None:
        return "랜덤", True
    if vc >= 1.0:
        return "통과", False
    if vc <= 0.0:
        return "거절", False
    # 0 < vc < 1: 보통은 랜덤◆. 단 적발(항상거절) 유형은 거절로 고정.
    if is_always_reject_type(ctype):
        return "거절", False
    return "랜덤", True


def build_cell(ctype, name, valid_chance, is_tutorial):
    abbr = abbr_for(ctype)
    result, is_random = decide_result_symbol(ctype, valid_chance)
    sym = ""
    if is_random:
        sym = "◆"
    elif is_special(ctype) or is_tutorial:
        sym = "★"
    return f"{abbr}·{result}{sym}({name})"


def rebuild(dry_run=False):
    bk, opened = get_book()
    changes = []
    try:
        sched, cust = read_schedule(bk)
        ws = bk.sheets[SHEET]
        # 기존 시트의 '요구 서류'(col2) 보존을 위해 현재 값 읽기
        nr = ws.used_range.last_cell.row
        existing = ws.range((2, 1), (max(nr, 15), 2)).value
        req_by_day = {}
        for row in existing:
            if row[0] is None:
                continue
            req_by_day[int(float(row[0]))] = row[1]
        # 일차 1~14 = 시트 행 2~15
        for day in range(1, 15):
            r = day + 1  # row index (1-based): day1->row2
            ws.range((r, 1)).value = day
            if day in req_by_day and req_by_day[day]:
                ws.range((r, 2)).value = req_by_day[day]
            for slot in range(1, 8):
                col = 2 + slot  # 방1 -> col3
                if (day, slot) not in sched:
                    continue
                cid, vc = sched[(day, slot)]
                ctype = cust[cid]["type"]
                name = cust[cid]["name"]
                is_tut = (day == 1 and slot == 1)
                cell = build_cell(ctype, name, vc, is_tut)
                old = ws.range((r, col)).value
                old = "" if old is None else str(old).strip()
                if old != cell:
                    changes.append((day, slot, old, cell))
                if not dry_run:
                    ws.range((r, col)).value = cell
        if not dry_run:
            bk.save()
    finally:
        if opened:
            bk.app.quit()
    return changes


def main():
    dry_run = "--dry-run" in sys.argv
    if not dry_run:
        bk_path = backup(SRC)
        print(f"[backup] {bk_path}")
    changes = rebuild(dry_run=dry_run)
    print(("[dry-run] " if dry_run else "[rebuild] ") + f"고객순서 시트 재생성 ({SHEET})")
    for day, slot, old, new in changes:
        print(f"  d{day} s{slot}: {old!r} -> {new!r}")
    print(f"[total] changed cells = {len(changes)}")


if __name__ == "__main__":
    main()
