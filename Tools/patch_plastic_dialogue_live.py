# -*- coding: utf-8 -*-
"""성형 12명 정상/불량 블록을 '지문 검사→대조' 흐름으로 교체 — xlwings 라이브(열린 채) 편집."""
import os, sys, json, glob, re
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw

ROOT=r"C:\Users\chris\Documents\produc_build_reecture"
GD=ROOT+r"\Assets\Resources\GameData"
XLSX=ROOT+r"\data\시나리오_스크립트.xlsx"
NATKR={"KOR":"대한민국","USA":"미국","CHN":"중국","JPN":"일본"}

def pid(docs):
    pp=next((x for x in (docs or []) if x.get('documentType')=='여권'),None)
    if not pp: return {}
    g=lambda k: next((f['value'] for f in pp.get('fields',[]) if f['key']==k),None)
    return {'name':g('name'),'birth':g('birth_date'),'nat':g('nationality')}
def mismatch(var):
    P=pid(var.get('documents')); rec=(var.get('fingerprint') or {}).get('record') or {}
    if (rec.get('dbName') or '').upper()!=(P.get('name') or '').upper(): return ('이름',P.get('name'),rec.get('dbName'))
    if rec.get('dbBirth')!=P.get('birth'): return ('생년월일',P.get('birth'),rec.get('dbBirth'))
    if (rec.get('dbNationality') or '') not in (P.get('nat') or ''): return ('국적',P.get('nat'),rec.get('dbNationality'))
    return ('이름',P.get('name'),rec.get('dbName'))

defect={}; order=[]
for p in sorted(glob.glob(GD+r"\day*.json"), key=lambda x:int(re.search(r'day(\d+)',x).group(1))):
    if "backup" in p: continue
    d=json.load(open(p,encoding='utf-8'))
    for c in d['customers']:
        if c.get('characterType')!='성형 수술 고객': continue
        nm=c.get('nameKr'); order.append(nm)
        if 0<c.get('validChance',0)<1:
            a=c.get('altVariant') or {}; bad=a if '거절' in a.get('correctResult','') else c
            defect[nm]=mismatch(bad)
        elif '거절' in c.get('correctResult',''):
            defect[nm]=('이름', pid(c.get('documents')).get('name'),(c.get('fingerprint') or {}).get('record',{}).get('dbName'))
GI={nm:i for i,nm in enumerate(dict.fromkeys(order))}; PLASTIC=set(GI.keys())

GREET=["안녕하세요. 사진이랑 좀 달라 보이죠? 얼마 전에 성형을 했어요.",
       "안녕하세요. 얼굴이 사진이랑 다르죠? 수술을 좀 했어요.",
       "안녕하세요. 성형해서 인상이 좀 바뀌었어요. 본인 맞아요."]
SCAN="여권 사진과 외모가 다릅니다. 지문 인식을 진행하겠습니다. 손을 올려주세요."
def dval(f,v): return f"{v}({NATKR.get(v,v)})" if f=='국적' else v
def normal_lines(nm,gi):
    return [("인삿말",nm,GREET[gi%3]),("지문 검사","심사관",SCAN),("",nm,"네, 여기요. (손을 올린다)"),
            ("지문 대조","심사관","지문 조회 결과… 신원이 여권과 일치하네요. 본인 확인되었습니다."),
            ("",nm,"거 봐요, 저 맞다니까요. 수술해서 얼굴만 달라진 거예요."),
            ("입국 허가 (정답)","심사관","본인 확인되었습니다. 즐거운 여행 되십시오."),("",nm,"감사합니다!"),
            ("입국 거부 (오판)","심사관","확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다."),("",nm,"본인 맞는데요? 지문도 확인하셨잖아요.")]
def defect_lines(nm,gi):
    f,pv,dv=defect[nm]; PV=dval(f,pv); DV=dval(f,dv)
    if f=='이름': call=f"지문 조회 결과… 신원이 '{DV}'로 나오는데요. 여권 이름 '{PV}'과(와) 일치하지 않습니다."; rej="지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다."
    elif f=='생년월일': call=f"지문 조회 결과… 신원 생년월일이 {DV}인데, 여권은 {PV}로 되어 있네요. 일치하지 않습니다."; rej="지문 신원이 여권 생년월일과 일치하지 않습니다. 입국하실 수 없습니다."
    else: call=f"지문 조회 결과… 신원 국적이 {DV}인데, 여권은 {PV}로 되어 있네요. 일치하지 않습니다."; rej="지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다."
    return [("인삿말",nm,GREET[(gi+1)%3]),("지문 검사","심사관",SCAN),("",nm,"(잠시 망설인다) …네."),
            ("지문 대조","심사관",call),("",nm,"어… 그건 시스템 오류 아닐까요?"),
            ("","심사관","지문은 성형으로도 바뀌지 않습니다. 본인 여권이 맞습니까?"),("",nm,"…(말을 잇지 못한다)"),
            ("입국 허가 (오판)","심사관","즐거운 여행 되십시오."),("",nm,"가, 감사합니다. (서둘러 지나간다)"),
            ("입국 거부 (정답)","심사관",rej),("",nm,"…죄송합니다.")]

def new_rows(day,slot,nm,state):
    gi=GI[nm]
    if '정상' in state: lines=normal_lines(nm,gi); st="정상(본인)"
    else: lines=defect_lines(nm,gi); st=f"불량(지문 {defect[nm][0]} 불일치)"
    out=[]
    for i,(br,spk,txt) in enumerate(lines):
        out.append([day,slot,nm,"성형수술고객",st,br,spk,txt] if i==0 else ["","","","","",br,spk,txt])
    return out

# ── attach to open workbook ──
bk=None
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower()==os.path.basename(XLSX).lower(): bk=b
        except: pass
opened=False
if bk is None:
    app=xw.App(visible=False); app.display_alerts=False; bk=app.books.open(XLSX); opened=True
ws=bk.sheets['대사_스크립트']

vals=ws.used_range.value          # list of rows
def cell(rowlist,c): 
    v=rowlist[c] if c<len(rowlist) else None
    return '' if v is None else str(v).strip()

# ── detect blocks (1-based rows; vals[0]=header row1) ──
blocks=[]; n=len(vals); i=1
while i<n:
    row=vals[i]; nm=cell(row,2)
    if nm in PLASTIC:
        state=cell(row,4); day=cell(row,0); slot=cell(row,1)
        e=i
        while e<n:
            r=vals[e]
            allblank=all(cell(r,c)=='' for c in range(8))
            if e>i and (allblank or cell(r,2)): break
            e+=1
        blocks.append((i+1, e-i, day, slot, nm, state)); i=e
    else: i+=1
print('블록',len(blocks),'개 교체')

RGB={'app':(0x1F,0x7A,0x1F),'rej':(0xB0,0x24,0x18),'fp':(0x70,0x30,0xA0)}
def bcol(lbl):
    if lbl.startswith("입국 허가"): return RGB['app']
    if lbl.startswith("입국 거부"): return RGB['rej']
    if lbl.startswith("지문"): return RGB['fp']
    return None

for start,clen,day,slot,nm,state in sorted(blocks,key=lambda b:-b[0]):
    nr=new_rows(day,slot,nm,state); m=len(nr)
    ws.api.Rows("{}:{}".format(start,start+clen-1)).Delete()
    ws.api.Rows("{}:{}".format(start,start+m-1)).Insert()
    for k,rv in enumerate(nr):
        rr=start+k
        ws.range((rr,1),(rr,8)).value=rv
        lbl=rv[5]; col=bcol(lbl)
        if col and lbl:
            ws.range((rr,6)).font.color=col; ws.range((rr,6)).font.bold=True
        if k==0: ws.range((rr,3)).font.bold=True

bk.save()
print('저장 완료')
if opened: bk.app.quit()
