# -*- coding: utf-8 -*-
"""
13일차 결함을 결함배분표(권위)에 맞춤. day13.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day13(정합 완료)에서 추출.
- 우 팅(87, 외관·거절): 여권 사진(FORCED_PHOTO) → 이름 불일치(여권≠비자)
- 다카하시 리쿠(89, 외관·거절): 여권 사진(FORCED_PHOTO) → 국적 불일치(여권≠비자)
- 제시카 윌슨(37, 장기·거절): 취업증빙 입사일 모순 — hire_date>issue_date 이미 정합 → 거절멘트/claim 동기화
- 황민서(90, 진상◆=랜덤, altVariant 결함): 여권 만료일 — 이미 정합 → 거절멘트 동기화
- 배은서(91, 성형◆=랜덤, altVariant 결함): 지문 도용(현 이름 불일치) → 지문 도용-국적
- 쉬 펑(92, 외관·거절): 여권 사진(FORCED_PHOTO) → 비자종류 거짓-진술(비자=장기 체류/진술=관광)
※ 권서윤(통과)은 건드리지 않음.
실행: python Tools/_patch_day13_defects.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf, set_reject,
    apply_visa_field_mismatch, apply_passport_expiry, apply_fingerprint_stolen,
    apply_false_purpose,
)

DAY = os.path.join(GAMEDATA, "day13.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 87 우 팅: 이름 불일치(여권≠비자). 사진 복구 + 비자 name 다르게.
    c = cust[87]
    pp_name = getf(get_doc(c, "여권"), "name")  # WU TING
    apply_visa_field_mismatch(
        c, c, "name", "WU TINGFANG", "영문이름",
        "비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.",
        "비자 이름 불일치")
    log.append(f"87 우 팅 → 이름 불일치(비자 name≠여권 {pp_name}, 사진 복구)")

    # 89 다카하시 리쿠: 국적 불일치(여권≠비자). 사진 복구 + 비자 nationality 다르게.
    c = cust[89]
    pp_nat = getf(get_doc(c, "여권"), "nationality")  # JPN
    apply_visa_field_mismatch(
        c, c, "nationality", "KOR", "국적",
        "여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.",
        "비자 국적 불일치")
    log.append(f"89 다카하시 리쿠 → 국적 불일치(비자 KOR≠여권 {pp_nat}, 사진 복구)")

    # 37 제시카 윌슨: 취업증빙 입사일 모순 — hire_date>issue_date 이미 정합. 거절멘트/claim 동기화.
    c = cust[37]
    emp = get_doc(c, "취업증빙")
    emp["variant"] = "비정상"
    emp["violationField"] = "입사일"
    set_reject(c, "재직증명서 입사일이 비자 발급일보다 빨라 논리가 맞지 않습니다. 입국하실 수 없습니다.",
               {"attr": "hire_date", "value": getf(emp, "hire_date"),
                "label": "입사일 모순", "unlocksScan": ""})
    log.append("37 제시카 윌슨 → 취업증빙 입사일 모순(거절멘트 동기화)")

    # 90 황민서: 여권 만료일 (altVariant 결함). 이미 정합 → 거절멘트 동기화.
    c = cust[90]
    alt = c["altVariant"]
    expiry = getf(get_doc(alt, "여권"), "expiry_date")
    apply_passport_expiry(
        alt, c, expiry,
        "여권 유효기간이 맞지 않아 입국하실 수 없습니다.")
    log.append(f"90 황민서(alt) → 여권 만료일(거절멘트 동기화, expiry={expiry})")

    # 91 배은서: 지문 도용-국적 (altVariant 결함, 기존 이름 불일치 → 국적).
    c = cust[91]
    alt = c["altVariant"]
    pp_nat = getf(get_doc(alt, "여권"), "nationality")  # KOR
    apply_fingerprint_stolen(
        alt, c, "nationality", "CHN",
        "지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"91 배은서(alt) → 지문 도용-국적(db CHN≠여권 {pp_nat})")

    # 92 쉬 펑: 비자종류 거짓-진술. 비자=장기 체류, 진술=관광. 사진 복구.
    c = cust[92]
    apply_false_purpose(
        c, c, "관광", "장기 체류",
        "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append("92 쉬 펑 → 비자종류 거짓-진술(진술 관광 ↔ 비자 장기 체류, 사진 복구)")

    save(DAY, data)
    print("day13 결함 패치 완료:")
    for l in log:
        print("  -", l)


if __name__ == "__main__":
    main()
