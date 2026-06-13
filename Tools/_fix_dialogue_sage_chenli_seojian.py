# -*- coding: utf-8 -*-
"""
대사_스크립트.xlsx 만 수정 (게임 JSON 절대 안 건드림 — 사용자 지시).
- Day8 오현석(현자): 현자 톤 + 마법 아이템 이벤트 대사
- Day8 첸 리: 통과 멘트를 취업 맥락으로
- Day6 서지안: 방역 정정 줄(심사관 PCR 안내 + 본인 응답) 추가
멱등(이미 적용돼 있으면 skip). 백업은 호출 측에서.
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import xlwings as xw

XLSX = r"C:\Users\chris\Documents\produc_build_reecture\data\대사_스크립트.xlsx"

def cv(rng):
    v = rng.value
    return "" if v is None else str(v).strip()

def find_book(path):
    base = os.path.basename(path).lower()
    for app in xw.apps:
        for b in app.books:
            try:
                if os.path.basename(b.fullname).lower() == base:
                    return b, None
            except Exception:
                pass
    app = xw.App(visible=False); app.display_alerts = False
    return app.books.open(path), app

def block_rows(ws, name, ncols=8):
    """name 손님 블록의 (행번호, [A..H값]) 리스트."""
    used = ws.used_range
    n = used.last_cell.row
    rows = []; on = False
    for r in range(1, n + 1):
        vals = [cv(ws.range((r, c))) for c in range(1, ncols + 1)]
        c2 = vals[2]
        if c2 == name:
            on = True
        elif c2 and c2 != name and c2 != "방문객":
            on = False
        if on:
            rows.append((r, vals))
    return rows

def main():
    bk, owned = find_book(XLSX)
    log = []

    # ── Day8 오현석(현자) ─────────────────────────────
    ws = bk.sheets["Day8"]
    rows = block_rows(ws, "오현석")
    ohyun = [(r, v) for (r, v) in rows if v[6] == "오현석"]  # G==오현석
    SAGE = [
        "안녕하신가. 천천히 보게나, 급할 것 없네.",
        "고맙네. 수고하는 그대에게 작은 정표를 두고 가지… 부디 가는 길이 환하기를.",
        "허허, 괜찮네. 인연이 닿으면 또 보겠네.",
    ]
    if len(ohyun) >= 3:
        cur = [cv(ws.range((ohyun[i][0], 8))) for i in range(3)]
        if cur != SAGE:
            for i in range(3):
                ws.range((ohyun[i][0], 8)).value = SAGE[i]
            log.append("Day8 오현석(현자) 대사 3줄 갱신")
        else:
            log.append("Day8 오현석 (이미 적용됨)")

    # ── Day8 첸 리: 입국 허가(정답) 심사관 멘트 ─────────
    rows = block_rows(ws, "첸 리")
    NEW_CHEN = "재직 정보 확인되었습니다. 좋은 직장 생활 되십시오."
    done = False
    for (r, v) in rows:
        if v[5].startswith("입국 허가") and v[6] == "심사관":
            if cv(ws.range((r, 8))) != NEW_CHEN:
                ws.range((r, 8)).value = NEW_CHEN
                log.append("Day8 첸 리 통과 멘트 취업 맥락으로")
            else:
                log.append("Day8 첸 리 (이미 적용됨)")
            done = True
            break
    if not done:
        log.append("Day8 첸 리 입국허가 행 못 찾음(확인 필요)")

    # ── Day6 서지안: 인삿말 뒤에 방역 정정 2줄 삽입 ─────
    ws6 = bk.sheets["Day6"]
    rows = block_rows(ws6, "서지안")
    greet = next((r for (r, v) in rows if v[5] == "인삿말" and v[6] == "서지안"), None)
    already = any(v[6] == "심사관" and "PCR" in v[7] for (r, v) in rows)
    if greet and not already:
        ins = greet + 1
        ws6.api.Rows("{}:{}".format(ins, ins + 1)).Insert()
        ws6.range((ins, 7)).value = "심사관"
        ws6.range((ins, 8)).value = "방역 기간에는 PCR 검사서도 함께 확인합니다. 제출해 주세요."
        ws6.range((ins + 1, 7)).value = "서지안"
        ws6.range((ins + 1, 8)).value = "아, 그렇군요. 여기 있습니다. (PCR 검사서를 꺼낸다)"
        log.append("Day6 서지안 방역 정정 2줄 삽입")
    elif already:
        log.append("Day6 서지안 (이미 적용됨)")
    else:
        log.append("Day6 서지안 인삿말 행 못 찾음(확인 필요)")

    bk.save()
    print("대사_스크립트.xlsx 수정 완료:")
    for l in log:
        print("  -", l)
    if owned is not None:
        owned.quit()

if __name__ == "__main__":
    main()
