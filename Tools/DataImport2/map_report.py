# -*- coding: utf-8 -*-
"""신규 _3 스케줄(유형·통과/거절)과 마스터 day_schedule(구체 customer_id)를 슬롯 단위로 정합.
표기해석(통과=정상고정/거절=불량고정/랜덤◆=valid_chance/★=특수) 기준으로 valid_chance 일치 여부 검증."""
import re, openpyxl

SCHED = r"C:\Users\chris\Downloads\dayeon_data\여권주세요_날짜별_방문고객_랜덤정리_3.xlsx"
MASTER= r"C:\Users\chris\Downloads\여권_정리_updated.xlsx"

# _3 스케줄 유형 약칭 -> 마스터 character_type
TYPE_ALIAS = {
    "일반": "일반 고객", "진상": "진상 고객", "외관": "외국인 관광객",
    "성형": "성형 의심 고객", "전O": "검역 대상자(PCR)", "전X": "검역 대상자(PCR)",
    "연예": "특수(연예인)★", "정치": "특수(정치인)★", "현자": "특수(현자)★",
    "장기": "장기체류자", "취업": "취업체류자",
    "성형범죄자": "범죄자(성형수술)", "범죄자": "범죄자(외국 도피자)", "테러범": "테러범",
}

def parse_token(tok):
    """'일반·통과★' -> (type='일반 고객', disp='통과', special=True)"""
    tok = tok.strip()
    special = "★" in tok
    rand = "◆" in tok
    t = tok.replace("★","").replace("◆","").strip()
    if "·" in t:
        kind, disp = t.split("·",1)
    else:
        kind, disp = t, ""
    return TYPE_ALIAS.get(kind.strip(), "?"+kind), disp.strip(), special, rand

def load_master():
    wb=openpyxl.load_workbook(MASTER,data_only=True,read_only=True)
    cust={r[0]:r[8] for r in wb['customer'].iter_rows(min_row=5,values_only=True) if r[0] is not None}
    sched={}
    for r in wb['day_schedule'].iter_rows(min_row=5,values_only=True):
        if r[1] is None: continue
        sched[(r[1],r[2])]=(r[3],r[4])  # (day,slot)->(custid,valid)
    wb.close()
    return cust,sched

def main():
    cust,msched=load_master()
    wb=openpyxl.load_workbook(SCHED,data_only=True,read_only=True)
    ws=wb['슬롯별배치(1회차)']
    rows=list(ws.iter_rows(values_only=True))[1:]
    mismatches=0; ok=0
    print("day slot | NEW(_3 유형·표기)          | MASTER cust(type, vc)         | 정합")
    print("-"*95)
    for row in rows:
        if row[0] is None: continue
        day=int(row[0])
        for slot in range(1,8):
            tok=row[1+slot]
            if tok is None: continue
            ntype,disp,special,rand=parse_token(str(tok))
            cid,vc=msched.get((day,slot),(None,None))
            mtype=cust.get(cid,"?")
            # 기대 vc: 통과->1, 거절->0, 랜덤->0<vc<1, 그외 special
            type_ok = (ntype==mtype) or (ntype=="검역 대상자(PCR)" and mtype=="검역 대상자(PCR)")
            if rand:
                vc_ok = (vc is not None and 0<vc<1)
            elif disp=="통과":
                vc_ok = (vc==1)
            elif disp=="거절":
                vc_ok = (vc==0)
            else:
                vc_ok = True
            mark = "OK" if (type_ok and vc_ok) else ("TYPE!" if not type_ok else "VC!")
            if mark=="OK": ok+=1
            else: mismatches+=1
            print(f"{day:>3} {slot:>4} | {str(tok):<26} | c{cid} {mtype}(vc={vc})    | {mark}")
    wb.close()
    print("-"*95)
    print(f"OK={ok}  MISMATCH={mismatches}  total={ok+mismatches}")

if __name__=="__main__":
    main()
