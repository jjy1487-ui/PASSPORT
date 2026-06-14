# -*- coding: utf-8 -*-
"""성형 9명을 대사_스크립트(DayN, 구어체)로 재동기화. 결함은 '실제 지문대조 대사' 기준.
- 입장/지문검사/허가/거부 손님반응 = 스크립트 그대로(구어체)
- 지문대조 추궁 + 정상거절 판정 = 결함 종류에 맞춰 템플릿(일관성 보장)
- 윤건우는 스크립트 모호 → 가짜이름 KANG MINHO 주입
- 장소율은 polarity 반대(baked=불량)"""
import json, os, shutil, openpyxl

SCRIPT='data/대사_스크립트.xlsx'; GD='Assets/Resources/GameData'
CFG={  # name: (day, cid, sheet, legit_slot)
 '윤건우':('day5',48,'Day5','baked'),'서지호':('day7',21,'Day7','baked'),
 '전도현':('day8',64,'Day8','baked'),'이지우':('day9',68,'Day9','baked'),
 '정다은':('day10',72,'Day10','baked'),'장소율':('day11',79,'Day11','alt'),
 '서하린':('day12',84,'Day12','baked'),'배은서':('day13',91,'Day13','baked'),
 '양수빈':('day14',96,'Day14','baked'),
}
DEFECT={  # 실제 지문대조 대사 기준
 '윤건우':('name','KANG MINHO'),'서지호':('nationality','USA'),'전도현':('name','CHOI JUWON'),
 '이지우':('name','LIM SEOAH'),'정다은':('birth_date','1994-11-20'),'장소율':('nationality','CHN'),
 '서하린':('name','NA EUNBI'),'배은서':('name','KIM TAEYANG'),'양수빈':('name','KIM HAEUN'),
}
HAN={'name':'이름','birth_date':'생년월일','nationality':'국적'}
def branch_key(b):
    b=str(b or '')
    if '인삿말' in b: return 'greet'
    if b.startswith('지문 검사'): return 'fpscan'
    if b.startswith('지문 대조'): return 'fpmatch'
    if b.startswith('입국 허가'): return 'approve'
    if b.startswith('입국 거부'): return 'reject'
    return None

wb=openpyxl.load_workbook(SCRIPT, read_only=True, data_only=True)
# linemap[(name, 'legit'|'defect')][branch] = [(speaker,text),...]
linemap={}
for name,(day,cid,sheet,slot) in CFG.items():
    ws=wb[sheet]
    cur_name=None; cur_state=None; cur_b=None
    for r in ws.iter_rows(values_only=True):
        if r[2]:
            cur_name=str(r[2]).strip()
            if cur_name!=name: cur_name=None
        if cur_name==name and r[4]:
            s=str(r[4]); cur_state='legit' if s.startswith('정상') else ('defect' if s.startswith('불량') else None)
        bk=branch_key(r[5])
        if bk: cur_b=bk
        if cur_name==name and cur_state and cur_b and r[6] and r[7]:
            linemap.setdefault((name,cur_state),{}).setdefault(cur_b,[]).append((str(r[6]).strip(),str(r[7])))

def mkrows(rows):
    out=[]
    for i,x in enumerate(rows):
        e={"order":i+1,"speaker":x[0],"text":x[1]}
        if len(x)==3 and x[2]: e["claim"]=x[2]
        out.append(e)
    return out
def set_entry(node,rows):
    for c in node['dialogueCases']:
        if c['caseType']=='입장': c['lines']=mkrows(rows); return
    node['dialogueCases'].insert(0,{"caseType":"입장","gameResult":"-","rejectCount":0,"lines":mkrows(rows)})
def set_case(node,gr,rows):
    for c in node['dialogueCases']:
        if c.get('gameResult')==gr and c.get('caseType')=='일반 심사': c['lines']=mkrows(rows); return

def fp_inspector(field,val,pname,pbirth,pnat):
    if field=='name': return f"지문 조회 결과… 신원이 '{val}'(으)로 나오는데요. 여권 이름 '{pname}'과(와) 일치하지 않습니다. 지문은 성형으로도 바뀌지 않습니다."
    if field=='birth_date': return f"지문 조회 결과… 등록 생년월일이 {val}인데, 여권은 {pbirth}네요. 일치하지 않습니다. 지문은 성형으로도 바뀌지 않습니다."
    return f"지문 조회 결과… 신원 국적이 {val}인데, 여권은 {pnat}로 되어 있네요. 일치하지 않습니다. 지문은 성형으로도 바뀌지 않습니다."
def verdict(field):
    return {'name':"지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.",
            'birth_date':"지문 신원의 생년월일이 여권과 일치하지 않습니다. 입국하실 수 없습니다.",
            'nationality':"지문 신원이 여권 국적과 일치하지 않습니다. 입국하실 수 없습니다."}[field]

def cust_lines(lm, branch):  # 손님 대사만(심사관 제외)
    return [(sp,tx) for sp,tx in lm.get(branch,[])]

def build(node, name, variant_kind, pname,pbirth,pnat):
    lm=linemap.get((name, 'legit' if variant_kind=='legit' else 'defect'),{})
    field,val=DEFECT[name]
    # 입장 = 인삿말
    if lm.get('greet'): set_entry(node, lm['greet'])
    # CCL face = 지문검사
    fs=lm.get('fpscan',[])
    insp=' '.join(t for s,t in fs if s=='심사관'); cust=' '.join(t for s,t in fs if s!='심사관')
    ccl=[{"attr":"face","inspector":insp,"customer":cust,"unlocksScan":""}]
    if variant_kind=='legit':
        node['fingerprint']={"type":"fingerprint","result":"일치","detail":"본인 일치","extra":pname,
            "claim":{"attr":"name","value":pname,"label":"지문 대조 신원","unlocksScan":""},
            "record":{"mode":"성형","dbName":pname,"dbBirth":pbirth,"dbNationality":pnat,"criminalRecord":"없음","wantedNo":""}}
        if lm.get('approve'): set_case(node,"정상 승인", lm['approve'])
        if lm.get('reject'):  set_case(node,"잘못 거절", lm['reject'])
    else:
        # 손님 추궁 반응(스크립트의 지문대조 손님 대사 합치기, 없으면 표준)
        fm_cust=' '.join(t for s,t in lm.get('fpmatch',[]) if s!='심사관') or "어… 그건 시스템 오류 아닐까요? …(말을 잇지 못한다)"
        ccl.append({"attr":field,"inspector":fp_inspector(field,val,pname,pbirth,pnat),"customer":fm_cust,"unlocksScan":""})
        rec={"mode":"성형","dbName":pname,"dbBirth":pbirth,"dbNationality":pnat,"criminalRecord":"없음","wantedNo":""}
        rec={'name':{**rec,'dbName':val},'birth_date':{**rec,'dbBirth':val},'nationality':{**rec,'dbNationality':val}}[field]
        node['fingerprint']={"type":"fingerprint","result":"불일치","detail":f"{HAN[field]} 불일치","extra":val,
            "claim":{"attr":field,"value":val,"label":"지문 대조 신원","unlocksScan":""},"record":rec}
        # 정상거절(정답)=입국거부: 심사관=표준 판정문(+claim), 손님=스크립트 반응
        rej_cust=[ (sp,tx) for sp,tx in lm.get('reject',[]) if sp!='심사관' ] or [("","…죄송합니다.")]
        rej=[("심사관", verdict(field), {"attr":field,"value":val,"label":"지문 대조 신원","unlocksScan":""})]
        for sp,tx in rej_cust: rej.append((sp if sp else name, tx))
        set_case(node,"정상 거절", rej)
        if lm.get('approve'): set_case(node,"잘못 허가", lm['approve'])
    node['crossCheckLines']=ccl

report=[]
for name,(day,cid,sheet,slot) in CFG.items():
    p=os.path.join(GD,day+'.json')
    if not os.path.exists(p+'.bak_resync_script'): shutil.copy(p,p+'.bak_resync_script')
    d=json.load(open(p,encoding='utf-8'))
    for c in d['customers']:
        if c.get('customerId')!=cid: continue
        pf={x['key']:x['value'] for x in c['documents'][0]['fields']}
        pn,pb,pnat=pf.get('name'),pf.get('birth_date'),pf.get('nationality')
        legit_baked=(slot=='baked')
        build(c, name, 'legit' if legit_baked else 'defect', pn,pb,pnat)
        build(c['altVariant'], name, 'defect' if legit_baked else 'legit', pn,pb,pnat)
        f,v=DEFECT[name]
        report.append(f"{day} {name}(cid{cid}) 결함={HAN[f]}({v}) baked={'정상' if legit_baked else '불량'}")
    json.dump(d,open(p,'w',encoding='utf-8'),ensure_ascii=False,indent=1)
print('\n'.join(report)); print('DONE 9 재동기화 (백업 *.bak_resync_script)')
