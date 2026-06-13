# -*- coding: utf-8 -*-
"""
10일차 결함을 결함배분표(권위)에 맞춤. day10.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day10(정합 완료)에서 추출.
- 최수아(71, 진상◆=랜덤, altVariant 결함): 여권 만료일 → 여권번호 위조
- 정다은(72, 성형◆=랜덤, altVariant 결함): 지문 도용-생년월일 → 지문 도용-이름
- 스즈키 소라(73, 외관·거절): 여권 사진(FORCED_PHOTO) → 번호 불일치(여권≠비자)
- 첸 웨이(3, 외관·거절): 여권 사진(FORCED_PHOTO) → 비자종류 거짓-진술
※ 박하윤·첸 하오(통과)·강예린(이미 여권 만료일 정합)은 건드리지 않음.
실행: python Tools/_patch_day10_defects.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf,
    apply_passport_no_forge, apply_fingerprint_stolen, apply_visa_field_mismatch,
    apply_false_purpose,
)

DAY = os.path.join(GAMEDATA, "day10.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 71 최수아: 여권번호 위조 (altVariant 결함, 만료일 → 여권번호). KOR → 앞자리 JP.
    c = cust[71]
    alt = c["altVariant"]
    apply_passport_no_forge(
        alt, c, "JP1032393",
        "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.")
    log.append("71 최수아(alt) → 여권번호 위조(KO→JP, 만료일 복구)")

    # 72 정다은: 지문 도용-이름 (altVariant 결함, 생년월일 → 이름).
    c = cust[72]
    alt = c["altVariant"]
    wrong_name = "KANG MINHO"  # 여권 JEONG DAEUN 과 다른 신원
    apply_fingerprint_stolen(
        alt, c, "name", wrong_name,
        "지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"72 정다은(alt) → 지문 도용-이름(db {wrong_name}≠여권 JEONG DAEUN)")

    # 73 스즈키 소라: 번호 불일치(여권≠비자). 사진 복구 + 비자 passport_no 다르게.
    c = cust[73]
    pp_no = getf(get_doc(c, "여권"), "passport_no")  # JP1029042
    fake_no = "JP7070707"
    apply_visa_field_mismatch(
        c, c, "passport_no", fake_no, "여권번호",
        "비자의 여권번호가 여권과 일치하지 않아 입국하실 수 없습니다.",
        "비자 여권번호 불일치")
    log.append(f"73 스즈키 소라 → 번호 불일치(비자 {fake_no}≠여권 {pp_no}, 사진 복구)")

    # 3 첸 웨이: 비자종류 거짓-진술. 비자=관광, 진술=취업. 사진 복구.
    #   (비자종류 다양화 — 최종 visa_type 은 _patch_visa_purpose_diversify.py 가 단일 소스로 재확정)
    c = cust[3]
    apply_false_purpose(
        c, c, "취업", "관광",
        "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append("3 첸 웨이 → 비자종류 거짓-진술(진술 취업 ↔ 비자 관광, 사진 복구)")

    save(DAY, data)
    print("day10 결함 패치 완료:")
    for l in log:
        print("  -", l)


if __name__ == "__main__":
    main()
