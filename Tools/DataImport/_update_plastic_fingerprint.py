# -*- coding: utf-8 -*-
"""여권 정리 엑셀 업데이트 (xlwings, 열려있어도 편집):
   ① '성형 의심 고객' → '성형 수술 고객' 개명 (customer/defect_rule/character_payout/character_score)
   ② fingerprint 시트 새 구조(역할/DB신원/범죄기록/정답) + 성형 손님 4명 레코드
"""
import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))  # repo root not needed
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Tools'))
from xlsx_live_edit import get_book

OLD, NEW = "성형 의심 고객", "성형 수술 고객"
log = []
bk, opened = get_book()
try:
    # ── ① 개명 ───────────────────────────────────────────────
    total = 0
    for sh in ["customer", "defect_rule", "character_payout", "character_score"]:
        ws = bk.sheets[sh]
        vals = ws.used_range.value
        if not isinstance(vals, list):
            vals = [[vals]]
        elif vals and not isinstance(vals[0], list):
            vals = [vals]  # 단일 행
        cnt = 0
        for r, rowv in enumerate(vals):
            if not isinstance(rowv, list):
                rowv = [rowv]
            for c, cell in enumerate(rowv):
                if isinstance(cell, str) and OLD in cell:
                    ws.range((r + 1, c + 1)).value = cell.replace(OLD, NEW)
                    cnt += 1
        total += cnt
        log.append(f"  [{sh}] 개명 {cnt}건")
    log.append(f"① 개명 합계 {total}건")

    # ── ② fingerprint 시트 재구성 ─────────────────────────────
    fp = bk.sheets["fingerprint"]
    fp.range("A1:L60").clear_contents()
    fp.range("E:E").number_format = "@"  # DB생년월일 텍스트로(날짜 자동변환 방지)
    rows = [
        ["PK", "FK", None, None, None, None, None, None, None],
        ["int", "int", "varchar(20)", "varchar(40)", "varchar(20)", "varchar(20)", "varchar(60)", "varchar(30)", "varchar(20)"],
        ["fingerprint_id", "customer_id", "role", "db_name", "db_birth", "db_nationality", "criminal_record", "wanted_no", "correct_result"],
        ["지문ID", "고객ID", "역할", "DB이름", "DB생년월일", "DB국적", "범죄기록", "수배번호", "정답"],
        # 성형 손님 4명 (본인=DB가 여권과 같음 / 도용=다름 / 수배자=다름+범죄기록)
        [1, 10, "수배자", "김서린", "1988-04-12", "대한민국(KOR)", "성형 위장 / 지명수배 중", "WA-2023-001192", "정상 거절"],
        [2, 12, "본인",   "정유나", "1996-04-12", "대한민국(KOR)", "없음", "", "정상 승인"],
        [3, 21, "본인",   "서지호", "1997-11-20", "대한민국(KOR)", "없음", "", "정상 승인"],
        [4, 30, "도용",   "박도윤", "1995-02-18", "대한민국(KOR)", "없음", "", "정상 거절"],
    ]
    fp.range("A1").value = rows
    log.append(f"② fingerprint 시트: 손님 4명 (수배자 윤서린10 / 본인 정유나12·서지호21 / 도용 노가은30)")

    bk.save()
    log.append("저장 완료")
finally:
    if opened:
        bk.app.quit()
print("\n".join(log))
