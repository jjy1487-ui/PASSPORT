#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

/// <summary>
/// ImmigrationScene 위에 1일차 심사 UI를 자동 생성하고 직렬 참조를 와이어링한다.
/// 기존 Background/News·Handset·Rulebook 버튼/FadePanel 은 건드리지 않는다.
/// 재실행 가능: 기존 "Day1Root" 를 지우고 다시 만든다.
/// 메뉴: Tools > Inspection > Build Day1 Scene
/// </summary>
public static class Day1SceneBuilder
{
    private const string RootName = "Day1Root";

    [MenuItem("Tools/Inspection/Build Day1 Scene")]
    public static void Build()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[Day1SceneBuilder] Canvas 를 찾을 수 없습니다.");
            return;
        }
        Transform canvasT = canvas.transform;

        // 기존 루트 제거(재실행)
        Transform old = canvasT.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        TMP_FontAsset font = FindFont();

        RectTransform root = NewUI(RootName, canvasT, Vector2.zero, Vector2.one);

        // ── 손님 영역 (왼쪽 노트 패널: 초상 + 이름 + 정보 + 대사) ──
        RectTransform custArea = NewUI("CustomerArea", root, new Vector2(0.025f, 0.20f), new Vector2(0.295f, 0.60f));
        Image portrait = AddImage(NewUI("Portrait", custArea, new Vector2(0.30f, 0.66f), new Vector2(0.70f, 0.97f)), new Color(0.6f, 0.7f, 0.85f));
        TMP_Text custName = AddText(custArea, "CustomerName", "이름", 20, new Vector2(0.02f, 0.57f), new Vector2(0.98f, 0.66f), font, TextAlignmentOptions.Center);
        custName.fontStyle = FontStyles.Bold; custName.color = new Color(0.15f, 0.15f, 0.2f);
        TMP_Text custInfo = AddText(custArea, "CustomerInfo", "정보", 14, new Vector2(0.02f, 0.49f), new Vector2(0.98f, 0.57f), font, TextAlignmentOptions.Center);
        custInfo.color = new Color(0.3f, 0.3f, 0.35f);
        CustomerView customerView = custArea.gameObject.AddComponent<CustomerView>();

        // 교차 대조용 selectable: 이름(attr=name)·얼굴(attr=face). 초상 영역에 클릭 위젯 2개.
        CrossCheckItemView custNameSel = MakeItemView(custName.rectTransform, "NameSelectable", font);
        CrossCheckItemView custFaceSel = MakeItemView(portrait.rectTransform, "FaceSelectable", font, "얼굴");
        Wire(customerView, "_nameSelectable", custNameSel);
        Wire(customerView, "_faceSelectable", custFaceSel);

        // ── 말풍선(손님 대사) — 크고 잘 보이게 ──
        RectTransform bubble = NewUI("SpeechBubble", root, new Vector2(0.30f, 0.68f), new Vector2(0.74f, 0.90f));
        AddImage(bubble, new Color(1f, 1f, 0.97f, 0.97f));
        RectTransform tail = NewUI("Tail", bubble, new Vector2(0.03f, -0.06f), new Vector2(0.11f, 0.10f));
        AddImage(tail, new Color(1f, 1f, 0.97f, 0.97f));
        tail.localRotation = Quaternion.Euler(0, 0, 45f);
        TMP_Text speaker = AddText(bubble, "SpeakerText", "화자", 20, new Vector2(0.04f, 0.74f), new Vector2(0.7f, 0.95f), font, TextAlignmentOptions.Left);
        speaker.fontStyle = FontStyles.Bold; speaker.color = new Color(0.2f, 0.35f, 0.6f);
        TMP_Text body = AddText(bubble, "BodyText", "대사", 22, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.74f), font, TextAlignmentOptions.TopLeft);
        body.color = new Color(0.1f, 0.1f, 0.12f);
        Button nextBtn = MakeButton(bubble, "NextButton", "▶", 22, new Vector2(0.88f, 0.05f), new Vector2(0.98f, 0.22f), font, new Color(0.25f, 0.45f, 0.7f));
        DialogueView dialogueView = bubble.gameObject.AddComponent<DialogueView>();
        Wire(dialogueView, "_root", bubble.gameObject);
        Wire(dialogueView, "_speakerText", speaker);
        Wire(dialogueView, "_bodyText", body);
        Wire(dialogueView, "_nextButton", nextBtn);

        // ── 우측 상단 HUD: 진행 순서만 표시 ──
        //  요구 1: 점수는 플레이 중 표시 안 함(엔딩에만) → 점수 HUD 미생성.
        //  요구 2: 돈은 상시 표시 안 함(일일 정산에만) → 골드 HUD 미생성.
        RectTransform hudBg = NewUI("HudPanel", root, new Vector2(0.78f, 0.905f), new Vector2(0.990f, 0.990f));
        AddImage(hudBg, new Color(0f, 0f, 0f, 0.62f)); // 어두운 반투명 배경

        Image peopleIcon = AddImage(NewUI("PeopleIcon", hudBg, new Vector2(0.05f, 0.10f), new Vector2(0.32f, 0.90f)), Color.white);
        peopleIcon.sprite = LoadSprite("Assets/Images/UI/people.png"); peopleIcon.preserveAspect = true;
        TMP_Text slotCounter = AddText(hudBg, "SlotCounterText", "1 / 7", 26, new Vector2(0.36f, 0.10f), new Vector2(0.97f, 0.90f), font, TextAlignmentOptions.Left);
        slotCounter.color = Color.white; slotCounter.fontStyle = FontStyles.Bold;

        // ── 좌상단 아이템 인디케이터(상시 표시, 작게) — 요구 4 ──
        //  좌상단 정렬: [오늘 날짜] 바로 아래에 배치(겹침 회피). 날짜=0.945~0.985, 아이템=0.78~0.935.
        RectTransform itemBg = NewUI("ItemIndicator", root, new Vector2(0.012f, 0.780f), new Vector2(0.175f, 0.935f));
        AddImage(itemBg, new Color(0f, 0f, 0f, 0.55f));
        TMP_Text itemTitle = AddText(itemBg, "ItemTitle", "아이템", 14, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f), font, TextAlignmentOptions.Left);
        itemTitle.color = new Color(0.85f, 0.80f, 0.55f); itemTitle.fontStyle = FontStyles.Bold;
        TMP_Text itemList = AddText(itemBg, "ItemList", "", 14, new Vector2(0.05f, 0.02f), new Vector2(0.97f, 0.84f), font, TextAlignmentOptions.TopLeft);
        itemList.color = new Color(0.95f, 0.93f, 0.85f);
        ItemIndicatorView itemView = itemBg.gameObject.AddComponent<ItemIndicatorView>();
        Wire(itemView, "_root", itemBg.gameObject);
        Wire(itemView, "_itemsText", itemList);
        itemBg.gameObject.SetActive(false); // 아이템 0개면 숨김(컴포넌트가 갱신 시 토글)

        // ── 서류 영역 (책상: 드래그로 펼치고, 거치 슬롯에 놓으면 접힘) ──
        RectTransform docArea = NewUI("DocumentArea", root, new Vector2(0.32f, 0.06f), new Vector2(0.985f, 0.66f));
        AddImage(docArea, new Color(1f, 1f, 1f, 0.03f)); // 펼침 영역(책상). 거의 투명

        // 접힘(거치) 영역 — 좌하단 슬롯. 보이지 않는 판정용 영역.
        RectTransform closeZone = NewUI("CloseZone", root, new Vector2(0.18f, 0.02f), new Vector2(0.31f, 0.17f));

        // 서류 카드 템플릿(비활성) — 펼침/접힘 두 상태 + 드래그
        // 카드 루트 크기 = 펼쳤을 때 크기. 접힌 표지(80×110)는 이 안에 작게 표시됨.
        RectTransform cardRt = NewUI("CardTemplate", docArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        cardRt.sizeDelta = new Vector2(340f, 440f);
        AddImage(cardRt, new Color(1f, 1f, 1f, 0f)); // 투명 드래그 히트영역

        // 펼친 여권(종이 + 필드)
        RectTransform openView = NewUI("OpenView", cardRt, Vector2.zero, Vector2.one);
        Image paper = AddImage(openView, new Color(0.97f, 0.96f, 0.90f));
        RectTransform fields = NewUI("Fields", openView, Vector2.zero, Vector2.one);
        VerticalLayoutGroup fv = fields.gameObject.AddComponent<VerticalLayoutGroup>();
        fv.spacing = 6; fv.padding = new RectOffset(16, 16, 14, 14);
        fv.childControlWidth = true; fv.childControlHeight = true;
        fv.childForceExpandWidth = true; fv.childForceExpandHeight = false;
        TMP_Text cardHeader = AddText(fields, "TypeHeader", "여권", 24, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        cardHeader.color = new Color(0.15f, 0.25f, 0.45f); cardHeader.fontStyle = FontStyles.Bold;
        AddLayoutElement(cardHeader.gameObject, 34);
        TMP_Text cardBody = AddText(fields, "BodyText", "항목", 17, Vector2.zero, Vector2.one, font, TextAlignmentOptions.TopLeft);
        cardBody.color = new Color(0.1f, 0.1f, 0.1f);
        AddLayoutElement(cardBody.gameObject, 360);

        // 필드 행 템플릿(클릭 대조용). 비활성 템플릿 → 런타임에 fields 컨테이너로 fields[]만큼 복제.
        RectTransform fieldRow = NewUI("FieldRowTemplate", fields, Vector2.zero, Vector2.one);
        Image fieldRowHit = AddImage(fieldRow, new Color(1f, 1f, 1f, 0f)); // 투명 raycast(버튼 타겟)
        fieldRowHit.raycastTarget = true;
        AddLayoutElement(fieldRow.gameObject, 30);
        Image fieldHighlight = AddImage(NewUI("Highlight", fieldRow, Vector2.zero, Vector2.one), new Color(0.231f, 0.510f, 0.769f, 0.45f));
        fieldHighlight.raycastTarget = false;
        fieldHighlight.gameObject.SetActive(false);
        TMP_Text fieldLabel = AddText(fieldRow, "Label", "라벨", 15, new Vector2(0f, 0f), new Vector2(0.46f, 1f), font, TextAlignmentOptions.Left);
        fieldLabel.color = new Color(0.30f, 0.30f, 0.35f); fieldLabel.raycastTarget = false;
        TMP_Text fieldValue = AddText(fieldRow, "Value", "값", 15, new Vector2(0.46f, 0f), new Vector2(1f, 1f), font, TextAlignmentOptions.Left);
        fieldValue.color = new Color(0.1f, 0.1f, 0.1f); fieldValue.fontStyle = FontStyles.Bold; fieldValue.raycastTarget = false;
        Button fieldBtn = fieldRow.gameObject.AddComponent<Button>();
        fieldBtn.targetGraphic = fieldRowHit;
        DocumentFieldView fieldView = fieldRow.gameObject.AddComponent<DocumentFieldView>();
        Wire(fieldView, "_labelText", fieldLabel);
        Wire(fieldView, "_valueText", fieldValue);
        Wire(fieldView, "_button", fieldBtn);
        Wire(fieldView, "_highlight", fieldHighlight);
        fieldRow.gameObject.SetActive(false); // 템플릿은 비활성(복제본만 활성)

        // 도장 자국(펼친 여권 종이 위, 비활성)
        RectTransform stampRt = NewUI("StampImprint", openView, new Vector2(0.10f, 0.28f), new Vector2(0.90f, 0.58f));
        TMP_Text stampText = AddText(stampRt, "StampText", "입국 허가", 40, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        stampText.fontStyle = FontStyles.Bold;
        stampText.outlineWidth = 0.25f; stampText.outlineColor = new Color32(20, 20, 20, 220);
        stampRt.localRotation = Quaternion.Euler(0, 0, -12f);
        stampRt.gameObject.SetActive(false);

        // 접힌 표지(국가별 여권 이미지; 없으면 갈색 책자)
        RectTransform closedView = NewUI("ClosedView", cardRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        closedView.sizeDelta = new Vector2(80f, 110f);
        Image closedImg = AddImage(closedView, new Color(0.45f, 0.30f, 0.18f));
        TMP_Text closedLabel = AddText(closedView, "ClosedLabel", "여권", 14, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        closedLabel.color = new Color(0.95f, 0.9f, 0.8f); closedLabel.fontStyle = FontStyles.Bold;

        PassportDocument pass = cardRt.gameObject.AddComponent<PassportDocument>();
        DocumentCardView cardView = cardRt.gameObject.AddComponent<DocumentCardView>();
        Wire(cardView, "_background", paper);
        Wire(cardView, "_typeHeader", cardHeader);
        Wire(cardView, "_bodyText", cardBody);
        Wire(cardView, "_fieldContainer", fields);
        Wire(cardView, "_fieldTemplate", fieldView);
        Wire(cardView, "_closedImage", closedImg);
        Wire(cardView, "_closedLabel", closedLabel);
        Wire(cardView, "_stampRoot", stampRt.gameObject);
        Wire(cardView, "_stampText", stampText);
        Wire(cardView, "_coverKor", LoadSprite("Assets/Images/Passports/passport_kor.png"));
        Wire(cardView, "_coverChn", LoadSprite("Assets/Images/Passports/passport_chn.png"));
        Wire(cardView, "_coverJpn", LoadSprite("Assets/Images/Passports/passport_jpn.png"));
        Wire(cardView, "_coverUsa", LoadSprite("Assets/Images/Passports/passport_usa.png"));
        Wire(pass, "_openView", openView.gameObject);
        Wire(pass, "_closedView", closedView.gameObject);
        Wire(pass, "_openZone", docArea);
        cardRt.gameObject.SetActive(false);

        DocumentView documentView = docArea.gameObject.AddComponent<DocumentView>();
        Wire(documentView, "_cardContainer", docArea);
        Wire(documentView, "_cardTemplate", cardView);
        Wire(documentView, "_restSlot", closeZone);

        // ── 판정(도장) ──
        RectTransform judge = NewUI("JudgmentPanel", root, Vector2.zero, Vector2.one);

        // 드로어: 배경에 그려진 측면 탭 위에 얹는 투명 버튼(실제 탭 아트가 그대로 보임)
        RectTransform drawerRt = NewUI("DrawerButton", judge, new Vector2(0.965f, 0.43f), new Vector2(0.998f, 0.55f));
        Image drawerImg = AddImage(drawerRt, new Color(1f, 1f, 1f, 0f)); // 투명, 클릭 영역만
        Button drawerBtn = drawerRt.gameObject.AddComponent<Button>();
        drawerBtn.targetGraphic = drawerImg;

        // 도장 트레이(세로: 도장 2개 행 + 안내문). 비활성 시작.
        RectTransform tray = NewUI("StampTray", judge, new Vector2(0.36f, 0.16f), new Vector2(0.80f, 0.40f));
        AddImage(tray, new Color(0.12f, 0.10f, 0.09f, 0.92f));
        VerticalLayoutGroup tv = tray.gameObject.AddComponent<VerticalLayoutGroup>();
        tv.spacing = 8; tv.padding = new RectOffset(16, 16, 12, 12);
        tv.childControlWidth = true; tv.childControlHeight = true;
        tv.childForceExpandWidth = true; tv.childForceExpandHeight = false;
        RectTransform row = NewUI("StampRow", tray, Vector2.zero, Vector2.one);
        HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16; hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
        AddLayoutElement(row.gameObject, 90);
        Button rejectBtn = MakeButton(row, "RejectStamp", "입국 거부", 28, Vector2.zero, Vector2.one, font, new Color(0.62f, 0.16f, 0.13f));
        Button approveBtn = MakeButton(row, "ApproveStamp", "입국 허가", 28, Vector2.zero, Vector2.one, font, new Color(0.40f, 0.55f, 0.18f));
        TMP_Text stampHint = AddText(tray, "StampHint", "여권을 도장 아래에 두십시오", 18, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        stampHint.color = new Color(1f, 0.92f, 0.7f);
        AddLayoutElement(stampHint.gameObject, 28);
        tray.gameObject.SetActive(false);
        JudgmentPanel judgment = judge.gameObject.AddComponent<JudgmentPanel>();
        Wire(judgment, "_drawerButton", drawerBtn);
        Wire(judgment, "_stampTray", tray.gameObject);
        Wire(judgment, "_approveStampButton", approveBtn);
        Wire(judgment, "_rejectStampButton", rejectBtn);

        // ── 뉴스 팝업 ──
        NewsPopup newsPopup = BuildListPopup<NewsPopup>(root, "NewsPanel", "뉴스", font, out RectTransform newsRt);
        // 뉴스 claim 단서 컨테이너 + 템플릿(우하단 영역)
        BuildClaimList(newsRt, font, out Transform newsClaimC, out CrossCheckItemView newsClaimTpl);
        Wire(newsPopup, "_claimContainer", newsClaimC);
        Wire(newsPopup, "_claimTemplate", newsClaimTpl);

        // ── 규정집 팝업 ──
        RulebookPopup rulePopup = BuildListPopup<RulebookPopup>(root, "RulebookPanel", "규정집", font, out RectTransform ruleRt);
        // 규정 1건을 대조 항목으로(관련성). 하단 영역에 단일 위젯.
        RectTransform ruleSelArea = NewUI("RuleSelectable", ruleRt, new Vector2(0.22f, 0.16f), new Vector2(0.78f, 0.21f));
        CrossCheckItemView ruleSel = MakeItemView(ruleSelArea, "Item", font, "규정");
        ruleSel.gameObject.SetActive(false);
        Wire(rulePopup, "_ruleSelectable", ruleSel);

        // ── 대화 기록 팝업 — DocumentArea 위에 꽉 채워 표시 ──
        RectTransform logRt = NewUI("DialogueLogPanel", docArea, Vector2.zero, Vector2.one);
        AddImage(logRt, new Color(0.08f, 0.10f, 0.14f, 0.97f));
        TMP_Text logTitle = AddText(logRt, "Title", "대화 기록", 26, new Vector2(0.04f, 0.88f), new Vector2(0.82f, 0.98f), font, TextAlignmentOptions.Left);
        logTitle.fontStyle = FontStyles.Bold; logTitle.color = new Color(1f, 0.9f, 0.6f);
        Button logClose = MakeButton(logRt, "CloseButton", "X", 22, new Vector2(0.88f, 0.89f), new Vector2(0.99f, 0.99f), font, new Color(0.60f, 0.18f, 0.18f));
        // 일반 라인(비-단서)은 상단, 단서 위젯은 하단 컨테이너에 쌓는다.
        TMP_Text logText = AddText(logRt, "LogText", "", 17, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.87f), font, TextAlignmentOptions.TopLeft);
        logText.color = Color.white;
        // claim 단서 컨테이너 + 템플릿
        RectTransform logClaimArea = NewUI("ClaimContainer", logRt, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.44f));
        VerticalLayoutGroup lcv = logClaimArea.gameObject.AddComponent<VerticalLayoutGroup>();
        lcv.spacing = 6; lcv.padding = new RectOffset(4, 4, 4, 4);
        lcv.childControlWidth = true; lcv.childControlHeight = true;
        lcv.childForceExpandWidth = true; lcv.childForceExpandHeight = false;
        CrossCheckItemView logClaimTpl = MakeItemView(NewUI("ClaimTemplate", logClaimArea, Vector2.zero, Vector2.one), "Item", font, "단서");
        AddLayoutElement(logClaimTpl.gameObject, 36);
        logClaimTpl.gameObject.SetActive(false);
        DialogueLogPopup logPopup = logRt.gameObject.AddComponent<DialogueLogPopup>();
        Wire(logPopup, "_root", logRt.gameObject);
        Wire(logPopup, "_logText", logText);
        Wire(logPopup, "_closeButton", logClose);
        Wire(logPopup, "_claimContainer", logClaimArea);
        Wire(logPopup, "_claimTemplate", logClaimTpl);
        logRt.gameObject.SetActive(false);

        // ── 헤드셋 — 기존 HandsetButton 이미지를 headset.png 스프라이트로 교체 ──
        // 헤드셋 드래그(DraggableHandset)는 기존 HandsetButton 위에 얹어서 드래그 가능하게.
        var handsetButtonT = canvasT.Find("HandsetButton");
        DraggableHandset draggableHandset = null;
        if (handsetButtonT != null)
        {
            Sprite headsetSprite = LoadSprite("Assets/Images/UI/headset.png");
            var handsetImg = handsetButtonT.GetComponent<Image>();
            if (headsetSprite != null && handsetImg != null)
            {
                handsetImg.sprite = headsetSprite;
                handsetImg.color = Color.white;
                handsetImg.preserveAspect = true;
            }
            // 기존 버튼 onClick 제거하고 DraggableHandset 부착
            var existBtn = handsetButtonT.GetComponent<Button>();
            if (existBtn != null) UnityEventTools.RemovePersistentListener(existBtn.onClick, 0);
            draggableHandset = handsetButtonT.GetComponent<DraggableHandset>();
            if (draggableHandset == null) draggableHandset = handsetButtonT.gameObject.AddComponent<DraggableHandset>();
            Wire(draggableHandset, "_dropZone", docArea);
            Wire(draggableHandset, "_logPopup", logPopup);
        }

        // ── 일자 완료(일일 정산) 패널 — 요구 2: 돈은 여기서만 노출(점수는 넣지 않음) ──
        RectTransform dayDone = NewUI("DayCompletePanel", root, new Vector2(0.33f, 0.34f), new Vector2(0.67f, 0.66f));
        AddImage(dayDone, new Color(0f, 0f, 0f, 0.85f));
        AddText(dayDone, "DoneText", "일자 완료", 40, new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.95f), font, TextAlignmentOptions.Center);

        AddText(dayDone, "EarnedLabel", "오늘 번 돈", 20, new Vector2(0.10f, 0.52f), new Vector2(0.50f, 0.66f), font, TextAlignmentOptions.Left)
            .color = new Color(0.85f, 0.82f, 0.6f);
        TMP_Text earnedText = AddText(dayDone, "EarnedValue", "+0", 26, new Vector2(0.50f, 0.52f), new Vector2(0.90f, 0.66f), font, TextAlignmentOptions.Right);
        earnedText.color = new Color(0.831f, 0.627f, 0.090f); earnedText.fontStyle = FontStyles.Bold; // Money/Gold #D4A017

        AddText(dayDone, "BalanceLabel", "누적 잔액", 20, new Vector2(0.10f, 0.36f), new Vector2(0.50f, 0.50f), font, TextAlignmentOptions.Left)
            .color = new Color(0.85f, 0.82f, 0.6f);
        TMP_Text balanceText = AddText(dayDone, "BalanceValue", "0", 26, new Vector2(0.50f, 0.36f), new Vector2(0.90f, 0.50f), font, TextAlignmentOptions.Right);
        balanceText.color = new Color(0.831f, 0.627f, 0.090f); balanceText.fontStyle = FontStyles.Bold;

        DaySettlementView settlement = dayDone.gameObject.AddComponent<DaySettlementView>();
        Wire(settlement, "_earnedText", earnedText);
        Wire(settlement, "_balanceText", balanceText);

        // "다음 날" 진행 버튼 — 패널 우하단(진행 버튼 관례). onClick 은 mgr 발견 후 아래에서 바인딩.
        // 패널 토글은 게임플레이가 처리하므로 버튼은 SetActive 를 만지지 않는다.
        Button nextDayBtn = MakeButton(dayDone, "NextDayButton", "다음 날", 24,
            new Vector2(0.60f, 0.06f), new Vector2(0.90f, 0.22f), font, new Color(0.231f, 0.510f, 0.769f)); // Score/Blue 계열 진행색
        // _controller 는 컨트롤러 생성 후 아래에서 와이어링한다.
        dayDone.gameObject.SetActive(false);

        // ── 컨트롤러 ──
        RectTransform ctrlObj = NewUI("InspectionController", root, Vector2.zero, Vector2.zero);
        InspectionController controller = ctrlObj.gameObject.AddComponent<InspectionController>();
        Wire(controller, "_customerView", customerView);
        Wire(controller, "_documentView", documentView);
        Wire(controller, "_dialogueView", dialogueView);
        Wire(controller, "_judgmentPanel", judgment);
        Wire(controller, "_slotCounterText", slotCounter);
        // 골드 상시 HUD 제거(요구 2) — _goldText 는 와이어링하지 않는다(컨트롤러 null 체크로 무시됨).
        if (draggableHandset != null) Wire(draggableHandset, "_controller", controller); // 헤드셋 → 컨트롤러 후기 와이어링
        Wire(controller, "_dayCompleteRoot", dayDone.gameObject);
        Wire(settlement, "_controller", controller); // 일일 정산 → 일자 완료 통지 구독

        Wire(customerView, "_portraitPlaceholder", portrait);
        Wire(customerView, "_nameText", custName);
        Wire(customerView, "_infoText", custInfo);

        // ── 호칭·아이템 획득 피드백(토스트 + 보유 목록 패널) ──
        BuildRewardFeedback(root, font);

        // ── 엔딩 화면(조기/누적/점수구간 모두) — 강제 모달, 닫기 X 우상단 ──
        BuildEndingPanel(root, font);

        // ── 고급 분기 선택지 패널(범죄자 뇌물/테러 상담/사이비/연예인·정치인) ──
        BuildAdvancedBranchPanel(root, controller, font);

        // ── 보조검사기(X-ray / 지문) — 버튼 2개(데스크 도구 영역) + 결과 패널 2개 ──
        ScanResultPanel xrayPanel = BuildScanPanel(root, "XrayPanel", "X-ray 검사",
            ScanResultPanel.ScanKind.Xray, controller, font);
        ScanResultPanel fpPanel = BuildScanPanel(root, "FingerprintPanel", "지문 대조",
            ScanResultPanel.ScanKind.Fingerprint, controller, font);

        // 검사기 버튼(데스크 도구 영역 — 헤드셋/뉴스/규정 버튼 근처, 화면 좌하단 도구바).
        // 기본 잠금 — _crossCheck 참조는 컨트롤러 생성 후 아래에서 와이어링한다.
        ScanRequestButton xrayBtn = BuildScanButton(root, "XrayButton", "X-ray", new Vector2(0.015f, 0.025f), new Vector2(0.075f, 0.085f),
            controller, xrayPanel, font, new Color(0.20f, 0.35f, 0.55f));
        ScanRequestButton fpBtn = BuildScanButton(root, "FingerprintButton", "지문", new Vector2(0.085f, 0.025f), new Vector2(0.145f, 0.085f),
            controller, fpPanel, font, new Color(0.30f, 0.30f, 0.40f));

        // ── 오늘 날짜(좌측하단, 작게) + 클릭 대조 소스 ──
        TodayDateView todayView = BuildTodayDateView(root, controller, font);

        // ── 교차 대조(연결선 + 컨트롤러) ──
        // 연결선 연출(점선 + 깜빡임). 최상단에 그려져야 하므로 root 마지막 자식.
        RectTransform xcRoot = NewUI("CrossCheckConnector", root, Vector2.zero, Vector2.one);
        RectTransform dash = NewUI("Dash", xcRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        Image dashImg = AddImage(dash, new Color(0.231f, 0.510f, 0.769f));
        dash.sizeDelta = new Vector2(10f, 3f);
        TMP_Text xcLabel = AddText(xcRoot, "ResultLabel", "", 22, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), font, TextAlignmentOptions.Center);
        xcLabel.rectTransform.sizeDelta = new Vector2(160f, 40f);
        xcLabel.fontStyle = FontStyles.Bold;
        CrossCheckConnectorView connector = xcRoot.gameObject.AddComponent<CrossCheckConnectorView>();
        Wire(connector, "_root", xcRoot);
        Wire(connector, "_dashLine", dash);
        Wire(connector, "_dashImage", dashImg);
        Wire(connector, "_resultLabel", xcLabel);
        Wire(connector, "_canvasRect", canvas.GetComponent<RectTransform>());
        xcRoot.gameObject.SetActive(false);

        RectTransform xcObj = NewUI("CrossCheckController", root, Vector2.zero, Vector2.zero);
        CrossCheckController crossCheck = xcObj.gameObject.AddComponent<CrossCheckController>();
        Wire(crossCheck, "_documentView", documentView);
        Wire(crossCheck, "_connectorView", connector);
        // 추가 공급자: 캐릭터/뉴스/규정/대화/보조검사(X-ray·지문)(ICrossCheckProvider 구현 MonoBehaviour 배열).
        WireArray(crossCheck, "_extraProviders", new Component[] { customerView, newsPopup, rulePopup, logPopup, xrayPanel, fpPanel, todayView });

        // 스캔 버튼 잠금 해제 트리거 소스 와이어링(컨트롤러 생성 후).
        if (xrayBtn != null) Wire(xrayBtn, "_crossCheck", crossCheck);
        if (fpBtn != null) Wire(fpBtn, "_crossCheck", crossCheck);

        // ── ImmigrationManager 와이어링 + 버튼 onClick ──
        ImmigrationManager mgr = Object.FindFirstObjectByType<ImmigrationManager>();
        if (mgr != null)
        {
            Wire(mgr, "inspectionController", controller);
            Wire(mgr, "newsPopup", newsPopup);
            Wire(mgr, "rulebookPopup", rulePopup);
            Wire(mgr, "dialogueLogPopup", logPopup);
            BindButton(canvasT, "HandsetButton", mgr, "OnHandsetButton"); // 폴백 클릭 유지(드래그 우선)
            BindButton(canvasT, "NewsButton", mgr, "OnNewsButton");
            BindButton(canvasT, "RulebookButton", mgr, "OnRulebookButton");
            BindButton(canvasT, "HandsetButton", mgr, "OnHandsetButton");
            // 일자완료 패널의 "다음 날" 버튼(패널 하위에 중첩되어 이름 검색 대신 직접 참조로 바인딩).
            BindButtonDirect(nextDayBtn, mgr, "OnNextDayButton");
        }
        else
        {
            Debug.LogWarning("[Day1SceneBuilder] ImmigrationManager 를 찾지 못해 팝업/버튼 와이어링을 건너뜁니다.");
        }

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Debug.Log("[Day1SceneBuilder] 완료: Day1Root 생성 및 와이어링.");
    }

    // 제목/본문/페이지/이전·다음·닫기 구조의 리스트 팝업 생성
    private static T BuildListPopup<T>(Transform parent, string name, string titleDefault, TMP_FontAsset font, out RectTransform rt)
        where T : Component
    {
        rt = NewUI(name, parent, new Vector2(0.28f, 0.25f), new Vector2(0.72f, 0.75f));
        AddImage(rt, new Color(0.1f, 0.12f, 0.16f, 0.95f));
        TMP_Text title = AddText(rt, "Title", titleDefault, 30, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.96f), font, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold; title.color = new Color(1f, 0.9f, 0.6f);
        TMP_Text content = AddText(rt, "Content", "내용", 20, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.8f), font, TextAlignmentOptions.TopLeft);
        content.color = Color.white;
        TMP_Text page = AddText(rt, "Page", "1 / 2", 18, new Vector2(0.4f, 0.05f), new Vector2(0.6f, 0.15f), font, TextAlignmentOptions.Center);
        page.color = new Color(0.8f, 0.8f, 0.8f);
        Button prev = MakeButton(rt, "PrevButton", "◀", 24, new Vector2(0.06f, 0.05f), new Vector2(0.2f, 0.16f), font, new Color(0.3f, 0.35f, 0.45f));
        Button next = MakeButton(rt, "NextButton", "▶", 24, new Vector2(0.8f, 0.05f), new Vector2(0.94f, 0.16f), font, new Color(0.3f, 0.35f, 0.45f));
        Button close = MakeButton(rt, "CloseButton", "닫기", 22, new Vector2(0.82f, 0.84f), new Vector2(0.97f, 0.95f), font, new Color(0.5f, 0.25f, 0.25f));

        T comp = rt.gameObject.AddComponent<T>();
        Wire(comp, "_root", rt.gameObject);
        Wire(comp, "_titleText", title);
        Wire(comp, "_contentText", content);
        Wire(comp, "_pageText", page);
        Wire(comp, "_prevButton", prev);
        Wire(comp, "_nextButton", next);
        Wire(comp, "_closeButton", close);
        rt.gameObject.SetActive(false);
        return comp;
    }

    // 클릭 가능한 대조 항목 위젯(라벨 + 버튼 + 선택 하이라이트)을 만든다.
    private static CrossCheckItemView MakeItemView(RectTransform parent, string name, TMP_FontAsset font, string label = "")
    {
        RectTransform rt = NewUI(name, parent, Vector2.zero, Vector2.one);
        // 선택 하이라이트(비활성 시작)
        RectTransform hi = NewUI("Highlight", rt, Vector2.zero, Vector2.one);
        Image hiImg = AddImage(hi, new Color(0.231f, 0.510f, 0.769f, 0.45f));
        hi.gameObject.SetActive(false);
        // 라벨
        TMP_Text t = AddText(rt, "Label", label, 16, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        t.color = Color.white;
        // 투명 버튼(클릭 영역)
        Image clickImg = AddImage(NewUI("Hit", rt, Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0f));
        Button btn = clickImg.gameObject.AddComponent<Button>();
        btn.targetGraphic = clickImg;

        CrossCheckItemView v = rt.gameObject.AddComponent<CrossCheckItemView>();
        Wire(v, "_labelText", t);
        Wire(v, "_button", btn);
        Wire(v, "_highlight", hiImg);
        return v;
    }

    // 팝업 하단에 claim 단서 컨테이너(VerticalLayoutGroup)와 비활성 템플릿을 만든다.
    private static void BuildClaimList(RectTransform popup, TMP_FontAsset font, out Transform container, out CrossCheckItemView template)
    {
        RectTransform area = NewUI("ClaimContainer", popup, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.2f));
        VerticalLayoutGroup vlg = area.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6; vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        CrossCheckItemView tpl = MakeItemView(NewUI("ClaimTemplate", area, Vector2.zero, Vector2.one), "Item", font, "단서");
        AddLayoutElement(tpl.gameObject, 34);
        tpl.gameObject.SetActive(false);
        container = area;
        template = tpl;
    }

    // 보조검사 결과 패널(제목/결과/세부/추가 + 닫기 X + claim 대조 항목 1개). 비활성 시작.
    private static ScanResultPanel BuildScanPanel(Transform parent, string name, string titleDefault,
        ScanResultPanel.ScanKind kind, InspectionController controller, TMP_FontAsset font)
    {
        RectTransform rt = NewUI(name, parent, new Vector2(0.30f, 0.30f), new Vector2(0.70f, 0.70f));
        AddImage(rt, new Color(0.10f, 0.12f, 0.16f, 0.96f));

        TMP_Text title = AddText(rt, "Title", titleDefault, 30, new Vector2(0.05f, 0.84f), new Vector2(0.82f, 0.96f), font, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold; title.color = new Color(0.55f, 0.85f, 1f);
        // 닫기 = X, 우상단(UI 규약)
        Button close = MakeButton(rt, "CloseButton", "X", 22, new Vector2(0.88f, 0.85f), new Vector2(0.97f, 0.96f), font, new Color(0.60f, 0.18f, 0.18f));

        TMP_Text result = AddText(rt, "ResultText", "결과: -", 24, new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.80f), font, TextAlignmentOptions.Left);
        result.color = new Color(1f, 0.85f, 0.5f); result.fontStyle = FontStyles.Bold;
        TMP_Text detail = AddText(rt, "DetailText", "-", 20, new Vector2(0.06f, 0.52f), new Vector2(0.94f, 0.66f), font, TextAlignmentOptions.Left);
        detail.color = Color.white;
        TMP_Text extra = AddText(rt, "ExtraText", "-", 20, new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.52f), font, TextAlignmentOptions.Left);
        extra.color = Color.white;

        // claim 대조 항목 1개(하단 중앙). 비활성 시작 → 데이터에 claim 있을 때만 노출.
        RectTransform claimArea = NewUI("ClaimSelectable", rt, new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.20f));
        CrossCheckItemView claimSel = MakeItemView(claimArea, "Item", font, "단서");
        claimSel.gameObject.SetActive(false);

        ScanResultPanel panel = rt.gameObject.AddComponent<ScanResultPanel>();
        Wire(panel, "_controller", controller);
        WireEnum(panel, "_kind", (int)kind);
        Wire(panel, "_root", rt.gameObject);
        Wire(panel, "_titleText", title);
        Wire(panel, "_resultText", result);
        Wire(panel, "_detailText", detail);
        Wire(panel, "_extraText", extra);
        Wire(panel, "_closeButton", close);
        Wire(panel, "_claimSelectable", claimSel);
        rt.gameObject.SetActive(false);
        return panel;
    }

    // 검사기 버튼(토글). 기본 잠금 — 데이터 없거나 미해제 시 흐리게(CanvasGroup) + 비활성.
    // _crossCheck 는 컨트롤러 생성 후 별도로 와이어링한다(생성 순서 의존).
    private static ScanRequestButton BuildScanButton(Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, InspectionController controller, ScanResultPanel panel,
        TMP_FontAsset font, Color bg)
    {
        Button btn = MakeButton(parent, name, label, 18, anchorMin, anchorMax, font, bg);
        CanvasGroup cg = btn.gameObject.AddComponent<CanvasGroup>();
        ScanRequestButton srb = btn.gameObject.AddComponent<ScanRequestButton>();
        Wire(srb, "_controller", controller);
        Wire(srb, "_panel", panel);
        Wire(srb, "_button", btn);
        Wire(srb, "_canvasGroup", cg);
        return srb;
    }

    // 오늘 날짜 패널(좌상단 최상단, 작게) + 클릭 대조 소스(attr="today").
    //  좌상단 정렬: 화면 최상단 좌측. 바로 아래에 아이템 인디케이터(0.78~0.935)가 온다(겹침 회피).
    private static TodayDateView BuildTodayDateView(Transform parent, InspectionController controller, TMP_FontAsset font)
    {
        RectTransform rt = NewUI("TodayDatePanel", parent, new Vector2(0.012f, 0.945f), new Vector2(0.150f, 0.985f));
        AddImage(rt, new Color(0.10f, 0.12f, 0.16f, 0.85f));

        // 날짜 텍스트(작게, 한 줄)
        TMP_Text dateText = AddText(rt, "DateText", "오늘: -", 14, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        dateText.color = new Color(0.95f, 0.92f, 0.7f);

        // 클릭 대조 selectable: 패널 전체를 클릭 영역으로(SourceType="오늘", attr="today").
        CrossCheckItemView todaySel = MakeItemView(rt, "TodaySelectable", font, "");

        TodayDateView view = rt.gameObject.AddComponent<TodayDateView>();
        Wire(view, "_controller", controller);
        Wire(view, "_dateText", dateText);
        Wire(view, "_todaySelectable", todaySel);
        return view;
    }

    // 보유 아이템 목록 패널(열기 버튼 좌하단 도구바 근처). 닫기 = X 우상단.
    //  호칭 토스트/아이템 토스트는 제거됨(요구 3·4). 호칭은 메인 메뉴 업적 패널, 아이템은 좌상단 상시 위젯.
    private static void BuildRewardFeedback(Transform parent, TMP_FontAsset font)
    {
        // 보유 목록 열기 버튼(좌상단, 아이템 인디케이터 바로 아래로 이동 — 아이템 관련 UI를 좌상단에 모음).
        //  날짜=0.945~0.985, 아이템 위젯=0.78~0.935, '보유' 버튼=0.735~0.775(겹침 회피, 세로 정렬).
        Button openBtn = MakeButton(parent, "InventoryButton", "보유", 16, new Vector2(0.012f, 0.735f), new Vector2(0.095f, 0.775f), font, new Color(0.30f, 0.28f, 0.20f));

        // 보유 목록 패널(중앙, 비활성 시작)
        RectTransform listRt = NewUI("InventoryPanel", parent, new Vector2(0.32f, 0.25f), new Vector2(0.68f, 0.75f));
        AddImage(listRt, new Color(0.10f, 0.12f, 0.16f, 0.96f));
        TMP_Text listTitle = AddText(listRt, "Title", "보유 아이템", 28, new Vector2(0.05f, 0.86f), new Vector2(0.80f, 0.97f), font, TextAlignmentOptions.Left);
        listTitle.fontStyle = FontStyles.Bold; listTitle.color = new Color(1f, 0.9f, 0.6f);
        Button listClose = MakeButton(listRt, "CloseButton", "X", 22, new Vector2(0.88f, 0.86f), new Vector2(0.97f, 0.97f), font, new Color(0.60f, 0.18f, 0.18f));
        TMP_Text listText = AddText(listRt, "ListText", "", 18, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.84f), font, TextAlignmentOptions.TopLeft);
        listText.color = Color.white;
        listRt.gameObject.SetActive(false);

        // 컴포넌트는 전용 호스트에 부착(패널이 꺼져도 살아있어야 함).
        GameObject host = new GameObject("RewardFeedback", typeof(RectTransform));
        host.transform.SetParent(parent, false);
        RewardFeedbackView view = host.AddComponent<RewardFeedbackView>();
        Wire(view, "_listRoot", listRt.gameObject);
        Wire(view, "_listText", listText);
        Wire(view, "_openButton", openBtn);
        Wire(view, "_closeButton", listClose);
    }

    // 엔딩 패널(강제 모달). 엔딩명/유형배지/설명+최종점수/호칭, 닫기 X 우상단.
    private static void BuildEndingPanel(Transform parent, TMP_FontAsset font)
    {
        // 전체 화면 Backdrop(강제 모달 — 클릭으로 안 닫힘)
        RectTransform rt = NewUI("EndingPanel", parent, Vector2.zero, Vector2.one);
        AddImage(rt, new Color(0.02f, 0.03f, 0.05f, 0.92f));

        RectTransform frame = NewUI("Frame", rt, new Vector2(0.25f, 0.22f), new Vector2(0.75f, 0.78f));
        AddImage(frame, new Color(0.10f, 0.12f, 0.16f, 0.98f));

        TMP_Text typeBadge = AddText(frame, "TypeBadge", "엔딩", 20, new Vector2(0.06f, 0.84f), new Vector2(0.50f, 0.94f), font, TextAlignmentOptions.Left);
        typeBadge.color = new Color(0.55f, 0.85f, 1f); typeBadge.fontStyle = FontStyles.Bold;
        Button close = MakeButton(frame, "CloseButton", "X", 22, new Vector2(0.88f, 0.85f), new Vector2(0.97f, 0.95f), font, new Color(0.60f, 0.18f, 0.18f));
        TMP_Text nameText = AddText(frame, "EndingName", "엔딩명", 40, new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.82f), font, TextAlignmentOptions.Center);
        nameText.color = new Color(1f, 0.9f, 0.6f); nameText.fontStyle = FontStyles.Bold;
        TMP_Text descText = AddText(frame, "EndingDesc", "", 20, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.60f), font, TextAlignmentOptions.TopLeft);
        descText.color = Color.white;

        EndingPanel panel = rt.gameObject.AddComponent<EndingPanel>();
        Wire(panel, "_root", rt.gameObject);
        Wire(panel, "_nameText", nameText);
        Wire(panel, "_descText", descText);
        Wire(panel, "_typeBadgeText", typeBadge);
        Wire(panel, "_closeButton", close);
        rt.gameObject.SetActive(false);
    }

    // 고급 분기 선택지 패널(좌=긍정/수령, 우=거부/신고; 닫기 X 우상단). 컨트롤러 OnCustomerChanged 로 자동 표시.
    private static void BuildAdvancedBranchPanel(Transform parent, InspectionController controller, TMP_FontAsset font)
    {
        RectTransform rt = NewUI("AdvancedBranchPanel", parent, new Vector2(0.34f, 0.42f), new Vector2(0.78f, 0.64f));
        AddImage(rt, new Color(0.12f, 0.10f, 0.14f, 0.96f));

        TMP_Text prompt = AddText(rt, "Prompt", "", 20, new Vector2(0.05f, 0.55f), new Vector2(0.82f, 0.92f), font, TextAlignmentOptions.TopLeft);
        prompt.color = new Color(1f, 0.92f, 0.8f);
        Button close = MakeButton(rt, "CloseButton", "X", 22, new Vector2(0.88f, 0.80f), new Vector2(0.97f, 0.93f), font, new Color(0.50f, 0.25f, 0.25f));

        // 좌 = 긍정/수령(승인 그린 톤), 우 = 거부/신고(거절 레드 톤) — 통과=좌/거절=우 규약.
        Button left = MakeButton(rt, "LeftChoice", "선택", 22, new Vector2(0.05f, 0.10f), new Vector2(0.48f, 0.42f), font, new Color(0.18f, 0.45f, 0.30f));
        Button right = MakeButton(rt, "RightChoice", "선택", 22, new Vector2(0.52f, 0.10f), new Vector2(0.95f, 0.42f), font, new Color(0.62f, 0.16f, 0.13f));
        TMP_Text leftLabel = left.transform.Find("Label").GetComponent<TMP_Text>();
        TMP_Text rightLabel = right.transform.Find("Label").GetComponent<TMP_Text>();

        AdvancedBranchPanel panel = rt.gameObject.AddComponent<AdvancedBranchPanel>();
        Wire(panel, "_controller", controller);
        Wire(panel, "_root", rt.gameObject);
        Wire(panel, "_promptText", prompt);
        Wire(panel, "_leftButton", left);
        Wire(panel, "_leftLabel", leftLabel);
        Wire(panel, "_rightButton", right);
        Wire(panel, "_rightLabel", rightLabel);
        Wire(panel, "_closeButton", close);
        rt.gameObject.SetActive(false);
    }

    // ───────── 헬퍼 ─────────

    private static RectTransform NewUI(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Image AddImage(RectTransform rt, Color color)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static void AddLayoutElement(GameObject go, float preferredHeight)
    {
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
    }

    private static TMP_Text AddText(Transform parent, string name, string text, float size,
        Vector2 anchorMin, Vector2 anchorMax, TMP_FontAsset font, TextAlignmentOptions align)
    {
        RectTransform rt = NewUI(name, parent, anchorMin, anchorMax);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align;
        t.color = Color.black;
        if (font != null) t.font = font;
        return t;
    }

    private static Button MakeButton(Transform parent, string name, string label, float size,
        Vector2 anchorMin, Vector2 anchorMax, TMP_FontAsset font, Color bg)
    {
        RectTransform rt = NewUI(name, parent, anchorMin, anchorMax);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        TMP_Text t = AddText(rt, "Label", label, size, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        t.color = Color.white;
        return btn;
    }

    private static void Wire(Component comp, string fieldName, Object value)
    {
        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[Day1SceneBuilder] {comp.GetType().Name}.{fieldName} 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireEnum(Component comp, string fieldName, int enumValue)
    {
        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[Day1SceneBuilder] {comp.GetType().Name}.{fieldName} 열거형 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.enumValueIndex = enumValue;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireArray(Component comp, string fieldName, Component[] values)
    {
        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null || !prop.isArray)
        {
            Debug.LogWarning($"[Day1SceneBuilder] {comp.GetType().Name}.{fieldName} 배열 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BindButton(Transform canvasT, string buttonName, ImmigrationManager mgr, string method)
    {
        Transform t = canvasT.Find(buttonName);
        if (t == null) { Debug.LogWarning($"[Day1SceneBuilder] {buttonName} 없음"); return; }
        Button btn = t.GetComponent<Button>();
        if (btn == null) { Debug.LogWarning($"[Day1SceneBuilder] {buttonName} 에 Button 없음"); return; }

        // 기존 영구 리스너 제거 후 단일 바인딩
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(btn.onClick, i);
        }
        UnityAction action = System.Delegate.CreateDelegate(typeof(UnityAction), mgr, method) as UnityAction;
        if (action != null)
        {
            UnityEventTools.AddPersistentListener(btn.onClick, action);
        }
    }

    // 이름 검색 없이 직접 Button 참조에 ImmigrationManager 메서드를 영구 리스너로 바인딩(중첩 패널용).
    private static void BindButtonDirect(Button btn, ImmigrationManager mgr, string method)
    {
        if (btn == null) { Debug.LogWarning($"[Day1SceneBuilder] BindButtonDirect: 버튼이 null ({method})"); return; }

        // 기존 영구 리스너 제거 후 단일 바인딩
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(btn.onClick, i);
        }
        UnityAction action = System.Delegate.CreateDelegate(typeof(UnityAction), mgr, method) as UnityAction;
        if (action != null)
        {
            UnityEventTools.AddPersistentListener(btn.onClick, action);
        }
        else
        {
            Debug.LogWarning($"[Day1SceneBuilder] BindButtonDirect: {method} 델리게이트 생성 실패");
        }
    }

    private static Sprite LoadSprite(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static TMP_FontAsset FindFont()
    {
        // 아틀라스가 비어 있는(깨진) 폰트는 건너뛴다. 한글 폰트(malgun) 우선.
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        TMP_FontAsset fallback = null;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TMP_FontAsset fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!IsValid(fa)) continue;
            if (fallback == null) fallback = fa;
            if (path.ToLowerInvariant().Contains("malgun")) return fa;
        }
        if (fallback != null) return fallback;
        return TMP_Settings.defaultFontAsset; // 최후 폴백(한글 미지원일 수 있음)
    }

    private static bool IsValid(TMP_FontAsset fa)
    {
        return fa != null
            && fa.material != null
            && fa.atlasTextures != null
            && fa.atlasTextures.Length > 0
            && fa.atlasTextures[0] != null;
    }
}
#endif
