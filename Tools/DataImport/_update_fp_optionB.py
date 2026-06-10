# -*- coding: utf-8 -*-
"""fingerprint 시트를 옵션 B(등장마다 본인/도용 랜덤) 구조로 재구성.
   mode=성형 → valid_chance로 본인/도용 롤 (도용일 때 위장신원 표시)
   mode=수배자 → 항상 수배 (위장신원 = 수배자 진짜신원)
   본인일 때 DB는 여권 신원 그대로(여기 안 적음). 위장신원만 정의.
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # repo/Tools
from xlsx_live_edit import get_book

bk, opened = get_book()
try:
    fp = bk.sheets["fingerprint"]
    fp.range("A1:L60").clear_contents()
    fp.range("E:E").number_format = "@"  # 위장생년월일 텍스트(날짜 자동변환 방지)
    rows = [
        ["PK", "FK", None, None, None, None, None, None],
        ["int", "int", "varchar(20)", "varchar(40)", "varchar(20)", "varchar(20)", "varchar(60)", "varchar(30)"],
        ["fingerprint_id", "customer_id", "mode", "alt_name", "alt_birth", "alt_nationality", "criminal_record", "wanted_no"],
        ["지문ID", "고객ID", "모드", "위장이름", "위장생년월일", "위장국적", "범죄기록", "수배번호"],
        [1, 12, "성형", "박도윤", "1995-02-18", "대한민국(KOR)", "없음", ""],
        [2, 21, "성형", "강민호", "1994-03-09", "대한민국(KOR)", "없음", ""],
        [3, 30, "성형", "윤하늘", "1996-12-01", "대한민국(KOR)", "없음", ""],
        [4, 10, "수배자", "김서린", "1988-04-12", "대한민국(KOR)", "성형 위장 / 지명수배 중", "WA-2023-001192"],
    ]
    fp.range("A1").value = rows
    # 팀원용 설명 (J1)
    fp.range("J1").value = ("[모드] 성형=등장마다 본인/도용 랜덤(valid_chance=본인확률) · 수배자=항상 수배. "
                            "[위장이름/생일] 도용·수배일 때 DB에 뜨는 가짜 신원. 본인일 땐 여권 신원 그대로 표시.")
    bk.save()
    print("fingerprint 시트 옵션B 구조로 갱신 완료 (성형3 + 수배자1)")
finally:
    if opened:
        bk.app.quit()
