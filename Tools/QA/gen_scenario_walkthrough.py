# -*- coding: utf-8 -*-
"""
scenario_walkthrough.md 생성기 (idempotent / 읽기 전용).

목적: 1~14일차 시나리오를 사람이 읽으며 검수할 수 있는 '서술형' 문서를 만든다.
표가 아니라 흐름(브리핑 -> 손님 등장 -> 대사 -> 판정 -> 특수흐름 -> 마무리)을 이야기처럼.

데이터 소스(읽기 전용):
  Assets/Resources/GameData/day1.json ~ day14.json  (런타임 진실원본)
  Assets/GameData/_source/GameData.source.json      (day_schedule / document_requirement / ending)

생성물:
  Tools/QA/scenario_walkthrough.md
"""
import json, os, io, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")
SOURCE = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
OUT = os.path.join(ROOT, "Tools", "QA", "scenario_walkthrough.md")


def load_json(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def load_source_sheet(src, name):
    """source.json의 sheet(rows) -> dict 리스트로 정규화."""
    s = src["sheets"][name]
    return s["rows"]


# ── 정상/비정상 판정 헬퍼 ─────────────────────────────────────────
def is_normal(cust):
    return cust.get("correctResult", "").strip() == "정상 승인"


def verdict_mark(cust):
    return "**✅ 승인**" if is_normal(cust) else "**⛔ 거부**"


def entrance_line(cust):
    """입장 대사 첫 1~2줄 인용."""
    for dc in cust.get("dialogueCases", []):
        if dc.get("caseType") == "입장" and dc.get("lines"):
            lines = dc["lines"][:2]
            return [(l.get("speaker", ""), l.get("text", "")) for l in lines]
    # 입장 케이스가 없으면 첫 케이스 첫 줄
    for dc in cust.get("dialogueCases", []):
        if dc.get("lines"):
            l = dc["lines"][0]
            return [(l.get("speaker", ""), l.get("text", ""))]
    return []


def correct_result_lines(cust):
    """정답 판정에 해당하는 결과 대사(정상 승인/정상 거절) 첫 1~2줄."""
    target = cust.get("correctResult", "").strip()
    for dc in cust.get("dialogueCases", []):
        if dc.get("gameResult") == target and dc.get("rejectCount", 0) == 0 and dc.get("lines"):
            lines = dc["lines"][:2]
            return [(l.get("speaker", ""), l.get("text", "")) for l in lines]
    return []


def defect_docs(cust):
    """결함(비정상) 서류 목록 [(documentType, violationField)]."""
    out = []
    for doc in cust.get("documents", []):
        if doc.get("variant") == "비정상":
            out.append((doc.get("documentType", ""), doc.get("violationField", "")))
    return out


def has_reject_loop(cust):
    """잘못 거절 재심사 루프(rejectCount 1~3)가 정의돼 있는지."""
    rcs = set()
    for dc in cust.get("dialogueCases", []):
        if dc.get("gameResult") == "잘못 거절":
            rcs.add(dc.get("rejectCount", 0))
    return sorted(rcs)


def scan_info(cust):
    """xray / fingerprint 적발 정보."""
    out = []
    for k, label in (("xray", "X-ray"), ("fingerprint", "지문")):
        sc = cust.get(k)
        if sc:
            out.append((label, sc.get("result", ""), sc.get("detail", ""), sc.get("extra", "")))
    return out


# ── 문서 작성 ────────────────────────────────────────────────────
def build():
    src = load_json(SOURCE)
    schedule = load_source_sheet(src, "day_schedule")
    doc_req = load_source_sheet(src, "document_requirement")
    ending = load_source_sheet(src, "ending")

    # (day,slot) -> valid_chance
    vc = {}
    for r in schedule:
        vc[(int(r["day"]), int(r["slot"]))] = r.get("valid_chance", "")

    days = {}
    for d in range(1, 15):
        days[d] = load_json(os.path.join(GAMEDATA, "day%d.json" % d))

    alerts = []  # 검수 경보 수집 (보조검사 적발 vs 승인 등)

    L = []
    w = L.append

    # 헤더
    w("# 「여권 주세요」 — 1~14일차 시나리오 검수 워크스루\n")
    w("> 자동 생성 문서 (`Tools/QA/gen_scenario_walkthrough.py`). 데이터 진실원본: "
      "`Assets/Resources/GameData/day*.json` + `Assets/GameData/_source/GameData.source.json`.\n")
    w("> 추측/창작 없음. 데이터에 있는 내용만 옮겼다. 빈 부분은 `(없음)`으로 표기.\n")
    w("> **읽는 법:** 일자 헤더(테마) -> 손님(슬롯 1->7) -> 등장 대사 -> 서류/결함 -> "
      "당신이 해야 할 판정(굵게) -> 특수 흐름 -> 마무리. 플레이하며 한 줄씩 대조하라.\n")

    # ── 0) 전체 개요 ──
    w("\n---\n\n## 0) 전체 개요\n")

    # 난이도 곡선(거절 비율)
    w("\n### 0-1. 난이도 곡선 — 일자별 거절(거부) 비율\n")
    w("거부가 정답인 손님 수가 많을수록 그날 '함정'이 많다는 뜻이다. (정답=`correctResult` 기준)\n")
    w("\n| 일차 | 거부(⛔) | 승인(✅) | 거부비율 |")
    w("|---|---|---|---|")
    for d in range(1, 15):
        custs = days[d]["customers"]
        rej = sum(1 for c in custs if not is_normal(c))
        app = len(custs) - rej
        ratio = "%d%%" % round(rej / len(custs) * 100) if custs else "-"
        w("| Day %d | %d | %d | %s |" % (d, rej, app, ratio))

    # 규정/기믹 도입 시점
    w("\n### 0-2. 규정·서류 도입 시점 (document_requirement)\n")
    w("\n| 서류 | 적용 일자 | 대상 | 비고 |")
    w("|---|---|---|---|")
    for r in doc_req:
        w("| %s | Day %s~%s | %s | %s |" % (
            r.get("document_type", ""), r.get("day_from", ""), r.get("day_to", ""),
            r.get("applies_to", ""), r.get("note", "")))
    w("\n기믹 도입 흐름(데이터 관측): **비자=Day 3부터**, **PCR검사서(검역)=Day 5~7**, "
      "**취업증빙=Day 8부터**, **보조검사(지문/X-ray)·범죄/테러 캐릭터=Day 11~14**.\n")

    # 엔딩 카운터 개요
    w("\n### 0-3. 엔딩으로 가는 누적 카운터·조기/히든 트리거 (ending)\n")
    w("점수 구간 엔딩은 회차 종료 시, 조기/히든 엔딩은 누적 카운터/시퀀스로 발동된다.\n")
    w("\n| ID | 분류 | 엔딩명 | 점수구간 | 발동시점 | 트리거 |")
    w("|---|---|---|---|---|---|")
    for e in ending:
        smin = e.get("score_min")
        smax = e.get("score_max")
        rng = "%s ~ %s" % (smin, smax) if smin is not None else "-"
        w("| #%s | %s | %s | %s | %s | %s |" % (
            e.get("ending_id", ""), e.get("ending_type", ""), e.get("ending_name", ""),
            rng, e.get("end_timing", ""), e.get("trigger_condition", "")))
    w("\n주요 누적 카운터: **#15 방역 실패**(검역 중 잘못된 입국 허가 3회, Day5~7), "
      "**#16 등잔 밑이 어둡다**(외국 여권 출국 도장 없음 묵인 3회), "
      "**#17 적성에 안 맞네**(단일 일자 오판 4명 이상, Day1~3), "
      "**#11 공범 / #12 순진한 녀석**(범죄자 제안 응함, Day11~14), "
      "**#13 포교 성공 / #14 난 돈을 믿어**(사이비 3회차 시퀀스).\n")

    # ── 1) 일자별 ──
    for d in range(1, 15):
        day = days[d]
        custs = sorted(day["customers"], key=lambda c: c["slot"])
        rej = sum(1 for c in custs if not is_normal(c))

        # 테마 추정: 그날 새 규정/뉴스 제목
        rules = day.get("rules", [])
        news = day.get("news", [])
        theme_bits = []
        if rules:
            theme_bits.append(rules[0]["title"])
        if news:
            theme_bits.append(news[0]["title"])
        # 도입 서류 표시
        intro = []
        for r in doc_req:
            if str(r.get("day_from")) == str(d):
                intro.append(r.get("document_type", ""))
        if intro:
            theme_bits.append("[신규 서류: %s]" % ", ".join(intro))
        theme = " / ".join(theme_bits) if theme_bits else "(특별 브리핑 없음)"

        w("\n---\n")
        w("\n## Day %d — %s\n" % (d, theme))
        w("\n그날 거부(⛔) %d명 / 총 %d명. " % (rej, len(custs)))

        # 브리핑: 규정/뉴스
        w("\n### 브리핑\n")
        if rules:
            w("\n**적용 규정:**\n")
            for r in rules:
                w("- **%s** — %s" % (r.get("title", ""), r.get("content", "")))
        else:
            w("\n**적용 규정:** (이 일자 day JSON에 규정 항목 없음 — 이전 일자 규정 누적 적용)")
        if news:
            w("\n\n**뉴스 헤드라인:**\n")
            for n in news:
                w("- *%s* — %s" % (n.get("title", ""), n.get("content", "")[:120]))
                # 스캔 잠금 해제 단서
                for cl in n.get("claims", []):
                    if cl.get("unlocksScan"):
                        w("  - 🔓 보조검사 단서: `%s=%s` -> **%s 스캔 잠금 해제** (%s)" % (
                            cl.get("attr", ""), cl.get("value", ""), cl.get("unlocksScan", ""),
                            cl.get("label", "")))
        else:
            w("\n\n**뉴스 헤드라인:** (없음)")

        # 손님 진행
        w("\n\n### 손님 진행 (슬롯 1 -> %d)\n" % len(custs))
        for c in custs:
            slot = c["slot"]
            vchance = vc.get((d, slot), "?")
            w("\n#### 슬롯 %d. %s (%s)\n" % (slot, c["nameKr"], c["characterType"]))

            # 메타 한 줄
            nat = c.get("nationality", "")
            w("\n- 국적 %s · 슬롯 정상확률 `valid_chance=%s`" % (nat, vchance))

            # 등장 대사
            ents = entrance_line(c)
            if ents:
                w("- **등장 대사:**")
                for sp, tx in ents:
                    w("  - %s: “%s”" % (sp, tx))
            else:
                w("- **등장 대사:** (없음)")

            # 상황/서류
            dfs = defect_docs(c)
            scans = scan_info(c)
            if is_normal(c) and not scans:
                w("- **상황/서류:** 모든 요구 서류 정상. 결함 없음.")
            else:
                if dfs:
                    parts = []
                    for dt, vf in dfs:
                        parts.append("**%s**의 `%s` 항목" % (dt, vf or "?"))
                    w("- **상황/서류:** %s 에 결함. (그 외 서류·필드는 진실 그대로)" % ", ".join(parts))
                if scans:
                    for label, res, det, ex in scans:
                        w("- **보조검사(%s):** 결과 `%s` — %s%s" % (
                            label, res, det, (" / 위치·신원: %s" % ex if ex else "")))
                if is_normal(c) and scans:
                    # 스캔이 있는데 정상? (적발 아닌 경우)
                    pass
                if not dfs and not scans and not is_normal(c):
                    w("- **상황/서류:** 거부가 정답이나 day JSON에 결함 서류·보조검사 표기 없음 (데이터 확인 필요).")

            # 보조검사가 '적발'인데 정답이 승인이면 규약 위반(검수 경보)
            scan_hit = any(("적발" in (res or "")) for _, res, _, _ in scans)
            contradiction = is_normal(c) and scan_hit

            # 플레이어가 해야 할 것
            reason = correct_result_lines(c)
            if is_normal(c):
                why = "서류·검사 모두 정상이므로 통과시킨다."
                if scan_hit:
                    why = ("데이터상 `correctResult=정상 승인`이지만 보조검사가 **적발**로 잡힘 "
                           "— 공통규약 4장 규칙3(보조검사 적발=거절이 정답)과 **모순**. 검수 필요.")
            else:
                if dfs:
                    dt, vf = dfs[0]
                    why = "%s의 `%s`가 규정 위반 -> 입국 거부." % (dt, vf or "?")
                elif scans:
                    why = "보조검사 적발(위험물/위장 신원) -> 입국 거부."
                else:
                    why = "거부가 정답."
            w("- **해야 할 판정:** %s — %s" % (verdict_mark(c), why))
            if contradiction:
                w("- ⚠️ **검수 경보:** 보조검사 적발과 정답(승인)이 충돌. 의도(예: '제안에 응하면 조기엔딩' 분기)인지 데이터 오류인지 확인 필요.")
                alerts.append("Day %d 슬롯%d %s (%s): 보조검사 적발인데 정답=정상 승인" % (
                    d, c["slot"], c["nameKr"], c["characterType"]))

            # 특수 흐름
            specials = []
            for label, res, det, ex in scans:
                specials.append("%s 스캔으로 적발 (%s)" % (label, det))
            loop = has_reject_loop(c)
            if loop:
                specials.append("정상 고객을 잘못 거절 시 항의 -> 재심사 루프(최대 reject_count %d)" % max(loop))
            ctype = c.get("characterType", "")
            if "범죄자" in ctype or "테러범" in ctype:
                specials.append("위험 캐릭터(%s): 오판 시 조기엔딩 카운터(#11/#12 또는 위험물 반입) 연동 가능" % ctype)
            if "검역" in ctype or "PCR" in ctype:
                specials.append("검역 구간 손님: 잘못 입국 허가 누적 시 #15 방역 실패 위험")
            if "★" in ctype:
                specials.append("특수(VIP) 손님: 특별 분기/대사 가능")
            if specials:
                w("- **특수 흐름:** " + "; ".join(specials) + ".")

            # 결과(정답 대사)
            if reason:
                w("- **정답 시 흐름:**")
                for sp, tx in reason:
                    w("  - %s: “%s”" % (sp, tx))

        # 일자 마무리
        w("\n\n### 일자 마무리 — 핵심 포인트\n")
        traps = []
        # 함정: 거부 손님의 결함을 요약
        for c in custs:
            if not is_normal(c):
                dfs = defect_docs(c)
                if dfs:
                    dt, vf = dfs[0]
                    traps.append("슬롯%d %s(%s 결함)" % (c["slot"], c["nameKr"], vf or dt))
                else:
                    sc = scan_info(c)
                    if sc:
                        traps.append("슬롯%d %s(%s 적발)" % (c["slot"], c["nameKr"], sc[0][2]))
                    else:
                        traps.append("슬롯%d %s(거부)" % (c["slot"], c["nameKr"]))
        if traps:
            w("- 놓치기 쉬운 거부 포인트: " + ", ".join(traps) + ".")
        else:
            w("- 이 날은 거부 대상 없음(전원 승인). 과도한 거절은 잘못 거절 -> 재심사 루프 유발.")
        # 카운터 변화 가능성
        cc = []
        if any(("검역" in c.get("characterType", "") or "PCR" in c.get("characterType", "")) for c in custs):
            cc.append("#15 방역 실패(검역 손님 오허가 누적)")
        if any(("범죄자" in c.get("characterType", "") or "테러범" in c.get("characterType", "")) for c in custs):
            cc.append("범죄/위험물 조기엔딩(#11/#12 등) 카운터")
        if d <= 3:
            cc.append("#17 적성에 안 맞네(단일 일자 오판 4명+)")
        cc.append("#16 등잔 밑이 어둡다(외국 여권 출국 도장 묵인) · 점수 누적")
        w("- 누적 카운터 변화 가능성: " + ", ".join(cc) + ".")

    # ── 부록) 검수 경보 모음 ──
    w("\n---\n\n## 부록 A) 자동 검출된 검수 경보 (보조검사 적발 vs 정답 충돌)\n")
    if alerts:
        w("\n공통규약 4장 규칙3은 '보조검사 적발 = 거절이 정답'이다. "
          "아래 손님들은 적발인데 `correctResult=정상 승인`이라 충돌한다. "
          "**의도된 분기(제안에 응하면 통과시켜 조기엔딩으로 가는 함정)인지, 데이터 오류인지** 확인 필요.\n")
        for a in alerts:
            w("- ⚠️ %s" % a)
    else:
        w("\n자동 검출된 충돌 없음.")

    w("")
    return "\n".join(L)


def main():
    text = build()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    # 콘솔 인코딩 회피
    out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    out.write("WROTE %s (%d chars)\n" % (OUT, len(text)))
    out.flush()


if __name__ == "__main__":
    main()
