# -*- coding: utf-8 -*-
"""
다채로운 날짜/기관 다양화 (passport/visa/pcr_test) — 결정론적(고정 시드).

원칙(불변식 보존):
- passport/visa/pcr 시트는 '진실(유효)' 데이터. 무효는 build_days.py가 런타임 주입.
- 게임 "오늘" = 2026-06-01 + (day-1). 모든 손님 등장일 <= day14(2026-06-14).
- 유효 손님 서류는 등장하는 모든 날에 유효해야 함:
    * passport/visa: expiry_date > 2026-06-14
    * pcr: valid_until >= 손님의 day5~7 등장일 (안전하게 >= 2026-06-12 유지)
- 의도적 무효 행은 무효성 보존(만료일을 과거로 유지, 값만 다양화):
    * visa_id=1 (cust3): expiry 과거 유지
    * pcr_id=1  (cust4): valid_until 과거 유지
- birth_date/age/이름/국적/한글 텍스트(visa_type/entry_type/memo/result)는 건드리지 않음(정합 보존).
- 셀은 모두 'YYYY-MM-DD' 문자열로 기록(원본과 동일 포맷).

실행: python Tools/DataImport/diversify_dates.py
"""
import os
import datetime as dt
import openpyxl

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
XLSX = os.path.join(REPO, "data", "여권_정리_updated.xlsx")

GAME_END = dt.date(2026, 6, 14)          # day14
SAFE_FUTURE = dt.date(2026, 9, 1)        # 유효 만료 최소 하한(여유)
PCR_VALID_MIN = dt.date(2026, 6, 14)     # 유효 pcr valid_until 최소 하한

# 의도적 무효 행
INVALID_VISA_IDS = {1}   # cust3
INVALID_PCR_IDS = {1}    # cust4


def add_years(d, years):
    try:
        return d.replace(year=d.year + years)
    except ValueError:  # 2/29
        return d.replace(year=d.year + years, day=28)


def add_months(d, months):
    m = d.month - 1 + months
    y = d.year + m // 12
    m = m % 12 + 1
    day = min(d.day, [31, 29 if y % 4 == 0 and (y % 100 != 0 or y % 400 == 0) else 28,
                      31, 30, 31, 30, 31, 31, 30, 31, 30, 31][m - 1])
    return dt.date(y, m, day)


def s(d):
    return d.strftime("%Y-%m-%d")


# 결정론적 의사난수(선형 합동) — 시드 고정, 외부 random 미사용
class Det:
    def __init__(self, seed):
        self.x = seed & 0x7fffffff

    def nxt(self):
        self.x = (self.x * 1103515245 + 12345) & 0x7fffffff
        return self.x

    def pick(self, lst):
        return lst[self.nxt() % len(lst)]

    def rng(self, lo, hi):  # inclusive
        return lo + self.nxt() % (hi - lo + 1)


def main():
    wb = openpyxl.load_workbook(XLSX)
    report = {"passport": [], "visa": [], "pcr": []}

    # ── passport ─────────────────────────────────────────
    ws = wb["passport"]
    rng = Det(20260603)
    # 발급월/일 풀(다양). 발급연도는 유효성 따라 하한 보장.
    months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]
    days = [3, 7, 11, 14, 18, 21, 25, 28]
    for r in range(5, ws.max_row + 1):
        pid = ws.cell(r, 1).value
        if pid is None:
            continue
        # 유효기간 5년/10년 섞기
        validity = rng.pick([5, 5, 10, 10, 10])  # 10년 비중↑(현실적)
        # 발급연도 범위: 만료(=issue+validity) > GAME_END(margin SAFE_FUTURE) 보장
        # issue_year + validity 의 9/1 이 SAFE_FUTURE(2026-09) 이후가 되도록.
        # 안전하게: issue_year >= 2026 - validity + 1
        lo = 2016 if validity == 10 else 2022
        hi = 2025 if validity == 10 else 2025
        iyear = rng.rng(lo, hi)
        imonth = rng.pick(months)
        iday = rng.pick(days)
        issue = dt.date(iyear, imonth, iday)
        expiry = add_years(issue, validity)
        # 안전장치: 유효 만료 보장
        while expiry <= SAFE_FUTURE:
            iyear += 1
            issue = dt.date(iyear, imonth, iday)
            expiry = add_years(issue, validity)
        ws.cell(r, 8).value = s(issue)
        ws.cell(r, 9).value = s(expiry)
        report["passport"].append((pid, ws.cell(r, 2).value, s(issue), s(expiry), validity))

    # ── visa ─────────────────────────────────────────────
    ws = wb["visa"]
    rng = Det(70260603)
    for r in range(5, ws.max_row + 1):
        vid = ws.cell(r, 1).value
        if vid is None:
            continue
        cid = ws.cell(r, 2).value
        # visa_no 접두사 유지, 숫자 접미만 다양화 (V-XXX-####)
        vno = ws.cell(r, 3).value
        if vno and "-" in vno:
            parts = vno.rsplit("-", 1)
            prefix = parts[0]
            newnum = rng.rng(1000, 9999)
            ws.cell(r, 3).value = f"{prefix}-{newnum}"
        if vid in INVALID_VISA_IDS:
            # 무효 유지: 발급/만료 모두 과거, 값만 다양화
            iyear = rng.rng(2022, 2023)
            imonth = rng.pick([1, 3, 5, 7, 9, 11])
            issue = dt.date(iyear, imonth, rng.pick([2, 10, 15, 20]))
            validity_m = rng.pick([6, 12])
            expiry = add_months(issue, validity_m)
            # 만료가 반드시 과거(GAME_END 이전)로
            while expiry >= GAME_END:
                issue = add_years(issue, -1)
                expiry = add_months(issue, validity_m)
            ws.cell(r, 6).value = s(issue)
            ws.cell(r, 7).value = s(expiry)
            report["visa"].append((vid, cid, s(issue), s(expiry), "INVALID(유지)"))
            continue
        # 유효: 만료 > GAME_END(margin)
        validity_m = rng.pick([6, 12, 12, 24, 24])  # 6개월/1년/2년 섞기
        # 발급은 과거~근접 과거, 만료가 SAFE_FUTURE 이후 되게 발급 하한 조정
        # issue = SAFE_FUTURE - validity_m + buffer 보다 늦게
        imonth = rng.pick([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
        iday = rng.pick([3, 8, 12, 17, 22, 27])
        # 발급연도 후보: 만료가 미래가 되도록
        iyear = rng.rng(2024, 2025)
        issue = dt.date(iyear, imonth, iday)
        expiry = add_months(issue, validity_m)
        while expiry <= SAFE_FUTURE:
            issue = add_months(issue, 1)
            expiry = add_months(issue, validity_m)
        ws.cell(r, 6).value = s(issue)
        ws.cell(r, 7).value = s(expiry)
        report["visa"].append((vid, cid, s(issue), s(expiry), f"{validity_m}m"))

    # ── pcr_test ─────────────────────────────────────────
    ws = wb["pcr_test"]
    rng = Det(99260603)
    labs = [
        "인천공항검역소", "국립검역소", "서울시립보건연구원",
        "부산국제공항검역소", "연세대학교의료원검사센터", "한국공항검역본부",
        "질병관리청진단검사센터", "삼성서울병원진단검사의학과",
    ]
    results = ["음성", "Negative", "음성(Negative)", "NEGATIVE"]
    for r in range(5, ws.max_row + 1):
        pid = ws.cell(r, 1).value
        if pid is None:
            continue
        cid = ws.cell(r, 2).value
        # test_no 다양화: PCR- + 연도조각 + 일련 (기존 형식 느슨히 유지)
        tno = ws.cell(r, 3).value
        ws.cell(r, 7).value = rng.pick(labs)  # lab_name (col7)
        if pid in INVALID_PCR_IDS:
            # 무효 유지: valid_until 과거
            ty = rng.rng(2024, 2025)
            test = dt.date(ty, rng.pick([1, 3, 6, 9]), rng.pick([5, 12, 19, 26]))
            valid = test + dt.timedelta(days=rng.pick([3, 7, 14]))
            while valid >= GAME_END:
                test = add_years(test, -1)
                valid = test + dt.timedelta(days=rng.pick([3, 7, 14]))
            ws.cell(r, 4).value = s(test)
            ws.cell(r, 6).value = s(valid)
            report["pcr"].append((pid, cid, s(test), s(valid), "INVALID(유지)"))
            continue
        # 유효: test_date 검역창(day5~7) 직전으로 분산, valid_until >= 2026-06-14
        # 유효기간 3~21일 섞기. test_date 후보: 2026-05-20 ~ 2026-06-05.
        test_pool = [dt.date(2026, 5, d) for d in (20, 22, 24, 26, 28, 30)] + \
                    [dt.date(2026, 6, d) for d in (1, 2, 3, 4, 5)]
        test = rng.pick(test_pool)
        validity_d = rng.pick([10, 14, 14, 21, 21])
        valid = test + dt.timedelta(days=validity_d)
        # valid_until 하한 보장 (>= 2026-06-14)
        while valid < PCR_VALID_MIN:
            validity_d += 7
            valid = test + dt.timedelta(days=validity_d)
        ws.cell(r, 4).value = s(test)
        ws.cell(r, 6).value = s(valid)
        report["pcr"].append((pid, cid, s(test), s(valid), f"{validity_d}d"))

    wb.save(XLSX)
    return report


if __name__ == "__main__":
    rep = main()
    for k, rows in rep.items():
        print(f"===== {k} ({len(rows)}) =====")
        for row in rows:
            print(row)
