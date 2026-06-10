# -*- coding: utf-8 -*-
"""
validate_data.py — 무결성 검증 리포트 (data-tools 소유)

중간 JSON(GameData.source.json)을 읽어 다음을 검증, 결과를 UTF-8 md로 저장:
 1. FK 참조 누락 (customer_id / schedule_id / dialogue_case_id)
 2. 14일 x 7슬롯(98건) 채움 + day/slot 중복
 3. 확률 범위 0~1 (valid_chance, random_valid_chance)
 4. 요구 서류 일관성 (document_requirement vs day_schedule 범위)
출력: Assets/GameData/_source/_validation_report.md
"""
import sys
import os
import json
import re

DEFAULT_IN = r"C:\Users\chris\Documents\produc_build_reecture\Assets\GameData\_source\GameData.source.json"
DEFAULT_OUT = r"C:\Users\chris\Documents\produc_build_reecture\Assets\GameData\_source\_validation_report.md"


def to_float(s):
    if s is None:
        return None
    try:
        return float(s)
    except (ValueError, TypeError):
        return None


def to_int(s):
    if s is None:
        return None
    try:
        return int(float(s))
    except (ValueError, TypeError):
        return None


def main():
    inp = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN
    out = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT

    with open(inp, "r", encoding="utf-8") as f:
        data = json.load(f)
    sheets = data["sheets"]

    def rows(name):
        return sheets.get(name, {}).get("rows", [])

    errors = []
    notes = []

    # ── 인덱스 ──
    customer_ids = set(r.get("customer_id") for r in rows("customer"))
    schedule_ids = set(r.get("schedule_id") for r in rows("day_schedule"))
    dialogue_case_ids = set(r.get("dialogue_case_id") for r in rows("dialogue_case"))

    # ── 1. FK: customer_id 참조 ──
    for tbl in ["passport", "visa", "pcr_test", "employment_cert",
                "day_schedule", "dialogue_case", "xray", "fingerprint"]:
        for r in rows(tbl):
            cid = r.get("customer_id")
            if cid is None or cid == "":
                continue
            if cid not in customer_ids:
                errors.append("[FK누락] %s.customer_id=%s -> customer 없음" % (tbl, cid))

    # dialogue_case.schedule_id -> day_schedule
    for r in rows("dialogue_case"):
        sid = r.get("schedule_id")
        if sid and sid not in schedule_ids:
            errors.append("[FK누락] dialogue_case.schedule_id=%s -> day_schedule 없음" % sid)

    # dialogue_line.dialogue_case_id -> dialogue_case
    for r in rows("dialogue_line"):
        dcid = r.get("dialogue_case_id")
        if dcid and dcid not in dialogue_case_ids:
            errors.append("[FK누락] dialogue_line.dialogue_case_id=%s -> dialogue_case 없음" % dcid)

    # reward.related_ending_id -> ending (nullable)
    ending_ids = set(r.get("ending_id") for r in rows("ending"))
    for r in rows("reward"):
        eid = r.get("related_ending_id")
        if eid and eid not in ending_ids:
            errors.append("[FK누락] reward.related_ending_id=%s -> ending 없음" % eid)

    # ── 2. day_schedule 14x7 채움 ──
    ds = rows("day_schedule")
    notes.append("day_schedule 데이터 행 수: %d (기대 98)" % len(ds))
    seen = {}
    for r in ds:
        d = to_int(r.get("day"))
        s = to_int(r.get("slot"))
        if d is None or s is None:
            errors.append("[일정] day/slot 비어있음: %s" % r)
            continue
        key = (d, s)
        if key in seen:
            errors.append("[일정중복] day=%d slot=%d 가 2회 이상" % (d, s))
        seen[key] = True
    missing = []
    for d in range(1, 15):
        for s in range(1, 8):
            if (d, s) not in seen:
                missing.append("day=%d slot=%d" % (d, s))
    if missing:
        errors.append("[일정누락] 빈 슬롯 %d개: %s" % (len(missing), ", ".join(missing)))
    else:
        notes.append("14일 x 7슬롯 98건 전부 채움 OK")

    # ── 3. 확률 범위 ──
    for r in ds:
        vc = to_float(r.get("valid_chance"))
        if vc is None:
            errors.append("[확률] day_schedule schedule_id=%s valid_chance 파싱불가=%r"
                          % (r.get("schedule_id"), r.get("valid_chance")))
        elif not (0.0 <= vc <= 1.0):
            errors.append("[확률범위] day_schedule schedule_id=%s valid_chance=%s (0~1 벗어남)"
                          % (r.get("schedule_id"), vc))

    for r in rows("defect_rule"):
        raw = r.get("random_valid_chance")
        f = to_float(raw)
        if f is None:
            # '고정(통과1/거절0)' 같은 텍스트는 경고로만
            notes.append("[확률주의] defect_rule rule_id=%s random_valid_chance 비수치=%r (런타임 특수처리 필요)"
                         % (r.get("rule_id"), raw))
        elif not (0.0 <= f <= 1.0):
            errors.append("[확률범위] defect_rule rule_id=%s random_valid_chance=%s (0~1 벗어남)"
                          % (r.get("rule_id"), f))

    # ── 4. 요구 서류 일관성 ──
    for r in rows("document_requirement"):
        df = to_int(r.get("day_from"))
        dt = to_int(r.get("day_to"))
        if df is None or dt is None:
            errors.append("[요구서류] req_id=%s day_from/day_to 파싱불가" % r.get("req_id"))
            continue
        if df > dt:
            errors.append("[요구서류] req_id=%s day_from(%d) > day_to(%d)" % (r.get("req_id"), df, dt))
        if df < 1 or dt > 14:
            errors.append("[요구서류] req_id=%s 범위 1~14 벗어남 (%d~%d)" % (r.get("req_id"), df, dt))

    # 비자 요구일(3~14)인데 외국인 고객 비자 데이터 있는지 가벼운 체크
    visa_customers = set(r.get("customer_id") for r in rows("visa"))
    notes.append("비자 보유 고객 수: %d" % len(visa_customers))
    notes.append("PCR 보유 고객 수: %d" % len(set(r.get("customer_id") for r in rows("pcr_test"))))
    notes.append("취업증빙 보유 고객 수: %d" % len(set(r.get("customer_id") for r in rows("employment_cert"))))

    # ── 5. fingerprint 옵션B 스키마 가드 ──
    # 옛 컬럼(result/match_status/matched_person)이 남아 있으면 build_days 가 옛 구조로 굴러
    # 지문 record 가 전부 빈칸이 되는 landmine. 옵션B 필수 컬럼 + mode 어휘를 검증한다.
    fp_sheet = sheets.get("fingerprint", {})
    fp_cols = set(c.get("key") for c in fp_sheet.get("columns", []))
    fp_rows = fp_sheet.get("rows", [])
    OLD_FP_COLS = {"result", "match_status", "matched_person", "customer_name"}
    REQUIRED_FP_COLS = {"fingerprint_id", "customer_id", "mode", "alt_name",
                        "alt_birth", "alt_nationality", "criminal_record", "wanted_no"}
    ALLOWED_FP_MODES = {"성형", "수배자"}
    if fp_cols:
        stale = OLD_FP_COLS & fp_cols
        if stale:
            errors.append("[지문스키마] fingerprint 에 옛 컬럼 잔존=%s "
                          "(옵션B 미반영 → xlsx_to_json 재실행 필요)" % ", ".join(sorted(stale)))
        missing_fp = REQUIRED_FP_COLS - fp_cols
        if missing_fp:
            errors.append("[지문스키마] fingerprint 옵션B 필수 컬럼 누락=%s" % ", ".join(sorted(missing_fp)))
        for r in fp_rows:
            mode = (r.get("mode") or "").strip()
            if mode and mode not in ALLOWED_FP_MODES:
                errors.append("[지문mode] fingerprint_id=%s mode=%r (허용: 성형|수배자)"
                              % (r.get("fingerprint_id"), mode))
            # 수배자 모드는 범죄기록/수배번호가 있어야 한다(빈칸이면 표시 깨짐).
            if mode == "수배자":
                if not (r.get("criminal_record") or "").strip():
                    errors.append("[지문수배] fingerprint_id=%s 수배자인데 criminal_record 비어있음"
                                  % r.get("fingerprint_id"))
                if not (r.get("wanted_no") or "").strip():
                    errors.append("[지문수배] fingerprint_id=%s 수배자인데 wanted_no 비어있음"
                                  % r.get("fingerprint_id"))
        notes.append("fingerprint 행 수: %d (옵션B 컬럼 OK)" % len(fp_rows))

    # 진실서류 미보유 고객 (passport 없는 customer) — 참고용
    pass_customers = set(r.get("customer_id") for r in rows("passport"))
    no_pass = sorted([c for c in customer_ids if c not in pass_customers], key=lambda x: to_int(x) or 0)
    if no_pass:
        notes.append("[참고] passport 없는 customer_id: %s (변조/특수 케이스 가능)" % ", ".join(no_pass))

    # ── 리포트 작성 ──
    md = []
    md.append("# 여권 주세요 — 데이터 검증 리포트")
    md.append("")
    md.append("- 원본: `%s`" % data.get("source_file"))
    md.append("- 시트: %d개" % len(sheets))
    md.append("- **오류(error): %d건**" % len(errors))
    md.append("- 참고(note): %d건" % len(notes))
    md.append("")
    md.append("## 오류 (해결 필요)")
    if errors:
        for e in errors:
            md.append("- %s" % e)
    else:
        md.append("- 없음 (참조 누락/범위 오류 0)")
    md.append("")
    md.append("## 참고 / 정보")
    for n in notes:
        md.append("- %s" % n)
    md.append("")
    if data.get("warnings"):
        md.append("## 변환기 경고")
        for w in data["warnings"]:
            md.append("- %s" % w)

    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(md))

    sys.stdout.write("VALIDATE errors=%d notes=%d out=%s\n" % (len(errors), len(notes), out))


if __name__ == "__main__":
    main()
