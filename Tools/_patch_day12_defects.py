# -*- coding: utf-8 -*-
"""
12일차 결함을 결함배분표(권위)에 맞춤. day12.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day12(정합 완료)에서 추출.
- 사토 하루키(11, 테러범·거절): 여권 위조 + X-ray 위험물 — 이미 정합(여권번호+xray) → 건드리지 않음
- 한지아(82, 성형·거절, MAIN 결함): 지문 도용-이름 — dbName 강민호→KANG MINHO(여권 HAN JIA 와 EN 대조) + 거절멘트
- 오나은(83, 진상◆=랜덤, altVariant 결함): 여권 만료일 → 여권 성별
- 서하린(84, 성형◆=랜덤, altVariant 결함): 지문 도용(현 dbName 불일치=이름) → 지문 도용-생년월일
- 황 레이(85, 외관·거절): 여권 사진(FORCED_PHOTO) → 비자종류 거짓-진술(비자=취업/진술=관광)
※ 임가은·신예은(통과)은 건드리지 않음.
실행: python Tools/_patch_day12_defects.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf,
    apply_passport_gender, apply_fingerprint_stolen, apply_false_purpose,
)

DAY = os.path.join(GAMEDATA, "day12.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 82 한지아: 지문 도용-이름 (MAIN 결함). dbName 을 여권 EN 이름과 다르게.
    c = cust[82]
    pp_name = getf(get_doc(c, "여권"), "name")  # HAN JIA
    apply_fingerprint_stolen(
        c, c, "name", "KANG MINHO",
        "지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"82 한지아(MAIN) → 지문 도용-이름(db KANG MINHO≠여권 {pp_name})")

    # 83 오나은: 여권 성별 (altVariant 결함, 만료일 → 성별).
    c = cust[83]
    alt = c["altVariant"]
    apply_passport_gender(
        alt, c,
        "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.")
    log.append("83 오나은(alt) → 여권 성별(만료일 복구)")

    # 84 서하린: 지문 도용-생년월일 (altVariant 결함, 기존 이름 불일치 → 생년월일).
    c = cust[84]
    alt = c["altVariant"]
    pp_birth = getf(get_doc(alt, "여권"), "birth_date")  # 1996-04-12
    wrong_birth = "1992-08-23"
    apply_fingerprint_stolen(
        alt, c, "birth", wrong_birth,
        "지문 신원이 여권 생년월일과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"84 서하린(alt) → 지문 도용-생년월일(db {wrong_birth}≠여권 {pp_birth})")

    # 85 황 레이: 비자종류 거짓-진술. 비자=취업, 진술=관광. 사진 복구.
    c = cust[85]
    apply_false_purpose(
        c, c, "관광", "취업",
        "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append("85 황 레이 → 비자종류 거짓-진술(진술 관광 ↔ 비자 취업, 사진 복구)")

    save(DAY, data)
    print("day12 결함 패치 완료:")
    for l in log:
        print("  -", l)
    print("  · 사토 하루키(11) 여권위조+X-ray 위험물 = 이미 정합(미변경)")


if __name__ == "__main__":
    main()
