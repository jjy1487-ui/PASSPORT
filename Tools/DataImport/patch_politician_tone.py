# -*- coding: utf-8 -*-
"""
patch_politician_tone.py — '연예인·정치인 ★' 시트의 '정치인 ★' 구획 대사를
정치인 전용 톤(격식/권위, 보좌관 대리, 표·공약·의정활동 어휘, 스캔들 회피)으로 강화한다.

설계 원칙:
  - '연예인 ★' 구획은 절대 건드리지 않는다(기존 연예인 톤 유지).
  - 케이스 구조(행 수/순서/분기/화자)는 불변. 「...」 인용문 텍스트만 교체.
  - idempotent: (row, 셀 prefix) 단위로 목표 문자열을 '항상 동일하게' 덮어쓴다.
    재실행해도 결과가 같다(원본 비교 후 동일하면 skip).
  - 원본 손상 방지: 실행 전 타임스탬프 백업(.bak_YYYYmmdd_HHMMSS.xlsx).

정치인 ★ 구획 행 매핑(원본 260604 기준, '정치인 ★' 헤더 이후):
  즉시 입국:        r50(대사1), r54(대사2), r55(심사관)
  1회 거절 후:      r59(대사1), r62(4-1 재요청), r64(대사2), r65(심사관)
  2회 거절 후:      r69(대사1), r72(4-1), r74(4-3), r76(대사2), r77(심사관)
  3회 강제 입국:    r81(대사1), r84(4-1), r86(4-3), r88(4-5 극대노), r90(심사관 사과)

행 번호는 '정치인 ★' 헤더(c0='══ 정치인 ★ ══')를 런타임에 탐색해 상대 오프셋으로 적용 →
시트 편집으로 행이 밀려도 매핑이 깨지지 않는다.
"""
import os
import re
import shutil
import datetime
import openpyxl

XLSX = r"C:\Users\chris\Downloads\dayeon_data\방문객_스크립트_전체_260604.xlsx"
SHEET = "연예인·정치인 ★"

# '정치인 ★' 헤더 행 기준 상대 오프셋 -> (대사 prefix, 새 인용문).
# prefix 는 원본 셀의 '대사 N: ' / '대사: ' 같은 접두를 보존하기 위함(없으면 "").
# 인용문은 「...」 안의 텍스트만 교체한다.
POLITICIAN_LINES = {
    # ── 즉시 입국 (정답) ──────────────────────────────────────
    3:  "수고가 많으십니다. (정중하게 여권을 내밀며) 공무로 입국합니다. 잘 부탁드립니다.",   # r50 대사1
    7:  "수고하셨소. 나라를 위한 길에 늘 애써주시오.",                                        # r54 대사2
    8:  "성공적인 회담 되십시오, 의원님.",                                                    # r55 심사관

    # ── 1회 거절 후 입국 ──────────────────────────────────────
    12: "수고가 많으십니다. (정중하게 여권을 내밀며) 공무로 입국합니다. 잘 부탁드립니다.",   # r59 대사1
    15: "다시 확인해 보시오. 공무로 입국하는 사람을 이렇게 붙잡아서야 되겠소?",              # r62 4-1
    17: "그래, 확인됐으면 됐소. 의정활동에 차질 없도록 수고하시오.",                          # r64 대사2
    18: "성공적인 회담 되십시오, 의원님.",                                                    # r65 심사관

    # ── 2회 거절 후 입국 ──────────────────────────────────────
    22: "수고가 많으십니다. (정중하게 여권을 내밀며) 공무로 입국합니다. 잘 부탁드립니다.",   # r69 대사1
    25: "다시 확인해 보시오. 공무로 입국하는 사람을 이렇게 붙잡아서야 되겠소?",              # r72 4-1
    27: "...이거 보좌관, 책임자 좀 불러주게. (낮게) 내 표가 어디서 나오는지 모르나 보군.",   # r74 4-3
    29: "...됐소. (냉랭하게) 통과시키시오. 이 일은 기억해 두겠소.",                            # r76 대사2
    30: "성공적인 회담 되십시오, 의원님.",                                                    # r77 심사관

    # ── 3회 거절 후 강제 입국 (극대노) ────────────────────────
    34: "수고가 많으십니다. (정중하게 여권을 내밀며) 공무로 입국합니다. 잘 부탁드립니다.",   # r81 대사1
    37: "다시 확인해 보시오. 공무로 입국하는 사람을 이렇게 붙잡아서야 되겠소?",              # r84 4-1
    39: "...이거 보좌관, 책임자 좀 불러주게. (낮게) 내 표가 어디서 나오는지 모르나 보군.",   # r86 4-3
    41: "지금 자네가 누구 앞길을 막는 줄이나 아나?! 이 일이 기사로 나가면 자네가 책임질 텐가? "
        "나는 국민의 표로 이 자리에 선 사람이오!",                                            # r88 4-5 극대노
}

_QUOTE = re.compile(r"「(.+?)」", re.S)


def _replace_quote(cell_text, new_quote):
    """셀 텍스트의 첫 「...」 를 new_quote 로 교체. 인용문이 없으면 통째 교체."""
    if cell_text and _QUOTE.search(cell_text):
        return _QUOTE.sub("「" + new_quote + "」", cell_text, count=1)
    return "「" + new_quote + "」"


def find_politician_header_row(ws):
    for r in range(1, ws.max_row + 1):
        c0 = ws.cell(r, 1).value
        if c0 and str(c0).strip().startswith("══") and "정치인" in str(c0):
            return r
    return None


def main():
    if not os.path.exists(XLSX):
        raise SystemExit(f"xlsx 없음: {XLSX}")

    wb = openpyxl.load_workbook(XLSX)
    ws = wb[SHEET]
    base = find_politician_header_row(ws)
    if base is None:
        raise SystemExit("'정치인 ★' 구획 헤더를 찾지 못함")

    changed = 0
    for off, new_quote in POLITICIAN_LINES.items():
        row = base + off
        cell = ws.cell(row, 5)  # E열 = 대사/액션
        old = cell.value or ""
        new = _replace_quote(old, new_quote)
        if new != old:
            cell.value = new
            changed += 1

    if changed == 0:
        print("이미 정치인 톤 적용됨 — 변경 없음(idempotent).")
        return

    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = XLSX.replace(".xlsx", f".bak_{ts}.xlsx")
    shutil.copy2(XLSX, bak)
    wb.save(XLSX)
    print(f"정치인 톤 {changed}줄 적용. 백업: {bak}")


if __name__ == "__main__":
    main()
