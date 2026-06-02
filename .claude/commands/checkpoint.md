---
description: 지금까지 작업을 PROGRESS.md에 정리하고 git 커밋한다. /clear 후 'PROGRESS.md 읽고 이어서 작업해'로 재개 가능.
---

이 세션 작업을 "체크포인트"한다. PROGRESS.md를 **단일 핸드오프 문서**로 삼아, 다음 세션이 그것만 읽고 이어갈 수 있게 만드는 것이 목표다. 아래를 순서대로 수행하라.

## 1. PROGRESS.md 갱신
- `C:\Users\chris\Documents\produc_build_reecture\PROGRESS.md`를 읽는다.
- 이번 세션에서 한 일/완료·검증된 것/다음 단계/미해결·주의사항이 최신인지 점검하고 부족하면 보강한다.
- 새 세션이 막힘없이 이어갈 만큼 구체적으로(파일 경로, 결정 사항, 검증 결과, 함정/교훈) 적는다.

## 2. git 커밋 (프로젝트 저장소, 브랜치 `재영`)
- 프로젝트 루트 `C:\Users\chris\Documents\produc_build_reecture`에서 `git status --short`로 변경을 분류(추가/수정/삭제)한다.
- **의도치 않은 변경(무관한 대량 삭제 등)이 섞였으면 커밋 전에 사용자에게 확인**하고 분리한다.
- 한글 요약 메시지로 커밋한다. 메시지 끝에 반드시:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- **push는 사용자가 명시적으로 요청할 때만.** 기본 브랜치면 먼저 브랜치 분리.

## 3. 안내
- 커밋 해시와 한 줄 요약을 보고한다.
- 사용자에게: **"이제 `/clear` 후 'PROGRESS.md 읽고 이어서 작업해'로 재개하면 됩니다. (검증하려면 Unity에서 이 프로젝트를 열어두세요.)"** 라고 안내한다.

## 환경 메모
- Unity 씬 변경은 절대 `.unity` 파일 직접 편집하지 말고 에디터 API(mcp-unity/Build Day1 Scene)로만.
- 스크립트 수정 검증은 Play 재시작으로 새 어셈블리 반영 후.
