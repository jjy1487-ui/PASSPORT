# -*- coding: utf-8 -*-
"""
8~14일차 결함 정합 공용 헬퍼. day2~7 _patch_dayN_defects.py 와 같은 철학.
판정은 correctResult 만 봄 → 문서 단서 + 정상거절 라인(text+claim)만 바꾸면 안전. 멱등.
거절멘트는 data/대사_스크립트.xlsx Day8~14(정합 완료)에서 추출한 값.
"""
import io
import json
import sys

# 강제 UTF-8 (Windows 콘솔)
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

GAMEDATA = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData"


def opp_gender(g):
    return "남성" if g == "여성" else "여성"


def load(path):
    return json.load(open(path, encoding="utf-8"))


def save(path, data):
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)


def by_id(data):
    return {c["customerId"]: c for c in data["customers"]}


def get_doc(o, dt):
    for d in o.get("documents", []):
        if d["documentType"] == dt:
            return d
    return None


def getf(doc, key):
    for f in doc["fields"]:
        if f["key"] == key:
            return f["value"]
    return None


def setf(doc, key, val):
    for f in doc["fields"]:
        if f["key"] == key:
            f["value"] = val
            return
    doc["fields"].append({"label": key, "value": val, "key": key})


def set_reject(o, text, claim=None):
    """그 변형의 '일반 심사 / 정상 거절' 케이스 첫 라인의 text(+claim)만 교체."""
    for case in o.get("dialogueCases", []):
        if case.get("caseType") == "일반 심사" and case.get("gameResult") == "정상 거절":
            ln = case["lines"][0]
            ln["text"] = text
            if claim is not None:
                ln["claim"] = claim
            elif "claim" in ln:
                del ln["claim"]
            return True
    return False


def find_entry_case(o):
    for case in o.get("dialogueCases", []):
        if case.get("caseType") == "입장":
            return case
    return None


# ── 결함 적용 빌더(진실 복구 + 결함 1개 + 거절멘트) ─────────────────────────
def _main_passport_value(cust, key):
    """cust(MAIN) 의 여권에서 정상값을 읽어 다른 변형의 결함 정리에 사용."""
    pp = get_doc(cust, "여권")
    return getf(pp, key) if pp else None


def apply_passport_no_forge(variant_obj, cust, pno_forged, reject_text):
    """여권번호 위조: 앞자리가 발급국 코드와 불일치. spriteRef/만료일 본인 복구(단일 결함)."""
    pp = get_doc(variant_obj, "여권")
    pp["variant"] = "비정상"
    pp["violationField"] = "여권번호"
    pp["spriteRef"] = cust["spriteRef"]
    setf(pp, "passport_no", pno_forged)
    # 다른 변형에서 넘어온 잔여 결함(만료일 등) 정상값으로 복구 → 결함은 여권번호 하나만.
    normal_expiry = _main_passport_value(cust, "expiry_date")
    if normal_expiry:
        setf(pp, "expiry_date", normal_expiry)
    set_reject(variant_obj, reject_text,
               {"attr": "passport_no", "value": "", "label": "여권번호 불일치", "unlocksScan": ""})


def apply_passport_gender(variant_obj, cust, reject_text):
    """여권 성별: passport gender = 본인 성별 반대. spriteRef/생년월일 본인 복구."""
    pp = get_doc(variant_obj, "여권")
    pp["variant"] = "비정상"
    pp["violationField"] = "성별"
    pp["spriteRef"] = cust["spriteRef"]
    setf(pp, "birth_date", cust["birthDate"])
    setf(pp, "gender", opp_gender(cust["gender"]))
    set_reject(variant_obj, reject_text,
               {"attr": "gender", "value": "", "label": "성별 불일치", "unlocksScan": ""})


def apply_passport_expiry(variant_obj, cust, expiry_past, reject_text):
    """여권 만료일: expiry 과거. spriteRef/생년월일 본인 복구(다른 잔여 결함 정리), 만료일 하나만."""
    pp = get_doc(variant_obj, "여권")
    pp["variant"] = "비정상"
    pp["violationField"] = "만료일"
    pp["spriteRef"] = cust["spriteRef"]
    setf(pp, "birth_date", cust["birthDate"])  # 생년월일 잔여 결함 복구
    setf(pp, "expiry_date", expiry_past)
    set_reject(variant_obj, reject_text, None)


def _restore_passport(variant_obj, cust):
    """사진 강제 결함을 제거하고 여권을 정상으로 되돌림(비자/취업 쪽 결함용)."""
    pp = get_doc(variant_obj, "여권")
    pp["variant"] = "정상"
    pp["violationField"] = "없음"
    pp["spriteRef"] = cust["spriteRef"]
    return pp


def apply_visa_field_mismatch(variant_obj, cust, attr, fake_value, vf_label, reject_text, claim_label):
    """비자 ≠ 여권: 비자의 attr(name/passport_no/nationality)을 여권과 다르게."""
    _restore_passport(variant_obj, cust)
    vz = get_doc(variant_obj, "비자")
    vz["variant"] = "비정상"
    vz["violationField"] = vf_label
    setf(vz, attr, fake_value)
    set_reject(variant_obj, reject_text,
               {"attr": attr, "value": fake_value, "label": claim_label, "unlocksScan": ""})


def apply_false_purpose(variant_obj, cust, stated_purpose, visa_purpose, reject_text):
    """비자종류 거짓-진술: 비자(정상)의 visa_type=visa_purpose, 입장 진술=stated_purpose.
    어떤 서류도 비정상이 아니며, 진술 ↔ 비자 대조로 적발."""
    _restore_passport(variant_obj, cust)
    vz = get_doc(variant_obj, "비자")
    vz["variant"] = "정상"
    vz["violationField"] = "없음"
    setf(vz, "visa_type", visa_purpose)
    # 입장 케이스에 방문목적 진술 claim 주입(이미 있으면 갱신)
    entry = find_entry_case(variant_obj)
    purpose_claim = {"attr": "visa_type", "value": stated_purpose,
                     "label": "방문 목적(진술)", "unlocksScan": ""}
    # 진술 라인이 이미 있으면 그 claim 만 갱신, 없으면 마지막 손님 라인 앞에 추가하지 않고
    # 첫 손님 라인에 claim 부착(최소 변경, day4 리강과 동형 — 진술 claim 한 개만 필요).
    visitor_lines = [ln for ln in entry["lines"] if ln.get("speaker") == cust["nameKr"]]
    target = None
    for ln in entry["lines"]:
        if ln.get("claim") and ln["claim"].get("attr") == "visa_type":
            target = ln
            break
    if target is None and visitor_lines:
        target = visitor_lines[0]
    if target is not None:
        target["claim"] = purpose_claim
    set_reject(variant_obj, reject_text, None)


def apply_fingerprint_stolen(o, cust, subtype, wrong_value, reject_text):
    """지문 도용. subtype in {name, birth, nationality}. passport 진실은 손대지 않음.
    record 의 한 필드만 여권과 다르게(나머지는 여권과 동일)."""
    pp = get_doc(o, "여권")
    pname = getf(pp, "name")
    pbirth = getf(pp, "birth_date")
    pnat = getf(pp, "nationality")
    if subtype == "name":
        detail = "신원 불일치"
        attr = "name"
        label = "지문 대조 신원"
        dbName, dbBirth, dbNat = wrong_value, pbirth, pnat
        extra = wrong_value
        cval = wrong_value
    elif subtype == "birth":
        detail = "생년월일 불일치"
        attr = "birth_date"
        label = "지문 대조 생년월일"
        dbName, dbBirth, dbNat = pname, wrong_value, pnat
        extra = pname
        cval = wrong_value
    elif subtype == "nationality":
        detail = "국적 불일치"
        attr = "nationality"
        label = "지문 대조 국적"
        dbName, dbBirth, dbNat = pname, pbirth, wrong_value
        extra = pname
        cval = wrong_value
    else:
        raise ValueError(subtype)
    o["fingerprint"] = {
        "type": "fingerprint",
        "result": "불일치",
        "detail": detail,
        "extra": extra,
        "claim": {"attr": attr, "value": cval, "label": label, "unlocksScan": ""},
        "record": {
            "mode": "성형",
            "dbName": dbName,
            "dbBirth": dbBirth,
            "dbNationality": dbNat,
            "criminalRecord": "없음",
            "wantedNo": "",
        },
    }
    set_reject(o, reject_text,
               {"attr": attr, "value": cval, "label": label, "unlocksScan": ""})


def _make_employment_doc(cust, cert_no, company, job_title, hire_date, issue_date, name_value):
    """취업증빙(재직증명서) 문서 1장 생성. label/key 는 첸 리(정상) 스키마와 동일."""
    def fld(key, label, value):
        return {"label": label, "value": value, "key": key}
    return {
        "documentType": "취업증빙",
        "variant": "정상",
        "violationField": "없음",
        "fields": [
            fld("name", "이름", name_value),
            fld("cert_no", "증빙 번호", cert_no),
            fld("company_name", "고용 회사", company),
            fld("job_title", "직종", job_title),
            fld("hire_date", "입사일", hire_date),
            fld("issue_date", "발급일", issue_date),
        ],
    }


def apply_employment_name_mismatch(variant_obj, cust, fake_name, cert_no,
                                   company, job_title, hire_date, issue_date, reject_text):
    """재직증명서 이름 불일치(신분 도용): 취업증빙 name ≠ 여권 영문이름.
    취업증빙 문서가 없으면 새로 추가, 있으면 name/violationField 만 갱신. 사진/비자 정상화."""
    _restore_passport(variant_obj, cust)
    emp = get_doc(variant_obj, "취업증빙")
    if emp is None:
        emp = _make_employment_doc(cust, cert_no, company, job_title,
                                   hire_date, issue_date, fake_name)
        variant_obj.setdefault("documents", []).append(emp)
    else:
        setf(emp, "name", fake_name)
    emp["variant"] = "비정상"
    emp["violationField"] = "이름"
    set_reject(variant_obj, reject_text,
               {"attr": "name", "value": fake_name, "label": "재직증명서 이름 불일치", "unlocksScan": ""})


def normalize_visa(variant_obj, cust, **field_overrides):
    """비자를 정상으로 되돌리고(국적/이름 등 잔여 결함 복구), 지정 필드만 덮어쓴다.
    여권의 정상값을 기준으로 비자 nationality/passport_no/name 을 맞춘다."""
    vz = get_doc(variant_obj, "비자")
    if vz is None:
        return None
    pp = get_doc(variant_obj, "여권")
    if pp is not None:
        for key in ("nationality", "passport_no", "name"):
            v = getf(pp, key)
            if v is not None:
                setf(vz, key, v)
    for key, val in field_overrides.items():
        setf(vz, key, val)
    vz["variant"] = "정상"
    vz["violationField"] = "없음"
    return vz


# ── 검증 ─────────────────────────────────────────────────────────────────
def defect_variant(c):
    """판정상의 '결함 변형' 객체 반환(MAIN 이 거절이면 MAIN, 아니면 altVariant)."""
    if c.get("correctResult") == "정상 거절":
        return c
    alt = c.get("altVariant")
    if alt and alt.get("correctResult") == "정상 거절":
        return alt
    return None
