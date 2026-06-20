# -*- coding: utf-8 -*-
"""day12.json 의 사토 하루키(slot2) scenario 노드그래프를 읽어
대사_스크립트.xlsx 'Day12' 시트의 8컬럼(일차/순서/방문객/유형/서류상태/분기/화자/대사)
행 리스트로 변환한다. 권위 소스 = day12.json. (재현 가능)

레이아웃 템플릿: 특수캐릭터_스크립트_260618.xlsx 'Day 12_사토 하루키' 시트
(── 섹션 헤더 ──, [분기점N], ▶[분기점N-x], 분기 라벨 F열, 선택 행 + 타이머 표기).
내용은 전부 day12.json 기준.
"""
import os
import json

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DAY12_JSON = os.path.join(ROOT, 'Assets', 'Resources', 'GameData', 'day12.json')

NAME = '사토 하루키'
TYPE = '테러범'
DOCSTATE = '불량(X-ray 폭발물 소지 · 테러범)'


def _sp(s):
    """speaker 매핑: 시스템 -> [시스템] (시트 관례). 그 외 그대로."""
    return '[시스템]' if s == '시스템' else s


def build_rows(day12_json=DAY12_JSON):
    with open(day12_json, 'r', encoding='utf-8') as f:
        d = json.load(f)
    # slot2 = 사토 하루키 (scenario 보유)
    cust = next(c for c in d['customers'] if c.get('scenario'))
    sc = cust['scenario']
    nodes = {n['id']: n for n in sc['nodes']}

    rows = []  # [A일차,B순서,C방문객,D유형,E서류상태,F분기,G화자,H대사]

    def add(F, G, H, A='', B='', C='', D='', E=''):
        rows.append([A, B, C, D, E, F, G, H])

    def section(text):
        rows.append(['', '', '', '', '', '', '', text])

    def _flabel(t):
        # 키워드 기반 섹션 라벨(코스메틱). 못 맞추면 빈 칸.
        if '방문 목적' in t:
            return '방문목적'
        if 'X-ray 검색' in t and '검색대' in t:
            return '전신 X-ray 검색'
        if '손이 멎' in t or '둘러져 있' in t:
            return 'X-ray 발각'
        return ''

    # ---- 공통 진입 (intro: 인사~검색대 입장) ----
    intro = nodes['intro']
    ln0 = intro['lines'][0]  # 블록 첫 행 = 식별자 적재 (인삿말)
    add('인삿말', _sp(ln0['speaker']), ln0['text'],
        A='12', B='2', C=NAME, D=TYPE, E=DOCSTATE)
    section('── 공통 진입 ──')
    for ln in intro['lines'][1:]:
        add(_flabel(ln['text']), _sp(ln['speaker']), ln['text'])

    # ---- X-ray 표시 지점(openScanAfter) → introReveal(검문관 발각 반응) ----
    if intro.get('openScanAfter'):
        section('── [전신 X-ray 검색 — 폭발물 적발 화면 표시] ──')
    reveal = nodes.get('introReveal')
    if reveal is not None:
        for ln in reveal.get('lines', []):
            add(_flabel(ln['text']), _sp(ln['speaker']), ln['text'])
        ch = reveal['choices']
    else:
        ch = intro['choices']  # 분할 전 데이터 호환
    section('[ 분기점1 — 플레이어 태도 선택 ]')
    add('분기점1', '[시스템]',
        f'▶ [{ch[0]["label"]}] → 분기점1-A(진정·경청)  /  [{ch[1]["label"]}] → 분기점1-B(강경 통제)')

    # ===== 분기점1-A : appease =====
    section(f'▶ [분기점1-A] {ch[0]["label"]} (진정·경청) — 플레이어 선택')
    ap = nodes['appease']
    for ln in ap['lines']:
        add('1-A', _sp(ln['speaker']), ln['text'])
    ac = ap['choices']
    add('1-A 선택', '[시스템]',
        f'▶ [{ac[0]["label"]}] → 분기1(설득 성공)  /  [{ac[1]["label"]}] → 분기2(폭탄)'
        f'   ⏱ 타이머 {ap["timer"]}초 · 무응답 시 분기2(폭탄)')

    # --- 분기1 : persuadeA ---
    o1 = nodes['persuadeA']['outcome']
    section(f'  ┌ [분기 1] 끝까지 들어준다 → 설교 후 스스로 돌아감  (+{o1["score"]}점)')
    for ln in nodes['persuadeA']['lines']:
        add('분기1', _sp(ln['speaker']), ln['text'])
    add(f'분기1 결과 (+{o1["score"]}점)', '[시스템]',
        f'결과: {o1["result"]} · 점수 +{o1["score"]} · 보상: {o1["reward"]}')

    # --- 분기2 : bombA ---
    o2 = nodes['bombA']['outcome']
    section(f'  └ [분기 2] 네, 가끔은요 (동조) → 폭탄 투척  (+{o2["score"]}점)')
    for ln in nodes['bombA']['lines']:
        add('분기2', _sp(ln['speaker']), ln['text'])
    add(f'분기2 결과 (+{o2["score"]}점)', '[시스템]',
        f'결과: {o2["result"]} · 점수 +{o2["score"]} · 보상: {o2["reward"]}')

    # ===== 분기점1-B : demand =====
    section(f'▶ [분기점1-B] {ch[1]["label"]} (강경 통제) — 플레이어 선택')
    dm = nodes['demand']
    for ln in dm['lines']:
        add('1-B', _sp(ln['speaker']), ln['text'])
    dc = dm['choices']
    section('[ 분기점2 — 말 걸기  /  신고버튼 ]')
    add('분기점2', '[시스템]',
        f'▶ [{dc[0]["label"]}] → 분기점2-말걸기  /  [{dc[1]["label"]}] → 분기점2-신고')

    # --- 분기점2 말걸기 : talk ---
    section('  ┬ [분기점2-말걸기] 무슨 일이신데 그러세요.. 저한테 말해주세요')
    tk = nodes['talk']
    for ln in tk['lines']:
        add('2-말', _sp(ln['speaker']), ln['text'])
    tc = tk['choices']
    add('2-말 선택', '[시스템]',
        f'▶ [{tc[0]["label"]}] → 분기3(설득 성공)  /  [{tc[1]["label"]}] → 분기4(폭탄)'
        f'   ⏱ 타이머 {tk["timer"]}초 · 무응답 시 분기4(폭탄)')

    # --- 분기3 : persuadeB ---
    o3 = nodes['persuadeB']['outcome']
    section(f'  ├ [분기 3] 진심으로 듣는다 → 설교 후 스스로 돌아감  (+{o3["score"]}점)')
    for ln in nodes['persuadeB']['lines']:
        add('분기3', _sp(ln['speaker']), ln['text'])
    add(f'분기3 결과 (+{o3["score"]}점)', '[시스템]',
        f'결과: {o3["result"]} · 점수 +{o3["score"]} · 보상: {o3["reward"]}  (설교 18줄 분기1과 동일)')

    # --- 분기4 : bombB ---
    o4 = nodes['bombB']['outcome']
    section(f'  └ [분기 4] 많이 힘드셨겠네요 (상투적 동정) → 폭탄 투척  (+{o4["score"]}점)')
    for ln in nodes['bombB']['lines']:
        add('분기4', _sp(ln['speaker']), ln['text'])
    add(f'분기4 결과 (+{o4["score"]}점)', '[시스템]',
        f'결과: {o4["result"]} · 점수 +{o4["score"]} · 보상: {o4["reward"]}')

    # --- 분기점2 신고 : report ---
    section('  ┬ [분기점2-신고] (조용히) 신고버튼을 누른다')
    rp = nodes['report']
    for ln in rp['lines']:
        add('2-신고', _sp(ln['speaker']), ln['text'])
    rc = rp['choices']
    add('2-신고 선택', '[시스템]',
        f'▶ [{rc[0]["label"]}] → 분기5(신고 성공)  /  [{rc[1]["label"]}] → 분기6(신고 발각)'
        f'   ⏱ 타이머 {rp["timer"]}초 · 무응답 시 분기6(발각)')

    # --- 분기5 : reportSafe ---
    o5 = nodes['reportSafe']['outcome']
    section(f'  ├ [분기 5] 책상 밑 비상 호출버튼 → 경찰 출동 · 테러 방지  (+{o5["score"]}점)')
    for ln in nodes['reportSafe']['lines']:
        add('분기5', _sp(ln['speaker']), ln['text'])
    add(f'분기5 결과 (+{o5["score"]}점)', '[시스템]',
        f'결과: {o5["result"]} · 점수 +{o5["score"]} · 보상: {o5["reward"]}')

    # --- 분기6 : reportCaught ---
    o6 = nodes['reportCaught']['outcome']
    section(f'  └ [분기 6] 사내 메신저로 컴퓨터에 알림 → 발각 → 폭탄 즉시 터짐  ({o6["score"]}점)')
    for ln in nodes['reportCaught']['lines']:
        add('분기6', _sp(ln['speaker']), ln['text'])
    add(f'분기6 결과 ({o6["score"]}점)', '[시스템]',
        f'결과: {o6["result"]} · 점수 {o6["score"]} · 보상: {o6["reward"]}')

    # 블록 구분 빈 행
    rows.append(['', '', '', '', '', '', '', ''])
    return rows


if __name__ == '__main__':
    import sys
    sys.stdout.reconfigure(encoding='utf-8')
    rs = build_rows()
    print('TOTAL ROWS:', len(rs))
    for i, r in enumerate(rs):
        print(f'{i:3d} | F={r[5]!r:24} G={r[6]!r:12} H={r[7][:58]!r}')
