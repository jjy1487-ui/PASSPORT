# -*- coding: utf-8 -*-
"""정밀 점수 곡선 시뮬레이션 — ScoreEconomyManager.Settle 로직을 1:1 재현.
14일 baked day JSON 98손님에 대해 정확도 0~100%에서 누적 점수를 계산한다.
QA의 PacingSimulationTests.Simulate 와 동일한 결정론 분배(앞에서부터 correctTarget명 정답).
"""
import json, os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# ── BranchKeys / DocStates / CharacterTypes 상수 (C#과 동일) ──
APPROVE_CORRECT="approve_correct"; REJECT_CORRECT="reject_correct"
APPROVE_WRONG="approve_wrong"; REJECT_WRONG="reject_wrong"
APPROVE_IMMEDIATE="approve_immediate"
AFTER_REJECT={1:"approve_after_reject_1",2:"approve_after_reject_2",3:"approve_after_reject_3"}
NORMAL="normal"; DEFECT="defect"
GR_APPROVE="정상 승인"  # GameResults.Approve
PLASTIC="성형 의심 고객"; CELEB="특수(연예인)★"; POLITICIAN="특수(정치인)★"

FALLBACK_CORRECT=10; FALLBACK_WRONG=-15

def uses_accrue_scale(ct):
    return ct in (PLASTIC, CELEB, POLITICIAN)

def resolve_branch(correct_result, player_approved, wrong_reject_count, char_type, defect_variant):
    """BranchKeyResolver.Resolve 재현. returns (docState, branchKey, variant)."""
    should_approve = (correct_result == GR_APPROVE)
    doc_state = NORMAL if should_approve else DEFECT
    variant = defect_variant if defect_variant else None
    if should_approve:
        if player_approved:
            if wrong_reject_count <= 0:
                bk = APPROVE_IMMEDIATE if uses_accrue_scale(char_type) else APPROVE_CORRECT
                return doc_state, bk, variant
            return doc_state, AFTER_REJECT.get(min(wrong_reject_count,3), AFTER_REJECT[3]), variant
        return doc_state, REJECT_WRONG, variant
    else:
        if not player_approved:
            return doc_state, REJECT_CORRECT, variant
        return doc_state, APPROVE_WRONG, variant

# ── CharacterScoreTable.Find 재현 ──
def load_score_rows():
    s=json.load(open(os.path.join(ROOT,'Assets/GameData/_source/GameData.source.json'),encoding='utf-8'))
    return s['sheets']['character_score']['rows']

def find_score(rows, char_type, doc_state, branch_key, defect_variant, visit_round=None):
    """1차: 변이까지 매칭, 2차: 변이 무시. C# LookupScore 순서 재현."""
    def _find(variant):
        for r in rows:
            if r.get('character_type') != char_type: continue
            if r.get('doc_state') != doc_state: continue
            if r.get('branch_key') != branch_key: continue
            if variant is not None and r.get('defect_variant') != variant: continue
            if visit_round is not None and r.get('visit_round') != visit_round: continue
            return r
        return None
    r = _find(defect_variant)
    if r is None:
        r = _find(None)
    return r

def lookup_score(rows, char_type, branch, was_correct):
    """ScoreEconomyManager.LookupScore 재현."""
    doc_state, branch_key, variant = branch
    r = find_score(rows, char_type, doc_state, branch_key, variant)
    if r is not None:
        sc = r.get('score')
        if sc is not None and str(sc).strip()!='' :
            try:
                return int(sc)
            except ValueError:
                return 0
        return 0  # 범위/이벤트 행
    return FALLBACK_CORRECT if was_correct else FALLBACK_WRONG

# ── 14일 손님 로드 ──
def load_all_customers():
    out=[]
    for day in range(1,15):
        d=json.load(open(os.path.join(ROOT,f'Assets/Resources/GameData/day{day}.json'),encoding='utf-8'))
        for c in d.get('customers') or []:
            out.append(c)
    return out

def simulate(accuracy, customers, rows):
    """PacingSimulationTests.Simulate 재현. returns (score, correct, judged)."""
    n=len(customers)
    correct_target = round(n*accuracy)  # Mathf.RoundToInt: banker? Unity RoundToInt = round half to even
    # Unity Mathf.RoundToInt uses round-half-to-even. Use that:
    import math
    def unity_round(x):
        f=math.floor(x); diff=x-f
        if diff<0.5: return f
        if diff>0.5: return f+1
        return f if f%2==0 else f+1
    correct_target=unity_round(n*accuracy)
    correct_so_far=0; score=0; correct=0; judged=0
    for c in customers:
        cr=c.get('correctResult')
        should_approve = (cr==GR_APPROVE)
        play_correct = correct_so_far < correct_target
        if play_correct: correct_so_far+=1
        approved = should_approve if play_correct else (not should_approve)
        branch=resolve_branch(cr, approved, 0, c.get('characterType'), c.get('defectVariant') or '')
        score += lookup_score(rows, c.get('characterType'), branch, play_correct)
        judged+=1
        if play_correct: correct+=1
    return score, correct, judged

if __name__=='__main__':
    rows=load_score_rows()
    customers=load_all_customers()
    print(f'총 손님 수: {len(customers)}')
    print('정확도 -> 누적점수 (평균 시나리오: 앞에서부터 정답)')
    curve=[]
    for i in range(0,11):
        acc=i/10.0
        sc,co,ju=simulate(acc, customers, rows)
        flat=round(2450*(co/ju)-1470) if ju else 0
        curve.append((acc,sc))
        print(f'  {acc*100:5.0f}% | 실측 {co}/{ju}={co/ju*100:5.1f}% | 캐릭터별표={sc:6d} | 옛평면공식={flat:6d}')
    # 대표 분산: 랜덤 시나리오로 분포 폭 확인 (정답 집합을 뒤에서부터)
    print()
    print('역순 시나리오(뒤에서부터 정답) - 같은 정확도의 분산 확인:')
    def simulate_rev(accuracy):
        n=len(customers); import math
        def ur(x):
            f=math.floor(x);d=x-f
            return f if d<0.5 else (f+1 if d>0.5 else (f if f%2==0 else f+1))
        ct=ur(n*accuracy); score=0
        for idx,c in enumerate(customers):
            cr=c.get('correctResult'); sa=(cr==GR_APPROVE)
            play_correct = idx >= (n-ct)
            approved = sa if play_correct else (not sa)
            branch=resolve_branch(cr, approved, 0, c.get('characterType'), c.get('defectVariant') or '')
            score += lookup_score(rows, c.get('characterType'), branch, play_correct)
        return score
    for i in range(0,11):
        acc=i/10.0
        print(f'  {acc*100:5.0f}% | 역순={simulate_rev(acc):6d}')
