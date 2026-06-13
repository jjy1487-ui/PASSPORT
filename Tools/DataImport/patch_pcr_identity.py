# -*- coding: utf-8 -*-
"""PCR검사서에 이름/국적 신원 필드를 주입(day5~7 직접 패치, idempotent).

배경: PcrCard 프리팹에는 Slot_name(_fieldKey=name)/Slot_nationality(_fieldKey=nationality)
슬롯이 있으나, PCR 문서 fields[]에 name/nationality 항목이 없어 빈 채 숨겨졌다.

규칙:
- PCR 문서(main + altVariant)마다, 같은 문서 묶음(documents[]) 안의 여권에서
  name/nationality 값을 읽어 그대로 복사한다(여권과 정확히 동일 → PCR↔여권 대조 일치).
- fields[] 맨 앞에 {이름, name} → {국적, nationality} 순으로 삽입.
- 기존에 key=name/nationality 항목이 있으면(과거 불완전 시도 잔재) 제거 후 재삽입(정합 보정).
- 결함(양성/위조/미제출)과 무관: 신원은 항상 본인(여권 동일).
- 전체 리빌드 금지: PCR fields 만 패치. 다른 필드/대사/큐는 불변.

재실행 안전(idempotent): 같은 입력 → 같은 결과.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GD = os.path.join(ROOT, "Assets", "Resources", "GameData")
DAYS = (5, 6, 7)


def passport_identity(documents):
    """문서 묶음에서 여권의 name/nationality 값을 읽는다. (값, 국적) 반환, 없으면 (None, None)."""
    name = nat = None
    for doc in documents:
        if doc.get("documentType") == "여권":
            for fl in doc.get("fields", []):
                if fl.get("key") == "name":
                    name = fl.get("value")
                elif fl.get("key") == "nationality":
                    nat = fl.get("value")
    return name, nat


def patch_pcr_doc(doc, documents):
    """PCR 문서 1장에 이름/국적을 맨 앞에 주입. 변경 시 True."""
    name, nat = passport_identity(documents)
    if name is None or nat is None:
        return False, "여권 name/nationality 없음(건너뜀)"

    fields = doc.get("fields", [])
    # 기존 name/nationality 항목 제거(과거 불완전 주입/불일치 값 보정)
    before = [(f.get("key"), f.get("value")) for f in fields]
    fields = [f for f in fields if f.get("key") not in ("name", "nationality")]

    # 맨 앞에 이름 → 국적 순 삽입
    fields.insert(0, {"label": "국적", "value": nat, "key": "nationality"})
    fields.insert(0, {"label": "이름", "value": name, "key": "name"})
    doc["fields"] = fields

    after = [(f.get("key"), f.get("value")) for f in fields]
    return (before != after), f"name={name!r} nat={nat!r}"


def main():
    report = []
    for day in DAYS:
        path = os.path.join(GD, f"day{day}.json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)

        changed_any = False
        for c in data.get("customers", []):
            cid = c.get("customerId")
            # main 변형
            for doc in c.get("documents", []):
                if doc.get("documentType") == "PCR검사서":
                    chg, info = patch_pcr_doc(doc, c.get("documents", []))
                    changed_any = changed_any or chg
                    report.append((day, cid, "main", chg, info))
            # altVariant 변형(있으면 자기 여권 기준으로 동일 처리)
            av = c.get("altVariant")
            if av:
                for doc in av.get("documents", []):
                    if doc.get("documentType") == "PCR검사서":
                        chg, info = patch_pcr_doc(doc, av.get("documents", []))
                        changed_any = changed_any or chg
                        report.append((day, cid, "alt", chg, info))

        if changed_any:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        print(f"day{day}: {'patched' if changed_any else 'no-change'} -> {path}")

    print("\n=== 패치 리포트 (day, cid, variant, changed, info) ===")
    for r in report:
        print(f"  day{r[0]} cid{r[1]:>4} [{r[2]:>4}] changed={r[3]!s:>5}  {r[4]}")
    print(f"\n총 PCR 문서 처리: {len(report)}건")


if __name__ == "__main__":
    main()
