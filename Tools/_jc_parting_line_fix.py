# -*- coding: utf-8 -*-
"""존 카터(Day11) 퇴장 대사 1줄(H31)을 대사_스크립트.xlsx에 라이브 반영.
OLD에 'It's just business' 포함 확인 후에만 교체(불일치면 중단)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_live_edit import get_book

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'data', '대사_스크립트.xlsx')

ROW = 31  # Day11 시트, H열(8) = 존 카터 정상거절(정답) 퇴장 대사
OLD_SUB = "It's just business"
NEW = "(끌려가며) You'll regret this. (…이거, 후회하게 될 거야.)"

bk, opened = get_book(SRC)
try:
    ws = bk.sheets['Day11']
    cur = ws.range((ROW, 8)).value or ''
    if OLD_SUB not in cur:
        print('ABORT: H%d 에 옛 문구 없음 → %r' % (ROW, cur))
    else:
        ws.range((ROW, 8)).value = NEW
        bk.save()
        print('OK saved H%d' % ROW)
        print('now=', repr(ws.range((ROW, 8)).value))
finally:
    if opened:
        bk.app.quit()
