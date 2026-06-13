# -*- coding: utf-8 -*-
"""
비자종류 거짓 손님 5명(리강 제외)의 거짓말을 다양화. 대사_스크립트.xlsx 만 수정.
- 입장 인삿말: 손님이 (거짓) 방문목적을 스스로 말하게
- 음성 대조: 심사관이 "방금 X라고 하셨잖아요. 비자엔 Y네요?" (X=진술, Y=비자종류)
게임 비자종류(visa_type)는 게임 적용 단계에서 Y로 맞춤. JSON 안 건드림.
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import xlwings as xw

XLSX = r"C:\Users\chris\Documents\produc_build_reecture\data\대사_스크립트.xlsx"

# sheet: (name, 새 인삿말, 새 음성대조)
DATA = {
 "Day7": ("왕 팡",
   "你好。我是来出差谈业务的。(안녕하세요. 출장 왔어요, 업무 때문에.)",
   "방금 업무차 출장 왔다고 하셨잖아요. 그런데 비자엔 방문 목적이 '관광'으로 돼 있는데요?"),
 "Day9": ("류 옌",
   "您好。我是来参加朋友婚礼的。(안녕하세요. 친구 결혼식 보러 왔어요.)",
   "방금 결혼식 보러 왔다고 하셨잖아요. 그런데 비자엔 방문 목적이 '취업'으로 돼 있는데요?"),
 "Day10": ("첸 웨이",
   "你好！我是来留学的，第一次来韩国。(안녕하세요! 유학 왔어요, 한국은 처음이에요.)",
   "방금 공부하러 유학 왔다고 하셨잖아요. 그런데 비자엔 방문 목적이 '관광'으로 돼 있는데요?"),
 "Day12": ("황 레이",
   "你好！第一次来韩国，就是来旅游的。(안녕하세요! 한국 처음이에요, 그냥 관광 왔어요.)",
   "방금 관광 왔다고 하셨잖아요. 그런데 비자엔 방문 목적이 '취업'으로 돼 있는데요?"),
 "Day13": ("쉬 펑",
   "你好～我是来短期旅游的。(안녕하세요~ 잠깐 여행 왔어요.)",
   "방금 잠깐 여행 왔다고 하셨잖아요. 그런데 비자엔 방문 목적이 '장기 체류'로 돼 있는데요?"),
}

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
    log = []
    for sheet, (name, greet, voice) in DATA.items():
        ws = bk.sheets[sheet]
        n = ws.used_range.last_cell.row
        in_block = False
        greet_done = voice_done = False
        for r in range(1, n + 1):
            c = str(ws.range((r, 3)).value or "").strip()
            if c == name:
                in_block = True
            elif c and c != name and c != "방문객":
                in_block = False
            if not in_block:
                continue
            br = str(ws.range((r, 6)).value or "").strip()
            spk = str(ws.range((r, 7)).value or "").strip()
            if br == "인삿말" and spk == name and not greet_done:
                ws.range((r, 8)).value = greet
                greet_done = True
            if br.startswith("음성 대조") and spk == "심사관" and not voice_done:
                ws.range((r, 8)).value = voice
                voice_done = True
        log.append(f"{sheet} {name}: 인삿말 {'OK' if greet_done else 'MISS'} / 음성대조 {'OK' if voice_done else 'MISS'}")
    bk.save()
    print("비자종류 거짓 다양화 완료:")
    for l in log:
        print("  -", l)
    if owned is not None:
        owned.quit()

if __name__ == "__main__":
    main()
