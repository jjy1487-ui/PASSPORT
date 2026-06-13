# -*- coding: utf-8 -*-
"""열려 있는 data/시나리오_스크립트.xlsx 의 '대사_스크립트' 시트를 손글 형식으로 재작성.
(파일이 Excel에 열려 있어도 xlwings/COM로 그 인스턴스에 붙어 갱신 — 닫을 필요 없음)
방문객별: 인삿말 + 입국 허가 + 입국 거부(의미 있는 2분기), 대사만, 오거부횟수 없음.
"""
import os, glob, re, json, io
import xlwings as xw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")
OUTNAME = "시나리오_스크립트.xlsx"
OUTPATH = os.path.join(ROOT, "data", OUTNAME)

def case_by_result(c, gr):
    for dc in c.get("dialogueCases", []):
        if dc.get("gameResult") == gr: return dc
    return None
def case_by_type(c, ct):
    if not ct: return None
    for dc in c.get("dialogueCases", []):
        if dc.get("caseType") == ct: return dc
    return None
def lines_sorted(case):
    return sorted(case.get("lines", []), key=lambda l: l.get("order", 0)) if case else []
def defect_label(c):
    parts = []
    for doc in c.get("documents", []) or []:
        vf = doc.get("violationField", "")
        if (doc.get("variant") == "비정상") or (vf and vf != "없음"):
            parts.append(f"{doc.get('documentType','')} {vf}".strip())
    if c.get("xray") and c["xray"].get("detail"): parts.append(f"X-ray:{c['xray']['detail']}")
    fp = c.get("fingerprint")
    if fp and fp.get("record", {}).get("mode") == "수배자": parts.append("지문:수배자")
    return ", ".join(parts) if parts else "결함"

# build rows
days = []
for p in sorted(glob.glob(os.path.join(GAMEDATA, "day*.json")),
                key=lambda p: int(re.search(r"day(\d+)", p).group(1))):
    dn = int(re.search(r"day(\d+)", p).group(1))
    days.append((dn, json.load(io.open(p, encoding="utf-8"))))

header = ["일차","순서","방문객","유형","서류상태","분기","화자","대사"]
rows = [header]
name_row_idx = []   # 1-based excel rows to bold (visitor name)
for dn, d in days:
    for c in d.get("customers", []):
        name = c.get("nameKr"); slot = c.get("slot"); ctype = c.get("characterType")
        should = (c.get("correctResult") == "정상 승인")
        docstate = "정상" if should else f"불량 ({defect_label(c)})"
        blocks = [("인삿말", lines_sorted(case_by_type(c, "입장")))]
        if should:
            blocks.append(("입국 허가 (정답)", lines_sorted(case_by_result(c, "정상 승인"))))
            blocks.append(("입국 거부 (오판)", lines_sorted(case_by_result(c, "잘못 거절"))))
        else:
            blocks.append(("입국 허가 (오판)", lines_sorted(case_by_result(c, "잘못 허가"))))
            guided = case_by_type(c, c.get("rejectGuidedCaseType"))
            if guided: blocks.append(("입국 거부 (정답·적발)", lines_sorted(guided)))
            else: blocks.append(("입국 거부 (정답)", lines_sorted(case_by_result(c, "정상 거절"))))
        first = True
        for label, lines in blocks:
            for li, ln in enumerate(lines):
                head = (li == 0 and label == "인삿말")
                rows.append([dn if head else "", slot if head else "", name if head else "",
                             ctype if head else "", docstate if head else "",
                             label if li == 0 else "", ln.get("speaker"), ln.get("text")])
                if first:
                    name_row_idx.append(len(rows)); first = False
        rows.append([""]*8)  # 방문객 구분 빈 줄

# locate open workbook (attach if open, else open invisibly)
bk = None; opened = False
base = OUTNAME.lower()
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower() == base:
                bk = b; break
        except Exception: pass
    if bk: break
if bk is None:
    app = xw.App(visible=False); app.display_alerts = False
    bk = app.books.open(OUTPATH); opened = True

ws = bk.sheets["대사_스크립트"]
ws.clear()
ws.range((1, 1)).value = rows  # bulk write (fast)
# header format
hdr = ws.range((1, 1), (1, 8))
hdr.color = (47, 85, 151)
hdr.api.Font.Bold = True
hdr.api.Font.Color = 0xFFFFFF
# column widths
for col, wdt in zip("ABCDEFGH", [6, 6, 14, 16, 28, 18, 10, 70]):
    ws.range(f"{col}1").column_width = wdt
# wrap 대사 column
ws.range((2, 8), (len(rows), 8)).api.WrapText = True
# bold visitor name cells
for r in name_row_idx:
    ws.range((r, 3)).api.Font.Bold = True
# freeze header
try:
    ws.range("A2").select(); bk.app.api.ActiveWindow.FreezePanes = True
except Exception: pass

bk.save()
if opened: bk.app.quit()
print(f"updated '대사_스크립트' rows={len(rows)} visitors={len(name_row_idx)} (saved {'invisible' if opened else 'attached'})")
