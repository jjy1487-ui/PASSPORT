# -*- coding: utf-8 -*-
"""
day8~14 결함·로스터 정합 검증. 권위 = data/일자별_결함배분표.xlsx + 작업 지시.
- 각 손님 결함 종류가 표와 일치하는지 대조 출력
- JSON 유효성 / correctResult 정합 / 로스터(슬롯·이름) 불일치 0
- 같은 날 얼굴(spriteRef) 겹침 0 (사진 결함은 디코이라 제외)
- FORCED_PHOTO 코드에서 day8~14 비사진 손님 제외 확인
실행: python Tools/_verify_day8_14.py
"""
import io
import json
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
GD = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData"

# 결함배분표 + 지시: day -> slot -> (이름, 판정, 결함분류)
TABLE = {
    8: {1: ("안준영", "통과", None), 2: ("송재윤", "랜덤", "여권번호위조"),
        3: ("장 민", "거절", "재직증명서이름불일치"), 4: ("첸 리", "통과", None),
        5: ("오현석", "통과", None), 6: ("매튜 앤더슨", "거절", "비자이름불일치"),
        7: ("전도현", "랜덤", "지문이름")},
    9: {1: ("홍성민", "랜덤", "여권만료일"), 2: ("김서아", "통과", None),
        3: ("류 옌", "거절", "비자종류거짓"), 4: ("윤채원", "랜덤", "여권성별"),
        5: ("이지우", "랜덤", "지문생년월일"), 6: ("크리스 토머스", "거절", "비자번호불일치"),
        7: ("자오 레이", "거절", "유령회사")},
    10: {1: ("박하윤", "통과", None), 2: ("최수아", "랜덤", "여권번호위조"),
         3: ("정다은", "랜덤", "지문이름"), 4: ("스즈키 소라", "거절", "비자번호불일치"),
         5: ("첸 하오", "통과", None), 6: ("강예린", "랜덤", "여권만료일"),
         7: ("첸 웨이", "거절", "비자종류거짓")},
    11: {1: ("존 카터", "거절", "여권번호위조+xray"), 2: ("조유진", "통과", None),
         3: ("자오 친", "거절", "비자국적불일치"), 4: ("윤정호", "통과", None),
         5: ("장소율", "랜덤", "지문국적"), 6: ("앤드류 화이트", "거절", "여권만료일"),
         7: ("천 징", "통과", None)},
    12: {1: ("임가은", "통과", None), 2: ("사토 하루키", "거절", "여권번호위조+xray"),
         3: ("한지아", "거절", "지문이름"), 4: ("오나은", "랜덤", "여권성별"),
         5: ("서하린", "랜덤", "지문생년월일"), 6: ("황 레이", "거절", "비자종류거짓"),
         7: ("신예은", "통과", None)},
    13: {1: ("우 팅", "거절", "비자이름불일치"), 2: ("권서윤", "통과", None),
         3: ("다카하시 리쿠", "거절", "비자국적불일치"), 4: ("제시카 윌슨", "거절", "취업입사일모순"),
         5: ("황민서", "랜덤", "여권만료일"), 6: ("배은서", "랜덤", "지문국적"),
         7: ("쉬 펑", "거절", "비자종류거짓")},
    14: {1: ("문지유", "통과", None), 2: ("선 메이", "거절", "비자번호불일치"),
         3: ("강도식", "거절", "여권번호위조+xray"), 4: ("와타나베 하나", "통과", None),
         5: ("양수빈", "랜덤", "지문이름"), 6: ("에밀리 클락", "거절", "여권사진"),
         7: ("백지원", "통과", None)},
}


def get_doc(o, dt):
    return next((d for d in o.get("documents", []) if d["documentType"] == dt), None)


def getf(doc, key):
    if not doc:
        return None
    return next((f["value"] for f in doc["fields"] if f["key"] == key), None)


def classify(o, cust):
    """결함 변형 객체 o(=거절 변형)에서 결함 분류 문자열 도출."""
    pp = get_doc(o, "여권")
    vz = get_doc(o, "비자")
    emp = get_doc(o, "취업증빙")
    fp = o.get("fingerprint")
    xr = o.get("xray")
    tags = []
    # X-ray
    if xr and xr.get("result") in ("적발", "위험"):
        tags.append("xray")
    # 여권 결함
    if pp and pp.get("variant") == "비정상":
        vf = pp.get("violationField")
        tags.append({"여권번호": "여권번호위조", "성별": "여권성별",
                     "만료일": "여권만료일", "사진": "여권사진"}.get(vf, "여권:" + str(vf)))
    # 비자 결함
    if vz and vz.get("variant") == "비정상":
        vf = vz.get("violationField")
        tags.append({"국적": "비자국적불일치", "영문이름": "비자이름불일치",
                     "여권번호": "비자번호불일치"}.get(vf, "비자:" + str(vf)))
    # 취업증빙 결함
    if emp and emp.get("variant") == "비정상":
        vf = emp.get("violationField")
        tags.append({"이름": "재직증명서이름불일치", "고용 회사": "유령회사",
                     "입사일": "취업입사일모순"}.get(vf, "취업:" + str(vf)))
    # 지문
    if fp and fp.get("result") == "불일치":
        det = fp.get("detail")
        tags.append({"신원 불일치": "지문이름", "생년월일 불일치": "지문생년월일",
                     "국적 불일치": "지문국적"}.get(det, "지문:" + str(det)))
    # 비자종류 거짓: 모든 서류 정상인데 입장 진술 claim attr=visa_type 존재 + 거절
    if not tags:
        entry = next((cs for cs in o.get("dialogueCases", [])
                      if cs.get("caseType") == "입장"), None)
        has_stated = False
        if entry:
            for ln in entry["lines"]:
                cl = ln.get("claim")
                if cl and cl.get("attr") == "visa_type":
                    has_stated = True
        if has_stated:
            tags.append("비자종류거짓")
    # 여권+xray 합성
    if "xray" in tags and "여권번호위조" in tags:
        tags = ["여권번호위조+xray"] + [t for t in tags if t not in ("xray", "여권번호위조")]
    elif "xray" in tags:
        tags = [t for t in tags if t != "xray"] + ["+xray"]
    return ",".join(tags) if tags else "없음"


def defect_obj(c):
    """거절(결함) 변형 객체. MAIN cr=정상거절이면 MAIN, alt cr=정상거절이면 alt."""
    if c.get("correctResult") == "정상 거절":
        return c
    alt = c.get("altVariant")
    if alt and alt.get("correctResult") == "정상 거절":
        return alt
    return None


def main():
    total_mismatch = 0
    roster_mismatch = 0
    json_errors = 0
    face_overlaps = 0

    for d in range(8, 15):
        path = os.path.join(GD, "day%d.json" % d)
        try:
            data = json.load(open(path, encoding="utf-8"))
        except Exception as e:
            print("[JSON ERROR] day%d: %s" % (d, e))
            json_errors += 1
            continue
        by_slot = {c.get("slot"): c for c in data["customers"]}
        print("=== DAY %d ===" % d)
        # 얼굴 겹침: 사진 결함(디코이) 슬롯 제외하고 spriteRef 중복 검사
        faces = {}
        for c in data["customers"]:
            do = defect_obj(c) or c
            is_photo = False
            pp = get_doc(do, "여권")
            if pp and pp.get("violationField") == "사진":
                is_photo = True
            if not is_photo:
                sr = c.get("spriteRef")
                faces.setdefault(sr, []).append(c.get("nameKr"))
        for sr, names in faces.items():
            if len(names) > 1:
                print("  [얼굴 겹침] %s: %s" % (sr, names))
                face_overlaps += 1

        for slot in range(1, 8):
            c = by_slot.get(slot)
            want = TABLE[d].get(slot)
            if not want:
                continue
            wname, wjudg, wdefect = want
            if c is None:
                print("  s%d MISSING (표=%s)" % (slot, wname))
                roster_mismatch += 1
                continue
            # 로스터(이름) 검증
            gname = c.get("nameKr")
            rflag = "OK" if gname == wname else "ROSTER-X(%s)" % gname
            if gname != wname:
                roster_mismatch += 1
            # 판정 검증
            cr = c.get("correctResult")
            do = defect_obj(c)
            if wjudg == "통과":
                jflag = "OK" if cr == "정상 승인" and do is None else "JUDGE-X(cr=%s)" % cr
                got_defect = "—"
            else:
                # 랜덤/거절 모두 결함 변형이 '정상 거절'이어야 함
                jflag = "OK" if do is not None else "JUDGE-X(거절변형없음)"
                got_defect = classify(do, c) if do else "없음"
            # 결함 분류 검증
            if wdefect is None:
                dflag = "OK" if got_defect == "—" else "DEFECT-X"
            else:
                dflag = "OK" if got_defect == wdefect else "DEFECT-X"
            ok = (rflag == "OK" and jflag == "OK" and dflag == "OK")
            if not ok:
                total_mismatch += 1
            mark = "  " if ok else "!!"
            print("%ss%d %-13s 표[%s/%s] 게임[%s] %s %s %s"
                  % (mark, slot, wname, wjudg, wdefect or "—", got_defect,
                     rflag, jflag, dflag))
        print()

    # FORCED_PHOTO 검증
    print("=== FORCED_PHOTO 코드 검증 ===")
    bd = open(r"C:\Users\chris\Documents\produc_build_reecture\Tools\DataImport\build_days.py",
              encoding="utf-8").read()
    import re
    m = re.search(r"FORCED_PHOTO_DEFECT_TOURIST = \{(.*?)\n\}", bd, re.S)
    body = m.group(1)
    active_ids = set(re.findall(r'^\s*"(\d+)",', body, re.M))
    print("  활성 강제사진 id:", sorted(active_ids, key=int))
    should_exclude = {"62", "63", "67", "69", "73", "3", "77", "80",
                      "85", "87", "89", "92", "94"}
    bad = active_ids & should_exclude
    if bad:
        print("  [FAIL] 제외돼야 할 비사진 손님이 강제사진에 남음:", sorted(bad, key=int))
        total_mismatch += len(bad)
    else:
        print("  [OK] day8~14 비사진 손님 전부 제외")
    if "97" in active_ids:
        print("  [OK] 에밀리 클락(97) 여권사진 강제 유지")
    else:
        print("  [WARN] 에밀리 클락(97) 강제사진 누락")

    print()
    print("=== 검증 요약 ===")
    print("  JSON 오류:", json_errors)
    print("  로스터 불일치:", roster_mismatch)
    print("  얼굴 겹침(비사진):", face_overlaps)
    print("  결함/판정 불일치(슬롯):", total_mismatch)
    ok = (json_errors == 0 and roster_mismatch == 0 and total_mismatch == 0)
    print("  >>> %s" % ("ALL PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
