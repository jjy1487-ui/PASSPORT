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

        // ── 우측 상단 HUD: 어두운 배경판 + 골드 + 진행 순서 ──
        RectTransform hudBg = NewUI("HudPanel", root, new Vector2(0.60f, 0.905f), new Vector2(0.990f, 0.990f));
        AddImage(hudBg, new Color(0f, 0f, 0f, 0.62f)); // 어두운 반투명 배경

        Image goldIcon = AddImage(NewUI("GoldIcon", hudBg, new Vector2(0.02f, 0.10f), new Vector2(0.22f, 0.90f)), Color.white);
        goldIcon.sprite = LoadSprite("Assets/Images/UI/gold.png"); goldIcon.preserveAspect = true;
        TMP_Text goldText = AddText(hudBg, "GoldText", "0", 26, new Vector2(0.23f, 0.10f), new Vector2(0.48f, 0.90f), font, TextAlignmentOptions.Left);
        goldText.color = new Color(0.95f, 0.85f, 0.3f); goldText.fontStyle = FontStyles.Bold;

        RectTransform divider = NewUI("Divider", hudBg, new Vector2(0.50f, 0.15f), new Vector2(0.52f, 0.85f));
        AddImage(divider, new Color(1f, 1f, 1f, 0.35f));

        Image peopleIcon = AddImage(NewUI("PeopleIcon", hudBg, new Vector2(0.53f, 0.10f), new Vector2(0.73f, 0.90f)), Color.white);
        peopleIcon.sprite = LoadSprite("Assets/Images/UI/people.png"); peopleIcon.preserveAspect = true;
        TMP_Text slotCounter = AddText(hudBg, "SlotCounterText", "1 / 7", 26, new Vector2(0.74f, 0.10f), new Vector2(0.99f, 0.90f), font, TextAlignmentOptions.Left);
        slotCounter.color = Color.white; slotCounter.fontStyle = FontStyles.Bold;

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
        NewsPopup newsPopup = BuildListPopup<NewsPopup>(root, "NewsPanel", "뉴스", font, out _);
        // ── 규정집 팝업 ──
        RulebookPopup rulePopup = BuildListPopup<RulebookPopup>(root, "RulebookPanel", "규정집", font, out _);

        // ── 대화 기록 팝업 — DocumentArea 위에 꽉 채워 표시 ──
        RectTransform logRt = NewUI("DialogueLogPanel", docArea, Vector2.zero, Vector2.one);
        AddImage(logRt, new Color(0.08f, 0.10f, 0.14f, 0.97f));
        TMP_Text logTitle = AddText(logRt, "Title", "대화 기록", 26, new Vector2(0.04f, 0.88f), new Vector2(0.82f, 0.98f), font, TextAlignmentOptions.Left);
        logTitle.fontStyle = FontStyles.Bold; logTitle.color = new Color(1f, 0.9f, 0.6f);
        Button logClose = MakeButton(logRt, "CloseButton", "X", 22, new Vector2(0.88f, 0.89f), new Vector2(0.99f, 0.99f), font, new Color(0.60f, 0.18f, 0.18f));
        TMP_Text logText = AddText(logRt, "LogText", "", 17, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.87f), font, TextAlignmentOptions.TopLeft);
        logText.color = Color.white;
        DialogueLogPopup logPopup = logRt.gameObject.AddComponent<DialogueLogPopup>();
        Wire(logPopup, "_root", logRt.gameObject);
        Wire(logPopup, "_logText", logText);
        Wire(logPopup, "_closeButton", logClose);
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

        // ── 1일차 완료 패널 ──
        RectTransform dayDone = NewUI("DayCompletePanel", root, new Vector2(0.33f, 0.40f), new Vector2(0.67f, 0.60f));
        AddImage(dayDone, new Color(0f, 0f, 0f, 0.8f));
        AddText(dayDone, "DoneText", "1일차 완료", 44, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        dayDone.gameObject.SetActive(false);

        // ── 컨트롤러 ──
        RectTransform ctrlObj = NewUI("InspectionController", root, Vector2.zero, Vector2.zero);
        InspectionController controller = ctrlObj.gameObject.AddComponent<InspectionController>();
        Wire(controller, "_customerView", customerView);
        Wire(controller, "_documentView", documentView);
        Wire(controller, "_dialogueView", dialogueView);
        Wire(controller, "_judgmentPanel", judgment);
        Wire(controller, "_slotCounterText", slotCounter);
        Wire(controller, "_goldText", goldText);
        if (draggableHandset != null) Wire(draggableHandset, "_controller", controller); // 헤드셋 → 컨트롤러 후기 와이어링
        // HudPanel 자식 탐색으로 자동 와이어링됨(slotCounter/goldText 변수가 직접 참조)
        Wire(controller, "_dayCompleteRoot", dayDone.gameObject);

        Wire(customerView, "_portraitPlaceholder", portrait);
        Wire(customerView, "_nameText", custName);
        Wire(customerView, "_infoText", custInfo);

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
