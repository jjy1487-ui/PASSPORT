# -*- coding: utf-8 -*-
"""day6.json 사토 유토(55) / day7.json 신유준(56 alt) 결함을 PCR 기반으로 전환.

C. day6 사토 유토(55): 여권 사진결함 제거 + PCR 이름 불일치.
D. day7 신유준(56) altVariant: 여권 만료일결함 제거 + PCR 검사일 오류.

원칙: 멱등 · 다른 손님/필드 불변 · JSON 구조(라인수/케이스) 보존.
백업: 편집 직전 day6/day7.json 을 _backup_pcr5556_<ts>/ 로 1회 사본(없을 때만).
"""
import os
import sys
import json
import shutil
import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GD = os.path.join(ROOT, 'Assets', 'Resources', 'GameData')
TS = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')


def load(name):
    with open(os.path.join(GD, name), encoding='utf-8') as f:
        return json.load(f)


def save(name, data):
    with open(os.path.join(GD, name), 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')


def find_customer(day, cid):
    for c in day['customers']:
        if str(c['customerId']) == str(cid):
            return c
    raise RuntimeError(f'customer {cid} not found')


def get_doc(container, doc_type):
    for d in container['documents']:
        if d['documentType'] == doc_type:
            return d
    return None


def get_field(doc, key):
    for f in doc['fields']:
        if f.get('key') == key:
            return f
    return None


def find_case(container, game_result):
    for c in container['dialogueCases']:
        if c.get('gameResult') == game_result:
            return c
    return None


def backup_once():
    dst = os.path.join(GD, f'_backup_pcr5556_{TS}')
    os.makedirs(dst, exist_ok=True)
    for n in ('day6.json', 'day7.json'):
        d = os.path.join(dst, n)
        if not os.path.exists(d):
            shutil.copy2(os.path.join(GD, n), d)
    print(f'  backup -> {os.path.basename(dst)}/')


# ── C. day6 사토 유토(55) ────────────────────────────────────────────
def edit_day6():
    changed = []
    day = load('day6.json')
    sato = find_customer(day, 55)
    assert sato['nameKr'] == '사토 유토', sato['nameKr']
    self_face = sato['spriteRef']  # 본인 얼굴 = customer.spriteRef (다나카 하루토)

    # 여권: 사진결함 제거 → 정상, 본인 얼굴 복구
    pp = get_doc(sato, '여권')
    if pp['variant'] != '정상':
        pp['variant'] = '정상'; changed.append('여권.variant=정상')
    if pp['violationField'] != '없음':
        pp['violationField'] = '없음'; changed.append('여권.violationField=없음')
    if pp.get('spriteRef') != self_face:
        old = pp.get('spriteRef'); pp['spriteRef'] = self_face
        changed.append(f'여권.spriteRef {old}->{self_face}')

    pp_name = get_field(pp, 'name')['value']  # SATO YUTO

    # PCR검사서: 비정상 + 이름 불일치. 맨 앞에 성명 칸 추가(키 name, 값 TANAKA YUMA).
    pcr = get_doc(sato, 'PCR검사서')
    if pcr['variant'] != '비정상':
        pcr['variant'] = '비정상'; changed.append('PCR.variant=비정상')
    if pcr['violationField'] != '이름':
        pcr['violationField'] = '이름'; changed.append('PCR.violationField=이름')
    FAKE_NAME = 'TANAKA YUMA'  # 여권 name(SATO YUTO)과 불일치
    nf = get_field(pcr, 'name')
    if nf is None:
        pcr['fields'].insert(0, {'label': '성명', 'value': FAKE_NAME, 'key': 'name'})
        changed.append(f'PCR.fields[0] 성명 추가={FAKE_NAME}')
    else:
        # 멱등: 위치(맨 앞)·값 보정
        if pcr['fields'][0] is not nf:
            pcr['fields'].remove(nf)
            pcr['fields'].insert(0, nf)
            changed.append('PCR 성명칸 맨앞 재배치')
        if nf['value'] != FAKE_NAME:
            nf['value'] = FAKE_NAME; changed.append(f'PCR 성명 값={FAKE_NAME}')
        if nf.get('label') != '성명':
            nf['label'] = '성명'; changed.append('PCR 성명 label')

    # 정상거절 대사 lines[0]
    rej = find_case(sato, '정상 거절')
    line0 = rej['lines'][0]
    want_text = 'PCR 검사서의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.'
    want_claim = {'attr': 'name', 'value': '', 'label': '검사서 이름 불일치', 'unlocksScan': ''}
    if line0.get('text') != want_text:
        line0['text'] = want_text; changed.append('정상거절 line0.text')
    if line0.get('claim') != want_claim:
        line0['claim'] = want_claim; changed.append('정상거절 line0.claim')

    # correctResult 유지(정상 거절)
    assert sato['correctResult'] == '정상 거절', sato['correctResult']

    if changed:
        save('day6.json', day)
    print(f'[C] day6 사토(55): {len(changed)} changes')
    for c in changed:
        print('    -', c)
    print(f'    (pp_name={pp_name}, pcr_name={FAKE_NAME})')
    return len(changed)


# ── D. day7 신유준(56) altVariant ────────────────────────────────────
def edit_day7():
    changed = []
    day = load('day7.json')
    shin = find_customer(day, 56)
    assert shin['nameKr'] == '신유준', shin['nameKr']

    # 메인 여권 만료일/발급일 = 정상값(alt 복구 기준)
    main_pp = get_doc(shin, '여권')
    main_expiry = get_field(main_pp, 'expiry_date')['value']  # 2029-07-14
    main_issue = get_field(main_pp, 'issue_date')['value']

    alt = shin['altVariant']

    # alt 여권: 만료일결함 제거 → 정상, 만료일/발급일 메인값으로 복구
    alt_pp = get_doc(alt, '여권')
    if alt_pp['variant'] != '정상':
        alt_pp['variant'] = '정상'; changed.append('alt여권.variant=정상')
    if alt_pp['violationField'] != '없음':
        alt_pp['violationField'] = '없음'; changed.append('alt여권.violationField=없음')
    ef = get_field(alt_pp, 'expiry_date')
    if ef['value'] != main_expiry:
        old = ef['value']; ef['value'] = main_expiry
        changed.append(f'alt여권.expiry {old}->{main_expiry}')
    isf = get_field(alt_pp, 'issue_date')
    if isf and isf['value'] != main_issue:
        old = isf['value']; isf['value'] = main_issue
        changed.append(f'alt여권.issue {old}->{main_issue}')

    # alt PCR검사서: 비정상 + 검사일 오류. issue_date(검사일) → 미래 2026-06-15.
    alt_pcr = get_doc(alt, 'PCR검사서')
    if alt_pcr['variant'] != '비정상':
        alt_pcr['variant'] = '비정상'; changed.append('altPCR.variant=비정상')
    if alt_pcr['violationField'] != '검사일':
        alt_pcr['violationField'] = '검사일'; changed.append('altPCR.violationField=검사일')
    FUTURE_DATE = '2026-06-15'  # day7=2026-06-07 이후 미래 = 논리 오류
    df = get_field(alt_pcr, 'issue_date')  # 검사일 (label '검사일')
    if df['value'] != FUTURE_DATE:
        old = df['value']; df['value'] = FUTURE_DATE
        changed.append(f'altPCR.검사일 {old}->{FUTURE_DATE}')

    # alt 정상거절 lines[0]
    rej = find_case(alt, '정상 거절')
    line0 = rej['lines'][0]
    want_text = 'PCR 검사일 정보에 오류가 있어 입국하실 수 없습니다.'
    want_claim = {'attr': 'issue_date', 'value': '', 'label': '검사일 오류', 'unlocksScan': ''}
    if line0.get('text') != want_text:
        line0['text'] = want_text; changed.append('alt정상거절 line0.text')
    if line0.get('claim') != want_claim:
        line0['claim'] = want_claim; changed.append('alt정상거절 line0.claim')

    # correctResult 유지
    assert alt['correctResult'] == '정상 거절', alt['correctResult']

    if changed:
        save('day7.json', day)
    print(f'[D] day7 신유준(56 alt): {len(changed)} changes')
    for c in changed:
        print('    -', c)
    print(f'    (main_expiry={main_expiry}, future_pcr={FUTURE_DATE})')
    return len(changed)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    backup_once()
    n = edit_day6() + edit_day7()
    print(f'\nTOTAL json changes: {n}')
