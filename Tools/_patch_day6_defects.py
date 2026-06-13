# -*- coding: utf-8 -*-
"""
6일차 결함을 결함배분표(권위)에 맞춤. day6.json 직접 패치(런타임 소스). 멱등.
거절멘트는 data/대사_스크립트.xlsx Day6 시트 + 플랜 패턴 기준(표=정답, 대사_스크립트 Day6는 옛버전).
- 오은우(52, MAIN=결함): 여권 생년월일 → 여권 성별(gender 반대, 생년월일 복구)
- 마이클 데이비스(32, MAIN=결함): PCR 검사결과(Negative 버그) → PCR 검사기관 위조(무허가 lab_name, pcr_result Negative 정상화, claim attr=lab_name)
- 다니엘 테일러(54, MAIN=결함): 여권 사진 → 이름 불일치(여권≠비자)(비자 name≠여권 name, 여권 사진 복구, claim attr=name)
※ 이 3명은 MAIN 변형이 결함(alt 가 정상). set_reject/문서수정은 top-level(main)을 건드림.
※ s5 다나카유이·s7 사토는 이미 정합 — 건드리지 않음. 메인DB/FORCED_PHOTO/설계문서는 일괄 동기화.
실행: python Tools/_patch_day6_defects.py
"""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day6.json"

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

    # 52 오은우: 여권 성별 (MAIN 결함, 생년월일 → 성별)
    c = cust[52]; pp = get_doc(c, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "성별"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 정상(본인 얼굴)
    setf(pp, "birth_date", c["birthDate"])           # 생년월일 정상 복구
    setf(pp, "gender", opp_gender(c["gender"]))      # 성별 불일치
    set_reject(c, "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "gender", "value": "", "label": "성별 불일치", "unlocksScan": ""})
    log.append("52 오은우 → 여권 성별(생년월일 복구)")

    # 32 마이클 데이비스: PCR 검사기관 위조(무허가) (MAIN 결함, 검사결과 버그 → 기관)
    c = cust[32]; pcr = get_doc(c, "PCR검사서")
    pcr["variant"] = "비정상"; pcr["violationField"] = "검사 기관"
    setf(pcr, "lab_name", "튼튼메디 무허가검사소")    # 무허가(미인증) 검사기관
    setf(pcr, "pcr_result", "Negative")              # 검사 결과는 정상(음성)으로 정상화
    set_reject(c, "서류 오류가 확인되어 입국하실 수 없습니다.",
               {"attr": "lab_name", "value": "튼튼메디 무허가검사소", "label": "검사 기관 불일치", "unlocksScan": ""})
    log.append("32 마이클 데이비스 → PCR 기관 위조(무허가, 결과 Negative 정상화)")

    # 54 다니엘 테일러: 이름 불일치(여권≠비자) (MAIN 결함, 사진 → 이름)
    c = cust[54]
    pp = get_doc(c, "여권"); vz = get_doc(c, "비자")
    pp["variant"] = "정상"; pp["violationField"] = "없음"
    pp["spriteRef"] = c["spriteRef"]                 # 사진 결함 제거(본인 얼굴 복구)
    pp_name = getf(pp, "name")                        # DANIEL TAYLOR
    fake_name = "DANIELLE TAYLER"                     # 비자 이름을 여권과 다르게(위조)
    vz["variant"] = "비정상"; vz["violationField"] = "영문이름"
    setf(vz, "name", fake_name)
    set_reject(c, "비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "name", "value": fake_name, "label": "비자 이름 불일치", "unlocksScan": ""})
    log.append("54 다니엘 테일러 → 이름 불일치(비자 name≠여권 name, 사진 복구)")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day6 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
