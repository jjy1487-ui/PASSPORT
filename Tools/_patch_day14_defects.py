# -*- coding: utf-8 -*-
"""
14일차 결함을 결함배분표(권위)에 맞춤. day14.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day14(정합 완료)에서 추출.
- 선 메이(94, 외관·거절): 여권 사진(FORCED_PHOTO) → 번호 불일치(여권≠비자)
- 강도식(9, 마약범·거절): 여권번호 위조 + X-ray 마약 — 이미 정합 → 건드리지 않음
- 양수빈(96, 성형◆=랜덤, altVariant 결함): 지문 도용(현 생년월일) → 지문 도용-이름
    dbName='KIM HAEUN'(≠여권 YANG SUBIN), dbBirth=여권과 같게(1999-05-05). detail=신원 불일치, claim attr=name.
- 에밀리 클락(97, 외관·거절): 여권 사진 — 결함배분표대로 사진 유지(FORCED_PHOTO 잔존). 거절멘트만 사진 전용으로 동기화.
※ 문지유·와타나베 하나·백지원(통과)은 건드리지 않음.
실행: python Tools/_patch_day14_defects.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf, set_reject,
    apply_visa_field_mismatch, apply_fingerprint_stolen,
)

DAY = os.path.join(GAMEDATA, "day14.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 94 선 메이: 번호 불일치(여권≠비자). 사진 복구 + 비자 passport_no 다르게.
    c = cust[94]
    pp_no = getf(get_doc(c, "여권"), "passport_no")  # CN5551234
    fake_no = "CN9998877"
    apply_visa_field_mismatch(
        c, c, "passport_no", fake_no, "여권번호",
        "비자의 여권번호가 여권과 일치하지 않아 입국하실 수 없습니다.",
        "비자 여권번호 불일치")
    log.append(f"94 선 메이 → 번호 불일치(비자 {fake_no}≠여권 {pp_no}, 사진 복구)")

    # 96 양수빈: 지문 도용-이름 (altVariant 결함, 기존 생년월일 → 이름).
    #   dbName=KIM HAEUN(≠여권 YANG SUBIN), dbBirth=여권과 동일(1999-05-05).
    c = cust[96]
    alt = c["altVariant"]
    pp_name = getf(get_doc(alt, "여권"), "name")  # YANG SUBIN
    apply_fingerprint_stolen(
        alt, c, "name", "KIM HAEUN",
        "지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"96 양수빈(alt) → 지문 도용-이름(db KIM HAEUN≠여권 {pp_name}, 생년월일 일치)")

    # 97 에밀리 클락: 여권 사진(결함배분표 유지). 거절멘트만 사진 전용으로 동기화.
    c = cust[97]
    pp = get_doc(c, "여권")
    pp["variant"] = "비정상"
    pp["violationField"] = "사진"
    # spriteRef 는 디코이(다른 얼굴) 유지 — 사진 결함이므로 복구하지 않음.
    set_reject(c, "여권 사진이 본인과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "face", "value": "", "label": "사진 불일치", "unlocksScan": ""})
    log.append("97 에밀리 클락 → 여권 사진(유지, 거절멘트 동기화)")

    save(DAY, data)
    print("day14 결함 패치 완료:")
    for l in log:
        print("  -", l)
    print("  · 강도식(9) 여권번호 위조+X-ray 마약 = 이미 정합(미변경)")


if __name__ == "__main__":
    main()
