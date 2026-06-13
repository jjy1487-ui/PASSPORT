# -*- coding: utf-8 -*-
"""17개 결함 블록 설계 — 타깃 셀 값만 교체(행 구조 불변). xlwings 라이브, 단일셀 쓰기만."""
import os,sys,re
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw
XLSX=os.path.abspath('data/대사_스크립트.xlsx')

# (sheet, name, label, type|None, beat_label, callout, closer, reject)
SPECS=[
 ('Day2','데이비드 스미스','불량(여권 이름 불일치)',None,'서류 대조(여권 ↔ 비자)',
  '여권 영문이름과 비자 이름을 대조하니, 철자가 다른데요?','두 서류의 이름이 일치해야 입국이 가능합니다.',
  '여권과 비자의 이름이 일치하지 않아 입국하실 수 없습니다.'),
 ('Day9','류 옌','불량(여권 국적 불일치)',None,'서류 대조(여권 ↔ 비자)',
  '여권 국적과 비자 국적이 서로 다른데요?','두 서류의 국적이 일치해야 합니다.',
  '여권과 비자의 국적이 일치하지 않아 입국하실 수 없습니다.'),
 ('Day8','매튜 앤더슨','불량(여권 성별 불일치)',None,'서류 대조(여권 ↔ 외모)',
  '여권 성별 표기가 실제 외모와 달라 보이는데요?','여권 정보가 본인과 일치해야 합니다.',
  '여권 성별 정보가 본인과 일치하지 않아 입국하실 수 없습니다.'),
 ('Day4','박하준','불량(여권 발급일 오류)',None,'서류 대조(여권 자체 검증)',
  '여권 발급일이 만료일보다 늦게 찍혀 있는데요? 논리상 맞지 않습니다.','여권 날짜 정보에 오류가 있으면 입국이 불가합니다.',
  '여권 발급일 정보에 오류가 있어 입국하실 수 없습니다.'),
 ('Day3','장 웨이','불량(비자 번호 변조)',None,'서류 대조(비자 자체 검증)',
  '비자 번호 형식이 정상 발급 번호와 다른데요? 위조가 의심됩니다.','비자 번호가 확인되지 않으면 입국이 불가합니다.',
  '비자 번호가 정상 확인되지 않아 입국하실 수 없습니다.'),
 ('Day11','자오 친','불량(비자 종류 불일치)',None,'음성 대조(비자 ↔ 음성기록)',
  '말씀하신 방문 목적과 비자 종류가 다른데요?','비자 종류가 방문 목적과 맞아야 합니다.',
  '비자 종류가 방문 목적과 일치하지 않아 입국하실 수 없습니다.'),
 ('Day12','황 레이','불량(비자 국적 불일치)',None,'서류 대조(비자 ↔ 여권)',
  '비자 국적과 여권 국적이 서로 다른데요?','두 서류의 국적이 일치해야 합니다.',
  '비자와 여권의 국적이 일치하지 않아 입국하실 수 없습니다.'),
 ('Day13','쉬 펑','불량(비자 발급일 오류)',None,'서류 대조(비자 자체 검증)',
  '비자 발급일 정보에 오류가 보이는데요?','비자 날짜 정보가 정확해야 합니다.',
  '비자 발급일 정보에 오류가 있어 입국하실 수 없습니다.'),
 ('Day13','우 팅','불량(비자 만료)',None,'서류 대조(비자 ↔ 오늘 날짜)',
  '비자 유효기간을 확인하니 이미 만료됐는데요?','유효한 비자가 있어야 입국이 가능합니다.',
  '비자가 만료되어 입국하실 수 없습니다.'),
 ('Day6','사토 유토','불량(PCR 이름 불일치)',None,'서류 대조(PCR ↔ 여권)',
  'PCR 검사서 이름과 여권 이름이 다른데요?','검사서가 본인 것이어야 합니다.',
  'PCR 검사서의 이름이 여권과 일치하지 않아 입국하실 수 없습니다.'),
 ('Day6','다니엘 테일러','불량(PCR 국적 불일치)',None,'서류 대조(PCR ↔ 여권)',
  'PCR 검사서 국적과 여권 국적이 다른데요?','검사서 정보가 여권과 일치해야 합니다.',
  'PCR 검사서의 국적이 여권과 일치하지 않아 입국하실 수 없습니다.'),
 ('Day7','왕 팡','불량(PCR 검사번호 변조)',None,'서류 대조(PCR 자체 검증)',
  'PCR 검사 번호가 정상 형식이 아닌데요? 위조가 의심됩니다.','검사 번호가 확인되지 않으면 입국이 불가합니다.',
  'PCR 검사 번호가 확인되지 않아 입국하실 수 없습니다.'),
 ('Day7','신유준','불량(PCR 검사일 오류)',None,'서류 대조(PCR ↔ 오늘 날짜)',
  'PCR 검사일이 오늘 이후로 찍혀 있는데요? 논리상 맞지 않습니다.','검사일 정보가 정확해야 합니다.',
  'PCR 검사일 정보에 오류가 있어 입국하실 수 없습니다.'),
 ('Day6','오은우','불량(PCR 유효기한 만료)',None,'서류 대조(PCR ↔ 오늘 날짜)',
  'PCR 검사서 유효기한이 이미 지났는데요?','유효한 검사서가 있어야 합니다.',
  'PCR 검사서 유효기간이 지나 입국하실 수 없습니다.'),
 ('Day10','스즈키 소라','불량(취업증빙 증빙번호 변조)','취업자','서류 대조(취업증빙 자체 검증)',
  '재직증명서 증빙 번호가 정상 형식이 아닌데요?','증빙 번호가 확인되지 않으면 입국이 불가합니다.',
  '재직증명서 증빙 번호가 확인되지 않아 입국하실 수 없습니다.'),
 ('Day11','앤드류 화이트','불량(취업증빙 직종 불일치)','취업자','서류 대조(취업증빙 ↔ 비자)',
  '재직증명서 직종과 비자 정보가 다른데요?','서류의 직종 정보가 일치해야 합니다.',
  '재직증명서 직종 정보가 일치하지 않아 입국하실 수 없습니다.'),
 ('Day13','다카하시 리쿠','불량(취업증빙 발급일 오류)','취업자','서류 대조(취업증빙 자체 검증)',
  '재직증명서 발급일 정보에 오류가 보이는데요?','서류 날짜가 정확해야 합니다.',
  '재직증명서 발급일 정보에 오류가 있어 입국하실 수 없습니다.'),
]
def lang_react(text):
    if re.search(r'[぀-ヿ]',text): return 'え？問題ないはずですが…(네? 문제 없을 텐데요…)'
    if re.search(r'[一-鿿]',text): return '啊？应该没问题的…(어? 문제 없을 텐데요…)'
    if re.search(r'[A-Za-z]',text) and not re.search(r'[가-힣]',re.sub(r'\([^)]*\)','',text)): return "What? That should be fine…"
    return '네? 그럴 리가요…'

bk=None
for app in xw.apps:
    for b in app.books:
        try:
            if os.path.basename(b.fullname).lower()=='대사_스크립트.xlsx': bk=b
        except: pass
if bk is None:
    print('파일 안 열림'); sys.exit(1)
try: bk.app.screen_updating=False
except: pass
from collections import defaultdict
bysheet=defaultdict(list)
for s in SPECS: bysheet[s[0]].append(s)
CROSS=('대조','검사')
done=0
for sh,specs in bysheet.items():
    ws=bk.sheets[sh]; vals=ws.used_range.value
    def cv(r,c):
        v=vals[r][c] if c<len(vals[r]) else None
        return '' if v is None else str(v).strip()
    for (_,name,label,typ,blab,callout,closer,reject) in specs:
        # 불량 블록 헤더행 찾기
        start=None
        for i in range(len(vals)):
            if cv(i,2)==name and cv(i,4).startswith('불량'): start=i; break
        if start is None:
            print('  미발견:',sh,name); continue
        # 블록 끝
        end=start+1
        while end<len(vals) and not (cv(end,2) and cv(end,5)=='인삿말') and not all(cv(end,c)=='' for c in range(8)): end+=1
        # 셀 교체
        ws.range((start+1,5)).value=label                  # 서류상태
        if typ: ws.range((start+1,4)).value=typ            # 유형
        # 대조 비트 행 (분기에 대조/검사 포함)
        beat=None; ban=[]
        for i in range(start+1,end):
            f=cv(i,5)
            if f and any(k in f for k in CROSS): beat=i
        if beat is not None:
            ws.range((beat+1,6)).value=blab                # 분기 라벨
            ws.range((beat+1,8)).value=callout             # 콜아웃
            # 비트 내 손님 반응(다음 손님 발화) 교체 → 언어 맞춤
            for i in range(beat+1,end):
                if cv(i,5).startswith('입국'): break
                if cv(i,6) and cv(i,6)!='심사관':
                    ws.range((i+1,8)).value=lang_react(cv(i,7)); break
            # 클로저(비트 내 마지막 심사관 줄)
            closer_row=None
            for i in range(beat+1,end):
                if cv(i,5).startswith('입국'): break
                if cv(i,6)=='심사관': closer_row=i
            if closer_row is not None: ws.range((closer_row+1,8)).value=closer
        # 입국 거부(정답) 사유
        for i in range(start+1,end):
            if cv(i,5).startswith('입국 거부') and ('정답' in cv(i,5)):
                ws.range((i+1,8)).value=reject; break
        done+=1
        print('  ✓',sh,name,'→',label)
bk.save()
try: bk.app.screen_updating=True
except: pass
print(f'\n완료 {done}/17 | Day5 max_col 점검:',bk.sheets['Day5'].used_range.last_cell.column)
