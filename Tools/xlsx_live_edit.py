# -*- coding: utf-8 -*-
"""엑셀 라이브 편집 헬퍼 (xlwings/COM 기반).

파일이 Excel에 '열려 있어도' 그 인스턴스에 붙어 편집할 수 있다 → 매번 닫고 열 필요 없음.
 - 열려 있으면: 그 워크북에 attach (닫지 않음, 저장은 호출부 선택)
 - 닫혀 있으면: 보이지 않게 열어 편집 후 저장+닫기

사용 예:
    from xlsx_live_edit import get_book
    bk, opened = get_book()           # opened=True면 우리가 연 것(끝나면 close)
    ws = bk.sheets['customer']
    ws.range('I5').value = '장기체류자'
    bk.save()
    if opened: bk.app.quit()
"""
import os
import xlwings as xw

SRC = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   'data', '여권_정리_updated.xlsx')


def get_book(path=SRC):
    """워크북 반환. (book, opened_by_us). 열려 있으면 attach, 아니면 invisible로 open."""
    base = os.path.basename(path).lower()
    for app in xw.apps:
        for bk in app.books:
            try:
                if os.path.basename(bk.fullname).lower() == base:
                    return bk, False  # 이미 열림 → 붙기만(닫지 말 것)
            except Exception:
                pass
    app = xw.App(visible=False)
    app.display_alerts = False
    return app.books.open(path), True


def cell_value(path, sheet, addr):
    bk, opened = get_book(path)
    try:
        return bk.sheets[sheet].range(addr).value
    finally:
        if opened:
            bk.save(); bk.app.quit()
