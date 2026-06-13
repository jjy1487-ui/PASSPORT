# -*- coding: utf-8 -*-
"""
한지원(id15)·임도현(id24)·장우진(id49) 3-사이클 슬롯 스왑 패치 (멱등).

현재 → 목표
  - 한지원(id15): day5 slot5  ->  day2 slot5   (PCR 제거, 여권만, day2 대사)
  - 임도현(id24): day2 slot5  ->  day5 slot2   (PCR 추가, day5 대사)
  - 장우진(id49): day5 slot2  ->  day5 slot5   (slot만 이동, 얼굴 임도현->박지훈)

결함 손님(다른 슬롯)은 절대 건드리지 않는다.
멱등: 이미 목표 상태면 무변경.
"""
import io
import json
import os
import sys

# 강제 UTF-8 (Windows 콘솔)
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

GAMEDATA = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Resources", "GameData",
)
DAY2_PATH = os.path.join(GAMEDATA, "day2.json")
DAY5_PATH = os.path.join(GAMEDATA, "day5.json")

ID_HANJIWON = 15
ID_IMDOHYUN = 24
ID_JANGWOOJIN = 49


def load(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def backup_once(path):
    """day*.json.bak_roster 백업을 한 번만 생성(멱등)."""
    bak = path + ".bak_roster"
    if not os.path.exists(bak):
        with open(path, "r", encoding="utf-8") as src:
            content = src.read()
        with open(bak, "w", encoding="utf-8") as dst:
            dst.write(content)
        print(f"[backup] 생성: {os.path.basename(bak)}")
    else:
        print(f"[backup] 이미 존재(스킵): {os.path.basename(bak)}")


def find_by_id(customers, cid):
    for c in customers:
        if c.get("customerId") == cid:
            return c
    return None


def find_by_slot(customers, slot):
    for c in customers:
        if c.get("slot") == slot:
            return c
    return None


def pcr_doc_imdohyun():
    """임도현 PCR검사서(정상 음성)."""
    return {
        "documentType": "PCR검사서",
        "variant": "정상",
        "violationField": "없음",
        "spriteRef": "",
        "country": "",
        "fields": [
            {"label": "검사 번호", "value": "PCR-024", "key": "test_no"},
            {"label": "검사일", "value": "2026-05-30", "key": "issue_date"},
            {"label": "검사 결과", "value": "Negative", "key": "pcr_result"},
            {"label": "유효 기한", "value": "2026-06-26", "key": "valid_until"},
            {"label": "검사 기관", "value": "인천공항검역소", "key": "lab_name"},
            {"label": "비고", "value": "정상 PCR(음성)", "key": "memo"},
        ],
    }


def dialogue_hanjiwon_day2():
    """한지원 day2용 대사(여권만, PCR 언급 없음). day2 슬롯5(임도현) 구조 틀."""
    return [
        {
            "caseType": "입장",
            "gameResult": "-",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "한지원",
                 "text": "안녕하세요. (모자를 눌러쓰며) 팬분들이 공항까지 와 계셔서 좀 정신없네요."},
                {"order": 2, "speaker": "한지원",
                 "text": "여권 확인 부탁드립니다."},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "정상 승인",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관", "text": "즐거운 여행 되십시오."},
                {"order": 2, "speaker": "한지원",
                 "text": "감사합니다. 무대에서 좋은 모습 보여드릴게요!"},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "정상 거절",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관",
                 "text": "서류 오류가 확인되어 입국하실 수 없습니다."},
                {"order": 2, "speaker": "한지원",
                 "text": "네? 제가요? 뭔가 착오가 있는 것 같은데요."},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "잘못 허가",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관", "text": "즐거운 여행 되십시오."},
                {"order": 2, "speaker": "한지원",
                 "text": "감사합니다. 무대에서 좋은 모습 보여드릴게요!"},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "잘못 거절",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관",
                 "text": "확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다."},
                {"order": 2, "speaker": "한지원",
                 "text": "네? 제가요? 뭔가 착오가 있는 것 같은데요."},
            ],
        },
    ]


def dialogue_imdohyun_day5():
    """임도현 day5용 대사(PCR 포함). day5 통과 손님(장우진/임선우) 구조 틀."""
    return [
        {
            "caseType": "입장",
            "gameResult": "-",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "임도현",
                 "text": "안녕하세요. 방역 강화됐다고 들어서 PCR 검사서도 챙겨왔어요."},
                {"speaker": "심사관",
                 "text": "방역 절차에 따라 PCR 검사서를 확인하겠습니다. 제출해 주세요.",
                 "claim": None, "order": 2},
                {"order": 3, "speaker": "임도현", "text": "여권 확인 부탁드립니다."},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "정상 승인",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관", "text": "즐거운 여행 되십시오."},
                {"order": 2, "speaker": "임도현",
                 "text": "감사합니다~ 즐거운 하루 되세요!"},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "정상 거절",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관",
                 "text": "서류 오류가 확인되어 입국하실 수 없습니다."},
                {"order": 2, "speaker": "임도현",
                 "text": "정말 이상하네요. 제가 확인했거든요."},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "잘못 허가",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관", "text": "즐거운 여행 되십시오."},
                {"order": 2, "speaker": "임도현",
                 "text": "감사합니다~ 즐거운 하루 되세요!"},
            ],
        },
        {
            "caseType": "일반 심사",
            "gameResult": "잘못 거절",
            "rejectCount": 0,
            "lines": [
                {"order": 1, "speaker": "심사관",
                 "text": "확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다."},
                {"order": 2, "speaker": "임도현",
                 "text": "정말 이상하네요. 제가 확인했거든요."},
            ],
        },
    ]


def build_hanjiwon_for_day2(han_src):
    """day5의 한지원 객체를 day2 slot5용으로 변환(여권만, day2 대사)."""
    c = json.loads(json.dumps(han_src))  # deep copy
    c["slot"] = 5
    # 여권만 남기고 PCR 제거
    c["documents"] = [d for d in c.get("documents", [])
                      if d.get("documentType") == "여권"]
    c["correctResult"] = "정상 승인"
    c["defectVariant"] = ""
    c["characterType"] = "특수(연예인)★"
    c["spriteRef"] = "한지원"
    c["dialogueCases"] = dialogue_hanjiwon_day2()
    c["rejectAdvancedBranchKey"] = ""
    c["rejectGuidedCaseType"] = ""
    c["xray"] = None
    c["fingerprint"] = None
    # 확률 변형 없음
    c.pop("validChance", None)
    c.pop("altVariant", None)
    return c


def build_imdohyun_for_day5(im_src):
    """day2의 임도현 객체를 day5 slot2용으로 변환(여권+PCR, day5 대사)."""
    c = json.loads(json.dumps(im_src))  # deep copy
    c["slot"] = 2
    # 여권 유지 + PCR 추가(중복 방지)
    docs = [d for d in c.get("documents", []) if d.get("documentType") == "여권"]
    docs.append(pcr_doc_imdohyun())
    c["documents"] = docs
    c["correctResult"] = "정상 승인"
    c["defectVariant"] = ""
    c["characterType"] = "일반 고객"
    c["spriteRef"] = "임도현"
    c["dialogueCases"] = dialogue_imdohyun_day5()
    c["rejectAdvancedBranchKey"] = ""
    c["rejectGuidedCaseType"] = ""
    c["xray"] = None
    c["fingerprint"] = None
    c.pop("validChance", None)
    c.pop("altVariant", None)
    return c


def patch():
    day2 = load(DAY2_PATH)
    day5 = load(DAY5_PATH)

    d2c = day2["customers"]
    d5c = day5["customers"]

    han = find_by_id(d5c, ID_HANJIWON) or find_by_id(d2c, ID_HANJIWON)
    im = find_by_id(d2c, ID_IMDOHYUN) or find_by_id(d5c, ID_IMDOHYUN)
    jang = find_by_id(d5c, ID_JANGWOOJIN) or find_by_id(d2c, ID_JANGWOOJIN)

    if han is None or im is None or jang is None:
        print("[error] 대상 손님(15/24/49) 중 일부를 찾지 못함. 중단.")
        return False

    # 멱등 판정: 이미 목표 상태인가?
    d2_slot5 = find_by_slot(d2c, 5)
    d5_slot2 = find_by_slot(d5c, 2)
    d5_slot5 = find_by_slot(d5c, 5)
    already = (
        d2_slot5 is not None and d2_slot5.get("customerId") == ID_HANJIWON
        and d5_slot2 is not None and d5_slot2.get("customerId") == ID_IMDOHYUN
        and d5_slot5 is not None and d5_slot5.get("customerId") == ID_JANGWOOJIN
        and d5_slot5.get("spriteRef") == "박지훈"
    )
    if already:
        print("[idempotent] 이미 목표 상태 — 무변경.")
        return True

    # 백업(최초 1회)
    backup_once(DAY2_PATH)
    backup_once(DAY5_PATH)

    # ① day2 slot5 = 한지원 (변환본)
    han_new = build_hanjiwon_for_day2(han)
    # 기존 day2 customers에서 임도현(24)·한지원(15) 제거 후 한지원 삽입
    day2["customers"] = [c for c in d2c
                         if c.get("customerId") not in (ID_IMDOHYUN, ID_HANJIWON)]
    day2["customers"].append(han_new)

    # ② day5 slot2 = 임도현 (변환본)
    im_new = build_imdohyun_for_day5(im)

    # ③ day5 slot5 = 장우진 (이동 + 얼굴 박지훈)
    jang_new = json.loads(json.dumps(jang))
    jang_new["slot"] = 5
    jang_new["spriteRef"] = "박지훈"
    for d in jang_new.get("documents", []):
        if d.get("documentType") == "여권":
            d["spriteRef"] = "박지훈"

    # day5 customers 재구성: 한지원(15)·장우진(49)·임도현(24) 제거 후 새 임도현/장우진 삽입
    day5["customers"] = [c for c in d5c
                         if c.get("customerId") not in
                         (ID_HANJIWON, ID_JANGWOOJIN, ID_IMDOHYUN)]
    day5["customers"].append(im_new)
    day5["customers"].append(jang_new)

    # 슬롯 오름차순 정렬(가독성/일관성)
    day2["customers"].sort(key=lambda c: c.get("slot", 0))
    day5["customers"].sort(key=lambda c: c.get("slot", 0))

    save(DAY2_PATH, day2)
    save(DAY5_PATH, day5)
    print("[patch] day2/day5 저장 완료.")
    return True


def verify():
    """검증 리포트."""
    ok = True
    day2 = load(DAY2_PATH)
    day5 = load(DAY5_PATH)

    def doc_types(c):
        return [d.get("documentType") for d in c.get("documents", [])]

    # day2 slot5 = 한지원, 여권만
    s = find_by_slot(day2["customers"], 5)
    if s and s.get("nameKr") == "한지원" and doc_types(s) == ["여권"] \
            and s.get("customerId") == ID_HANJIWON:
        print("[verify] OK  day2 slot5 = 한지원, 서류=[여권]")
    else:
        ok = False
        print(f"[verify] FAIL day2 slot5 = {s and s.get('nameKr')}, 서류={s and doc_types(s)}")

    # day5 slot2 = 임도현, 여권+PCR
    s = find_by_slot(day5["customers"], 2)
    if s and s.get("nameKr") == "임도현" and doc_types(s) == ["여권", "PCR검사서"] \
            and s.get("customerId") == ID_IMDOHYUN:
        print("[verify] OK  day5 slot2 = 임도현, 서류=[여권, PCR검사서]")
    else:
        ok = False
        print(f"[verify] FAIL day5 slot2 = {s and s.get('nameKr')}, 서류={s and doc_types(s)}")

    # day5 slot5 = 장우진, spriteRef 박지훈
    s = find_by_slot(day5["customers"], 5)
    if s and s.get("nameKr") == "장우진" and s.get("spriteRef") == "박지훈" \
            and s.get("customerId") == ID_JANGWOOJIN:
        pass_doc = all(d.get("spriteRef") == "박지훈"
                       for d in s.get("documents", [])
                       if d.get("documentType") == "여권")
        if pass_doc:
            print("[verify] OK  day5 slot5 = 장우진, spriteRef=박지훈(여권 동기화)")
        else:
            ok = False
            print("[verify] FAIL day5 slot5 장우진 여권 spriteRef != 박지훈")
    else:
        ok = False
        print(f"[verify] FAIL day5 slot5 = {s and s.get('nameKr')}, spriteRef={s and s.get('spriteRef')}")

    # day5 얼굴 중복 없음
    faces = [c.get("spriteRef") for c in day5["customers"]]
    dups = {f for f in faces if faces.count(f) > 1}
    if not dups:
        print(f"[verify] OK  day5 얼굴 중복 없음: {faces}")
    else:
        ok = False
        print(f"[verify] FAIL day5 얼굴 중복: {dups} / {faces}")

    # 슬롯 1~7 완전성
    for name, data in (("day2", day2), ("day5", day5)):
        slots = sorted(c.get("slot") for c in data["customers"])
        if slots == [1, 2, 3, 4, 5, 6, 7]:
            print(f"[verify] OK  {name} 슬롯 1~7 완전")
        else:
            ok = False
            print(f"[verify] FAIL {name} 슬롯 = {slots}")

    return ok


if __name__ == "__main__":
    print("=== 3-사이클 스왑 패치 (한지원/임도현/장우진) ===")
    if patch():
        print("--- 검증 ---")
        if verify():
            print("[done] 모든 검증 통과.")
            sys.exit(0)
        else:
            print("[done] 검증 실패 항목 있음.")
            sys.exit(1)
    else:
        sys.exit(2)
