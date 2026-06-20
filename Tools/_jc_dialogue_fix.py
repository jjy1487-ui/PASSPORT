# -*- coding: utf-8 -*-
"""존 카터(Day11) 대사 확정 수정 7곳을 대사_스크립트.xlsx에 라이브 반영.
각 셀의 기존값에 '예상 옛 문구'가 들어있는지 먼저 검증 → 하나라도 불일치면 전체 중단(쓰기 0)."""
import os, sys, shutil
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
from xlsx_live_edit import get_book

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'data', '대사_스크립트.xlsx')

# (Day11 시트 행번호, 기존값에 반드시 포함돼야 할 옛 문구, 새 값)  — 모두 H열(8, 대사)
EDITS = [
    (9,  "Just personal stuff",
         "No, nothing. Just personal belongings. (없어요. 그냥 개인 소지품이에요.)"),
    (10, "옷차림이 좀 두꺼우시네요",
         "…눈을 잘 안 마주치시네요. 안색도 좀 안 좋으시고."),
    (11, "I get cold easily",
         "(살짝 웃으며) Ha. Long flight, that's all. Just tired. (하하, 긴 비행이라요. 좀 피곤할 뿐이에요.)"),
    (12, "비행 내내 그 코트",
         "손도 떨고 계시는군요. 긴장되는 일이라도 있으십니까?"),
    (13, "Just more comfortable this way",
         "Not at all. Nothing strange about it. (전혀요. 이상할 거 없어요.)"),
    (22, "코트 안주머니에서",
         "(주머니에서 묵직한 것을 꺼내 보인다) You see this? A gold bullion. Pure gold, the real thing. (이거 보여요? 금괴예요. 순금, 진짜배기.)"),
    (31, "lose faith in the system",
         "(끌려가며 돌아본다) ...! It's just business. Everyone takes the deal. Everyone but you. (이건 그냥 거래야. 다들 받아. 너만 빼고.)"),
]

# 백업(마지막 저장본)
try:
    bak = SRC + '.bak_jc_dialogfix'
    shutil.copy2(SRC, bak)
    print('BACKUP:', os.path.basename(bak))
except Exception as e:
    print('BACKUP FAILED (열린 상태일 수 있음, 계속):', e)

bk, opened = get_book(SRC)
try:
    ws = bk.sheets['Day11']
    ok = True
    for row, old_sub, _new in EDITS:
        cur = ws.range((row, 8)).value or ''
        if old_sub not in cur:
            print('MISMATCH H%d: 예상 옛 문구 "%s" 없음 → 현재=%r' % (row, old_sub, cur))
            ok = False
    if not ok:
        print('ABORTED — 쓰기 0건')
    else:
        for row, _old, new in EDITS:
            ws.range((row, 8)).value = new
        bk.save()
        print('SAVED — 7건 반영 완료')
        print('--- 반영 후 확인 ---')
        for row, _o, _n in EDITS:
            print('H%-3d %r' % (row, ws.range((row, 8)).value))
finally:
    if opened:
        bk.app.quit()
