# 받았는데 사운드·이미지가 안 들리거나 깨질 때 (Git LFS 문제 해결)

> **증상:** clone/pull 했는데 **푸시된 사운드가 안 들리거나**, 이미지·폰트가 깨져 보인다.
> **원인:** 이 저장소는 큰 파일(오디오 `*.mp3`, 이미지 `*.png`, 폰트 등)을 **Git LFS**로 저장합니다.
> LFS가 제대로 설정 안 되면, 실제 파일 대신 **"포인터(133바이트짜리 텍스트 껍데기)"**만 받아져서 Unity가 빈 에셋으로 임포트 → 소리/그림이 안 나옵니다.

---

## 1단계 — 원인 확인 (둘 중 하나만 해도 됨)

### (A) 파일 크기로 확인 — 제일 쉬움
탐색기에서 `Assets\Audio\Main Theme.mp3` **우클릭 → 속성 → 크기**:

| 크기 | 상태 |
|---|---|
| **약 5MB** | ✅ 정상 (소리 나야 함 → 3단계로) |
| **1KB 미만 (수백 바이트)** | ❌ 포인터만 받음 = **이게 원인** → 2단계 |

### (B) 명령어로 확인
프로젝트 폴더에서 터미널(Git Bash / PowerShell):
```bash
git lfs version     # "git: 'lfs' is not a git command" 나오면 → LFS 미설치(원인)
git lfs ls-files    # Main Theme.mp3, *.png 들이 목록에 나오는지
```

---

## 2단계 — 해결 (포인터만 받은 경우)

```bash
git lfs install          # ★핵심: clone 전에 이걸 안 했으면 이게 원인. 한 번만(전역 설정)
git lfs pull             # LFS 실제 파일 내려받기
git checkout -- .        # 작업트리의 '포인터'를 실제 파일로 교체 (중요)
```

> `git lfs install`을 **clone하기 전에** 안 했으면, 작업트리 파일이 껍데기로 굳어 있습니다.
> 위 3줄(설치 → pull → checkout)이면 대부분 해결됩니다. 그래도 안 되면 **LFS 설치 후 새로 clone**이 가장 확실합니다:
> ```bash
> git lfs install
> git clone https://github.com/jjy1487-ui/PASSPORT.git
> ```

### ⚠️ 그다음 Unity에서 꼭 — 다시 임포트
Unity는 이미 '깨진 포인터'를 한 번 임포트해놨기 때문에, 실제 파일로 **다시 임포트**해야 반영됩니다. 둘 중 하나:
- **Unity를 완전히 껐다가 다시 켜기**, 또는
- Project 창에서 **`Assets/Audio` 폴더 우클릭 → Reimport**

---

## 3단계 — LFS는 정상인데도 사운드가 안 들릴 때

### (a) 최신 커밋 맞는지
```bash
git log --oneline -1
```
→ **`브리핑 BGM(끊김없는 연속 재생)...`** 커밋이 보여야 합니다. 아니면:
```bash
git checkout 재영 && git pull && git lfs pull
```

### (b) 테스트한 씬이 맞는지 — ⭐ 의외로 이 경우가 많음
BGM은 **`TitleScene` / `MainMenuScene` / `BriefingScene` 에서만** 재생되도록 만들었습니다.
**심사(DayN) 씬에선 일부러 안 나옵니다.**

→ **`Assets/Scenes/TitleScene.unity` 를 열고 ▶ Play** 해서 들어보세요.
(다른 씬에서 테스트했다면 안 들리는 게 정상입니다.)

### (c) 그 외 체크
- 시스템/에디터 음소거 아닌지 (Game 뷰 우상단 🔊 Mute 버튼)
- `BgmManager` 오브젝트가 씬에 있는지 (Title/MainMenu/Briefing 각 씬 Hierarchy에 `BgmManager`)
- 스크립트 컴파일 에러 없는지 (Console 빨간 에러 확인)

---

## 참고 — 이 BGM은 어떻게 동작하나
- `BgmManager`(싱글톤, `DontDestroyOnLoad`)가 **Title → MainMenu → Briefing** 사이를 오갈 때
  **음악을 끊지 않고 연속 재생**합니다(씬마다 새로 시작 안 함).
- 그 외 씬(심사 DayN 등)으로 가면 **정지**합니다.
- 클립/볼륨은 프리팹 `Assets/Prefabs/BgmManager.prefab` 한 곳에서 관리.

---

## 한 줄 요약
**가장 흔한 원인 = `git lfs install`을 clone 전에 안 함.**
→ `git lfs install` → `git lfs pull` → `git checkout -- .` → **Unity 재시작** → `TitleScene`에서 Play.
