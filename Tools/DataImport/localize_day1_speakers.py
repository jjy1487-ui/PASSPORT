# -*- coding: utf-8 -*-
"""
localize_day1_speakers.py — day1.json(수작업 완성본)의 손님측 화자명을 실제 이름으로 치환.

build_days.py 는 day2~14 를 생성하며 손님측 speaker("캐릭터"/"손님")를 그 손님의
nameKr 로 치환한다(localize_speakers). day1.json 은 수작업본이라 build 가 건드리지 않으므로,
동일 규칙을 day1.json 에도 적용한다.

규칙(build_days.CUSTOMER_SPEAKERS 와 동일 단일 기준):
  - 손님측 speaker 값("캐릭터"/"손님")만 그 손님의 nameKr 로 치환.
  - 심사관/검사관/시스템 등 비손님 화자는 보존.
  - 이미 nameKr 로 치환된 값은 손님측 집합에 없으므로 재실행해도 불변(idempotent).
  - text/order/case 구조는 불변. speaker 만 교체.

실행: python Tools/DataImport/localize_day1_speakers.py
"""
import json
import os

from build_days import localize_speakers  # 단일 치환 규칙 재사용

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DAY1 = os.path.join(ROOT, "Assets", "Resources", "GameData", "day1.json")


def main():
    with open(DAY1, encoding="utf-8") as f:
        data = json.load(f)
    total = 0
    for customer in data.get("customers", []):
        total += localize_speakers(customer)
    with open(DAY1, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"day1 speakers localized: {total}")


if __name__ == "__main__":
    main()
