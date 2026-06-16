# -*- coding: utf-8 -*-
"""
build_days.py — GameData.source.json -> Assets/Resources/GameData/day1.json ... day14.json

「여권 주세요」 1~14일차 케이스 데이터 생성기.
- 1일차(day1.json)도 source 일정에서 생성한다(손님 구성은 수작업본과 동일, 대사는 스크립트 주입).
- 진실 서류만 source에 있고, 비정상 슬롯이면 defect_rule로 결함 1개를 결정론적으로 주입한다.
- 대사는 메뉴 최소 플레이용 짧은 공통 라인만 생성하고 [TODO 대사] 표식을 남긴다.

스키마 대상: Assets/Scripts/Inspection/DataModels.cs 의 Day1Data 계열.
시드: 20260602 (고정, 슬롯별 결정론적 resolve)

실행:
    python Tools/DataImport/build_days.py
산출:
    Assets/Resources/GameData/day1.json ... day14.json (14개)
"""
import json
import os
import hashlib

# 시나리오 대사 주입기(branch_dialogue.json 조인). 케이스 구조 불변, text 만 교체.
from branch_dialogue_map import (
    load_branch, index_branch, fill_customer_dialogue, load_guided_block, TYPE_MAP,
)
# 캐릭터별 손글 대사 오버라이드(character_dialogue.json 조인). 유형 레이어 위에 덮어씀.
# 손님 라인 text 만 customerId별 캐논 대사로 교체(구조 불변). 심사관 라인은 보존.
from character_dialogue_map import load_char_dialogue, apply_character_override
# branch 에 없는 슬롯(유형×결과×구조)을 day1 톤 작성본으로 채우는 폴백 테이블.
# branch 가 못 채운 잔여 [TODO 대사] 만 채운다(이미 채운 라인은 불변).
from authored_lines import fill_authored
# 대사 정화(post-processing) — 파이프라인 마지막 손질(단일 정화 지점, 6장).
#   (1) 액션/시스템/몽타주 narration 라인 제거 (2) 외국인 대사 {모국어}({한국어}) 통일
#   (3) 윤서린(범죄자(성형수술)) 거절 반응 다양화 + 마스크 흐름 정리.
from dialogue_polish import polish_customer

# ── 경로 ─────────────────────────────────────────────────────
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
# 출력 디렉터리. 기본은 실제 게임 데이터 위치. 검증용으로 BUILD_DAYS_OUT_DIR 환경변수로
# 스테이징 경로를 지정하면 실제 데이터를 건드리지 않고 재생성해 diff할 수 있다.
OUT_DIR = os.environ.get("BUILD_DAYS_OUT_DIR") or os.path.join(ROOT, "Assets", "Resources", "GameData")

SEED = 20260602
TODO = "[TODO 대사]"

# ── 속성 키 어휘 & 한글 라벨 -> 속성 키 매핑 (단일 진실, 6장: 변경 흡수 한 곳) ─────
# 표시는 한글 라벨, 비교는 속성 키. 새 컬럼/라벨이 와도 여기만 고친다.
# 공백 변형("여권번호"/"여권 번호") 모두 흡수하기 위해 정규화 후 조회한다.
ATTR_VOCAB = {
    "name", "nationality", "birth_date", "gender", "face", "passport_no",
    "issue_date", "expiry_date", "visa_type", "visa_no", "pcr_result",
    "valid_until", "lab_name", "test_no", "company_name", "job_title",
    "cert_no", "hire_date", "entry_type", "purpose", "memo", "contraband",
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
    "이름": "name",          # 취업증빙 이름(여권 영문이름과 동일 = 정상 일치 대조)
    "증빙번호": "cert_no",
    "고용회사": "company_name",
    "직종": "job_title",
    "입사일": "hire_date",    # 취업증빙 신규(발급일 이전이면 정상, 이후면 논리오류)
    # rule_book related_field 전용(중복 키는 위와 동일)
    "여권": "passport_no",
    "비자": "visa_type",
    "위험물": "contraband",   # 테러 발생 지시 규정 ↔ X-ray 적발물 대조
    "적발물": "contraband",
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

# 지문(옵션B)으로 본인/도용을 가리는 캐릭터 유형. 이 손님들은 서류를 변조하지 않고,
# 본인/도용 차이는 오직 지문 DB record(이름/생일/국적)에만 나타난다.
#   - 성형 수술 고객(mode=성형): is_normal 에 따라 본인(승인 정답)/도용(거절 정답).
#   - 범죄자(성형수술)(mode=수배자): 양쪽 모두 항상 수배(거절 정답, is_normal 무시).
# 주의: 문자열은 customer 시트 character_type 실값과 정확히 일치해야 한다(성형 수술 고객).
FINGERPRINT_DISCRIMINATED_TYPES = {"성형 수술 고객", "범죄자(성형수술)"}
PLASTIC_WANTED_TYPE = "범죄자(성형수술)"   # 수배자: 지문 record 가 항상 alt 신원 + 수배번호
PCR_RULE_KEY = "PCR검사"   # defect_rule/character_score 의 PCR 보편 키(손님 종류 아님 — 5~7일 전원 공통 서류 검사)
TOURIST_TYPE = "외국인 관광객"


# ── defect_variant 결정 (BRANCH_CATALOG 어휘 정합, baked 데이터로 도출 가능한 변이만) ──
# 같은 branch_key 라도 변이별 점수가 다른 캐릭터는 변이를 day JSON 에 기록해야
# 게임플레이 BranchKeyResolver 가 character_score/payout 을 정확히 조인한다.
# baked 데이터로 결정 가능한 변이만 채우고(관광객·검역 대상자 PCR),
# 그 외(성형/테러/연예인/현자 등 런타임 상태머신 결과)는 "" 로 둔다(게임플레이가 결정).
#
# 어휘 출처: Tools/DataImport/BRANCH_CATALOG.md (character_score.defect_variant 와 1:1).
def resolve_defect_variant(character_type, is_normal, corruption_type, target_field, defect_document=""):
    """주입된 결함(corruption_type/target_field/defect_document)으로부터 defect_variant 키를 도출.
    도출 불가/대상 아님이면 "" 반환. corruption_type 은 실제 적용된 단일 타입."""
    ct = (corruption_type or "").strip().upper()
    tf = (target_field or "").strip()
    dd = (defect_document or "").strip()

    # PCR 검사서(5~7일 전원 공통, 손님 종류 무관): 결함 corruption 으로 1-B/1-C/1-D 구분.
    #  결함이 PCR 서류에 들어갔는지(defect_document)로 판별 — 옛 '검역 대상자' 타입에 의존하지 않는다.
    if dd == "pcr_test":
        if ct == "POSITIVE":          # 양성 = 백신 미접종/감염
            return "1-C 백신X"
        # (검사결과 누락 결함은 시나리오에서 제거됨 — MISSING 미사용)
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


# 특정 손님의 결함 종류를 설계 의도대로 고정(복수 corruption_type 규칙에서 시드 우연 회피).
# 키=customer_id, 값=(corruption_type, target_field). seeded_pick 결과가 규칙의 유효 쌍이 아니면 무시.
# [레거시] 코드 레벨 결함 고정. 이제 결함 배정은 엑셀 'defect_assign' 시트(data-driven)로 관리한다.
#   - 자오레이(35)=회사위조, PCR 전염병 환자(양성/누락/기관위조) 등은 모두 defect_assign 표에 있음.
#   - 표에 없는 손님만 이 dict로 폴백 고정 가능(현재 비어 있음).
FORCED_DEFECT = {}


# ── 사진(얼굴) 불일치 결함 강제 (외국인 관광객, 손글 스크립트 설계) ─────────────
# 손글 스크립트(방문객_스크립트_일자별_260609_2)에서 '서류: 불량(여권 사진 불일치)'로
# 설계된 슬롯 중 '외국인 관광객' 손님(day_schedule (day,slot) 조인 결과)을 사진 결함으로 강제한다.
# - 이 손님들은 vc=0(또는 비정상으로 굴렀을 때)이면 여권 photo_ref 를 '다른 인물 얼굴'로 교체 →
#   얼굴 대조 시 customer.spriteRef ≠ 여권.spriteRef 로 '불일치' 적발이 정답(=거절)이 된다.
# - 기존 결함(국적/만료일 등)을 '추가'가 아니라 '대체'한다(비정상 슬롯 수·correctResult 불변).
# - 성형 수술 고객/범죄자(성형수술)는 지문(옵션B)으로 본인/도용을 가리는 설계라 제외한다
#   (여권 사진은 일치시켜야 옵션B 가 성립 — 사진을 깨면 지문 메커니즘이 무력화됨).
# - day1(cid 25 윌리엄·7 왕웨이)은 수작업본이라 build_days 가 건드리지 않는다(day1.json 직접 수정).
#   디코이 현실화(260611): 윌리엄→제임스 밀러(USA여31), 왕웨이→박지훈(KOR남36)로 교정
#   (옛 한지원=특수연예인★/사토하루키=테러범은 외형이 튀어 부적합 → 같은 성별·나이대·국적권으로 교체).
# 키는 source 원본 customer_id 문자열. (day,slot) 은 주석 참고용.
FORCED_PHOTO_DEFECT_TOURIST = {
    # [2026-06-12] day2~7 외국인은 결함배분표에 맞춰 사진 외 결함으로 재정합 → 사진 강제 제외.
    #   제외: 38(데이비드 여권번호) 26(다나카하루토 만료일) 27(장웨이 비자국적) 34(야마모토 비자만료)
    #         43(토머스 비자만료) 46(리강 방문목적) 54(다니엘 비자이름) 58(왕팡 방문목적)
    #   런타임 day*.json은 이미 직접 패치됨. 이 제외는 리빌드 시 사진으로 되돌림 방지. 상세: Tools/_design_reconcile_plan.md
    # [2026-06-12] 사토 유토(55)는 사진→PCR 이름 불일치로 전환(결함배분표·day6.json·대사 동기화). 사진 강제 제외.
    # [2026-06-13] day8~14 외국인도 결함배분표(권위)에 맞춰 사진 외 결함으로 재정합 → 사진 강제 제외.
    #   런타임 day*.json 은 _patch_day{8..14}_defects.py + _patch_jangmin_employment.py 로 직접 패치됨.
    #   이 제외는 리빌드 시 사진으로 되돌림 방지(상세: Tools/_design_reconcile_plan.md "8~14일차 정합").
    #   제외(=사진 아님): 62 장 민(재직증명서 이름) 63 매튜 앤더슨(비자 이름) 67 류 옌(비자종류거짓)
    #     69 크리스 토머스(비자 번호) 73 스즈키 소라(비자 번호) 3 첸 웨이(비자종류거짓) 77 자오 친(비자 국적)
    #     80 앤드류 화이트(여권 만료) 85 황 레이(비자종류거짓) 87 우 팅(비자 이름) 89 다카하시 리쿠(비자 국적)
    #     92 쉬 펑(비자종류거짓) 94 선 메이(비자 번호)
    "97",  # day14 slot6 에밀리 클락(여)  ← 결함배분표 14DAY=여권 사진(유지). 유일하게 사진 강제 유지.
}


# ── 얼굴 디코이(사진 불일치 위장) — 현실적 선택 ─────────────────────────
# 사진 불일치 결함은 외국인 손님 여권 사진을 '다른 인물 얼굴'(디코이)로 바꾼다.
# 위장이 그럴듯하려면 디코이가 **같은 성별 + 나이대 근접 + 가능하면 같은 국적권**이어야 한다.
# 한눈에 너무 다른 인물(노인/아동/특수★/범죄·테러 등 스토리상 튀는 얼굴)은 디코이로 쓰지 않는다.
#
# 디코이 후보 메타데이터는 **customer 시트(진실 소스)에서 자동 도출**한다(6장: 변경 흡수 한 곳).
#  - 키 = sprite_ref(= Resources/Characters/<이름>.png 실존 에셋 이름).
#  - 한 sprite_ref 를 여러 클론이 공유하므로 '원본 소유자'(name_kr==sprite_ref) 행의 속성을 대표값으로 쓴다.
#  - 에셋이 없는 sprite_ref 나 customer 에 안 쓰이는 고아 PNG(이하민/장지원 등)는 메타가 없어 후보에서 자동 제외.
#
# 제외 규칙(외형이 튀어 위장에 부적합):
#  - 특수 유형(특수(현자/연예인/정치인)★)
#  - 범죄자/테러범(스토리 식별 얼굴 — 남의 여권에 붙으면 '그 수배자'로 보여 위장이 깨짐)
#  - 성인 범위를 벗어난 나이(노인/아동): age < 20 또는 age > 55
DECOY_AGE_WINDOW = 12          # 나이대 근접 허용 폭(±세). 이 범위 내를 '비슷한 나이'로 본다.
DECOY_MIN_AGE = 20
DECOY_MAX_AGE = 55

# 국적권(region): 동아시아(KOR/CHN/JPN) vs 그 외(서양 등). 같은 권역끼리 우선 매칭.
EAST_ASIA = {"KOR", "CHN", "JPN"}


def _nat_region(nat_value):
    """'대한민국(KOR)' / 'KOR' -> 'EA'(동아시아) | 'W'(그 외)."""
    code = ""
    s = str(nat_value or "")
    l, r = s.rfind("("), s.rfind(")")
    if 0 <= l < r:
        code = s[l + 1:r].strip()
    else:
        code = s.strip()
    return "EA" if code in EAST_ASIA else "W"


def _decoy_eligible(character_type, age):
    """디코이 후보 적격 여부. 특수/범죄/테러·노인/아동 제외."""
    ct = character_type or ""
    if "★" in ct:                         # 특수(현자/연예인/정치인)
        return False
    if "범죄자" in ct or "테러" in ct:      # 스토리 식별 얼굴
        return False
    try:
        a = int(age)
    except (TypeError, ValueError):
        return False
    return DECOY_MIN_AGE <= a <= DECOY_MAX_AGE


def build_face_meta(customer_rows):
    """customer 시트 rows -> { sprite_ref: {gender, age, region, eligible} }.
    원본 소유자(name_kr==sprite_ref) 행을 대표값으로 사용(클론은 같은 속성 복제이므로 동일)."""
    by_sprite = {}
    for r in customer_rows:
        by_sprite.setdefault(r["sprite_ref"], []).append(r)
    meta = {}
    for sprite, rs in by_sprite.items():
        owner = next((x for x in rs if x.get("name_kr") == sprite), rs[0])
        try:
            age = int(owner.get("age", 0))
        except (TypeError, ValueError):
            age = 0
        meta[sprite] = {
            "gender": owner.get("gender", ""),
            "age": age,
            "region": _nat_region(owner.get("nationality", "")),
            "eligible": _decoy_eligible(owner.get("character_type", ""), age),
        }
    return meta


# 모듈 전역 캐시. build() 에서 source 로드 후 채워진다(테스트가 직접 호출 시에도 안전하게 lazy 로드).
FACE_META = {}


def _ensure_face_meta():
    global FACE_META
    if not FACE_META:
        FACE_META = build_face_meta(rows(load_source()["customer"]))
    return FACE_META


def pick_decoy_face(self_sprite, self_gender, self_age, self_nat, *ctx):
    """사진 불일치 디코이를 '같은 성별 + 나이 근접 + 같은 국적권' 우선으로 결정론 선택.

    절차(본인/부적격/에셋없음 제외 후):
      1) 같은 성별 + 같은 권역 + 나이 ±DECOY_AGE_WINDOW   (가장 그럴듯)
      2) 같은 성별 + 나이 ±DECOY_AGE_WINDOW (권역 무관)
      3) 같은 성별 + 같은 권역 (나이 무관)
      4) 같은 성별 내 나이 최근접 1명들(동률 풀)
    각 단계에서 후보가 있으면 그 단계 풀에서 seeded_pick(결정론). 같은 성별 후보가 전혀 없으면
    적격 전체에서 나이 최근접 폴백. 그래도 없으면 'photo_mismatch'.
    """
    meta = _ensure_face_meta()
    try:
        my_age = int(self_age)
    except (TypeError, ValueError):
        my_age = 0
    my_region = _nat_region(self_nat)

    # 적격 + 본인 제외 후보. (sprite, gender, age, region)
    cands = [
        (sp, m["gender"], m["age"], m["region"])
        for sp, m in meta.items()
        if m["eligible"] and sp != self_sprite
    ]
    if not cands:
        return "photo_mismatch"

    same_gender = [c for c in cands if c[1] == self_gender]

    def near(c):
        return abs(c[2] - my_age) <= DECOY_AGE_WINDOW

    # 1) 같은 성별 + 같은 권역 + 나이 근접
    tier = [c for c in same_gender if c[3] == my_region and near(c)]
    # 2) 같은 성별 + 나이 근접(권역 무관)
    if not tier:
        tier = [c for c in same_gender if near(c)]
    # 3) 같은 성별 + 같은 권역(나이 무관)
    if not tier:
        tier = [c for c in same_gender if c[3] == my_region]
    # 4) 같은 성별 내 나이 최근접(동률 풀)
    if not tier and same_gender:
        best = min(abs(c[2] - my_age) for c in same_gender)
        tier = [c for c in same_gender if abs(c[2] - my_age) == best]
    # 폴백: 같은 성별 후보가 전무 → 적격 전체에서 나이 최근접
    if not tier:
        best = min(abs(c[2] - my_age) for c in cands)
        tier = [c for c in cands if abs(c[2] - my_age) == best]

    names = sorted(c[0] for c in tier)   # 정렬로 결정론적 인덱싱 안정화
    return seeded_pick(names, "decoyface", *ctx)


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
    """visa/pcr용: 시트 컬럼(한글 label)으로 fields[] 구성."""
    out = []
    for c in columns:
        k = c["key"]
        if k in skip_keys:
            continue
        out.append({"label": c["label"] or k, "value": row.get(k, "")})
    return out


def employment_fields(emp_row, passport_row):
    """취업증빙 fields[] — EmploymentCard 양식 순서/라벨/키에 정확히 정렬.
    이름/증빙 번호/고용 회사/직종/입사일/발급일 (6필드, 만료일 없음).
    name 은 시트에 없고 그 손님 여권 영문이름과 동일해야 하므로 passport 에서 조인한다
    (여권 문서 표시값과 같은 소스 = 정상 일치 보장). 만료일(expiry_date)은 양식에 칸이
    없어 취업증빙에서 제거(시트에 남아 있어도 생성기가 무시)."""
    return [
        {"label": "이름", "value": clean_name(passport_row["name_en"])},
        {"label": "증빙 번호", "value": emp_row.get("cert_no", "")},
        {"label": "고용 회사", "value": emp_row.get("company_name", "")},
        {"label": "직종", "value": emp_row.get("job_title", "")},
        {"label": "입사일", "value": emp_row.get("hire_date", "")},
        {"label": "발급일", "value": emp_row.get("issue_date", "")},
    ]


def visa_fields(visa_row, columns, passport_row):
    """비자 fields[] — VisaCard 양식의 이름/여권번호 슬롯(_fieldKey=name/passport_no)을 채운다.
    이름·여권번호는 visa 시트에 없고 그 손님 여권과 동일해야 하므로 passport 에서 조인한다
    (여권 표시값과 같은 소스 = 정상 일치 보장 + 비자↔여권 대조 가능. 여권 필드가 위조되면
    비자는 원본을 유지하므로 불일치로 적발됨 — 취업증빙과 동일한 조인 규칙).
    라벨 '영문이름'/'여권번호'는 tag_fields 가 key=name/passport_no 로 변환한다.
    나머지(비자번호/종류/국적/발급·만료일/입국횟수/비고)는 visa 시트 컬럼에서 가져온다."""
    joined = [
        {"label": "영문이름", "value": clean_name(passport_row["name_en"])},
        {"label": "여권번호", "value": passport_row["passport_no"]},
    ]
    return joined + fields_from_sheet(visa_row, columns, {"visa_id", "customer_id"})


def pcr_fields(pcr_row, passport_row):
    """PCR검사서 fields[] — PcrCard 양식 슬롯(_fieldKey)에 정확히 정렬.
    이름/국적/검사 번호/검사일/검사 결과/유효 기한/검사 기관 순.

    이름·국적은 PcrCard 의 Slot_name(_fieldKey=name)/Slot_nationality(_fieldKey=nationality)
    슬롯을 채우기 위한 신원 필드다. pcr_test 시트에도 name/nationality 컬럼이 있으나
    **그 손님 여권과 정확히 동일해야** PCR↔여권 대조가 일치하므로 passport 에서 조인한다
    (visa_fields/employment_fields 와 같은 조인 규칙 = 단일 진실).

    라벨을 시트 row4 가 아니라 여기서 한글로 고정하는 이유: 현재 엑셀 row4(한글 라벨)가
    인코딩 손상(mojibake)이라 fields_from_sheet 로는 깨진 라벨/빈 key 가 나온다. 안정적인
    영문 컬럼 key(row3)만 값 소스로 쓰고, 표시 라벨/속성 key 는 코드에서 확정한다
    (6장: 변경 흡수 한 곳 — 컬럼 라벨이 바뀌어도 PCR 표시는 안 흔들린다).
    신원(이름/국적)은 결함(양성/위조/미제출)과 무관하게 항상 본인(여권 동일)."""
    return [
        {"label": "이름", "value": clean_name(passport_row["name_en"])},
        {"label": "국적", "value": passport_row["nationality"]},
        {"label": "검사 번호", "value": pcr_row.get("test_no", "")},
        {"label": "검사일", "value": pcr_row.get("test_date", "")},
        {"label": "검사 결과", "value": pcr_row.get("result", "")},
        {"label": "유효 기한", "value": pcr_row.get("valid_until", "")},
        {"label": "검사 기관", "value": pcr_row.get("lab_name", "")},
    ]


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
    "hire_date": "입사일",
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


# 서류 종류별 표준 유효기간(년). EXPIRE 주입 시 발급일=만료일−유효기간으로 함께 보정해
# 항상 발급일 < 만료일 < 오늘이 성립하게 한다(불가능 데이터 방지).
EXPIRE_VALIDITY_YEARS = {
    "여권": 10,
    "비자": 1,
    "취업증빙": 1,
}


def _shift_years_str(date_str, years):
    """'YYYY-MM-DD'에서 연도를 years만큼 뺀(또는 더한) 문자열. 2/29는 28로 보정."""
    try:
        y, m, d = (int(x) for x in str(date_str).split("-"))
    except (ValueError, AttributeError):
        return None
    ny = y + years
    if m == 2 and d == 29:
        d = 28
    return f"{ny:04d}-{m:02d}-{d:02d}"


def apply_expire(doc, fields):
    """EXPIRE 결함: 만료일을 과거로 두고, 발급일도 (만료일−유효기간)으로 함께 보정.
    발급일 < 만료일 < 오늘 불변식을 보장한다. 변조된 라벨('만료일') 반환."""
    doc_type = doc.get("documentType", "")
    new_expiry = expire_date(get_field(fields, "만료일"))
    set_field(fields, "만료일", new_expiry)
    # 발급일이 존재하는 서류면(여권/비자/취업증빙) 함께 과거로 끌어내려 모순 제거.
    if get_field(fields, "발급일") is not None:
        years = EXPIRE_VALIDITY_YEARS.get(doc_type, 1)
        new_issue = _shift_years_str(new_expiry, -years)
        if new_issue:
            set_field(fields, "발급일", new_issue)
    return "만료일"


def apply_defect(doc, fields, corruption_type, target_key, fake_pool, ctx,
                 ctx_self_name="", ctx_self_gender="", ctx_self_sprite="",
                 ctx_self_age="", ctx_self_nat=""):
    """단일 결함 1개 주입. 변조된 한글 라벨을 반환.
    ctx_self_name: 사진 결함 시 디코이로 본인 에셋을 고르지 않도록 손님 본인 한글 이름.
    ctx_self_gender/ctx_self_sprite: 사진 결함 시 같은 성별의 다른 얼굴을 고르기 위한 손님 성별/본인 sprite_ref.
    ctx_self_age/ctx_self_nat: 사진 결함 시 나이대 근접·국적권 매칭에 쓰는 손님 나이/국적."""
    label = KO_LABEL.get(target_key, target_key)

    if corruption_type == "EXPIRE":
        # 만료일 과거로 + 발급일 동반 보정(발급일 < 만료일 < 오늘 보장)
        return apply_expire(doc, fields)

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
        # 사진 결함: 손님 본인이 아닌 '그럴듯한 다른 실존 인물' 얼굴 에셋을 디코이로 끼운다.
        # → 얼굴 대조 시 customer.spriteRef(본인) ≠ 여권 사진(디코이) 로 '불일치' 적발이 정답.
        # 현실성: 같은 성별 + 나이대 근접 + 가능하면 같은 국적권. 노인/특수/범죄 얼굴은 후보에서 제외.
        #   (build_face_meta 가 customer 시트에서 적격 후보만 도출 → 외형이 튀는 디코이가 안 나온다.)
        # ctx = (day, slot, customer_id). 본인은 ctx_self_sprite(=customer.sprite_ref) 로 제외.
        self_sprite = (ctx_self_sprite or ctx_self_name or "").strip()
        gender = (ctx_self_gender or "").strip()
        decoy = pick_decoy_face(self_sprite, gender, ctx_self_age, ctx_self_nat, *ctx)
        if not decoy or decoy == "photo_mismatch":
            # 폴백: 적격 메타가 비는 극단 상황 — fake_value_pool 의 face 항목(실존 PNG)에서 본인 제외.
            face_cands = [r["fake_value"] for r in fake_pool
                          if r["field"] == "face" and (r["fake_value"] or "").strip() != self_sprite]
            decoy = seeded_pick(face_cands, "fakeface", *ctx) if face_cands else "photo_mismatch"
        doc["spriteRef"] = decoy
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
            fv = fake_value_for("lab_name", fake_pool, *ctx) or "종합검진센터"
            set_field(fields, "검사 기관", fv)
            return "검사 기관"
        # company_name (취업증빙)
        fv = fake_value_for("company_name", fake_pool, *ctx) or "(주)유령상사"
        set_field(fields, "고용 회사", fv)
        return "고용 회사"

    if corruption_type == "HIRE_LOGIC":
        # 취업증빙 입사일 논리오류: 입사일을 발급일보다 "늦게"(발급일 이후 = 불가능).
        # 발급일은 정상 유지하고 입사일만 발급일+1년으로 밀어 hire_date > issue_date 모순 생성.
        issue = get_field(fields, "발급일")
        bad_hire = _shift_years_str(issue, +1) if issue else None
        set_field(fields, "입사일", bad_hire or "2099-01-01")
        return "입사일"

    # (MISSING = PCR 검사결과 누락 결함은 시나리오에서 제거됨)

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
# 대조 시 Match 가 보장된다. 식별 키는 **위조되지 않는 안정 필드 nameEn(attr="name")** 으로 통일한다.
#   - passport_no 는 밀수/마약범 등에서 런타임 위조될 수 있어 트리거 식별값으로 부적합(불일치로 미해금).
#   - nationality 는 같은 날 동일 국적이 여럿이면 단일 손님을 못 가린다.
#   nameEn 은 위조 대상이 아니고 같은 날 중복도 없어(검증 완료) 그 손님 1명에게만 정확히 매칭된다.
#
# 형식: (day, customer_id, attr, value, [unlocksScan...])
#   attr/value  : 손님 식별(여권 필드와 동일). attr="name", value=손님 영문이름(name_en).
#   unlocksScan : 이 손님이 보유한 검사 종류(들).
SCAN_TRIGGERS = [
    # day3 손님10 윤서린(지문, 성형 위장 지명수배) — day3 등장으로 이동. 영문이름 유일.
    (3, "10", "name", "YOON SEORIN", ["fingerprint"]),
    # day11 존 카터·day14 강도식: day11 '보안 강화' 이후 전원 입장 자동 X-ray(InspectionController)로
    #   바뀌어, 이름 지목 뉴스 대조(unlocksScan) 없이도 X-ray 가 열린다 → 명단 트리거 제거(일반 경보로 대체).
    #   (옛: (11,"8","name","JOHN CARTER",["xray"]) / (14,"9","name","KANG DOSIK",["xray"]))
    # day12 사토 하루키(테러범)는 이름 지목 뉴스로 해금하지 않는다(2026-06).
    #   → 비자↔여권 여권번호 불일치(visa JP1012287 ≠ passport JP9911287)를 적발하면 CrossCheckController 의
    #     DetectPassportNoMismatchUnlock 이 X-ray 를 잠금 해제한다(서류↔서류 passport_no Mismatch + 손님이 X-ray 보유).
    #   → day12 위험물 뉴스는 특정인 미지목 일반 경보(GENERIC_ALERT_NEWS)로 대체. name-기반 unlocksScan 의존 제거.
    # day13 은 더 이상 주요 스캔 범죄자 없음(사토 하루키 day12 이동) → xray 트리거 제거(헛 단서 방지).
]

# ── 특정인 미지목 일반 경보 뉴스 (이름/unlocksScan 없는 연출 뉴스) ──────────────
# SCAN_TRIGGERS(이름 지목 해금)와 달리, 손님을 직접 가리키지 않는 일반 경보를 day 에 주입한다.
# claim 은 contraband(위험물) 관련성만 표기하고 unlocksScan=""(트리거 아님) → 해금은 다른 경로(예: 여권번호 불일치)가 담당.
# 형식: (day, title, content, claim_label)
GENERIC_ALERT_NEWS = [
    # day12: 사토 하루키는 여권번호 불일치 → X-ray 흐름으로 해금. 뉴스는 일반 폭발물/위험물 경보(미지목).
    (12, "[속보] 위험물 반입 경보",
     "국제 공조 수사 결과 최근 입국 경로에서 폭발물 부품·밀수품 은닉 사례가 다수 적발되었습니다. "
     "여권·비자 등 서류 정보가 서로 일치하지 않는 입국자는 위험물 반입 가능성을 의심해 정밀 검사를 시행하십시오.",
     "위험물 반입 주의"),
    # day11·14: 보안 강화로 전원 입장 자동 X-ray 시행 → 이름 지목 없는 일반 경보(연출).
    (11, "[속보] 위험물 반입 경보",
     "보안 강화 조치에 따라 모든 입국자의 수하물을 X-ray로 정밀 검사합니다. "
     "폭발물·마약·밀수품 등 금지 물품이 적발되면 입국이 거부됩니다.",
     "위험물 반입 주의"),
    (14, "[속보] 위험물 반입 경보",
     "보안 강화 조치에 따라 모든 입국자의 수하물을 X-ray로 정밀 검사합니다. "
     "폭발물·마약·밀수품 등 금지 물품이 적발되면 입국이 거부됩니다.",
     "위험물 반입 주의"),
]
# day별 일반 경보 뉴스 ID(원본/스캔트리거 ID와 충돌 방지). SCAN_TRIGGER_NEWS_ID_BASE 와 동일 대역(900000)에서
# day12 사토 자리를 유지(기존 912000 보존) — idempotent.
GENERIC_ALERT_NEWS_ID_BASE = 900000

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


def build_generic_alert_news(day):
    """특정인 미지목 일반 경보 뉴스(이름/unlocksScan 없음)를 합성 생성. 없으면 빈 리스트.
    claim 은 contraband 관련성만(unlocksScan="") → 손님 해금은 다른 경로(여권번호 불일치 등)가 담당.
    idempotent: 같은 GENERIC_ALERT_NEWS 입력이면 같은 news_id(day12=912000 보존)."""
    out = []
    for (d, title, content, claim_label) in GENERIC_ALERT_NEWS:
        if d != day:
            continue
        news_id = GENERIC_ALERT_NEWS_ID_BASE + d * 1000
        out.append({
            "newsId": news_id,
            "title": title,
            "content": content,
            "iconRef": "",
            "claims": [{
                "attr": "contraband",
                "value": "",
                "label": claim_label,
                "unlocksScan": "",
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


def _nat_code(value, fallback=""):
    """'대한민국(KOR)' / '대한민국 (KOR)' -> 'KOR'. 괄호 코드가 없으면 fallback(여권 nat_code) 사용."""
    if not value:
        return fallback
    s = str(value)
    l = s.rfind("(")
    r = s.rfind(")")
    if 0 <= l < r:
        code = s[l + 1:r].strip()
        if code:
            return code
    return fallback or s.strip()


def build_fingerprint_scan(fp_row, customer, passport, is_self):
    """지문 시트 1행(옵션B) + 손님/여권 + 본인여부 -> ScanData(dict, record 포함).

    옵션B 컬럼: mode(성형|수배자), alt_name, alt_birth, alt_nationality, criminal_record, wanted_no.
    - 본인(is_self=True): record 의 이름/생일/국적을 그 손님 '여권'에서 복제 → 교차대조 시 Match.
      (데스크 name 셀렉터블/여권 영문이름은 모두 영문이므로 dbName 도 여권 영문이름으로 둔다.)
    - 도용/수배(is_self=False): alt_name/alt_birth 를 쓰고 국적은 여권 형식 코드로 통일 →
      이름(과 생일)만 불일치 → 플레이어가 도용으로 판정.
    수배자(mode=수배자)는 호출부에서 항상 is_self=False 로 들어온다(양쪽 모두 수배).
    """
    pname = clean_name(passport["name_en"])          # 영문 (데스크 name 비교 기준)
    pbirth = passport["birth_date"]                  # 여권 형식(YYYY-MM-DD)
    pnat = passport["nationality"]                   # 여권 국가코드(KOR 등)

    if is_self:
        db_name = pname
        db_birth = pbirth
        db_nat = pnat
        criminal = "없음"
        wanted = ""
        result = "일치"
        match_status = "본인 일치"
    else:
        # 도용/수배: 지문 DB 가 가리키는 '진짜 신원'(여권 주장 신원과 다름).
        db_name = (fp_row.get("alt_name") or "").strip()
        db_birth = (fp_row.get("alt_birth") or "").strip()
        db_nat = _nat_code(fp_row.get("alt_nationality"), fallback=pnat)
        criminal = (fp_row.get("criminal_record") or "없음").strip() or "없음"
        wanted = (fp_row.get("wanted_no") or "").strip()
        result = "불일치"
        match_status = "신원 불일치"

    record = {
        "mode": (fp_row.get("mode") or "").strip(),
        "dbName": db_name,
        "dbBirth": db_birth,
        "dbNationality": db_nat,
        "criminalRecord": criminal,
        "wantedNo": wanted,
    }
    return {
        "type": "fingerprint",
        "result": result,
        "detail": match_status,                    # 대조 상태(표시용)
        "extra": db_name,                          # 진짜 신원 이름(표시용)
        # claim.attr=name: 지문 DB 이름을 여권/캐릭터 이름(attr=name)과 교차대조.
        "claim": {"attr": "name", "value": db_name, "label": "지문 대조 신원", "unlocksScan": ""},
        "record": record,
    }


# ── document_requirement 평가 ────────────────────────────────
def required_documents(day, character_type, passport_nat_code):
    """그날 손님에게 요구되는 서류 타입 목록."""
    docs = ["여권"]  # 전일 필수
    if 3 <= day <= 14 and passport_nat_code != "KOR":
        docs.append("비자")
    if 5 <= day <= 7:
        docs.append("PCR검사서")   # 방역 구간: 전원 PCR 검사(document_requirement #3 = 전원)
    if 8 <= day <= 14 and character_type in EMPLOYMENT_TYPES:
        docs.append("취업증빙")
    return docs


# ── 대사(최소 플레이) ────────────────────────────────────────
# ── 화자명 치환 (손님측 speaker -> 그 손님 실제 이름, 6장: 변경 흡수 한 곳) ──
# 대사 라인 speaker 는 빌드 시 "캐릭터"/"손님"(손님측) 또는 "심사관"/"검사관"/"시스템"(비손님)으로
# 생성된다. 표시상 손님 발화에 일반 명칭 대신 그 손님의 실제 한글 이름(nameKr)을 노출한다.
# - 손님측으로 보는 speaker 값만 nameKr 로 치환하고, 심사관/시스템 등은 그대로 둔다.
# - 이미 nameKr 로 치환된 값은 손님측 집합에 없으므로 재실행해도 불변(idempotent).
CUSTOMER_SPEAKERS = {"캐릭터", "손님"}


def localize_speakers(customer):
    """customer.dialogueCases 의 손님측 speaker 를 그 손님 nameKr 로 치환(in-place).
    심사관/검사관/시스템 등 비손님 화자는 보존. 반환: 치환한 라인 수."""
    name = customer.get("nameKr") or ""
    if not name:
        return 0
    n = 0
    for case in customer.get("dialogueCases", []):
        for ln in case.get("lines", []):
            if ln.get("speaker") in CUSTOMER_SPEAKERS:
                ln["speaker"] = name
                n += 1
    return n


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


# ── 손님별 '정상 거절' 심사관 라인 특수 오버라이드 (customer_id -> (text, claim_label)) ──
# 일반 경로(make_dialogue_cases)는 "{위반항목} 항목에 문제가 있습니다" 를 쓰지만, 특정 손님은
# 흐름상 더 자연스러운 거절 멘트가 필요하다. 여기 등록된 손님만 '정상 거절' 1번 심사관 라인을 덮어쓴다.
# (구조/케이스 수/order/speaker 불변, 심사관 text 와 claim.label 만 교체. claim.attr 은 violation_attr 유지.)
REJECT_INSPECTOR_OVERRIDE = {
    # day12 사토 하루키(테러범): 여권번호 불일치(비자 JP1012287 ≠ 여권 JP9911287) → X-ray 해금 → 위험물 적발.
    # X-ray 해금은 이 불일치 적발(CrossCheckController)로 일어나므로 뉴스 이름 지목 불필요.
    # 정상 거절 멘트는 정밀 검사(X-ray) 위험물 적발 결과로 통일(번호 불일치 멘트 대체).
    11: ("정밀 검사에서 위험물이 발견되었습니다. 입국을 허가할 수 없으며, 보안 절차에 회부됩니다.",
         "위험물 적발"),
}


def make_dialogue_cases(nat_code, violation_label, violation_attr="", character_type="", customer_id=None):
    """손님마다 7케이스 보장(입장/정상승인/정상거절/잘못허가/잘못거절1·2·3).

    violation_attr가 있으면 '정상 거절' 케이스의 심사관 지적 라인에 claim을 달아
    뉴스/서류와 같은 속성 키로 대조 가능하게 한다(placeholder 대사 한계 내 최선).
    customer_id 가 REJECT_INSPECTOR_OVERRIDE 에 있으면 '정상 거절' 심사관 라인을 그 손님 전용 멘트로 교체.
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
    override = REJECT_INSPECTOR_OVERRIDE.get(customer_id)
    if override is not None:
        ov_text, ov_claim_label = override
        reject_inspector_line = {"order": 1, "speaker": "심사관", "text": ov_text}
        if violation_attr:
            reject_inspector_line["claim"] = {
                "attr": violation_attr, "value": "", "label": ov_claim_label, "unlocksScan": "",
            }
    else:
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

    # 보스 가이드 케이스: 성형 수술 지명수배 범죄자 — 거부(최선) 시 재생할 몽타주 시퀀스(분기 A).
    # 표준 7케이스와 충돌하지 않도록 별도 caseType('분기 거부')로 추가(런타임은 FindCaseByType 로 조회).
    if character_type == "범죄자(성형수술)":
        guided = load_guided_block(TYPE_MAP.get("범죄자(성형수술)"), "분기 A")
        if guided:
            cases.append({
                "caseType": "분기 거부", "gameResult": "정상 거절", "rejectCount": 0,
                "lines": guided,
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

    # 결함 배정 표(손님별 결함 종류 명시): 시드 자동배정/코드 FORCED_DEFECT 대신 표로 제어.
    # cid(str) -> {document, corruption_type, target_field}. 시트 없으면 빈 dict(레거시 동작 유지).
    defect_assign = {}
    if sheets.get("defect_assign"):
        for r in rows(sheets["defect_assign"]):
            cid_k = r.get("customer_id")
            if cid_k not in (None, ""):
                defect_assign[str(int(cid_k))] = r

    visa_cols = sheets["visa"]["columns"]
    # pcr 은 pcr_fields() 가 안정 영문 key 로 직접 조립(엑셀 라벨 mojibake 회피)하므로 cols 불요.
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
    # 지문(옵션B)은 등장마다 본인/도용이 달라지므로 '행'만 인덱싱하고 record 는 손님 루프 안에서 생성.
    fingerprint_rows = {r["customer_id"]: r for r in rows(sheets["fingerprint"])}

    # day_schedule grouped by day
    schedule = {}
    for r in rows(sheets["day_schedule"]):
        schedule.setdefault(int(r["day"]), []).append(r)

    # 시나리오 대사 인덱스(characterType -> 블록들). [TODO 대사] 채우기에 사용.
    branch_index = index_branch(load_branch())
    # 캐릭터별 손글 대사 인덱스(customerId -> entry). 유형 대사 위에 덮어쓴다.
    char_index = load_char_dialogue()
    dialogue_report = {}   # (dayCharType, caseType, gameResult) -> {filled,todo}
    char_report = {}       # (caseType, gameResult) -> {override, skip}
    authored_report = {}   # (ctype, caseType, gameResult) -> {authored, still_todo}
    polish_report = {}     # {removed, foreign, yoon, unmapped:[...]} 누적(정화 통계)

    report = {}

    for day in range(1, 15):
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
                    "fields": tag_fields(visa_fields(visas[cid], visa_cols, pp)),
                })
            # PCR검사서
            if "PCR검사서" in req_docs and cid in pcrs:
                documents.append({
                    "documentType": "PCR검사서", "variant": "정상", "violationField": "없음",
                    "spriteRef": "", "country": "",
                    "fields": tag_fields(pcr_fields(pcrs[cid], pp)),
                })
            # 취업증빙 — EmploymentCard 양식 정렬(이름은 여권 조인, 만료일 제거)
            if "취업증빙" in req_docs and cid in emps:
                documents.append({
                    "documentType": "취업증빙", "variant": "정상", "violationField": "없음",
                    "spriteRef": "", "country": "",
                    "fields": tag_fields(employment_fields(emps[cid], pp)),
                })

            violation_label = None
            applied_ct = ""   # 실제 적용된 corruption_type (defect_variant 도출용)
            applied_tf = ""   # 실제 적용된 target_field
            applied_doc = ""  # 실제 결함이 들어간 서류(defect_document) — PCR 변이 판별용

            # 지문 대조로 본인/도용을 가리는 손님(성형 수술 고객 / 성형수술 범죄자)은
            # 서류(여권)를 변조하지 않는다. 본인이면 여권=지문DB(일치), 도용이면 지문DB만 다르다.
            # 이렇게 해야 '여권은 정상인데 지문 신원만 다르다'는 옵션B 설계가 성립한다.
            # 판별: 캐릭터 유형 set(설계 의도) 또는 그 손님이 지문 행을 가졌는지(데이터 주도, 리네임 안전).
            fp_discriminated = (ctype in FINGERPRINT_DISCRIMINATED_TYPES
                                or cid in fingerprint_rows)

            if is_normal or fp_discriminated:
                if is_normal:
                    stats["normal"] += 1
                else:
                    stats["abnormal"] += 1
            else:
                stats["abnormal"] += 1
                # ── 사진 결함 강제(외국인 관광객, 손글 스크립트 '여권 사진 불일치' 슬롯) ──
                # 규칙 선택보다 우선. 여권 photo_ref 를 같은 성별의 다른 인물 얼굴로 교체해
                # 얼굴 대조 시 '불일치'(=거절 정답)가 되게 한다. 기존 결함을 '대체'(추가 아님).
                forced_photo = (str(cid) in FORCED_PHOTO_DEFECT_TOURIST
                                and ctype == TOURIST_TYPE)
                if forced_photo:
                    vlabel = apply_defect(
                        pdoc, pdoc["fields"], "MISMATCH_PHOTO", "photo_ref", fake_pool,
                        (day, slot, cid),
                        ctx_self_name=cust.get("name_kr", ""),
                        ctx_self_gender=cust.get("gender", ""),
                        ctx_self_sprite=cust.get("sprite_ref", ""),
                        ctx_self_age=cust.get("age", ""),
                        ctx_self_nat=cust.get("nationality", ""))
                    pdoc["variant"] = "비정상"
                    pdoc["violationField"] = vlabel
                    violation_label = vlabel
                    applied_ct = "MISMATCH_PHOTO"
                    applied_tf = "photo_ref"
                    applied_doc = "여권"
                    stats["defects"]["photo_forced"] = stats["defects"].get("photo_forced", 0) + 1
                    # baked 변이/정산은 기존 흐름과 동일하게 아래에서 처리되며, 규칙 선택은 건너뛴다.
                    # (외국인 관광객 분실/출국X 변이는 사진 불일치엔 해당 없음 → defect_variant="")
                    # 강제 사진 결함을 적용했으니 일반 규칙 주입 블록을 통째로 우회한다.
                if not forced_photo:
                    # 상황별 규칙 선택: 결함서류가 요구 서류에 포함된 규칙 우선
                    doc_name_map = {"여권": "여권", "비자": "비자", "pcr_test": "PCR검사서",
                                    "employment_cert": "취업증빙"}
                    # 종류 기반 결함 선택(전염병 환자=PCR 결함 규칙을 가짐 → 자동으로 PCR 주입).
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
                        # 결함 배정 표(data-driven) 우선: cid가 표에 있고 대상 서류가 현재 규칙과 같으면
                        # 시드 결과를 무시하고 표의 명시값(결함 종류/대상 필드)을 그대로 적용한다.
                        asg = defect_assign.get(str(cid))
                        if asg and str(asg.get("document", "")) == rule.get("defect_document"):
                            ct, tf = asg["corruption_type"], asg["target_field"]
                        else:
                            # 레거시 코드 고정(표로 이전 안 된 경우만). 규칙의 유효 쌍 안에서만 덮어쓴다.
                            forced = FORCED_DEFECT.get(str(cid))
                            if forced and forced in pairs:
                                ct, tf = forced

                        # 결함 대상 서류 찾기
                        defect_doc_name = rule["defect_document"]
                        doc_map = {"여권": "여권", "비자": "비자", "pcr_test": "PCR검사서",
                                   "employment_cert": "취업증빙"}
                        target_doc_type = doc_map.get(defect_doc_name, defect_doc_name)
                        target_doc = next((d for d in documents if d["documentType"] == target_doc_type), None)

                        if target_doc is not None:
                            vlabel = apply_defect(target_doc, target_doc["fields"], ct, tf, fake_pool,
                                                  (day, slot, cid), ctx_self_name=cust.get("name_kr", ""))
                            if vlabel:
                                target_doc["variant"] = "비정상"
                                target_doc["violationField"] = vlabel
                                # 결함 배정 표에 명시값(value)이 있으면 그 값으로 고정 → 랜덤 FORGE 대신 결정론(소스=게임 일치).
                                if asg and str(asg.get("document", "")) == rule.get("defect_document") \
                                        and asg.get("value") not in (None, ""):
                                    set_field(target_doc["fields"], vlabel, asg["value"])
                                violation_label = vlabel
                                applied = True
                                applied_ct = ct
                                applied_tf = tf
                                applied_doc = rule["defect_document"]
                                stats["defects"][rule["rule_id"]] = stats["defects"].get(rule["rule_id"], 0) + 1
                    if not applied:
                        # 결함 주입 실패(요구 서류에 결함서류가 없거나 NONE) → 여권 만료로 폴백
                        apply_expire(pdoc, pdoc["fields"])
                        pdoc["variant"] = "비정상"
                        pdoc["violationField"] = "만료일"
                        violation_label = "만료일"
                        applied_ct = "EXPIRE"
                        applied_tf = "expiry_date"
                        applied_doc = "여권"
                        stats["defects"]["fallback"] = stats["defects"].get("fallback", 0) + 1

            # defect_variant 도출(baked 로 가능한 변이만; 그 외 ""→게임플레이 결정)
            defect_variant = resolve_defect_variant(ctype, is_normal, applied_ct, applied_tf, applied_doc)
            if defect_variant:
                stats.setdefault("variants", {})
                stats["variants"][defect_variant] = stats["variants"].get(defect_variant, 0) + 1

            # 적발물(X-ray)은 '거부가 정답'(=실제 범죄 버전)일 때만 부여한다.
            # 정상으로 굴러 '승인이 정답'이면 깨끗(적발물 None). 성형수술 범죄자는 항상 거부.
            correct_result = ("정상 거절" if ctype == "범죄자(성형수술)"
                              else ("정상 승인" if is_normal else "정상 거절"))
            caught = correct_result == "정상 거절"

            # ── 지문(옵션B): 등장마다 본인/도용을 생성. caught 게이팅과 독립. ──
            # 그 손님의 지문 행이 있으면 항상 부착(성형 의심 고객은 본인=승인 정답일 때도 노출).
            #  - 본인 여부: 수배자(범죄자(성형수술))는 항상 도용(False), 그 외는 is_normal 그대로.
            #    valid_chance 가 본인/도용 갈림을 결정하므로 (day,slot,cid) 시드로 결정론 보장.
            fp_scan = None
            fp_row = fingerprint_rows.get(cid)
            if fp_row is not None:
                fp_is_self = False if ctype == PLASTIC_WANTED_TYPE else is_normal
                fp_scan = build_fingerprint_scan(fp_row, cust, pp, fp_is_self)

            customer_entry = {
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
                # 변장 지명수배범: 서류는 정상이나 '거부가 정답'(승인=잘못 허가 페널티).
                # 거부 시 InspectionController 가 rejectAdvancedBranchKey 로 가이드 최선 분기 정산.
                "correctResult": correct_result,
                "documents": documents,
                "dialogueCases": make_dialogue_cases(
                    nat_code, violation_label, attr_for_label(violation_label),
                    character_type=ctype, customer_id=int(cid)),
                # 고급 분기 손님 플래그: 거부 시 가이드 대사+최선 분기 정산(대상 외 손님은 빈 문자열).
                "rejectAdvancedBranchKey": "detect_montage_xray_reject" if ctype == "범죄자(성형수술)" else "",
                "rejectGuidedCaseType": "분기 거부" if ctype == "범죄자(성형수술)" else "",
                # X-ray 적발물은 '거부가 정답'일 때만(정상 손님이면 깨끗 → None). valid_chance 도박/엔딩 밸런스 불변.
                "xray": xray_scans.get(cid) if caught else None,
                # 지문은 caught 와 독립 — 지문 행이 있으면 본인/도용 record 를 항상 부착(옵션B).
                "fingerprint": fp_scan,
            }
            # 시나리오 대사 주입: dialogueCases 의 [TODO 대사] text 를 branch 대사로 교체.
            # 구조(개수/order/speaker) 불변. 매핑 불가 라인은 [TODO] 유지(리포트에 집계).
            fill_customer_dialogue(customer_entry, branch_index, dialogue_report)
            # 1.5차: 캐릭터별 손글 대사 오버라이드(유형 위에 덮어씀). 손님 라인 text 만 교체.
            # 캐논에 없는 라인은 유형 대사 유지. 심사관 라인·구조는 불변.
            apply_character_override(customer_entry, char_index, char_report)
            # 2차: branch 에 없는 잔여 [TODO 대사] 를 작성 폴백으로 채운다(구조 불변, text 만).
            fill_authored(customer_entry, authored_report)
            # 3차: 손님측 화자명(캐릭터/손님)을 그 손님 실제 이름(nameKr)으로 치환(표시용).
            localize_speakers(customer_entry)
            # 4차(정화): 액션/시스템 라인 제거 + 외국인 언어 통일 + 윤서린 거절 변주.
            polish_customer(customer_entry, polish_report)
            day_customers.append(customer_entry)

        # rule_book 은 사용자가 컬럼을 줄일 수 있다(related_field 제거 등) → .get 으로 흡수.
        # end_day: 이벤트성 규정의 종료 일차(이 일차 이후 규정집에서 제외). 값이 없으면 0(=종료 없음).
        # C# RuleData.endDay 가 0=무기한이므로, 양수일 때만 endDay 를 출력한다(다른 규정은 필드 부재).
        def _rule_obj(r):
            obj = {"ruleId": int(r["rule_id"]), "title": r.get("rule_title", ""),
                   "content": r.get("rule_content", ""),
                   "relatedField": r.get("related_field", "") or "",
                   "attr": attr_for_label(r.get("related_field", ""))}
            end_day_raw = r.get("end_day", "")
            try:
                end_day = int(end_day_raw) if str(end_day_raw).strip() not in ("", "None") else 0
            except (TypeError, ValueError):
                end_day = 0
            if end_day > 0:
                obj["endDay"] = end_day
            return obj

        day_rules = [_rule_obj(r) for r in rule_book if int(r["day"]) == day]
        day_news = [
            {"newsId": int(r["news_id"]), "title": r["news_title"],
             "content": r["news_content"], "iconRef": r["icon_ref"],
             "claims": derive_news_claims(r["news_id"], r["news_title"], r["news_content"])}
            for r in news if int(r["day"]) == day
        ]
        # 스캔 잠금 해제 트리거 뉴스(합성) 주입 — idempotent, 11~14일차에만 존재
        day_news.extend(build_scan_trigger_news(day))
        # 특정인 미지목 일반 경보 뉴스(합성) 주입 — idempotent, day12 위험물 경보(이름 지목 없음)
        day_news.extend(build_generic_alert_news(day))

        data = {"day": day, "customers": day_customers, "rules": day_rules, "news": day_news}

        # day1.json 은 수작업 완성본(손글 톤)이므로 절대 덮어쓰지 않는다.
        # build_days 는 day2~14 만 영속화한다(작업 지시: "build_days 가 2~14만 생성").
        # BUILD_DAYS_INCLUDE_DAY1=1 로 명시할 때만 day1 도 쓴다(스테이징 비교용).
        if day == 1 and os.environ.get("BUILD_DAYS_INCLUDE_DAY1") != "1":
            report[day] = stats
            continue

        out_path = os.path.join(OUT_DIR, f"day{day}.json")
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

        report[day] = stats

    return report, dialogue_report, authored_report, char_report, polish_report


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


def write_dialogue_report(dialogue_report):
    """시나리오 대사 매핑 커버리지 리포트(유형×케이스×결과별 채운/미채운 줄수)."""
    lines = ["# 시나리오 대사 매핑 커버리지", ""]
    by_type = {}
    for (ctype, casetype, gr), v in dialogue_report.items():
        by_type.setdefault(ctype, []).append((casetype, gr, v["filled"], v["todo"]))
    grand_filled = grand_todo = 0
    for ctype in sorted(by_type):
        rows_ = sorted(by_type[ctype])
        tf = sum(r[2] for r in rows_)
        tt = sum(r[3] for r in rows_)
        grand_filled += tf
        grand_todo += tt
        lines.append(f"## {ctype} (채움 {tf} / 미채움 {tt})")
        for casetype, gr, f, t in rows_:
            mark = "" if t == 0 else f"  [TODO {t}]"
            lines.append(f"  - {casetype} / {gr}: 채움 {f}{mark}")
    lines.insert(1, f"총 채움 {grand_filled} / 총 미채움(TODO) {grand_todo}")
    with open(os.path.join(os.path.dirname(__file__), "dialogue_coverage_report.txt"),
              "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    return grand_filled, grand_todo


def write_authored_report(authored_report):
    """작성 폴백 커버리지(유형×케이스×결과별 작성/잔여 줄수). 잔여 0 이 목표."""
    lines = ["# 작성 폴백(authored) 커버리지", ""]
    by_type = {}
    for (ctype, casetype, gr), v in authored_report.items():
        by_type.setdefault(ctype, []).append((casetype, gr, v["authored"], v["still_todo"]))
    grand_a = grand_t = 0
    for ctype in sorted(by_type):
        rows_ = sorted(by_type[ctype])
        ta = sum(r[2] for r in rows_)
        tt = sum(r[3] for r in rows_)
        grand_a += ta
        grand_t += tt
        lines.append(f"## {ctype} (작성 {ta} / 잔여 {tt})")
        for casetype, gr, a, t in rows_:
            mark = "" if t == 0 else f"  [잔여 {t}]"
            lines.append(f"  - {casetype} / {gr}: 작성 {a}{mark}")
    lines.insert(1, f"총 작성 {grand_a} / 총 잔여(TODO) {grand_t}")
    with open(os.path.join(os.path.dirname(__file__), "authored_coverage_report.txt"),
              "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    return grand_a, grand_t


def write_char_report(char_report):
    """캐릭터 오버라이드 커버리지(케이스×결과별 교체/건너뜀 손님 라인 수)."""
    lines = ["# 캐릭터 손글 대사 오버라이드 커버리지", ""]
    grand_ov = grand_sk = 0
    for (casetype, gr), v in sorted(char_report.items()):
        grand_ov += v["override"]
        grand_sk += v["skip"]
        lines.append(f"  - {casetype} / {gr}: 교체 {v['override']} / 건너뜀 {v['skip']}")
    lines.insert(1, f"총 교체 {grand_ov} / 총 건너뜀(캐논 결측·연출보존) {grand_sk}")
    with open(os.path.join(os.path.dirname(__file__), "character_override_report.txt"),
              "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    return grand_ov, grand_sk


if __name__ == "__main__":
    rep, drep, arep, crep, prep = build()
    write_report(rep)
    gf, gt = write_dialogue_report(drep)
    ga, gtleft = write_authored_report(arep)
    co, cs = write_char_report(crep)
    # stdout 한글 깨짐 방지 위해 숫자만 출력
    total = sum(v["normal"] + v["abnormal"] for v in rep.values())
    print(f"days generated: {len(rep)} customers total: {total}")
    print(f"branch filled: {gf} (branch todo: {gt})")
    print(f"char override: {co} (skipped: {cs})")
    print(f"authored filled: {ga} todo remaining: {gtleft}")
    print(f"polish: removed action/system={prep.get('removed', 0)} "
          f"foreign normalized={prep.get('foreign', 0)} "
          f"yoon variants={prep.get('yoon', 0)} "
          f"unmapped foreign lines={len(prep.get('unmapped', []))}")
