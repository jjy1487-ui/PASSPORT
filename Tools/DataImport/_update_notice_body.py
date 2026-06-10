# -*- coding: utf-8 -*-
"""inspection_notice 시트 body(F열)를 '오판(잘못 통과시킴)' 말투로 갱신.
   거절 시점 문구("…입국이 거부되었습니다") → 플레이어 오판 문구("…였으나, 입국을 허가하였습니다").
   {field}/{document} 치환자는 유지.
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # Tools 디렉터리
from xlsx_live_edit import get_book

NEW = {
    ("정보불일치", "여권"): "여권 인적사항({field})이 본인 진술·등록 정보와 일치하지 않는 손님이었으나, 입국을 허가하였습니다.",
    ("사진불일치", "여권"): "여권 사진이 본인의 용모와 상이하여 신원 확인이 불가한 손님이었으나, 입국을 허가하였습니다.",
    ("기간만료", "여권"): "{document}의 유효기간이 만료되어 효력이 없는 서류였으나, 입국을 허가하였습니다.",
    ("위조", "여권"): "{document}에서 위·변조 흔적이 확인된 손님이었으나, 입국을 허가하였습니다.",
    ("서류누락", "여권"): "입국에 필요한 필수 서류({document})가 누락된 손님이었으나, 입국을 허가하였습니다.",
    ("정보불일치", "비자"): "체류 목적에 부합하지 않거나 정보가 일치하지 않는 비자를 제출한 손님이었으나, 입국을 허가하였습니다.",
    ("검사부적합", "PCR검사서"): "검역 서류의 검사 결과 또는 유효기한이 입국 기준에 부합하지 않는 손님이었으나, 입국을 허가하였습니다.",
    ("위험물반입", "여권"): "X-ray 검사 결과 반입 금지 물품({field})이 적발된 손님이었으나, 입국을 허가하였습니다.",
    ("신원위조", "여권"): "지문 대조 결과 신원 정보가 위조된 것으로 확인된 손님이었으나, 입국을 허가하였습니다.",
}

bk, opened = get_book()
try:
    ws = bk.sheets["inspection_notice"]
    nrows = ws.used_range.last_cell.row
    changed = 0
    for r in range(1, nrows + 1):
        et = ws.range((r, 2)).value  # B: error_type
        dt = ws.range((r, 3)).value  # C: document_type
        key = (et, dt)
        if key in NEW:
            ws.range((r, 6)).value = NEW[key]  # F: body
            print("행%d: %s/%s body 갱신" % (r, et, dt))
            changed += 1
    bk.save()
    print("총 %d행 갱신, 저장 완료" % changed)
finally:
    if opened:
        bk.app.quit()
