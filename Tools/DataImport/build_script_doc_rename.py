# -*- coding: utf-8 -*-
"""
방문객 스크립트 문서(xlsx) 생성기 — "손글 원본 블록을 클론에 상속(이름 변경)" 방식.

목적
  사용자 손글 문서(원본 37명·캐릭터별·날짜별 맥락 대사)를 현재 98명(14일×7슬롯) 스케줄에
  맞춰 재배치한다. 새 대사를 한 줄도 쓰지 않고, 각 새 캐릭터가 자기 "source 원본"의
  손글 블록을 그대로 가져와 **이름만 바꾼다.**

통합 규칙
  - source 결정: customerId <= 37 → 자기 자신(원본). customerId >= 38 → 클론이므로
    customer.sprite_ref(이미지 참조)가 가리키는 원본 이름.
  - 98자리 중 대부분은 source 원본이 손글 문서의 **같은 날·같은 슬롯 위치**에 있어 그 자리에서
    이름만 바꾸면 된다(same-position). 소수만 source 블록이 다른 위치에 있다(cross-slot).
  - 손글 블록의 다국어/맥락 대사는 그대로 보존하고, D열(이름)·블록 헤더의 이름만 정확 치환한다.

설계 원칙
  - 손글 원본 파일은 절대 수정하지 않는다(읽기 전용). 새 이름으로만 출력(출력경로 assert 가드).
  - 결정론적: 같은 입력 → 같은 출력. datetime 의존 없음(파일명 260611 고정).
  - 슬롯당 블록 1개(변형 2개면 새 캐릭터 correctResult 로 1개 선택).
  - 비-Day 시트(풀_*·비교대조)는 그대로 복사해 자립형 문서로 만든다.

실행
  python Tools/DataImport/build_script_doc_rename.py
"""

import json
import os
import re
import sys
import unicodedata
from copy import copy

import openpyxl
from openpyxl.utils import get_column_letter

# ── 경로 (도구 위치 기준 — cwd 비의존) ───────────────────────────────────────
THIS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(THIS_DIR, os.pardir, os.pardir))
JSON_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "GameData")
REPO_XLSX = os.path.join(REPO_ROOT, "data", "여권_정리_updated.xlsx")

DOWNLOADS = os.path.join(os.path.expanduser("~"), "Downloads")
# 손글 소스(읽기 전용, 최신본)
HAND_SRC = os.path.join(DOWNLOADS, "방문객_스크립트_일자별_260609_2 (1).xlsx")
# 출력(새 이름 — 손글 원본 이름과 절대 겹치지 않음)
# 대상 파일이 Excel 보호된 보기로 잠겼을 때 새 이름으로 우회 가능하도록 env 오버라이드 허용.
OUT_PATH = os.path.join(DOWNLOADS, os.environ.get(
    "VSCRIPT_OUT", "방문객_스크립트_일자별_생성_손글기반_260611.xlsx"))

DAYS = list(range(1, 15))
NCOL = 8  # A~H

# 손글 Day 시트 블록 헤더 패턴: "  [N번째 방문객] {이름}  |  {유형약칭}  |  서류: {설명}"
HDR_RE = re.compile(r"\[(\d+)번째 방문객\]\s*(.+?)\s*\|\s*(.+?)\s*\|\s*(서류:.+)")


# ── 정규화 ──────────────────────────────────────────────────────────────────
def nfc(s):
    """NFC 정규화 + 양끝 공백 제거. None → ''."""
    return unicodedata.normalize("NFC", str(s).strip()) if s is not None else ""


# ── repo 로더: 스케줄 + 캐릭터 + source 산출 ────────────────────────────────
def load_repo():
    wb = openpyxl.load_workbook(REPO_XLSX, data_only=True)
    cs = wb["customer"]
    ds = wb["day_schedule"]

    cust = {}      # customerId -> {name, sprite, type}
    name_to_id = {}
    for r in range(5, cs.max_row + 1):
        cid = cs.cell(row=r, column=1).value
        if cid is None:
            continue
        cid = int(cid)
        d = {
            "name": nfc(cs.cell(row=r, column=2).value),
            "sprite": nfc(cs.cell(row=r, column=8).value),
            "type": nfc(cs.cell(row=r, column=9).value),
        }
        cust[cid] = d
        name_to_id.setdefault(d["name"], cid)

    # day_schedule: row3 영문 컬럼 = schedule_id,day,slot,customer_id,valid_chance(col5),customer_name
    sched = {}     # day -> [(slot, customerId)]
    valid_chance = {}  # (day, slot) -> float (슬롯 정상 확률; 1=고정통과, 0=고정거절, 0<x<1=랜덤)
    for r in range(5, ds.max_row + 1):
        day = ds.cell(row=r, column=2).value
        if day is None:
            continue
        day_i = int(day)
        slot_i = int(ds.cell(row=r, column=3).value)
        cid_i = int(ds.cell(row=r, column=4).value)
        sched.setdefault(day_i, []).append((slot_i, cid_i))
        vc = ds.cell(row=r, column=5).value
        valid_chance[(day_i, slot_i)] = float(vc) if vc is not None else None
    for day in sched:
        sched[day].sort()

    wb.close()
    return cust, name_to_id, sched, valid_chance


def source_name(cid, cust):
    """source 원본 이름: id<=37 → 자기 이름, id>=38(클론) → sprite_ref 원본 이름."""
    c = cust[cid]
    return c["name"] if cid <= 37 else c["sprite"]


# ── day JSON: correctResult 로 변형(불량/정상) 선택 ─────────────────────────
def load_correct_results():
    """ (day, slot) -> correctResult('정상 승인'|'정상 거절'...) """
    out = {}
    for d in DAYS:
        path = os.path.join(JSON_DIR, f"day{d}.json")
        try:
            with open(path, encoding="utf-8") as f:
                j = json.load(f)
        except FileNotFoundError:
            continue
        for c in j.get("customers", []):
            slot = c.get("slot")
            if slot is None:
                continue
            out[(d, int(slot))] = c.get("correctResult", "")
    return out


# ── 손글 소스 파싱: 블록 추출 ───────────────────────────────────────────────
class Block:
    """손글 문서의 방문객 변형 블록 1개.
       rows = [(value, style_clone), ...] (A~H 셀의 (값, 스타일)을 행 단위로 보관)."""

    __slots__ = ("day", "pos", "name", "type_abbr", "doc_desc", "is_normal", "rows")

    def __init__(self, day, pos, name, type_abbr, doc_desc, is_normal, rows):
        self.day = day
        self.pos = pos
        self.name = name
        self.type_abbr = type_abbr
        self.doc_desc = doc_desc          # "서류: 정상" / "서류: 불량(...)"
        self.is_normal = is_normal        # True=정상 변형
        self.rows = rows                  # 블록의 모든 행 (헤더 포함)


def _cell_snapshot(cell):
    """(값, 스타일객체튜플) — 새 워크북에 '객체'로 다시 칠한다.
    주의: cell._style 은 소스 워크북 스타일테이블의 '인덱스' 배열이라,
    이를 그대로 복사하면 출력 워크북의 (더 작은) 테이블에 없는 인덱스를
    가리켜 Excel '콘텐츠 복구' 경고가 난다. 그래서 font/fill/border 등
    스타일 '객체'를 복사해 두고, 적용 시 객체 할당(=출력 테이블에 정상 등록)한다."""
    if cell.has_style:
        st = (copy(cell.font), copy(cell.fill), copy(cell.border),
              copy(cell.alignment), cell.number_format, copy(cell.protection))
    else:
        st = None
    return (cell.value, st)


def _apply_style(cell, st):
    """_cell_snapshot 의 스타일객체튜플을 셀에 적용(객체 할당 → 출력 테이블 정합 유지)."""
    if st is None:
        return
    font, fill, border, alignment, number_format, protection = st
    cell.font = copy(font)
    cell.fill = copy(fill)
    cell.border = copy(border)
    cell.alignment = copy(alignment)
    cell.number_format = number_format
    cell.protection = copy(protection)


def parse_hand_source():
    """손글 소스의 모든 Day 시트를 파싱 → day -> [Block, ...] (등장 순서).
       또한 row 높이 정보도 블록에 함께 보존(시각 유지)."""
    wb = openpyxl.load_workbook(HAND_SRC)  # 스타일 보존 위해 data_only=False
    days = {}
    row_heights = {}  # (day, block_index, local_row) -> height

    for d in DAYS:
        ws = wb[f"Day {d:02d}"]
        max_row = ws.max_row

        # 헤더행 위치 수집
        header_rows = []
        for r in range(1, max_row + 1):
            a = ws.cell(row=r, column=1).value
            if a and "번째 방문객" in str(a):
                m = HDR_RE.search(str(a))
                if m:
                    header_rows.append((r, m))

        blocks = []
        for hi, (hr, m) in enumerate(header_rows):
            # 블록 종료: 다음 헤더행 직전. 마지막이면 시트 끝.
            end = header_rows[hi + 1][0] - 1 if hi + 1 < len(header_rows) else max_row
            # 트레일링 빈 행 trim
            while end > hr:
                row_empty = all(
                    ws.cell(row=end, column=c).value in (None, "")
                    for c in range(1, NCOL + 1)
                )
                if row_empty:
                    end -= 1
                else:
                    break

            rows = []
            heights = []
            for rr in range(hr, end + 1):
                cells = [_cell_snapshot(ws.cell(row=rr, column=c)) for c in range(1, NCOL + 1)]
                rows.append(cells)
                heights.append(ws.row_dimensions[rr].height)

            pos = int(m.group(1))
            name = nfc(m.group(2))
            type_abbr = nfc(m.group(3))
            doc_desc = nfc(m.group(4))           # "서류: ..."
            is_normal = doc_desc.replace("서류:", "").strip().startswith("정상")
            blk = Block(d, pos, name, type_abbr, doc_desc, is_normal, rows)
            blk_idx = len(blocks)
            for li, h in enumerate(heights):
                row_heights[(d, blk_idx, li)] = h
            blocks.append(blk)

        days[d] = blocks

    # 비-Day 시트(풀_*·비교대조)도 함께 반환(검증/복사용)
    aux_sheets = [s for s in wb.sheetnames if not s.startswith("Day ")]
    return wb, days, aux_sheets, row_heights


def deduped_sequence(blocks):
    """연속 같은 이름 = 한 명. [(name, [block, ...]), ...] (날의 방문객 시퀀스, 보통 7명)."""
    seq = []
    for b in blocks:
        if seq and seq[-1][0] == b.name:
            seq[-1][1].append(b)
        else:
            seq.append((b.name, [b]))
    return seq


def build_global_index(days):
    """source 이름 -> [Block, ...] (전 날짜에 걸친 그 이름의 모든 변형 블록)."""
    idx = {}
    for d in DAYS:
        for b in days[d]:
            idx.setdefault(b.name, []).append(b)
    return idx


# ── 변형 선택 ───────────────────────────────────────────────────────────────
def pick_variant(variants, correct_result):
    """방문객의 변형 블록들 중 1개 선택.
       정상 승인 → 정상 변형 우선, 그 외(거절 등) → 불량 변형 우선. 없으면 첫 변형."""
    if not variants:
        return None
    if len(variants) == 1:
        return variants[0]
    want_normal = (correct_result == "정상 승인")
    for b in variants:
        if b.is_normal == want_normal:
            return b
    return variants[0]


def pick_cross_block(all_blocks, correct_result, target_day):
    """cross-slot: 전 날짜에서 source 이름의 블록 중 적합한 1개 선택.
       1) correctResult 에 맞는 변형(정상/불량)만 후보로.
       2) 후보가 여러 날에 있으면 target_day 에 가장 가까운 날(동률이면 이른 날).
       3) 적합 변형이 없으면 전체 후보에서 동일 근접 규칙으로."""
    if not all_blocks:
        return None
    want_normal = (correct_result == "정상 승인")
    matched = [b for b in all_blocks if b.is_normal == want_normal]
    pool = matched if matched else all_blocks
    pool = sorted(pool, key=lambda b: (abs(b.day - target_day), b.day))
    return pool[0]


def pick_both_variants(same_pos_variants, all_blocks, want_normal, target_day):
    """확률(랜덤) 슬롯용: 정상 변형 / 불량 변형을 **각각** 1개씩 골라 반환.
       same-position 변형이 있으면 그 안에서, 없으면 cross 후보(전 날짜)에서 근접 규칙으로.
       반환: (normal_block 또는 None, defect_block 또는 None).

       same_pos_variants 가 그 자리의 변형(보통 정상1+불량1)이고, all_blocks 는
       cross 폴백용 전 날짜 origin 블록 목록. want_normal 은 이 자리에서 source 가
       same-position 인지(=손글 같은 자리에 origin 변형이 있는지) 판정에 쓰지 않고,
       단지 두 변형을 둘 다 뽑는 데 의미가 없으므로 무시한다(둘 다 뽑음)."""
    def nearest(blocks):
        return sorted(blocks, key=lambda b: (abs(b.day - target_day), b.day))[0] if blocks else None

    # same-position 변형이 있으면 우선 그 안에서 정상/불량을 가른다.
    if same_pos_variants:
        normal = next((b for b in same_pos_variants if b.is_normal), None)
        defect = next((b for b in same_pos_variants if not b.is_normal), None)
        # same-position 에 한쪽 변형이 없으면 cross 후보로 보강(다른 날 origin 블록).
        if normal is None:
            normal = nearest([b for b in all_blocks if b.is_normal])
        if defect is None:
            defect = nearest([b for b in all_blocks if not b.is_normal])
        return normal, defect

    # same-position 이 아니면(cross) 전 날짜 origin 블록에서 정상/불량 각각 근접 선택.
    normal = nearest([b for b in all_blocks if b.is_normal])
    defect = nearest([b for b in all_blocks if not b.is_normal])
    return normal, defect


# ── 대사 오버라이드(게임플레이상 깨지는 손글 대사 교체) ──────────────────────
# 키=(새 캐릭터 이름, 단계). 값=교체할 대사. 손글 원본은 건드리지 않고 생성 시에만 적용.
#  - 사토 하루키(테러범): 입장컷에서 「비켜! 다 비켜! (폭탄을 흔들며)」로 정체를 노출하면
#    그 자리에서 이벤트가 끝나 심사가 무의미 → 다른 위협 인물처럼 '정상인 척'하고
#    서류(테러범 일치)로 적발되도록 입장 대사를 은밀하게 교체.
DIALOGUE_OVERRIDES = {
    ("사토 하루키", "인삿말"): "「…(굳은 표정으로 주위를 살피며 말없이 여권을 내민다.)」",
}


# ── 블록 렌더(이름 치환 + 헤더 갱신) ────────────────────────────────────────
def render_block(ws, start_row, block, new_name, new_slot, new_type_abbr,
                 is_dummy, source_origin, row_heights, src_day, src_blk_idx,
                 doc_desc_override=None):
    """source 블록을 ws 의 start_row 부터 다시 쓰며 이름/헤더/슬롯을 새 캐릭터로 치환.
       doc_desc_override: 헤더의 '서류: ...' 문구를 강제 지정(확률 슬롯 두 변형 표시용).
       반환: 다음 빈 행을 포함하지 않은, 마지막으로 쓴 행 + 1 (다음 블록 시작 행)."""
    old_name = block.name
    r = start_row

    for li, cells in enumerate(block.rows):
        # 이 행의 단계(B열) — 대사 오버라이드 매칭용.
        row_stage = nfc(cells[1][0]) if len(cells) > 1 and cells[1][0] is not None else ""
        for ci, (val, style) in enumerate(cells):
            new_val = val
            col = ci + 1

            if li == 0 and col == 1:
                # 블록 헤더(A열): [N번째] → 새 슬롯, 이름·유형 치환, 더미 주석.
                new_val = _rebuild_header(
                    val, new_name, new_slot, new_type_abbr, is_dummy, source_origin,
                    doc_desc_override,
                )
            elif col == 4 and nfc(val) == old_name:
                # D열 이름 정확 치환(부분일치 오치환 방지: 정확 일치만).
                new_val = new_name
            elif col == 8 and (new_name, row_stage) in DIALOGUE_OVERRIDES:
                # H열 대사: 게임플레이상 깨지는 손글 대사 교체(예: 테러범 입장컷 정체 노출).
                new_val = DIALOGUE_OVERRIDES[(new_name, row_stage)]
            else:
                # 그 외 셀: 본문 대사는 그대로 보존. (대사 내 이름 언급은 손글 그대로 둔다)
                new_val = val

            cell = ws.cell(row=r, column=col, value=new_val)
            _apply_style(cell, style)

        h = row_heights.get((src_day, src_blk_idx, li))
        if h is not None:
            ws.row_dimensions[r].height = h
        r += 1

    return r


def _rebuild_header(orig_header, new_name, new_slot, new_type_abbr, is_dummy, source_origin,
                    doc_desc_override=None):
    """헤더 문자열 재구성. 원형: '  [N번째 방문객] {이름}  |  {유형}  |  서류: {설명}'.
       doc_desc_override 가 주어지면 '서류: ...' 문구를 그것으로 강제(확률 슬롯 변형 표시)."""
    m = HDR_RE.search(str(orig_header))
    if not m:
        return orig_header
    doc_desc = nfc(doc_desc_override) if doc_desc_override else nfc(m.group(4))  # "서류: ..."
    header = f"  [{new_slot}번째 방문객] {new_name}  |  {new_type_abbr}  |  {doc_desc}"
    if is_dummy:
        header += f"  (더미·클론←{source_origin})"
    return header


# ── 새 Day 시트 작성 ────────────────────────────────────────────────────────
def write_new_day_sheet(wb, day, slot_plan, src_ws, first):
    """slot_plan: [(slot, new_name, new_type_abbr, is_dummy, source_origin,
                    block, src_day, src_blk_idx, mapping_note), ...] (슬롯 오름차순)."""
    title = f"Day {day:02d}"
    if first:
        ws = wb.active
        ws.title = title
    else:
        ws = wb.create_sheet(title)

    # 컬럼 폭/헤더행/타이틀행은 손글 원본 Day 시트에서 그대로 가져온다(1~3행).
    for c in range(1, NCOL + 1):
        L = get_column_letter(c)
        ws.column_dimensions[L].width = src_ws.column_dimensions[L].width

    # row1(헤더), row2(타이틀), row3(빈) 복사
    for rr in (1, 2, 3):
        for c in range(1, NCOL + 1):
            sc = src_ws.cell(row=rr, column=c)
            dc = ws.cell(row=rr, column=c, value=sc.value)
            if sc.has_style:
                dc.font = copy(sc.font)
                dc.fill = copy(sc.fill)
                dc.border = copy(sc.border)
                dc.alignment = copy(sc.alignment)
                dc.number_format = sc.number_format
                dc.protection = copy(sc.protection)
        if src_ws.row_dimensions[rr].height is not None:
            ws.row_dimensions[rr].height = src_ws.row_dimensions[rr].height

    ws.freeze_panes = "A2"
    return ws


# ── 비-Day 시트 복사(풀_*·비교대조) ────────────────────────────────────────
def copy_sheet_verbatim(wb_out, src_wb, sheet_name):
    """src_wb 의 한 시트를 값+스타일+폭+높이+병합 그대로 새 워크북에 복사.
       병합을 먼저 적용한 뒤 셀 값/스타일을 칠한다(병합 시 border 재계산이
       비어있는 셀 스타일을 읽다 IndexError 나는 것을 방지)."""
    src = src_wb[sheet_name]
    dst = wb_out.create_sheet(sheet_name)
    # 1) 병합 먼저(빈 시트 상태에서 안전).
    for mr in src.merged_cells.ranges:
        dst.merge_cells(str(mr))
    # 2) 값/스타일 복사.
    for row in src.iter_rows():
        for cell in row:
            d = dst.cell(row=cell.row, column=cell.column, value=cell.value)
            if cell.has_style:
                d.font = copy(cell.font)
                d.fill = copy(cell.fill)
                d.border = copy(cell.border)
                d.alignment = copy(cell.alignment)
                d.number_format = cell.number_format
                d.protection = copy(cell.protection)
    for col, dim in src.column_dimensions.items():
        if dim.width is not None:
            dst.column_dimensions[col].width = dim.width
    for r, dim in src.row_dimensions.items():
        if dim.height is not None:
            dst.row_dimensions[r].height = dim.height
    dst.freeze_panes = src.freeze_panes
    return dst


# ── 저장(파일 잠금 처리) ────────────────────────────────────────────────────
def _unblock_motw(path):
    """MOTW(다운로드 표식) 제거 — 다음 열기 때 ProtectedView 방지. 실패해도 무시."""
    try:
        import subprocess
        subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             f"Unblock-File -LiteralPath '{path}'"],
            check=False, capture_output=True, timeout=30,
        )
    except Exception:
        pass


def _reflect_via_xlwings(out_wb, path):
    """잠긴 캐논 파일(열린 워크북)에 xlwings 로 attach 해 out_wb 내용을 제자리 반영.
       Day 시트는 clear→재작성, 비-Day 시트는 그대로 유지(자립형 보강은 openpyxl 경로에서만).
       반환: 'xlwings_inplace' 성공 / 예외 전파(상위에서 닫기 폴백 판단).
       주의: 셀 값만 반영(서식은 손글 원본 동일 레이아웃이라 시각 영향 미미). 헤더/대사 텍스트가 핵심."""
    import xlwings as xw

    target = os.path.abspath(path)
    app = None
    bk = None
    for a in xw.apps:
        for b in a.books:
            try:
                if os.path.abspath(b.fullname) == target:
                    app, bk = a, b
                    break
            except Exception:
                # ProtectedViewWindow 등은 .fullname/.Application 접근에서 터질 수 있다.
                raise
        if bk is not None:
            break
    if bk is None:
        raise FileNotFoundError("열린 워크북 중 대상 캐논 파일을 찾지 못함(xlwings)")

    # Day 시트만 값 반영(셀 단위). 비-Day 시트는 건드리지 않음.
    for ws_src in out_wb.worksheets:
        name = ws_src.title
        if not name.startswith("Day "):
            continue
        try:
            sht = bk.sheets[name]
        except Exception:
            sht = bk.sheets.add(name, after=bk.sheets[-1])
        sht.clear_contents()
        # 2D 배열로 한 번에 기록(빠르고 결정론적).
        max_r = ws_src.max_row
        max_c = NCOL
        data = []
        for r in range(1, max_r + 1):
            row = []
            for c in range(1, max_c + 1):
                row.append(ws_src.cell(row=r, column=c).value)
            data.append(row)
        if data:
            sht.range((1, 1)).value = data
    bk.save()
    return "xlwings_inplace"


def _close_locked_then_save(out_wb, path):
    """ProtectedView 등으로 in-place 반영이 막히면: 열린 워크북을 xlwings 로 닫고
       (변경 없으면 저장 안 함) openpyxl 로 같은 이름에 재생성. 닫기는 COM 이 수행."""
    import xlwings as xw
    target = os.path.abspath(path)
    closed = False
    for a in list(xw.apps):
        for b in list(a.books):
            try:
                same = os.path.abspath(b.fullname) == target
            except Exception:
                # ProtectedView 윈도우는 fullname 접근 자체가 실패 → 이름으로 매칭 시도.
                try:
                    same = (b.name == os.path.basename(path))
                except Exception:
                    same = False
            if same:
                try:
                    b.close()  # 우리가 만든 변경분이 아니므로 저장 안 함(읽기 보기였음)
                    closed = True
                except Exception:
                    pass
    out_wb.save(path)  # 이제 openpyxl 이 같은 이름에 쓸 수 있음
    return "xlwings_close_then_openpyxl" if closed else "openpyxl_after_close_attempt"


def save_workbook(out_wb, path):
    """잠금 안전 저장. 반환: 사용한 저장 방식 문자열.

       순서(서식 완전 보존 우선):
         1) openpyxl 직접 저장(잠겨 있지 않으면 끝).
         2) (PermissionError) 열린 워크북을 xlwings 로 닫고(우리 변경 아님 → 저장 안 함)
            openpyxl 로 같은 이름에 **완전한 서식 포함** 재생성. 닫기는 COM 이 수행(사용자 수동 X).
            → 손글 원본 스타일 객체 복사가 그대로 살아 복구 경고 없음.
         3) (닫기가 ProtectedView 등으로 막힘) xlwings in-place 반영(셀 값만, 서식 일부 손실)
            — 파일을 닫지 못하는 최후 수단. 그래도 새 이름은 만들지 않는다.
       모두 실패하면 새 이름 만들지 않고 예외를 올린다(상위에서 멈춤·보고)."""
    try:
        out_wb.save(path)
        _unblock_motw(path)
        return "openpyxl_direct"
    except PermissionError:
        pass  # 잠김 → 아래 폴백

    # 2) 닫고 openpyxl 재생성(서식 보존 우선)
    try:
        method = _close_locked_then_save(out_wb, path)
        _unblock_motw(path)
        return method
    except Exception as e_close:
        close_err = e_close

    # 3) 닫기 불가(ProtectedView/COM) → in-place 반영(값만, 서식 일부 손실)
    try:
        method = _reflect_via_xlwings(out_wb, path)
        _unblock_motw(path)
        return method + f" (닫기 폴백 실패: {type(close_err).__name__}: {close_err})"
    except Exception as e_inplace:
        raise RuntimeError(
            "캐논 파일이 잠겨 저장 실패. 닫기 후 재생성도, in-place 반영도 막혔습니다. "
            "새 이름 파일은 만들지 않습니다(정책). 멈추고 보고합니다.\n"
            f"  - 닫기/재생성 실패: {type(close_err).__name__}: {close_err}\n"
            f"  - in-place 실패: {type(e_inplace).__name__}: {e_inplace}"
        ) from e_inplace


# ── 메인 빌드 ───────────────────────────────────────────────────────────────
def build():
    # 출력 보호: 손글 원본 이름과 절대 겹치지 않는 새 이름.
    assert os.path.basename(OUT_PATH).startswith("방문객_스크립트_일자별_생성_손글기반"), "출력 파일명 보호"
    assert os.path.abspath(OUT_PATH) != os.path.abspath(HAND_SRC), "손글 원본 덮어쓰기 금지"
    assert "260609" not in os.path.basename(OUT_PATH), "손글 원본(260609)과 출력 충돌 방지"

    cust, name_to_id, sched, valid_chance = load_repo()
    correct = load_correct_results()
    src_wb, days, aux_sheets, row_heights = parse_hand_source()
    global_index = build_global_index(days)

    # 날별 deduped 시퀀스(7명) — same-position 매칭용
    deduped = {d: deduped_sequence(days[d]) for d in DAYS}

    # 블록 인덱스(같은 이름 변형 묶음, 빠른 src_blk_idx 조회용): (day) -> block -> idx
    blk_idx_map = {}
    for d in DAYS:
        for i, b in enumerate(days[d]):
            blk_idx_map[id(b)] = (d, i)

    out_wb = openpyxl.Workbook()

    mapping_report = []   # (day, slot, new_name, source_origin, src_kind, src_loc)
    cross_slots = []
    dist = {"orig_slot": 0, "clone_slot": 0}
    todo_cells = 0
    todo_slots = []

    first = True
    for d in DAYS:
        src_ws = src_wb[f"Day {d:02d}"]
        seq = deduped[d]  # [(name, [blocks])]
        new_ws = write_new_day_sheet(out_wb, d, None, src_ws, first)
        first = False

        # 슬롯 오름차순으로 블록 다시 쓰기. 시작 행 = 4 (1~3 헤더/타이틀/빈)
        cur_row = 4
        for i, (slot, cid) in enumerate(sched[d]):
            c = cust[cid]
            new_name = c["name"]
            new_type = c["type"]
            origin = source_name(cid, cust)
            is_clone = cid > 38 - 1  # cid >= 38
            is_dummy = is_clone or (origin != new_name)
            cr = correct.get((d, slot), "")

            if is_clone:
                dist["clone_slot"] += 1
            else:
                dist["orig_slot"] += 1

            # same-position 후보: 손글 같은 (day, deduped 위치 i)
            same_pos_name = seq[i][0] if i < len(seq) else None
            same_pos_variants = seq[i][1] if i < len(seq) else []
            is_samepos = (same_pos_name is not None and nfc(same_pos_name) == nfc(origin))
            all_blocks = global_index.get(origin, [])

            # 슬롯 정상 확률(day_schedule). 0<vc<1 = 랜덤 슬롯 → 두 변형 모두 출력.
            vc = valid_chance.get((d, slot))
            is_random_slot = (vc is not None and 0.0 < vc < 1.0)

            def _render(block, doc_override=None):
                """공통 렌더: 헤더 유형약칭=source 블록 약칭, 더미주석 유지. 반환 = (src_kind, src_loc)."""
                nonlocal cur_row
                new_type_abbr = block.type_abbr
                sd, sbi = blk_idx_map[id(block)]
                cur_row = render_block(
                    new_ws, cur_row, block, new_name, slot, new_type_abbr,
                    is_dummy, origin, row_heights, sd, sbi,
                    doc_desc_override=doc_override,
                )
                cur_row += 1  # 블록 사이 빈 행
                kind = "samepos" if is_samepos else "cross"
                loc = f"D{d}" if is_samepos else f"D{block.day}(pos{block.pos})"
                return kind, loc

            if is_random_slot:
                # ── 확률(랜덤) 슬롯: 정상 변형 + 불량 변형 두 블록 모두 출력 ──
                # cross 슬롯이면 same_pos 변형은 '다른 캐릭터'의 블록이므로 쓰지 않는다.
                # origin(전 날짜) 블록에서만 정상/불량을 뽑는다.
                want_normal = (cr == "정상 승인")  # 참고용(둘 다 출력하므로 선택엔 미사용)
                sp_for_pick = same_pos_variants if is_samepos else []
                nrm, dfc = pick_both_variants(sp_for_pick, all_blocks, want_normal, d)
                if not is_samepos:
                    cross_slots.append((d, slot, new_name, cid, origin, same_pos_name,
                                        f"D{(nrm or dfc).day}(pos{(nrm or dfc).pos})" if (nrm or dfc) else None))

                if nrm is None and dfc is None:
                    todo_slots.append((d, slot, new_name, origin))
                    new_ws.cell(row=cur_row, column=1,
                                value=f"  [{slot}번째 방문객] {new_name}  |  {new_type}  |  서류: [TODO 대사: source '{origin}' 미발견]")
                    cur_row += 2
                    mapping_report.append((d, slot, new_name, origin, "MISSING", "-"))
                    continue

                # 정상 → 불량 순으로 출력(있는 것만). 헤더에 변형을 명시(정상/불량).
                emitted = []
                if nrm is not None:
                    k, l = _render(nrm, "서류: 정상")
                    emitted.append("정상")
                if dfc is not None:
                    # 불량 설명은 source 블록의 doc_desc(예 "서류: 불량(여권 기간 오류)")를 그대로 보존.
                    k, l = _render(dfc, dfc.doc_desc)
                    emitted.append("불량")
                src_kind = ("samepos" if is_samepos else "cross") + f"+rand({'/'.join(emitted)})"
                src_loc = "D" + str(d) if is_samepos else "(cross)"
                mapping_report.append((d, slot, new_name, origin, src_kind, src_loc))
                continue

            # ── 확정 슬롯(vc==1.0 / 0.0 / None): 현행대로 correctResult 에 맞는 1개 ──
            if is_samepos:
                block = pick_variant(same_pos_variants, cr)
                src_kind = "samepos"
                src_loc = f"D{d}"
            else:
                block = pick_cross_block(all_blocks, cr, d)
                src_kind = "cross"
                src_loc = f"D{block.day}(pos{block.pos})" if block is not None else None
                cross_slots.append((d, slot, new_name, cid, origin, same_pos_name, src_loc))

            if block is None:
                # source 블록을 못 찾음 — TODO 슬롯 기록(빈 블록 1행만)
                todo_slots.append((d, slot, new_name, origin))
                new_ws.cell(row=cur_row, column=1,
                            value=f"  [{slot}번째 방문객] {new_name}  |  {new_type}  |  서류: [TODO 대사: source '{origin}' 미발견]")
                cur_row += 2
                mapping_report.append((d, slot, new_name, origin, "MISSING", "-"))
                continue

            _render(block)
            mapping_report.append((d, slot, new_name, origin, src_kind, src_loc))

    # 비-Day 시트 복사(풀_*·비교대조) — 자립형 문서
    for sn in aux_sheets:
        copy_sheet_verbatim(out_wb, src_wb, sn)

    # 시트 순서: Day 01..14, 그다음 풀/비교대조 (손글 순서는 풀 먼저였지만 가독상 Day 먼저)
    # 그대로 둔다(active=Day01). 필요 시 재정렬 가능.

    # 결정론: 문서 메타 타임스탬프 고정
    import datetime
    fixed = datetime.datetime(2026, 6, 11, 0, 0, 0)
    out_wb.properties.created = fixed
    out_wb.properties.modified = fixed
    out_wb.properties.creator = "build_script_doc_rename"
    out_wb.properties.lastModifiedBy = "build_script_doc_rename"

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    save_method = save_workbook(out_wb, OUT_PATH)
    src_wb.close()

    return {
        "mapping": mapping_report,
        "cross_slots": cross_slots,
        "save_method": save_method,
        "valid_chance": valid_chance,
        "dist": dist,
        "todo_slots": todo_slots,
        "cust": cust,
        "sched": sched,
        "days": days,
        "deduped": deduped,
        "correct": correct,
    }


# ── 검증 리포트 ─────────────────────────────────────────────────────────────
KNOWN_CROSS = {
    # (day, slot): (new_name, source_origin)
    (3, 6): ("윤서린", "윤서린"),
    (11, 1): ("존 카터", "존 카터"),
    (12, 2): ("사토 하루키", "사토 하루키"),
    (12, 3): ("한지아", "서지호"),
    (13, 1): ("우 팅", "왕 웨이"),
}


def _block_first_greeting(block, new_name):
    """블록의 인삿말(2단계) 첫 대사를 이름 치환 후 반환(미리보기용)."""
    if block is None:
        return ""
    for cells in block.rows:
        # 데이터행: A=순서, D=이름. 2단계(인삿말) 행은 A=='2' 또는 B=='인삿말'
        a = nfc(cells[0][0])
        b = nfc(cells[1][0])
        if a == "2" or b == "인삿말":
            return str(cells[7][0] or "").split("\n")[0]
    return ""


def validate(state):
    out = []
    P = out.append
    P("=" * 70)
    P("검증 리포트 — 방문객 스크립트(손글 블록 상속·이름변경) 98명 생성")
    P("=" * 70)

    # 출력 재로드
    rb = openpyxl.load_workbook(OUT_PATH)
    day_sheets = [s for s in rb.sheetnames if s.startswith("Day ")]
    aux = [s for s in rb.sheetnames if not s.startswith("Day ")]

    # 블록 수 카운트(각 Day 시트의 [N번째 방문객] 헤더 수) + 슬롯별 헤더 수집
    total_blocks = 0
    per_day_blocks = {}
    per_slot_headers = {}  # (dayNum, slotN) -> [헤더문자열, ...]
    todo_in_doc = 0
    slot_hdr_re = re.compile(r"\[(\d+)번째 방문객\]")
    for sn in day_sheets:
        ws = rb[sn]
        day_num = int(sn.split()[-1])
        cnt = 0
        for r in range(1, ws.max_row + 1):
            a = ws.cell(row=r, column=1).value
            if a and "번째 방문객" in str(a):
                cnt += 1
                if "[TODO 대사" in str(a):
                    todo_in_doc += 1
                ms = slot_hdr_re.search(str(a))
                if ms:
                    per_slot_headers.setdefault((day_num, int(ms.group(1))), []).append(str(a))
        per_day_blocks[sn] = cnt
        total_blocks += cnt

    # 본문 셀의 [TODO 대사] 마크
    todo_body = 0
    for sn in day_sheets:
        ws = rb[sn]
        for row in ws.iter_rows():
            for cell in row:
                if cell.value and "[TODO 대사]" in str(cell.value):
                    todo_body += 1

    # 확률 슬롯 = day_schedule valid_chance 0<x<1
    valid_chance = state["valid_chance"]
    prob_slots = sorted([k for k, v in valid_chance.items() if v is not None and 0.0 < v < 1.0])
    fixed_slots = [k for k, v in valid_chance.items() if not (v is not None and 0.0 < v < 1.0)]
    # 기대 블록 수: 확정 98 + 확률 슬롯마다 (출력된 변형 수 - 1)
    P(f"저장 방식: {state.get('save_method')}")
    P(f"Day 시트: {len(day_sheets)}개 (기대 14)")
    P(f"확률(랜덤) 슬롯: {len(prob_slots)}개 (day_schedule 0<valid_chance<1)")
    P(f"총 블록(헤더) 수: {total_blocks}")
    # 확정 슬롯은 정확히 1블록이어야, 확률 슬롯은 1~2블록(둘 다/한쪽).
    fixed_bad = [(d, s) for (d, s) in fixed_slots
                 if len(per_slot_headers.get((d, s), [])) != 1]
    if fixed_bad:
        P(f"  [경고] 확정 슬롯인데 1블록이 아닌 곳: {fixed_bad[:20]}")
    else:
        P("  확정 슬롯: 전부 1블록 유지(정상).")
    P(f"비-Day 시트(자립형 복사): {aux}")

    P("")
    P(f"[TODO 대사] 셀 수: {todo_body}  (기대 0)")
    if state["todo_slots"]:
        P("  [경고] source 블록 미발견 슬롯:")
        for d, slot, nm, origin in state["todo_slots"]:
            P(f"    Day{d} s{slot} {nm} ← source '{origin}' (미발견)")
    else:
        P("  정상: 모든 슬롯이 손글 source 블록을 찾아 채움(빈 source 0).")

    # ── 확률 슬롯 두 변형 검증 ──
    P("")
    P("── 확률(랜덤) 슬롯 두 변형(정상+불량) 출력 검증 ──")
    two_var = 0
    one_var = 0
    for (d, s) in prob_slots:
        hdrs = per_slot_headers.get((d, s), [])
        has_normal = any(("서류: 정상" in h) for h in hdrs)
        has_defect = any(("서류: 불량" in h or "서류: 결함" in h) for h in hdrs)
        n = len(hdrs)
        tag = "정상+불량" if (has_normal and has_defect) else (
            "정상만" if has_normal else ("불량만" if has_defect else "?"))
        if has_normal and has_defect:
            two_var += 1
        else:
            one_var += 1
        # 표본만 출력(전부 출력하면 길다): day1/3/4/12 + 단일변형은 항상 출력
        sample = (d in (1, 3, 4, 12)) or (n < 2)
        if sample:
            P(f"  Day{d} s{s}: 블록 {n}개 [{tag}]")
    P(f"  → 두 변형(정상+불량) 출력 슬롯: {two_var} / 단일변형(손글에 한쪽만): {one_var}"
      f"  (총 {len(prob_slots)})")

    # 분포
    P("")
    od = state["dist"]["orig_slot"]
    cl = state["dist"]["clone_slot"]
    P(f"분포: 원본 슬롯 {od} / 클론(더미) 슬롯 {cl}  (합계 {od + cl})")

    # 매핑표(전체) — same/cross 표기 (확률 슬롯은 '...+rand(...)' 접미사)
    cross = [m for m in state["mapping"] if str(m[4]).startswith("cross")]
    same = [m for m in state["mapping"] if str(m[4]).startswith("samepos")]
    rand = [m for m in state["mapping"] if "rand(" in str(m[4])]
    miss = [m for m in state["mapping"] if m[4] == "MISSING"]
    P(f"매핑: same-position {len(same)} / cross-slot {len(cross)} / "
      f"그중 확률(2변형) {len(rand)} / missing {len(miss)}")

    # cross-slot 상세 + 알려진 목록 대조
    P("")
    P("── cross-slot 처리 결과 (source 가 다른 위치에 있던 슬롯) ──")
    got = {}
    for d, slot, nm, cid, origin, samepos_nm, src_loc in state["cross_slots"]:
        got[(d, slot)] = (nm, origin)
        expect = KNOWN_CROSS.get((d, slot))
        flag = ""
        if expect is None:
            flag = "  <<< 알려진 목록에 없음"
        elif (nfc(expect[0]) != nfc(nm)) or (nfc(expect[1]) != nfc(origin)):
            flag = f"  <<< 불일치(기대 {expect})"
        P(f"  Day{d} s{slot}: {nm} ← source({origin}) ← {src_loc}"
          f" (같은자리엔 {samepos_nm}){flag}")
    # 알려진 cross 중 누락
    for k, v in KNOWN_CROSS.items():
        if k not in got:
            P(f"  [정보] Day{k[0]} s{k[1]} {v[0]}(source={v[1]}) 는 same-position 으로 해소됨"
              f" (id<=37 자기이름 규칙). cross 아님.")

    # 자오 레이 특기사항
    P("")
    P("── 특기: Day9 s7 자오 레이(id35) ──")
    P("  sprite_ref 가 '지오 레이'(오타)지만 id<=37 이므로 source=자기이름(자오 레이).")
    P("  손글 Day9 s7 same-position 에 자오 레이 블록이 있어 그 자리에서 해소(cross 아님).")

    # 매핑표 일부(Day1·Day8) 예시
    for show_day in (1, 8):
        P("")
        P(f"── 매핑표 예시 (Day {show_day}) ──")
        for (d, slot, nm, origin, kind, loc) in state["mapping"]:
            if d == show_day:
                tag = "같은자리" if kind == "samepos" else (kind if kind != "MISSING" else "MISSING")
                P(f"  Day{d} s{slot}: {nm} ← {origin} ← {loc} [{tag}]")

    # Day4/12/13 사람이 읽는 미리보기
    days = state["days"]
    deduped = state["deduped"]
    cust = state["cust"]
    sched = state["sched"]
    correct = state["correct"]
    gindex = build_global_index(days)

    def src_name(cid):
        return source_name(cid, cust)

    for target in (4, 12, 13):
        P("")
        P(f"── Day {target} 미리보기 (슬롯별: 새이름 ← source ← 첫인삿말) ──")
        seq = deduped[target]
        for i, (slot, cid) in enumerate(sched[target]):
            c = cust[cid]
            origin = src_name(cid)
            cr = correct.get((target, slot), "")
            same_pos_name = seq[i][0] if i < len(seq) else None
            if same_pos_name is not None and nfc(same_pos_name) == nfc(origin):
                blk = pick_variant(seq[i][1], cr)
                where = f"D{target}(같은자리)"
            else:
                blk = pick_cross_block(gindex.get(origin, []), cr, target)
                where = f"D{blk.day}(cross)" if blk else "(미발견)"
            greet = _block_first_greeting(blk, c["name"])
            kind = "원본" if cid <= 37 else f"클론←{origin}"
            P(f"  s{slot} {c['name']} [{c['type']}] ({kind}) {where}")
            P(f"       인삿말: {greet}")

    # 사토 하루키 입장 대사 무결성(폭탄 노출 금지 → 굳은 표정 오버라이드 유지)
    P("")
    P("── 사토 하루키 입장 대사 오버라이드 검증 ──")
    sato_found = []
    for sn in day_sheets:
        ws = rb[sn]
        in_sato = False
        for r in range(1, ws.max_row + 1):
            a = ws.cell(row=r, column=1).value
            if a and "번째 방문객" in str(a):
                in_sato = ("사토 하루키" in str(a))
            if in_sato:
                stage = ws.cell(row=r, column=2).value
                if stage and nfc(stage) == "인삿말":
                    txt = str(ws.cell(row=r, column=8).value or "").split("\n")[0]
                    sato_found.append((sn, txt))
    bomb = [(sn, t) for sn, t in sato_found if ("폭탄" in t or "비켜" in t)]
    for sn, t in sato_found:
        P(f"  {sn}: {t}")
    if bomb:
        P(f"  [경고] 사토 입장 대사에 폭탄/비켜 노출: {bomb}")
    else:
        P("  정상: 사토 입장 대사가 폭탄 노출 아님(굳은 표정 오버라이드 적용).")

    P("")
    P(f"[재로드 확인] 출력 시트 {len(rb.sheetnames)}개: {rb.sheetnames}")
    rb.close()

    # ── 스타일 무결성: styles.xml cellXfs 의 font/fill/border 인덱스가 테이블 범위 내인가 ──
    P("")
    P("── 스타일 무결성(zip 검사: cellXfs 인덱스 범위 초과 = Excel 복구 경고 원인) ──")
    style_ok, style_msg = _check_style_integrity(OUT_PATH)
    P("  " + style_msg)

    P("=" * 70)
    return "\n".join(out)


def _check_style_integrity(path):
    """xlsx(zip)의 xl/styles.xml 을 직접 파싱해 cellXfs 의 fontId/fillId/borderId 가
       각 풀(fonts/fills/borders) 크기 범위 내인지 검사. 초과하면 Excel '복구' 경고 원인.
       반환: (ok: bool, message: str). 파일이 잠겨 Excel 이 열고 있어도 zip 읽기는 가능."""
    import zipfile
    import xml.etree.ElementTree as ET
    ns = {"a": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
    try:
        with zipfile.ZipFile(path) as z:
            xml = z.read("xl/styles.xml")
    except Exception as e:
        return False, f"styles.xml 읽기 실패: {type(e).__name__}: {e}"
    root = ET.fromstring(xml)

    def count(tag):
        el = root.find(f"a:{tag}", ns)
        return len(list(el)) if el is not None else 0

    n_fonts = count("fonts")
    n_fills = count("fills")
    n_borders = count("borders")
    cellxfs = root.find("a:cellXfs", ns)
    over = []
    n_xf = 0
    if cellxfs is not None:
        for xf in cellxfs:
            n_xf += 1
            fid = int(xf.get("fontId", "0"))
            flid = int(xf.get("fillId", "0"))
            bid = int(xf.get("borderId", "0"))
            if fid >= n_fonts or flid >= n_fills or bid >= n_borders:
                over.append((fid, flid, bid))
    ok = (len(over) == 0)
    msg = (f"fonts={n_fonts} fills={n_fills} borders={n_borders} cellXfs={n_xf}; "
           f"범위초과 인덱스 {len(over)}개"
           + ("  → 정상(복구 경고 없음 기대)" if ok else f"  [경고] 초과 예: {over[:5]}"))
    return ok, msg


# ── 엔트리 ──────────────────────────────────────────────────────────────────
def main():
    state = build()
    print(f"[생성] {OUT_PATH}")
    print()
    print(validate(state))


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    main()
