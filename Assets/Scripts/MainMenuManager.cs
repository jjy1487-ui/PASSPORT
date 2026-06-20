using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] GameObject achieveButton;
    [SerializeField] GameObject endingButton;
    [SerializeField] GameObject continueButton; // 이어하기(저장 있을 때만 표시). 시작 버튼 복제본, 엔딩 아래 배치.
    [SerializeField] CanvasGroup fadePanel;
    [SerializeField] float fadeInDuration = 0.8f;
    [SerializeField] float fadeOutDuration = 0.5f;

    [Header("업적(호칭) 패널")]
    [Tooltip("비워두면 첫 열람 시 런타임으로 자동 생성한다.")]
    [SerializeField] TitleAchievementPanel achievementPanel;

    const string SAVE_KEY = "HasSaveData";

    bool isTransitioning = false;

    void Start()
    {
        bool hasSave = PlayerPrefs.HasKey(SAVE_KEY);
        // null 가드: 씬에 미연결(achieveButton 등)이라도 여기서 NRE 로 멈추지 않게 한다.
        //  (예전엔 achieveButton 미연결이면 이 줄에서 예외 → 아래 이어하기 생성까지 도달을 못 했음)
        if (achieveButton != null) achieveButton.SetActive(hasSave);
        if (endingButton != null) endingButton.SetActive(hasSave);
        // 이어하기 버튼: 진행 중인 세이브가 있을 때만 보인다(엔딩/업적과 동일 패턴).
        if (continueButton != null) continueButton.SetActive(GameProgressSave.HasSave());
        if (fadePanel != null) StartCoroutine(FadeIn());
    }

    public void OnStartGame()
    {
        if (isTransitioning) return;
        GameProgressSave.ClearProgressKeepMeta(); // 새 게임: 점수/돈/엔딩카운터 초기화(호칭/아이템 메타는 유지)
        ShopSave.Clear();                      // 새 게임: 상점 구매 내역/활성 효과 초기화
        PlayerPrefs.SetInt(SAVE_KEY, 1);
        PlayerPrefs.SetInt("CurrentDay", 1);  // 1일차 시작
        PlayerPrefs.Save();
        StartCoroutine(TransitionToScene("BriefingScene")); // 브리핑 → 심사 씬
    }

    /// <summary>
    /// [이어하기] 저장된 진행을 리셋하지 않고 그대로 재개한다(저장된 CurrentDay 의 브리핑부터).
    /// 새 게임(OnStartGame)과 달리 ClearProgressKeepMeta/CurrentDay=1 을 하지 않는다.
    /// </summary>
    public void OnContinue()
    {
        if (isTransitioning) return;
        PlayerPrefs.SetInt(SAVE_KEY, 1);
        PlayerPrefs.Save();
        // 세션 중(앱 안 끄고) 재개 시엔 매니저가 살아 있어 Awake 의 LoadInto 가 다시 안 돈다 → 직접 복원해 '그날 시작' 상태로.
        if (ScoreEconomyManager.Instance != null)
            GameProgressSave.LoadInto(ScoreEconomyManager.Instance);
        StartCoroutine(TransitionToScene("BriefingScene"));
    }

    public void OnAchievement()
    {
        if (achievementPanel == null) achievementPanel = BuildAchievementPanel();
        if (achievementPanel != null) achievementPanel.Toggle();
    }

    /// <summary>
    /// 업적(호칭) 패널을 런타임으로 생성한다(메인 메뉴 씬에 수동 배치가 없을 때 폴백).
    /// 중앙 모달, 닫기 = "닫기" 텍스트 버튼 우상단(닫기 버튼 통일), malgun 폰트.
    /// </summary>
    TitleAchievementPanel BuildAchievementPanel()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[MainMenuManager] Canvas 를 찾지 못해 업적 패널을 생성할 수 없습니다.");
            return null;
        }
        TMP_FontAsset font = FindMalgunFont();

        GameObject rootGo = NewUI("AchievementPanel", canvas.transform, Vector2.zero, Vector2.one);
        // 반투명 딤(클릭으로 안 닫힘 — 닫기는 X/Esc)
        AddImage(rootGo, new Color(0.02f, 0.03f, 0.05f, 0.85f));

        GameObject frame = NewUI("Frame", rootGo.transform, new Vector2(0.30f, 0.22f), new Vector2(0.70f, 0.78f));
        AddImage(frame, new Color(0.10f, 0.12f, 0.16f, 0.98f));

        TMP_Text title = AddText(frame.transform, "Title", "업적 — 호칭", 30,
            new Vector2(0.06f, 0.86f), new Vector2(0.80f, 0.96f), font, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold; title.color = new Color(1f, 0.9f, 0.6f);

        Button close = MakeButton(frame.transform, "CloseButton", "닫기", 22,
            new Vector2(0.82f, 0.86f), new Vector2(0.97f, 0.96f), font, new Color(0.60f, 0.18f, 0.18f));

        TMP_Text list = AddText(frame.transform, "ListText", "", 20,
            new Vector2(0.07f, 0.08f), new Vector2(0.93f, 0.84f), font, TextAlignmentOptions.TopLeft);
        list.color = Color.white;

        TitleAchievementPanel panel = rootGo.AddComponent<TitleAchievementPanel>();
        // 런타임 생성이라 SerializedObject 없이 private 필드를 직접 주입한다.
        AssignPrivate(panel, "_root", rootGo);
        AssignPrivate(panel, "_listText", list);
        AssignPrivate(panel, "_closeButton", close);
        rootGo.SetActive(false);
        return panel;
    }

    // ── 런타임 UI 헬퍼(메인 메뉴 빌더 부재 → 자체 생성) ──
    static void AssignPrivate(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(target, value);
        else Debug.LogWarning($"[MainMenuManager] 필드 {field} 를 찾지 못했습니다.");
    }

    static GameObject NewUI(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    static Image AddImage(GameObject go, Color c)
    {
        var img = go.AddComponent<Image>();
        img.color = c;
        return img;
    }

    static TMP_Text AddText(Transform parent, string name, string text, float size,
        Vector2 aMin, Vector2 aMax, TMP_FontAsset font, TextAlignmentOptions align)
    {
        var go = NewUI(name, parent, aMin, aMax);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
        if (font != null) t.font = font;
        return t;
    }

    static Button MakeButton(Transform parent, string name, string label, float size,
        Vector2 aMin, Vector2 aMax, TMP_FontAsset font, Color bg)
    {
        var go = NewUI(name, parent, aMin, aMax);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var t = AddText(go.transform, "Label", label, size, Vector2.zero, Vector2.one, font, TextAlignmentOptions.Center);
        t.color = Color.white;
        return btn;
    }

    static TMP_FontAsset FindMalgunFont()
    {
        // 한글 폰트(malgun) 우선. 로드된 폰트 중 이름에 malgun 포함을 찾고, 없으면 기본 폰트.
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (var fa in fonts)
        {
            if (fa == null || fa.name == null) continue;
            if (fa.name.ToLowerInvariant().Contains("malgun")) return fa;
        }
        return TMP_Settings.defaultFontAsset;
    }

    public void OnEnding()
    {
        // placeholder
    }

    IEnumerator FadeIn()
    {
        fadePanel.alpha = 1f;
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            fadePanel.alpha = 1f - Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        fadePanel.alpha = 0f;
    }

    IEnumerator TransitionToScene(string sceneName)
    {
        isTransitioning = true;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            fadePanel.alpha = Mathf.Clamp01(elapsed / fadeOutDuration);
            yield return null;
        }
        fadePanel.alpha = 1f;
        SceneManager.LoadScene(sceneName);
    }
}
