# -*- coding: utf-8 -*-
"""rule_book 12개 규정 교체 + id12(X-ray) 삭제를 각 dayN.json 의 rules 배열에 타깃 패치.

런타임 규정집(ImmigrationManager.BuildActiveRules)은 dayN.json 의 rules 를 읽는다.
전체 리빌드(build_days) 금지 — 대사 되돌림 위험. 여기서는 rules 배열의 content/존재만
엑셀 소스와 동기화한다(title/relatedField/attr/endDay 는 현행 유지).

idempotent: 같은 입력 → 같은 결과.
"""
import json
import os

GAMEDATA = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "Resources", "GameData"))

# rule_id -> 새 content (엑셀 rule_book 와 동일 단일 소스). _edit_rulebook_sato_news_xlsx.py 와 일치해야 한다.
RULE_CONTENT = {
    1: ("모든 입국자는 유효한 서류를 갖추고 심사관의 허가를 받아야 입국할 수 있다. "
        "제출 서류·본인·진술의 정보가 모두 일치할 때만 허가하며, 어느 하나라도 어긋나면 입국을 거부한다."),
    2: ("여권의 이름·생년월일·성별·사진은 입국자 본인과 일치해야 한다. "
        "어느 한 항목이라도 본인과 다르면 입국을 거부한다."),
    3: ("여권은 여권번호·영문 이름·성별·생년월일·국적·발급일·만료일을 모두 갖추어야 한다. "
        "여권번호 앞 두 자리는 발급 국가 코드와 일치해야 한다(대한민국 KO·미국 US·중국 CN·일본 JP). "
        "앞자리가 국적과 다르면 위조 여권이다. "
        "정보가 본인이나 다른 서류와 다르거나 만료일이 오늘 이전이면 입국을 거부한다."),
    8: ("관광·취업·장기 체류 목적의 외국인은 비자를 함께 제출해야 한다. "
        "비자의 비자번호·비자종류·국적·발급일·만료일은 여권과 일치해야 하며, "
        "만료되었거나 여권과 어긋나는 비자는 입국을 거부한다."),
    15: ("성형 등으로 얼굴이 여권 사진과 달라 보이는 경우, 본인 여부는 지문 신원으로 가린다. "
         "지문 신원(이름·생년월일·국적)이 여권과 다르면 신분 도용으로 보아 입국을 거부한다."),
    11: ("외국인의 실제 방문 목적은 비자에 기재된 방문 목적과 일치해야 한다. "
         "진술한 목적이 비자와 다르면(거짓 진술) 입국을 거부한다."),
    7: ("검역 대상 입국자는 PCR 검사서를 제출해야 한다. "
        "검사 결과가 양성이거나, 유효 기간이 지났거나, 검사서의 이름·국적이 여권과 다르면 입국을 거부한다."),
    14: ("PCR 검사서는 공인 기관(국립검역소·인천공항검역소·질병관리청진단검사센터)이 발급한 것만 유효하다. "
         "명단에 없는 사설·무허가 기관의 검사서는 무효이며 입국을 거부한다."),
    9: ("취업 목적 입국자는 재직증명서를 제출해야 한다. "
        "증빙번호·고용 회사·직종·입사일·발급일은 다른 서류와 일치해야 하며, "
        "항목이 누락되거나 위조 흔적이 있으면 입국을 거부한다."),
    13: ("고용 회사가 실재하지 않는 유령회사(사업자 미등록·폐업)이거나 입국 금지 명단에 오른 회사이면, "
         "서류가 정상으로 보여도 입국을 거부한다."),
    4: ("무기·마약·폭발물·밀수품 등은 반입할 수 없다. "
        "신원이 의심되거나 위험물 반입 경보가 있는 입국자는 X-ray 정밀 검사로 확인하며, "
        "금지 물품이 적발되면 입국을 거부한다."),
    6: ("테러·폭발 등 특별 보안 경보가 발령된 기간에는 심사를 강화한다. "
        "서류나 신원에 의심이 있는 입국자는 X-ray 정밀 검사와 보안 명단 대조를 시행하며, "
        "위험물이 적발되거나 수배자로 확인되면 즉시 입국을 거부한다."),
}

DELETE_RULE_ID = 12  # X-ray 정밀 검사 행 삭제(금지물품 id4 에 통합)


def patch_day(day):
    path = os.path.join(GAMEDATA, f"day{day}.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    rules = data.get("rules") or []
    if not rules:
        return None  # 변경 없음(규정 없는 날)

    new_rules = []
    content_changed = 0
    deleted = 0
    for r in rules:
        rid = int(r.get("ruleId", 0))
        if rid == DELETE_RULE_ID:
            deleted += 1
            continue  # id12 행 제거
        if rid in RULE_CONTENT and r.get("content") != RULE_CONTENT[rid]:
            r["content"] = RULE_CONTENT[rid]
            content_changed += 1
        new_rules.append(r)

    if content_changed == 0 and deleted == 0:
        return ("noop", 0, 0)

    data["rules"] = new_rules
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    return ("written", content_changed, deleted)


def main():
    for day in range(1, 15):
        res = patch_day(day)
        if res is None:
            continue
        status, changed, deleted = res
        if status == "noop":
            print(f"[day{day}] up to date (no change)")
        else:
            print(f"[day{day}] content replaced={changed} deleted_id12={deleted}")


if __name__ == "__main__":
    main()
