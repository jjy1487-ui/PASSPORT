# -*- coding: utf-8 -*-
"""PC 엑셀 → 구글시트 '일방향' 푸시(덮어쓰기). PC가 원본.

사용:  data/ 의 엑셀을 수정·저장한 뒤  ➜  push_sheets.bat 더블클릭 (또는 `python push_sheets.py`)
       그러면 아래 TARGETS 의 두 구글 파일이 로컬 내용으로 덮어써집니다(같은 URL 유지).

최초 1회: 같은 폴더에 credentials.json 필요(구글 OAuth). README.md 참고.
필요 패키지: pip install google-api-python-client google-auth-httplib2 google-auth-oauthlib
"""
import os, sys
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.abspath(os.path.join(HERE, '..', '..', 'data'))
SCOPES = ['https://www.googleapis.com/auth/drive']
XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

# (로컬 파일명, 구글 파일 ID)  — ID는 시트 URL .../d/<여기>/edit 의 가운데 부분
TARGETS = [
    ('대사_스크립트.xlsx',      '146hYiUruYi0KupXzoPbooF7W4fiamOKt'),
    ('여권_정리_updated.xlsx',  '1GtQQ58wL3so6w0KtGwbKGH02zKEk_ubIzsaYzYuD-P8'),
]


def get_service():
    creds = None
    tok = os.path.join(HERE, 'token.json')
    cred = os.path.join(HERE, 'credentials.json')
    if os.path.exists(tok):
        creds = Credentials.from_authorized_user_file(tok, SCOPES)
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            creds.refresh(Request())
        else:
            if not os.path.exists(cred):
                print('[오류] credentials.json 이 없습니다.')
                print('       README.md 의 OAuth 설정을 먼저 끝내고 이 폴더에 credentials.json 을 두세요.')
                sys.exit(1)
            flow = InstalledAppFlow.from_client_secrets_file(cred, SCOPES)
            creds = flow.run_local_server(port=0)   # 브라우저로 1회 동의
        with open(tok, 'w', encoding='utf-8') as f:
            f.write(creds.to_json())
    return build('drive', 'v3', credentials=creds)


def main():
    svc = get_service()
    ok = 0
    for fname, fid in TARGETS:
        path = os.path.join(DATA, fname)
        if not os.path.exists(path):
            print(f'[건너뜀] 로컬 파일 없음: {path}')
            continue
        try:
            meta = svc.files().get(fileId=fid, fields='name,mimeType',
                                   supportsAllDrives=True).execute()
            media = MediaFileUpload(path, mimetype=XLSX, resumable=True)
            svc.files().update(fileId=fid, media_body=media,
                               supportsAllDrives=True).execute()
            print(f'[완료] {fname}  ->  "{meta.get("name")}"')
            print(f'        https://docs.google.com/spreadsheets/d/{fid}/edit')
            ok += 1
        except Exception as e:
            print(f'[실패] {fname} (ID {fid}): {e}')
    print(f'\n끝. {ok}/{len(TARGETS)}개 시트 반영. (엑셀에서 먼저 저장했는지 확인하세요!)')


if __name__ == '__main__':
    main()
