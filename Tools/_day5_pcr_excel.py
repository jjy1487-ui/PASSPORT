# -*- coding: utf-8 -*-
"""5일차 시트: 각 블록 인삿말에 심사관 PCR 요청 라인 삽입 + 조지호 불량(PCR 미제출) 블록 교체. xlwings 라이브."""
import os,sys
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw
XLSX=os.path.abspath('data/대사_스크립트.xlsx')
INTRO='오늘부터 방역 절차가 시행됩니다. 여권과 PCR 검사서를 함께 보여주세요.'
ROT=['방역 기간입니다. PCR 검사서도 함께 보여주세요.','방역 절차에 따라 PCR 검사서를 확인하겠습니다. 제출해 주세요.','PCR 검사서도 함께 보여주시겠어요?']
# 슬롯/인덱스 → 심사관 라인 (게임과 동일)
INSP_BY_NAME={'조지호':INTRO,'장우진':ROT[1],'로버트 존슨':ROT[2],'윤건우':ROT[0],'한지원':ROT[1],'리 나':ROT[2],'임선우':ROT[0]}
# 조지호 불량(PCR 미제출) 교체 블록: (분기, 화자, 대사)
JI_BAD=[
 ('인삿말','조지호','요즘 전염병이 유행이라며? 난리도 아니네. 빨리 좀 해줘요.'),
 ('','심사관',INTRO),
 ('','조지호','어? PCR요? 그건 안 가져왔는데. 난 멀쩡하다니까, 빨리 좀 해줘요.'),
 ('입국 허가 (오판)','심사관','즐거운 여행 되십시오.'),
 ('','조지호','됐어요.'),
 ('입국 거부 (정답)','심사관','방역 규정상 PCR 검사서가 필수입니다. 제출하지 않으셔서 입국하실 수 없습니다.'),
 ('','조지호','아니 그걸 꼭 가져와야 돼요? 난 멀쩡하다니까!'),
]

bk=None
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower()=='대사_스크립트.xlsx': bk=b
        except: pass
opened=False
if bk is None:
    app=xw.App(visible=False); app.display_alerts=False; bk=app.books.open(XLSX); opened=True
ws=bk.sheets['Day5']
vals=ws.used_range.value
def cv(r,c):
    v=r[c] if c<len(r) else None
    return '' if v is None else str(v).strip()
# 블록 탐색
blocks=[]; i=0; n=len(vals)
while i<n:
    nm=cv(vals[i],2)
    if nm and cv(vals[i],5)=='인삿말':
        day=cv(vals[i],0); slot=cv(vals[i],1); state=cv(vals[i],4); typ=cv(vals[i],3)
        e=i+1
        while e<n and not (cv(vals[e],2) and cv(vals[e],5)=='인삿말') and not all(cv(vals[e],c)=='' for c in range(8)): e+=1
        blocks.append((i+1,e-i,day,slot,nm,typ,state)); i=e
    else: i+=1

FP=(0x70,0x30,0xA0); APP=(0x1F,0x7A,0x1F); REJ=(0xB0,0x24,0x18)
def setfont(r,c,col): ws.range((r,c)).font.color=col; ws.range((r,c)).font.bold=True
for start,clen,day,slot,nm,typ,state in sorted(blocks,key=lambda b:-b[0]):
    if nm=='조지호' and state.startswith('불량'):
        ws.api.Rows('{}:{}'.format(start,start+clen-1)).Delete()
        ws.api.Rows('{}:{}'.format(start,start+len(JI_BAD)-1)).Insert()
        for k,(br,spk,txt) in enumerate(JI_BAD):
            rr=start+k
            head=[day,slot,nm,typ,'불량(PCR 미제출)'] if k==0 else ['','','','','']
            ws.range((rr,1),(rr,8)).value=head+[br,spk,txt]
            if br.startswith('입국 허가'): setfont(rr,6,APP)
            elif br.startswith('입국 거부'): setfont(rr,6,REJ)
            if k==0: ws.range((rr,3)).font.bold=True
    else:
        line=INSP_BY_NAME.get(nm)
        if not line: continue
        ws.api.Rows('{}:{}'.format(start+1,start+1)).Insert()  # 인삿말 첫줄 바로 뒤
        ws.range((start+1,1),(start+1,8)).value=['','','','','','','심사관',line]
bk.save()
print('5일차 엑셀: 심사관 PCR 요청 삽입 + 조지호 불량 교체 완료')
if opened: bk.app.quit()
