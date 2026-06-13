# -*- coding: utf-8 -*-
"""
4일차 전원 입장(인삿말) 대사에 방문목적/어디다녀오셨어요 질문을 엑셀 설계문서 2곳에 동기화.
대상:
  - data/시나리오_스크립트.xlsx  시트 '대사_스크립트'  (손님당 단일 블록 → main 인삿말)
  - data/대사_스크립트.xlsx       시트 'Day4'         (정상=main / 불량=alt 인삿말)
기존 승인/거절/오거부 등 다른 분기 행은 보존, '인삿말' 섹션만 교체. 멱등(심사관 줄 있으면 skip).
day4.json 과 동일 문구. xlwings COM(열려 있어도 편집). 백업: *.bak_visitpurpose.xlsx

실행: python Tools/_patch_day4_visit_purpose_xlsx.py
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import xlwings as xw

ROOT = r"C:\Users\chris\Documents\produc_build_reecture"
TARGETS = [
    (os.path.join(ROOT, "data", "시나리오_스크립트.xlsx"), "대사_스크립트", "single"),
    (os.path.join(ROOT, "data", "대사_스크립트.xlsx"), "Day4", "bystate"),
]
NAMES = {"박하준", "최예준", "토머스 무어", "노가은", "정시우", "강주원", "리 강"}

# (branch, speaker, text) — 첫 행만 branch='인삿말'
main_greet = {
    "박하준": [("인삿말", "박하준", "아니 왜 이렇게 오래 걸려요?"),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "박하준", "출장 다녀왔어요, 출장. 여권 냈으니까 빨리 처리해.")],
    "최예준": [("인삿말", "최예준", "안녕하세요. 요즘 여권 사진 때문에 문제됐다는 얘기 들었어요."),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "최예준", "친구 만나러 일본에 다녀왔어요. 여권 확인 부탁드립니다.")],
    "토머스 무어": [("인삿말", "토머스 무어", "Good morning! I'm so excited to be here. (안녕하세요! 오게 되어 정말 설레요.)"),
                ("", "심사관", "방문 목적이 어떻게 되십니까?"),
                ("", "토머스 무어", "I'm here for sightseeing. (관광하러 왔어요.)"),
                ("", "토머스 무어", "Here is my passport. (여기 여권 드릴게요.)")],
    "노가은": [("인삿말", "노가은", "사진 때문에 민원이 많다고 들었어요...잘 봐주세요. (여권 내밀며)"),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "노가은", "해외에서 잠깐 지내다 왔어요. 사진이랑 좀 달라 보여도 저 맞아요.")],
    "정시우": [("인삿말", "정시우", "신원 조사 때문에 이렇게 기다려야해? 빨리 처리해줘요."),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "정시우", "여행 좀 다녀왔어요. 여권 냈으니까 빨리 처리해.")],
    "강주원": [("인삿말", "강주원", "안녕하세요. 사진이랑 똑같이 생겼으니 걱정 없겠죠? (웃으며 여권 내밀며)"),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "강주원", "가족이랑 동남아 여행 다녀왔어요. 여권 확인 부탁드립니다.")],
    "리 강": [("인삿말", "리 강", "您好，请帮我看看。 (안녕하세요, 좀 봐주세요.)"),
            ("", "심사관", "방문 목적이 어떻게 되십니까?"),
            ("", "리 강", "我是来旅游观光的。 (관광하러 왔어요.)"),
            ("", "리 강", "这是我的护照和签证。 (여권이랑 비자 여기요.)")],
}
alt_greet = {
    "박하준": [("인삿말", "박하준", "빨리 좀 해줘요, 나 바쁜 사람이야."),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "박하준", "출장이요. 빨리 좀 합시다.")],
    "노가은": [("인삿말", "노가은", "(얼굴을 굳히며) 사진이랑 다르면 안 보내준다는 얘기 듣고 좀 긴장했어요."),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "노가은", "해외에서 잠깐 지내다 왔어요. 확인해 주세요.")],
    "정시우": [("인삿말", "정시우", "아 진짜, 빨리 좀 해줘요."),
             ("", "심사관", "어디 다녀오셨습니까?"),
             ("", "정시우", "여행이요, 여행. 빨리요.")],
}


def cv(row, c):
    v = row[c] if (row is not None and c < len(row)) else None
    return "" if v is None else str(v).strip()


def find_book(path):
    base = os.path.basename(path).lower()
    for app in xw.apps:
        for b in app.books:
            try:
                if os.path.basename(b.fullname).lower() == base:
                    return b, None  # attached, no app to quit
            except Exception:
                pass
    app = xw.App(visible=False)
    app.display_alerts = False
    b = app.books.open(path)
    return b, app  # opened, quit app after


def greeting_for(name, mode, state):
    if mode == "bystate" and "불량" in state and name in alt_greet:
        return alt_greet[name]
    return main_greet.get(name)


def process(path, sheet, mode):
    bk, owned_app = find_book(path)
    ws = bk.sheets[sheet]
    rng = ws.used_range
    base = rng.row
    vals = rng.value
    if vals and not isinstance(vals[0], (list, tuple)):
        vals = [vals]
    vals = [list(r) if isinstance(r, (list, tuple)) else [r] for r in vals]
    n = len(vals)

    edits = []
    i = 0
    while i < n:
        row = vals[i]
        name = cv(row, 2)
        day = cv(row, 0).split(".")[0]  # '4.0'(엑셀 float) -> '4'
        if name in NAMES and (mode != "single" or day == "4"):
            gstart = i
            gend = i
            j = i + 1
            while j < n:
                r2 = vals[j]
                if cv(r2, 2) != "":      # 다음 블록(이름)
                    break
                if cv(r2, 5) != "":      # 다음 분기(인삿말 끝)
                    break
                gend = j
                j += 1
            already = any(cv(vals[k], 6) == "심사관" for k in range(gstart, gend + 1))
            state = cv(row, 4)
            newg = greeting_for(name, mode, state)
            if newg and not already:
                edits.append({
                    "start": base + gstart, "count": gend - gstart + 1, "rows": newg,
                    "header": [cv(row, 0), cv(row, 1), cv(row, 2), cv(row, 3), cv(row, 4)],
                    "name": name, "state": state,
                })
            k = i + 1
            while k < n and cv(vals[k], 2) == "":
                k += 1
            i = k
        else:
            i += 1

    for e in sorted(edits, key=lambda x: x["start"], reverse=True):
        s, c, rows, hdr = e["start"], e["count"], e["rows"], e["header"]
        ws.api.Rows("{}:{}".format(s, s + c - 1)).Delete()
        m = len(rows)
        ws.api.Rows("{}:{}".format(s, s + m - 1)).Insert()
        for k, (br, spk, txt) in enumerate(rows):
            rr = s + k
            head = hdr + [br] if k == 0 else ["", "", "", "", "", ""]
            ws.range((rr, 1), (rr, 8)).value = head + [spk, txt]

    bk.save()
    print(f"[{os.path.basename(path)}::{sheet}] {len(edits)}개 블록 인삿말 갱신")
    for e in edits:
        print(f"   - {e['name']} ({e['state']}) @row{e['start']} ({e['count']}->{len(e['rows'])})")
    if owned_app is not None:
        owned_app.quit()


def main():
    for path, sheet, mode in TARGETS:
        process(path, sheet, mode)


if __name__ == "__main__":
    main()
