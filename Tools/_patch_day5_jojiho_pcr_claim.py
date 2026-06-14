# -*- coding: utf-8 -*-
"""
조지호(id 47) PCR '미제출' 변이에 대조 단서(claim)를 주입한다.

배경: 미제출 변이(altVariant)는 PCR 검사서 카드가 아예 없어 규정집과 대조할 대상이 없었다.
      대화에만 "어? PCR요? 그건 안 가져왔는데요." 진술이 있으므로, 그 줄을 대조 앵커로 만든다.
      (선례: day4 리 강 '방문목적 거짓' = 진술 줄에 claim(attr=visa_type) 주입 — _patch_day4_visit_purpose.py)

주입: altVariant 입장(인삿말) 케이스의 "안 가져왔" 진술 줄에
      claim {attr: pcr_result, value: 미제출, label: PCR 미제출(진술)} 부여.
      → 대화기록에서 그 줄을 클릭 + 규정집 'PCR 검사서' 클릭 시 대조 성립.
      (CrossCheckController 가 pcr_result '미제출' 을 불일치로 판정 — 코드 동기화 필요.)

엑셀 소스(대사_스크립트.xlsx Day5!F13)는 먼저 '대화 대조(PCR 미제출 진술 ↔ 규정집)' 로 동기화 완료.

day5.json 을 직접 패치(런타임 실제 소스). 멱등 — 다시 돌려도 같은 결과.
실행: python Tools/_patch_day5_jojiho_pcr_claim.py
"""
import json, os, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

DAY5 = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Assets", "Resources", "GameData", "day5.json")

CUSTOMER_ID = 47           # 조지호
ANCHOR_SPEAKER = "조지호"
ANCHOR_TEXT_KEY = "안 가져왔"   # 진술 줄 식별 부분 문자열
PCR_CLAIM = {"attr": "pcr_result", "value": "미제출",
             "label": "PCR 미제출(진술)", "unlocksScan": ""}


def patch_lines(lines):
    """입장 케이스 lines 중 조지호의 '안 가져왔' 진술 줄에 claim 주입. 변경 수 반환."""
    n = 0
    for ln in lines or []:
        if (ln.get("speaker") == ANCHOR_SPEAKER
                and ANCHOR_TEXT_KEY in str(ln.get("text", ""))):
            if ln.get("claim") != PCR_CLAIM:
                ln["claim"] = dict(PCR_CLAIM)
                n += 1
    return n


def main():
    with open(DAY5, encoding="utf-8") as f:
        data = json.load(f)

    total = 0
    found_customer = False
    for c in data.get("customers", []):
        if c.get("customerId") != CUSTOMER_ID:
            continue
        found_customer = True
        alt = c.get("altVariant") or {}
        for case in alt.get("dialogueCases", []):
            if case.get("caseType") == "입장":
                total += patch_lines(case.get("lines"))

    if not found_customer:
        print(f"[!] customerId {CUSTOMER_ID}(조지호) 를 day5.json 에서 찾지 못함.")
        return

    if total == 0:
        print("[=] 변경 없음 (이미 주입되어 있거나 앵커 줄 없음). 멱등 OK.")
        return

    with open(DAY5, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"[+] claim 주입 완료: {total}줄 → {os.path.relpath(DAY5)}")
    print(f"    {PCR_CLAIM}")


if __name__ == "__main__":
    main()
