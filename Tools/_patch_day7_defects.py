# -*- coding: utf-8 -*-
"""
7일차 결함을 결함배분표(권위)에 맞춤. day7.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day7 시트 + 리강(day4) 방문목적 패턴 기준(표=정답).
- 왕 팡(58): 여권 사진 → 비자종류 거짓-진술(입장 claim attr=visa_type value=진술목적, 비자 visa_type 다름)
- 서지호(21, alt=결함): 지문 신원(이름) → 지문 도용-국적(dbNationality≠여권nat, dbName=동일, detail '국적 불일치', claim attr=nationality)
※ 왕팡 비자는 정상(variant 정상) — 거짓은 진술(claim)과 비자 visa_type 의 불일치로만 드러남(리강 패턴).
※ s1 스즈키·s2 신유준·s4 류양은 이미 정합 — 건드리지 않음. 메인DB/FORCED_PHOTO/설계문서는 일괄 동기화.
실행: python Tools/_patch_day7_defects.py
"""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day7.json"

def get_doc(o, dt):
    for d in o["documents"]:
        if d["documentType"] == dt: return d
    return None

def getf(doc, key):
    for f in doc["fields"]:
        if f["key"] == key: return f["value"]
    return None

def setf(doc, key, val):
    for f in doc["fields"]:
        if f["key"] == key: f["value"] = val; return
    doc["fields"].append({"label": key, "value": val, "key": key})

def set_reject(o, text, claim=None):
    for case in o.get("dialogueCases", []):
        if case.get("caseType") == "일반 심사" and case.get("gameResult") == "정상 거절":
            ln = case["lines"][0]
            ln["text"] = text
            if claim is not None: ln["claim"] = claim
            elif "claim" in ln: del ln["claim"]
            return True
    return False

def get_entry(o):
    for case in o.get("dialogueCases", []):
        if case.get("caseType") == "입장":
            return case
    return None

def main():
    data = json.load(open(DAY, encoding="utf-8"))
    cust = {c["customerId"]: c for c in data["customers"]}
    log = []

    # 58 왕 팡: 비자종류 거짓-진술 (여권 사진 결함 제거, 비자 정상, 입장 claim 으로 거짓 진술)
    c = cust[58]
    pp = get_doc(c, "여권"); vz = get_doc(c, "비자")
    pp["variant"] = "정상"; pp["violationField"] = "없음"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 결함 제거(본인 얼굴 복구)
    vz["variant"] = "정상"; vz["violationField"] = "없음"
    visa_type = getf(vz, "visa_type")                # 비자 실제 종류(관광)
    spoken = "장기 체류" if visa_type != "장기 체류" else "관광"  # 비자와 다른 진술
    # 입장 대사: 리강 패턴(방문 목적 질문 + 진술 claim) 으로 재구성(멱등)
    entry = get_entry(c)
    nm = c["nameKr"]
    entry["lines"] = [
        {"order": 1, "speaker": nm, "text": "您好。听说标准严了，有点紧张。 (안녕하세요. 기준이 강화됐다는 얘기 듣고 좀 긴장했어요.)"},
        {"order": 2, "speaker": "심사관", "text": "방문 목적이 어떻게 되십니까?"},
        {"order": 3, "speaker": nm, "text": "我打算在这边长期待一段时间。 (이쪽에서 한동안 길게 지낼 생각이에요.)",
         "claim": {"attr": "visa_type", "value": spoken, "label": "방문 목적(진술)", "unlocksScan": ""}},
        {"order": 4, "speaker": nm, "text": "这是我的护照和签证。 (여권이랑 비자 여기요.)"},
    ]
    set_reject(c, "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.", None)
    log.append(f"58 왕 팡 → 비자종류 거짓-진술(진술='{spoken}' ≠ 비자='{visa_type}', 사진 복구)")

    # 21 서지호: 지문 도용-국적 (altVariant 가 결함, 이름 → 국적)
    c = cust[21]; alt = c["altVariant"]
    pp = get_doc(alt, "여권")
    pp_nat = getf(pp, "nationality")                 # KOR
    pp_name = getf(pp, "name")                        # SEO JIHO
    fp = alt["fingerprint"]
    fp["result"] = "불일치"; fp["detail"] = "국적 불일치"
    fp["extra"] = pp_name                            # 신원 이름은 일치(국적만 결함)
    fp["claim"] = {"attr": "nationality", "value": fp["record"]["dbNationality"],
                   "label": "지문 대조 국적", "unlocksScan": ""}
    fp["record"]["dbName"] = pp_name                 # 이름 동일
    # dbNationality 는 이미 USA(여권 KOR 과 다름) — 명시 보정(멱등)
    if fp["record"].get("dbNationality") == pp_nat:
        fp["record"]["dbNationality"] = "USA"
        fp["claim"]["value"] = "USA"
    set_reject(alt, "지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다.",
               {"attr": "nationality", "value": fp["record"]["dbNationality"], "label": "지문 대조 국적", "unlocksScan": ""})
    log.append(f"21 서지호(alt) → 지문 도용-국적(dbNationality={fp['record']['dbNationality']}≠여권 {pp_nat})")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day7 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
