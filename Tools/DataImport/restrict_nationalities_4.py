# -*- coding: utf-8 -*-
"""
restrict_nationalities_4.py — 국적을 4개국(KOR/USA/CHN/JPN)으로 제한 + 이름 정합 (data-tools 소유)

목적:
  게임 등장 국적을 대한민국/미국/중국/일본 4개로 제한한다.
  14종(손님 15명) 외국 국적 손님을 USA/CHN/JPN으로 결정론적으로 재배정하고,
  이름(name_kr/name_en)을 새 국적에 어울리게 교체한다.
  customer / passport / visa 3시트의 nationality 표기, name_en, passport_no/visa_no
  국가코드 접두사까지 한 손님 단위로 정합되게 갱신한다.

설계:
  - 진실 데이터만 수정한다(런타임 변조는 gameplay 소유, 건드리지 않음).
  - 명시적 매핑 테이블(REASSIGN)을 단일 소스로 둔다 → idempotent.
    재실행해도 (이미 KOR/USA/CHN/JPN인 손님은 건너뛰므로) 같은 결과.
  - 분산 규칙: 대상 손님을 customer_id 오름차순 정렬 후 USA→CHN→JPN 순환(균등 5/5/5).
  - 이름 정합: 한 손님의 name_en이 customer/passport/visa 전부에서 동일.
    passport.name_en은 원본 'LAST<<FIRST' MRZ식 표기를 유지(임포터 clean_name이 흡수).
  - passport_no 2글자 코드 접두사, visa_no 'V-XXX-####' 3글자 코드 접두사를
    새 국적 코드로 교체(숫자부 유지 → 번호 충돌 없음, 트리거 키 안정).

사용:
  python restrict_nationalities_4.py            # 기본 xlsx in-place 수정
  python restrict_nationalities_4.py 입력.xlsx   # 지정 파일 수정
"""
import sys
import os
import re
import openpyxl

# 저장소 상대경로(PC 독립). 일회성 마이그레이션 스크립트지만 하드코딩 경로 제거.
_REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_XLSX = os.path.join(_REPO, "data", "여권_정리_updated.xlsx")

# 허용 국적 코드(이 4개 외 진실 데이터는 모두 재배정 대상)
ALLOWED = {"KOR", "USA", "CHN", "JPN"}

# 국적 코드 -> customer.nationality 표시 문자열("한글(코드)")
NAT_DISPLAY = {
    "KOR": "대한민국(KOR)",
    "USA": "미국(USA)",
    "CHN": "중국(CHN)",
    "JPN": "일본(JPN)",
}
# 국적 코드 -> passport_no 2글자 접두사 / visa_no 3글자 코드
PP_PREFIX = {"USA": "US", "CHN": "CN", "JPN": "JP", "KOR": "KO"}
VISA_CODE = {"USA": "USA", "CHN": "CHN", "JPN": "JPN", "KOR": "KOR"}

# ── 재배정 매핑 (단일 소스) ──────────────────────────────────────
# customer_id -> (옛코드, 새코드, name_en, name_kr)
# 분산: id 오름차순 USA/CHN/JPN 순환. 이름은 새 국적에 맞춰 교체(전 서류 동일).
REASSIGN = {
    4:  ("ESP", "USA", "JAMES MILLER",   "제임스 밀러"),
    7:  ("FRA", "CHN", "WANG WEI",       "왕 웨이"),
    11: ("PAK", "JPN", "SATO HARUKI",    "사토 하루키"),
    13: ("VNM", "USA", "ROBERT JOHNSON", "로버트 존슨"),
    14: ("GBR", "CHN", "LI NA",          "리 나"),
    19: ("IND", "JPN", "TANAKA YUI",     "다나카 유이"),
    25: ("GBR", "USA", "WILLIAM BROWN",  "윌리엄 브라운"),
    27: ("MEX", "CHN", "ZHANG WEI",      "장 웨이"),
    31: ("BRA", "JPN", "SUZUKI AOI",     "스즈키 아오이"),
    32: ("EGY", "USA", "MICHAEL DAVIS",  "마이클 데이비스"),
    33: ("TWN", "CHN", "LIU YANG",       "류 양"),
    34: ("SYR", "JPN", "YAMAMOTO REN",   "야마모토 렌"),
    # 35: 본래 순환상 USA였으나 day12에서 손님8(USA,스캔트리거)과 국적 충돌 →
    #     트리거 손님8을 nationality 로 유일 식별하도록 35를 CHN 으로 조정.
    35: ("PHL", "CHN", "ZHAO LEI",       "자오 레이"),
    36: ("RUS", "CHN", "CHEN JING",      "천 징"),
    # 37: 본래 JPN이었으나 day14에서 손님34(JPN,스캔트리거)와 충돌 →
    #     트리거 손님34를 nationality 로 유일 식별하도록 37을 USA 로 조정.
    37: ("BGD", "USA", "JESSICA WILSON", "제시카 윌슨"),
}


def mrz_name(name_en):
    """'JAMES MILLER' -> 'MILLER<<JAMES' (passport 원본 MRZ식 표기 유지)."""
    parts = name_en.split()
    if len(parts) >= 2:
        first = parts[0]
        last = " ".join(parts[1:])
        return "%s<<%s" % (last, first)
    return name_en


def repp(no, newcode):
    """passport_no 앞 2글자 코드 접두사를 새 국적 2글자 코드로 교체(숫자부 유지)."""
    if not isinstance(no, str):
        return no
    m = re.match(r"^[A-Z]{2}(.*)$", no)
    if m:
        return PP_PREFIX[newcode] + m.group(1)
    return no


def revisa(no, newcode):
    """'V-ESP-3932' -> 'V-USA-3932' (3글자 코드만 교체)."""
    if not isinstance(no, str):
        return no
    return re.sub(r"^V-[A-Z]{3}-", "V-%s-" % VISA_CODE[newcode], no)


def col_index(ws, want):
    """row3(영문 컬럼명) 기준 컬럼 인덱스 맵."""
    idx = {}
    for c in range(1, ws.max_column + 1):
        v = ws.cell(3, c).value
        if v in want:
            idx[v] = c
    return idx


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_XLSX
    wb = openpyxl.load_workbook(path)  # 수식 없음 → data_only 불필요, 값/스타일 보존
    changes = []

    # customer 시트
    ws = wb["customer"]
    ci = col_index(ws, {"customer_id", "name_kr", "name_en", "nationality"})
    for r in range(5, ws.max_row + 1):
        cid = ws.cell(r, ci["customer_id"]).value
        if cid in REASSIGN:
            old, new, en, kr = REASSIGN[cid]
            ws.cell(r, ci["nationality"]).value = NAT_DISPLAY[new]
            ws.cell(r, ci["name_en"]).value = en
            ws.cell(r, ci["name_kr"]).value = kr
            changes.append(("customer", cid, "%s->%s" % (old, new), en))

    # passport 시트
    ws = wb["passport"]
    pi = col_index(ws, {"customer_id", "passport_no", "name_en", "nationality"})
    for r in range(5, ws.max_row + 1):
        cid = ws.cell(r, pi["customer_id"]).value
        if cid in REASSIGN:
            old, new, en, kr = REASSIGN[cid]
            ws.cell(r, pi["nationality"]).value = new
            ws.cell(r, pi["name_en"]).value = mrz_name(en)
            ws.cell(r, pi["passport_no"]).value = repp(ws.cell(r, pi["passport_no"]).value, new)

    # visa 시트
    ws = wb["visa"]
    vi = col_index(ws, {"customer_id", "visa_no", "nationality"})
    for r in range(5, ws.max_row + 1):
        cid = ws.cell(r, vi["customer_id"]).value
        if cid in REASSIGN:
            old, new, en, kr = REASSIGN[cid]
            ws.cell(r, vi["nationality"]).value = new
            ws.cell(r, vi["visa_no"]).value = revisa(ws.cell(r, vi["visa_no"]).value, new)

    # fake_value_pool: nationality 가짜값을 4개국 코드로 정리(비4개국 코드 제거).
    # build_days 는 이미 진짜국적 제외 4개국에서 위조국적을 고르므로 baked 엔 영향 없지만,
    # 단일 소스(엑셀)에서도 비4개국 코드가 남지 않도록 정합.
    if "fake_value_pool" in wb.sheetnames:
        ws = wb["fake_value_pool"]
        # fake_value_pool 은 row1 헤더(`field(필드명)`/`fake_value(가짜값)`), row2~ 데이터.
        fcol = vcol = None
        for c in range(1, ws.max_column + 1):
            h = str(ws.cell(3, c).value or "") + str(ws.cell(1, c).value or "")
            if "field" in h and fcol is None:
                fcol = c
            if "fake_value" in h and vcol is None:
                vcol = c
        if fcol and vcol:
            repl = {"IRL": "USA", "PRT": "JPN", "BEL": "CHN", "KAZ": "KOR"}
            for r in range(2, ws.max_row + 1):
                if str(ws.cell(r, fcol).value).startswith("nationality"):
                    cur = ws.cell(r, vcol).value
                    if cur in repl:
                        ws.cell(r, vcol).value = repl[cur]

    wb.save(path)
    print("저장: %s" % path)
    print("재배정 손님 %d명:" % len(changes))
    for c in changes:
        print("  customer", c[1], c[2], "->", c[3])


if __name__ == "__main__":
    main()
