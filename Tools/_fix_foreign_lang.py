# -*- coding: utf-8 -*-
"""외국인 손님 발화 중 기대 언어와 다른 줄을 각 나라 언어 템플릿으로 교체(맥락별). xlwings 단일셀."""
import os,sys,re
sys.stdout.reconfigure(encoding='utf-8')
import openpyxl,xlwings as xw
# 국적 맵
wbv=openpyxl.load_workbook('data/여권_정리_updated.xlsx',data_only=True)
cs=wbv['customer']; rows=list(cs.iter_rows(values_only=True))
hi=next(i for i,r in enumerate(rows) if r and 'customer_id' in [str(x) for x in r])
hdr=[str(x) for x in rows[hi]]; ni=hdr.index('name_kr'); nti=hdr.index('nationality')
natmap={}
for r in rows[hi+1:]:
    if r[ni]:
        m=re.search(r'\(([A-Z]{3})\)',str(r[nti] or '')); natmap[str(r[ni]).strip()]=m.group(1) if m else ''
LANG={'CHN':'중국어','JPN':'일본어','USA':'영어','GBR':'영어'}
def detect(text):
    t=re.sub(r'\([^)]*\)','',text)
    if re.search(r'[぀-ヿ]',t): return '일본어'
    if re.search(r'[一-鿿]',t): return '중국어'
    if re.search(r'[가-힣]',t): return '한국어'
    if re.search(r'[A-Za-z]',t): return '영어'
    return '-'
TPL={
 ('중국어','인삿말'):'你好，麻烦您了。(안녕하세요, 잘 부탁드립니다.)',
 ('중국어','대조'):'啊？应该没问题的…(어? 문제 없을 텐데요…)',
 ('중국어','허가'):'谢谢！祝您愉快！(감사합니다! 좋은 하루 되세요!)',
 ('중국어','거부'):'哦，原来那里有问题。对不起。(아, 거기 문제가 있었군요. 죄송합니다.)',
 ('일본어','인삿말'):'こんにちは。お願いします。(안녕하세요. 잘 부탁드립니다.)',
 ('일본어','대조'):'え？問題ないはずですが…(네? 문제 없을 텐데요…)',
 ('일본어','허가'):'ありがとうございます！(감사합니다!)',
 ('일본어','거부'):'あ、そうなんですか。すみません。(아, 그렇군요. 죄송합니다.)',
 ('영어','인삿말'):'Hello, here you go.(안녕하세요, 여기요.)',
 ('영어','대조'):'What? That should be fine…(네? 문제 없을 텐데요…)',
 ('영어','허가'):'Thank you! Have a great day!(감사합니다! 좋은 하루 되세요!)',
 ('영어','거부'):"Oh, I see. I'm sorry.(아, 그렇군요. 죄송합니다.)",
}
def ctx(branch):
    b=branch or ''
    if b=='인삿말': return '인삿말'
    if '대조' in b or '검사' in b: return '대조'
    if b.startswith('입국 허가'): return '허가'
    if b.startswith('입국 거부'): return '거부'
    return None
bk=None
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower()=='대사_스크립트.xlsx': bk=b
        except: pass
if bk is None: print('파일 안 열림'); sys.exit(1)
try: bk.app.screen_updating=False
except: pass
def cvv(v): return '' if v is None else str(v).strip()
changed=0; greet_changed=[]
for sh in [s.name for s in bk.sheets if s.name.startswith('Day')]:
    ws=bk.sheets[sh]; vals=ws.used_range.value
    curname=None; curctx=None
    for i in range(len(vals)):
        row=vals[i]
        c2=cvv(row[2] if len(row)>2 else None)
        if c2: curname=c2
        br=cvv(row[5] if len(row)>5 else None)
        cx=ctx(br)
        if cx: curctx=cx
        spk=cvv(row[6] if len(row)>6 else None); txt=cvv(row[7] if len(row)>7 else None)
        if not txt or spk=='심사관' or not curname: continue
        nat=natmap.get(curname,'')
        if nat not in ('CHN','JPN','USA','GBR'): continue
        exp=LANG[nat]; det=detect(txt)
        if det in (exp,'-'): continue
        key=(exp,curctx)
        if key in TPL:
            ws.range((i+1,8)).value=TPL[key]; changed+=1
            if curctx=='인삿말': greet_changed.append(f'{sh} {curname}')
bk.save()
try: bk.app.screen_updating=True
except: pass
print(f'교체 {changed}줄')
if greet_changed: print('인삿말 교체된 손님(테마 일반화됨):', greet_changed)
