# -*- coding: utf-8 -*-
"""
장 민(62, day8 s3) 특수 정합: 외국인 관광객 → 장기체류자, 국적 불일치 제거 → 재직증명서 이름 불일치.
결함배분표 8DAY s3 = "재직증명서 이름 불일치(검사서≠여권)". 멱등.
  - characterType: 외국인 관광객 → 장기체류자
  - 비자 정상화: nationality CHN(여권과 일치), visa_type 장기 체류 → 국적불일치 결함 제거
  - 취업증빙(재직증명서) 추가: name='ZHANG WEI'(≠여권 ZHANG MIN = 신분 도용), 그 외 정상
  - correctResult = 정상 거절(불변), 거절멘트 = 대사_스크립트 Day8 장 민
대사 자연화/특수 이식은 2단계 — 결함·구조·판정·거절문만.
실행: python Tools/_patch_jangmin_employment.py  (day8 결함 패치 이후/대신 실행)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _reconcile_helpers import (  # noqa: E402
    GAMEDATA, load, save, by_id, get_doc, getf,
    normalize_visa, apply_employment_name_mismatch,
)

DAY = os.path.join(GAMEDATA, "day8.json")
ID_JANGMIN = 62
FAKE_EMP_NAME = "ZHANG WEI"  # 여권 ZHANG MIN 과 다른 이름(신분 도용)
REJECT = "재직증명서가 본인 것과 맞지 않아서, 이대로는 입국이 어렵습니다."


def main():
    data = load(DAY)
    cust = by_id(data)
    c = cust.get(ID_JANGMIN)
    if c is None:
        print("[error] 장 민(62) 미발견 — day8 에 없음. 중단.")
        return 2

    pp_name = getf(get_doc(c, "여권"), "name")  # ZHANG MIN
    pp_nat = getf(get_doc(c, "여권"), "nationality")  # CHN

    # 1) 장기체류자로 종류 변경
    c["characterType"] = "장기체류자"

    # 2) 비자 정상화(국적 CHN 일치) + 장기 체류로
    normalize_visa(c, c, visa_type="장기 체류")

    # 3) 재직증명서 이름 불일치 결함(취업증빙 추가/갱신)
    apply_employment_name_mismatch(
        c, c, FAKE_EMP_NAME,
        cert_no="EMP-062", company="한성테크(주)", job_title="생산 라인",
        hire_date="2023-05-01", issue_date="2025-05-01",
        reject_text=REJECT)

    save(DAY, data)
    print("장 민(62) 특수 정합 완료:")
    print(f"  - characterType → 장기체류자")
    print(f"  - 비자 국적 정상화({pp_nat} 일치), visa_type=장기 체류")
    print(f"  - 취업증빙 추가: name={FAKE_EMP_NAME} ≠ 여권 {pp_name}(신분 도용)")
    print(f"  - correctResult=정상 거절, 거절멘트 동기화")
    return 0


if __name__ == "__main__":
    sys.exit(main())
