# -*- coding: utf-8 -*-
"""대사_스크립트.xlsx Day8~14 손님 결함 흐름을 권위 소스(일자별_결함배분표.xlsx)에 맞춰 정합.

- 권위 소스 = data/일자별_결함배분표.xlsx (8DAY~14DAY): 각 손님 목표 결함.
- 본 스크립트는 data/대사_스크립트.xlsx 의 Day8~Day14 시트만 수정한다(Day1~7 불변).
- 인삿말 라인과 정상 블록은 보존, 불량 블록의 상태라벨(E)·대조 분기(F/H)·입국거부 정답(H)만 교체.
- 대사 패턴은 같은 엑셀 기존 블록 재활용(Day2~7 참조), 신규 결함은 plan 지정 멘트.
- 멱등: 같은 목표 상태면 변경 없음(이미 일치하면 skip).
- xlwings 라이브 편집(파일 열려 있어도 attach). 백업은 호출 전 별도 생성(bak_8to14).

블록 모델: 한 시트는 빈 구분행(전 셀 공백)으로 블록 분리.
 각 블록의 첫 행에 A(일차) B(순서) C(이름) D(유형) E(서류상태) 가 채워지고,
 F(분기)/G(화자)/H(대사) 가 행마다. 같은 손님의 정상/불량은 별도 블록.
"""
import os
import sys
import xlwings as xw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'data', '대사_스크립트.xlsx')


def get_book(path):
    base = os.path.basename(path).lower()
    for app in xw.apps:
        for b in app.books:
            try:
                if os.path.basename(b.fullname).lower() == base:
                    return b, False
            except Exception:
                pass
    app = xw.App(visible=False)
    app.display_alerts = False
    return app.books.open(path), True


# ── 셀 읽기/쓰기 헬퍼 ─────────────────────────────────────────────

def read_grid(ws):
    """A:H 전체를 2D 리스트(문자열)로. 빈 셀은 ''. 헤더 포함 last H행까지."""
    lastH = ws.range((ws.cells.last_cell.row, 8)).end('up').row
    last = max(lastH, ws.range((ws.cells.last_cell.row, 3)).end('up').row)
    vals = ws.range((1, 1), (last, 8)).value
    grid = []
    for row in vals:
        grid.append([('' if c is None else str(c)) for c in row])
    return grid


def is_sep(r):
    return all((c or '').strip() == '' for c in r)


def split_blocks(grid):
    """헤더(1~2행) 보존, 3행부터 블록 분리. 반환: (header_rows, blocks)
    각 block = (start_idx, rows[list of 8-col]) — 구분행 제외."""
    # 헤더: 1행(제목), 2행(컬럼명) 까지 보존
    header = grid[:2]
    body = grid[2:]
    blocks = []
    cur = []
    for r in body:
        if is_sep(r):
            if cur:
                blocks.append(cur)
                cur = []
        else:
            cur.append(r)
    if cur:
        blocks.append(cur)
    return header, blocks


def block_key(block):
    """블록 헤더에서 (day, slot, name, state) 추출."""
    first = block[0]
    return (first[0].strip(), first[1].strip(), first[2].strip(), first[4].strip())


# ── 결함 블록 템플릿 (행 리스트 생성) ────────────────────────────
# row = [A,B,C,D,E,F,G,H]  단, A/B/C/D/E 는 첫 행에만 채움(나머지 빈칸)
# 호출부에서 day/slot/name/dtype/greeting/reactions 주입.

REJECT_MISJUDGE = '확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다.'


def _hdr(day, slot, name, dtype, state, fdiv, sp, text):
    return [day, slot, name, dtype, state, fdiv, sp, text]


def _cont(fdiv, sp, text):
    return ['', '', '', '', '', fdiv, sp, text]


def build_defect_block(day, slot, name, dtype, defect, ctx):
    """defect 키별 불량 블록 행 리스트 생성.
    ctx = {greeting, approve_sp, approve_text, approve_visitor,
           reject_visitor, visitor_react(중간 손님 반응)}
    인삿말/허가반응/거부반응 손님멘트는 기존 블록에서 가져온 ctx 사용."""
    g = ctx['greeting']
    vmid = ctx.get('visitor_mid', '네? 그럴 리가요. 다시 확인해 주세요.')
    appT = ctx['approve_text']
    appV = ctx['approve_visitor']
    rejV = ctx['reject_visitor']
    rows = []

    def emit_tail(state):
        # 입국 허가(오판) → 입국 거부(정답)
        return rows

    if defect == 'passport_no':
        state = '불량(여권번호 위조)'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권번호 ↔ 발급국)', '심사관', '여권번호를 확인하겠습니다… 앞자리가 국적(발급국)과 맞지 않는데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '여권번호 앞자리는 발급국과 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '여권번호가 발급국 코드와 일치하지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'gender':
        state = '불량(여권 성별 불일치)'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권 성별 ↔ 본인)', '심사관', '여권 정보를 확인하겠습니다… 성별이 본인과 다른데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '성별이 본인과 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '여권의 성별이 본인과 일치하지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'expiry':
        state = '불량(여권 만료일 경과)'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권 만료일 ↔ 오늘 날짜)', '심사관', '여권 만료일을 보겠습니다… 기간이 지나 있네요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '유효기간이 지난 여권으로는 입국이 불가합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '여권 유효기간이 맞지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'nat_mismatch':  # 국적 불일치(여권≠비자)
        state = '불량(국적 불일치(여권≠비자))'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권 ↔ 비자 국적)', '심사관', '여권과 비자의 국적이 다른데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '여권과 비자의 국적이 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'name_mismatch':  # 이름 불일치(여권≠비자)
        state = '불량(이름 불일치(여권≠비자))'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권 ↔ 비자)', '심사관', '여권과 비자를 대조합니다… 영문 이름이 서로 다른데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '여권과 비자의 이름이 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '비자의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'no_mismatch':  # 번호 불일치(여권≠비자) — 신규 멘트
        state = '불량(번호 불일치(여권≠비자))'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(여권 ↔ 비자 여권번호)', '심사관', '여권과 비자의 번호가 서로 다른데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '여권과 비자의 여권번호가 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '비자의 여권번호가 여권과 일치하지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'visa_lie':  # 비자종류 거짓 - 진술
        state = '불량(비자종류 거짓 - 진술)'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('음성 대조(음성기록 ↔ 비자)', '심사관', "음성기록엔 '관광'이라 하셨는데, 비자의 방문 목적은 '장기 체류'네요?"))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '진술하신 방문 목적이 비자와 일치해야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'ghost_company':  # 유령회사(입국 금지 회사) — 신규 멘트
        state = '불량(유령회사(입국 금지 회사))'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(재직증명서 ↔ 입국 금지 회사 목록)', '심사관', '재직 회사가 입국 금지 목록에 있는데요?'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '입국이 제한된 회사 소속은 입국이 불가합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '입국이 제한된 회사 소속으로 확인되어 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect == 'hire_logic':  # 취업증빙 입사일 모순 — 신규 멘트
        state = '불량(취업증빙 입사일 모순)'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('서류 대조(재직증명서 입사일 ↔ 비자 발급일)', '심사관', '재직증명서 입사일이 비자 발급일보다 빠른데요? 모순됩니다.'))
        rows.append(_cont('', name, vmid))
        rows.append(_cont('', '심사관', '입사일이 논리에 맞아야 입국이 가능합니다.'))
        rows.append(_cont('입국 허가 (오판)', '심사관', appT))
        rows.append(_cont('', name, appV))
        rows.append(_cont('입국 거부 (정답)', '심사관', '재직 정보의 입사일이 맞지 않아 입국하실 수 없습니다.'))
        rows.append(_cont('', name, rejV))

    elif defect in ('fp_name', 'fp_birth', 'fp_nat'):
        # 지문 도용 — 성형수술고객 전용 흐름
        if defect == 'fp_name':
            state = '불량(지문 이름 불일치)'
            fp_line = ctx.get('fp_line', '지문 조회 결과… 등록된 신원이 여권 이름과 일치하지 않습니다.')
            reject = '지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.'
        elif defect == 'fp_birth':
            state = '불량(지문 생년월일 불일치)'
            fp_line = ctx.get('fp_line', '지문 조회 결과… 등록 생년월일이 여권과 일치하지 않습니다.')
            reject = '지문 신원이 여권 생년월일과 일치하지 않습니다. 입국하실 수 없습니다.'
        else:  # fp_nat
            state = '불량(지문 국적 불일치)'
            fp_line = ctx.get('fp_line', '지문 조회 결과… 등록 국적이 여권과 일치하지 않습니다.')
            reject = '지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다.'
        rows.append(_hdr(day, slot, name, dtype, state, '인삿말', name, g))
        rows.append(_cont('지문 검사(얼굴 ↔ 여권 사진)', '심사관', '여권 사진과 외모가 다릅니다. 지문 인식을 진행하겠습니다. 손을 올려주세요.'))
        rows.append(_cont('', name, '(잠시 망설인다) …네.'))
        rows.append(_cont('지문 대조(지문 신원 ↔ 여권)', '심사관', fp_line))
        rows.append(_cont('', name, '어… 그건 시스템 오류 아닐까요?'))
        rows.append(_cont('', '심사관', '지문은 성형으로도 바뀌지 않습니다. 본인 여권이 맞습니까?'))
        rows.append(_cont('', name, '…(말을 잇지 못한다)'))
        rows.append(_cont('입국 허가 (오판)', '심사관', '즐거운 여행 되십시오.'))
        rows.append(_cont('', name, '가, 감사합니다. (서둘러 지나간다)'))
        rows.append(_cont('입국 거부 (정답)', '심사관', reject))
        rows.append(_cont('', name, '…죄송합니다.'))

    else:
        raise ValueError(f'unknown defect {defect}')

    return rows


def build_normal_block(day, slot, name, dtype, ctx):
    """단순 정상 블록(인삿말→허가 정답→거부 오판)."""
    g = ctx['greeting']
    appV = ctx['approve_visitor']
    rejV = ctx['reject_visitor']
    rows = [
        _hdr(day, slot, name, dtype, '정상', '인삿말', name, g),
        _cont('입국 허가 (정답)', '심사관', '즐거운 여행 되십시오.'),
        _cont('', name, appV),
        _cont('입국 거부 (오판)', '심사관', REJECT_MISJUDGE),
        _cont('', name, rejV),
    ]
    return rows


def build_pass_with_normalonly(day, slot, name, dtype, ctx):
    return build_normal_block(day, slot, name, dtype, ctx)
