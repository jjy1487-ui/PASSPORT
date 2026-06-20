# -*- coding: utf-8 -*-
"""Day12 시트의 사토 하루키 블록(row 11~24, 14행)을 day12.json 기준 6분기
시나리오 행으로 교체한다. xlwings(COM)로 열린 Excel 인스턴스에 붙어 편집.

- 사토 블록만 삭제 후 새 행 삽입 (위/아래 다른 손님 블록은 위치만 밀림, 내용 불변)
- 서식: 맑은 고딕 11, vertical top, col3(방문객) bold, col8(대사) wrap,
        col5(서류상태) 첫 행 fill(FFFCE4D6), 섹션/분기 헤더 행 col8 bold
"""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_sato_day12_rows import build_rows  # noqa: E402

import xlwings as xw  # noqa: E402

XLSX_NAME = '대사_스크립트.xlsx'
SHEET = 'Day12'
SATO_FIRST_ROW = 11   # 사토 블록 시작 (1-based)
SATO_OLD_COUNT = 14   # 기존 사토 블록 행 수 (row 11~24 포함, 24=빈 구분행)


def get_book():
    base = XLSX_NAME.lower()
    for app in xw.apps:
        for i in range(1, app.books.count + 1):
            try:
                bk = app.books[i - 1]
                if bk.name.lower() == base:
                    return bk
            except Exception:
                pass
    raise RuntimeError('대사_스크립트.xlsx 가 Excel에서 열려 있지 않습니다.')


def find_sato_block(ws):
    """C열에서 '사토 하루키' 시작행과, 그 뒤 첫 '다른 손님'(B열 채워짐) 행을 찾아
    (first_row, count) 반환. 하드코딩 대신 현재 블록 크기를 동적 탐지."""
    used = ws.api.UsedRange.Rows.Count
    start = None
    for r in range(1, used + 2):
        c = ws.range((r, 3)).value
        if c and str(c).strip() == '사토 하루키':
            start = r
            break
    if start is None:
        raise RuntimeError('사토 하루키 블록 시작행을 찾지 못했습니다.')
    nxt = None
    for r in range(start + 1, used + 2):
        b = ws.range((r, 2)).value
        cc = ws.range((r, 3)).value
        if b not in (None, '') and cc and str(cc).strip() != '사토 하루키':
            nxt = r
            break
    if nxt is None:
        raise RuntimeError('사토 다음 손님 블록을 찾지 못했습니다.')
    return start, nxt - start  # 블록 = start .. nxt-1


def main():
    rows = build_rows()
    new_count = len(rows)

    bk = get_book()
    ws = bk.sheets[SHEET]

    # --- 사토 블록 동적 탐지 (옛 14행 하드코딩 폐기) ---
    first_row, old_count = find_sato_block(ws)
    print(f'detected 사토 블록: row {first_row}, {old_count}행 → 새 {new_count}행')

    # --- pre-readback: 사토 위/아래 블록 마커 ---
    before = {
        'r3_imgaeun': ws.range('C3').value,    # 임가은 (순서1, 위)
        'sato_start': ws.range((first_row, 3)).value,
        'next_cust': ws.range((first_row + old_count, 3)).value,
        'total_used': ws.api.UsedRange.Rows.Count,
    }
    print('BEFORE:', before)

    # --- 1) delete old sato block ---
    del_rng = ws.range(f'{first_row}:{first_row + old_count - 1}')
    del_rng.api.EntireRow.Delete()

    # --- 2) insert new_count blank rows at first_row ---
    ins_rng = ws.range(f'{first_row}:{first_row + new_count - 1}')
    ins_rng.api.EntireRow.Insert()

    # --- 3) write values + formatting ---
    for ridx, r in enumerate(rows):
        excel_row = first_row + ridx
        for cidx in range(8):  # A..H = cols 1..8
            cell = ws.range((excel_row, cidx + 1))
            val = r[cidx]
            cell.value = val if val != '' else None
            # base font
            cell.font.name = '맑은 고딕'
            cell.font.size = 11
            cell.api.VerticalAlignment = -4160  # xlTop
            cell.font.bold = False
        # col3 (방문객) bold per sheet convention
        ws.range((excel_row, 3)).font.bold = True
        # col8 (대사) wrap
        ws.range((excel_row, 8)).api.WrapText = True
        # section / branch header rows: H bold for readability
        is_header = (r[7] != '' and r[6] == '' and r[2] == '')
        if is_header:
            ws.range((excel_row, 8)).font.bold = True
        # 분기점/선택/결과 행 F열 강조 (보라 bold) — 단계 라벨 관례
        fval = r[5]
        if fval and ('분기점' in fval or '선택' in fval or '결과' in fval
                     or fval in ('등장', '인삿말', '방문목적', '전신 X-ray 검색', 'X-ray 발각', 'xray출력')):
            fc = ws.range((excel_row, 6))
            fc.font.bold = True
            fc.api.Font.Color = 0xA03070  # BGR for #7030A0 -> stored as 0x7030A0? handle below
    # fix col6 purple color properly (Excel uses BGR int)
    def rgb_to_bgr(r, g, b):
        return b * 65536 + g * 256 + r
    purple = rgb_to_bgr(0x70, 0x30, 0xA0)
    for ridx, r in enumerate(rows):
        fval = r[5]
        if fval and ('분기점' in fval or '선택' in fval or '결과' in fval
                     or fval in ('등장', '인삿말', '방문목적', '전신 X-ray 검색', 'X-ray 발각', 'xray출력')):
            ws.range((first_row + ridx, 6)).api.Font.Color = purple

    # col5 fill on the block-first row (FFFCE4D6) like other blocks
    fill_bgr = rgb_to_bgr(0xFC, 0xE4, 0xD6)
    c5 = ws.range((first_row, 5))
    c5.api.Interior.Color = fill_bgr

    bk.save()

    # --- post-readback ---
    after = {
        'r3_imgaeun': ws.range('C3').value,
        'sato_start': ws.range((first_row, 3)).value,
        'next_cust_row': first_row + new_count,
        'next_cust_name': ws.range((first_row + new_count, 3)).value,
    }
    print('AFTER:', after)
    print(f'DELETED {old_count} rows, INSERTED {new_count} rows '
          f'(net {new_count - old_count:+d})')


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
