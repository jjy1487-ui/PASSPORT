# PC 엑셀 → 구글시트 푸시 (일방향)

`data/대사_스크립트.xlsx`, `data/여권_정리_updated.xlsx` 를 수정·저장한 뒤
**`push_sheets.bat` 더블클릭**하면 구글시트 두 개가 로컬 내용으로 덮어써집니다(URL 유지).

> 방향: **PC → 구글시트 (일방향)**. 구글시트에서 직접 한 편집은 다음 푸시 때 덮어써집니다. PC가 원본.

---

## 최초 1회 설정 (약 10분)

### 1) 파이썬 패키지 설치 (이미 설치돼 있으면 건너뜀)
```
pip install google-api-python-client google-auth-httplib2 google-auth-oauthlib
```

### 2) 구글 OAuth 키(credentials.json) 발급
1. https://console.cloud.google.com/ 접속(구글 로그인) → 상단에서 **새 프로젝트** 생성(이름 아무거나, 예: passport-sync).
2. 좌측 메뉴 **API 및 서비스 → 라이브러리** → "Google Drive API" 검색 → **사용 설정**.
3. **API 및 서비스 → OAuth 동의 화면**:
   - User Type = **외부(External)** → 만들기
   - 앱 이름/이메일만 채우고 저장(나머지 기본)
   - **테스트 사용자(Test users)** 에 **본인 구글 이메일 추가** (중요! 안 넣으면 동의 단계에서 막힘)
4. **API 및 서비스 → 사용자 인증 정보 → 사용자 인증 정보 만들기 → OAuth 클라이언트 ID**:
   - 애플리케이션 유형 = **데스크톱 앱**
   - 만든 뒤 **JSON 다운로드** → 파일 이름을 **`credentials.json`** 으로 바꿔 **이 폴더**
     (`Tools/gsheet_sync/`) 에 둡니다.

### 3) 첫 실행 (브라우저 동의 1회)
- `push_sheets.bat` 더블클릭 → 브라우저가 열리며 구글 로그인·권한 동의.
  - "Google에서 확인하지 않은 앱" 경고가 나오면 **고급 → (앱이름)(으)로 이동** → 허용.
- 동의하면 `token.json` 이 생성되고, 이후엔 동의 없이 바로 푸시됩니다.

---

## 평소 사용
1. 엑셀에서 수정하고 **저장(Ctrl+S)**.
2. `push_sheets.bat` 더블클릭.
3. "[완료]" 가 두 줄 뜨면 끝. 구글시트 새로고침하면 반영돼 있습니다.

## 주의
- **엑셀에서 저장(Ctrl+S) 후** 실행하세요. 디스크에 저장된 내용을 올립니다.
- 시트 ID가 바뀌면(파일 새로 올리면) `push_sheets.py` 의 `TARGETS` 안 ID를 고치세요.
- 네이티브 구글시트의 경우 탭 순서/gid가 재생성될 수 있으나 URL은 유지됩니다.
- `credentials.json`, `token.json` 은 개인 키이므로 git 에 올리지 마세요(.gitignore 권장).
