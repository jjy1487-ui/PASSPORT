# -*- coding: utf-8 -*-
"""day12 사토 intro 를 둘로 쪼개 X-ray 패널이 대사 '사이'에 뜨게 한다.

흐름: intro(인사~검색대 입장) → [openScanAfter:"xray" → 전신 X-ray 표시] → introReveal(검문관 발각 반응~돌변) → 분기점1.

- intro      : lines = 발각 직전까지("잠깐만요" 전), choices 제거, next="introReveal", openScanAfter="xray".
- introReveal : lines = "잠깐만요"부터 끝까지, 분기점1 choices(잠깐 진정/당장 멈춰) 이관, next="".
멱등: 이미 쪼개져 있으면 introReveal 를 intro 뒤로 합쳐 원복 후 동일 규칙으로 재분할(동일 입력→동일 출력).
다른 노드/손님 불변. json.dump(indent=1, ensure_ascii=False).
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DAY12 = os.path.join(ROOT, "Assets", "Resources", "GameData", "day12.json")

REVEAL_ID = "introReveal"
# 발각 시작 라인(검문관이 화면을 보고 멈칫) — 이 라인부터 introReveal.
SPLIT_MARKERS = ("잠깐만요", "손이 멎")


def reindex(lines):
    for i, ln in enumerate(lines):
        ln["order"] = i
    return lines


def find_split(lines):
    for i, ln in enumerate(lines):
        t = ln.get("text", "")
        if any(m in t for m in SPLIT_MARKERS):
            return i
    return -1


def main():
    with open(DAY12, encoding="utf-8") as f:
        d = json.load(f)

    sato = next((c for c in d["customers"] if c.get("scenario")), None)
    if sato is None or sato.get("nameKr") != "사토 하루키":
        print("ERROR: 사토 하루키 scenario customer not found", file=sys.stderr)
        sys.exit(1)

    others_before = json.dumps(
        [c for c in d["customers"] if c is not sato], ensure_ascii=False, sort_keys=True)

    nodes = sato["scenario"]["nodes"]  # list
    intro = next((n for n in nodes if n.get("id") == "intro"), None)
    if intro is None:
        print("ERROR: intro 노드 없음", file=sys.stderr)
        sys.exit(1)
    reveal = next((n for n in nodes if n.get("id") == REVEAL_ID), None)

    # 1) 원복: introReveal 가 있으면 그 lines 를 intro 뒤로 합치고 choices 를 intro 로 되돌린다.
    full_lines = list(intro["lines"])
    choices = list(intro.get("choices") or [])
    if reveal is not None:
        full_lines += list(reveal.get("lines") or [])
        if reveal.get("choices"):
            choices = list(reveal["choices"])
        nodes.remove(reveal)

    # 2) 분할 지점 탐지
    k = find_split(full_lines)
    if k <= 0:
        print(f"ERROR: 발각 분할 지점({SPLIT_MARKERS}) 을 못 찾음", file=sys.stderr)
        sys.exit(1)

    pre = reindex([dict(l) for l in full_lines[:k]])
    rev = reindex([dict(l) for l in full_lines[k:]])

    # 3) intro 재구성(키 순서 보존: id,lines,choices,next,timer,timeoutNext,openScanAfter,outcome)
    intro["lines"] = pre
    intro["choices"] = []
    intro["next"] = REVEAL_ID
    intro["timer"] = intro.get("timer", 0) or 0
    intro["timeoutNext"] = intro.get("timeoutNext", "") or ""
    intro["openScanAfter"] = "xray"
    intro["outcome"] = None

    reveal_node = {
        "id": REVEAL_ID,
        "lines": rev,
        "choices": choices,
        "next": "",
        "timer": 0,
        "timeoutNext": "",
        "openScanAfter": "",
        "outcome": None,
    }
    # 4) intro 바로 뒤에 introReveal 삽입
    idx = nodes.index(intro)
    nodes.insert(idx + 1, reveal_node)

    with open(DAY12, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)

    # ── 검증 ──
    with open(DAY12, encoding="utf-8") as f:
        d2 = json.load(f)
    sato2 = next(c for c in d2["customers"] if c.get("scenario"))
    others_after = json.dumps(
        [c for c in d2["customers"] if not (c.get("nameKr") == "사토 하루키" and c.get("scenario"))],
        ensure_ascii=False, sort_keys=True)
    n2 = {n["id"]: n for n in sato2["scenario"]["nodes"]}

    print("=== PATCH day12 사토 intro 분할(X-ray) ===")
    print("other customers unchanged:", others_before == others_after)
    print("start:", sato2["scenario"]["start"])
    print("intro lines:", len(n2["intro"]["lines"]),
          "| next:", n2["intro"]["next"], "| openScanAfter:", n2["intro"]["openScanAfter"],
          "| choices:", len(n2["intro"]["choices"]))
    print("intro 마지막 줄:", n2["intro"]["lines"][-1]["text"][:34])
    print("introReveal lines:", len(n2[REVEAL_ID]["lines"]),
          "| choices:", [c["label"] + "→" + c["next"] for c in n2[REVEAL_ID]["choices"]])
    print("introReveal 첫 줄:", n2[REVEAL_ID]["lines"][0]["text"][:34])

    # 그래프 무결성
    ids = set(n2.keys())
    bad = []
    for nid, nd in n2.items():
        refs = [c["next"] for c in (nd.get("choices") or [])]
        if nd.get("next"):
            refs.append(nd["next"])
        if nd.get("timeoutNext"):
            refs.append(nd["timeoutNext"])
        for r in refs:
            if r and r not in ids:
                bad.append((nid, r))
    print("dangling refs:", bad)
    print("node count:", len(n2))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
