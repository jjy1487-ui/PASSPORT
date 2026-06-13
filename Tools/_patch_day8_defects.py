# -*- coding: utf-8 -*-
"""
8일차 결함을 결함배분표(권위)에 맞춤. day8.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day8(정합 완료)에서 추출.
- 송재윤(61, 진상◆=랜덤): altVariant 여권 만료일 → 여권번호 위조(앞자리 발급국≠)
- 장 민(62, 외관·거절): 여권 사진(FORCED_PHOTO) → 국적 불일치(여권≠비자)
- 매튜 앤더슨(63, 외관·거절): 여권 사진(FORCED_PHOTO) → 이름 불일치(여권≠비자)
- 전도현(64, 성형◆=랜덤): altVariant 지문 도용-이름 이미 정합 → 거절멘트/claim 만 동기화
※ 이미 정합인 손님(안준영/첸 리/오현석 통과)은 건드리지 않음.
실행: python Tools/_patch_day8_defects.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf, set_reject,
    apply_passport_no_forge, apply_visa_field_mismatch, apply_fingerprint_stolen,
)

DAY = os.path.join(GAMEDATA, "day8.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 61 송재윤: 여권번호 위조 (랜덤 → altVariant 가 결함)
    c = cust[61]
    alt = c["altVariant"]
    apply_passport_no_forge(
        alt, c, "JP1022340",
        "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.")
    log.append("61 송재윤(alt) → 여권번호 위조(KO→JP, 만료일 복구)")

    # 62 장 민: 국적 불일치(여권≠비자) — 사진 강제 제거 + 비자 국적 다르게
    c = cust[62]
    pp_nat = getf(get_doc(c, "여권"), "nationality")  # CHN
    apply_visa_field_mismatch(
        c, c, "nationality", "JPN", "국적",
        "여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.",
        "비자 국적 불일치")
    log.append(f"62 장 민 → 국적 불일치(비자 nationality JPN≠여권 {pp_nat}, 사진 복구)")

    # 63 매튜 앤더슨: 이름 불일치(여권≠비자) — 사진 강제 제거 + 비자 이름 다르게
    c = cust[63]
    fake = "MATHEW ANDERSEN"  # 여권 MATTHEW ANDERSON 과 미세 차이(위조)
    apply_visa_field_mismatch(
        c, c, "name", fake, "영문이름",
        "비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.",
        "비자 이름 불일치")
    log.append("63 매튜 앤더슨 → 이름 불일치(비자 name≠여권 name, 사진 복구)")

    # 64 전도현: 지문 도용-이름 (랜덤 → altVariant 가 결함). 이미 CHOI JUWON 불일치.
    #   거절멘트/claim 만 대사_스크립트와 동기화(지문 이름 불일치 전용 멘트).
    c = cust[64]
    alt = c["altVariant"]
    apply_fingerprint_stolen(
        alt, c, "name", "CHOI JUWON",
        "지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append("64 전도현(alt) → 지문 도용-이름(CHOI JUWON, 거절멘트 동기화)")

    save(DAY, data)
    print("day8 결함 패치 완료:")
    for l in log:
        print("  -", l)


if __name__ == "__main__":
    main()
