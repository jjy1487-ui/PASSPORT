# -*- coding: utf-8 -*-
"""rule_book(12개 규정 교체 + id15 편입 + id12 삭제) / news id13(사토 이름 제거) /
passport(사토 cid11 여권번호 = JP9911287) 엑셀 라이브 편집 (xlwings/COM, 열려 있어도 편집).

전체 리빌드 금지 작업의 '엑셀 먼저' 단계. dayN.json 타깃 패치는 별도 스크립트가 수행한다.
idempotent: 같은 입력 → 같은 결과(재실행 안전).
"""
import os
import sys
import io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "Tools"))
from xlsx_live_edit import get_book  # noqa: E402

# ── rule_book 새 content (rule_id -> content) ──────────────────────────────
RULE_CONTENT = {
    1: ("모든 입국자는 유효한 서류를 갖추고 심사관의 허가를 받아야 입국할 수 있다. "
        "제출 서류·본인·진술의 정보가 모두 일치할 때만 허가하며, 어느 하나라도 어긋나면 입국을 거부한다."),
    2: ("여권의 이름·생년월일·성별·사진은 입국자 본인과 일치해야 한다. "
        "어느 한 항목이라도 본인과 다르면 입국을 거부한다."),
    3: ("여권은 여권번호·영문 이름·성별·생년월일·국적·발급일·만료일을 모두 갖추어야 한다. "
        "여권번호 앞 두 자리는 발급 국가 코드와 일치해야 한다(대한민국 KO·미국 US·중국 CN·일본 JP). "
        "앞자리가 국적과 다르면 위조 여권이다. "
        "정보가 본인이나 다른 서류와 다르거나 만료일이 오늘 이전이면 입국을 거부한다."),
    8: ("관광·취업·장기 체류 목적의 외국인은 비자를 함께 제출해야 한다. "
        "비자의 비자번호·비자종류·국적·발급일·만료일은 여권과 일치해야 하며, "
        "만료되었거나 여권과 어긋나는 비자는 입국을 거부한다."),
    15: ("성형 등으로 얼굴이 여권 사진과 달라 보이는 경우, 본인 여부는 지문 신원으로 가린다. "
         "지문 신원(이름·생년월일·국적)이 여권과 다르면 신분 도용으로 보아 입국을 거부한다."),
    11: ("외국인의 실제 방문 목적은 비자에 기재된 방문 목적과 일치해야 한다. "
         "진술한 목적이 비자와 다르면(거짓 진술) 입국을 거부한다."),
    7: ("검역 대상 입국자는 PCR 검사서를 제출해야 한다. "
        "검사 결과가 양성이거나, 유효 기간이 지났거나, 검사서의 이름·국적이 여권과 다르면 입국을 거부한다."),
    14: ("PCR 검사서는 공인 기관(국립검역소·인천공항검역소·질병관리청진단검사센터)이 발급한 것만 유효하다. "
         "명단에 없는 사설·무허가 기관의 검사서는 무효이며 입국을 거부한다."),
    9: ("취업 목적 입국자는 재직증명서를 제출해야 한다. "
        "증빙번호·고용 회사·직종·입사일·발급일은 다른 서류와 일치해야 하며, "
        "항목이 누락되거나 위조 흔적이 있으면 입국을 거부한다."),
    13: ("고용 회사가 실재하지 않는 유령회사(사업자 미등록·폐업)이거나 입국 금지 명단에 오른 회사이면, "
         "서류가 정상으로 보여도 입국을 거부한다."),
    4: ("무기·마약·폭발물·밀수품 등은 반입할 수 없다. "
        "신원이 의심되거나 위험물 반입 경보가 있는 입국자는 X-ray 정밀 검사로 확인하며, "
        "금지 물품이 적발되면 입국을 거부한다."),
    6: ("테러·폭발 등 특별 보안 경보가 발령된 기간에는 심사를 강화한다. "
        "서류나 신원에 의심이 있는 입국자는 X-ray 정밀 검사와 보안 명단 대조를 시행하며, "
        "위험물이 적발되거나 수배자로 확인되면 즉시 입국을 거부한다."),
}

# id15 신규 편입 행(현행 rule_book에 없음). day=3, end_day 없음.
RULE15 = {"rule_id": 15, "day": 3, "rule_title": "성형·지문 확인",
          "rule_content": RULE_CONTENT[15], "related_field": None, "end_day": None}

DELETE_RULE_ID = 12  # X-ray 정밀 검사 행 삭제(금지물품 id4에 통합)

# news id13 단서: value="사토 하루키" 이름 지목 제거 → 일반 경보(미지목).
NEWS_ID13_NEW_VALUE = None  # G열 비움
NEWS_ID13_NEW_ATTR = "X-RAY, 비자 종류 확인"  # 단서 속성은 유지(일반 경보)

# 사토 하루키 cid11 여권번호 정합값(앞자리 JP → 여권 규정 무위반, 비자(JP1012287)와 불일치).
SATO_CID = 11
SATO_PASSPORT_NO = "JP9911287"


def col_index(ws, en_name):
    """row3(영문 컬럼명)에서 en_name 컬럼의 1-indexed 위치를 찾는다."""
    for c in range(1, ws.used_range.last_cell.column + 1):
        if (ws.range((3, c)).value or "") == en_name:
            return c
    raise KeyError(f"column {en_name!r} not found in row3")


def edit_rule_book(bk):
    ws = bk.sheets["rule_book"]
    last = ws.used_range.last_cell.row
    # 헤더 컬럼 위치
    c_id = col_index(ws, "rule_id")
    c_day = col_index(ws, "day")
    c_title = col_index(ws, "rule_title")
    c_content = col_index(ws, "rule_content")
    c_endday = col_index(ws, "end_day")

    # 1) id12 삭제(행 전체 delete)
    del_row = None
    for r in range(5, last + 1):
        if ws.range((r, c_id)).value == DELETE_RULE_ID:
            del_row = r
            break
    if del_row is not None:
        ws.range(f"{del_row}:{del_row}").api.Delete()
        print(f"[rule_book] deleted id={DELETE_RULE_ID} (excel row {del_row})")
        last -= 1
    else:
        print(f"[rule_book] id={DELETE_RULE_ID} already absent (skip delete)")

    # 2) 12개 규정 content 교체
    changed = 0
    present_ids = set()
    for r in range(5, last + 1):
        rid = ws.range((r, c_id)).value
        if rid is None:
            continue
        rid = int(rid)
        present_ids.add(rid)
        if rid in RULE_CONTENT:
            cur = ws.range((r, c_content)).value or ""
            if cur != RULE_CONTENT[rid]:
                ws.range((r, c_content)).value = RULE_CONTENT[rid]
                changed += 1
    print(f"[rule_book] content replaced for {changed} rule(s)")

    # 3) id15 편입(없으면 id8 행 다음에 추가)
    if 15 not in present_ids:
        # id8(입국 비자, day3) 행 다음에 삽입
        anchor = None
        for r in range(5, last + 1):
            if ws.range((r, c_id)).value == 8:
                anchor = r
                break
        insert_at = (anchor + 1) if anchor else (last + 1)
        ws.range(f"{insert_at}:{insert_at}").api.Insert()
        ws.range((insert_at, c_id)).value = 15
        ws.range((insert_at, c_day)).value = 3
        ws.range((insert_at, c_title)).value = RULE15["rule_title"]
        ws.range((insert_at, c_content)).value = RULE15["rule_content"]
        ws.range((insert_at, c_endday)).value = None
        print(f"[rule_book] inserted id=15 성형·지문 확인 (day3) at excel row {insert_at}")
    else:
        print("[rule_book] id=15 already present (skip insert)")


def edit_news(bk):
    ws = bk.sheets["news"]
    last = ws.used_range.last_cell.row
    c_id = col_index(ws, "news_id")
    # attribute 컬럼은 시트 오타 'attrirube'. value 컬럼은 그 다음.
    c_attr = None
    c_val = None
    for c in range(1, ws.used_range.last_cell.column + 1):
        name = ws.range((3, c)).value or ""
        if name in ("attrirube", "attribute"):
            c_attr = c
            c_val = c + 1
    for r in range(5, last + 1):
        if ws.range((r, c_id)).value == 13:
            before = ws.range((r, c_val)).value
            if before not in (None, ""):
                ws.range((r, c_val)).value = NEWS_ID13_NEW_VALUE
                print(f"[news] id13 value cleared (was {before!r}) — 이름 지목 제거")
            else:
                print("[news] id13 value already empty (skip)")
            break


def edit_passport(bk):
    ws = bk.sheets["passport"]
    last = ws.used_range.last_cell.row
    c_cust = col_index(ws, "customer_id")
    c_pno = col_index(ws, "passport_no")
    for r in range(5, last + 1):
        if ws.range((r, c_cust)).value == SATO_CID:
            before = ws.range((r, c_pno)).value
            if before != SATO_PASSPORT_NO:
                ws.range((r, c_pno)).value = SATO_PASSPORT_NO
                print(f"[passport] cid{SATO_CID} passport_no {before!r} -> {SATO_PASSPORT_NO!r}")
            else:
                print(f"[passport] cid{SATO_CID} passport_no already {SATO_PASSPORT_NO!r} (skip)")
            break


def main():
    bk, opened = get_book()
    try:
        edit_rule_book(bk)
        edit_news(bk)
        edit_passport(bk)
        bk.save()
        print("[saved] data/여권_정리_updated.xlsx")
    finally:
        if opened:
            bk.app.quit()


if __name__ == "__main__":
    main()
