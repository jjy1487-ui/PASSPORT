# -*- coding: utf-8 -*-
"""
Day11 존 카터(특수: 첫 X-ray 밀수범+수배자) 대사 확장. 대사_스크립트.xlsx 만.
X-ray 도입 안내 + 단계별 긴장 고조 + 드라마틱 적발 + 스토리 떡밥. JSON 안 건드림.
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import xlwings as xw

XLSX = r"C:\Users\chris\Documents\produc_build_reecture\data\대사_스크립트.xlsx"
NAME = "존 카터"
STATE = "불량(여권번호 위조 + X-ray 밀수)"

# (branch, speaker, text)
NEW = [
 ("인삿말", "존 카터", "Morning. Just here on business — nothing to declare. (안녕하세요. 사업차 왔어요. 신고할 것도 없고요.)"),
 ("", "심사관", "요즘 보안이 강화돼서, 오늘부터 수하물도 X-ray로 함께 봅니다. 가방 좀 올려주시겠어요?"),
 ("", "존 카터", "Oh. Sure, of course. (아… 네, 그럼요.)"),
 ("서류 대조(여권번호 ↔ 발급국)", "심사관", "먼저 여권부터 볼게요… 어, 번호 앞자리가 발급국이랑 안 맞는데요?"),
 ("", "존 카터", "That must be a mistake. It's a valid passport. (착오겠죠. 멀쩡한 여권인데.)"),
 ("X-ray 검사(수하물 ↔ 금지물품 규정)", "심사관", "이제 가방 한번 볼게요… (화면을 보다 멈칫) …이거, 안에 숨겨둔 물건이 잡히는데요?"),
 ("", "존 카터", "(표정이 굳으며) That's… not mine. I don't know how that got there. (그건… 제 거 아니에요. 어떻게 들어갔는지 몰라요.)"),
 ("경보 대조(경보 명단 ↔ 신원)", "심사관", "잠시만요, 신원 조회 좀 하겠습니다… (경보음) …경보 명단과 일치하네요. 수배 중인 분이군요."),
 ("", "존 카터", "(낮은 목소리로) Look… we can sort this out quietly, can't we? (이봐요… 조용히 해결하면 되잖아요?)"),
 ("입국 거부 (정답)", "심사관", "안 됩니다. 보안팀, 이쪽으로 와주세요."),
 ("", "존 카터", "(끌려가며 돌아본다) …This isn't over. (…이걸로 끝이 아니야.)"),
 ("입국 허가 (오판)", "심사관", "확인됐습니다. 들어가셔도 좋습니다."),
 ("", "존 카터", "(안도하며 빠르게 지나간다) Thank you. Have a good one. (감사합니다. 수고하세요.)"),
]

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

def main():
    bk, owned = find_book(XLSX)
    ws = bk.sheets["Day11"]
    n = ws.used_range.last_cell.row
    # 블록 시작/끝
    start = None
    for r in range(1, n + 1):
        if str(ws.range((r, 3)).value or "").strip() == NAME:
            start = r; break
    if start is None:
        print("존 카터 블록 못 찾음");
        if owned: owned.quit()
        return
    end = start
    r = start + 1
    while r <= n:
        c = str(ws.range((r, 3)).value or "").strip()
        if c and c != NAME and c != "방문객":
            break
        if c == "" and all(str(ws.range((r, k)).value or "").strip() == "" for k in range(6, 9)):
            # 완전 빈 행이면 블록 끝으로 안 봄 — 일단 포함하지 않고 멈춤
            break
        end = r; r += 1
    # 헤더(A,B,D) 보존
    day = ws.range((start, 1)).value
    order = ws.range((start, 2)).value
    typ = ws.range((start, 4)).value
    # 기존 블록 삭제
    ws.api.Rows("{}:{}".format(start, end)).Delete()
    # 새 블록 삽입
    m = len(NEW)
    ws.api.Rows("{}:{}".format(start, start + m - 1)).Insert()
    for i, (br, spk, txt) in enumerate(NEW):
        rr = start + i
        if i == 0:
            ws.range((rr, 1)).value = day
            ws.range((rr, 2)).value = order
            ws.range((rr, 3)).value = NAME
            ws.range((rr, 4)).value = typ
            ws.range((rr, 5)).value = STATE
        ws.range((rr, 6)).value = br
        ws.range((rr, 7)).value = spk
        ws.range((rr, 8)).value = txt
    bk.save()
    print(f"존 카터 블록 확장: {end-start+1}행 → {m}행 (행 {start})")
    if owned is not None:
        owned.quit()

if __name__ == "__main__":
    main()
