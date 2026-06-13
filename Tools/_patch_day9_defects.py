# -*- coding: utf-8 -*-
"""
9일차 결함을 결함배분표(권위)에 맞춤. day9.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day9(정합 완료)에서 추출.
- 홍성민(65, 진상◆=랜덤, MAIN 결함): 여권 생년월일 → 여권 만료일
- 류 옌(67, 외관·거절): 여권 사진(FORCED_PHOTO) → 비자종류 거짓-진술
- 윤채원(78, s4): 로스터 스왑 스크립트(swap_yoonchaewon_yoonjeongho.py)에서 여권 성별로 처리(여기선 건드리지 않음)
- 이지우(68, 성형◆=랜덤, altVariant 결함): 지문 도용-이름 → 지문 도용-생년월일
- 크리스 토머스(69, 외관·거절): 여권 사진(FORCED_PHOTO) → 번호 불일치(여권≠비자)
- 자오 레이(35, 장기·거절): 유령회사(입국 금지 회사) — company_name 이미 정합 → 거절멘트/claim 동기화
※ 김서아(통과)는 건드리지 않음.
실행: python Tools/_patch_day9_defects.py  (로스터 스왑 이후 실행)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf, setf, set_reject,
    apply_passport_expiry, apply_false_purpose, apply_visa_field_mismatch,
    apply_fingerprint_stolen,
)

DAY = os.path.join(GAMEDATA, "day9.json")


def main():
    data = load(DAY)
    cust = by_id(data)
    log = []

    # 65 홍성민: 여권 만료일 (MAIN 이 결함). 생년월일 결함 복구.
    c = cust[65]
    apply_passport_expiry(
        c, c, "2024-03-14",
        "여권 유효기간이 맞지 않아 입국하실 수 없습니다.")
    log.append("65 홍성민(MAIN) → 여권 만료일(생년월일 복구)")

    # 67 류 옌: 비자종류 거짓-진술. 비자=취업, 진술=관광. 사진 복구.
    #   (비자종류 다양화 — 최종 visa_type 은 _patch_visa_purpose_diversify.py 가 단일 소스로 재확정)
    c = cust[67]
    apply_false_purpose(
        c, c, "관광", "취업",
        "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append("67 류 옌 → 비자종류 거짓-진술(진술 관광 ↔ 비자 취업, 사진 복구)")

    # 68 이지우: 지문 도용-생년월일 (altVariant 결함, 기존 이름 불일치 → 생년월일).
    c = cust[68]
    alt = c["altVariant"]
    pp_birth = getf(get_doc(alt, "여권"), "birth_date")  # LEE JIWOO 정상 생년월일
    wrong_birth = "1991-04-12"  # 여권(1996-04-12)과 다른 생년월일
    apply_fingerprint_stolen(
        alt, c, "birth", wrong_birth,
        "지문 신원이 여권 생년월일과 일치하지 않습니다. 입국하실 수 없습니다.")
    log.append(f"68 이지우(alt) → 지문 도용-생년월일(db {wrong_birth}≠여권 {pp_birth})")

    # 69 크리스 토머스: 번호 불일치(여권≠비자). 비자 passport_no 다르게, 사진 복구.
    c = cust[69]
    pp_no = getf(get_doc(c, "여권"), "passport_no")  # US1027925
    fake_no = "US9090909"
    apply_visa_field_mismatch(
        c, c, "passport_no", fake_no, "여권번호",
        "비자의 여권번호가 여권과 일치하지 않아 입국하실 수 없습니다.",
        "비자 여권번호 불일치")
    log.append(f"69 크리스 토머스 → 번호 불일치(비자 {fake_no}≠여권 {pp_no}, 사진 복구)")

    # 35 자오 레이: 유령회사 — company_name=페이퍼컴퍼니(주) 이미 정합. 거절멘트/claim 동기화.
    c = cust[35]
    emp = get_doc(c, "취업증빙")
    emp["variant"] = "비정상"
    emp["violationField"] = "고용 회사"
    company = getf(emp, "company_name")
    set_reject(c, "입국이 제한된 회사 소속으로 확인되어 입국하실 수 없습니다.",
               {"attr": "company_name", "value": company, "label": "입국 금지 회사", "unlocksScan": ""})
    log.append(f"35 자오 레이 → 유령회사({company}, 거절멘트 동기화)")

    save(DAY, data)
    print("day9 결함 패치 완료:")
    for l in log:
        print("  -", l)


if __name__ == "__main__":
    main()
