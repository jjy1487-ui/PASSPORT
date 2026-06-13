# -*- coding: utf-8 -*-
"""
5일차 결함을 결함배분표(권위)에 맞춤. day5.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day5 시트 + 기존 여권번호/지문 패턴 기준.
- 조지호(47, alt=결함): PCR 미제출 → 여권번호 위조(앞자리≠국적, PCR 복구)
- 윤건우(48, alt=결함): 지문(데이터=생일) → 지문 도용-이름(dbName≠여권name=딴사람, detail '신원 불일치', claim attr=name)
※ 결함배분표가 권위 → 윤건우는 '이름'(생년월일 아님). 대사_스크립트 Day5는 옛버전(생일)이라 따르지 않음.
※ 로스터 스왑(s2 장우진/s5 한지원)·s3 로버트·s6 리나·s7 임선우는 건드리지 않음(별도/이미 정합).
※ 메인DB/FORCED_PHOTO 코드/설계문서는 일괄 동기화 단계.
실행: python Tools/_patch_day5_defects.py
"""
import json, sys, io, copy
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day5.json"

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

    # 47 조지호: 여권번호 위조 (altVariant 가 결함, PCR 미제출 → 여권번호)
    c = cust[47]; main_v = c; alt = c["altVariant"]
    pp = get_doc(alt, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "여권번호"
    setf(pp, "passport_no", "JP1031276")             # KOR인데 앞자리 JP → 발급국 불일치
    # PCR 복구: 미제출 결함 제거 — main 의 정상 PCR 을 alt 에 추가(없을 때만, 멱등)
    if get_doc(alt, "PCR검사서") is None:
        main_pcr = get_doc(main_v, "PCR검사서")
        if main_pcr is not None:
            alt["documents"].append(copy.deepcopy(main_pcr))
    set_reject(alt, "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "passport_no", "value": "", "label": "여권번호 불일치", "unlocksScan": ""})
    log.append("47 조지호(alt) → 여권번호 위조(KO→JP, PCR 복구)")

    # 48 윤건우: 지문 도용-이름 (altVariant 가 결함, dbName≠여권name=딴사람)
    c = cust[48]; alt = c["altVariant"]
    pp = get_doc(alt, "여권"); pp_name = getf(pp, "name")  # YOON GEONWOO
    pp_birth = getf(pp, "birth_date")
    fp = alt["fingerprint"]
    STOLEN = "KIM TAEHYUN"                            # 여권과 다른 타인 이름(도용)
    fp["result"] = "불일치"; fp["detail"] = "신원 불일치"
    fp["extra"] = STOLEN
    fp["claim"] = {"attr": "name", "value": STOLEN, "label": "지문 대조 신원", "unlocksScan": ""}
    fp["record"]["dbName"] = STOLEN                   # 이름 불일치(여권 YOON GEONWOO ≠ KIM TAEHYUN)
    fp["record"]["dbBirth"] = pp_birth               # 생년월일은 일치(이름만 결함)
    set_reject(alt, "지문 신원이 여권과 일치하지 않습니다. 입국하실 수 없습니다.",
               {"attr": "name", "value": STOLEN, "label": "지문 대조 신원", "unlocksScan": ""})
    log.append("48 윤건우(alt) → 지문 도용-이름(dbName=KIM TAEHYUN≠YOON GEONWOO)")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day5 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
