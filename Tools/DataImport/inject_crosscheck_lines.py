# -*- coding: utf-8 -*-
"""
대사_스크립트.xlsx 의 "서류 대조(...)" 단계 대사를 손님별 crossCheckLines 로 dayN.json 에 주입한다.

- 마스터(권위) = data/대사_스크립트.xlsx (DayN 시트).
- 각 "서류 대조" 행: 검사관 라인(다음 행이 손님이면 손님 반응)을 한 쌍으로 묶고,
  분기 괄호 텍스트("여권 성별 ↔ 본인" 등)를 속성 키(attr)로 매핑해 CrossCheckLine 을 만든다.
- dayN.json 의 customers[*] 를 nameKr 로 매칭(슬롯/일자로 보조 검증)해 crossCheckLines 를 덮어쓴다.
- idempotent: 같은 입력이면 같은 결과. 기존 crossCheckLines 는 통째로 교체.

사용:
  python inject_crosscheck_lines.py --day 12 --only-name "사토 하루키"   # 사토만
  python inject_crosscheck_lines.py --day all                            # 전 일자
  python inject_crosscheck_lines.py --day 12 --dry-run                   # 적용 없이 미리보기
"""
import argparse
import io
import json
import os
import re

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
XLSX = os.path.join(ROOT, "data", "대사_스크립트.xlsx")
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")

# 분기 괄호 안 키워드 → 속성 키(attr) 매핑. 우선순위 순서대로 첫 매칭.
# 키워드는 "서류 대조(...)" 의 괄호 내용에서 찾는다.
ATTR_RULES = [
    # 가장 구체적인 것 먼저(부분 문자열 충돌 방지)
    ("입사일", "hire_date"),            # 재직증명서 입사일 ↔ 비자 발급일
    ("인증기관", "lab_name"),           # PCR 검사서 ↔ 인증기관
    ("회사", "company_name"),           # 재직증명서 ↔ 입국 금지 회사 목록
    ("성별", "gender"),                 # 여권 성별 ↔ 본인
    ("여권번호", "passport_no"),        # 여권번호 ↔ 발급국 / 비자 ↔ 여권 번호 / 여권 ↔ 비자 여권번호
    ("여권 번호", "passport_no"),
    ("번호", "passport_no"),            # "비자 ↔ 여권 번호"
    ("국적", "nationality"),            # 여권 ↔ 비자 국적
    ("만료일", "expiry_date"),          # 여권 만료일 ↔ 오늘 날짜
    ("검사일", "test_date"),            # PCR ↔ 오늘 날짜(검사일)
    ("규정집", "pcr_result"),           # PCR 검사서 ↔ 규정집(양성/공란)
    ("오늘 날짜", "expiry_date"),       # 비자 ↔ 오늘 날짜(만료)
]

# 이름(name) 대조: 괄호에 위 키워드가 없고 두 서류만 나열될 때(예 "여권 ↔ 비자", "PCR ↔ 여권",
# "재직증명서 ↔ 여권"). 검사관/손님 대사가 "이름" 문맥이라 name 으로 본다.
NAME_FALLBACK = "name"


def attr_for_branch(branch):
    """'서류 대조(...)' 괄호 내용 → 속성 키. 못 찾으면 NAME_FALLBACK(이름 대조)."""
    inside = branch
    m = re.search(r"\((.*)\)", branch)
    if m:
        inside = m.group(1)
    # 특례: PCR 날짜 대조는 test_date(런타임 TryEvaluateDate 가 test_date 키로 만든다).
    # 'PCR' + ('오늘'|'날짜'|'검사일') → test_date (만료일 expiry_date 와 구분).
    if "PCR" in inside and ("오늘" in inside or "날짜" in inside or "검사일" in inside):
        return "test_date"
    for kw, attr in ATTR_RULES:
        if kw in inside:
            return attr
    return NAME_FALLBACK


def load_sheet_crosschecks(ws):
    """한 DayN 시트에서 손님별 crossCheckLines 를 추출. {nameKr: [CrossCheckLine,...]}"""
    result = {}
    cur_name = ""
    max_row = ws.max_row
    r = 3
    while r <= max_row:
        name = ws.cell(row=r, column=3).value
        branch = ws.cell(row=r, column=6).value
        speaker = ws.cell(row=r, column=7).value
        text = ws.cell(row=r, column=8).value
        if name:
            cur_name = str(name).strip()
        if branch and "서류 대조" in str(branch):
            attr = attr_for_branch(str(branch))
            inspector = ""
            customer = ""
            # 이 행 + 다음 행들에서 (검사관 첫 라인 / 손님 첫 라인)을 잡는다.
            # 다음 '분기'가 시작되면 중단.
            rr = r
            while rr <= max_row:
                br2 = ws.cell(row=rr, column=6).value
                if rr > r and br2 and str(br2).strip():
                    break  # 다음 분기 시작
                sp = ws.cell(row=rr, column=7).value
                tx = ws.cell(row=rr, column=8).value
                sp = "" if sp is None else str(sp).strip()
                tx = "" if tx is None else str(tx).strip()
                if sp == "심사관" and not inspector and tx:
                    inspector = tx
                elif sp and sp != "심사관" and not customer and tx:
                    customer = tx
                rr += 1
            if inspector or customer:
                result.setdefault(cur_name, [])
                result[cur_name].append({
                    "attr": attr,
                    "inspector": inspector,
                    "customer": customer,
                })
        r += 1
    return result


def main():
    import openpyxl

    ap = argparse.ArgumentParser()
    ap.add_argument("--day", default="all", help="대상 일차(숫자) 또는 all")
    ap.add_argument("--only-name", default=None, help="이 nameKr 손님만 주입")
    ap.add_argument("--dry-run", action="store_true", help="적용 없이 미리보기")
    args = ap.parse_args()

    wb = openpyxl.load_workbook(XLSX, data_only=True)

    if args.day == "all":
        days = list(range(1, 15))
    else:
        days = [int(args.day)]

    log = io.open("_inject_crosscheck_log.txt", "w", encoding="utf-8")
    total_injected = 0

    for day in days:
        sheet = "Day%d" % day
        if sheet not in wb.sheetnames:
            continue
        json_path = os.path.join(GAMEDATA, "day%d.json" % day)
        if not os.path.exists(json_path):
            log.write("[skip] %s 없음\n" % json_path)
            continue

        cc_by_name = load_sheet_crosschecks(wb[sheet])
        with io.open(json_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        changed = False
        for cust in data.get("customers", []):
            nk = (cust.get("nameKr") or "").strip()
            if args.only_name and nk != args.only_name:
                continue
            lines = cc_by_name.get(nk)
            if not lines:
                continue
            before = cust.get("crossCheckLines")
            cust["crossCheckLines"] = lines
            changed = True
            total_injected += len(lines)
            # 확률 손님(altVariant 존재, 실제 변형)이면 변형 측에도 동일 대사를 단다.
            # 불일치는 불량 변형이 활성일 때만 발생하므로, 어느 쪽이 활성이든 대사가 따라가게 한다.
            # (정상 측은 불일치가 없어 발화되지 않음 — 무해.)
            av = cust.get("altVariant")
            also_variant = ""
            if isinstance(av, dict) and av.get("correctResult"):
                av["crossCheckLines"] = lines
                also_variant = " (+altVariant)"
            log.write("[day%d] %s (id=%s) crossCheckLines: %s -> %d개%s\n" % (
                day, nk, cust.get("customerId"),
                ("없음" if before is None else "%d개" % len(before)), len(lines), also_variant))
            for ln in lines:
                log.write("    attr=%s\n      검사관: %s\n      손님:   %s\n" % (
                    ln["attr"], ln["inspector"], ln["customer"]))

        if changed and not args.dry_run:
            with io.open(json_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            log.write("[saved] %s\n" % json_path)
        elif changed:
            log.write("[dry-run] %s 변경 예정(미저장)\n" % json_path)

    log.write("\n총 주입 라인 수: %d\n" % total_injected)
    log.close()
    print("done; log=_inject_crosscheck_log.txt")


if __name__ == "__main__":
    main()
