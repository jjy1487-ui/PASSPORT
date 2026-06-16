# -*- coding: utf-8 -*-
"""대사_스크립트.xlsx(저작 원본) ↔ dayN.json(게임 박힌 대사) 드리프트 감사.

초점:
 1) 결함 손님의 '대조'(사진/이름/번호/기간/성별…) 대사가 엑셀엔 있는데 게임 crossCheckLines엔 비었는가
 2) 기본 대사(인삿말/입국 허가/입국 거부) 텍스트 드리프트

실행: python Tools/QA/audit_dialogue_drift.py [day]
"""
import os, sys, json, glob, re
import openpyxl
sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
XLSX = os.path.join(ROOT, 'data', '대사_스크립트.xlsx')
GAMEDIR = os.path.join(ROOT, 'Assets', 'Resources', 'GameData')

def parse_excel_day(ws):
    """Day 시트 → 손님 블록 리스트. 각 블록 = {slot,name,type,docstate,beats:[(분기,화자,대사)]}"""
    rows = list(ws.iter_rows(values_only=True))
    # 헤더 행 찾기
    hidx = None
    for i, r in enumerate(rows[:5]):
        if r and r[0] == '일차':
            hidx = i; break
    if hidx is None: return []
    blocks = []
    cur = None
    fill = {'slot': None, 'name': None, 'type': None, 'docstate': None}
    for r in rows[hidx+1:]:
        if not r or all(x is None for x in r): continue
        slot = r[1]; name = r[2]; typ = r[3]; doc = r[4]
        branch = r[5] if len(r) > 5 else None
        speaker = r[6] if len(r) > 6 else None
        text = r[7] if len(r) > 7 else None
        if name not in (None, ''):
            # 새 손님 블록
            cur = {'slot': slot, 'name': str(name).strip(), 'type': (str(typ).strip() if typ else ''),
                   'docstate': (str(doc).strip() if doc else ''), 'beats': []}
            blocks.append(cur)
            fill = {'slot': slot, 'name': cur['name'], 'type': cur['type'], 'docstate': cur['docstate']}
        if cur is None: continue
        if (speaker not in (None, '')) or (text not in (None, '')) or (branch not in (None, '')):
            cur['beats'].append((str(branch).strip() if branch else '',
                                 str(speaker).strip() if speaker else '',
                                 str(text).strip() if text else ''))
    return blocks

def load_game_day(n):
    p = os.path.join(GAMEDIR, f'day{n}.json')
    if not os.path.exists(p): return None
    return json.load(open(p, encoding='utf-8'))

CROSS_BEAT = re.compile(r'대조|취조|추궁')

def audit_day(n, verbose=True):
    wb = openpyxl.load_workbook(XLSX, read_only=True, data_only=True)
    sn = f'Day{n}'
    if sn not in wb.sheetnames: return None
    xblocks = parse_excel_day(wb[sn])
    g = load_game_day(n)
    if g is None: return None
    gcust = {c.get('slot'): c for c in g.get('customers', [])}
    gname = {}
    for c in g.get('customers', []):
        gname.setdefault(str(c.get('nameKr')), c)

    missing = []   # 엑셀에 대조대사 있는데 게임 crossCheckLines 없음
    present = []   # 둘 다 있음
    for b in xblocks:
        cross_beats = [bt for bt in b['beats'] if CROSS_BEAT.search(bt[0])]
        if not cross_beats:
            continue
        # 게임 손님 매칭: slot 우선, 없으면 이름
        gc = gcust.get(b['slot']) or gname.get(b['name'])
        if gc is None:
            missing.append((b, None, 'NO_GAME_MATCH')); continue
        alt = gc.get('altVariant') or {}
        ccl = (gc.get('crossCheckLines') or []) + (alt.get('crossCheckLines') or [])
        viol = [d.get('violationField') for d in gc.get('documents', []) if d.get('variant') == '비정상']
        if not viol:
            viol = [d.get('violationField') for d in (alt.get('documents') or []) if d.get('variant') == '비정상']
        if not ccl:
            missing.append((b, gc, viol))
        else:
            present.append((b, gc, viol))
    return {'day': n, 'xblocks': xblocks, 'missing': missing, 'present': present,
            'n_excel_cross': sum(1 for b in xblocks if any(CROSS_BEAT.search(x[0]) for x in b['beats']))}

if __name__ == '__main__':
    days = [int(sys.argv[1])] if len(sys.argv) > 1 else range(1, 15)
    grand_missing = 0
    for n in days:
        r = audit_day(n)
        if r is None:
            print(f'day{n}: (시트/파일 없음)'); continue
        print(f'\n===== day{n} =====  엑셀 대조비트 손님 {r["n_excel_cross"]}명 | 게임 누락 {len(r["missing"])} / 보유 {len(r["present"])}')
        for b, gc, viol in r['missing']:
            cid = gc.get('customerId') if gc else '?'
            print(f'  [누락] slot{b["slot"]} {b["name"]} (cid{cid}, 위반{viol}) — 엑셀 대조대사:')
            for br, sp, tx in b['beats']:
                if CROSS_BEAT.search(br):
                    print(f'        ({br}) {sp}: {tx}')
            grand_missing += 1
    print(f'\n######## 총 crossCheckLines 누락(엑셀엔 대조대사 있음): {grand_missing}명 ########')
