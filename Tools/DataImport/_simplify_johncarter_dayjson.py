# -*- coding: utf-8 -*-
"""존카터 단순화(밀수품만)를 day11.json에 반영.
- 여권 정상화: passport_no KO1188042 → US1008936(엑셀 소스값), variant 정상, violationField 없음
- crossCheckLines(passport_no 추궁) 제거 — 밀수품은 X-ray로 적발
- 정상거절 claim attr=passport_no("여권번호 불일치") → contraband("X-ray 적발물")
멱등. 바이트 재현(json.dumps ensure_ascii=False, indent=1), 원본 줄바꿈 보존.
"""
import io
import os
import sys
import json

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
P = os.path.join(ROOT, "Assets", "Resources", "GameData", "day11.json")

JC_ID = 8
NORMAL_PASSPORT_NO = "US1008936"


def main():
    raw = open(P, "rb").read()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    data = json.loads(raw.decode("utf-8"))

    changes = []
    for c in data["customers"]:
        if c.get("customerId") != JC_ID:
            continue
        # 1) 여권 정상화
        for doc in c.get("documents", []):
            if doc.get("documentType") != "여권":
                continue
            if doc.get("variant") != "정상":
                doc["variant"] = "정상"
                changes.append("variant→정상")
            if doc.get("violationField") != "없음":
                doc["violationField"] = "없음"
                changes.append("violationField→없음")
            for f in doc.get("fields", []):
                if f.get("key") == "passport_no" and f.get("value") != NORMAL_PASSPORT_NO:
                    changes.append(f"passport_no {f.get('value')}→{NORMAL_PASSPORT_NO}")
                    f["value"] = NORMAL_PASSPORT_NO
        # 2) crossCheckLines 제거
        if c.get("crossCheckLines"):
            c["crossCheckLines"] = []
            changes.append("crossCheckLines 비움")
        # 3) 정상거절 claim → contraband
        for case in c.get("dialogueCases", []):
            if case.get("caseType") == "일반 심사" and case.get("gameResult") == "정상 거절":
                lines = case.get("lines", [])
                if lines and isinstance(lines[0].get("claim"), dict) and lines[0]["claim"].get("attr") != "contraband":
                    lines[0]["claim"] = {"attr": "contraband", "value": "밀수품", "label": "X-ray 적발물", "unlocksScan": ""}
                    changes.append("정상거절 claim→contraband")

    out = json.dumps(data, ensure_ascii=False, indent=1)
    if newline == "\r\n":
        out = out.replace("\n", "\r\n")
    with open(P, "wb") as f:
        f.write(out.encode("utf-8"))
    print("존카터 변경:", changes if changes else "없음(이미 정상)")


if __name__ == "__main__":
    main()
