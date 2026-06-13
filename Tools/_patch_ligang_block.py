# -*- coding: utf-8 -*-
import os,sys
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw
XLSX=r"C:\Users\chris\Documents\produc_build_reecture\data\시나리오_스크립트.xlsx"
NAME="리 강"
NEW=[  # (branch, speaker, text)  첫 행에 일차/순서/방문객/유형/상태
 ("인삿말","리 강","您好，请帮我看看。 (안녕하세요, 좀 봐주세요.)"),
 ("","리 강","我是来旅游观光的。 (관광하러 왔어요.)"),
 ("","리 강","这是我的护照和签证。 (여권이랑 비자 여기요.)"),
 ("음성 대조","심사관","음성기록엔 '관광'이라 하셨는데, 비자의 방문 목적은 '장기 체류'네요?"),
 ("","리 강","어… 그건…"),
 ("","심사관","진술과 비자의 방문 목적이 일치하지 않습니다."),
 ("입국 허가 (오판)","심사관","Have a pleasant trip!"),
 ("","리 강","谢谢！ (감사합니다!)"),
 ("입국 거부 (정답)","심사관","진술하신 방문 목적이 비자와 일치하지 않습니다. 입국하실 수 없습니다."),
 ("","리 강","…对不起。 (…죄송합니다.)"),
]
STATE="불량(방문목적 거짓)"
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
vals=ws.used_range.value
def cv(r,c):
    v=r[c] if c<len(r) else None
    return '' if v is None else str(v).strip()
# 리 강 블록 찾기
start=None; day=slot=None
for i,row in enumerate(vals):
    if cv(row,2)==NAME:
        start=i+1; day=cv(row,0); slot=cv(row,1); break
e=start
while e<=len(vals):
    row=vals[e-1] if e-1<len(vals) else None
    if row is None: break
    if e>start and (cv(row,2) or all(cv(row,c)=='' for c in range(8))): break
    e+=1
clen=e-start
m=len(NEW)
ws.api.Rows("{}:{}".format(start,start+clen-1)).Delete()
ws.api.Rows("{}:{}".format(start,start+m-1)).Insert()
FP=(0x70,0x30,0xA0); APP=(0x1F,0x7A,0x1F); REJ=(0xB0,0x24,0x18)
for k,(br,spk,txt) in enumerate(NEW):
    rr=start+k
    head=[day,slot,NAME,"외국인관광객",STATE] if k==0 else ["","","","",""]
    ws.range((rr,1),(rr,8)).value=head+[br,spk,txt]
    if br:
        col=FP if br.startswith("음성") else (APP if br.startswith("입국 허가") else (REJ if br.startswith("입국 거부") else None))
        if col: ws.range((rr,6)).font.color=col; ws.range((rr,6)).font.bold=True
    if k==0: ws.range((rr,3)).font.bold=True
bk.save()
print(f'리 강 블록 교체 완료 (행 {start}, {clen}→{m})')
if opened: bk.app.quit()
