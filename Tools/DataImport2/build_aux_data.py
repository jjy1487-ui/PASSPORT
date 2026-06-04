# -*- coding: utf-8 -*-
"""
dayeon_data 신규 소스(스케줄/스크립트) + 마스터 엑셀을 우리 런타임 보조 JSON으로 변환한다.
출력: Assets/Resources/GameData/ 아래 day1.json 스타일 평이한 JSON.
  - branch_dialogue.json     : 캐릭터 유형별 분기 대사 라이브러리 (방문객_스크립트_전체_260604.xlsx 14개 유형 시트)
  - rejection_templates.json : 거부 대사 템플릿
  - scores.json              : 캐릭터별 분기점 점수표(일반 단순화 + 일반 외 분기)
  - payouts.json             : 캐릭터별 분기별 지급 금액표
  - items.json               : 아이템 인벤토리
실행: PYTHONUTF8=1 python build_aux_data.py
모든 출력은 idempotent(같은 입력 -> 같은 JSON, ensure_ascii=False, 정렬 키 고정).
'미정'/빈칸 값은 가공 없이 그대로 보존한다(임의 생성 금지).
"""
import os, re, json
import openpyxl

SCHED_XLSX  = r"C:\Users\chris\Downloads\dayeon_data\여권주세요_날짜별_방문고객_랜덤정리_3.xlsx"
SCRIPT_XLSX = r"C:\Users\chris\Downloads\dayeon_data\방문객_스크립트_전체_260604.xlsx"
OUT_DIR     = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData"

# 신규 스크립트 결과 표기 -> 기존 day JSON gameResult 어휘 (단일 매핑 지점)
RESULT_MAP = {
    "입국 (정답)": "정상 승인",
    "거부 (정답)": "정상 거절",
    "거부 (오판)": "잘못 거절",
    "입국 (오판)": "잘못 허가",
}
# 화자 표기 -> 기존 day JSON speaker 어휘
SPEAKER_MAP = {
    "방문객": "캐릭터",
    "검문관(플레이어)": "심사관",
    "[시스템]": "시스템",
}

def norm_result(text):
    """분기/결과 문자열에서 표준 gameResult를 추출. 못 찾으면 원문 반환."""
    if not text:
        return ""
    for k, v in RESULT_MAP.items():
        if k in text:
            return v
    # '즉시 입국 (정답)' '3회 거절 후 강제 입국' 등 변형 흡수.
    # 연예인/정치인 'N회 거절 후 입국'·'강제 입국'은 최종적으로 입국=정상 승인.
    if "입국" in text and "정답" in text:
        return "정상 승인"
    if "거절 후" in text and "입국" in text:
        return "정상 승인"
    if "강제 입국" in text:
        return "정상 승인"
    if "거부" in text and "정답" in text:
        return "정상 거절"
    if "거부" in text and "오판" in text:
        return "잘못 거절"
    if "입국" in text and "오판" in text:
        return "잘못 허가"
    return text.strip()

def cell(v):
    return "" if v is None else str(v).strip()

def num(v):
    """'+12' '-9' '+10,000원' 등에서 정수 추출. 실패 시 None."""
    if v is None:
        return None
    s = str(v)
    m = re.search(r"[-+]?\d[\d,]*", s)
    if not m:
        return None
    try:
        return int(m.group(0).replace(",", ""))
    except ValueError:
        return None

# ───────────────────────────────────────────── branch_dialogue.json
# 각 유형 시트: 헤더(순서|캐릭터|서류상태|분기/결과|대사/액션),
# '▶ 분기:...' 또는 '══...══' 가 블록 헤더. 숫자/숫자-숫자 순서행이 대사.
BRANCH_HEADER_RE = re.compile(r"^\s*(▶|══|◆)")

def build_branch_dialogue():
    wb = openpyxl.load_workbook(SCRIPT_XLSX, data_only=True, read_only=True)
    # 대사 라이브러리는 취조/거부 템플릿을 제외한 유형 시트만
    skip = {"취조 질문 스크립트", "거부 대사 템플릿"}
    types = []
    for sheet in wb.sheetnames:
        if sheet in skip:
            continue
        ws = wb[sheet]
        blocks = []
        cur = None
        section = ""  # ══ 구획(연예인/정치인, 사이비 회차 등)
        for r_i, row in enumerate(ws.iter_rows(values_only=True), 1):
            if r_i == 1:
                continue  # 컬럼 헤더
            c0 = cell(row[0]) if len(row) > 0 else ""
            c1 = cell(row[1]) if len(row) > 1 else ""
            c2 = cell(row[2]) if len(row) > 2 else ""
            c3 = cell(row[3]) if len(row) > 3 else ""
            c4 = cell(row[4]) if len(row) > 4 else ""
            joined = " ".join([c0, c1, c2, c3, c4]).strip()
            if not joined:
                continue
            # ══ 구획 헤더 (연예인 ★ / 사이비 1회차 등) — c0에 위치.
            # ▶ 블록을 쓰지 않는 시트는 구획 전환 자체가 새 블록 경계가 된다.
            if c0.startswith("══"):
                section = c0.strip("═ ").strip()
                cur = None  # 다음 대사행에서 이 구획으로 새 블록 시작
                continue
            # ▶ 분기 블록 헤더
            if c0.startswith("▶"):
                # 블록 라벨 = c0 (+ c1에 결과 텍스트가 이어지기도)
                label = (c0 + (" " + c1 if c1 else "")).strip()
                cur = {
                    "section": section,
                    "branchLabel": label,
                    "gameResult": norm_result(label),
                    "lines": [],
                }
                blocks.append(cur)
                continue
            # 일반 대사/액션 행: c0=순서, c1=화자, c2=서류상태, c3=분기/결과, c4=대사
            # 블록 헤더가 아직 없으면(시트가 ▶없이 시작) 임시 블록 생성
            if cur is None:
                cur = {"section": section, "branchLabel": "",
                       "gameResult": norm_result(c3), "lines": []}
                blocks.append(cur)
            speaker = SPEAKER_MAP.get(c1, c1)
            cur["lines"].append({
                "order": c0,                # '6-1' 등 비정수 포함 -> 문자열 보존
                "speaker": speaker,
                "docState": c2,             # 정상/불량/정상 [1-A] 등
                "result": norm_result(c3),
                "text": c4,                 # 대사 또는 [모션]/[UI] 액션 그대로
            })
        # 빈 블록 제거 + 블록 gameResult가 비면 첫 라인 result로 보강
        blocks = [b for b in blocks if b["lines"]]
        for b in blocks:
            if not b["gameResult"]:
                for ln in b["lines"]:
                    if ln["result"]:
                        b["gameResult"] = ln["result"]
                        break
        types.append({"characterType": sheet, "blocks": blocks})
    wb.close()
    return {"source": "방문객_스크립트_전체_260604.xlsx",
            "resultMapping": RESULT_MAP,
            "types": types}

# ───────────────────────────────────────────── rejection_templates.json
def build_rejection_templates():
    wb = openpyxl.load_workbook(SCRIPT_XLSX, data_only=True, read_only=True)
    ws = wb["거부 대사 템플릿"]
    rows = list(ws.iter_rows(values_only=True))
    items = []
    for row in rows[1:]:
        if not any(cell(c) for c in row):
            continue
        items.append({
            "problemType": cell(row[0]),
            "document":    cell(row[1]),
            "errorField":  cell(row[2]),
            "rejectFirm":  cell(row[3]),
            "rejectPolite":cell(row[4]),
        })
    wb.close()
    return {"source": "방문객_스크립트_전체_260604.xlsx / 거부 대사 템플릿",
            "templates": items}

# ───────────────────────────────────────────── scores.json
def build_scores():
    wb = openpyxl.load_workbook(SCHED_XLSX, data_only=True, read_only=True)
    out = []
    # 단순화 점수표: 캐릭터|서류상태|분기/결과|점수|비고
    ws = wb["캐릭터별 분기점 점수표(단순화)"]
    for row in list(ws.iter_rows(values_only=True))[1:]:
        ch = cell(row[0])
        if not ch:
            continue
        out.append({
            "characterType": ch,
            "docState": cell(row[1]),
            "branch": cell(row[2]),
            "gameResult": norm_result(cell(row[2])),
            "score": num(row[3]),
            "scoreText": cell(row[3]),
            "note": cell(row[4]),
        })
    # 일반 외 분기 점수표: 캐릭터|(분기진행순서 무시)|서류상태|분기/결과|점수|비고
    ws2 = wb["일반 캐릭터 외 분기 접근 및 점수표"]
    for row in list(ws2.iter_rows(values_only=True))[1:]:
        ch = cell(row[0])
        if not ch:
            continue
        out.append({
            "characterType": ch,
            "docState": cell(row[2]),
            "branch": cell(row[3]),
            "gameResult": norm_result(cell(row[3])),
            "score": num(row[4]),
            "scoreText": cell(row[4]),
            "note": cell(row[5]),
        })
    wb.close()
    return {"source": "여권주세요_날짜별_방문고객_랜덤정리_3.xlsx (점수표 2종)",
            "model": "엔딩 점수 = 판정 분기 합산 (A-1). 돈과 분리.",
            "rows": out}

# ───────────────────────────────────────────── payouts.json
def build_payouts():
    wb = openpyxl.load_workbook(SCHED_XLSX, data_only=True, read_only=True)
    ws = wb["캐릭터별 분기별 지급 금액표(1회차기준)"]
    out = []
    for row in list(ws.iter_rows(values_only=True))[1:]:
        ch = cell(row[0])
        if not ch:
            continue
        out.append({
            "characterType": ch,
            "baseTierText": cell(row[1]),
            "baseTier": num(row[1]),
            "docState": cell(row[2]),
            "branch": cell(row[3]),
            "gameResult": norm_result(cell(row[3])),
            "payout": num(row[4]),
            "payoutText": cell(row[4]),
            "formula": cell(row[5]),
            "appearsRound1": cell(row[6]),
            "note": cell(row[7]),
        })
    wb.close()
    return {"source": "여권주세요_날짜별_방문고객_랜덤정리_3.xlsx / 지급 금액표",
            "rows": out}

# ───────────────────────────────────────────── items.json
def build_items():
    wb = openpyxl.load_workbook(SCHED_XLSX, data_only=True, read_only=True)
    ws = wb["아이템 인벤토리"]
    out = []
    for row in list(ws.iter_rows(values_only=True))[1:]:
        if not any(cell(c) for c in row):
            continue
        out.append({
            "category": cell(row[0]),
            "itemName": cell(row[1]),
            "obtainBranch": cell(row[2]),
            "obtainChance": cell(row[3]),
            "ingameValueText": cell(row[4]),   # '미정' 등 텍스트 그대로 보존
            "ingameValue": num(row[4]),         # 숫자면 추출, 아니면 null
            "earlyEnding": cell(row[5]),
            "effect": cell(row[6]),
        })
    wb.close()
    return {"source": "여권주세요_날짜별_방문고객_랜덤정리_3.xlsx / 아이템 인벤토리",
            "note": "'미정' 값은 원본 보존(임의 생성 금지). ingameValue는 숫자일 때만 채움.",
            "items": out}

def write_json(name, obj):
    path = os.path.join(OUT_DIR, name)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)
    print(f"wrote {path}")

def main():
    write_json("branch_dialogue.json",     build_branch_dialogue())
    write_json("rejection_templates.json",  build_rejection_templates())
    write_json("scores.json",               build_scores())
    write_json("payouts.json",              build_payouts())
    write_json("items.json",                build_items())

if __name__ == "__main__":
    main()
