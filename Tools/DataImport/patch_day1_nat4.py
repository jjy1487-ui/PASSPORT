# -*- coding: utf-8 -*-
"""
patch_day1_nat4.py — day1.json(수작업 완성본) 국적4제한 in-place 정합 패치 (data-tools)

build_days.py 는 day2~14 만 재생성하고 day1.json 은 수작업본이라 건드리지 않는다.
day1 에 등장하는 재배정 손님(4: ESP->USA, 7: FRA->CHN)의 국적/이름/번호 필드만
정합 패치한다. 대사(dialogueCases)는 절대 수정하지 않는다.

idempotent: REASSIGN 매핑과 코드 접두사 규칙만 적용 → 재실행해도 동일 결과.
"""
import json
import re

DAY1 = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day1.json"

NAT_DISPLAY = {"USA": "미국(USA)", "CHN": "중국(CHN)", "JPN": "일본(JPN)", "KOR": "대한민국(KOR)"}
PP_PREFIX = {"USA": "US", "CHN": "CN", "JPN": "JP", "KOR": "KO"}

# customer_id -> (새코드, name_en, name_kr)  (restrict_nationalities_4.py 와 동일 소스)
REASSIGN = {
    4: ("USA", "JAMES MILLER", "제임스 밀러"),
    7: ("CHN", "WANG WEI", "왕 웨이"),
}


def doc_name(name_en):
    """'JAMES MILLER' -> 'MILLER JAMES' (day1 여권 name 필드의 '성 이름' 공백 표기)."""
    parts = name_en.split()
    if len(parts) >= 2:
        return "%s %s" % (" ".join(parts[1:]), parts[0])
    return name_en


def repp(no, code):
    if isinstance(no, str):
        m = re.match(r"^[A-Z]{2}(.*)$", no)
        if m:
            return PP_PREFIX[code] + m.group(1)
    return no


def main():
    with open(DAY1, encoding="utf-8") as f:
        d = json.load(f)

    patched = []
    for c in d["customers"]:
        cid = c["customerId"]
        if cid not in REASSIGN:
            continue
        code, en, kr = REASSIGN[cid]
        c["nationality"] = NAT_DISPLAY[code]
        c["nameEn"] = en
        c["nameKr"] = kr
        for doc in c["documents"]:
            if doc.get("country"):
                doc["country"] = code
            for fld in doc.get("fields", []):
                k = fld.get("key")
                if k == "nationality":
                    fld["value"] = code
                elif k == "name":
                    fld["value"] = doc_name(en)
                elif k == "passport_no":
                    fld["value"] = repp(fld["value"], code)
                elif k == "visa_no":
                    fld["value"] = re.sub(r"^V-[A-Z]{3}-", "V-%s-" % code, str(fld["value"]))
        patched.append((cid, code, en))

    with open(DAY1, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)

    print("day1.json 패치:")
    for p in patched:
        print("  customer", p[0], "->", p[1], p[2])


if __name__ == "__main__":
    main()
