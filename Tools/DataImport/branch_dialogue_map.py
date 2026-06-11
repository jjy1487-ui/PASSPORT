# -*- coding: utf-8 -*-
"""
branch_dialogue_map.py — branch_dialogue.json 의 시나리오 대사를 dayN.json 케이스에 주입한다.

build_days.py 가 만드는 dayN 손님의 dialogueCases([TODO 대사] placeholder)를,
characterType + gameResult 로 branch_dialogue 블록과 조인해 실제 대사로 채운다.

설계 원칙(작업 지시 준수):
  - 케이스 구조(개수/order/speaker)는 절대 바꾸지 않는다. text 만 교체한다.
  - branch_dialogue 에 해당 유형·결과 대사가 없으면 추측하지 않고 [TODO 대사] 유지.
  - day1.json 은 손대지 않는다(build_days 가 2~14만 생성).

branch_dialogue 블록 구조:
  각 블록 lines 의 「...」 인용문만 실제 발화. order=2(혹은 첫 인용)=입장 캐릭터 대사,
  order=6-1/6-2 = 판정 후 발화(심사관/캐릭터). 외국인 관광객은 영/일/중 3언어가
  한 블록에 연속 → 첫 언어(영어)만 채택.
"""
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BRANCH_PATH = os.path.join(ROOT, "Assets", "Resources", "GameData", "branch_dialogue.json")

TODO = "[TODO 대사]"

# dayN characterType -> branch_dialogue characterType.
# 특수/범죄자 세부유형은 branch 의 단일 유형으로 합치되, 범죄자는 branchLabel 로 세분.
TYPE_MAP = {
    "일반 고객": "일반 고객",
    "진상 고객": "진상 고객",
    "외국인 관광객": "외국인 관광객",
    "검역 대상자(PCR)": "전염병 환자",
    "전염병 환자": "전염병 환자",                 # 현행 day JSON characterType(소스 갱신 반영)
    "취업체류자": "외국인 장기체류자 (취업)",
    "장기체류자": "외국인 장기체류자 (대학)",
    "성형 의심 고객": "성형 수술 고객 (일반)",
    "성형 수술 고객": "성형 수술 고객 (일반)",     # 현행 day JSON characterType(비범죄 성형)
    "범죄자(성형수술)": "성형 수술 고객 (범죄자)",
    "범죄자(밀수품 범죄자)": "범죄자",
    "범죄자(마약 범죄자)": "범죄자",
    "테러범": "테러범",
    "특수(연예인)★": "연예인·정치인 ★",
    "특수(정치인)★": "연예인·정치인 ★",
    "특수(현자)★": "꼬마·현자 ★",
}

# 한 branch characterType 시트가 '══ 구획(section)' 으로 여러 캐릭터를 합쳐 담는 경우,
# dayN characterType 마다 사용할 section 키워드를 지정한다.
# (연예인·정치인 ★ 시트 = '연예인 ★' / '정치인 ★' 두 구획 → 톤이 묶이지 않도록 분리.)
# 값은 section 문자열에 포함되어야 하는 키워드. 미지정 유형은 section 무시(전체 사용).
SECTION_FILTER = {
    "특수(연예인)★": "연예인",
    "특수(정치인)★": "정치인",
}

# 범죄자 세부유형 -> branchLabel 에 포함되어야 하는 키워드(블록 필터).
# 키=dayN characterType(엑셀 새이름). 값=branch_dialogue.json 의 기존 조인 라벨(구명칭 유지).
#   밀수품 범죄자(존 카터,12일) = 구 '외국 도피자' 블록 / 마약 범죄자(강도식,14일) = 구 '국내 유입자' 블록.
CRIMINAL_BRANCH_KEY = {
    "범죄자(밀수품 범죄자)": "외국 도피자",
    "범죄자(마약 범죄자)": "국내 유입자",
}

_QUOTE = re.compile(r"「(.+?)」", re.S)


def _first_quote(text):
    """한 라인 텍스트에서 첫 인용문(「...」)을 뽑아 정리. 없으면 None.
    인용문 안의 줄바꿈/괄호 번역병기는 첫 줄만 남긴다(외국어 병기 제거)."""
    m = _QUOTE.search(text or "")
    if not m:
        return None
    seg = m.group(1).strip()
    # 인용문 내부 줄바꿈은 '원문\n(번역)' 형태 → 첫 줄(원문)만.
    if "\n" in seg:
        seg = seg.split("\n", 1)[0].strip()
    return seg or None


def load_branch():
    with open(BRANCH_PATH, encoding="utf-8") as f:
        return json.load(f)


# ── 가이드(보스) 케이스 로더 ────────────────────────────────────
# 일반 매퍼(_first_quote)는 「」 인용문만 남기고 [시스템]/검문관 절차 라인을 버린다.
# 보스 시퀀스(예: 성형 수술 지명수배범 분기 A)는 그 절차 라인이 연출의 핵심이므로
# 별도 로더로 블록 전체를 정리해서 보존한다.
_GUIDED_SKIP_PREFIX = ("[UI", "[모션]", "[사전 이벤트]")
_DASA_PREFIX = re.compile(r"^\s*대사\s*\d*\s*[:：]\s*")
_SPEAKER_LABEL = re.compile(r"^\s*(검문관|심사관|방문객)\s*[:：]\s*")


def _clean_guided_text(text):
    """가이드 라인 정리: 개행 평탄화 + ※주석 제거 + '대사 N:'/'검문관:' 라벨 제거 + 「」 해제."""
    if not text:
        return ""
    s = text.replace("\n", " ").strip()
    if "※" in s:                       # 디자이너 주석(예 '※ 포상금 +120원...') 제거
        s = s.split("※", 1)[0].strip()
    s = _DASA_PREFIX.sub("", s)         # '대사 1:' 접두 제거
    s = _SPEAKER_LABEL.sub("", s)       # '검문관:'/'심사관:' 라벨 제거(speaker 로 구분됨)
    s = s.replace("「", "").replace("」", "")  # 인용 괄호 해제
    return s.strip()


def load_guided_block(branch_ctype, label_substring):
    """branch_dialogue 의 한 블록을 보스 가이드 대사 케이스로 반환(_first_quote 우회).
    순수 연출 큐([UI .../[모션]/[사전 이벤트])는 제외, [시스템]/검문관/방문객 라인은 보존.
    order 는 1부터 순차 int(원본 '4-1' 등 문자열 회피). 반환: [{order,speaker,text}] 또는 None."""
    if not branch_ctype:
        return None
    bd = load_branch()
    for t in bd.get("types", []):
        if t.get("characterType") != branch_ctype:
            continue
        for b in t.get("blocks", []):
            if label_substring not in (b.get("branchLabel") or ""):
                continue
            out = []
            order = 0
            for ln in b.get("lines", []):
                flat = (ln.get("text") or "").replace("\n", " ").strip()
                if flat.startswith(_GUIDED_SKIP_PREFIX):
                    continue
                txt = _clean_guided_text(ln.get("text"))
                if not txt:
                    continue
                order += 1
                out.append({"order": order, "speaker": ln.get("speaker") or "심사관", "text": txt})
            return out if out else None
    return None


def index_branch(bd):
    """branch_dialogue -> { branchCharType: [ {gameResult, branchLabel, entry, results:[(speaker,text)...]} ] }.

    entry  : 입장 캐릭터 대사(첫 인용문, speaker=캐릭터). 없으면 None.
    results: 판정 후 발화 리스트 [(speaker, text), ...] (입장 대사 제외).
    한 블록에 같은 발화가 언어별로 반복되면(관광객) 첫 등장만 채택.
    """
    out = {}
    for t in bd["types"]:
        ctype = t["characterType"]
        blocks = []
        for b in t["blocks"]:
            entry = None
            results = []
            seen_entry = False
            lang_markers = 0   # '[영어]/[일본어]/[중국어]' 헤더 카운트. 2번째부터는 무시(첫 언어만).
            for ln in b["lines"]:
                order = str(ln.get("order", ""))
                # 언어 그룹 헤더(speaker 비고, order 가 '[...]') → 카운트하고 스킵.
                if (ln["speaker"] or "") == "" and order.startswith("[") and order.endswith("]"):
                    lang_markers += 1
                    continue
                # 두 번째 언어 그룹 이후는 첫 언어 중복이므로 채택하지 않는다.
                if lang_markers >= 2:
                    continue
                q = _first_quote(ln["text"])
                if q is None:
                    continue
                sp = ln["speaker"]
                # 입장 대사: 첫 캐릭터 인용문(order 2 계열). 언어 반복 시 첫 것만.
                if not seen_entry and sp == "캐릭터" and (order == "2" or order.startswith("2")):
                    entry = q
                    seen_entry = True
                    continue
                # 같은 (speaker,text) 중복(언어 반복) 제거
                if (sp, q) in results:
                    continue
                results.append((sp, q))
            blocks.append({
                "section": (b.get("section") or "").strip(),
                "gameResult": b["gameResult"],
                "branchLabel": (b.get("branchLabel") or "").strip(),
                "entry": entry,
                "results": results,
            })
        out[ctype] = blocks
    return out


def _by_section(blocks, section_key):
    """section 키워드가 지정되면 해당 구획 블록만 남긴다(없으면 전체).
    매칭되는 블록이 하나도 없으면(데이터 변형) 전체로 폴백한다."""
    if not section_key:
        return blocks
    keyed = [b for b in blocks if section_key in b.get("section", "")]
    return keyed if keyed else blocks


def _select_block(blocks, game_result, branch_key=None, section_key=None):
    """gameResult(+선택 branch_key/section 키워드)로 블록 1개 선택. 없으면 None.
    같은 gameResult 블록이 여럿이면 첫 번째(정답/대표 분기)를 쓴다."""
    cands = _by_section(blocks, section_key)
    cands = [b for b in cands if b["gameResult"] == game_result]
    if branch_key:
        keyed = [b for b in cands if branch_key in b["branchLabel"]]
        if keyed:
            cands = keyed
    return cands[0] if cands else None


def _entry_block(blocks, branch_key=None, section_key=None):
    """입장 대사용 블록: entry 가 있는 첫 블록(필요시 section/branch_key 우선)."""
    pool = _by_section(blocks, section_key)
    if branch_key:
        keyed = [b for b in pool if branch_key in b["branchLabel"]]
        if keyed:
            pool = keyed
    for b in pool:
        if b["entry"]:
            return b
    return None


def fill_customer_dialogue(customer, index, report):
    """customer dialogueCases 의 [TODO 대사] text 를 branch 대사로 교체(in-place).
    구조(개수/order/speaker)는 불변. 못 채운 라인은 [TODO] 유지하고 report 에 기록.

    report: dict, 키 = (dayCharType, caseType, gameResult) -> {'filled':n,'todo':n}
    반환: (filled, todo) 이번 손님 누적.
    """
    day_ctype = customer["characterType"]
    branch_ctype = TYPE_MAP.get(day_ctype)
    branch_key = CRIMINAL_BRANCH_KEY.get(day_ctype)
    section_key = SECTION_FILTER.get(day_ctype)  # 연예인/정치인 톤 분리(구획 필터)

    filled_total = 0
    todo_total = 0

    blocks = index.get(branch_ctype) if branch_ctype else None

    for case in customer["dialogueCases"]:
        ctype_case = case["caseType"]
        gr = case["gameResult"]
        lines = case["lines"]

        rep_key = (day_ctype, ctype_case, gr)
        rep = report.setdefault(rep_key, {"filled": 0, "todo": 0})

        # 채울 발화 목록 결정
        if blocks is None:
            # 매핑 가능한 branch 유형 없음 → 전부 TODO 유지
            for ln in lines:
                if TODO in ln["text"]:
                    rep["todo"] += 1
                    todo_total += 1
            continue

        if ctype_case == "입장":
            blk = _entry_block(blocks, branch_key, section_key)
            # 입장 2줄(캐릭터,캐릭터) → entry 대사 1개를 첫 줄에. 둘째 줄은 안내 멘트.
            speak_pool = []
            if blk and blk["entry"]:
                speak_pool.append(blk["entry"])
        else:
            blk = _select_block(blocks, gr, branch_key, section_key)
            speak_pool = []
            if blk:
                if blk["entry"]:
                    speak_pool_entry = blk["entry"]
                else:
                    speak_pool_entry = None
                # 판정 후 발화 리스트
                speak_pool = list(blk["results"])

        # 라인별 채우기 — speaker 일치 우선, 순서대로 소비
        if ctype_case == "입장":
            # 입장: 첫 캐릭터 라인에 entry 대사. 나머지 라인은 채울 소스 없음 → TODO 유지.
            used = False
            for ln in lines:
                if TODO not in ln["text"]:
                    continue
                if not used and speak_pool:
                    ln["text"] = speak_pool[0]
                    used = True
                    rep["filled"] += 1
                    filled_total += 1
                else:
                    rep["todo"] += 1
                    todo_total += 1
            continue

        # 일반 심사 케이스: results 를 speaker 매칭하며 소비.
        # speak_pool = [(speaker,text), ...]  (entry 제외, 판정 후 발화)
        pool = list(speak_pool) if blk else []

        def take(speaker):
            # speaker 가 일치하는 첫 항목을 꺼낸다. 없으면 speaker 무시하고 첫 항목.
            for i, (sp, tx) in enumerate(pool):
                if sp == speaker:
                    return pool.pop(i)[1]
            if pool:
                return pool.pop(0)[1]
            return None

        for ln in lines:
            if TODO not in ln["text"]:
                continue
            tx = take(ln["speaker"]) if pool else None
            if tx is not None:
                ln["text"] = tx
                rep["filled"] += 1
                filled_total += 1
            else:
                rep["todo"] += 1
                todo_total += 1

    return filled_total, todo_total
