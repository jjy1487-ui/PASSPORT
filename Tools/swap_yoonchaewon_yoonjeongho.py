# -*- coding: utf-8 -*-
"""
로스터 스왑(윤채원 78 ↔ 윤정호 17) — 결함배분표 기준. 멱등.

현재 게임 → 목표(표)
  - 윤정호(17): day9 slot4(통과)  ->  day11 slot4 (통과 유지)
  - 윤채원(78): day11 slot4(랜덤,만료일) -> day9 slot4 (진상◆ 랜덤, altVariant=여권 성별)

둘 다 한국인. 얼굴 겹침 검증:
  - 윤채원 face=한만수 → day9 다른 얼굴과 겹치지 않음
  - 윤정호 face=윤정호 → day11 다른 얼굴과 겹치지 않음
결함 손님(다른 슬롯)은 절대 건드리지 않는다.
실행: python Tools/swap_yoonchaewon_yoonjeongho.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, apply_passport_gender, get_doc, getf,
)

DAY9 = os.path.join(GAMEDATA, "day9.json")
DAY11 = os.path.join(GAMEDATA, "day11.json")
ID_YOONJEONGHO = 17
ID_YOONCHAEWON = 78
SLOT = 4

GENDER_REJECT = "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다."


def find_by_id(custs, cid):
    for c in custs:
        if c.get("customerId") == cid:
            return c
    return None


def find_by_slot(custs, slot):
    for c in custs:
        if c.get("slot") == slot:
            return c
    return None


def main():
    day9 = load(DAY9)
    day11 = load(DAY11)

    yj = find_by_id(day9["customers"], ID_YOONJEONGHO) \
        or find_by_id(day11["customers"], ID_YOONJEONGHO)
    yc = find_by_id(day11["customers"], ID_YOONCHAEWON) \
        or find_by_id(day9["customers"], ID_YOONCHAEWON)
    if yj is None or yc is None:
        print("[error] 윤정호(17)/윤채원(78) 중 일부 미발견. 중단.")
        return 2

    # 멱등 판정: 이미 목표 상태?
    d9s4 = find_by_slot(day9["customers"], SLOT)
    d11s4 = find_by_slot(day11["customers"], SLOT)
    already = (
        d9s4 is not None and d9s4.get("customerId") == ID_YOONCHAEWON
        and d11s4 is not None and d11s4.get("customerId") == ID_YOONJEONGHO
    )

    if not already:
        # 깊은 복사본 이동
        yc_new = json.loads(json.dumps(yc))
        yj_new = json.loads(json.dumps(yj))
        yc_new["slot"] = SLOT
        yj_new["slot"] = SLOT

        # day9 customers: 윤정호/윤채원 제거 후 윤채원 삽입
        day9["customers"] = [c for c in day9["customers"]
                             if c.get("customerId") not in (ID_YOONJEONGHO, ID_YOONCHAEWON)]
        day9["customers"].append(yc_new)
        # day11 customers: 윤정호/윤채원 제거 후 윤정호 삽입
        day11["customers"] = [c for c in day11["customers"]
                              if c.get("customerId") not in (ID_YOONJEONGHO, ID_YOONCHAEWON)]
        day11["customers"].append(yj_new)

        day9["customers"].sort(key=lambda c: c.get("slot", 0))
        day11["customers"].sort(key=lambda c: c.get("slot", 0))
    else:
        yc_new = d9s4

    # 윤채원(day9 s4): altVariant 결함을 여권 성별로(만료일 → 성별). MAIN 은 통과(정상).
    alt = yc_new.get("altVariant")
    if alt is None:
        print("[error] 윤채원 altVariant 없음 — 결함 변형 구성 불가.")
        return 2
    # 만료일 잔여 결함 정상화 후 성별 결함 1개만.
    pp = get_doc(alt, "여권")
    main_pp = get_doc(yc_new, "여권")
    normal_expiry = getf(main_pp, "expiry_date")
    if normal_expiry:
        from _reconcile_helpers import setf
        setf(pp, "expiry_date", normal_expiry)
    apply_passport_gender(alt, yc_new, GENDER_REJECT)

    save(DAY9, day9)
    save(DAY11, day11)
    print("로스터 스왑 완료(윤채원→day9 s4 여권성별 / 윤정호→day11 s4 통과):")
    print("  - 윤채원(78) day9 s4: altVariant 여권 성별(만료일 복구)")
    print("  - 윤정호(17) day11 s4: 통과(무결함)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
