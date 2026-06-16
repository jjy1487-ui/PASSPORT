# -*- coding: utf-8 -*-
"""존카터 단순화(밀수품만) — 엑셀 설계doc 정리.
 1) 대사_스크립트.xlsx Day11: 존카터 블록에서 '서류 대조(여권번호↔발급국)' + '지문 검사(경보 명단↔신원)' 비트 삭제
    (각 심사관 비트 행 + 다음 손님 반응 행). 인삿말·수하물검사(적발)·거부·허가는 보존.
 2) 일자별_결함배분표.xlsx 11DAY: 존카터 결함 '여권번호 위조 + X-ray=밀수' → 'X-ray=밀수'.
멱등(이미 없으면 skip). 열려 있어도 xlwings COM. 실행 전 .bak 백업.
day11.json(여권 정상화 등) 적용은 별도(검수 후).
"""
import os
import sys
import io
import shutil

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Tools"))
from xlsx_live_edit import get_book  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
DIALOGUE = os.path.join(ROOT, "data", "대사_스크립트.xlsx")
DEFECT = os.path.join(ROOT, "data", "일자별_결함배분표.xlsx")

C_VISITOR, C_BRANCH = 3, 6
DELETE_BEATS = ("서류 대조", "지문 검사")


def backup(path, tag):
    bak = path.replace(".xlsx", f".bak_{tag}.xlsx")
    if not os.path.exists(bak):
        try:
            shutil.copyfile(path, bak)
            print(f"[backup] {os.path.basename(bak)}")
        except Exception as e:
            print(f"[backup] 실패(무시): {e}")


def delete_jc_beats(ws):
    last = ws.used_range.last_cell.row
    # 존카터 인삿말 행
    jc = None
    for r in range(3, last + 1):
        if (ws.range((r, C_VISITOR)).value or "") == "존 카터" and (ws.range((r, C_BRANCH)).value or "") == "인삿말":
            jc = r
            break
    if jc is None:
        print("[대사] 존카터 인삿말 못 찾음")
        return 0
    # 다음 손님 인삿말 = 블록 끝
    nxt = last + 1
    for r in range(jc + 1, last + 1):
        if (ws.range((r, C_BRANCH)).value or "") == "인삿말":
            nxt = r
            break
    # 블록 내 삭제 대상(비트 행 + 다음 반응 행)
    to_del = []
    for r in range(jc, nxt):
        br = ws.range((r, C_BRANCH)).value or ""
        if any(br.startswith(p) for p in DELETE_BEATS):
            to_del.append(r)
            to_del.append(r + 1)
    for r in sorted(set(to_del), reverse=True):
        ws.range(f"{r}:{r}").api.Delete()
    return len(set(to_del))


def edit_defect(ws):
    last = ws.used_range.last_cell.row
    for r in range(1, last + 1):
        if (ws.range((r, 2)).value or "") == "존 카터":
            cur = ws.range((r, 5)).value or ""
            new = cur.replace("여권번호 위조 + ", "")
            if new != cur:
                ws.range((r, 5)).value = new
                print(f"[결함배분표] 존카터 결함: {cur!r} -> {new!r}")
                return True
            print(f"[결함배분표] 이미 정리됨: {cur!r}")
            return False
    print("[결함배분표] 존카터 행 못 찾음")
    return False


def main():
    backup(DIALOGUE, "jc_simplify")
    backup(DEFECT, "jc_simplify")

    bk, opened = get_book(DIALOGUE)
    try:
        n = delete_jc_beats(bk.sheets["Day11"])
        print(f"[대사] Day11 존카터 비트 삭제 {n}행")
        bk.save()
    finally:
        if opened:
            bk.app.quit()

    bk2, opened2 = get_book(DEFECT)
    try:
        edit_defect(bk2.sheets["11DAY"])
        bk2.save()
        print("[saved] 두 엑셀 정리 완료")
    finally:
        if opened2:
            bk2.app.quit()


if __name__ == "__main__":
    main()
