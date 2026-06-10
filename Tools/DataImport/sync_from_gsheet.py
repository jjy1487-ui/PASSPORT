# -*- coding: utf-8 -*-
"""
sync_from_gsheet.py — 구글 시트(여권정리) → 로컬 엑셀 교체 + 재빌드 한 방.

공개 구글 시트를 .xlsx 로 내려받아 data/여권_정리_updated.xlsx 를 교체(타임스탬프 백업)한 뒤
xlsx_to_json → build_days → validate_data 파이프라인을 실행한다.

⚠️ GameDatabase(SO) 갱신은 Unity 에디터 메뉴 'Tools > Passport > Import Data' 가 필요하다.
   (이 스크립트는 소스JSON + dayN.json 까지만 만든다. 상점/테이블이 게임에 반영되려면 에디터 임포트 1번 더.)

실행:  python Tools/DataImport/sync_from_gsheet.py
주의:  로컬 엑셀이 Excel 에서 열려 있으면 교체 불가 → Excel 닫고 실행.
"""
import os, sys, io, shutil, subprocess, datetime
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 공개 구글 시트 ID (여권정리). 시트가 바뀌면 이 값만 교체.
SHEET_ID = "1LeEbmvb0wVPp_6UeOEAMbqG9HPwratEP"
URL = "https://docs.google.com/spreadsheets/d/%s/export?format=xlsx" % SHEET_ID

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
XLSX = os.path.join(REPO, "data", "여권_정리_updated.xlsx")
TMP  = os.path.join(REPO, "data", "_gsheet_download.tmp.xlsx")


def run_py(rel_script):
    print(">> python %s" % rel_script)
    r = subprocess.run([sys.executable, rel_script], cwd=REPO,
                       capture_output=True, text=True, encoding="utf-8")
    if r.stdout:
        print(r.stdout.strip())
    if r.returncode != 0:
        if r.stderr:
            print(r.stderr.strip())
        print("!! 실패 (rc=%d) — 중단" % r.returncode)
        sys.exit(1)


def main():
    # 1) 다운로드 (curl -L 로 리다이렉트 따라감)
    print("1) 구글 시트 다운로드 …")
    dl = subprocess.run(["curl", "-sL", "--max-time", "60", URL, "-o", TMP])
    if dl.returncode != 0 or not os.path.exists(TMP):
        print("!! 다운로드 실패 — 인터넷 연결/시트 공개 여부 확인"); sys.exit(1)
    with open(TMP, "rb") as f:
        if f.read(2) != b"PK":   # xlsx(zip) 시그니처. 아니면 로그인 HTML(비공개).
            os.remove(TMP)
            print("!! 받은 게 엑셀이 아님 = 시트가 비공개일 수 있음.")
            print("   구글 시트 [공유] → '링크가 있는 모든 사용자 = 뷰어' 로 바꿔주세요.")
            sys.exit(1)

    # 2) 로컬 엑셀 잠김 확인
    try:
        open(XLSX, "r+b").close()
    except PermissionError:
        os.remove(TMP)
        print("!! 로컬 엑셀이 Excel 에서 열려 있어 교체 불가. Excel 닫고 다시 실행."); sys.exit(1)

    # 3) 백업 + 교체
    ts = datetime.datetime.now().strftime("%y%m%d_%H%M%S")
    bak = XLSX[:-5] + (".bak_gsync_%s.xlsx" % ts)
    shutil.copy2(XLSX, bak)
    shutil.move(TMP, XLSX)
    print("3) 로컬 엑셀 교체 완료 (백업: %s)" % os.path.basename(bak))

    # 4) 재빌드 (소스JSON → dayN.json → 검증)
    print("4) 재빌드 …")
    run_py("Tools/DataImport/xlsx_to_json.py")
    run_py("Tools/DataImport/build_days.py")
    run_py("Tools/DataImport/validate_data.py")

    print("")
    print("✅ 완료: 구글 시트 → 로컬 엑셀 교체 + dayN.json 재빌드.")
    print("⚠️ 남은 1단계(에디터): Unity 메뉴 'Tools > Passport > Import Data' 실행 → GameDatabase(SO) 갱신.")
    print("   (상점/테이블이 게임에 반영되려면 필요. 안 하면 dayN.json 만 갱신됨.)")


if __name__ == "__main__":
    main()
