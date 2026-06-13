# -*- coding: utf-8 -*-
"""
11일차 결함을 결함배분표(권위)에 맞춤. day11.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day11(정합 완료)에서 추출.
- 존 카터(8, 밀수범·거절): 여권번호 위조 + X-ray 밀수 — 이미 정합(여권번호 위조 + xray) → 건드리지 않음
- 자오 친(77, 외관·거절): 여권 사진(FORCED_PHOTO) → 국적 불일치(여권≠비자)
- 윤정호(17, s4): 로스터 스왑에서 통과 처리 → 건드리지 않음
- 장소율(79, 성형◆=랜덤, MAIN 결함): 지문 도용-국적 — record(dbNat=CHN) 이미 정합 → 거절멘트/claim 동기화
- 앤드류 화이트(80, 외관·거절): 여권 사진(FORCED_PHOTO) → 여권 만료일
※ 조유진·천 징(통과)은 건드리지 않음.
실행: python Tools/_patch_day11_defects.py  (로스터 스왑 이후 실행)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf,
    apply_visa_field_mismatch, apply_fingerprint_stolen, apply_passport_expiry,
)

DAY = os.path.join(GAMEDATA, "day11.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 77 자오 친: 국적 불일치(여권≠비자). 사진 복구 + 비자 국적 다르게.
    c = cust[77]
    pp_nat = getf(get_doc(c, "여권"), "nationality")  # CHN
    apply_visa_field_mismatch(
        c, c, "nationality", "JPN", "국적",
        "여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.",
        "비자 국적 불일치")
    log.append(f"77 자오 친 → 국적 불일치(비자 JPN≠여권 {pp_nat}, 사진 복구)")

    # 79 장소율: 지문 도용-국적 (MAIN 이 결함, dbNat=CHN≠여권 KOR 이미 정합).
    #   거절멘트/claim/detail 을 국적 불일치 전용으로 동기화.
    c = cust[79]
    pp_nat = getf(get_doc(c, "여권"), "nationality")  # KOR
    wrong_nat = "CHN"
    apply_fingerprint_stolen(
        c, c, "nationality", wrong_nat,
        "지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"79 장소율(MAIN) → 지문 도용-국적(db {wrong_nat}≠여권 {pp_nat})")

    # 80 앤드류 화이트: 여권 만료일. 사진 복구 + expiry 과거.
    c = cust[80]
    apply_passport_expiry(
        c, c, "2024-03-28",
        "여권 유효기간이 맞지 않아 입국하실 수 없습니다.")
    log.append("80 앤드류 화이트 → 여권 만료일(사진 복구)")

    save(DAY, data)
    print("day11 결함 패치 완료:")
    for l in log:
        print("  -", l)
    print("  · 존 카터(8) 여권번호+X-ray 밀수 = 이미 정합(미변경)")
    print("  · 윤정호(17) 통과 = 로스터 스왑 처리(미변경)")


if __name__ == "__main__":
    main()
