# -*- coding: utf-8 -*-
"""
branch_normalize.py — 캐릭터 분기 점수표/금액표 정규화 (data-tools 소유)

입력: 새 엑셀(여권주세요_..._금액표추가_캐릭터선지추가_260602.xlsx)의 두 시트
  - "캐릭터별 분기별 지급 금액표(1회차기준)"  (자유서술 8열)
  - "캐릭터별 분기점 점수표(단순화)"          (자유서술 5열)

출력: SHARED-CONVENTIONS 관계형 규약에 맞는 두 테이블
  - character_payout
  - character_score
두 테이블은 (character_type, doc_state, defect_variant, branch_key)로 조인된다.

핵심: 자유서술 분기 텍스트("몽타주 인식 + X-ray + 거부")를 branch_key enum으로 정규화.
변경 흡수 단일 지점(규약 6장): 한글->정규화 매핑은 전부 이 파일에만 둔다. idempotent.
"""
import re

# ── 캐릭터명(엑셀 한글) -> customer.character_type 어휘 매핑 ────────
# 기존 customer 시트 character_type 어휘(확정):
#   일반 고객 / 진상 고객 / 외국인 관광객 / 성형 의심 고객 / 검역 대상자(PCR) /
#   장기체류자 / 취업체류자 / 범죄자(성형수술) / 범죄자(국내 유입자) /
#   범죄자(외국 도피자) / 테러범 / 특수(연예인)★ / 특수(정치인)★ / 특수(현자)★
# 신규(customer 미등재): 사이비 신도, 꼬마
CHAR_MAP = {
    "일반": "일반 고객",
    "진상": "진상 고객",
    "외국인 관광객": "외국인 관광객",
    "성형 수술 고객 (일반)": "성형 의심 고객",
    "전염병 환자": "검역 대상자(PCR)",
    "외국인 장기체류자": "장기체류자",
    "성형 수술 고객 (범죄자)": "범죄자(성형수술)",
    "범죄자": "범죄자(외국 도피자)",           # 금액표 '범죄자'는 도피자/유입자 분기를 모두 포함 -> branch로 구분
    "범죄자 (외국 도피자)": "범죄자(외국 도피자)",
    "범죄자 (국내 유입자)": "범죄자(국내 유입자)",
    "테러범": "테러범",
    "연예인 ★": "특수(연예인)★",
    "연예인": "특수(연예인)★",
    "정치인 ★": "특수(정치인)★",
    "정치인": "특수(정치인)★",
    "현자 ★": "특수(현자)★",
    "현자": "특수(현자)★",
    "사이비 신도": "사이비 신도",               # 신규
    "사이비 신도 (1회차)": "사이비 신도",
    "사이비 신도 (2회차)": "사이비 신도",
    "사이비 신도 (3회차)": "사이비 신도",
    "꼬마": "꼬마",                              # 신규
}


def map_char(raw):
    raw = (raw or "").strip()
    # '범죄자 (외국 도피자)' 처럼 괄호 공백 변형 흡수
    if raw in CHAR_MAP:
        return CHAR_MAP[raw]
    base = re.sub(r"\s*\(.*?\)\s*", "", raw).strip().rstrip("★").strip()
    return CHAR_MAP.get(base, raw)


def visit_round(raw):
    """사이비 신도 (N회차) 등에서 방문 회차 추출. 없으면 None."""
    m = re.search(r"(\d+)\s*회차", raw or "")
    return int(m.group(1)) if m else None


# ── 서류 상태 정규화: 정상/불량 + variant ────────────────────────
def parse_doc_state(raw):
    """ '불량 [1-C 백신X]' -> ('defect', '1-C 백신X').  '정상' -> ('normal', None) """
    s = (raw or "").strip()
    variant = None
    m = re.search(r"\[([^\]]*)\]", s)
    if m:
        variant = m.group(1).strip()
        s = re.sub(r"\[[^\]]*\]", "", s).strip()
    if s.startswith("불량") or "불량" in s:
        return "defect", variant
    if s.startswith("정상") or "정상" in s:
        return "normal", variant
    return s or "normal", variant


# ── branch_key enum 정규화 ───────────────────────────────────────
# 자유서술 '분기/결과' 텍스트 -> 안정 enum. (점수표/금액표가 같은 키로 조인되게)
# 규칙: 패턴 매칭 우선순위 순서대로 검사. 못 찾으면 fallback(slug).
def norm_branch(raw, doc_state):
    t = (raw or "").strip()
    low = t

    # ── 재거절 감액 단계(성형/연예인/정치인 공통) ──
    if "즉시 입국" in low or "즉시입국" in low:
        return "approve_immediate"
    if re.search(r"1\s*회\s*거절", low):
        return "approve_after_reject_1"
    if re.search(r"2\s*회\s*거절", low):
        return "approve_after_reject_2"
    if re.search(r"3\s*회\s*(강제\s*)?입국|강제\s*입국", low):
        return "approve_after_reject_3"
    if re.search(r"거부\s*1\s*회", low):
        return "reject_accrue_1"
    if re.search(r"거부\s*2\s*회", low):
        return "reject_accrue_2"
    if re.search(r"거부\s*3\s*회", low):
        return "reject_accrue_3"

    # ── 범죄자/테러범 적발 분기 ──
    if "자수 유도" in low or "달램" in low and "설교" in low and "시한폭탄" not in low and "신고" not in low:
        return "terror_persuade_confess"
    if "신고 성공" in low:
        return "terror_persuade_report_ok"
    if "설교" in low and "시한폭탄" in low:
        return "terror_persuade_then_bomb"
    if "신고 들킴" in low and "무관심" in low:
        return "terror_ignore_report_caught_bomb"
    if "신고 들킴" in low:
        return "terror_persuade_report_caught_bomb"
    if "여권 → 상담 → 설교" in low or ("여권" in low and "상담" in low and "설교" in low):
        return "terror_ignore_then_persuade"
    if "여권 → 시한폭탄" in low or ("여권" in low and "시한폭탄" in low and "상담" not in low):
        return "terror_ignore_then_bomb"
    if "제압 성공" in low:
        return "terror_ignore_subdue_ok"
    if "제압 실패" in low:
        return "terror_ignore_subdue_fail"
    if "X-ray 폭탄" in low or "폭탄 발견" in low:
        return "terror_xray_bomb_reject"

    if "몽타주 인식 + X-ray" in low or ("몽타주" in low and "X-ray" in low and "거부" in low):
        return "detect_montage_xray_reject"
    if "몽타주 인식" in low and "거부" in low:
        return "detect_montage_reject"
    if "거부" in low and ("몽타주 인식O" in low or "몽타주O" in low):
        return "detect_montage_reject"
    if "단순 거부" in low or "운 좋음" in low:
        return "reject_lucky"

    # ── 부패(응함) 분기 ──
    if "응함" in low and "금괴" in low:
        return "corrupt_accept_gold"
    if "응함" in low and ("마약" in low or "밀수품" in low):
        return "corrupt_accept_drugs"

    # ── 성형/몽타주 오판 세부 (점수표 전용) ──
    if "마스크 미요청" in low or "마스크 안 벗기고" in low:
        return "approve_wrong_no_mask"
    if "지문 미요청" in low or "지문 미확인" in low:
        return "approve_wrong_no_fingerprint"
    if "몽타주O" in low and "범인 아님" in low:
        return "approve_correct_montage_clear"
    if "몽타주X" in low:
        return "approve_wrong_no_montage"

    # ── 사이비 신도 응답 조합 ──
    if "예/예/예" in low and "새뇌" in low:
        return "cult_yyy_brainwash"
    if "아니오/예/예" in low and ("신종" in low or "추종자" in low):
        return "cult_nyy_follower"
    if "최고 조합" in low or "강해야/예/예" in low:
        return "cult_best_combo"
    if "예/예/예" in low:
        return "cult_yyy"
    if "예/예/아니오" in low or "예/아니오/예" in low:
        return "cult_partial_yes"
    if "아니오/아니오/아니오" in low:
        return "cult_all_no"
    if "기타 조합" in low:
        return "cult_other_combo"
    if "신도" in low and "거부" in low:
        return "cult_reject"
    if "거부" in low and ("응답" in low or "어떤" in low):
        return "cult_reject"
    # 금액표 사이비 신도: '1회차 거부' / '2회차 입국 (최고 조합)' 등 (회차는 visit_round로 별도 보존)
    if "거부" in low and "회차" in low:
        return "cult_reject"

    # ── 꼬마 ──
    if "발판 제공" in low:
        return "approve_with_step"
    if "발판 미제공" in low:
        return "approve_no_step"

    # ── 현자(분기는 doc_state variant로 식별, 결과는 아이템 드롭) ──
    if "마법" in low and "입국" in low:
        return "approve_sage_item"

    # ── 연예인/정치인 마스크/대리 ──
    if "마스크 벗기 요청" in low and "1턴" in low:
        return "mask_request_turn_1"
    if "마스크 벗기 요청" in low and "2턴" in low:
        return "mask_request_turn_2"
    if "인성 논란" in low:
        return "scandal_third_turn"
    if "본인 직접 요청" in low:
        return "proxy_self_request"

    # ── 기본 정답/오판 ──
    if "입국" in low and ("정답" in low):
        return "approve_correct" if doc_state == "normal" else "approve_correct"
    if "거부" in low and "정답" in low:
        return "reject_correct"
    if "입국" in low and "오판" in low:
        return "approve_wrong"
    if "거부" in low and "오판" in low:
        return "reject_wrong"

    # fallback: 슬러그
    slug = re.sub(r"[^0-9A-Za-z]+", "_", t).strip("_").lower()
    return slug or "unknown"


# ── 트리거 추출(호칭/아이템/조기엔딩/이벤트) ───────────────────────
EVENT_RE = re.compile(r"#(\d+)")


def extract_event(*texts):
    for t in texts:
        if not t:
            continue
        m = EVENT_RE.search(t)
        if m:
            return "#" + m.group(1)
    return None


TITLE_PATTERNS = [
    ("우수 사원", "우수 사원"),
    ("청렴한 직원", "청렴한 직원"),
    ("전문 테러 방지반", "전문 테러 방지반"),
    ("경찰아저씨 여기예요", "경찰아저씨 여기예요"),
    ("스며들기", "스며들기"),
    ("새뇌", "새뇌"),
    ("신종 추종자", "신종 추종자"),
    ("해탈한 자", "해탈한 자"),
]


def extract_title(*texts):
    blob = " ".join(t for t in texts if t)
    for needle, title in TITLE_PATTERNS:
        if needle in blob:
            return title
    # '... 호칭' 일반 패턴
    m = re.search(r"'([^']+)'\s*호칭", blob)
    if m:
        return m.group(1)
    m = re.search(r"호칭\s*'([^']+)'", blob)
    if m:
        return m.group(1)
    return None


ITEM_PATTERNS = [
    "마법의 수정구슬", "마법의 구슬", "마법의 손목시계", "마법의 거울",
    "마법의 손수건", "마법의 주머니", "마법의 와인", "마법의 지갑",
    "맛있는 사탕", "발판", "마법 아이템", "금괴", "마약", "밀수품",
]


def extract_item(*texts):
    blob = " ".join(t for t in texts if t)
    for it in ITEM_PATTERNS:
        if it in blob:
            return it
    return None


def parse_money(raw):
    """ '+280원' / '-80원 (벌금)' / '160 + 120 포상금' -> 정수 산식 결과는 산식 그대로 보존,
        지급액은 첫 부호 숫자. """
    if raw is None:
        return None
    m = re.search(r"([+-]?\d+)\s*원", str(raw))
    if m:
        return int(m.group(1))
    m = re.search(r"([+-]?\d+)", str(raw))
    return int(m.group(1)) if m else None


def parse_score(raw):
    """ '+3' / '-12' / '0~+6' / '(이벤트)' / '+1 추가' -> (int 또는 None, range_text 또는 None) """
    if raw is None:
        return None, None
    s = str(raw).strip()
    if "~" in s:
        return None, s  # 범위
    if "(" in s and not re.search(r"[+-]?\d", s):
        return None, s  # (이벤트)
    m = re.search(r"([+-]?\d+)", s)
    if m:
        return int(m.group(1)), (s if "추가" in s else None)
    return None, s or None


def parse_tier(raw):
    """ '160원 + 포상 120원' -> (base=160, bounty=120). '100원' -> (100, 0). '0원' -> (0,0) """
    if raw is None:
        return None, 0
    s = str(raw)
    nums = re.findall(r"(\d+)", s)
    base = int(nums[0]) if nums else None
    bounty = 0
    bm = re.search(r"포상\s*(\d+)", s)
    if bm:
        bounty = int(bm.group(1))
    return base, bounty


def truthy_o(raw):
    """ 'O (1~14일, 26회)' -> True, 'X (조기엔딩)' -> False """
    s = (raw or "").strip()
    if s.startswith("O"):
        return "true"
    if s.startswith("X"):
        return "false"
    return ""


# ── 시트 -> 정규화 행 ────────────────────────────────────────────
PAYOUT_COLS = [
    {"key": "payout_id", "type": "int", "label": "지급ID", "pkfk": "PK"},
    {"key": "character_type", "type": "varchar", "label": "캐릭터유형", "pkfk": "FK customer.character_type"},
    {"key": "visit_round", "type": "int", "label": "방문회차", "pkfk": ""},
    {"key": "doc_state", "type": "varchar", "label": "서류상태", "pkfk": ""},
    {"key": "defect_variant", "type": "varchar", "label": "결함세부", "pkfk": ""},
    {"key": "branch_key", "type": "varchar", "label": "분기키", "pkfk": ""},
    {"key": "base_tier", "type": "int", "label": "기본금액", "pkfk": ""},
    {"key": "bounty", "type": "int", "label": "포상금", "pkfk": ""},
    {"key": "payout", "type": "int", "label": "지급금액", "pkfk": ""},
    {"key": "formula", "type": "varchar", "label": "산식", "pkfk": ""},
    {"key": "item_drop", "type": "varchar", "label": "아이템드롭", "pkfk": ""},
    {"key": "early_ending", "type": "varchar", "label": "조기엔딩", "pkfk": ""},
    {"key": "appears_round1", "type": "bool", "label": "1회차등장", "pkfk": ""},
    {"key": "note", "type": "varchar", "label": "비고", "pkfk": ""},
]

SCORE_COLS = [
    {"key": "score_id", "type": "int", "label": "점수ID", "pkfk": "PK"},
    {"key": "character_type", "type": "varchar", "label": "캐릭터유형", "pkfk": "FK customer.character_type"},
    {"key": "visit_round", "type": "int", "label": "방문회차", "pkfk": ""},
    {"key": "doc_state", "type": "varchar", "label": "서류상태", "pkfk": ""},
    {"key": "defect_variant", "type": "varchar", "label": "결함세부", "pkfk": ""},
    {"key": "branch_key", "type": "varchar", "label": "분기키", "pkfk": ""},
    {"key": "score", "type": "int", "label": "점수", "pkfk": ""},
    {"key": "score_range", "type": "varchar", "label": "점수범위", "pkfk": ""},
    {"key": "title", "type": "varchar", "label": "호칭", "pkfk": ""},
    {"key": "event_id", "type": "varchar", "label": "이벤트", "pkfk": ""},
    {"key": "note", "type": "varchar", "label": "비고", "pkfk": ""},
]


def _cols_out(cols):
    return [{"key": c["key"], "type": c["type"], "label": c["label"]} for c in cols]


def build_payout(sheet_rows):
    """금액표 시트(헤더 1행 + 데이터). sheet_rows = list of list(셀)."""
    out = []
    pid = 0
    for r in sheet_rows[1:]:
        char_raw = (r[0] if len(r) > 0 else "") or ""
        if not str(char_raw).strip():
            continue
        tier_raw = r[1] if len(r) > 1 else None
        state_raw = r[2] if len(r) > 2 else None
        branch_raw = r[3] if len(r) > 3 else None
        money_raw = r[4] if len(r) > 4 else None
        formula = r[5] if len(r) > 5 else None
        appear_raw = r[6] if len(r) > 6 else None
        note = r[7] if len(r) > 7 else None

        ctype = map_char(char_raw)
        vround = visit_round(char_raw) or visit_round(branch_raw)
        doc_state, variant = parse_doc_state(state_raw)
        branch = norm_branch(branch_raw, doc_state)
        base, bounty = parse_tier(tier_raw)
        payout = parse_money(money_raw)
        item = extract_item(branch_raw, note, formula)
        event = extract_event(appear_raw, note, branch_raw)
        early = event if (appear_raw and str(appear_raw).strip().startswith("X")) else None
        pid += 1
        out.append({
            "payout_id": str(pid),
            "character_type": ctype,
            "visit_round": str(vround) if vround else None,
            "doc_state": doc_state,
            "defect_variant": variant,
            "branch_key": branch,
            "base_tier": str(base) if base is not None else None,
            "bounty": str(bounty),
            "payout": str(payout) if payout is not None else None,
            "formula": _s(formula),
            "item_drop": item,
            "early_ending": early,
            "appears_round1": truthy_o(appear_raw),
            "note": _s(note),
        })
    return {"columns": _cols_out(PAYOUT_COLS), "rows": out, "layout": "relational"}


def build_score(sheet_rows):
    """점수표 시트(헤더 1행 + 데이터). sheet_rows = list of list(셀)."""
    out = []
    sid = 0
    for r in sheet_rows[1:]:
        char_raw = (r[0] if len(r) > 0 else "") or ""
        if not str(char_raw).strip():
            continue
        state_raw = r[1] if len(r) > 1 else None
        branch_raw = r[2] if len(r) > 2 else None
        score_raw = r[3] if len(r) > 3 else None
        note = r[4] if len(r) > 4 else None

        ctype = map_char(char_raw)
        vround = visit_round(char_raw)
        doc_state, variant = parse_doc_state(state_raw)
        branch = norm_branch(branch_raw, doc_state)
        score, srange = parse_score(score_raw)
        title = extract_title(branch_raw, note)
        event = extract_event(note, branch_raw)
        sid += 1
        out.append({
            "score_id": str(sid),
            "character_type": ctype,
            "visit_round": str(vround) if vround else None,
            "doc_state": doc_state,
            "defect_variant": variant,
            "branch_key": branch,
            "score": str(score) if score is not None else None,
            "score_range": srange,
            "title": title,
            "event_id": event,
            "note": _s(note),
        })
    return {"columns": _cols_out(SCORE_COLS), "rows": out, "layout": "relational"}


def _s(v):
    if v is None:
        return None
    s = str(v).strip()
    return s if s else None
