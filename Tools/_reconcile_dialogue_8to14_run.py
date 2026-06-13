# -*- coding: utf-8 -*-
"""Day8~14 정합 실행기. _reconcile_dialogue_8to14.py 의 템플릿을 사용해
각 시트의 블록을 권위 소스에 맞게 교체하고 저장한다. 멱등."""
import os
import sys
import importlib.util

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, 'Tools')
sys.path.insert(0, TOOLS)

spec = importlib.util.spec_from_file_location(
    'rec', os.path.join(TOOLS, '_reconcile_dialogue_8to14.py'))
rec = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rec)

get_book = rec.get_book
read_grid = rec.read_grid
split_blocks = rec.split_blocks
block_key = rec.block_key
build_defect_block = rec.build_defect_block
build_normal_block = rec.build_normal_block
_hdr = rec._hdr
_cont = rec._cont
REJECT_MISJUDGE = rec.REJECT_MISJUDGE
SRC = rec.SRC


# ── 손님 기존 블록에서 ctx(인삿말/허가·거부 손님멘트) 추출 ─────────
def extract_ctx(block):
    """블록 행 리스트에서 greeting(인삿말 손님), approve_visitor, reject_visitor 등 추출.
    found 못하면 기본값."""
    greeting = ''
    approve_visitor = ''
    reject_visitor = ''
    approve_text = '즐거운 여행 되십시오.'
    fp_line = None
    # 손님(=심사관이 아닌 화자) 중간 반응 라인 수집(인삿말 이후 ~ 입국허가 이전)
    visitor_mids = []
    for i, r in enumerate(block):
        fdiv, sp, text = r[5].strip(), r[6].strip(), r[7]
        if fdiv == '인삿말':
            greeting = text
        if fdiv.startswith('입국 허가'):
            approve_text = r[7] if r[7].strip() else approve_text
            if i + 1 < len(block):
                approve_visitor = block[i + 1][7]
        if fdiv.startswith('입국 거부') and ('정답' in fdiv):
            if i + 1 < len(block):
                reject_visitor = block[i + 1][7]
        if fdiv.startswith('지문 대조'):
            fp_line = r[7]
    # 손님 중간 반응(심사관 아님, 인삿말/허가/거부 아님)
    started = False
    for r in block:
        fdiv, sp, text = r[5].strip(), r[6].strip(), r[7]
        if fdiv == '인삿말':
            started = True
            continue
        if fdiv.startswith('입국'):
            break
        if started and sp and sp != '심사관' and text.strip():
            visitor_mids.append(text)
    ctx = {
        'greeting': greeting or '안녕하세요.',
        'approve_text': approve_text,
        'approve_visitor': approve_visitor or '감사합니다.',
        'reject_visitor': reject_visitor or '…알겠습니다.',
    }
    if fp_line:
        ctx['fp_line'] = fp_line
    if visitor_mids:
        ctx['xray_mid1'] = visitor_mids[0]
        ctx['xray_mid2'] = visitor_mids[1] if len(visitor_mids) > 1 else visitor_mids[0]
        ctx['visitor_mid'] = visitor_mids[0]
    return ctx


# ── X-ray 결합 블록 (여권/번호 위조 대조 + X-ray 적발) ─────────────
def build_xray_block(day, slot, name, dtype, state_label, xray_kind, ctx,
                     wanted_alarm):
    """존카터/사토/강도식: 여권(번호)위조 대조 단계 + X-ray 적발 + 신원 경보 일치 후 거절."""
    g = ctx['greeting']
    appV = ctx['approve_visitor']
    rejV = ctx['reject_visitor']
    # xray_kind: '밀수'|'위험물'|'마약'
    detect_map = {
        '밀수': ('X-ray 검사(수하물 ↔ 금지물품 규정)', '수하물 X-ray 검사를 진행하겠습니다… 밀수 의심 물품이 확인됩니다.'),
        '위험물': ('X-ray 검사(수하물 ↔ 금지물품 규정)', '수하물 X-ray 검사를 진행하겠습니다… 위험물(폭발물)로 의심되는 물품이 확인됩니다.'),
        '마약': ('X-ray 검사(수하물 ↔ 금지물품 규정)', '수하물 X-ray 검사를 진행하겠습니다… 마약류로 의심되는 물품이 확인됩니다.'),
    }
    xf, xh = detect_map[xray_kind]
    rows = []
    rows.append(_hdr(day, slot, name, dtype, state_label, '인삿말', name, g))
    # 1) 여권(번호) 위조 대조
    if '번호' in state_label:
        rows.append(_cont('서류 대조(여권번호 ↔ 발급국)', '심사관', '여권번호를 확인하겠습니다… 앞자리가 국적(발급국)과 맞지 않는데요?'))
    else:
        rows.append(_cont('서류 대조(여권 자체 검증)', '심사관', '여권을 확인하겠습니다… 위조가 의심되는 부분이 있는데요?'))
    rows.append(_cont('', name, ctx.get('xray_mid1', '…(말없이 주위를 살핀다)')))
    # 2) X-ray 적발
    rows.append(_cont(xf, '심사관', xh))
    rows.append(_cont('', name, ctx.get('xray_mid2', '…(아무 말이 없다)')))
    # 3) 경보 신원 일치
    rows.append(_cont('경보 대조(경보 명단 ↔ 신원)', '심사관', '경보 대상과 신원도 일치합니다.'))
    # 허가(오판)
    rows.append(_cont('입국 허가 (오판)', '심사관', '즐거운 여행 되십시오.'))
    rows.append(_cont('', name, appV))
    # 거부(정답)
    rows.append(_cont('입국 거부 (정답)', '심사관', wanted_alarm))
    rows.append(_cont('', name, rejV))
    return rows


# ── 일자별 정합 규칙 ─────────────────────────────────────────────
# action 종류:
#  ('defect', defect_key)                : 기존 불량 블록을 그 결함으로 교체(ctx 보존)
#  ('xray', state_label, xray_kind, alarm): X-ray 결합 블록으로 교체
#  ('to_pass',)                          : 불량 블록 제거, 정상 블록만 유지
#  ('add_defect', defect_key)            : 정상만 있는 손님에 불량 블록 추가
#  ('roster_to', new_name, new_dtype, action...) : 손님 교체(이름/유형) + 후처리
# 키 = (slot, 현재이름)  →  대상이 무엇인지

PLAN = {
    'Day8': {
        '2': ('defect', 'passport_no'),   # 송재윤: 만료→여권번호위조
        '3': ('defect', 'nat_mismatch'),  # 장 민: 사진→국적불일치
        '6': ('defect', 'name_mismatch'), # 매튜앤더슨: 성별→이름불일치
        '7': ('keep',),                   # 전도현: 지문도용-이름 OK
    },
    'Day9': {
        '1': ('defect', 'expiry'),        # 홍성민: 생일변조→만료일
        '3': ('defect', 'visa_lie'),      # 류 옌: 국적불일치→비자종류거짓
        '4': ('roster_chae',),            # 윤정호→윤채원(여권 성별 불량)
        '5': ('defect', 'fp_birth'),      # 이지우: 지문이름→지문생일
        '6': ('defect', 'no_mismatch'),   # 크리스토머스: 사진→번호불일치
        '7': ('add_defect', 'ghost_company'),  # 자오레이: 통과→유령회사
    },
    'Day10': {
        '2': ('defect', 'passport_no'),   # 최수아: 만료→여권번호위조
        '3': ('defect', 'fp_name'),       # 정다은: 지문생일→지문이름
        '4': ('defect', 'no_mismatch'),   # 스즈키소라: 증빙번호→번호불일치
        '6': ('keep',),                   # 강예린: 만료일 OK
        '7': ('defect', 'visa_lie'),      # 첸 웨이: 사진→비자종류거짓
    },
    'Day11': {
        '1': ('xray', '불량(여권번호 위조 + X-ray 밀수)', '밀수', '신원이 확인되었습니다. 당신은 지명수배자입니다. 입국하실 수 없습니다.'),  # 존카터
        '3': ('defect', 'nat_mismatch'),  # 자오친: 비자종류→국적불일치
        '4': ('roster_jung',),            # 윤채원→윤정호(통과)
        '5': ('keep',),                   # 장소율: 지문국적 OK
        '6': ('defect', 'expiry'),        # 앤드류화이트: 직종→만료일
        '7': ('to_pass',),                # 천 징: 불량→통과
    },
    'Day12': {
        '2': ('xray', '불량(여권 위조 + X-ray 위험물)', '위험물', '위험 인물로 확인되어 입국하실 수 없습니다.'),  # 사토하루키
        '3': ('keep',),                   # 한지아: 지문이름 OK
        '4': ('defect', 'gender'),        # 오나은: 만료→여권성별
        '5': ('defect', 'fp_birth'),      # 서하린: 지문이름→지문생일
        '6': ('defect', 'visa_lie'),      # 황 레이: 국적불일치→비자종류거짓
    },
    'Day13': {
        '1': ('defect', 'name_mismatch'), # 우 팅: 비자만료→이름불일치
        '3': ('defect', 'nat_mismatch'),  # 다카하시리쿠: 발급일→국적불일치
        '4': ('defect', 'hire_logic'),    # 제시카윌슨: 일반→입사일모순
        '5': ('keep',),                   # 황민서: 만료 OK
        '6': ('defect', 'fp_nat'),        # 배은서: 지문이름→지문국적
        '7': ('defect', 'visa_lie'),      # 쉬 펑: 발급일→비자종류거짓
    },
    'Day14': {
        '2': ('defect', 'no_mismatch'),   # 선 메이: 사진→번호불일치
        '3': ('xray', '불량(여권번호 위조 + X-ray 마약)', '마약', '신원이 확인되었습니다. 당신은 지명수배자입니다. 입국하실 수 없습니다.'),  # 강도식
        '5': ('defect', 'fp_name'),       # 양수빈: 지문생일→지문이름
        '6': ('keep',),                   # 에밀리클락: 여권사진 OK
    },
}


def process_sheet(ws, day_label, plan):
    grid = read_grid(ws)
    header, blocks = split_blocks(grid)
    # index blocks by (slot, name, is_defect)
    out_blocks = []  # 최종 블록 순서대로
    changes = []     # (slot, name, before_state, after_state)

    # 손님별로 정상/불량 블록을 묶기 위해 slot 기준 그룹화
    # 순서 보존하며 블록 처리.
    # roster 교체는 손님 단위라 특수 처리.
    # 먼저 slot->blocks 매핑
    from collections import OrderedDict
    by_slot = OrderedDict()
    for b in blocks:
        day, slot, name, state = block_key(b)
        by_slot.setdefault(slot, []).append(b)

    for slot, slotblocks in by_slot.items():
        name0 = block_key(slotblocks[0])[2]
        action = plan.get(slot)
        if action is None or action[0] == 'keep':
            out_blocks.extend(slotblocks)
            if action and action[0] == 'keep':
                st = '/'.join(block_key(b)[3] for b in slotblocks)
                changes.append((slot, name0, st, st + ' (유지)'))
            continue

        a0 = action[0]
        day = block_key(slotblocks[0])[0]

        if a0 == 'defect':
            defect = action[1]
            # 정상 블록 유지, 불량 블록 교체
            new_slot = []
            before = []
            after = []
            for b in slotblocks:
                _, sl, nm, st = block_key(b)
                before.append(st)
                if st.startswith('불량'):
                    ctx = extract_ctx(b)
                    dtype = b[0][3]
                    nb = build_defect_block(day, slot, nm, dtype, defect, ctx)
                    new_slot.append(nb)
                    after.append(nb[0][4])
                else:
                    new_slot.append(b)
                    after.append(st)
            out_blocks.extend(new_slot)
            changes.append((slot, name0, '/'.join(before), '/'.join(after)))

        elif a0 == 'xray':
            state_label, xray_kind, alarm = action[1], action[2], action[3]
            # 손님은 단일 불량 블록(밀수범/테러범/마약범)
            new_slot = []
            before = []
            after = []
            for b in slotblocks:
                _, sl, nm, st = block_key(b)
                before.append(st)
                if st.startswith('불량'):
                    ctx = extract_ctx(b)
                    dtype = b[0][3]
                    nb = build_xray_block(day, slot, nm, dtype, state_label,
                                          xray_kind, ctx, alarm)
                    new_slot.append(nb)
                    after.append(state_label)
                else:
                    new_slot.append(b)
                    after.append(st)
            out_blocks.extend(new_slot)
            changes.append((slot, name0, '/'.join(before), '/'.join(after)))

        elif a0 == 'to_pass':
            # 불량 블록 제거, 정상 블록만. 정상 블록 없으면 정상 생성.
            before = [block_key(b)[3] for b in slotblocks]
            normal = [b for b in slotblocks if not block_key(b)[3].startswith('불량')]
            if normal:
                out_blocks.extend(normal)
                after = [block_key(b)[3] for b in normal]
            else:
                # 불량만 있던 손님 → 정상 블록 합성
                b = slotblocks[0]
                nm = block_key(b)[2]
                dtype = b[0][3]
                ctx = extract_ctx(b)
                nb = build_normal_block(day, slot, nm, dtype, ctx)
                out_blocks.append(nb)
                after = ['정상']
            changes.append((slot, name0, '/'.join(before), '/'.join(after)))

        elif a0 == 'add_defect':
            defect = action[1]
            before = [block_key(b)[3] for b in slotblocks]
            # 멱등: 정상 블록만 유지(기존 불량 블록은 새로 생성) + 불량 블록 1개 추가
            normal_blocks = [b for b in slotblocks
                             if not block_key(b)[3].startswith('불량')]
            base = normal_blocks[0] if normal_blocks else slotblocks[0]
            nm = block_key(base)[2]
            dtype = base[0][3]
            ctx = extract_ctx(base)
            ctx2 = dict(ctx)
            # 자오 레이(중국인) 유령회사: 적발 후 손님 반응 현지화
            if defect == 'ghost_company':
                ctx2['visitor_mid'] = '啊？应该没问题的…(어? 문제 없을 텐데요…)'
                ctx2['reject_visitor'] = '哦，原来那里有问题。'
            nb = build_defect_block(day, slot, nm, dtype, defect, ctx2)
            out_blocks.extend(normal_blocks)
            out_blocks.append(nb)
            after = [block_key(b)[3] for b in normal_blocks] + [nb[0][4]]
            changes.append((slot, name0, '/'.join(before), '/'.join(after)))

        elif a0 == 'roster_chae':
            # Day9 s4: 윤정호(정상,정치인) → 윤채원(진상): 정상 + 여권 성별 불량
            before = [block_key(b)[3] for b in slotblocks]
            b = slotblocks[0]
            dtype_new = '진상'
            # 윤채원 인삿말(Day11 진상 톤 재활용)
            g_norm = '신원 조사 때문에 이렇게 기다려야해? 빨리 처리해줘요.'
            ctx_norm = {'greeting': g_norm,
                        'approve_visitor': '그래요, 이제 가요.',
                        'reject_visitor': '말이 안 되잖아요!'}
            nb_norm = build_normal_block(day, slot, '윤채원', dtype_new, ctx_norm)
            ctx_def = {'greeting': '아니 취업이니 뭐니 더 까다로워진 거예요? 빨리 좀 해줘요.',
                       'approve_text': '즐거운 여행 되십시오.',
                       'approve_visitor': '이제 됐죠?',
                       'reject_visitor': '됐어요, 알겠어요.',
                       'visitor_mid': '네? 그럴 리가요. 다시 확인해 주세요.'}
            nb_def = build_defect_block(day, slot, '윤채원', dtype_new, 'gender', ctx_def)
            out_blocks.append(nb_norm)
            out_blocks.append(nb_def)
            changes.append((slot, '윤정호→윤채원', '/'.join(before),
                            '정상/' + nb_def[0][4]))

        elif a0 == 'roster_jung':
            # Day11 s4: 윤채원(진상,정상+만료) → 윤정호(정치인,통과 정상만)
            before = [block_key(b)[3] for b in slotblocks]
            dtype_new = '정치인'
            ctx_norm = {'greeting': '안녕하세요. 외교 일정 마치고 왔는데, 신원 확인이 강화됐다고 들었습니다. 여권 드리겠습니다.',
                        'approve_visitor': '감사합니다! 수고 많으세요.',
                        'reject_visitor': '저 분명히 제대로 챙겨왔는데요.'}
            nb_norm = build_normal_block(day, slot, '윤정호', dtype_new, ctx_norm)
            out_blocks.append(nb_norm)
            changes.append((slot, '윤채원→윤정호', '/'.join(before), '정상(통과)'))

        else:
            out_blocks.extend(slotblocks)

    return header, out_blocks, changes


def write_sheet(ws, header, blocks):
    """헤더 + 블록(블록 사이 빈 구분행 1줄)을 시트에 다시 쓴다.
    기존 영역을 먼저 클리어."""
    # 클리어 (A:H, 충분히 큰 범위)
    lastrow = ws.range((ws.cells.last_cell.row, 8)).end('up').row
    lastrow = max(lastrow, ws.range((ws.cells.last_cell.row, 3)).end('up').row, 200)
    ws.range((1, 1), (lastrow, 8)).clear_contents()
    # A,B 열을 텍스트 서식으로 → '8'/'1' 정수 표기 유지(8.0/1.0 방지)
    ws.range((3, 1), (lastrow, 2)).number_format = '@'

    # 조립
    out = []
    out.extend(header)
    for i, b in enumerate(blocks):
        out.extend(b)
        out.append(['', '', '', '', '', '', '', ''])  # 구분행
    # 마지막 구분행 제거 안 해도 무방
    # A,B 열 정수 정규화('8.0'/8.0 → '8') — 라운드트립 부동소수 오염 방지, 멱등
    import re as _re
    for row in out:
        for ci in (0, 1):
            v = row[ci]
            if v is None or v == '':
                continue
            s = str(v)
            m = _re.fullmatch(r'(\d+)\.0', s)
            if m:
                row[ci] = m.group(1)
            elif isinstance(v, float) and v == int(v):
                row[ci] = str(int(v))
            else:
                row[ci] = s
    n = len(out)
    ws.range((1, 1), (n, 8)).value = out
    return n


def main():
    bk, opened = get_book(SRC)
    try:
        all_changes = {}
        for day_label, plan in PLAN.items():
            ws = bk.sheets[day_label]
            header, blocks, changes = process_sheet(ws, day_label, plan)
            write_sheet(ws, header, blocks)
            all_changes[day_label] = changes
        bk.save()
    finally:
        if opened:
            bk.app.quit()

    # 리포트
    print('=== Day8~14 손님 상태 before → after ===')
    for day_label in ['Day8', 'Day9', 'Day10', 'Day11', 'Day12', 'Day13', 'Day14']:
        print(f'\n[{day_label}]')
        for slot, name, before, after in all_changes[day_label]:
            print(f'  s{slot} {name}: {before}  →  {after}')


if __name__ == '__main__':
    main()
