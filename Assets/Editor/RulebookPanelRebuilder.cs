#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

/// <summary>
/// 규정집 패널 프리팹(Assets/Prefabs/UIPopups/RulebookPanel.prefab)을 2단 구조로 재구성한다.
/// 왼쪽 = 규정 제목 행 14개(미리 수동 배치, RulebookPopup 이 들어온 규정 수만큼 채우고 나머지 숨김),
/// 오른쪽 = 선택한 규정의 제목 + 내용. 닫기 X(우상단)/overrideSorting/Canvas 동작은 유지한다.
///
/// 재실행 가능: 기존 RuleList/RuleSelectable/Page/PrevButton/NextButton 을 지우고 새로 만든다.
/// Title/Content/CloseButton 은 위치만 재배치해 재사용한다(에셋 GUID 보존).
/// 메뉴: Tools > Inspection > Rebuild Rulebook Panel (2-Column)
/// </summary>
public static class RulebookPanelRebuilder
{
    private const string PrefabPath = "Assets/Prefabs/UIPopups/RulebookPanel.prefab";
    private const int RowCount = 14;

    [MenuItem("Tools/Inspection/Rebuild Rulebook Panel (2-Column)")]
    public static void Rebuild()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[RulebookPanelRebuilder] 프리팹을 열 수 없습니다: {PrefabPath}");
            return;
        }

        try
        {
            RulebookPopup popup = root.GetComponent<RulebookPopup>();
            if (popup == null)
            {
                Debug.LogError("[RulebookPanelRebuilder] 루트에 RulebookPopup 이 없습니다.");
                return;
            }

            RectTransform rootRt = root.GetComponent<RectTransform>();
            TMP_FontAsset font = FindFont();

            // 패널을 2단을 담을 만큼 넓힌다(기준 1920×1080 기반, 한 모달).
            rootRt.sizeDelta = new Vector2(1100f, 600f);

            // ── 기존 잔재 제거(재실행 안전) ──
            DestroyChild(root.transform, "RuleList");
            DestroyChild(root.transform, "RuleSelectable");
            DestroyChild(root.transform, "Page");
            DestroyChild(root.transform, "PrevButton");
            DestroyChild(root.transform, "NextButton");

            // ── 왼쪽 리스트 컨테이너 ──
            // 좌측 세로 영역. 위→아래로 행이 균등 배치되도록 행별 앵커를 직접 계산해 배치.
            RectTransform listArea = NewUI("RuleList", root.transform, new Vector2(0.035f, 0.07f), new Vector2(0.40f, 0.86f));
            AddImage(listArea, new Color(0.13f, 0.15f, 0.19f, 0.6f)); // 좌측 패널 표면(살짝 어둡게)

            // 리스트 헤더(작은 제목)
            TMP_Text listHeader = AddText(listArea, "ListHeader", "규정 목록", 18, new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.995f), font, TextAlignmentOptions.Left);
            listHeader.fontStyle = FontStyles.Bold; listHeader.color = new Color(0.8f, 0.82f, 0.88f);

            // 14개 행을 위→아래로 균등 배치(헤더 아래 0.0~0.93 영역).
            CrossCheckItemView[] rows = new CrossCheckItemView[RowCount];
            const float top = 0.93f;       // 행 영역 상단(헤더 아래)
            const float bottom = 0.01f;     // 행 영역 하단
            float slot = (top - bottom) / RowCount;
            const float gap = 0.004f;       // 행 간 간격
            for (int i = 0; i < RowCount; i++)
            {
                float yMax = top - i * slot;
                float yMin = yMax - slot + gap;
                CrossCheckItemView row = MakeItemView(listArea, $"Rule_{i:D2}", font, "");
                RectTransform rrt = row.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0.03f, yMin);
                rrt.anchorMax = new Vector2(0.97f, yMax);
                rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
                // 라벨 좌측 정렬(제목 리스트 느낌)
                TMP_Text lbl = row.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) { lbl.alignment = TextAlignmentOptions.Left; lbl.margin = new Vector4(10f, 0f, 4f, 0f); lbl.fontSize = 16f; }
                row.gameObject.SetActive(false); // 채워질 때만 RulebookPopup 이 켠다
                rows[i] = row;
            }

            // ── 오른쪽 내용 패널 ──
            // Title/Content 를 우측으로 재배치(기존 오브젝트 재사용 — 에셋/폰트 보존).
            RectTransform rightArea = NewUI("RightPanel", root.transform, new Vector2(0.42f, 0.07f), new Vector2(0.965f, 0.86f));
            AddImage(rightArea, new Color(0.12f, 0.14f, 0.18f, 0.5f));

            Transform titleT = root.transform.Find("Title");
            Transform contentT = root.transform.Find("Content");
            if (titleT != null)
            {
                titleT.SetParent(rightArea, false);
                RectTransform trt = (RectTransform)titleT;
                trt.anchorMin = new Vector2(0.04f, 0.86f); trt.anchorMax = new Vector2(0.96f, 0.98f);
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                TMP_Text tt = titleT.GetComponent<TMP_Text>();
                if (tt != null) { tt.alignment = TextAlignmentOptions.Left; tt.fontStyle = FontStyles.Bold; }
            }
            if (contentT != null)
            {
                contentT.SetParent(rightArea, false);
                RectTransform crt = (RectTransform)contentT;
                crt.anchorMin = new Vector2(0.04f, 0.03f); crt.anchorMax = new Vector2(0.96f, 0.84f);
                crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                TMP_Text ct = contentT.GetComponent<TMP_Text>();
                if (ct != null) { ct.alignment = TextAlignmentOptions.TopLeft; }
            }

            // ── RulebookPopup 직렬 참조 와이어링 ──
            // _root/_closeButton/_titleText/_contentText 는 유지(존재 확인), _ruleRows 신규 배열, 잔재 필드는 비움.
            Wire(popup, "_root", root);
            // _closeButton 은 Button 타입 → Button 컴포넌트로 와이어링.
            Button close = FindButton(root.transform, "CloseButton");
            if (close != null) Wire(popup, "_closeButton", close);

            if (titleT != null) Wire(popup, "_titleText", titleT.GetComponent<TMP_Text>());
            if (contentT != null) Wire(popup, "_contentText", contentT.GetComponent<TMP_Text>());

            WireArray(popup, "_ruleRows", rows);

            // 잔재 필드(_prevButton/_nextButton/_pageText)는 제거됐으니 비운다(있던 참조 무효화).
            Wire(popup, "_prevButton", null);
            Wire(popup, "_nextButton", null);
            Wire(popup, "_pageText", null);

            // 시작은 비활성(팝업)
            root.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Debug.Log(ok
                ? $"[RulebookPanelRebuilder] 2단 규정집 재구성 완료: {PrefabPath} (행 {RowCount}개)"
                : $"[RulebookPanelRebuilder] 저장 실패: {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // ── 헬퍼(Day1SceneBuilder 패턴 계승) ──────────────────────────
    private static void DestroyChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    private static Button FindButton(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        return t != null ? t.GetComponent<Button>() : null;
    }

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

    private static TMP_Text AddText(Transform parent, string name, string text, float size,
        Vector2 anchorMin, Vector2 anchorMax, TMP_FontAsset font, TextAlignmentOptions align)
    {
        RectTransform rt = NewUI(name, parent, anchorMin, anchorMax);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align;
        t.color = Color.white;
        if (font != null) t.font = font;
        return t;
    }

    private static CrossCheckItemView MakeItemView(RectTransform parent, string name, TMP_FontAsset font, string label = "")
    {
        RectTransform rt = NewUI(name, parent, Vector2.zero, Vector2.one);
        // 선택 하이라이트(비활성 시작)
        RectTransform hi = NewUI("Highlight", rt, Vector2.zero, Vector2.one);
        Image hiImg = AddImage(hi, new Color(0.231f, 0.510f, 0.769f, 0.45f));
        hi.gameObject.SetActive(false);
        // 라벨(한글 폰트 = MalgunGothic SDF)
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

    private static void Wire(Component comp, string fieldName, Object value)
    {
        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[RulebookPanelRebuilder] {comp.GetType().Name}.{fieldName} 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireArray(Component comp, string fieldName, Component[] values)
    {
        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null || !prop.isArray)
        {
            Debug.LogWarning($"[RulebookPanelRebuilder] {comp.GetType().Name}.{fieldName} 배열 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMP_FontAsset FindFont()
    {
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
        return fallback != null ? fallback : TMP_Settings.defaultFontAsset;
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
