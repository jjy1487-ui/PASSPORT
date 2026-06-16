# -*- coding: utf-8 -*-
"""day12·13·14 입장 대사에 수하물 X-ray 검사 안내(심사관 요청 + 손님 캐릭터별 반응)를 넣는다.
day11 '보안 강화' 이후 전원 입장 자동 X-ray와 짝을 맞춤. 손님 반응은 _baggage_responses 의 캐릭터별 문구.
위험 손님(테러범·마약/밀수 범죄자)의 정상 거절 멘트는 X-ray 적발 문구로 교체.

- 멱등: 같은 입력 → 같은 출력(반응 문구가 바뀌면 갱신).
- 바이트 재현: json.dumps(ensure_ascii=False, indent=1), 원본 줄바꿈 보존.
- altVariant(인삿말 2변형)도 함께 패치.
"""
import io
import os
import sys
import json

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _baggage_responses import inspector_for, response_for, BAGGAGE_MARKER  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")

REJECT_XRAY = {
    "테러범": "X-ray 검사에서 폭발물로 의심되는 물품이 적발되었습니다. 입국하실 수 없습니다.",
    "범죄자(마약 범죄자)": "X-ray 검사에서 마약류가 적발되었습니다. 입국하실 수 없습니다.",
    "범죄자(밀수품 범죄자)": "X-ray 검사에서 밀수품이 적발되었습니다. 입국하실 수 없습니다.",
}


def get_case(node, case_type, game_result=None):
    for case in node.get("dialogueCases", []):
        if case.get("caseType") != case_type:
            continue
        if game_result is None or case.get("gameResult") == game_result:
            return case
    return None


def renumber(case):
    for i, ln in enumerate(case.get("lines", []), start=1):
        ln["order"] = i


def patch_entry(node, cust_name, inspector, response):
    """입장 케이스에 심사관 수하물 질문 + 손님 반응을 보장한다(없으면 삽입, 있으면 문구 갱신).
    심사관 수하물 줄은 speaker=심사관 + '수하물' 키워드로 식별(캐릭터별 문구라 정확 일치 불가)."""
    ec = get_case(node, "입장")
    if ec is None:
        return False
    lines = ec.setdefault("lines", [])
    idx = next((i for i, l in enumerate(lines)
                if l.get("speaker") == "심사관" and BAGGAGE_MARKER in (l.get("text") or "")), None)
    changed = False
    if idx is None:
        at = 1 if len(lines) >= 1 else 0  # 인삿말 첫 줄 뒤
        lines[at:at] = [
            {"order": 0, "speaker": "심사관", "text": inspector},
            {"order": 0, "speaker": cust_name, "text": response},
        ]
        changed = True
    else:
        if lines[idx].get("text") != inspector:
            lines[idx]["text"] = inspector
            changed = True
        ai = idx + 1  # 수하물 줄 바로 다음 = 손님 반응
        if ai < len(lines) and lines[ai].get("speaker") == cust_name:
            if lines[ai].get("text") != response:
                lines[ai]["text"] = response
                changed = True
        else:
            lines[idx + 1:idx + 1] = [{"order": 0, "speaker": cust_name, "text": response}]
            changed = True
    if changed:
        renumber(ec)
    return changed


def patch_reject(node, character_type):
    new_text = REJECT_XRAY.get(character_type)
    if not new_text:
        return False
    rc = get_case(node, "일반 심사", "정상 거절")
    if rc is None:
        return False
    lines = rc.get("lines", [])
    if lines and lines[0].get("speaker") == "심사관" and lines[0].get("text") != new_text:
        lines[0]["text"] = new_text
        return True
    return False


def main():
    for d in (11, 12, 13, 14):
        p = os.path.join(GAMEDATA, f"day{d}.json")
        raw = open(p, "rb").read()
        newline = "\r\n" if b"\r\n" in raw else "\n"
        data = json.loads(raw.decode("utf-8"))

        e = r = 0
        for cust in data["customers"]:
            name = cust.get("nameKr") or "손님"
            ct = cust.get("characterType")
            insp = inspector_for(name)
            # 메인
            if patch_entry(cust, name, insp, response_for(name, alt=False)):
                e += 1
            if patch_reject(cust, ct):
                r += 1
            # altVariant
            av = cust.get("altVariant")
            if av:
                if patch_entry(av, name, insp, response_for(name, alt=True)):
                    e += 1
                if patch_reject(av, ct):
                    r += 1

        out = json.dumps(data, ensure_ascii=False, indent=1)
        if newline == "\r\n":
            out = out.replace("\n", "\r\n")
        with open(p, "wb") as f:
            f.write(out.encode("utf-8"))
        print(f"day{d}.json: 입장 반응 {e}건 / 위험손님 거절 멘트 {r}건")


if __name__ == "__main__":
    main()
