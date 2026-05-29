#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// BriefingScene 을 자동 생성한다.
/// Tools > Briefing > Build Briefing Scene
/// </summary>
public static class BriefingSceneBuilder
{
    [MenuItem("Tools/Briefing/Build Briefing Scene")]
    public static void Build()
    {
        // 새 씬 생성
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        TMP_FontAsset font = FindFont();

        // Camera
        GameObject camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.94f, 0.94f, 0.94f);
        cam.orthographic = true;
        cam.tag = "MainCamera";
        camGo.AddComponent<AudioListener>();

        // Canvas (Screen Space Overlay)
        GameObject canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        Transform canvasT = canvasGo.transform;

        RectTransform NewUI(string name, Transform parent, Vector2 amin, Vector2 amax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = amin; rt.anchorMax = amax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        // 슬라이드 이미지 (화면 중앙 상단 2/3)
        var imgRt = NewUI("SlideImage", canvasT, new Vector2(0.2f, 0.28f), new Vector2(0.8f, 0.88f));
        var slideImg = imgRt.gameObject.AddComponent<Image>();
        slideImg.preserveAspect = true;
        slideImg.raycastTarget = false;

        // 슬라이드 텍스트 (이미지 아래)
        var textRt = NewUI("SlideText", canvasT, new Vector2(0.1f, 0.06f), new Vector2(0.9f, 0.27f));
        var slideTxt = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        slideTxt.fontSize = 44;
        slideTxt.alignment = TextAlignmentOptions.Center;
        slideTxt.color = new Color(0.12f, 0.12f, 0.15f);
        slideTxt.lineSpacing = 12f;
        if (font != null) slideTxt.font = font;

        // 클릭 안내 (우하단)
        var hintRt = NewUI("HintText", canvasT, new Vector2(0.6f, 0.01f), new Vector2(0.99f, 0.055f));
        var hintTxt = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hintTxt.fontSize = 26;
        hintTxt.alignment = TextAlignmentOptions.Right;
        hintTxt.color = new Color(0.5f, 0.5f, 0.5f);
        hintTxt.text = "클릭하여 계속";
        if (font != null) hintTxt.font = font;

        // 페이드 패널
        var fadeRt = NewUI("FadePanel", canvasT, Vector2.zero, Vector2.one);
        var fadeImg = fadeRt.gameObject.AddComponent<Image>();
        fadeImg.color = Color.black;
        var fadeCg = fadeRt.gameObject.AddComponent<CanvasGroup>();
        fadeCg.alpha = 1f;

        // EventSystem
        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // BriefingManager GameObject
        var mgrGo = new GameObject("BriefingManager");
        var mgr = mgrGo.AddComponent<BriefingManager>();

        // 직렬 참조 와이어링
        void Wire(Component comp, string field, Object val)
        {
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"BriefingSceneBuilder: field {field} not found"); return; }
            prop.objectReferenceValue = val;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Wire(mgr, "_camera", cam);
        Wire(mgr, "_slideImage", slideImg);
        Wire(mgr, "_slideText", slideTxt);
        Wire(mgr, "_fadePanel", fadeCg);
        Wire(mgr, "_hintText", hintTxt);

        // 1일차 슬라이드 3장 설정
        Sprite LoadSprite(string path)
        {
            var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (spr == null) Debug.LogWarning($"BriefingSceneBuilder: sprite not found at {path}");
            return spr;
        }

        var slidesProp = new SerializedObject(mgr).FindProperty("_slides");
        slidesProp.arraySize = 3;
        var so2 = new SerializedObject(mgr);
        var sp = so2.FindProperty("_slides");
        sp.arraySize = 3;
        SetSlide(sp, 0, LoadSprite("Assets/Images/Briefing/briefing_d1_1.png"),
            "2026년 5월 26일...", new Color(0.94f, 0.94f, 0.94f));
        SetSlide(sp, 1, LoadSprite("Assets/Images/Briefing/briefing_d1_2.png"),
            "오늘은 인천국제공항 검문소 첫 출근입니다.\n여러분은 이제부터 다양한 방문객을 심사해야 합니다.", new Color(0.94f, 0.94f, 0.94f));
        SetSlide(sp, 2, LoadSprite("Assets/Images/Briefing/briefing_d1_3.png"),
            "떨리고 긴장되는 상황이지만 잘 하시리라 믿습니다!", new Color(0.72f, 0.90f, 0.95f));
        so2.ApplyModifiedPropertiesWithoutUndo();

        // 씬 저장
        string scenePath = "Assets/Scenes/BriefingScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        // Build Settings에 추가
        AddToBuildSettings(scenePath);

        Debug.Log($"[BriefingSceneBuilder] 완료: {scenePath}");
    }

    private static void SetSlide(SerializedProperty sp, int idx, Sprite sprite, string text, Color bg)
    {
        var elem = sp.GetArrayElementAtIndex(idx);
        elem.FindPropertyRelative("image").objectReferenceValue = sprite;
        elem.FindPropertyRelative("text").stringValue = text;
        var col = elem.FindPropertyRelative("backgroundColor");
        col.colorValue = bg;
    }

    private static void AddToBuildSettings(string newScenePath)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool exists = scenes.Exists(s => s.path == newScenePath);
        if (!exists)
        {
            // BriefingScene을 ImmigrationScene 바로 앞에 삽입
            int insertIdx = scenes.FindIndex(s => s.path.Contains("ImmigrationScene"));
            if (insertIdx < 0) insertIdx = scenes.Count;
            scenes.Insert(insertIdx, new EditorBuildSettingsScene(newScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[BriefingSceneBuilder] BriefingScene을 Build Settings에 추가했습니다.");
        }
    }

    private static TMP_FontAsset FindFont()
    {
        foreach (var g in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            if (fa == null || fa.material == null) continue;
            if (p.ToLowerInvariant().Contains("malgun") && fa.atlasTextures != null
                && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
                return fa;
        }
        return TMP_Settings.defaultFontAsset;
    }
}
#endif
