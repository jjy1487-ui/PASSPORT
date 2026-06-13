# -*- coding: utf-8 -*-
"""사토 유토(55, day6 s7) / 신유준(56, day7 s2) 결함을 PCR 기반으로 전환 (엑셀 권위).

대상 엑셀 2종 (둘 다 Excel에 열려 있을 수 있어 xlwings/COM 으로 라이브 편집):
  A. data/대사_스크립트.xlsx  — Day6 사토 / Day7 신유준 '불량' 블록을 PCR 버전으로 교체
  B. data/일자별_결함배분표.xlsx — 6DAY 사토 / 7DAY 신유준 결함 셀 교체

원칙: 멱등(이미 목표값이면 무변경) · 인삿말/정상블록 보존 · '불량' 블록 셀만 교체.
백업: 편집 직전 각 파일을 타임스탬프 .bak_pcr5556_<ts>.xlsx 로 1회 사본(이미 같은 내용이면 skip).
"""
import os
import sys
import datetime
import shutil
import xlwings as xw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'data')
TS = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')


def _open(path):
    """열려 있으면 attach, 아니면 invisible open. (book, opened_by_us)."""
    base = os.path.basename(path).lower()
    for app in xw.apps:
        for bk in app.books:
            try:
                if os.path.basename(bk.fullname).lower() == base:
                    return bk, False
            except Exception:
                pass
    app = xw.App(visible=False)
    app.display_alerts = False
    return app.books.open(path), True


def backup(path):
    """편집 전 디스크 사본 백업(파일이 닫혀 있어야 정확하지만, 열려 있어도 마지막 저장본 복사)."""
    dst = path.replace('.xlsx', f'.bak_pcr5556_{TS}.xlsx')
    if not os.path.exists(dst):
        shutil.copy2(path, dst)
        print(f'  backup -> {os.path.basename(dst)}')
    return dst


# ── 대사_스크립트.xlsx 목표 셀값 (시트, row, col1-based) → 값 ────────────
# Day6 사토 유토 블록(rows 63~71): 인삿말/허가오판/손님반응 보존, 불량블록만 PCR.
DIALOGUE_EDITS = {
    'Day6': {
        (63, 5): '불량(PCR 이름 불일치)',
        (64, 6): '서류 대조(PCR ↔ 여권)',
        (64, 8): 'PCR 검사서 이름과 여권 이름이 다른데요?',
        (66, 8): '검사서가 본인 것이어야 합니다.',
        (69, 8): 'PCR 검사서의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.',
    },
    'Day7': {
        (18, 5): '불량(PCR 검사일 오류)',
        (19, 6): '서류 대조(PCR ↔ 오늘 날짜)',
        (19, 8): 'PCR 검사일이 오늘 이후로 찍혀 있는데요? 논리상 맞지 않습니다.',
        (21, 8): '검사일 정보가 정확해야 합니다.',
        (24, 8): 'PCR 검사일 정보에 오류가 있어 입국하실 수 없습니다.',
    },
}

# 안전 가드: 편집 row 의 이름 칸(col3) 또는 블록 식별을 확인
DIALOGUE_GUARD = {
    'Day6': (63, 3, '사토 유토'),
    'Day7': (18, 3, '신유준'),
}

# ── 일자별_결함배분표.xlsx 목표 셀 ────────────────────────────────────
# (시트, row, col) → (손님이름 가드, 새 결함값)
DEFECT_EDITS = {
    '6DAY': {'guard': (13, 2, '사토 유토'), 'cell': (13, 5), 'value': 'PCR 이름 불일치(검사서≠여권)'},
    '7DAY': {'guard': (8, 2, '신유준'), 'cell': (8, 5), 'value': '[불량] PCR 검사일 오류'},
}


def edit_dialogue():
    path = os.path.join(DATA, '대사_스크립트.xlsx')
    print(f'[A] {os.path.basename(path)}')
    backup(path)
    bk, opened = _open(path)
    changed = 0
    try:
        for sheet, edits in DIALOGUE_EDITS.items():
            ws = bk.sheets[sheet]
            grow, gcol, gname = DIALOGUE_GUARD[sheet]
            actual = ws.range((grow, gcol)).value
            if actual is None or gname not in str(actual):
                raise RuntimeError(f'{sheet} R{grow}C{gcol} 가드 실패: 기대 "{gname}", 실제 "{actual}"')
            for (r, c), val in edits.items():
                cur = ws.range((r, c)).value
                if cur == val:
                    continue
                ws.range((r, c)).value = val
                changed += 1
                print(f'  {sheet} R{r}C{c}: {cur!r} -> {val!r}')
        if changed:
            bk.save()
        print(f'  {"saved" if changed else "no change (idempotent)"} ({changed} cells)')
    finally:
        if opened:
            bk.app.quit()
    return changed


def edit_defect_table():
    path = os.path.join(DATA, '일자별_결함배분표.xlsx')
    print(f'[B] {os.path.basename(path)}')
    backup(path)
    bk, opened = _open(path)
    changed = 0
    try:
        for sheet, spec in DEFECT_EDITS.items():
            ws = bk.sheets[sheet]
            grow, gcol, gname = spec['guard']
            actual = ws.range((grow, gcol)).value
            if actual is None or gname not in str(actual):
                raise RuntimeError(f'{sheet} R{grow}C{gcol} 가드 실패: 기대 "{gname}", 실제 "{actual}"')
            r, c = spec['cell']
            val = spec['value']
            cur = ws.range((r, c)).value
            if cur != val:
                ws.range((r, c)).value = val
                changed += 1
                print(f'  {sheet} R{r}C{c}: {cur!r} -> {val!r}')
        if changed:
            bk.save()
        print(f'  {"saved" if changed else "no change (idempotent)"} ({changed} cells)')
    finally:
        if opened:
            bk.app.quit()
    return changed


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    n = edit_dialogue()
    n += edit_defect_table()
    print(f'\nTOTAL cells changed: {n}')
