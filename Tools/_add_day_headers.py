# -*- coding: utf-8 -*-
"""대사_스크립트 시트에 일차별 헤더 행 삽입 (날짜별 정리). xlwings 라이브."""
import os,sys
sys.stdout.reconfigure(encoding='utf-8')
import xlwings as xw
XLSX=r"C:\Users\chris\Documents\produc_build_reecture\data\시나리오_스크립트.xlsx"
DAYS={
 1:("첫 번째 도장","신입 관리직 배치 — 기본 심사 튜토리얼"),
 2:("여기가 BTS의 나라입니까?","외국인 관광객 증가"),
 3:("강남은 어디에 있나요?","성형 수술 고객 입국 — 지문 판독기·비자"),
 4:("잠깐만요, 다시 보여주세요","입국 심사 불만 증가 — 방문목적 확인"),
 5:("검역 강화","방역 체계 가동 — PCR 검사서"),
 6:("긴장 속의 일상","방역 체제 유지"),
 7:("다음 사람, 다음 사람","피로 누적"),
 8:("새로운 시작","유학생 입국 — 입학 합격서"),
 9:("꿈을 찾아서","취업 목적 방문객 — 재직 증명서"),
 10:("평범한 하루","무난한 하루"),
 11:("그 사람을 왜 통과시켰죠?","범죄 조직 유입 — X-RAY"),
 12:("서울 테러 속보","보안 경계 격상 — 범죄자 등장"),
 13:("정말 괜찮은 사람일까?","의심의 일상화"),
 14:("최종 심사","마지막 근무 — 단일 엔딩"),
}
def asday(v):
    if isinstance(v,bool): return None
    if isinstance(v,(int,float)): return int(v)
    if isinstance(v,str) and v.strip().replace('.0','').isdigit(): return int(float(v))
    return None
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
starts=[]; seen=set()
for i,row in enumerate(vals):
    day=asday(row[0] if len(row)>0 else None)
    if day in DAYS and day not in seen:
        seen.add(day); starts.append((i+1,day))
print('일차 시작 행:',starts)
HFILL=(31,78,151); HFONT=(255,255,255)
for r,day in sorted(starts,key=lambda x:-x[0]):
    title,theme=DAYS[day]
    ws.api.Rows("{}:{}".format(r,r)).Insert()
    cell=ws.range((r,1))
    cell.value=f"━━━  {day}일차 · {title}   —   {theme}  ━━━"
    for c in range(1,9):
        ws.range((r,c)).color=HFILL
    cell.font.color=HFONT; cell.font.bold=True; cell.font.size=12
bk.save()
try: bk.app.screen_updating=True
except: pass
print(f'헤더 {len(starts)}개 삽입 완료')
if opened: bk.app.quit()
