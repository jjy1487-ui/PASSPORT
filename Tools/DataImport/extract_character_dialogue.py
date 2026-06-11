# -*- coding: utf-8 -*-
"""
extract_character_dialogue.py — 손글 캐논 문서(일자별 방문객 스크립트)에서
캐릭터별 대사를 추출해 Assets/Resources/GameData/character_dialogue.json 으로 변환한다.

배경:
  기존 dayN.json 대사는 characterType 만으로 조인되는 "유형 평면"이라 같은 유형이면
  날짜·캐릭터가 달라도 대사가 동일했다. 이 추출기는 (day, slot) 단위로 캐논 문서의
  실제 손글 대사를 뽑아 customerId 키로 영속화한다. 주입은 character_dialogue_map.py 가 한다.

단일 소스(읽기 전용):
  C:\\Users\\chris\\Downloads\\방문객_스크립트_일자별_생성_손글기반_260611.xlsx
  - Day 01 ~ Day 14 시트(스케줄순, 슬롯 오름차순), 14일×7슬롯 = 98 블록.
  - 컬럼 A~H: 순서|단계|캐릭터|이름|서류상태|문제유형|결과|대사/액션
  - 블록 헤더행: "[N번째 방문객] {이름} | {유형} | 서류:..."  (N = slot)
  - 인삿말 행(단계=인삿말, 캐릭터=방문객): 손님 입장 인사 「...」
  - 방문객반응 행(캐릭터=방문객): 결과=허가 → 허가 반응 / 결과=거부 → 거부 반응 「...」

(day, slot) → customerId 조인:
  Assets/GameData/_source/GameData.source.json 의 day_schedule 시트(98행)에서
  (day, slot) → customer_id 를 읽는다. customer 시트에서 name_kr/character_type 보강.
  ※ 빌드된 dayN.json 에 의존하지 않으므로 빌드 순서와 무관(닭·달걀 없음).

산출(repo 안):
  Assets/Resources/GameData/character_dialogue.json
  { "<customerId>": {
       "day": int, "slot": int, "nameKr": str, "characterType": str,
       "entry": [손님 입장 인사 텍스트...],          # 보통 1줄
       "approveReaction": str,                        # 허가 시 방문객 반응(없으면 "")
       "rejectReaction": str,                         # 거부 시 방문객 반응(없으면 "")
       "todo": [누락 표식...]                          # 인사/반응이 비면 기록
    }, ... }

제약:
  - 캐논 문서·Downloads 어디에도 쓰지 않는다(읽기 전용).
  - 결정론(난수·시각 의존 없음). 모든 IO utf-8, JSON ensure_ascii=False.
  - idempotent: 같은 xlsx → 같은 character_dialogue.json.

실행:
    python Tools/DataImport/extract_character_dialogue.py
"""
import json
import os
import re
import sys
import io

try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

import openpyxl

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CANON_XLSX = r"C:\Users\chris\Downloads\방문객_스크립트_일자별_생성_손글기반_260611.xlsx"
SOURCE_JSON = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
OUT_PATH = os.path.join(ROOT, "Assets", "Resources", "GameData", "character_dialogue.json")

# 블록 헤더: "[N번째 방문객] {이름} | {유형} | 서류:..."
_HEADER = re.compile(r"\[(\d+)번째 방문객\]\s*(.+)")
# 「...」 인용문(여러 개면 첫 것).
_QUOTE = re.compile(r"「(.+?)」", re.S)
# 헤더 괄호주석 안의 클론 출처: "(더미·클론←박철수)" / "클론<박철수" 등 화살표 변형 흡수.
_CLONE = re.compile(r"클론\s*[←<\-]\s*(.+)")


def _norm(s):
    return "" if s is None else str(s).strip()


def _source_name(header_body):
    """블록 헤더 본문에서 '원본(source) 캐릭터 이름'을 도출.

    헤더 형식: "{이름}  |  {유형}  |  서류:...  (괄호주석)"
    - 괄호주석에 클론 표기("더미·클론←박철수")가 있으면 그 원본 이름이 source.
    - 클론 표기가 없으면(원본 캐릭터) 헤더 첫 칸의 자기 이름이 곧 source.
    같은 source 의 인사 변주를 모으는 풀의 키로 쓴다(클론은 원본 풀을 공유).
    """
    own = header_body.split("|")[0].strip()
    for paren in re.findall(r"\(([^)]*)\)", header_body):
        m = _CLONE.search(paren)
        if m:
            return m.group(1).strip()
    return own


def _first_quote(cell_text):
    """셀 텍스트에서 첫 인용문(「...」) 내용을 뽑는다.
    캐논 셀은 '「대사」\\n※ 주석' 형태가 많으므로 인용문만 취하고 주석은 버린다.
    인용문이 없으면(액션 라인 등) None.
    내부 줄바꿈은 보존하지 않고 공백으로 평탄화한다(표시용 한 줄).
    """
    if not cell_text:
        return None
    m = _QUOTE.search(str(cell_text))
    if not m:
        return None
    seg = m.group(1)
    # 인용문 내부 줄바꿈(병기 등)은 공백으로 평탄화. 다개행 → 단일 공백.
    seg = re.sub(r"\s*\n\s*", " ", seg).strip()
    return seg or None


def load_day_correct_results():
    """이미 빌드된 dayN.json 에서 customerId -> correctResult 를 읽는다(있으면 권위 소스).
    valid_chance 는 시드로 슬롯별 resolve 되므로 correctResult 는 build_days 만 정확히 안다.
    dayN.json 이 아직 없으면(최초 빌드) 빈 맵 반환 → 스케줄 근사값으로 폴백.
    """
    day_dir = os.path.join(ROOT, "Assets", "Resources", "GameData")
    out = {}
    for day in range(1, 15):
        p = os.path.join(day_dir, "day%d.json" % day)
        if not os.path.exists(p):
            continue
        try:
            with open(p, encoding="utf-8") as f:
                d = json.load(f)
        except Exception:
            continue
        for c in d.get("customers", []):
            cid = c.get("customerId")
            if cid is not None and c.get("correctResult"):
                out[int(cid)] = c["correctResult"]
    return out


def load_schedule_join():
    """day_schedule + customer 시트로 (day, slot) -> {customerId, nameKr, characterType, validChance} 맵.
    correctResult 는 빌드된 dayN.json(권위 소스)에서 읽고, 없으면 스케줄 근사값으로 폴백.
    """
    with open(SOURCE_JSON, encoding="utf-8") as f:
        src = json.load(f)
    sheets = src["sheets"]

    cust_by_id = {}
    for r in sheets["customer"]["rows"]:
        cid = int(r["customer_id"])
        cust_by_id[cid] = {
            "nameKr": _norm(r.get("name_kr")),
            "characterType": _norm(r.get("character_type")),
        }

    day_correct = load_day_correct_results()

    join = {}
    for r in sheets["day_schedule"]["rows"]:
        day = int(r["day"])
        slot = int(r["slot"])
        cid = int(r["customer_id"])
        vc = r.get("valid_chance")
        try:
            vc = float(vc)
        except (TypeError, ValueError):
            vc = None
        c = cust_by_id.get(cid, {})
        ctype = c.get("characterType", "")
        # 권위 소스: 빌드된 dayN.json 의 correctResult. 없으면 스케줄 근사(참고용).
        correct = day_correct.get(cid)
        if not correct:
            is_normal = (vc is None) or (vc >= 1.0)
            correct = "정상 거절" if ctype == "범죄자(성형수술)" else (
                "정상 승인" if is_normal else "정상 거절")
        join[(day, slot)] = {
            "customerId": cid,
            "nameKr": c.get("nameKr", ""),
            "characterType": ctype,
            "validChance": vc,
            "correctResult": correct,
        }
    return join


def parse_canon():
    """캐논 xlsx 의 Day 01~14 시트를 파싱해 두 결과를 돌려준다.

    반환:
      result  : (day, slot) -> {entry, approve, reject, source}
      greet_pool : source 이름 -> 그 source 가 어느 날이든 말한 입장 인사 변주 모음
                   각 항목 {text, day, slot}. 같은 source(클론 포함)의 인사 충돌을
                   다른 변주로 재배정할 때 쓴다.
    읽기 전용(read_only)으로 열어 Excel 잠금 중에도 read-share 로 접근.
    """
    wb = openpyxl.load_workbook(CANON_XLSX, read_only=True, data_only=True)
    result = {}
    greet_pool = {}   # source -> [{"text", "day", "slot"}...] (등장 순서 보존, 결정론)
    for day in range(1, 15):
        sheet_name = "Day %02d" % day
        if sheet_name not in wb.sheetnames:
            continue
        ws = wb[sheet_name]
        cur_slot = None
        cur_src = None
        for row in ws.iter_rows(values_only=True):
            cells = list(row) if row else []
            a = _norm(cells[0]) if len(cells) > 0 else ""
            # 블록 헤더 감지(A열에 들어있다)
            m = _HEADER.search(a) if a else None
            if m:
                cur_slot = int(m.group(1))
                cur_src = _source_name(m.group(2))
                result.setdefault((day, cur_slot),
                                  {"entry": [], "approve": "", "reject": "", "source": cur_src})
                continue
            if cur_slot is None:
                continue
            # 데이터 행: A=순서, B=단계, C=캐릭터, G=결과, H=대사
            step = _norm(cells[1]) if len(cells) > 1 else ""
            who = _norm(cells[2]) if len(cells) > 2 else ""
            res = _norm(cells[6]) if len(cells) > 6 else ""
            text = cells[7] if len(cells) > 7 else None

            if who != "방문객":
                continue  # 검문관 라인·시스템 라인은 무시(게임 심사관 라인 유지)

            quote = _first_quote(text)
            entry = result[(day, cur_slot)]
            if step == "인삿말":
                if quote:
                    entry["entry"].append(quote)
                    # source 별 인사 변주 풀에 적재(중복 텍스트는 한 번만, 등장 순서 보존).
                    pool = greet_pool.setdefault(cur_src, [])
                    if all(p["text"] != quote for p in pool):
                        pool.append({"text": quote, "day": day, "slot": cur_slot})
            elif step == "방문객반응":
                if quote:
                    if res == "허가":
                        if not entry["approve"]:
                            entry["approve"] = quote
                    elif res == "거부":
                        if not entry["reject"]:
                            entry["reject"] = quote
    wb.close()
    return result, greet_pool


def diversify_same_day_greetings(out, greet_pool):
    """같은 날 입장 인사(entry[0]) 충돌을 source 풀의 다른 변주로 재배정(결정론).

    규칙(작업 지시 준수):
      - 입장 인사만 대상(허가/거부 반응은 손대지 않는다).
      - 하루를 slot 오름차순으로 본다. 먼저 본 슬롯이 인사를 '선점'하고,
        뒤 슬롯이 같은 인사를 쓰면 그 캐릭터를 자기 source 풀의 '미사용 변주'로 재배정.
        → 변주 적은 source(예: 박철수)는 선점(유지)되고, 변주 많은 source(예: 한만수)가 양보.
      - 재배정 후보는 그 캐릭터 source 풀의 변주를 '텍스트 정렬'한 뒤 '그 날 아직 안 쓰인
        첫 변주'(결정론: 등장 순서가 아니라 정렬 기준이라 입력 행 순서에 둔감).
        source 풀에 미사용 변주가 없으면(변주 1개뿐 등) 불가피 → 유지하고 사유 기록.
    반환: 재배정 로그 리스트 [(day, slot, cid, nameKr, before, after_or_None)].
    """
    # day -> [(slot, cid)] (slot 오름차순)
    by_day = {}
    for cid_str, v in out.items():
        by_day.setdefault(v["day"], []).append((v["slot"], cid_str))
    for day in by_day:
        by_day[day].sort()

    log = []
    for day in sorted(by_day):
        used = set()   # 그 날 확정된 인사 텍스트들
        for slot, cid_str in by_day[day]:
            v = out[cid_str]
            entry = v["entry"]
            if not entry:
                continue
            greet = entry[0]
            if greet not in used:
                used.add(greet)
                continue
            # 충돌: 이 캐릭터를 자기 source 풀의 미사용 변주로 재배정 시도.
            # 결정론: 변주를 텍스트 정렬한 뒤 그 날 아직 안 쓰인 첫 변주를 고른다
            # (등장 순서가 아니라 정렬 기준 → 입력 행 순서 흔들림에 둔감).
            src = v.get("_source") or ""
            cand_texts = sorted({p["text"] for p in greet_pool.get(src, [])})
            alt = next((t for t in cand_texts if t not in used), None)
            if alt is not None and alt != greet:
                entry[0] = alt
                used.add(alt)
                v.setdefault("todo", [])
                log.append((day, slot, int(cid_str), v["nameKr"], greet, alt))
            else:
                # 미사용 변주 없음(source 변주 1개뿐 등) → 불가피한 중복으로 유지.
                used.add(greet)
                log.append((day, slot, int(cid_str), v["nameKr"], greet, None))
    return log


def build():
    join = load_schedule_join()
    canon, greet_pool = parse_canon()

    out = {}
    missing_join = []
    for (day, slot), info in sorted(join.items()):
        cid = info["customerId"]
        cdat = canon.get((day, slot))
        entry_lines = []
        approve = ""
        reject = ""
        source = ""
        todo = []
        if cdat is None:
            missing_join.append((day, slot, cid))
            todo.append("캐논 블록 없음")
        else:
            entry_lines = list(cdat["entry"])
            approve = cdat["approve"]
            reject = cdat["reject"]
            source = cdat.get("source", "")
            if not entry_lines:
                todo.append("인삿말 없음")
            if not approve:
                todo.append("허가 반응 없음")
            if not reject:
                todo.append("거부 반응 없음")

        out[str(cid)] = {
            "day": day,
            "slot": slot,
            "nameKr": info["nameKr"],
            "characterType": info["characterType"],
            "correctResult": info["correctResult"],
            "entry": entry_lines,
            "approveReaction": approve,
            "rejectReaction": reject,
            "todo": todo,
            # 내부용 source(인사 변주 풀 조회용). 출력 직전에 제거(스키마 불변).
            "_source": source,
        }

    # 같은 날 입장 인사 충돌 다양화(결정론). entry[0] 만 손댄다.
    diversify_log = diversify_same_day_greetings(out, greet_pool)

    # 내부 전용 키(_source) 제거 — 영속 스키마는 기존과 동일.
    for v in out.values():
        v.pop("_source", None)

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    # 리포트
    n = len(out)
    no_entry = sum(1 for v in out.values() if not v["entry"])
    no_app = sum(1 for v in out.values() if not v["approveReaction"])
    no_rej = sum(1 for v in out.values() if not v["rejectReaction"])
    print("[extract_character_dialogue] entries=%d -> %s" % (n, OUT_PATH))
    print("  결측: 인삿말 %d / 허가반응 %d / 거부반응 %d" % (no_entry, no_app, no_rej))
    if missing_join:
        print("  캐논 블록 누락 (day,slot,cid):", missing_join)
    # 인사 충돌 다양화 결과
    reassigned = [r for r in diversify_log if r[5] is not None]
    forced = [r for r in diversify_log if r[5] is None]
    print("  인사 충돌 다양화: 재배정 %d / 불가피 유지 %d" % (len(reassigned), len(forced)))
    for day, slot, cid, nm, before, after in reassigned:
        print("    [재배정] day%d slot%d cid%d %s: %r -> %r" % (day, slot, cid, nm, before, after))
    for day, slot, cid, nm, before, _ in forced:
        print("    [불가피 유지] day%d slot%d cid%d %s: %r (source 변주 1개)" % (day, slot, cid, nm, before))
    return out


if __name__ == "__main__":
    build()
