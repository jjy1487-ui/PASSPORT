# -*- coding: utf-8 -*-
"""
4일차 결함을 결함배분표(권위)에 맞춤. day4.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day4 시트(서류 대조 단계 포함)에서 확인해 맞춤.
- 토머스 무어(43): 여권 사진 → 비자 만료일(visa expiry 2025-11-12, 여권 사진 복구)
- 노가은(30, alt=결함): 지문 신원불일치(이름) → 지문 생년월일 불일치(dbName=여권name, dbBirth 1996-12-01, claim attr=birth_date)
- 정시우(44, alt=결함): 여권 만료일 → 여권 성별(gender 반대, expiry 정상 복구)
※ 입장 방문목적 대사는 이미 적용됨 — set_reject 는 '일반 심사/정상 거절' 케이스만 건드림(입장 불변).
※ 로스터/메인DB/FORCED_PHOTO 코드/설계문서는 일괄 동기화 단계(건드리지 않음).
실행: python Tools/_patch_day4_defects.py
"""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day4.json"

def opp_gender(g): return "남성" if g == "여성" else "여성"

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

def main():
    data = json.load(open(DAY, encoding="utf-8"))
    cust = {c["customerId"]: c for c in data["customers"]}
    log = []

    # 43 토머스 무어: 비자 만료일 (여권 사진 결함 제거 → 비자 만료)
    c = cust[43]
    pp = get_doc(c, "여권"); vz = get_doc(c, "비자")
    pp["variant"] = "정상"; pp["violationField"] = "없음"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 결함 제거(본인 얼굴 복구)
    vz["variant"] = "비정상"; vz["violationField"] = "만료일"
    setf(vz, "expiry_date", "2025-11-12")            # 비자 만료(과거)
    set_reject(c, "비자 유효기간이 지나 입국하실 수 없습니다.", None)
    log.append("43 토머스 무어 → 비자 만료일(여권사진 복구)")

    # 30 노가은: 지문 생년월일 불일치 (altVariant 가 결함)
    c = cust[30]; alt = c["altVariant"]
    pp = get_doc(alt, "여권"); pp_name = getf(pp, "name")
    fp = alt["fingerprint"]
    fp["result"] = "불일치"; fp["detail"] = "생년월일 불일치"
    fp["extra"] = pp_name                            # 지문 신원 이름 = 여권 이름(동일)
    fp["claim"] = {"attr": "birth_date", "value": "1996-12-01", "label": "지문 대조 생년월일", "unlocksScan": ""}
    fp["record"]["dbName"] = pp_name                 # 이름은 일치
    fp["record"]["dbBirth"] = "1996-12-01"           # 생년월일은 불일치(여권 1999-05-05)
    set_reject(alt, "지문 신원의 생년월일이 여권과 일치하지 않습니다. 입국하실 수 없습니다.",
               {"attr": "birth_date", "value": "1996-12-01", "label": "지문 대조 생년월일", "unlocksScan": ""})
    log.append("30 노가은(alt) → 지문 생년월일 불일치(dbName=NOH GAEUN, dbBirth=1996-12-01)")

    # 44 정시우: 여권 성별 (altVariant 가 결함, 만료일→성별)
    c = cust[44]; alt = c["altVariant"]; pp = get_doc(alt, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "성별"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 정상(본인 얼굴)
    setf(pp, "gender", opp_gender(c["gender"]))      # 성별 불일치
    setf(pp, "expiry_date", "2029-07-14")            # 만료일 정상 복구
    set_reject(alt, "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "gender", "value": "", "label": "성별 불일치", "unlocksScan": ""})
    log.append("44 정시우(alt) → 여권 성별(만료일 복구)")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day4 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
