# -*- coding: utf-8 -*-
"""사진 참조 패치 → 소스 JSON → day1~14 → 검증을 한 번에. 엑셀 잠금 해제 대기 포함."""
import os, sys, time, subprocess, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
XLSX = os.path.join(REPO, 'data', '여권_정리_updated.xlsx')
LOG = os.path.join(HERE, '_rebuild_result.txt')
PY = sys.executable

def writable(p):
    try:
        f = open(p, 'r+b'); f.close(); return True
    except PermissionError:
        return False

def run(args, cwd):
    r = subprocess.run([PY] + args, cwd=cwd, capture_output=True, text=True, encoding='utf-8')
    return r.returncode, (r.stdout or '') + (r.stderr or '')

lines = []
# 엑셀 잠금 해제까지 최대 30분 대기
deadline = time.time() + 1800
while not writable(XLSX):
    if time.time() > deadline:
        lines.append('TIMEOUT: 엑셀이 30분간 잠겨 있어 중단')
        open(LOG,'w',encoding='utf-8').write('\n'.join(lines)); sys.exit(1)
    time.sleep(3)

lines.append('xlsx writable -> 파이프라인 시작')
# 모든 단계 동일 규약: cwd=REPO, 경로는 REPO 기준 상대경로(스크립트는 __file__로 REPO 재계산하므로 cwd 무관).
rc, o = run(['Tools/DataImport/patch_sprite_refs.py'], REPO)
lines.append('[patch_sprite_refs] rc=%d %s' % (rc, o.strip()))
rc, o = run(['Tools/DataImport/xlsx_to_json.py'], REPO)
lines.append('[xlsx_to_json] rc=%d %s' % (rc, o.strip()))
rc, o = run(['Tools/DataImport/build_days.py'], REPO)
lines.append('[build_days] rc=%d %s' % (rc, o.strip()))
rc, o = run(['Tools/DataImport/validate_data.py'], REPO)
lines.append('[validate_data] rc=%d %s' % (rc, o.strip()))
lines.append('DONE')
open(LOG,'w',encoding='utf-8').write('\n'.join(lines))
print('\n'.join(lines))
