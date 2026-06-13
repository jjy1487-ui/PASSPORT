# -*- coding: utf-8 -*-
"""① 조지호 자백 줄 단축 ② 규정 대조 비트 자연스럽게(메타 제거) ③ PCR 비트 양성/검사기관 구분. 엑셀 라이브."""
import os,sys
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw
XLSX=os.path.abspath('data/대사_스크립트.xlsx')

# 직접 치환(텍스트 유니크)
DIRECT={
 '어? PCR요? 그건 안 가져왔는데. 난 멀쩡하다니까, 빨리 좀 해줘요.':'어? PCR요? 그건 안 가져왔는데요.',
 # 규정 대조 비트 — 메타("대조하니/음성기록을 보니") 제거, 자연스러운 심사관 반응
 '음성기록을 보니 "PCR 검사서를 안 가져왔다"고 하셨네요. 규정집상 방역 기간엔 전원 PCR 검사서 제출이 필수입니다.':'방역 기간에는 모든 분이 PCR 검사서를 제출하셔야 합니다. 안 가져오셨군요?',
 '아, 그게 꼭 있어야 돼요? 난 멀쩡하다니까, 빨리 좀 해줘요.':'아, 그게 꼭 있어야 돼요? 난 멀쩡한데.',
 '규정 위반으로 확인됩니다. PCR 미제출은 입국 거부 대상입니다.':'규정상 PCR 검사서 없이는 입국이 불가합니다.',
}
# 블록 서류상태별 PCR 비트 치환
OLD_Q='PCR 검사서를 확인하겠습니다… 결과가 기준을 초과했는데요?'
OLD_C='기준 초과 시에는 입국이 제한됩니다.'
POS_Q='PCR 검사서를 확인합니다… 검사 결과가 양성으로 나오는데요?'
POS_C='양성 판정 시에는 입국이 거부됩니다.'
ORG_Q='PCR 검사서를 확인합니다… 검사 기관 정보가 확인되지 않는데요?'
ORG_C='검사 기관이 확인되지 않으면 입국이 제한됩니다.'

bk=None
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower()=='대사_스크립트.xlsx': bk=b
        except: pass
opened=False
if bk is None:
    app=xw.App(visible=False); app.display_alerts=False; bk=app.books.open(XLSX); opened=True

total=0
for shn in ['Day5','Day6','Day7']:
    ws=bk.sheets[shn]; vals=ws.used_range.value
    def cv(r,c):
        v=vals[r][c] if c<len(vals[r]) else None
        return '' if v is None else str(v).strip()
    cur_state=''
    for i in range(len(vals)):
        # 블록 서류상태 추적
        st=cv(i,4)
        if cv(i,2) and st: cur_state=st
        txt=cv(i,7)
        new=None
        if txt in DIRECT: new=DIRECT[txt]
        elif txt==OLD_Q: new=POS_Q if '양성' in cur_state else (ORG_Q if '검사기관' in cur_state else None)
        elif txt==OLD_C: new=POS_C if '양성' in cur_state else (ORG_C if '검사기관' in cur_state else None)
        if new and new!=txt:
            ws.range((i+1,8)).value=new; total+=1
bk.save()
print(f'엑셀 치환 {total}건')
if opened: bk.app.quit()
