# -*- coding: utf-8 -*-
"""
2일차 결함을 결함배분표(권위)에 맞춤. day2.json 직접 패치(런타임 소스). 멱등.
- 데이비드 스미스(38): 여권 사진 → 여권번호 위조(발급국 불일치)
- 송하늘(23, main=결함): 여권 만료일 → 여권 성별
- 다나카 하루토(26, alt=결함): 여권 사진 → 여권 만료일
- 오만석(28): 여권 생년월일 → 여권 성별
※ 로스터 s5(임도현→한지원)는 별도 스왑 단계. FORCED_PHOTO 코드/메인DB는 일괄 동기화 단계.
실행: python Tools/_patch_day2_defects.py
"""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
DAY = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day2.json"

def opp_gender(g): return "남성" if g == "여성" else "여성"

def get_doc(o, dt):
    for d in o["documents"]:
        if d["documentType"] == dt: return d
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

    # 38 데이비드 스미스: 여권번호 위조 (main 여권 결함)
    c = cust[38]; pp = get_doc(c, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "여권번호"
    pp["spriteRef"] = c["spriteRef"]               # 사진 결함 제거(본인 얼굴 복구)
    setf(pp, "passport_no", "CN1027925")           # USA인데 앞자리 CN → 발급국 불일치
    set_reject(c, "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "passport_no", "value": "", "label": "여권번호 불일치", "unlocksScan": ""})
    log.append("38 데이비드 스미스 → 여권번호 위조")

    # 23 송하늘: 여권 성별 (main 이 결함 변형)
    c = cust[23]; pp = get_doc(c, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "성별"
    setf(pp, "gender", opp_gender(c["gender"]))     # 본인과 반대 성별
    setf(pp, "expiry_date", "2029-01-01")           # 만료일 정상 복구
    set_reject(c, "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "gender", "value": "", "label": "성별 불일치", "unlocksScan": ""})
    log.append("23 송하늘 → 여권 성별")

    # 26 다나카 하루토: 여권 만료일 (altVariant 가 결함)
    c = cust[26]; alt = c["altVariant"]; pp = get_doc(alt, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "만료일"
    pp["spriteRef"] = c["spriteRef"]                # 사진 결함 제거
    setf(pp, "expiry_date", "2024-03-14")           # 과거(만료)
    set_reject(alt, "여권 유효기간이 지나 입국하실 수 없습니다.", None)
    log.append("26 다나카 하루토(alt) → 여권 만료일")

    # 28 오만석: 여권 성별 (main 결함, 생년월일→성별)
    c = cust[28]; pp = get_doc(c, "여권")
    pp["variant"] = "비정상"; pp["violationField"] = "성별"
    setf(pp, "birth_date", c["birthDate"])          # 생년월일 정상 복구
    setf(pp, "gender", opp_gender(c["gender"]))      # 성별 불일치
    set_reject(c, "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.",
               {"attr": "gender", "value": "", "label": "성별 불일치", "unlocksScan": ""})
    log.append("28 오만석 → 여권 성별(생년월일 복구)")

    json.dump(data, open(DAY, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("day2 결함 패치 완료:")
    for l in log: print("  -", l)

if __name__ == "__main__":
    main()
