# 씬 와이어링 가이드 — 문서 대조 + 대화 요청 (2026-06-02)

> 우리 프로젝트가 Unity에 연결되지 않은 상태에서 C# 스크립트만 작성됨.
> **프로젝트를 Unity에서 연 뒤** 아래대로 씬(ImmigrationScene) GameObject/프리팹/바인딩을 수동 구성한다.
> 코드는 미연결 시 기존 한 덩어리 표시로 **폴백**하므로, 와이어링 전에도 day1은 깨지지 않는다.

## 추가된 스크립트 (Assets/Scripts/Inspection/)
- `DocumentFieldView.cs` — 필드 1행(라벨+값+선택 하이라이트). 클릭 시 `OnFieldClicked` 발행.
- `CrossCheckController.cs` — 카드 간 두 필드 선택 관리 + 정규화 비교(대소문자/공백/`<`/`-./:,` 무시 → "KIM<<MINJUN"↔"KIM MINJUN" 일치). 표시 전용(판정 무관).
- `CrossCheckResultView.cs` — 결과 뱃지 패널. `Show(a,b,isMatch)`/`Hide()`.
- `DialogueRequestButton.cs` — '대화/심문' 버튼. `InspectionController.OnRequestableChanged` 구독, 클릭 시 `RequestDialogue()`.
- `InspectionController.cs`(수정) — 훅만 추가: `event Action<bool> OnRequestableChanged`, `bool CanRequestDialogue`, `void RequestDialogue()`. **판정/오거부 3회 루프/점수 로직 불변.**

## 와이어링 순서
1) **FieldRow 프리팹**(클릭 가능 필드 행): 높이 ≥44px. 자식 — `Highlight`(Image, 비활성)=`_highlight`, `Label`(TMP)=`_labelText`, `Value`(TMP)=`_valueText`, 루트에 Button=`_button`. `DocumentFieldView` 부착·바인딩. 비활성으로 프리팹화.
2) **DocumentCardView 템플릿**: 펼침 영역에 `FieldContainer`(VerticalLayoutGroup+ContentSizeFitter)=`_fieldContainer` 추가, 1)의 비활성 인스턴스를 `_fieldTemplate`에 바인딩. 기존 `_bodyText`는 둬도 됨(연결 시 자동 숨김).
3) **CrossCheckResultPanel**(닫기 X 우상단): 비활성 루트=`_root`, `ComparisonText`(TMP)=`_comparisonText`, 뱃지 Image=`_badgeBackground`+TMP=`_resultBadge`, X Button=`_closeButton`. `CrossCheckResultView` 부착.
4) **CrossCheckController** GameObject: `_documentView`=씬 DocumentView, `_resultView`=3), `_enabledOnStart`=true. (선택) 도구바 '대조' 버튼 OnClick→`Toggle()`.
5) **DialogueRequestButton**: 데스크에 Button 추가, `_controller`=InspectionController, `_button`=자기 Button, `_canvasGroup`=버튼 루트 CanvasGroup(비활성 시 흐리게).

## 프로젝트 연결 후 검증 체크리스트
- [ ] 컴파일 Error 0 (`read_console`)
- [ ] day1 서류가 필드 행으로 표시 / 프리팹 미연결 시 기존 한 덩어리 폴백
- [ ] 카드 A 필드 → 카드 B 필드 클릭 → 일치/불일치 뱃지, 같은 필드 재클릭=취소
- [ ] 다중 서류 손님(day3+ 비자, day5~7 PCR)에서 **서로 다른 두 서류 간** 비교 동작
- [ ] 대조가 점수/판정에 영향 없음(JudgmentPanel·오거부 루프 그대로)
- [ ] '대화/심문' 버튼: 입장/오거부 항의 대사 재생, 재생 중·요청 케이스 없을 때 비활성

## TODO / 메모
- 이 프로젝트엔 아직 도구바·UITheme·UI 폴더가 없어, 상시 클릭선택 + 결과패널의 단순 모델 채택. 색은 시맨틱 기본값 하드코딩(Approve #2E8B57 / Reject #C0392B). 추후 UITheme 도입 시 테마 참조로 교체.
- 대조는 **보조 도구**(MVP) — 불일치를 못 찾아도 자유롭게 판정 가능. 추후 '근거 필수' 심화 시 InspectionController 판정부와 연결 필요(현재 미연결).
