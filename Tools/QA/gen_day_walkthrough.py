# -*- coding: utf-8 -*-
"""
「여권 주세요」 1~14일차 진행 검수표 생성기 (QA 산출물).
런타임 진실원본 = Assets/Resources/GameData/dayN.json + 보조 Assets/GameData/_source/GameData.source.json.
게임 변경 없음. xlsx 데이터만 생성.
"""
import json, os
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GD = os.path.join(ROOT, "Assets", "Resources", "GameData")
SRC = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
OUT = os.path.join(ROOT, "Tools", "QA", "day_walkthrough.xlsx")

# ── 소스 로드 ───────────────────────────────────────────
def load_days():
    days = {}
    for n in range(1, 15):
        with open(os.path.join(GD, f"day{n}.json"), encoding="utf-8") as f:
            days[n] = json.load(f)
    return days

def load_src():
    with open(SRC, encoding="utf-8") as f:
        return json.load(f)["sheets"]

def rows_of(sheets, name):
    return sheets[name]["rows"]

# ── 점수/금액 룩업 ───────────────────────────────────────
# 정답(올바른 플레이) 분기를 대표값으로 선택.
# correctResult 가 "정상 승인"이면 승인계열 정답, "정상 거절"이면 거절/적발계열 정답.
CORRECT_BRANCHES = {
    "approve_correct", "reject_correct", "approve_with_step", "approve_no_step",
    "approve_sage_item", "approve_immediate", "proxy_self_request",
    "detect_montage_reject", "detect_montage_xray_reject", "reject_lucky",
    "terror_persuade_confess", "terror_persuade_report_ok",
    "cult_best_combo", "cult_yyy", "cult_yyy_brainwash", "cult_nyy_follower",
    "mask_request_turn_1",
}
WRONG_HINT = ("wrong", "corrupt", "bomb", "fail", "accrue", "ignore_then_bomb")

def build_score_index(sheets):
    idx = {}
    for r in rows_of(sheets, "character_score"):
        key = (r["character_type"], r["doc_state"], r["defect_variant"])
        idx.setdefault(key, []).append(r)
    return idx

def build_payout_index(sheets):
    idx = {}
    for r in rows_of(sheets, "character_payout"):
        key = (r["character_type"], r["doc_state"], r["defect_variant"])
        idx.setdefault(key, []).append(r)
    return idx

def _to_int(v):
    try:
        return int(v)
    except (TypeError, ValueError):
        return None

def pick_correct_row(rows):
    """올바른 플레이 분기 1개를 고른다(정답 점수/금액 대표값)."""
    if not rows:
        return None, "행없음"
    # 1) CORRECT_BRANCHES 우선, 그중 점수 최대
    cands = [r for r in rows if r.get("branch_key") in CORRECT_BRANCHES]
    if cands:
        cands.sort(key=lambda r: (_to_int(r.get("score")) if _to_int(r.get("score")) is not None else _to_int(r.get("payout")) or -999), reverse=True)
        return cands[0], "정답분기"
    # 2) 오답힌트 제외하고 점수 양수 최대
    cands = [r for r in rows if not any(h in (r.get("branch_key") or "") for h in WRONG_HINT)]
    if cands:
        cands.sort(key=lambda r: (_to_int(r.get("score")) if _to_int(r.get("score")) is not None else -999), reverse=True)
        return cands[0], "추정(분기다수)"
    return rows[0], "추정"

def score_lookup(score_idx, payout_idx, ctype, doc_state, variant):
    """returns (score_text, payout_text, branch, note)"""
    issues = []
    # 키 매칭: variant 정확 매칭 → variant None 폴백
    def find(idx, v):
        if (ctype, doc_state, v) in idx:
            return idx[(ctype, doc_state, v)]
        if (ctype, doc_state, None) in idx:
            return idx[(ctype, doc_state, None)]
        # variant 무시 매칭(키 다양성 흡수)
        merged = []
        for (ct, ds, vv), rr in idx.items():
            if ct == ctype and ds == doc_state:
                merged += rr
        return merged
    srows = find(score_idx, variant)
    prows = find(payout_idx, variant)
    srow, sflag = pick_correct_row(srows)
    prow, pflag = pick_correct_row(prows)
    if not srows:
        issues.append("점수표 매칭 실패")
    if not prows:
        issues.append("금액표 매칭 실패")
    if srow is None:
        score_txt = "?"
        branch = "-"
    else:
        sc = srow.get("score")
        rng = srow.get("score_range")
        score_txt = (str(sc) if sc not in (None, "") else (rng or "(이벤트)"))
        branch = srow.get("branch_key") or "-"
        if sflag != "정답분기":
            issues.append(f"점수분기 {sflag}")
    if prow is None:
        pay_txt = "?"
    else:
        pay = prow.get("payout")
        pay_txt = (str(pay) if pay not in (None, "") else "?")
    return score_txt, pay_txt, branch, srow, prow, "; ".join(issues)

# ── 서류 결함 분석 ───────────────────────────────────────
NORMAL_VARIANTS = {"정상", "", None}

def analyze_documents(cust):
    """결함 서류 목록과 결함 항목, 결함 개수."""
    defect_docs = []
    for doc in cust.get("documents", []):
        var = doc.get("variant")
        vio = doc.get("violationField")
        is_defect = (var not in NORMAL_VARIANTS) or (vio not in ("없음", "정상", "", None))
        if is_defect:
            defect_docs.append((doc.get("documentType"), var, vio))
    return defect_docs

def entry_line(cust):
    for dc in cust.get("dialogueCases", []):
        if dc.get("caseType") == "입장":
            lines = dc.get("lines", [])
            if lines:
                return lines[0].get("text", "")
    # fallback: 첫 손님대사
    for dc in cust.get("dialogueCases", []):
        for ln in dc.get("lines", []):
            return ln.get("text", "")
    return ""

def all_dialogue(cust):
    """모든 대화 케이스의 전체 대사를 케이스별로 나열한다."""
    parts = []
    for dc in cust.get("dialogueCases", []):
        ct = dc.get("caseType", "")
        parts.append(f"〔{ct}〕")
        for ln in dc.get("lines", []):
            sp = ln.get("speaker", "")
            tx = ln.get("text", "")
            parts.append(f"  {sp}: {tx}")
    return "\n".join(parts)

# 요구 서류 (document_requirement → 일자별)
def required_docs(sheets, day, ctype, nationality):
    """해당 손님에게 그날 실제로 요구되는 서류 (국적/유형 면제 규칙 반영)."""
    out = []
    nat_kor = nationality is not None and "KOR" in str(nationality)
    for r in rows_of(sheets, "document_requirement"):
        df = _to_int(r.get("day_from")); dt = _to_int(r.get("day_to"))
        if df is None or dt is None:
            continue
        if not (df <= day <= dt):
            continue
        dtype = r.get("document_type")
        # applies_to 면제 규칙
        if dtype == "비자" and nat_kor:                       # 한국인 비자 면제
            continue
        if dtype == "취업증빙" and ctype not in ("취업체류자", "장기체류자"):  # 취업/장기만
            continue
        out.append(dtype)
    return out

# 특수 기믹 판정
SCAN_TRIGGERS = {  # day -> customerId -> scan
    11: {10: "지문"}, 12: {8: "지문"}, 13: {11: "X-ray"},
    14: {9: "X-ray+지문", 34: "X-ray"},
}
ADV_TYPES = {"테러범", "사이비 신도", "특수(현자)★", "특수(연예인)★", "특수(정치인)★",
             "범죄자(성형수술)", "범죄자(외국 도피자)", "범죄자(국내 유입자)", "성형 의심 고객"}

def gimmicks(day, cust, early):
    g = []
    cid = cust.get("customerId")
    if day in SCAN_TRIGGERS and cid in SCAN_TRIGGERS[day]:
        g.append(f"스캔잠금({SCAN_TRIGGERS[day][cid]})")
    if cust.get("characterType") in ADV_TYPES:
        g.append("고급분기")
    if early:
        g.append(f"조기엔딩{early}")
    return " ".join(g)

# ── 스타일 ──────────────────────────────────────────────
HDR_FILL = PatternFill("solid", fgColor="305496")
HDR_FONT = Font(bold=True, color="FFFFFF", size=10)
APPROVE_FILL = PatternFill("solid", fgColor="C6EFCE")
REJECT_FILL = PatternFill("solid", fgColor="FFC7CE")
ISSUE_FILL = PatternFill("solid", fgColor="FFEB9C")
THIN = Side(style="thin", color="BFBFBF")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
WRAP = Alignment(wrap_text=True, vertical="top")
CENTER = Alignment(horizontal="center", vertical="center")

def style_header(ws, ncol, rowi=1):
    for c in range(1, ncol + 1):
        cell = ws.cell(row=rowi, column=c)
        cell.fill = HDR_FILL; cell.font = HDR_FONT
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = BORDER

def set_widths(ws, widths):
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(i)].width = w

# ── 메인 ────────────────────────────────────────────────
def main():
    days = load_days()
    sheets = load_src()
    score_idx = build_score_index(sheets)
    payout_idx = build_payout_index(sheets)

    wb = Workbook()
    issues = []  # (day, slot, name, problem, cause)

    # 마스터 시트(전체 손님)
    master = wb.active
    master.title = "전체진행"
    headers = ["일자", "슬롯", "이름", "캐릭터유형", "서류상태", "잡아야할 결함",
               "정답판정", "필요서류", "기대점수", "기대금액", "전체 대사", "특수기믹/비고"]
    master.append(headers)
    style_header(master, len(headers))
    master.freeze_panes = "A2"

    day_counts = {}
    char_dist = {}

    for day in range(1, 15):
        custs = sorted(days[day]["customers"], key=lambda c: c.get("slot", 0))
        day_counts[day] = {"total": len(custs), "approve": 0, "reject": 0}
        for cust in custs:
            ctype = cust.get("characterType", "")
            char_dist[ctype] = char_dist.get(ctype, 0) + 1
            correct = cust.get("correctResult", "")
            is_reject = (correct == "정상 거절")
            if is_reject:
                day_counts[day]["reject"] += 1
            else:
                day_counts[day]["approve"] += 1
            variant = cust.get("defectVariant") or None
            defect_docs = analyze_documents(cust)
            doc_state = "defect" if (is_reject or defect_docs or ctype.startswith("범죄자") or ctype == "테러범") else "normal"
            # 서류상태 텍스트
            if defect_docs:
                state_txt = "불량: " + ", ".join(f"{d}({v or '-'})" for d, v, vio in defect_docs)
                defect_txt = ", ".join(f"{d}:{vio}" for d, v, vio in defect_docs)
            else:
                state_txt = "정상"
                defect_txt = "-" if not is_reject else "(거절인데 서류결함 표기없음)"
            score_txt, pay_txt, branch, srow, prow, lk_issue = score_lookup(
                score_idx, payout_idx, ctype, doc_state, variant)
            early = (prow or {}).get("early_ending") if prow else None
            req = required_docs(sheets, day, ctype, cust.get("nationality"))
            gim = gimmicks(day, cust, early)
            entry = all_dialogue(cust)

            row = [day, cust.get("slot"), cust.get("nameKr"), ctype, state_txt, defect_txt,
                   correct, ", ".join(req), score_txt, pay_txt, entry, gim]
            master.append(row)
            ri = master.max_row
            for c in range(1, len(headers) + 1):
                master.cell(row=ri, column=c).border = BORDER
                master.cell(row=ri, column=c).alignment = WRAP
            # 정답판정 색
            vcell = master.cell(row=ri, column=7)
            vcell.fill = APPROVE_FILL if not is_reject else REJECT_FILL
            vcell.alignment = CENTER

            # ── 이슈 수집 ──
            n_defect = len(defect_docs)
            if is_reject and n_defect == 0:
                issues.append((day, cust.get("slot"), cust.get("nameKr"),
                               "정답=거절인데 결함 서류 0개", "변조 미주입 또는 데이터 누락"))
            if (not is_reject) and n_defect >= 1:
                issues.append((day, cust.get("slot"), cust.get("nameKr"),
                               f"정답=승인인데 결함 서류 {n_defect}개", "결함/정답 불일치"))
            if n_defect >= 2:
                issues.append((day, cust.get("slot"), cust.get("nameKr"),
                               f"결함 서류 {n_defect}개(규칙: 1개)", "다중 결함 — 규칙4장 위반 소지"))
            # 요구서류 밖 서류를 결함으로 표기?
            for d, v, vio in defect_docs:
                if d not in req:
                    issues.append((day, cust.get("slot"), cust.get("nameKr"),
                                   f"결함 서류 '{d}'가 그날 요구서류 아님(요구:{req})", "판정 비반영 서류에 결함"))
                # 위반항목이 다른 서류를 가리킴 (예: 여권인데 비자 위반)
                if vio not in ("없음", "정상", "", None) and vio not in d and vio in ("비자", "PCR검사서", "취업증명서"):
                    issues.append((day, cust.get("slot"), cust.get("nameKr"),
                                   f"{d} 서류의 위반항목이 '{vio}'(타 서류명)", "violationField/문서 불일치 소지"))
            if lk_issue and ("매칭 실패" in lk_issue):
                issues.append((day, cust.get("slot"), cust.get("nameKr"),
                               f"점수/금액 조회: {lk_issue}", f"{ctype}/{doc_state}/{variant} 분기 미존재"))

    set_widths(master, [5, 5, 12, 18, 34, 26, 9, 22, 10, 10, 70, 24])

    # ── 일자별 시트 ──
    for day in range(1, 15):
        ws = wb.create_sheet(f"Day{day}")
        ws.append(headers)
        style_header(ws, len(headers))
        ws.freeze_panes = "A2"
        # 마스터에서 해당 일자 행 복사 대신 재생성(간단)
        for mr in range(2, master.max_row + 1):
            if master.cell(row=mr, column=1).value == day:
                vals = [master.cell(row=mr, column=c).value for c in range(1, len(headers) + 1)]
                ws.append(vals)
                ri = ws.max_row
                for c in range(1, len(headers) + 1):
                    ws.cell(row=ri, column=c).border = BORDER
                    ws.cell(row=ri, column=c).alignment = WRAP
                vcell = ws.cell(row=ri, column=7)
                vcell.fill = APPROVE_FILL if vals[6] != "정상 거절" else REJECT_FILL
                vcell.alignment = CENTER
        set_widths(ws, [5, 5, 12, 18, 34, 26, 9, 22, 10, 10, 70, 24])

    # ── 요약 시트(맨 앞으로) ──
    summ = wb.create_sheet("요약", 0)
    summ.append(["「여권 주세요」 1~14일차 진행 검수표"])
    summ.cell(row=1, column=1).font = Font(bold=True, size=14)
    summ.append([])
    summ.append(["일자별 손님 수 / 정상·거절 분포"])
    summ.cell(row=3, column=1).font = Font(bold=True)
    sh = ["일자", "손님 수", "정상(승인)", "비정상(거절)", "거절 비율", "특수기믹 등장"]
    summ.append(sh)
    style_header(summ, len(sh), summ.max_row)
    hr = summ.max_row
    summ.freeze_panes = f"A{hr+1}"
    for day in range(1, 15):
        dc = day_counts[day]
        ratio = f"{dc['reject']/dc['total']*100:.0f}%" if dc['total'] else "-"
        gtags = []
        if day in SCAN_TRIGGERS:
            gtags.append("스캔잠금")
        # 어떤 캐릭터 고급분기 있는지
        adv = sorted({c["characterType"] for c in days[day]["customers"] if c.get("characterType") in ADV_TYPES})
        if adv:
            gtags.append("고급분기:" + ",".join(adv))
        summ.append([day, dc["total"], dc["approve"], dc["reject"], ratio, "; ".join(gtags)])
        ri = summ.max_row
        for c in range(1, len(sh) + 1):
            summ.cell(row=ri, column=c).border = BORDER
    total_all = sum(d["total"] for d in day_counts.values())
    summ.append(["합계", total_all, sum(d["approve"] for d in day_counts.values()),
                 sum(d["reject"] for d in day_counts.values()), "", ""])
    summ.cell(row=summ.max_row, column=1).font = Font(bold=True)

    summ.append([])
    summ.append(["캐릭터 유형 분포(전체)"])
    summ.cell(row=summ.max_row, column=1).font = Font(bold=True)
    summ.append(["캐릭터유형", "등장 수"])
    style_header(summ, 2, summ.max_row)
    for ct, n in sorted(char_dist.items(), key=lambda x: -x[1]):
        summ.append([ct, n])
        for c in range(1, 3):
            summ.cell(row=summ.max_row, column=c).border = BORDER

    summ.append([])
    summ.append(["특수 기믹 등장 일자"])
    summ.cell(row=summ.max_row, column=1).font = Font(bold=True)
    summ.append(["기믹", "등장 일자(손님)"])
    style_header(summ, 2, summ.max_row)
    scan_txt = "; ".join(f"D{d}: " + ",".join(f"손님{cid}({s})" for cid, s in m.items())
                         for d, m in SCAN_TRIGGERS.items())
    summ.append(["보조검사 스캔 잠금해제", scan_txt])
    # 조기엔딩 일자 (payout early_ending 보유 캐릭터 첫 등장)
    early_days = {}
    for day in range(1, 15):
        for c in days[day]["customers"]:
            ct = c["characterType"]
            v = c.get("defectVariant") or None
            ds = "defect" if c.get("correctResult") == "정상 거절" or analyze_documents(c) or ct.startswith("범죄자") or ct == "테러범" else "normal"
            _, _, _, _, prow, _ = score_lookup(score_idx, payout_idx, ct, ds, v)
            ee = (prow or {}).get("early_ending") if prow else None
            if ee:
                early_days.setdefault(ee, []).append(f"D{day}/{ct}")
    summ.append(["조기엔딩 트리거(분기)", "; ".join(f"{k}:{','.join(set(v))}" for k, v in sorted(early_days.items()))])
    for r in range(summ.max_row - 1, summ.max_row + 1):
        for c in range(1, 3):
            summ.cell(row=r, column=c).border = BORDER
    set_widths(summ, [22, 18, 14, 14, 12, 60])

    # ── 이슈 시트 ──
    isheet = wb.create_sheet("이슈")
    ih = ["일자", "슬롯", "손님", "문제", "추정 원인"]
    isheet.append(ih)
    style_header(isheet, len(ih))
    isheet.freeze_panes = "A2"
    for it in issues:
        isheet.append(list(it))
        ri = isheet.max_row
        for c in range(1, len(ih) + 1):
            isheet.cell(row=ri, column=c).border = BORDER
            isheet.cell(row=ri, column=c).alignment = WRAP
            isheet.cell(row=ri, column=c).fill = ISSUE_FILL
    if not issues:
        isheet.append(["-", "-", "-", "정합 문제 미발견", "-"])
    set_widths(isheet, [5, 5, 12, 50, 40])

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    wb.save(OUT)
    print("SAVED:", OUT)
    print("total customers:", total_all)
    print("issues:", len(issues))
    for it in issues[:20]:
        print("  ", it)
    print("day_counts:")
    for d in range(1, 15):
        print("  D%d total=%d approve=%d reject=%d" % (d, day_counts[d]["total"], day_counts[d]["approve"], day_counts[d]["reject"]))

if __name__ == "__main__":
    main()
