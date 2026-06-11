# -*- coding: utf-8 -*-
"""
방문객 스크립트 문서(xlsx) 자동 생성기 — repo day JSON 단일 소스.

목적
  Assets/Resources/GameData/day1.json ~ day14.json 의 박힌(authored) 대사를 그대로 읽어
  사람이 읽는 일자별 스크립트 문서(xlsx)를 생성한다. 게임(day JSON)과 100% 동기화된 문서.

설계 원칙
  - 진실 소스 = day JSON. 이 도구는 그것을 사람이 읽는 형태로 옮길 뿐, 내용을 만들거나 가리지 않는다.
  - 더미(클론)의 "원본 대사 상속"은 이미 day JSON 에 반영돼 있으므로 그대로 옮긴다.
  - 빈 대사([TODO 대사])는 숨기지 않고 그대로 노출한다.
  - 결정론적: 같은 입력 JSON → 항상 같은 출력. datetime 등 외부 상태 의존 없음(파일명 날짜는 260611 고정).
  - 절대 기존 손글 원본 파일을 덮어쓰지 않는다. 새 이름으로만 출력.

실행
  python Tools/DataImport/build_visitor_script_doc.py
"""

import json
import os
import sys

import openpyxl
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

# ── 경로 (도구 위치 기준 repo 루트 산출 — cwd 비의존) ─────────────────────────
THIS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(THIS_DIR, os.pardir, os.pardir))
JSON_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "GameData")
OUT_PATH = os.path.join(
    os.path.expanduser("~"), "Downloads", "방문객_스크립트_일자별_생성_260611.xlsx"
)

DAYS = list(range(1, 15))
TODO_MARK = "[TODO 대사]"
INSPECTOR = "심사관"  # day JSON 상의 심사관 화자 키

# 컬럼 A~H 헤더
HEADERS = ["순서", "단계", "캐릭터", "이름", "서류상태", "문제유형", "결과", "대사 / 액션"]
NCOL = len(HEADERS)

# ── 캐릭터 유형 → 등장행 약칭 ────────────────────────────────────────────────
# 1단계(등장)의 C(캐릭터역할)에 쓸 짧은 라벨. 없으면 "방문객".
TYPE_ABBR = {
    "일반 고객": "일반",
    "외국인 관광객": "외국인",
    "진상 고객": "진상",
    "성형 수술 고객": "성형",
    "전염병 환자": "환자",
    "특수(연예인)★": "특수",
    "사이비 신도": "신도",
    "밀수꾼": "밀수꾼",
    "위조범": "위조범",
}


# ── 스타일 ──────────────────────────────────────────────────────────────────
THIN = Side(style="thin", color="D0D0D0")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
WRAP = Alignment(wrap_text=True, vertical="top")
WRAP_CENTER = Alignment(wrap_text=True, vertical="center", horizontal="left")

TITLE_FILL = PatternFill("solid", fgColor="2F4858")
TITLE_FONT = Font(bold=True, color="FFFFFF", size=13)

BLOCK_FILL = PatternFill("solid", fgColor="6C8EBF")
BLOCK_FONT = Font(bold=True, color="FFFFFF", size=11)

SUB_FILL = PatternFill("solid", fgColor="DAE8FC")
SUB_FONT = Font(bold=True, color="1F3864", size=10)

HEADER_FILL = PatternFill("solid", fgColor="BDD7EE")
HEADER_FONT = Font(bold=True, size=10)

COMMON_FONT = Font(size=10)
TODO_FONT = Font(size=10, color="C00000", bold=True)


# ── day JSON 로더 ───────────────────────────────────────────────────────────
def load_day(day):
    path = os.path.join(JSON_DIR, f"day{day}.json")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


# ── 추출 헬퍼 ───────────────────────────────────────────────────────────────
def is_dummy(c):
    """이미지 참조(spriteRef)가 이름과 다르면 클론(더미)."""
    return c.get("spriteRef", "") and c.get("spriteRef") != c.get("nameKr")


def _has_violation(v):
    return v not in (None, "", "없음", "-")


def defect_info(c):
    """
    문서들로부터 (서류상태=주 documentType, 문제유형=violation, defect_summary) 추출.
    - violationField 가 채워진 첫 문서의 '{documentType}: {violationField}'.
    - 전부 없음이면:
        correctResult=='정상 승인' → 정상 / violation='없음'
        그 외(거절인데 violation 없음=사진/대조류) → '본인대조' / violation='사진/대조불일치'
    반환: (doc_type_primary, violation, defect_summary)
    """
    docs = c.get("documents", []) or []
    primary_doc = docs[0]["documentType"] if docs else "여권"

    for doc in docs:
        vf = doc.get("violationField")
        if _has_violation(vf):
            summary = f"{doc['documentType']}: {vf}"
            return primary_doc, vf, summary

    # 결함 표기 없음
    if c.get("correctResult") == "정상 승인":
        return primary_doc, "없음", "정상"
    else:
        # 거절인데 서류 violation 이 없음 → 사진/대조류
        return primary_doc, "사진/대조불일치", "본인대조"


def find_case(c, case_type=None, game_result=None, reject_count=None):
    """조건에 맞는 첫 dialogueCase 반환(없으면 None)."""
    for dc in c.get("dialogueCases", []) or []:
        if case_type is not None and dc.get("caseType") != case_type:
            continue
        if game_result is not None and dc.get("gameResult") != game_result:
            continue
        if reject_count is not None and dc.get("rejectCount") != reject_count:
            continue
        return dc
    return None


def correct_label(correct_result):
    """correctResult → 정답 분기 라벨."""
    if correct_result == "정상 승인":
        return "입국 허가 (정답)", "허가"
    if correct_result == "정상 거절":
        return "입국 거부 (정답)", "거부"
    # 방어적 기본값
    return f"{correct_result} (정답)", correct_result


def wrong_label(correct_result):
    """correctResult → 오판 분기 라벨 + 결과 텍스트."""
    if correct_result == "정상 승인":
        return "입국 거부 (오판)", "거부"
    if correct_result == "정상 거절":
        return "입국 허가 (오판)", "허가"
    return f"{correct_result} (오판)", "오판"


def correct_game_result(correct_result):
    """correctResult 와 일치하는 '일반 심사' gameResult 키."""
    # 정상 승인 → 정상 승인, 정상 거절 → 정상 거절
    return correct_result


def wrong_game_result(correct_result):
    """오판 케이스 선택 규칙(스펙)."""
    return {
        "정상 승인": "잘못 거절",
        "정상 거절": "잘못 허가",
        "잘못 허가": "정상 거절",
    }.get(correct_result, "잘못 허가")


def lines_text(dc):
    """dialogueCase 의 lines 를 (speaker, text) 리스트로(순서대로). 없으면 빈 리스트."""
    if not dc:
        return []
    out = []
    for ln in sorted(dc.get("lines", []) or [], key=lambda x: x.get("order", 0)):
        out.append((ln.get("speaker", ""), ln.get("text", "")))
    return out


# ── 시트 쓰기 ───────────────────────────────────────────────────────────────
class Cursor:
    """현재 행 추적 + 셀 쓰기 헬퍼."""

    def __init__(self, ws):
        self.ws = ws
        self.row = 1

    def merged_banner(self, text, fill, font, height=None):
        ws = self.ws
        ws.merge_cells(start_row=self.row, start_column=1, end_row=self.row, end_column=NCOL)
        cell = ws.cell(row=self.row, column=1, value=text)
        cell.fill = fill
        cell.font = font
        cell.alignment = WRAP_CENTER
        if height:
            ws.row_dimensions[self.row].height = height
        self.row += 1

    def data_row(self, values, todo_flag=False):
        """A~H 8칸 데이터행. todo_flag면 H(대사)를 빨강 강조."""
        ws = self.ws
        for ci in range(NCOL):
            v = values[ci] if ci < len(values) else ""
            cell = ws.cell(row=self.row, column=ci + 1, value=v)
            cell.border = BORDER
            cell.alignment = WRAP
            if ci == NCOL - 1 and todo_flag:
                cell.font = TODO_FONT
            else:
                cell.font = COMMON_FONT
        self.row += 1

    def blank(self):
        self.row += 1


def write_block(cur, c):
    """방문객 1명 블록을 시트에 기록."""
    name = c.get("nameKr", "")
    ctype = c.get("characterType", "")
    slot = c.get("slot", "")
    correct = c.get("correctResult", "")
    doc_type, violation, defect_summary = defect_info(c)
    dummy = is_dummy(c)
    abbr = TYPE_ABBR.get(ctype, "방문객")

    # 1) 블록 헤더 (병합 A:H)
    header = f"  [{slot}번째 방문객] {name}  | {ctype} | 서류: {defect_summary}"
    if dummy:
        header += f"  (더미·클론←{c.get('spriteRef','')})"
    cur.merged_banner(header, BLOCK_FILL, BLOCK_FONT, height=20)

    # 2) 공통 진입 서브헤더
    cur.merged_banner("    ── 공통 진입 (1~3단계 고정) ──", SUB_FILL, SUB_FONT)

    # 3) 단계행 5개
    # 1: 등장
    cur.data_row(
        ["1", "등장", abbr, name, doc_type, violation, "공통",
         "[UI 등장] 방문객이 심사대 앞에 선다."]
    )
    # 2: 인삿말 — '입장' dialogueCase 의 lines 를 순서대로(여러 줄이면 행 늘림)
    entry = find_case(c, case_type="입장")
    entry_lines = lines_text(entry)
    if entry_lines:
        first = True
        for spk, txt in entry_lines:
            todo = TODO_MARK in txt
            cur.data_row(
                ["2" if first else "", "인삿말" if first else "", "방문객", name,
                 doc_type if first else "", violation if first else "", "공통", txt],
                todo_flag=todo,
            )
            first = False
    else:
        cur.data_row(["2", "인삿말", "방문객", name, doc_type, violation, "공통", "(입장 대사 없음)"])
    # 3: 서류드롭
    cur.data_row(
        ["3", "서류드롭", "방문객", name, doc_type, violation, "공통", "[모션] 여권 드롭"]
    )
    # 4: 서류검수
    cur.data_row(
        ["4", "서류검수", "검문관(플레이어)", name, doc_type, violation, "공통", "(플레이어) 서류 검수"]
    )
    # 5: 취조(선택)
    cur.data_row(
        ["5", "취조(선택)", "검문관(플레이어)", name, doc_type, violation, "공통", "(선택) 취조"]
    )

    # 4) 분기 A — 정답
    a_label, a_result = correct_label(correct)
    cur.merged_banner(f"    ▶ [분기 A] {a_label}", SUB_FILL, SUB_FONT)
    a_case = find_case(c, case_type="일반 심사", game_result=correct_game_result(correct))
    _write_branch_lines(cur, name, doc_type, violation, a_case, a_result)

    # 5) 분기 B — 오판
    b_label, b_result = wrong_label(correct)
    cur.merged_banner(f"    ▶ [분기 B] {b_label}", SUB_FILL, SUB_FONT)
    b_case = find_case(c, case_type="일반 심사", game_result=wrong_game_result(correct))
    _write_branch_lines(cur, name, doc_type, violation, b_case, b_result)

    # 6) 블록 사이 빈 행
    cur.blank()


def _write_branch_lines(cur, name, doc_type, violation, dc, result_text):
    """분기 케이스의 lines 를 speaker 별로 기록."""
    lines = lines_text(dc)
    if not lines:
        cur.data_row(["", "(대사 없음)", "", name, "", "", result_text, "(해당 케이스 없음)"])
        return
    order = 1
    for spk, txt in lines:
        todo = TODO_MARK in txt
        if spk == INSPECTOR:
            stage, role = "검문관", "검문관(플레이어)"
        else:
            stage, role = "방문객반응", "방문객"
        cur.data_row([str(order), stage, role, name, "", "", result_text, txt], todo_flag=todo)
        order += 1


def build_sheet(wb, day_data, first):
    day = day_data["day"]
    title = f"Day {day:02d}"
    if first:
        ws = wb.active
        ws.title = title
    else:
        ws = wb.create_sheet(title)

    # 컬럼 폭
    widths = [6, 12, 16, 14, 12, 16, 10, 70]
    for i, w in enumerate(widths):
        ws.column_dimensions[get_column_letter(i + 1)].width = w

    cur = Cursor(ws)

    # row1: 헤더
    for ci, h in enumerate(HEADERS):
        cell = ws.cell(row=1, column=ci + 1, value=h)
        cell.fill = HEADER_FILL
        cell.font = HEADER_FONT
        cell.border = BORDER
        cell.alignment = Alignment(horizontal="center", vertical="center")
    cur.row = 2

    # row2: 타이틀(병합)
    cur.merged_banner(f"Day {day} — (자동생성)", TITLE_FILL, TITLE_FONT, height=22)

    # 슬롯 오름차순으로 블록
    customers = sorted(day_data.get("customers", []), key=lambda x: x.get("slot", 0))
    for c in customers:
        write_block(cur, c)

    ws.freeze_panes = "A2"
    return ws


# ── 검증 리포트 ─────────────────────────────────────────────────────────────
def validate(all_days):
    """검증 수치 + Day4/Day5 미리보기 텍스트를 문자열로 반환."""
    out = []
    out.append("=" * 64)
    out.append("검증 리포트 — 방문객 스크립트 문서 자동생성")
    out.append("=" * 64)

    total_days = len(all_days)
    total_slots = 0
    bad_slot_days = []
    todo_cell_count = 0
    todo_type_dist = {}
    dummy_count = 0
    dummy_with_todo = 0  # 더미인데 TODO 가 있는(=상속 안 된 것처럼 보이는) 케이스
    dummy_inherited_ok = 0  # 더미인데 TODO 없이 대사 완비

    EXPECTED_TODO_TYPES = {"성형 수술 고객", "전염병 환자"}
    foreign_todo_types = set()

    for d in all_days:
        custs = d.get("customers", [])
        n = len(custs)
        total_slots += n
        if n != 7:
            bad_slot_days.append((d["day"], n))

        for c in custs:
            ctype = c.get("characterType", "")
            dummy = is_dummy(c)
            if dummy:
                dummy_count += 1

            # 이 손님 전체 대사 라인 중 TODO 세기
            c_has_todo = False
            for dc in c.get("dialogueCases", []) or []:
                for ln in dc.get("lines", []) or []:
                    if TODO_MARK in (ln.get("text") or ""):
                        todo_cell_count += 1
                        c_has_todo = True

            if c_has_todo:
                todo_type_dist[ctype] = todo_type_dist.get(ctype, 0) + 1
                if ctype not in EXPECTED_TODO_TYPES:
                    foreign_todo_types.add(ctype)

            if dummy:
                if c_has_todo:
                    dummy_with_todo += 1
                else:
                    dummy_inherited_ok += 1

    out.append(f"총 일수: {total_days} (기대 14)")
    out.append(f"총 슬롯 수: {total_slots} (기대 98)")
    if bad_slot_days:
        out.append("  [경고] 슬롯이 7이 아닌 날: " +
                   ", ".join(f"Day{dd}={nn}" for dd, nn in bad_slot_days))
    else:
        out.append("  모든 날 7슬롯 정상.")

    out.append("")
    out.append(f"[TODO 대사] 포함 라인(셀) 수: {todo_cell_count}")
    out.append("[TODO]가 있는 캐릭터의 characterType 분포(손님 수 기준):")
    for t, cnt in sorted(todo_type_dist.items(), key=lambda x: -x[1]):
        flag = "" if t in EXPECTED_TODO_TYPES else "  <<< 예상밖!"
        out.append(f"    {t}: {cnt}명{flag}")
    if foreign_todo_types:
        out.append("  [경고] 성형/전염병 외 유형에 TODO 존재: " + ", ".join(sorted(foreign_todo_types)))
    else:
        out.append("  정상: TODO 는 '성형 수술 고객'·'전염병 환자' 두 유형에만 존재.")

    out.append("")
    out.append(f"더미(spriteRef≠nameKr) 캐릭터 수: {dummy_count}")
    out.append(f"  └ TODO 없이 대사 상속 완료: {dummy_inherited_ok}")
    out.append(f"  └ TODO 보유(성형/전염병 클론 등): {dummy_with_todo}")
    if dummy_count:
        ratio = dummy_inherited_ok / dummy_count * 100.0
        out.append(f"  └ 상속 완료 비율: {ratio:.1f}% "
                   f"(성형/전염병 클론 제외 시 100% 기대)")

    # Day4/Day5 미리보기
    for target in (4, 5):
        dd = next((x for x in all_days if x["day"] == target), None)
        if not dd:
            continue
        out.append("")
        out.append(f"── Day {target} 미리보기 (슬롯별) ──")
        for c in sorted(dd["customers"], key=lambda x: x.get("slot", 0)):
            entry = find_case(c, case_type="입장")
            first_line = ""
            el = lines_text(entry)
            if el:
                first_line = el[0][1]
            dummy = is_dummy(c)
            dm = f"더미←{c.get('spriteRef')}" if dummy else "원본"
            out.append(
                f"  s{c['slot']} {c['nameKr']} [{c['characterType']}] ({dm}) "
                f"인삿말: {first_line}"
            )

    out.append("=" * 64)
    return "\n".join(out)


# ── 메인 ────────────────────────────────────────────────────────────────────
def main():
    # 출력 보호: 손글 원본 이름과 절대 겹치지 않는 고정 새 이름.
    assert OUT_PATH.endswith("방문객_스크립트_일자별_생성_260611.xlsx"), "출력 파일명 보호"

    all_days = [load_day(d) for d in DAYS]

    wb = openpyxl.Workbook()
    for i, dd in enumerate(all_days):
        build_sheet(wb, dd, first=(i == 0))

    # 결정론 강화: 문서 메타 타임스탬프를 고정(외부 시각 비의존).
    # day JSON 이 같으면 raw 바이트도 동일하게 재현되도록 한다.
    import datetime
    fixed = datetime.datetime(2026, 6, 11, 0, 0, 0)
    wb.properties.created = fixed
    wb.properties.modified = fixed
    wb.properties.creator = "build_visitor_script_doc"
    wb.properties.lastModifiedBy = "build_visitor_script_doc"

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    wb.save(OUT_PATH)

    print(f"[생성] {OUT_PATH}")
    print()
    print(validate(all_days))

    # 재로드 확인
    print()
    rb = openpyxl.load_workbook(OUT_PATH, read_only=True)
    print(f"[재로드 확인] 시트 {len(rb.sheetnames)}개: {rb.sheetnames}")
    rb.close()


if __name__ == "__main__":
    # 한글 콘솔 출력 보장
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    main()
