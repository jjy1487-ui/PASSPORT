# -*- coding: utf-8 -*-
"""
build_interrogation.py — 대본 '취조 질문 스크립트' 시트 → interrogation.json (4-A 대화 라운드 데이터)

소스: Downloads/dayeon_data/방문객_스크립트_전체_260604.xlsx '취조 질문 스크립트' 시트
출력: Assets/Resources/GameData/interrogation.json

규약: SHARED-CONVENTIONS 4-A(대화 라운드 & 재심사 루프), 3.6(inspection_notice), 3.8(속성 키 어휘).
설계:
  - 취조는 심사 라운드 내 '선택적 서브 시퀀스'. errorType 별로 검문관 질문 1개 + 답변유형(시인/부인/회피) 3분기.
  - errorType 은 inspection_notice.error_type 어휘로 정규화(기간오류→기간만료). violationField 는 속성 키 어휘(규약 3.8).
  - '결과 판정'(거부 근거 확보/추가 검토 필요/의심 강화)이 4-A 라운드 전이 신호. 상태머신은 게임플레이 소관.
  - 임의 생성 금지: 빈칸/원문 그대로 보존. idempotent(같은 입력 -> 같은 JSON).

실행: PYTHONUTF8=1 python build_interrogation.py
"""
import os, re, json
import openpyxl

SCRIPT_XLSX = r"C:\Users\chris\Downloads\dayeon_data\방문객_스크립트_전체_260604.xlsx"
OUT_DIR     = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData"
SHEET       = "취조 질문 스크립트"

# 대본 오류유형 -> inspection_notice.error_type 어휘(규약 3.6) + 속성 키(규약 3.8) 정규화.
# 단일 매핑 지점(규약 6장). 새 오류유형이 오면 여기만 고친다.
ERROR_TYPE_NORM = {
    "정보불일치": ("정보불일치", "name"),
    "사진불일치": ("사진불일치", "face"),
    "기간오류":   ("기간만료",   "expiry_date"),
    "위조":       ("위조",       "passport_no"),
}

# 답변유형 표기 정규화: '시인 (기본)' -> '시인' 등.
ANSWER_TYPE_NORM = {"시인": "시인", "부인": "부인", "회피": "회피"}


def cell(v):
    return "" if v is None else str(v).strip()


def norm_error_type(raw):
    """'정보불일치\\n(이름/성별/생년월일/국적)' 같은 셀에서 (errorType, violationField) 추출."""
    base = cell(raw).split("\n", 1)[0].strip()
    # '정보불일치 / (...)' 변형도 흡수
    base = base.split("/", 1)[0].strip()
    base = re.sub(r"\s+", "", base)
    for k, (et, vf) in ERROR_TYPE_NORM.items():
        if base.startswith(k):
            return et, vf
    return base, ""


def norm_answer_type(raw):
    s = cell(raw)
    for k in ANSWER_TYPE_NORM:
        if k in s:
            return k
    return s


def build_interrogation():
    wb = openpyxl.load_workbook(SCRIPT_XLSX, data_only=True, read_only=True)
    ws = wb[SHEET]
    rows = list(ws.iter_rows(values_only=True))
    wb.close()

    entries = []
    cur = None  # 현재 errorType 블록(질문 셀이 채워진 행이 새 블록 시작)
    for row in rows[1:]:  # row0 = 컬럼 헤더
        if not any(c is not None for c in row):
            continue
        c_err   = cell(row[0]) if len(row) > 0 else ""
        c_q     = cell(row[2]) if len(row) > 2 else ""   # 검문관 질문 (5단계)
        c_atype = cell(row[3]) if len(row) > 3 else ""   # 답변 유형
        c_ans   = cell(row[4]) if len(row) > 4 else ""   # 방문객 답변 (5-1단계)
        c_out   = cell(row[5]) if len(row) > 5 else ""   # 결과 판정
        c_note  = cell(row[6]) if len(row) > 6 else ""   # 비고

        # 검문관 질문이 채워진 행 = 새 오류유형 블록 시작.
        if c_q:
            et, vf = norm_error_type(row[0])
            cur = {
                "errorType": et,
                "violationField": vf,
                "target": cell(row[1]) if len(row) > 1 else "방문객",
                "inspectorQuestion": c_q,
                "answers": [],
            }
            entries.append(cur)

        # 답변 행(답변유형/답변이 있는 행)을 현재 블록에 추가.
        if cur is not None and (c_atype or c_ans):
            cur["answers"].append({
                "answerType": norm_answer_type(c_atype),
                "visitorAnswer": c_ans,
                "outcome": c_out,
                "note": c_note,
            })

    return {
        "source": "방문객_스크립트_전체_260604.xlsx / 취조 질문 스크립트",
        "model": "4-A 대화 라운드의 선택적 취조 서브 시퀀스. answerType(시인/부인/회피)이 라운드 전이 신호.",
        "joinsInspectionNotice": "errorType/violationField 로 inspection_notice 와 1:1 정렬(거부 근거 확보 시 citation 후보).",
        "entries": entries,
    }


def main():
    obj = build_interrogation()
    path = os.path.join(OUT_DIR, "interrogation.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)
    print("wrote %s (%d errorType blocks)" % (path, len(obj["entries"])))


if __name__ == "__main__":
    main()
