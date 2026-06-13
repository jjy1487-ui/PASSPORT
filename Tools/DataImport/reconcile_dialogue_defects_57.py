# -*- coding: utf-8 -*-
"""
reconcile_dialogue_defects_57.py

설계문서 data/대사_스크립트.xlsx (시트 Day5/Day6/Day7) 의 손님 '불량' 블록 대사 흐름을
결함배분표(권위)에 맞게 교체한다. 인삿말/정상 블록은 유지하고, 지정 손님의 불량 블록만
새 결함 흐름(상태라벨 E + 서류대조 분기 F/대사 H + 입국거부 정답 H)으로 교체한다.

레이아웃: A=일차 B=순서 C=방문객 D=유형 E=서류상태 F=분기 G=화자 H=대사
손님 블록 = C(이름)+E(상태) 채워진 헤더행 ~ 다음 빈 구분행 직전.

멱등(idempotent): 이미 새 흐름이면 변경 없음. 같은 입력 -> 같은 결과.

xlwings 로 열린 워크북을 우선 사용(파일이 Excel 에서 열려 있을 수 있음).
"""
import os
import sys

XLSX_NAME = "대사_스크립트.xlsx"
XLSX_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "data", XLSX_NAME))


def get_workbook():
    """열린 워크북을 찾고, 없으면 연다. (wb, app, opened_here) 반환."""
    import xlwings as xw
    for a in xw.apps:
        for b in a.books:
            if b.name == XLSX_NAME:
                return b, a, False
    app = xw.App(visible=False)
    wb = app.books.open(XLSX_PATH)
    return wb, app, True


def read_grid(sh):
    """used_range 를 2D 리스트(1-based 행렬 모사, [r][c], r/c 0-based)로 반환."""
    vals = sh.used_range.value
    grid = []
    for row in vals:
        grid.append(["" if c is None else c for c in row])
    return grid


def cell_str(grid, r, c):
    """0-based (r,c) 셀 문자열."""
    if r < 0 or r >= len(grid):
        return ""
    row = grid[r]
    if c < 0 or c >= len(row):
        return ""
    v = row[c]
    return "" if v is None else str(v)


# 컬럼 인덱스 (0-based)
A, B, C, D, E, F, G, H = 0, 1, 2, 3, 4, 5, 6, 7


def find_blocks(grid):
    """헤더행(C 채워짐)마다 (start_row0, end_row0_exclusive) 블록 경계 목록 반환.
    블록은 헤더행부터 다음 헤더행(또는 데이터 끝) 직전까지."""
    header_rows = []
    for r in range(2, len(grid)):  # r0=0 타이틀, r1=헤더라벨, 데이터는 r2부터
        if cell_str(grid, r, C).strip() != "":
            header_rows.append(r)
    blocks = []
    for i, hr in enumerate(header_rows):
        end = header_rows[i + 1] if i + 1 < len(header_rows) else len(grid)
        blocks.append((hr, end))
    return blocks


def find_block(grid, name, status_substr):
    """이름(C) 일치 + 상태(E) 가 status_substr 포함인 블록 (start,end) 반환. 없으면 None."""
    for (s, e) in find_blocks(grid):
        nm = cell_str(grid, s, C).strip()
        st = cell_str(grid, s, E).strip()
        if nm == name and status_substr in st:
            return (s, e)
    return None


def find_branch_row(grid, start, end, branch_substr):
    """블록 [start,end) 안에서 F(분기) 가 branch_substr 포함인 첫 행 r0 반환. 없으면 None."""
    for r in range(start, end):
        if branch_substr in cell_str(grid, r, F):
            return r
    return None


# ─────────────────────────────────────────────────────────────────────────────
# 셀 기록 헬퍼: xlwings 1-based (row, col). col: A=1..H=8
# ─────────────────────────────────────────────────────────────────────────────

def set_cell(sh, r0, c0, value):
    sh.range((r0 + 1, c0 + 1)).value = value


def log(msg):
    print(msg)


# ─────────────────────────────────────────────────────────────────────────────
# 결함 흐름 정의 (각 손님 불량 블록 교체 사양)
#   각 사양은 그 손님의 '기존 불량 블록'을 찾기 위한 (name, old_status_substr) +
#   교체할 새 상태라벨(E) + 새 흐름 라인들(상대 인덱스로 기존 블록 구조에 매핑)
# 실제 교체는 함수별로 블록 구조를 직접 다룬다(블록마다 행 구성이 달라서).
# ─────────────────────────────────────────────────────────────────────────────


def edited_marker(grid, start, end, branch_label, dialogue_substr):
    """이미 새 흐름으로 교체됐는지(멱등) 판정: 지정 분기 라벨 + 대사 일부가 존재하면 True."""
    br = find_branch_row(grid, start, end, branch_label)
    if br is None:
        return False
    if dialogue_substr and dialogue_substr not in cell_str(grid, br, H):
        return False
    return True


def main():
    wb, app, opened_here = get_workbook()
    try:
        changes = []  # (sheet, name, before_status, after_status, before_reject, after_reject)

        # ===================== Day5 =====================
        sh5 = wb.sheets["Day5"]
        g5 = read_grid(sh5)

        # 1) 조지호 불량: PCR 미제출 -> 여권번호 발급국 불일치 (패턴=Day2 데이비드 스미스)
        do_jiho(sh5, g5, changes)

        # 2) 윤건우 불량: 지문 생년월일 불일치 -> 지문 도용(이름) (패턴=Day3 윤서린, 수배 아님)
        g5 = read_grid(sh5)  # 재독(앞 편집 반영)
        do_gunwoo(sh5, g5, changes)

        # 3) 리 나 불량: PCR 검사결과 양성 -> PCR 검사결과 누락
        g5 = read_grid(sh5)
        do_rina(sh5, g5, changes)

        # 4) 로스터 순서 스왑: 장우진(2) <-> 임도현(5)
        g5 = read_grid(sh5)
        do_swap_order(sh5, g5, changes)

        # ===================== Day6 =====================
        sh6 = wb.sheets["Day6"]
        g6 = read_grid(sh6)

        # 5) 오은우 불량: PCR 유효기한 만료 -> 여권 성별 불일치 (패턴=Day2 송하늘)
        do_eunwoo(sh6, g6, changes)

        # 6) 마이클 데이비스 불량: PCR 검사결과 양성 -> PCR 검사기관 위조 (패턴=Day7 스즈키)
        g6 = read_grid(sh6)
        do_michael(sh6, g6, changes)

        # 7) 다니엘 테일러 불량: PCR 국적 불일치 -> 이름 불일치(여권≠비자)
        g6 = read_grid(sh6)
        do_daniel(sh6, g6, changes)

        # 8) 사토 유토 불량: PCR 이름 불일치 -> 여권 사진 불일치 (패턴=Day1 윌리엄)
        g6 = read_grid(sh6)
        do_sato(sh6, g6, changes)

        # ===================== Day7 =====================
        sh7 = wb.sheets["Day7"]
        g7 = read_grid(sh7)

        # 9) 신유준 불량: PCR 검사일 오류 -> 여권 만료일 경과 (패턴=Day2 한만수)
        do_yujun(sh7, g7, changes)

        # 10) 왕 팡 불량: PCR 검사번호 변조 -> 비자종류 거짓(진술) (패턴=Day4 리강)
        g7 = read_grid(sh7)
        do_wangfang(sh7, g7, changes)

        wb.save()

        # ── 보고 ──
        log("")
        log("===== 변경 요약 (before -> after) =====")
        for c in changes:
            log(c)

    finally:
        if opened_here:
            wb.close()
            app.quit()


# ─────────────────────────────────────────────────────────────────────────────
# 개별 손님 교체 함수
# 각 함수는: 기존 불량 블록을 (name, old_status_substr)로 찾고,
# E(상태라벨)·해당 분기행 F/H·입국거부 정답 H 만 교체. 인삿말/정상블록·심사관 입장 라인은 유지.
# 멱등: 새 상태라벨이 이미 있으면 skip.
# ─────────────────────────────────────────────────────────────────────────────

NEW = {}


def _block_status(grid, start):
    return cell_str(grid, start, E).strip()


def do_jiho(sh, g, changes):
    name = "조지호"
    new_status = "불량(여권번호 발급국 불일치)"
    # 이미 바뀐 블록이 있으면 skip
    if find_block(g, name, "발급국 불일치"):
        changes.append(("Day5", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 미제출")
    if blk is None:
        log("[WARN] Day5 조지호 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 인삿말 손님 라인(헤더행 H) 교체: 여권번호 결함 톤(진상)
    set_cell(sh, s, H, "여기 여권이요. 빨리 좀 부탁해요.")
    # 인삿말 심사관 라인(s+1): 방역일이라 PCR 요구 유지(다른 day5 블록과 동일).
    # 인삿말 손님 반응(s+2): PCR 미지참 잔여 대사 -> 진상 톤 일반 반응으로 교체
    set_cell(sh, s + 2, H, "여기 다 냈잖아요. 뭘 또 봐요?")
    # 분기행: '규정 대조(음성기록 ↔ 규정집)' -> '서류 대조(여권번호 ↔ 발급국)'
    br = find_branch_row(g, s, e, "규정 대조")
    if br is None:
        br = find_branch_row(g, s, e, "음성기록")
    set_cell(sh, br, F, "서류 대조(여권번호 ↔ 발급국)")
    set_cell(sh, br, H, "여권번호를 확인하겠습니다… 앞자리가 국적(발급국)과 맞지 않는데요?")
    # 분기 다음 손님 반응(br+1): PCR 잔여 대사 -> 결함 부인 톤으로 교체
    set_cell(sh, br + 1, H, "네? 그럴 리가요. 다시 확인해 봐요.")
    # 그 다음 심사관 설명(br+2) 교체
    set_cell(sh, br + 2, H, "여권번호 앞자리는 발급국과 일치해야 입국이 가능합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.")
    # 거부 후 손님 라인(rj+1): PCR 잔여 대사 -> 진상 톤 항의로 교체
    set_cell(sh, rj + 1, H, "에이 진짜, 그게 뭐가 문제예요!")
    changes.append(("Day5", name, before, new_status,
                    "여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.", ""))


def do_gunwoo(sh, g, changes):
    name = "윤건우"
    new_status = "불량(지문 도용 - 이름)"
    if find_block(g, name, "지문 도용"):
        changes.append(("Day5", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "지문 생년월일 불일치")
    if blk is None:
        log("[WARN] Day5 윤건우 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 지문 대조 분기행: 신원 생년월일 불일치 -> 신원(이름) 불일치
    br = find_branch_row(g, s, e, "지문 대조")
    set_cell(sh, br, H, "지문 조회 결과… 등록된 신원이 여권 이름과 일치하지 않습니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "지문 신원이 여권과 일치하지 않습니다. 입국하실 수 없습니다.")
    changes.append(("Day5", name, before, new_status,
                    "지문 신원이 여권과 일치하지 않습니다. 입국하실 수 없습니다.", ""))


def do_rina(sh, g, changes):
    name = "리 나"
    new_status = "불량(PCR 검사결과 누락)"
    if find_block(g, name, "검사결과 누락"):
        changes.append(("Day5", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 검사결과 양성")
    if blk is None:
        log("[WARN] Day5 리 나 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR 검사서 ↔ 규정집): 양성 -> 결과 비어 있음
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "서류 대조(PCR 검사서 ↔ 규정집)")
    set_cell(sh, br, H, "PCR 검사서를 확인합니다… 검사 결과가 비어 있는데요?")
    # 분기 다음 심사관 설명(br+2): 양성 거부 -> 결과 미확인 거부
    set_cell(sh, br + 2, H, "검사 결과가 확인되지 않으면 입국이 제한됩니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "PCR 검사 결과가 확인되지 않아 입국하실 수 없습니다.")
    changes.append(("Day5", name, before, new_status,
                    "PCR 검사 결과가 확인되지 않아 입국하실 수 없습니다.", ""))


def do_swap_order(sh, g, changes):
    """장우진(현 2) <-> 임도현(현 5) B열 순서값 스왑. 둘 다 정상, 대사 유지."""
    blk_jang = find_block(g, "장우진", "정상")
    blk_im = find_block(g, "임도현", "정상")
    if blk_jang is None or blk_im is None:
        log("[WARN] Day5 장우진/임도현 정상 블록을 못 찾음")
        return
    sj, _ = blk_jang
    si, _ = blk_im
    bj = cell_str(g, sj, B).strip()
    bi = cell_str(g, si, B).strip()
    # 멱등: 이미 임도현=2, 장우진=5면 skip
    if bj == "5" and bi == "2":
        changes.append(("Day5", "장우진<->임도현 순서스왑", "(이미 적용됨)", "장우진=5, 임도현=2", "-", "-"))
        return

    def to_num(x):
        try:
            f = float(x)
            return int(f) if f == int(f) else f
        except Exception:
            return x

    set_cell(sh, sj, B, to_num("5"))
    set_cell(sh, si, B, to_num("2"))
    changes.append(("Day5", "장우진<->임도현 순서스왑",
                    "장우진=%s, 임도현=%s" % (bj, bi), "장우진=5, 임도현=2", "-", "-"))


def do_eunwoo(sh, g, changes):
    name = "오은우"
    new_status = "불량(여권 성별 불일치)"
    if find_block(g, name, "여권 성별 불일치"):
        changes.append(("Day6", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 유효기한 만료")
    if blk is None:
        log("[WARN] Day6 오은우 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR ↔ 오늘 날짜) -> 서류 대조(여권 성별 ↔ 본인)
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "서류 대조(여권 성별 ↔ 본인)")
    set_cell(sh, br, H, "여권 정보를 확인하겠습니다… 성별이 본인과 다른데요?")
    # 분기 다음 심사관 설명(br+2): 유효검사서 -> 성별 일치 요구
    set_cell(sh, br + 2, H, "성별이 본인과 일치해야 입국이 가능합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.")
    changes.append(("Day6", name, before, new_status,
                    "여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.", ""))


def do_michael(sh, g, changes):
    name = "마이클 데이비스"
    new_status = "불량(PCR 검사기관 위조)"
    if find_block(g, name, "검사기관 위조"):
        changes.append(("Day6", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 검사결과 양성")
    if blk is None:
        log("[WARN] Day6 마이클 데이비스 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR 검사서 ↔ 규정집): 양성 -> 검사기관 미인증 (패턴=Day7 스즈키 톤)
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "서류 대조(PCR 검사서 ↔ 인증기관)")
    set_cell(sh, br, H, "PCR 검사서를 확인합니다… 검사 기관이 인증되지 않은 곳인데요?")
    # 분기 다음 심사관 설명(br+2): 양성 거부 -> 미인증 기관 제한
    set_cell(sh, br + 2, H, "인증되지 않은 검사 기관의 검사서는 인정되지 않습니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "인증되지 않은 검사 기관의 검사서로는 입국하실 수 없습니다.")
    changes.append(("Day6", name, before, new_status,
                    "인증되지 않은 검사 기관의 검사서로는 입국하실 수 없습니다.", ""))


def do_daniel(sh, g, changes):
    name = "다니엘 테일러"
    new_status = "불량(이름 불일치(여권≠비자))"
    if find_block(g, name, "이름 불일치"):
        changes.append(("Day6", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 국적 불일치")
    if blk is None:
        log("[WARN] Day6 다니엘 테일러 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR ↔ 여권) -> 서류 대조(여권 ↔ 비자)
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "서류 대조(여권 ↔ 비자)")
    set_cell(sh, br, H, "여권과 비자를 대조합니다… 영문 이름이 서로 다른데요?")
    # 분기 다음 심사관 설명(br+2): 검사서 정보 일치 -> 이름 일치 요구
    set_cell(sh, br + 2, H, "여권과 비자의 이름이 일치해야 입국이 가능합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.")
    changes.append(("Day6", name, before, new_status,
                    "비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.", ""))


def do_sato(sh, g, changes):
    name = "사토 유토"
    new_status = "불량(여권 사진 불일치)"
    if find_block(g, name, "여권 사진 불일치"):
        changes.append(("Day6", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 이름 불일치")
    if blk is None:
        log("[WARN] Day6 사토 유토 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR ↔ 여권) -> 사진 대조(여권 사진 ↔ 얼굴) (패턴=Day1 윌리엄)
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "사진 대조(여권 사진 ↔ 얼굴)")
    set_cell(sh, br, H, "여권 사진과 얼굴을 대조하겠습니다… 사진과 본인 외모가 일치하지 않습니다.")
    # 분기 다음 심사관 설명(br+2): 검사서 본인 -> 사진 일치 요구
    set_cell(sh, br + 2, H, "여권 사진과 본인이 일치해야 입국이 가능합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "여권 본인 확인이 되지 않아 입국하실 수 없습니다.")
    changes.append(("Day6", name, before, new_status,
                    "여권 본인 확인이 되지 않아 입국하실 수 없습니다.", ""))


def do_yujun(sh, g, changes):
    name = "신유준"
    new_status = "불량(여권 만료일 경과)"
    if find_block(g, name, "여권 만료일 경과"):
        changes.append(("Day7", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 검사일 오류")
    if blk is None:
        log("[WARN] Day7 신유준 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 서류 대조(PCR ↔ 오늘 날짜) -> 서류 대조(여권 만료일 ↔ 오늘 날짜)
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "서류 대조(여권 만료일 ↔ 오늘 날짜)")
    set_cell(sh, br, H, "여권 유효기간을 확인하니 만료일이 지났는데요?")
    # 분기 다음 심사관 설명(br+2): 검사일 정확 -> 유효기간 지난 여권 불가
    set_cell(sh, br + 2, H, "유효기간이 지난 여권으로는 입국이 불가합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "여권 유효기간이 지나 입국하실 수 없습니다.")
    changes.append(("Day7", name, before, new_status,
                    "여권 유효기간이 지나 입국하실 수 없습니다.", ""))


def do_wangfang(sh, g, changes):
    name = "왕 팡"
    new_status = "불량(비자종류 거짓 - 진술)"
    if find_block(g, name, "비자종류 거짓"):
        changes.append(("Day7", name, "(이미 적용됨)", new_status, "-", "-"))
        return
    blk = find_block(g, name, "PCR 검사번호 변조")
    if blk is None:
        log("[WARN] Day7 왕 팡 불량 블록을 못 찾음")
        return
    s, e = blk
    before = _block_status(g, s)
    set_cell(sh, s, E, new_status)
    # 인삿말 블록에 방문목적 진술 추가 (패턴=Day4 리강).
    # 기존 블록 구조: s=인삿말(손님), s+1=서류대조 분기(심사관), ...
    # 리강 패턴: 심사관 "방문 목적이 어떻게 되십니까?" / 손님 "관광하러 왔어요." / 손님 "여권/비자 여기요."
    # 기존 인삿말 손님 라인(s) 은 유지하고, 그 아래 진술 라인을 삽입 대신
    # 헤더행 다음(분기행 br) 앞에 진술 흐름을 만들기 위해 br 행을 음성대조로 바꾸고
    # 그 직전 흐름을 활용. 단순화를 위해 인삿말 손님 라인 H 에 진술을 합치지 않고,
    # 분기 직전에 들어갈 진술은 '서류 대조' 분기를 '음성 대조'로 교체하며 표현한다.

    # 손님 인삿말 라인(s, G=왕 팡) text: 진술 추가
    set_cell(sh, s, H, "你好。我是来旅游观光的。(안녕하세요. 관광하러 왔어요.)")

    # 분기행: '서류 대조(PCR 자체 검증)' -> '음성 대조(음성기록 ↔ 비자)'
    br = find_branch_row(g, s, e, "서류 대조")
    set_cell(sh, br, F, "음성 대조(음성기록 ↔ 비자)")
    set_cell(sh, br, H, "음성기록엔 '관광'이라 하셨는데, 비자의 방문 목적은 '장기 체류'네요?")
    # 분기 다음 심사관 설명(br+2): 검사 번호 -> 진술/비자 일치 요구
    set_cell(sh, br + 2, H, "진술하신 방문 목적이 비자와 일치해야 입국이 가능합니다.")
    # 입국 거부(정답)
    rj = find_branch_row(g, s, e, "입국 거부 (정답)")
    set_cell(sh, rj, H, "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.")
    changes.append(("Day7", name, before, new_status,
                    "진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.", ""))


if __name__ == "__main__":
    main()
