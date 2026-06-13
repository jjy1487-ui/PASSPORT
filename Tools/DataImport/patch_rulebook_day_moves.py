# -*- coding: utf-8 -*-
"""규정집 시나리오 정리 — dayN.json 타깃 패치 (전체 리빌드 없이).
  C) 금지물품(ruleId=4) day1.json 에서 제거 -> day11.json 에 추가(ruleId=12 옆).
  D) PCR: day5.json 의 ruleId=5(방역 지시) 제거 + ruleId=7 content 통합(endDay=7 유지).

소스 동기화: data/여권_정리_updated.xlsx rule_book 시트도 동일하게 갱신됨
(_patch_rulebook_scenario.py). build_days 의 rule 배치는 rule_book.day 컬럼 그대로라
day5/day11 은 리빌드 시에도 재현된다. day1 은 build_days 가 안 만드므로 여기서 직접 패치.
idempotent: 이미 적용돼 있으면 변경 없음.
"""
import os, sys, json
sys.stdout.reconfigure(encoding="utf-8")

GAMEDATA = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                        "Assets", "Resources", "GameData")

UNIFIED_PCR = (
    "검역대상자는 PCR 검사서를 제출해야 한다.\n"
    "검사 결과가 양성이거나 유효 기간이 지났거나, 검사서 정보가 여권과 일치하지 않으면 입국을 거부한다.\n"
    "검사서의 이름, 국적, 검사번호, 검사일, 결과, 유효기간을 확인한다."
)


def load(d):
    with open(os.path.join(GAMEDATA, f"day{d}.json"), encoding="utf-8") as f:
        return json.load(f)


def save(d, data):
    with open(os.path.join(GAMEDATA, f"day{d}.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def find_rule(rules, rid):
    for r in rules:
        if r.get("ruleId") == rid:
            return r
    return None


# ── C) day1 -> day11 ────────────────────────────────────────────────
d1 = load(1)
d11 = load(11)
rule4 = find_rule(d1["rules"], 4)

# day1: ruleId=4 제거
before1 = [r.get("ruleId") for r in d1["rules"]]
d1["rules"] = [r for r in d1["rules"] if r.get("ruleId") != 4]
if before1 != [r.get("ruleId") for r in d1["rules"]]:
    save(1, d1)
    print(f"[C] day1: removed ruleId=4. {before1} -> {[r.get('ruleId') for r in d1['rules']]}")
else:
    print("[C] day1: ruleId=4 already absent (noop)")

# day11: ruleId=4 추가(없으면). build_days _rule_obj 와 같은 형태로.
if find_rule(d11["rules"], 4) is None:
    # rule4 가 day1 에 없으면(이미 옮겨짐) 정본 형태로 생성
    if rule4 is None:
        rule4 = {
            "ruleId": 4,
            "title": "금지 물품",
            "content": "마약과 밀수품은 반입할 수 없다\n금지 물품이 확인되면 입국을 거부한다",
            "relatedField": "",
            "attr": "",
        }
    # endDay 키가 붙어있으면 떼어낸다(금지물품은 무기한)
    rule4 = {k: rule4[k] for k in ("ruleId", "title", "content", "relatedField", "attr") if k in rule4}
    rule4.setdefault("relatedField", "")
    rule4.setdefault("attr", "")
    d11["rules"].append(rule4)
    save(11, d11)
    print(f"[C] day11: added ruleId=4 -> rules now {[r.get('ruleId') for r in d11['rules']]}")
else:
    print("[C] day11: ruleId=4 already present (noop)")

# ── D) day5: ruleId=5 제거 + ruleId=7 통합 ──────────────────────────
d5 = load(5)
changed = False

r7 = find_rule(d5["rules"], 7)
if r7 is not None:
    if r7.get("content") != UNIFIED_PCR or r7.get("endDay") != 7:
        r7["content"] = UNIFIED_PCR
        r7["endDay"] = 7
        changed = True
        print("[D] day5: ruleId=7 content unified + endDay=7")
    else:
        print("[D] day5: ruleId=7 already unified (noop)")
else:
    print("[D] day5: ruleId=7 NOT FOUND!")

before5 = [r.get("ruleId") for r in d5["rules"]]
d5["rules"] = [r for r in d5["rules"] if r.get("ruleId") != 5]
if before5 != [r.get("ruleId") for r in d5["rules"]]:
    changed = True
    print(f"[D] day5: removed ruleId=5. {before5} -> {[r.get('ruleId') for r in d5['rules']]}")
else:
    print("[D] day5: ruleId=5 already absent (noop)")

if changed:
    save(5, d5)
    print("[D] day5 saved.")

print("done.")
