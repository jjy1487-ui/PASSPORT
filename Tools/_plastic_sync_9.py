# -*- coding: utf-8 -*-
"""성형 고객 9명 대사를 성형고객_대사_정상불량.xlsx 스크립트에 맞춰 동기화.
매핑(노가은과 동일): 인삿말→입장, 지문검사→CCL(face), 지문대조→지문DB record+CCL(field), 허가/거부→일반심사."""
import json, os, shutil, openpyxl

GD='Assets/Resources/GameData'
SCRIPT='data/성형고객_대사_정상불량.xlsx'

CFG={
 '윤건우': dict(day='day5', cid=48, field='birth_date', wrong='1993-04-12', legit='baked'),
 '서지호': dict(day='day7', cid=21, field='nationality', wrong='USA', legit='baked'),
 '전도현': dict(day='day8', cid=64, field='name', wrong='CHOI JUWON', legit='baked'),
 '이지우': dict(day='day9', cid=68, field='name', wrong='LIM SEOAH', legit='baked'),
 '정다은': dict(day='day10', cid=72, field='birth_date', wrong='1994-11-20', legit='baked'),
 '장소율': dict(day='day11', cid=79, field='nationality', wrong='CHN', legit='alt'),  # baked=defect
 '서하린': dict(day='day12', cid=84, field='name', wrong='NA EUNBI', legit='baked'),
 '배은서': dict(day='day13', cid=91, field='name', wrong='KIM TAEYANG', legit='baked'),
 '양수빈': dict(day='day14', cid=96, field='birth_date', wrong='1996-05-05', legit='baked'),
}
HANGUL={'name':'이름','birth_date':'생년월일','nationality':'국적'}

# ── 스크립트 파싱: linemap[(name, 'legit'|'defect')] = {branchkey:[(speaker,text)]} ──
def branch_key(b):
    if b is None: return None
    b=str(b)
    if '인삿말' in b: return 'greet'
    if b.startswith('지문 검사'): return 'fpscan'
    if b.startswith('지문 대조'): return 'fpmatch'
    if b.startswith('입국 허가'): return 'approve'
    if b.startswith('입국 거부'): return 'reject'
    return None

wb=openpyxl.load_workbook(SCRIPT, read_only=True, data_only=True)
ws=wb[wb.sheetnames[0]]
linemap={}
cur_name=None; cur_state=None; cur_branch=None
for r in ws.iter_rows(values_only=True):
    name=r[2]; state=r[4]; branch=r[5]; sp=r[6]; tx=r[7]
    if name: cur_name=str(name).strip()
    if state:
        s=str(state)
        cur_state='legit' if s.startswith('정상') else ('defect' if s.startswith('불량') else None)
    bk=branch_key(branch)
    if bk: cur_branch=bk
    if sp and tx and cur_name and cur_state and cur_branch:
        linemap.setdefault((cur_name,cur_state),{}).setdefault(cur_branch,[]).append((str(sp).strip(),str(tx)))

def L(o,sp,tx,claim=None):
    e={"order":o,"speaker":sp,"text":tx}
    if claim: e["claim"]=claim
    return e

def set_entry(cases, lines):
    for c in cases:
        if c.get('caseType')=='입장': c['lines']=lines; return
    cases.insert(0,{"caseType":"입장","gameResult":"-","rejectCount":0,"lines":lines})

def set_case(cases, gr, lines):
    for c in cases:
        if c.get('gameResult')==gr and c.get('caseType')=='일반 심사': c['lines']=lines; return
    cases.append({"caseType":"일반 심사","gameResult":gr,"rejectCount":0,"lines":lines})

def verdict_lines(branchlines):
    # 심사관/손님 2줄 그대로
    return [L(i+1, sp, tx) for i,(sp,tx) in enumerate(branchlines)]

def build_apply(node, name, pname, pbirth, pnat, field, wrong, is_legit):
    lm=linemap.get((name,'legit' if is_legit else 'defect'),{})
    cases=node['dialogueCases']
    # 입장 = 인삿말 (손님 대사들)
    greet=lm.get('greet',[])
    if greet:
        set_entry(cases,[L(i+1,sp,tx) for i,(sp,tx) in enumerate(greet)])
    # CCL
    ccl=[]
    fpscan=lm.get('fpscan',[])
    insp=' '.join(t for s,t in fpscan if s=='심사관')
    cust=' '.join(t for s,t in fpscan if s!='심사관')
    if insp or cust:
        ccl.append({"attr":"face","inspector":insp,"customer":cust,"unlocksScan":""})
    if not is_legit:
        fpm=lm.get('fpmatch',[])
        insp2=' '.join(t for s,t in fpm if s=='심사관')
        cust2=' '.join(t for s,t in fpm if s!='심사관')
        ccl.append({"attr":field,"inspector":insp2,"customer":cust2,"unlocksScan":""})
    node['crossCheckLines']=ccl
    # fingerprint
    claim_name={"attr":"name","value":pname,"label":"지문 대조 신원","unlocksScan":""}
    if is_legit:
        node['fingerprint']={"type":"fingerprint","result":"일치","detail":"본인 일치","extra":pname,
            "claim":claim_name,
            "record":{"mode":"성형","dbName":pname,"dbBirth":pbirth,"dbNationality":pnat,"criminalRecord":"없음","wantedNo":""}}
        # verdicts: approve=정답(정상승인), reject=오판(잘못거절)
        if lm.get('approve'): set_case(cases,"정상 승인",verdict_lines(lm['approve']))
        if lm.get('reject'): set_case(cases,"잘못 거절",verdict_lines(lm['reject']))
    else:
        rec={"mode":"성형","dbName":pname,"dbBirth":pbirth,"dbNationality":pnat,"criminalRecord":"없음","wantedNo":""}
        if field=='name': rec['dbName']=wrong
        elif field=='birth_date': rec['dbBirth']=wrong
        elif field=='nationality': rec['dbNationality']=wrong
        node['fingerprint']={"type":"fingerprint","result":"불일치","detail":f"{HANGUL[field]} 불일치","extra":wrong,
            "claim":{"attr":field,"value":wrong,"label":"지문 대조 신원","unlocksScan":""},"record":rec}
        # verdicts: approve=오판(잘못허가), reject=정답(정상거절, claim 부여)
        if lm.get('reject'):
            rl=verdict_lines(lm['reject'])
            if rl: rl[0]['claim']={"attr":field,"value":wrong,"label":"지문 대조 신원","unlocksScan":""}
            set_case(cases,"정상 거절",rl)
        if lm.get('approve'): set_case(cases,"잘못 허가",verdict_lines(lm['approve']))

report=[]
for name,cfg in CFG.items():
    p=os.path.join(GD,cfg['day']+'.json')
    if not os.path.exists(p+'.bak_plastic9'): shutil.copy(p,p+'.bak_plastic9')
    d=json.load(open(p,encoding='utf-8'))
    for c in d['customers']:
        if c.get('customerId')!=cfg['cid']: continue
        pf={x['key']:x['value'] for x in c['documents'][0]['fields']}
        pname,pbirth,pnat=pf.get('name'),pf.get('birth_date'),pf.get('nationality')
        a=c['altVariant']
        legit_baked = (cfg['legit']=='baked')
        build_apply(c, name, pname,pbirth,pnat, cfg['field'], cfg['wrong'], is_legit=legit_baked)
        build_apply(a, name, pname,pbirth,pnat, cfg['field'], cfg['wrong'], is_legit=not legit_baked)
        report.append(f"{cfg['day']} {name}(cid{cfg['cid']}) field={cfg['field']} wrong={cfg['wrong']} baked={'정상' if legit_baked else '불량'}")
    json.dump(d,open(p,'w',encoding='utf-8'),ensure_ascii=False,indent=1)

print('\n'.join(report))
print('DONE 9 plastic synced (백업 *.bak_plastic9)')
