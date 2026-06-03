# -*- coding: utf-8 -*-
"""
build_days.py — GameData.source.json -> Assets/Resources/GameData/day2.json ... day14.json

「여권 주세요」 2~14일차 케이스 데이터 생성기.
- 1일차(day1.json)는 수작업 완성본이므로 절대 건드리지 않는다(포맷 호환성 검증만).
- 진실 서류만 source에 있고, 비정상 슬롯이면 defect_rule로 결함 1개를 결정론적으로 주입한다.
- 대사는 메뉴 최소 플레이용 짧은 공통 라인만 생성하고 [TODO 대사] 표식을 남긴다.

스키마 대상: Assets/Scripts/Inspection/DataModels.cs 의 Day1Data 계열.
시드: 20260602 (고정, 슬롯별 결정론적 resolve)

실행:
    python Tools/DataImport/build_days.py
산출:
    Assets/Resources/GameData/day2.json ... day14.json (13개)
"""
import json
import os
import hashlib

# ── 경로 ─────────────────────────────────────────────────────
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "GameData")

SEED = 20260602
TODO = "[TODO 대사]"

# ── 속성 키 어휘 & 한글 라벨 -> 속성 키 매핑 (단일 진실, 6장: 변경 흡수 한 곳) ─────
# 표시는 한글 라벨, 비교는 속성 키. 새 컬럼/라벨이 와도 여기만 고친다.
# 공백 변형("여권번호"/"여권 번호") 모두 흡수하기 위해 정규화 후 조회한다.
ATTR_VOCAB = {
    "name", "nationality", "birth_date", "gender", "face", "passport_no",
    "issue_date", "expiry_date", "visa_type", "visa_no", "pcr_result",
    "valid_until", "lab_name", "test_no", "company_name", "job_title",
    "cert_no", "entry_type", "purpose", "memo", "contraband",
}

# 한글 라벨(공백 제거) -> 속성 키. day1 수작업 라벨 + 시트 라벨 둘 다 커버.
LABEL_TO_ATTR = {
    # 공통/여권
    "여권번호": "passport_no",
    "영문이름": "name",
    "영문이름": "name",
    "성별": "gender",
    "생년월일": "birth_date",
    "국적": "nationality",
    "발급국": "nationality",
    "발급일": "issue_date",
    "만료일": "expiry_date",
    "유효기간": "expiry_date",
    "사진": "face",
    "사진참조": "face",
    # 비자
    "비자번호": "visa_no",
    "비자종류": "visa_type",
    "입국횟수": "entry_type",
    "비고": "memo",
    # pcr
    "검사번호": "test_no",
    "검사일": "issue_date",
    "검사결과": "pcr_result",
    "유효기한": "valid_until",
    "검사기관": "lab_name",
    # 취업증빙
    "증빙번호": "cert_no",
    "고용회사": "company_name",
    "직종": "job_title",
    # rule_book related_field 전용(중복 키는 위와 동일)
    "여권": "passport_no",
    "비자": "visa_type",
}


def _norm_label(label):
    """라벨 정규화: 공백 제거 + 소문자(영문 MRZ 대응)."""
    if label is None:
        return ""
    return str(label).replace(" ", "").lower()


def attr_for_label(label):
    """한글 라벨 -> 속성 키. 매핑 없으면 ''(대조 비대상)."""
    return LABEL_TO_ATTR.get(_norm_label(label), "")


def tag_fields(fields):
    """fields[] 각 항목에 key(속성 키)를 채운다. 원본 리스트를 그대로 갱신."""
    for f in fields:
        f["key"] = attr_for_label(f.get("label"))
    return fields

# document_requirement 의 취업증빙 대상 캐릭터 유형(실제 DB 표기)
EMPLOYMENT_TYPES = {"취업체류자", "장기체류자"}
PCR_TYPE = "검역 대상자(PCR)"
TOURIST_TYPE = "외국인 관광객"


# ── defect_variant 결정 (BRANCH_CATALOG 어휘 정합, baked 데이터로 도출 가능한 변이만) ──
# 같은 branch_key 라도 변이별 점수가 다른 캐릭터는 변이를 day JSON 에 기록해야
# 게임플레이 BranchKeyResolver 가 character_score/payout 을 정확히 조인한다.
# baked 데이터로 결정 가능한 변이만 채우고(관광객·검역 대상자 PCR),
# 그 외(성형/테러/연예인/현자 등 런타임 상태머신 결과)는 "" 로 둔다(게임플레이가 결정).
#
# 어휘 출처: Tools/DataImport/BRANCH_CATALOG.md (character_score.defect_variant 와 1:1).
def resolve_defect_variant(character_type, is_normal, corruption_type, target_field):
    """주입된 결함(corruption_type/target_field)으로부터 defect_variant 키를 도출.
    도출 불가/대상 아님이면 "" 반환. corruption_type 은 실제 적용된 단일 타입."""
    ct = (corruption_type or "").strip().upper()
    tf = (target_field or "").strip()

    # 검역 대상자(PCR): normal=1-A, 결함은 corruption 으로 1-B/1-C/1-D 구분
    if character_type == PCR_TYPE:
        if is_normal:
            return "1-A"
        if ct == "POSITIVE":          # 양성 = 백신 미접종/감염
            return "1-C 백신X"
        if ct == "MISSING":           # 결과 누락 = 모두 미비
            return "1-D 모두 미비"
        if ct in ("FORGE_SOURCE", "EXPIRE"):  # 위조/만료 = 출국X/만료
            return "1-B 출국X/만료"
        return ""

    # 외국인 관광객: 발급국 위조/도용 = 분실, 만료(체류 초과) = 출국X
    if character_type == TOURIST_TYPE:
        if is_normal:
            return ""
        if ct == "ALTER_FIELD" and tf == "nationality":
            return "분실"
        if ct == "EXPIRE":
            return "출국X"
        return ""

    # 그 외 캐릭터: baked 로 도출 불가(런타임 상태머신 결과) → 게임플레이가 결정.
    return ""


# ── 결정론적 resolve ─────────────────────────────────────────
def seeded_unit(*parts):
    """seed + 식별자들로 0~1 사이의 결정론적 실수 생성."""
    key = f"{SEED}:" + ":".join(str(p) for p in parts)
    h = hashlib.sha256(key.encode("utf-8")).hexdigest()
    return int(h[:8], 16) / 0xFFFFFFFF


def resolve_normal(valid_chance, day, slot, customer_id):
    """valid_chance로 정상/비정상 1회 판정. 1=정상, 0=비정상, 그 외=시드 resolve."""
    vc = float(valid_chance)
    if vc >= 1.0:
        return True
    if vc <= 0.0:
        return False
    return seeded_unit("normal", day, slot, customer_id) < vc


def seeded_pick(options, *parts):
    """리스트에서 시드로 하나 선택."""
    if len(options) == 1:
        return options[0]
    idx = int(seeded_unit("pick", *parts) * len(options)) % len(options)
    return options[idx]


# ── 자료 로드 & 인덱싱 ───────────────────────────────────────
def load_source():
    with open(SOURCE, encoding="utf-8") as f:
        return json.load(f)["sheets"]


def rows(sheet):
    return sheet["rows"]


def label_map(sheet):
    """컬럼 key -> 한글 label."""
    return {c["key"]: (c["label"] or c["key"]) for c in sheet["columns"]}


# ── 이름 정규화 (MRZ 표기 제거) ──────────────────────────────
def clean_name(name_en):
    """여권 MRZ식 이름 표기('NGUYEN<<VAN')를 일반 표기('NGUYEN VAN')로. <</< -> 공백, 연속공백 정리."""
    if not name_en:
        return name_en
    s = str(name_en).replace("<", " ")
    return " ".join(s.split())


# ── 서류 fields 빌더 ─────────────────────────────────────────
def passport_fields(p):
    """day1.json 라벨/순서를 그대로 따른다(MRZ 제거)."""
    country = p["nationality"]
    return [
        {"label": "여권번호", "value": p["passport_no"]},
        {"label": "영문이름", "value": clean_name(p["name_en"])},
        {"label": "성별", "value": p["gender"]},
        {"label": "생년월일", "value": p["birth_date"]},
        {"label": "국적", "value": country},
        {"label": "발급일", "value": p["issue_date"]},
        {"label": "만료일", "value": p["expiry_date"]},
    ]


def fields_from_sheet(row, columns, skip_keys):
    """visa/pcr/employment용: 시트 컬럼(한글 label)으로 fields[] 구성."""
    out = []
    for c in columns:
        k = c["key"]
        if k in skip_keys:
            continue
        out.append({"label": c["label"] or k, "value": row.get(k, "")})
    return out


# ── 결함 주입 ────────────────────────────────────────────────
# corruption_type 별 변조 함수. 각 함수는 (fields, target_key, ctx) -> 변조된 한글 라벨 반환.
KO_LABEL = {  # snake_case target_field -> 서류 fields의 한글 라벨
    "expiry_date": "만료일",
    "nationality": "국적",
    "birth_date": "생년월일",
    "name_en": "영문이름",
    "passport_no": "여권번호",
    "photo_ref": "사진",
    "company_name": "고용 회사",
    "result": "검사 결과",
    "lab_name": "검사 기관",
}


def set_field(fields, label, value):
    for f in fields:
        if f["label"] == label:
            f["value"] = value
            return True
    return False


def get_field(fields, label):
    for f in fields:
        if f["label"] == label:
            return f["value"]
    return None


def fake_value_for(field_key, fake_pool, *seed_parts):
    cands = [r["fake_value"] for r in fake_pool if r["field"] == field_key]
    if not cands:
        return None
    return seeded_pick(cands, "fake", field_key, *seed_parts)


def expire_date(value):
    """만료일을 과거로 변조."""
    return "2023-01-01"


def apply_defect(doc, fields, corruption_type, target_key, fake_pool, ctx):
    """단일 결함 1개 주입. 변조된 한글 라벨을 반환."""
    label = KO_LABEL.get(target_key, target_key)

    if corruption_type == "EXPIRE":
        # 만료일 과거로
        lbl = "만료일"
        set_field(fields, lbl, expire_date(get_field(fields, lbl)))
        return lbl

    if corruption_type == "ALTER_FIELD":
        if target_key == "nationality":
            # 국적4제한: 위조 국적도 허용 4개국 내에서만(진짜 국적 제외) 결정론적 선택.
            # fake_value_pool 의 비4개국 코드(IRL 등)는 쓰지 않는다 → baked 국적 항상 {KOR,USA,CHN,JPN}.
            true_nat = get_field(fields, "국적")
            cands = [n for n in ("KOR", "USA", "CHN", "JPN") if n != true_nat]
            fv = seeded_pick(cands, "fake", "nationality", *ctx)
            set_field(fields, "국적", fv)
            return "국적"
        if target_key == "birth_date":
            set_field(fields, "생년월일", "1900-01-01")
            return "생년월일"
        if target_key == "name_en":
            cur = get_field(fields, "영문이름") or ""
            set_field(fields, "영문이름", cur + "X")
            return "영문이름"
        set_field(fields, label, "(불일치)")
        return label

    if corruption_type == "MISMATCH_PHOTO":
        doc["spriteRef"] = "photo_mismatch"
        return "사진"

    if corruption_type == "FORGE_NUMBER":
        fv = fake_value_for("passport_no", fake_pool, *ctx) or "##INVALID##"
        set_field(fields, "여권번호", fv)
        return "여권번호"

    if corruption_type == "POSITIVE":
        set_field(fields, "검사 결과", "Positive")
        return "검사 결과"

    if corruption_type == "FORGE_SOURCE":
        if target_key == "lab_name":
            fv = fake_value_for("lab_name", fake_pool, *ctx) or "무허가검진센터"
            set_field(fields, "검사 기관", fv)
            return "검사 기관"
        # company_name (취업증빙)
        fv = fake_value_for("company_name", fake_pool, *ctx) or "(주)유령상사"
        set_field(fields, "고용 회사", fv)
        return "고용 회사"

    if corruption_type == "MISSING":
        # PCR 결과 누락 처리
        set_field(fields, "검사 결과", "(누락)")
        return "검사 결과"

    # NONE / 알 수 없는 타입 → 결함 없음
    return None


# ── 뉴스 claim 도출 (본문 키워드 기반, 결정론적) ─────────────────
# 본문에서 사람·서류와 대조 가능한 단서를 1~2개 구조화. 없으면 빈 배열.
def derive_news_claims(news_id, title, content):
    text = (title or "") + " " + (content or "")
    claims = []

    def add(attr, value, label):
        # 본문 도출 단서는 스캔 트리거가 아님 → unlocksScan="".
        claims.append({"attr": attr, "value": value, "label": label, "unlocksScan": ""})

    # 위조 여권 적발 -> 여권번호 진위 주의
    if "위조" in text and "여권" in text:
        add("passport_no", "", "위조 여권 주의")
    # 분실/도용 여권 -> 사진(본인 일치) 대조 강조
    if "도용" in text or "분실" in text:
        add("face", "", "사진 본인 일치 확인")
    # 만료/유효기간 -> 만료일 대조
    if "만료" in text or "유효기간" in text:
        add("expiry_date", "", "여권 유효기간 확인")
    # 밀수/마약/폭발물 등 -> X-ray 적발물(contraband) 대조 강조
    if any(kw in text for kw in ("밀수", "마약", "폭발물", "위험물", "반입 금지", "은닉")):
        add("contraband", "", "위험물 반입 주의")
    # 단체 관광객 -> 대조 단서 없음(연출용) → 비움
    return claims


# ── 스캔 잠금 해제 트리거 단서 (뉴스/규정 합성, 2026-06) ──────────────
# 보조검사 버튼은 기본 잠금. 뉴스/규정 단서(claim.unlocksScan)가 손님 정보와
# 대조되어 Match/Related가 나오면 해당 스캔이 잠금 해제된다.
# 여기서는 그 "트리거 단서 데이터"를 합성한다(news 시트 원본은 못 늘리므로 day별 주입).
#
# 각 트리거 claim 의 attr/value 는 그 손님이 day JSON 에서 실제로 가진 여권 필드와 일치해야
# 대조 시 Match 가 보장된다(passport 필드: nationality=국가코드, passport_no=여권번호).
# 같은 날 같은 국적 손님이 여럿이면 nationality 로는 그 손님만 못 가리므로 passport_no 사용.
#
# 형식: (day, customer_id, attr, value, [unlocksScan...], label)
#   attr/value  : 손님 식별(여권 필드와 동일). nationality=코드, passport_no=번호.
#   unlocksScan : 이 손님이 보유한 검사 종류(들). 손님9는 xray+fingerprint 둘 다.
SCAN_TRIGGERS = [
    # day11 손님10(지문) — KOR 국적이 day11에 5명 → passport_no 로 그 손님만 매칭
    (11, "10", "passport_no", "KO1011170", ["fingerprint"]),
    # day12 손님8(지문) — USA 가 day12 유일 → nationality 코드로 매칭 (여권번호는 변조될 수 있어 비사용)
    (12, "8", "nationality", "USA", ["fingerprint"]),
    # day13 손님11(xray) — 재배정 JPN. day13에 JPN이 11/26(기존 일본) 둘 → passport_no 유일키.
    #   손님11 여권번호는 정상(JP1012287, 국적/만료일 변조만) → 안정 식별 가능.
    (13, "11", "passport_no", "JP1012287", ["xray"]),
    # day14 손님9(xray+지문) — KOR 가 day14에 3명 → passport_no, 스캔 2종 각각 트리거
    (14, "9", "passport_no", "KO1010053", ["xray", "fingerprint"]),
    # day14 손님34(xray) — 재배정 JPN. 손님37을 USA로 옮겨 day14 JPN 유일 → nationality 코드.
    #   (손님34 여권번호는 변조될 수 있어 nationality 사용)
    (14, "34", "nationality", "JPN", ["xray"]),
]

# 스캔 종류별 테마 라벨(label). 값에 식별값(국적코드/여권번호)을 끼워 연출.
SCAN_TRIGGER_LABEL = {
    "xray": "폭발물/밀수 경보: {ident}발 위험물 주의",
    "fingerprint": "위조신원/수배자 입국 경보: {ident} 대상자 주의",
}
# 합성 뉴스 본문 템플릿(연출용, 대조는 claim 으로).
SCAN_TRIGGER_NEWS = {
    "xray": ("[속보] 위험물 반입 경보",
             "국제 공조 수사 결과 {ident} 경유 입국자 중 폭발물·밀수품 은닉 사례가 다수 적발되었습니다. "
             "해당 대상과 정보가 일치하면 X-ray 정밀 검사를 시행하십시오."),
    "fingerprint": ("[속보] 위조신원·수배자 입국 경보",
                    "위조 신원으로 입국을 시도한 국제 수배자 정보가 공유되었습니다. {ident} 관련 대상은 "
                    "지문 대조로 신원 진위를 반드시 확인하십시오."),
}
# day별 합성 뉴스 ID 시작값(원본 news_id와 충돌 방지용 큰 오프셋, 결정론적).
SCAN_TRIGGER_NEWS_ID_BASE = 900000


def build_scan_trigger_news(day):
    """해당 날짜의 스캔 잠금 해제 트리거 뉴스(claim 포함)를 합성 생성. 없으면 빈 리스트.
    idempotent: 같은 SCAN_TRIGGERS 입력이면 같은 결과(news_id/순서 고정)."""
    out = []
    triggers = [t for t in SCAN_TRIGGERS if t[0] == day]
    for idx, (d, cid, attr, value, scans) in enumerate(triggers):
        for sidx, scan in enumerate(scans):
            ident = value  # 국적코드 또는 여권번호를 연출 문구에 노출
            title, content = SCAN_TRIGGER_NEWS[scan]
            news_id = SCAN_TRIGGER_NEWS_ID_BASE + d * 1000 + idx * 10 + sidx
            out.append({
                "newsId": news_id,
                "title": title,
                "content": content.format(ident=ident),
                "iconRef": "",
                "claims": [{
                    "attr": attr,
                    "value": value,
                    "label": SCAN_TRIGGER_LABEL[scan].format(ident=ident),
                    "unlocksScan": scan,
                }],
            })
    return out


# ── 보조검사(xray/fingerprint) -> ScanData (customer_id로 연결) ────────
def build_xray_scan(row):
    """xray row -> ScanData(dict). claim.attr=contraband(적발물)."""
    detected = row.get("detected_item") or ""
    return {
        "type": "xray",
        "result": row.get("result") or "",
        "detail": detected,                       # detected_item
        "extra": row.get("hidden_location") or "",  # hidden_location
        "claim": {"attr": "contraband", "value": detected, "label": "X-ray 적발물", "unlocksScan": ""},
    }


def build_fingerprint_scan(row):
    """fingerprint row -> ScanData(dict). claim.attr=name(matched_person -> 여권 이름과 대조)."""
    matched = row.get("matched_person") or ""
    return {
        "type": "fingerprint",
        "result": row.get("result") or "",
        "detail": row.get("match_status") or "",  # match_status
        "extra": matched,                          # matched_person
        "claim": {"attr": "name", "value": matched, "label": "지문 대조 신원", "unlocksScan": ""},
    }


# ── document_requirement 평가 ────────────────────────────────
def required_documents(day, character_type, passport_nat_code):
    """그날 손님에게 요구되는 서류 타입 목록."""
    docs = ["여권"]  # 전일 필수
    if 3 <= day <= 14 and passport_nat_code != "KOR":
        docs.append("비자")
    if 5 <= day <= 7 and character_type == PCR_TYPE:
        docs.append("PCR검사서")
    if 8 <= day <= 14 and character_type in EMPLOYMENT_TYPES:
        docs.append("취업증빙")
    return docs


# ── 대사(최소 플레이) ────────────────────────────────────────
def is_foreigner(nat_code):
    return nat_code != "KOR"


def entry_lines(nat_code):
    if is_foreigner(nat_code):
        return [
            {"order": 1, "speaker": "캐릭터", "text": f"Hello! 안녕하세요. {TODO}"},
            {"order": 2, "speaker": "캐릭터", "text": f"여기 서류 드릴게요. {TODO}"},
        ]
    return [
        {"order": 1, "speaker": "캐릭터", "text": f"안녕하세요, 서류 드릴게요. {TODO}"},
        {"order": 2, "speaker": "캐릭터", "text": f"확인 부탁드립니다. {TODO}"},
    ]


def make_dialogue_cases(nat_code, violation_label, violation_attr=""):
    """손님마다 7케이스 보장(입장/정상승인/정상거절/잘못허가/잘못거절1·2·3).

    violation_attr가 있으면 '정상 거절' 케이스의 심사관 지적 라인에 claim을 달아
    뉴스/서류와 같은 속성 키로 대조 가능하게 한다(placeholder 대사 한계 내 최선).
    """
    cases = []
    # 1. 입장
    cases.append({"caseType": "입장", "gameResult": "-", "rejectCount": 0, "lines": entry_lines(nat_code)})
    # 2. 정상 승인
    cases.append({
        "caseType": "일반 심사", "gameResult": "정상 승인", "rejectCount": 0,
        "lines": [
            {"order": 1, "speaker": "심사관", "text": f"확인 완료되었습니다. 지나가셔도 됩니다. {TODO}"},
            {"order": 2, "speaker": "캐릭터", "text": f"감사합니다! {TODO}"},
        ],
    })
    # 3. 정상 거절 — 심사관이 지적하는 위반 항목에 claim(속성 키) 부여
    vf = violation_label or "서류"
    reject_inspector_line = {
        "order": 1, "speaker": "심사관",
        "text": f"{vf} 항목에 문제가 있습니다. 입국은 어렵습니다. {TODO}",
    }
    if violation_attr:
        reject_inspector_line["claim"] = {
            "attr": violation_attr, "value": "", "label": f"{vf} 불일치", "unlocksScan": "",
        }
    cases.append({
        "caseType": "일반 심사", "gameResult": "정상 거절", "rejectCount": 0,
        "lines": [
            reject_inspector_line,
            {"order": 2, "speaker": "캐릭터", "text": f"앗, 제가 확인을 못 했네요. 죄송합니다. {TODO}"},
        ],
    })
    # 4. 잘못 허가
    cases.append({
        "caseType": "일반 심사", "gameResult": "잘못 허가", "rejectCount": 0,
        "lines": [
            {"order": 1, "speaker": "심사관", "text": f"확인 완료되었습니다. 지나가셔도 됩니다. {TODO}"},
            {"order": 2, "speaker": "캐릭터", "text": f"감사합니다. 안녕히 계세요! {TODO}"},
        ],
    })
    # 5/6/7. 잘못 거절 1·2·3 (항의 강도 증가)
    protest = {
        1: "어..? 어디가 잘못된 부분이 있나요?",
        2: "분명 제대로 다 챙겼어요. 다시 한 번 확인해 주세요.",
        3: "아니, 뭐 때문에 입국이 안 된다는 거예요? 다시 제대로 확인하세요!",
    }
    resolve_line = {
        1: "다시 확인해보니 서류가 맞네요. 지나가시면 됩니다.",
        2: "다시 확인했는데 서류가 모두 맞네요. 죄송합니다. 지나가셔도 됩니다.",
        3: "고객님 서류 확인되셔서 바로 지나가시면 될 것 같습니다. 정말 죄송합니다.",
    }
    for rc in (1, 2, 3):
        cases.append({
            "caseType": "일반 심사", "gameResult": "잘못 거절", "rejectCount": rc,
            "lines": [
                {"order": 1, "speaker": "심사관", "text": f"죄송하지만 입국은 어렵습니다. {TODO}"},
                {"order": 2, "speaker": "캐릭터", "text": f"{protest[rc]} {TODO}"},
                {"order": 3, "speaker": "심사관", "text": f"{resolve_line[rc]} {TODO}"},
                {"order": 4, "speaker": "캐릭터", "text": f"네, 알겠습니다. {TODO}"},
            ],
        })
    return cases


# ── 메인 빌드 ────────────────────────────────────────────────
def build():
    sheets = load_source()

    customers = {r["customer_id"]: r for r in rows(sheets["customer"])}
    passports = {r["customer_id"]: r for r in rows(sheets["passport"])}
    visas = {r["customer_id"]: r for r in rows(sheets["visa"])}
    pcrs = {r["customer_id"]: r for r in rows(sheets["pcr_test"])}
    emps = {r["customer_id"]: r for r in rows(sheets["employment_cert"])}

    visa_cols = sheets["visa"]["columns"]
    pcr_cols = sheets["pcr_test"]["columns"]
    emp_cols = sheets["employment_cert"]["columns"]

    fake_pool = rows(sheets["fake_value_pool"])

    # defect_rule: character_type -> [rules] (외국인 관광객처럼 상황별 복수 규칙 존재)
    defect_rules = {}
    for r in rows(sheets["defect_rule"]):
        defect_rules.setdefault(r["character_type"], []).append(r)

    rule_book = rows(sheets["rule_book"])
    news = rows(sheets["news"])

    # 보조검사: customer_id -> ScanData(dict). 한 손님에 동종 검사 여러 건이면 마지막 행 채택.
    xray_scans = {r["customer_id"]: build_xray_scan(r) for r in rows(sheets["xray"])}
    fingerprint_scans = {r["customer_id"]: build_fingerprint_scan(r)
                         for r in rows(sheets["fingerprint"])}

    # day_schedule grouped by day
    schedule = {}
    for r in rows(sheets["day_schedule"]):
        schedule.setdefault(int(r["day"]), []).append(r)

    report = {}

    for day in range(2, 15):
        slots = sorted(schedule.get(day, []), key=lambda r: int(r["slot"]))
        day_customers = []
        stats = {"normal": 0, "abnormal": 0, "defects": {}}

        for s in slots:
            cid = s["customer_id"]  # 문자열 키(source 원본)
            slot = int(s["slot"])
            cust = customers[cid]
            pp = passports[cid]
            nat_code = pp["nationality"]
            ctype = cust["character_type"]

            is_normal = resolve_normal(s["valid_chance"], day, slot, cid)
            req_docs = required_documents(day, ctype, nat_code)

            documents = []
            # 여권
            pdoc = {
                "documentType": "여권", "variant": "정상", "violationField": "없음",
                "spriteRef": pp["photo_ref"], "country": nat_code,
                "fields": tag_fields(passport_fields(pp)),
            }
            documents.append(pdoc)
            # 비자
            if "비자" in req_docs and cid in visas:
                documents.append({
                    "documentType": "비자", "variant": "정상", "violationField": "없음",
                    "spriteRef": "", "country": "",
                    "fields": tag_fields(fields_from_sheet(visas[cid], visa_cols, {"visa_id", "customer_id"})),
                })
            # PCR검사서
            if "PCR검사서" in req_docs and cid in pcrs:
                documents.append({
                    "documentType": "PCR검사서", "variant": "정상", "violationField": "없음",
                    "spriteRef": "", "country": "",
                    "fields": tag_fields(fields_from_sheet(pcrs[cid], pcr_cols, {"pcr_id", "customer_id"})),
                })
            # 취업증빙
            if "취업증빙" in req_docs and cid in emps:
                documents.append({
                    "documentType": "취업증빙", "variant": "정상", "violationField": "없음",
                    "spriteRef": "", "country": "",
                    "fields": tag_fields(fields_from_sheet(emps[cid], emp_cols, {"employment_id", "customer_id"})),
                })

            violation_label = None
            applied_ct = ""   # 실제 적용된 corruption_type (defect_variant 도출용)
            applied_tf = ""   # 실제 적용된 target_field

            if is_normal:
                stats["normal"] += 1
            else:
                stats["abnormal"] += 1
                # 상황별 규칙 선택: 결함서류가 요구 서류에 포함된 규칙 우선
                doc_name_map = {"여권": "여권", "비자": "비자", "pcr_test": "PCR검사서",
                                "employment_cert": "취업증빙"}
                candidate_rules = [cr for cr in defect_rules.get(ctype, [])
                                   if cr.get("corruption_type", "NONE") != "NONE"]
                # 결함서류가 요구 서류에 포함된 규칙 중, 비여권(특수 서류) 우선 선택.
                rule = None
                applicable = [cr for cr in candidate_rules
                              if doc_name_map.get(cr["defect_document"], cr["defect_document"]) in req_docs]
                non_passport = [cr for cr in applicable if cr["defect_document"] != "여권"]
                if non_passport:
                    rule = seeded_pick(non_passport, "rulesel", day, slot, cid)
                elif applicable:
                    rule = seeded_pick(applicable, "rulesel", day, slot, cid)
                if rule is None and candidate_rules:
                    # 폴백: corruption 있는 첫 규칙
                    rule = candidate_rules[0]
                applied = False
                if rule and rule.get("corruption_type", "NONE") != "NONE":
                    # 복수 corruption_type / target_field -> 시드로 1개 선택(쌍으로)
                    ct_opts = [x.strip() for x in rule["corruption_type"].replace("|", "/").split("/")]
                    tf_opts = [x.strip() for x in rule["target_field"].replace("|", "/").split("/")]
                    # 짝 맞추기: 같은 인덱스로 묶되 길이 다르면 잘라서 안전하게
                    pairs = []
                    n = max(len(ct_opts), len(tf_opts))
                    for i in range(n):
                        ct = ct_opts[i] if i < len(ct_opts) else ct_opts[-1]
                        tf = tf_opts[i] if i < len(tf_opts) else tf_opts[-1]
                        pairs.append((ct, tf))
                    ct, tf = seeded_pick(pairs, "defect", day, slot, cid)

                    # 결함 대상 서류 찾기
                    defect_doc_name = rule["defect_document"]
                    doc_map = {"여권": "여권", "비자": "비자", "pcr_test": "PCR검사서",
                               "employment_cert": "취업증빙"}
                    target_doc_type = doc_map.get(defect_doc_name, defect_doc_name)
                    target_doc = next((d for d in documents if d["documentType"] == target_doc_type), None)

                    if target_doc is not None:
                        vlabel = apply_defect(target_doc, target_doc["fields"], ct, tf, fake_pool,
                                              (day, slot, cid))
                        if vlabel:
                            target_doc["variant"] = "비정상"
                            target_doc["violationField"] = vlabel
                            violation_label = vlabel
                            applied = True
                            applied_ct = ct
                            applied_tf = tf
                            stats["defects"][rule["rule_id"]] = stats["defects"].get(rule["rule_id"], 0) + 1
                if not applied:
                    # 결함 주입 실패(요구 서류에 결함서류가 없거나 NONE) → 여권 만료로 폴백
                    set_field(pdoc["fields"], "만료일", expire_date(None))
                    pdoc["variant"] = "비정상"
                    pdoc["violationField"] = "만료일"
                    violation_label = "만료일"
                    applied_ct = "EXPIRE"
                    applied_tf = "expiry_date"
                    stats["defects"]["fallback"] = stats["defects"].get("fallback", 0) + 1

            # defect_variant 도출(baked 로 가능한 변이만; 그 외 ""→게임플레이 결정)
            defect_variant = resolve_defect_variant(ctype, is_normal, applied_ct, applied_tf)
            if defect_variant:
                stats.setdefault("variants", {})
                stats["variants"][defect_variant] = stats["variants"].get(defect_variant, 0) + 1

            day_customers.append({
                "customerId": int(cid),
                "slot": slot,
                "nameKr": cust["name_kr"],
                "nameEn": clean_name(cust["name_en"]),
                "nationality": cust["nationality"],
                "gender": cust["gender"],
                "birthDate": cust["birth_date"],
                "age": int(cust["age"]),
                "spriteRef": cust["sprite_ref"],
                "characterType": ctype,
                "defectVariant": defect_variant,   # baked 변이 키(없으면 ""). 게임플레이가 branch 조인에 사용.
                "correctResult": "정상 승인" if is_normal else "정상 거절",
                "documents": documents,
                "dialogueCases": make_dialogue_cases(
                    nat_code, violation_label, attr_for_label(violation_label)),
                "xray": xray_scans.get(cid),                # 없으면 None -> JSON null
                "fingerprint": fingerprint_scans.get(cid),  # 없으면 None -> JSON null
            })

        day_rules = [
            {"ruleId": int(r["rule_id"]), "title": r["rule_title"],
             "content": r["rule_content"], "relatedField": r["related_field"],
             "attr": attr_for_label(r["related_field"])}
            for r in rule_book if int(r["day"]) == day
        ]
        day_news = [
            {"newsId": int(r["news_id"]), "title": r["news_title"],
             "content": r["news_content"], "iconRef": r["icon_ref"],
             "claims": derive_news_claims(r["news_id"], r["news_title"], r["news_content"])}
            for r in news if int(r["day"]) == day
        ]
        # 스캔 잠금 해제 트리거 뉴스(합성) 주입 — idempotent, 11~14일차에만 존재
        day_news.extend(build_scan_trigger_news(day))

        data = {"day": day, "customers": day_customers, "rules": day_rules, "news": day_news}

        out_path = os.path.join(OUT_DIR, f"day{day}.json")
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

        report[day] = stats

    return report


def write_report(report):
    lines = ["# build_days 리포트", ""]
    for day in sorted(report):
        st = report[day]
        lines.append(f"## day{day}: 손님 7 (정상 {st['normal']} / 비정상 {st['abnormal']})")
        if st["defects"]:
            lines.append("  결함 분포: " + json.dumps(st["defects"], ensure_ascii=False))
        if st.get("variants"):
            lines.append("  변이 분포: " + json.dumps(st["variants"], ensure_ascii=False))
    with open(os.path.join(os.path.dirname(__file__), "build_days_report.txt"),
              "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


if __name__ == "__main__":
    rep = build()
    write_report(rep)
    # stdout 한글 깨짐 방지 위해 숫자만 출력
    total = sum(v["normal"] + v["abnormal"] for v in rep.values())
    print(f"days generated: {len(rep)} customers total: {total}")
