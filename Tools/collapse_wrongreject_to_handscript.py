# -*- coding: utf-8 -*-
"""손글 스크립트 모델로 정리: '잘못 거절'(오거부) 대사를 오거부횟수 루프(r1/r2/r3, 4줄
'다시 확인…맞네요' 강제통과 포함)에서 → 단일 2줄(검문관 거부 안내 + 방문객 항의)로 축약.
손글 원본의 분기 B(거부) 모델과 일치시킨다. day1~14 모두 적용.
"""
import json, io, glob, os, re

GAMEDATA = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                        "Assets", "Resources", "GameData")
WR = "잘못 거절"

def collapse_customer(c):
    cases = c.get("dialogueCases", [])
    wr_cases = [dc for dc in cases if dc.get("gameResult") == WR]
    if not wr_cases:
        return 0
    # 가장 낮은 rejectCount 케이스를 대표로(없으면 첫 번째)
    rep = min(wr_cases, key=lambda dc: dc.get("rejectCount", 0))
    lines = sorted(rep.get("lines", []), key=lambda ln: ln.get("order", 0))
    # 첫 검문관 거부안내 + 첫 방문객 항의 = order<=2 (모든 케이스가 [심사관,방문객,심사관,방문객] 구조)
    keep = [ln for ln in lines if ln.get("order", 99) <= 2]
    if len(keep) < 2 and len(lines) >= 2:
        keep = lines[:2]
    new_case = {
        "caseType": rep.get("caseType", "일반 심사"),
        "gameResult": WR,
        "rejectCount": 0,
        "lines": keep,
    }
    # 기존 '잘못 거절' 전부 제거 후 정리된 1건만 같은 자리(첫 등장 위치)에 삽입
    out, inserted = [], False
    for dc in cases:
        if dc.get("gameResult") == WR:
            if not inserted:
                out.append(new_case); inserted = True
            # 나머지 잘못거절은 버림
        else:
            out.append(dc)
    c["dialogueCases"] = out
    return len(wr_cases)

total_files = total_custs = total_collapsed = 0
for path in sorted(glob.glob(os.path.join(GAMEDATA, "day*.json")),
                   key=lambda p: int(re.search(r"day(\d+)", p).group(1))):
    d = json.load(io.open(path, encoding="utf-8"))
    fc = 0
    for c in d.get("customers", []):
        n = collapse_customer(c)
        if n:
            fc += 1
            total_collapsed += n
    json.dump(d, io.open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    total_files += 1
    total_custs += fc
    print(f"{os.path.basename(path)}: {fc} customers collapsed")

print(f"\nDONE files={total_files} customers={total_custs} wrongreject_cases_removed_or_merged={total_collapsed}")
