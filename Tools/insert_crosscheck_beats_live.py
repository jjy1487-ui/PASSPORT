# -*- coding: utf-8 -*-
"""비-성형 '불량(거절 정답)' 손님 블록에 '대조 티키타카' 비트를 인삿말 뒤·입국판정 앞에 삽입.
기존 인삿말/판정 대사는 보존. xlwings 라이브 편집(열린 채). 아래→위 처리."""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw

XLSX=r"C:\Users\chris\Documents\produc_build_reecture\data\시나리오_스크립트.xlsx"
SKIP_TYPES={"성형","성형수술고객"}   # 일반 성형 고객(이미 처리). 윤서린(성형범죄자)은 포함.

INSP="심사관"
# (branch_label, [(speaker_is_name?, text), ...]) — speaker_is_name=False→심사관
def beat(name, typ, state, k):
    def C(*pairs): return pairs
    if "여권 기간" in state:
        callout=["여권 유효기간을 확인하겠습니다… 만료일이 지난 것 같은데요?",
                 "여권 만료일을 보겠습니다… 기간이 지나 있네요?"][k%2]
        react=["네? 그럴 리가요. 다시 확인해 주세요.","아직 안 지났을 텐데요? 빨리 좀 해줘요.",
               "에이, 그런 걸 일일이 봐요? 바쁜데."][k%3]
        return ("서류 대조",[(False,callout),(True,react),(False,"유효기간이 지난 여권으로는 입국이 불가합니다.")])
    if "여권 사진" in state:
        react=["어… 제 사진 맞는데요.","사진이 좀 오래돼서 그래요.","요즘 살이 빠져서 그런가 봐요."][k%3]
        return ("서류 대조",[(False,"여권 사진과 얼굴을 대조하겠습니다… 사진과 달라 보이는데요?"),
                          (True,react),(False,"사진과 본인이 일치해야 입국이 가능합니다.")])
    if "여권 정보" in state:
        react=["그럴 리가 없는데요…","어? 제 생년월일이 왜…"][k%2]
        return ("서류 대조",[(False,"여권 정보를 대조하겠습니다… 생년월일이 기록과 다릅니다."),
                          (True,react),(False,"정보가 일치하지 않으면 입국이 어렵습니다.")])
    if "PCR" in state:
        react=["전 멀쩡해요. 증상도 없는데요.","검사받은 지 얼마 안 됐는데요?","I feel completely fine, though."][k%3]
        return ("서류 대조",[(False,"PCR 검사서를 확인하겠습니다… 결과가 기준을 초과했는데요?"),
                          (True,react),(False,"기준 초과 시에는 입국이 제한됩니다.")])
    if "비자" in state:
        react=["다시 확인해 주시면 안 될까요?","회사에서 받은 서류 그대로인데요."][k%2]
        return ("서류 대조",[(False,"비자 정보를 대조하겠습니다… 서류 내용과 맞지 않네요?"),
                          (True,react),(False,"비자 정보가 일치하지 않으면 입국이 어렵습니다.")])
    if "테러범" in typ:
        return ("경보 대조",[(False,"보안 경보 대상과 신원을 대조하겠습니다… 위험 인물 명단과 일치합니다."),
                          (True,"…(말없이 주위를 살핀다)"),(False,"추가 확인이 필요합니다.")])
    if "밀수" in typ:
        return ("X-ray 검사",[(False,"수하물 X-ray 검사를 진행하겠습니다… 밀수 의심 물품이 확인됩니다."),
                            (True,"그건… 오해입니다."),(False,"경보 대상과 신원도 일치합니다.")])
    if "마약" in typ:
        return ("X-ray 검사",[(False,"수하물 X-ray 검사를 진행하겠습니다… 마약류로 의심되는 물품이 확인됩니다."),
                            (True,"…(아무 말이 없다)"),(False,"경보 대상과 신원이 일치합니다.")])
    if "성형범죄자" in typ:  # 윤서린
        return ("지문 검사",[(False,"여권 사진과 외모가 다릅니다. 지문 인식을 진행하겠습니다."),
                          (True,"…(선글라스를 고쳐 쓴다)"),(False,"지문 조회 결과… 지명수배자 '김서린'과 일치합니다.")])
    return None  # 정상 등 → 비트 없음

# attach
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
try: bk.app.screen_updating=False
except: pass

vals=ws.used_range.value
def cv(r,c):
    v=r[c] if c<len(r) else None
    return '' if v is None else str(v).strip()

# 블록 탐색
blocks=[]; i=1; n=len(vals)
while i<n:
    if cv(vals[i],2):
        typ=cv(vals[i],3); state=cv(vals[i],4); name=cv(vals[i],2)
        e=i+1
        while e<n and not cv(vals[e],2) and not all(cv(vals[e],c)=='' for c in range(8)): e+=1
        # 입국 분기 첫 행(블록 내 상대)
        ins=None
        for j in range(i,e):
            if cv(vals[j],5).startswith("입국"): ins=j; break
        blocks.append((i+1,e-i,ins+1 if ins is not None else None,name,typ,state))
        i=e
    else: i+=1

# 카테고리별 카운터(변형 회전)
from collections import defaultdict
kc=defaultdict(int)
def catkey(typ,state):
    for key in ("여권 기간","여권 사진","여권 정보","PCR","비자"):
        if key in state: return key
    return typ
FP=(0x70,0x30,0xA0)
todo=[]
for start,clen,insrow,name,typ,state in blocks:
    if typ in SKIP_TYPES: continue
    if insrow is None: continue
    ck=catkey(typ,state); b=beat(name,typ,state,kc[ck])
    if not b: continue
    kc[ck]+=1
    todo.append((insrow,name,b))

print(f'대조 비트 삽입 대상: {len(todo)} 블록')
# 아래→위
for insrow,name,(label,lines) in sorted(todo,key=lambda x:-x[0]):
    m=len(lines)
    ws.api.Rows("{}:{}".format(insrow,insrow+m-1)).Insert()
    for k,(isname,txt) in enumerate(lines):
        rr=insrow+k; spk=name if isname else INSP
        br=label if k==0 else ""
        ws.range((rr,1),(rr,8)).value=["","","","","",br,spk,txt]
        if k==0:
            ws.range((rr,6)).font.color=FP; ws.range((rr,6)).font.bold=True

try: bk.app.screen_updating=True
except: pass
bk.save()
print("저장 완료")
if opened: bk.app.quit()
