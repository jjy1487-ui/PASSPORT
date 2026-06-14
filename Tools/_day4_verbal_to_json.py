# -*- coding: utf-8 -*-
"""day4 구두확인 → day4.json 반영. 6명 입장(전환대사+이름/생년월일) + 박하준 불량(만료일→음성기록 생년월일) + 규정11.
대사_스크립트 Day4와 동일. 노가은(성형)은 제외."""
import json, os, shutil

p='Assets/Resources/GameData/day4.json'
shutil.copy(p, p+'.bak_day4verbal_json')
d=json.load(open(p,encoding='utf-8'))

TR="요즘 신원 확인이 강화돼서 몇 가지 더 여쭙겠습니다."
def ln(o,sp,tx,claim=None):
    e={"order":o,"speaker":sp,"text":tx}
    if claim: e["claim"]=claim
    return e
def mk(lines):  # lines: list of (speaker, text) or (speaker, text, claim)
    return [ln(i+1,*x) if len(x)==3 else ln(i+1,x[0],x[1]) for i,x in enumerate(lines)]

# 입장 대사(baked) — cid -> lines
ENTRY={
 41:[("박하준","아니 왜 이렇게 오래 걸려요?"),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("박하준","박하준입니다."),("심사관","생년월일은요?"),("박하준","1982년 9월 14일이요."),("심사관","어디 다녀오셨습니까?"),("박하준","출장 다녀왔어요, 출장. 여권 냈으니까 빨리 처리해.")],
 42:[("최예준","안녕하세요. 요즘 여권 사진 때문에 문제됐다는 얘기 들었어요."),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("최예준","최예준입니다."),("심사관","생년월일은요?"),("최예준","1988년 6월 30일이요."),("심사관","어디 다녀오셨습니까?"),("최예준","친구 만나러 일본에 다녀왔어요. 여권 확인 부탁드립니다.")],
 43:[("토머스 무어","Good morning! I'm so excited to be here. (안녕하세요! 오게 되어 정말 설레요.)"),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("토머스 무어","Thomas Moore. (토머스 무어요.)"),("심사관","생년월일은요?"),("토머스 무어","February 28th, 1995. (1995년 2월 28일이요.)"),("심사관","방문 목적이 어떻게 되십니까?"),("토머스 무어","I'm here for sightseeing. (관광하러 왔어요.)"),("토머스 무어","Here is my passport. (여기 여권 드릴게요.)")],
 44:[("정시우","신원 조사 때문에 이렇게 기다려야해? 빨리 처리해줘요."),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("정시우","정시우입니다."),("심사관","생년월일은요?"),("정시우","1979년 5월 5일입니다."),("심사관","어디 다녀오셨습니까?"),("정시우","여행 좀 다녀왔어요. 여권 냈으니까 빨리 처리해.")],
 45:[("강주원","안녕하세요. 사진이랑 똑같이 생겼으니 걱정 없겠죠? (웃으며 여권 내밀며)"),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("강주원","강주원입니다."),("심사관","생년월일은요?"),("강주원","1990년 1월 20일이요."),("심사관","어디 다녀오셨습니까?"),("강주원","가족이랑 동남아 여행 다녀왔어요. 여권 확인 부탁드립니다.")],
 46:[("리 강","您好，请帮我看看。 (안녕하세요, 좀 봐주세요.)"),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("리 강","李刚。(리 강이요.)"),("심사관","생년월일은요?"),("리 강","1993年12月5日。(1993년 12월 5일이요.)"),("심사관","방문 목적이 어떻게 되십니까?"),("리 강","我是来旅游观光的。 (관광하러 왔어요.)"),("리 강","这是我的护照和签证。 (여권이랑 비자 여기요.)")],
}
# 정시우 불량(alt) 입장 — gender 결함 유지, 입장만 구두확인 반영
SIWOO_ALT_ENTRY=[("정시우","아 진짜, 빨리 좀 해줘요."),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("정시우","정시우입니다."),("심사관","생년월일은요?"),("정시우","1979년 5월 5일입니다."),("심사관","어디 다녀오셨습니까?"),("정시우","여행이요, 여행. 빨리요.")]
# 박하준 불량(alt) 입장 — 음성기록 생년월일 결함
HAJUN_ALT_ENTRY=[("박하준","빨리 좀 합시다, 나 바쁜 사람이야."),("심사관",TR),("심사관","성함이 어떻게 되십니까?"),("박하준","박연준입니다."),("심사관","박연준 씨… 맞으십니까?"),("박하준","아, 박하준입니다. 박하준."),("심사관","생년월일은 어떻게 되시죠?"),
 ("박하준","1985년 3월 22일이요.",{"attr":"birth_date","value":"1985-03-22","label":"진술 생년월일","unlocksScan":""}),
 ("심사관","어디 다녀오셨습니까?"),("박하준","출장이요. 빨리 좀 합시다.")]

def set_entry(node, lines):
    for c in node.get('dialogueCases',[]):
        if c.get('caseType')=='입장': c['lines']=mk(lines); return
    node.setdefault('dialogueCases',[]).insert(0,{"caseType":"입장","gameResult":"-","rejectCount":0,"lines":mk(lines)})
def set_case(node, gr, lines):
    for c in node.get('dialogueCases',[]):
        if c.get('gameResult')==gr and c.get('caseType')=='일반 심사': c['lines']=mk(lines); return
    node['dialogueCases'].append({"caseType":"일반 심사","gameResult":gr,"rejectCount":0,"lines":mk(lines)})

for c in d['customers']:
    cid=c.get('customerId')
    if cid in ENTRY:
        set_entry(c, ENTRY[cid])      # baked 입장
    if cid==44:  # 정시우 alt 입장(성별 결함 유지)
        set_entry(c['altVariant'], SIWOO_ALT_ENTRY)
    if cid==41:  # 박하준 alt = 음성기록 생년월일
        a=c['altVariant']
        set_entry(a, HAJUN_ALT_ENTRY)
        # 여권 정상 복구
        for doc in a.get('documents',[]):
            if '여권' in (doc.get('documentType') or ''):
                doc['violationField']="없음"
                for f in doc.get('fields',[]):
                    if f.get('key')=='expiry_date': f['value']="2029-07-14"
        # 음성 대조 추궁(생년월일)
        a['crossCheckLines']=[{"attr":"birth_date","inspector":"방금 말씀하신 생년월일이 여권과 다른데요?","customer":"어… 그게…","unlocksScan":""}]
        # 판정 대사
        set_case(a,"정상 거절",[("심사관","진술하신 생년월일이 여권과 일치하지 않습니다. 입국하실 수 없습니다."),("박하준","에이 진짜... 알겠어요.")])
        set_case(a,"잘못 허가",[("심사관","즐거운 여행 되십시오."),("박하준","이제 됐죠?")])

# 규정 11
for r in d.get('rules',[]):
    if r.get('ruleId')==11:
        r['title']="방문객 구두 확인"
        r['content']="심사관은 입국자에게 신원과 방문 정보를 직접 확인한다. 진술한 내용이 제출 서류와 다르면 입국을 거부한다."
        r['relatedField']=""; r['attr']=""

json.dump(d,open(p,'w',encoding='utf-8'),ensure_ascii=False,indent=1)
print("day4.json 반영 완료 (백업 day4.json.bak_day4verbal_json)")
