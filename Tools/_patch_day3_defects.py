# -*- coding: utf-8 -*-
"""
3일차 결함을 결함배분표(권위)에 맞춤. day3.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day3 시트(서류 대조 단계 포함)에서 확인해 맞춤.
- 장 웨이(27): 여권 사진 → 국적 불일치(여권≠비자) (비자 nationality≠여권, violationField 국적, claim attr=nationality)
- 배수정(29, alt=결함): 여권 만료일 → 여권번호 위조(앞자리≠국적)
- 윤서린(10): 지문 신원불일치(수배자) — 라벨/수배 확인(유지, 거절멘트만 동기화)
- 야마모토 렌(34): 여권 사진 → 비자 만료일(visa expiry 과거, violationField 만료일)
※ 로스터/메인DB/FORCED_PHOTO 코드/설계문서는 일괄 동기화 단계(건드리지 않음).
실행: python Tools/_patch_day3_defects.py
"""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day3.json"

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

    # 27 장 웨이: 국적 불일치(여권≠비자) — 비자 국적을 여권과 다르게
    c = cust[27]
    pp = get_doc(c, "여권"); vz = get_doc(c, "비자")
    pp["variant"] = "정상"; pp["violationField"] = "없음"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 결함 제거(본인 얼굴 복구)
    vz["variant"] = "비정상"; vz["violationField"] = "국적"
    setf(vz, "nationality", "JPN")                   # 여권 국적(CHN)과 다른 국적
    set_reject(c, "여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "nationality", "value": "JPN", "label": "비자 국적 불일치", "unlocksScan": ""})
    log.append("27 장 웨이 → 국적 불일치(비자 nationality JPN≠여권 CHN)")

    # 29 배수정: 여권번호 위조 (altVariant 가 결함)
    c = cust[29]; alt = c["altVariant"]; pp = get_doc(alt, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "여권번호"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 정상(본인 얼굴)
    setf(pp, "passport_no", "JP1032393")             # KOR인데 앞자리 JP → 발급국 불일치
    setf(pp, "expiry_date", "2029-07-28")            # 만료일 정상 복구
    set_reject(alt, "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "passport_no", "value": "", "label": "여권번호 불일치", "unlocksScan": ""})
    log.append("29 배수정(alt) → 여권번호 위조(KO→JP, 만료일 복구)")

    # 10 윤서린: 지문 도용-이름(수배자) — 데이터 이미 정합. 거절멘트만 대사_스크립트와 동기화.
    c = cust[10]
    set_reject(c, "입국하실 수 없습니다. 보안팀!",
               {"attr": "name", "value": "김서린", "label": "지문 대조 신원", "unlocksScan": ""})
    log.append("10 윤서린 → 지문 도용-이름(수배자 유지, 거절멘트 동기화)")

    # 34 야마모토 렌: 비자 만료일 (여권 사진 결함 제거 → 비자 만료)
    c = cust[34]
    pp = get_doc(c, "여권"); vz = get_doc(c, "비자")
    pp["variant"] = "정상"; pp["violationField"] = "없음"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 결함 제거(본인 얼굴 복구)
    vz["variant"] = "비정상"; vz["violationField"] = "만료일"
    setf(vz, "expiry_date", "2025-08-03")            # 비자 만료(과거)
    set_reject(c, "비자 유효기간이 지나 입국하실 수 없습니다.", None)
    log.append("34 야마모토 렌 → 비자 만료일(여권사진 복구)")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day3 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
