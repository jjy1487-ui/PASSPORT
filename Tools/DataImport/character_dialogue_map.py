# -*- coding: utf-8 -*-
"""
character_dialogue_map.py — character_dialogue.json 의 캐릭터별 손글 대사를
dayN.json 케이스의 손님 라인에 주입한다(유형 평면 대사를 customerId별 대사로 덮어씀).

위치(파이프라인 순서):
    build_days → branch_dialogue_map(유형 폴백) → **character_dialogue_map(캐릭터 오버라이드)**
              → authored_lines(잔여 [TODO] 폴백)
  즉 캐릭터 레이어가 유형 레이어 위에 덮어쓴다. build_days.py 가 손님 1명을 만들 때
  fill_customer_dialogue(유형) 직후, fill_authored(잔여 TODO) 직전에 이 모듈을 호출한다.

설계 원칙(작업 지시 준수):
  - 케이스 구조(개수/order/speaker)는 절대 바꾸지 않는다. **손님 라인 text 만** 교체한다.
  - 심사관 라인(speaker="심사관"/"검사관"/"시스템")은 그대로 둔다.
  - localize_speakers 이전에 호출되므로 손님 speaker 는 아직 "캐릭터"/"손님" 이다.
  - 캐논에 해당 대사가 없으면(빈 문자열) 그 라인은 건드리지 않는다(유형 폴백 살림).
  - day1.json 은 build_days 가 처리(이 모듈은 customer_entry 단위로만 동작, 파일 IO 없음).
  - idempotent: 같은 character_dialogue.json + 같은 customer_entry → 같은 결과.

매핑 규칙(케이스별, 손님 라인만):
  - 입장(gameResult="-"): 첫 손님 라인 = 캐논 entry[0]. day JSON 입장이 2줄이고 캐논 인삿말이
    1줄이면 1줄째만 교체(2줄째는 유형 폴백 유지, 비우지 않음). 라인 수 불변.
  - 정상 승인: 손님 라인 = 캐논 허가 반응(approveReaction).
  - 잘못 허가: 손님 라인 = 캐논 허가 반응(approveReaction). (correctResult="정상 거절" 손님의 오판)
  - 정상 거절: 손님 라인 = 캐논 거부 반응(rejectReaction).
  - 잘못 거절 rc1/2/3: 첫 손님 항의 라인(order 2) = 캐논 거부 반응(rejectReaction).
    심사관 번복(order 3)·그 뒤 손님 수긍(order 4)은 day JSON 유지(번복 연출 보존).
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CHAR_DIALOGUE_PATH = os.path.join(ROOT, "Assets", "Resources", "GameData", "character_dialogue.json")

# 손님측 speaker(아직 nameKr 치환 전). localize_speakers 와 동일 어휘.
_VISITOR_SPEAKERS = {"캐릭터", "손님"}


def load_char_dialogue():
    """character_dialogue.json -> {customerId(int): entry dict}. 없으면 빈 dict."""
    if not os.path.exists(CHAR_DIALOGUE_PATH):
        return {}
    with open(CHAR_DIALOGUE_PATH, encoding="utf-8") as f:
        raw = json.load(f)
    out = {}
    for k, v in raw.items():
        try:
            out[int(k)] = v
        except (TypeError, ValueError):
            continue
    return out


def _is_visitor_line(ln, name_kr=""):
    """손님 라인 판별. build_days 경로는 speaker 가 아직 '캐릭터'/'손님' 이지만,
    day1.json 처럼 이미 nameKr 로 치환(localize)된 본도 손님 라인을 잡아야 하므로
    speaker == 그 손님 nameKr 인 경우도 손님 라인으로 본다(심사관/시스템은 제외).
    """
    sp = ln.get("speaker")
    if sp in _VISITOR_SPEAKERS:
        return True
    return bool(name_kr) and sp == name_kr


def _visitor_lines(case, name_kr=""):
    return [ln for ln in case.get("lines", []) if _is_visitor_line(ln, name_kr)]


def apply_character_override(customer, index, report=None):
    """customer.dialogueCases 의 손님 라인 text 를 캐논 대사로 교체(in-place).

    index   : load_char_dialogue() 결과({customerId: entry}).
    report  : 선택. dict 누적 통계. 키 = (caseType, gameResult) -> {"override":n, "skip":n}.
    반환     : (overridden, skipped) 이번 손님 누적(교체한 손님 라인 / 캐논 결측으로 건너뛴 손님 라인).
    """
    cid = customer.get("customerId")
    entry = index.get(int(cid)) if cid is not None else None
    if entry is None:
        return 0, 0

    name_kr = customer.get("nameKr") or ""
    canon_entry = list(entry.get("entry") or [])
    approve = entry.get("approveReaction") or ""
    reject = entry.get("rejectReaction") or ""

    overridden = 0
    skipped = 0

    def _rep(case_type, gr, ov, sk):
        if report is None:
            return
        r = report.setdefault((case_type, gr), {"override": 0, "skip": 0})
        r["override"] += ov
        r["skip"] += sk

    for case in customer.get("dialogueCases", []):
        ct = case.get("caseType")
        gr = case.get("gameResult")
        vlines = _visitor_lines(case, name_kr)
        if not vlines:
            continue

        if ct == "입장":
            # 입장: 첫 손님 라인 = 캐논 entry[0]. 캐논 인삿말이 여러 줄이면 순서대로,
            # day JSON 라인 수를 넘지 않게 채운다(없는 줄은 그대로 유지).
            ov = sk = 0
            for i, ln in enumerate(vlines):
                if i < len(canon_entry) and canon_entry[i]:
                    ln["text"] = canon_entry[i]
                    ov += 1
                else:
                    sk += 1
            overridden += ov
            skipped += sk
            _rep(ct, gr, ov, sk)
            continue

        # 일반 심사 케이스
        if gr in ("정상 승인", "잘못 허가"):
            src = approve
        elif gr in ("정상 거절", "잘못 거절"):
            src = reject
        else:
            src = ""

        if not src:
            # 캐논에 해당 반응이 없으면 손님 라인 유지(유형 폴백 살림).
            skipped += len(vlines)
            _rep(ct, gr, 0, len(vlines))
            continue

        if gr == "잘못 거절":
            # 첫 손님 라인(항의, order 2)만 교체. 그 뒤 손님 수긍 라인(order 4)은 day JSON 유지.
            first = vlines[0]
            first["text"] = src
            overridden += 1
            skipped += max(0, len(vlines) - 1)
            _rep(ct, gr, 1, max(0, len(vlines) - 1))
        else:
            # 정상 승인/정상 거절/잘못 허가: 손님 라인(보통 1줄) 전부 동일 반응으로 교체.
            for ln in vlines:
                ln["text"] = src
                overridden += 1
            _rep(ct, gr, len(vlines), 0)

    return overridden, skipped
