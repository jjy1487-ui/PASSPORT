# -*- coding: utf-8 -*-
"""
apply_character_dialogue_day1.py — day1.json(수작업 완성본)에 캐릭터별 손글 대사를 주입.

배경:
  build_days.py 는 day2~14 만 생성하며(day1 보호 가드), 그 안에서 character_dialogue_map.
  apply_character_override 로 손님 라인 text 를 customerId별 캐논 대사로 덮어쓴다.
  day1.json 은 수작업 완성본이라 build_days 가 건드리지 않아 손님 인사가 '유형 평면'
  (예: 김민준·이지은·최서연이 동일 인사)으로 남아 있었다.

  이 스크립트는 **full build_days 로 day1 을 재생성하지 않고**, 기존 day1.json 을 로드해
  apply_character_override 만 직접 적용한다(손님 대사 라인 text 만 캐릭터별로 교체).
  → 수작업 day1 의 케이스 구조·심사관 라인·customers 필드·rules·news·라인 수는 전부 보존된다.

불변 보장:
  - apply_character_override 는 손님 라인(speaker == nameKr 또는 "캐릭터"/"손님") text 만 교체.
    day1.json 은 이미 nameKr 로 localize 되어 있으므로 character_dialogue_map._is_visitor_line 이
    speaker == 그 손님 nameKr 인 라인을 손님 라인으로 인식한다(심사관/시스템 제외).
  - 캐논에 해당 반응이 없으면 그 라인은 건드리지 않는다(수작업 대사 보존).
  - 케이스 개수/order/speaker/라인 수는 불변.

제약:
  - day1.json 만 수정한다(다른 dayN.json·엑셀·Downloads 미수정).
  - 결정론: 같은 character_dialogue.json + 같은 day1.json → 같은 결과.
  - idempotent: 두 번 적용해도 동일 출력(같은 캐논 대사로 다시 덮어쓰므로).
  - 모든 IO utf-8, JSON ensure_ascii=False, 다국어 보존.

실행: python Tools/DataImport/apply_character_dialogue_day1.py
"""
import json
import os

from character_dialogue_map import load_char_dialogue, apply_character_override
# 대사 정화(post-processing) — build_days 와 동일한 단일 정화 지점.
#   (1) 액션/시스템 라인 제거 (2) 외국인 {모국어}({한국어}) 통일 (3) 윤서린 거절 변주.
from dialogue_polish import polish_customer

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DAY1 = os.path.join(ROOT, "Assets", "Resources", "GameData", "day1.json")

# day1 사진불일치 정정(재현 가능 — 손편집이 자꾸 유실되어 스크립트로 고정).
#  배경: 커밋된 day1 의 윌리엄/왕웨이는 '국적' 결함(여권 국적 필드 위조)이지만, 스크립트/튜토리얼
#  의도는 '여권 사진 불일치'(얼굴 대조로 적발)다. 국적 필드는 올바른 값으로 되돌리고, 여권 사진
#  spriteRef 를 '같은 성별·국적권·나이대'의 그럴듯한 다른 인물로 바꿔 사진 불일치 결함으로 만든다.
#  (디코이는 build_days 개선 선택기와 동일 기준으로 도출된 인물.)
DAY1_PHOTO_FIX = {
    "윌리엄 브라운": {"nationality": "USA", "decoy": "제임스 밀러"},  # 여/USA → 여/USA
    "왕 웨이":      {"nationality": "CHN", "decoy": "박지훈"},      # 남/CHN → 남/KOR(동아시아)
}


def _fix_day1_photo_defects(data):
    """day1 윌리엄/왕웨이의 '국적' 결함을 '여권 사진 불일치'로 정정(결함은 사진 하나만 남김)."""
    fixed = []
    for c in data.get("customers", []):
        spec = DAY1_PHOTO_FIX.get(c.get("nameKr"))
        if not spec:
            continue
        for doc in c.get("documents", []):
            # 국적 필드는 전 서류에서 올바른 값으로(결함은 사진뿐 → 국적 위조 제거).
            for fld in doc.get("fields", []):
                if fld.get("key") == "nationality":
                    fld["value"] = spec["nationality"]
            if doc.get("documentType") == "여권":
                doc["spriteRef"] = spec["decoy"]   # 얼굴 ≠ 여권사진 → 대조 시 불일치
                doc["violationField"] = "사진"
                doc["variant"] = "비정상"
            else:
                doc["violationField"] = "없음"      # 비자 등은 정상(단일 결함 보장)
                doc["variant"] = "정상"
        fixed.append(c.get("nameKr"))
    return fixed


def main():
    index = load_char_dialogue()
    with open(DAY1, encoding="utf-8") as f:
        data = json.load(f)

    report = {}
    total_ov = total_sk = 0
    for customer in data.get("customers", []):
        ov, sk = apply_character_override(customer, index, report)
        total_ov += ov
        total_sk += sk

    # 정화: 액션/시스템 라인 제거 + 외국인 언어 통일 + 윤서린 거절 변주(build_days 와 동일).
    polish_report = {}
    for customer in data.get("customers", []):
        polish_customer(customer, polish_report)

    photo_fixed = _fix_day1_photo_defects(data)

    with open(DAY1, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print("[apply_character_dialogue_day1] 손님 라인 교체 %d / 건너뜀(캐논 결측·연출보존) %d"
          % (total_ov, total_sk))
    print("[apply_character_dialogue_day1] 정화: 액션/시스템 제거 %d / 외국인 통일 %d / 윤서린 변주 %d / 미매핑 %d"
          % (polish_report.get("removed", 0), polish_report.get("foreign", 0),
             polish_report.get("yoon", 0), len(polish_report.get("unmapped", []))))
    print("[apply_character_dialogue_day1] 사진불일치 정정: %s" % (photo_fixed or "없음"))
    for (ct, gr), v in sorted(report.items(), key=lambda kv: str(kv[0])):
        print("  - %s / %s: 교체 %d / 건너뜀 %d" % (ct, gr, v["override"], v["skip"]))


if __name__ == "__main__":
    main()
