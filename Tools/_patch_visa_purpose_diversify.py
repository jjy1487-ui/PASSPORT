# -*- coding: utf-8 -*-
"""
비자종류 거짓 진술(false purpose) 손님들의 비자 visa_type 을 다양화하고
입장 진술 claim(attr=visa_type)을 손님이 말하는 (거짓)목적으로 맞춘다. 멱등.
대사_스크립트와 일치(진술 ↔ 비자 불일치로 적발). 이 스크립트가 visa_type 의 단일 소스.

  손님            일차/슬롯   비자 visa_type   입장 진술(거짓 목적)   진술 근거(대사_스크립트)
  왕 팡(58)       day7 s5     관광            취업                "출장 왔어요, 업무 때문에"
  류 옌(67)       day9 s3     취업            관광                "친구 결혼식 보러 왔어요"
  첸 웨이(3)      day10 s7    관광            취업                "유학 왔어요, 공부하러"
  황 레이(85)     day12 s6    취업            관광                "그냥 관광 왔어요"
  쉬 펑(92)       day13 s7    장기 체류        관광                "잠깐 여행 왔어요"

※ 리강(day4)=관광/장기체류 는 유지(이 스크립트가 건드리지 않음).
거절멘트는 각 일차 _patch_dayN_defects.py 가 이미 동기화. 여기선 비자 type + 진술 claim 만 확정.
실행: python Tools/_patch_visa_purpose_diversify.py  (각 일차 결함 패치 이후 실행)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, setf, find_entry_case,
)

# (day, customer_id, visa_type, stated_purpose)
TARGETS = [
    (7, 58, "관광", "취업"),
    (9, 67, "취업", "관광"),
    (10, 3, "관광", "취업"),
    (12, 85, "취업", "관광"),
    (13, 92, "장기 체류", "관광"),
]


def set_stated_claim(c, stated):
    """입장 케이스에서 attr=visa_type 진술 claim 을 stated 로 맞춘다(없으면 손님 첫 라인에 부착)."""
    entry = find_entry_case(c)
    if entry is None:
        return False
    claim = {"attr": "visa_type", "value": stated,
             "label": "방문 목적(진술)", "unlocksScan": ""}
    # 이미 visa_type 진술 라인이 있으면 그 claim 갱신
    for ln in entry["lines"]:
        if ln.get("claim") and ln["claim"].get("attr") == "visa_type":
            ln["claim"] = claim
            return True
    visitor_lines = [ln for ln in entry["lines"]
                     if ln.get("speaker") == c.get("nameKr")]
    if visitor_lines:
        visitor_lines[0]["claim"] = claim
        return True
    return False


def main():
    log = []
    for day, cid, visa_type, stated in TARGETS:
        path = os.path.join(GAMEDATA, "day%d.json" % day)
        data = load(path)
        cust = by_id(data)
        c = cust.get(cid)
        if c is None:
            log.append(f"[skip] day{day} id{cid} 미발견")
            continue
        vz = get_doc(c, "비자")
        if vz is None:
            log.append(f"[skip] day{day} {c.get('nameKr')}({cid}) 비자 없음")
            continue
        setf(vz, "visa_type", visa_type)
        ok = set_stated_claim(c, stated)
        save(path, data)
        log.append("day%d %s(%d): 비자 visa_type=%s ↔ 진술=%s%s"
                   % (day, c.get("nameKr"), cid, visa_type, stated,
                      "" if ok else "  [진술 claim 부착 실패!]"))

    print("비자종류 거짓 진술 다양화 완료:")
    for l in log:
        print("  -", l)


if __name__ == "__main__":
    main()
