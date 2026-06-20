using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 게임 플레이 중(Day*/ImmigrationScene)에 "≡ 메뉴" 버튼 + ESC 로 일시정지 패널을 띄우고,
/// 확인 후 메인메뉴로 나갈 수 있게 한다. 코드로 자체 생성(DontDestroyOnLoad 싱글톤)이라
/// 14개 플레이 씬에 일일이 배치할 필요 없이 모든 플레이 씬에서 동작한다(위치/모양은 코드 기본값).
///
/// 일차 단위 세이브: 메인메뉴로 나가면 그 일차는 다음 이어하기 때 '처음부터' 시작된다(중간 저장 없음).
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    static PauseMenuController _instance;

    Canvas _canvas;
    GameObject _menuButton;
    GameObject _pausePanel;
    GameObject _confirmPanel;
    TMP_FontAsset _font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("PauseMenuController");
        _instance = go.AddComponent<PauseMenuController>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        _font = FindMalgunFont();
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyVisibility(SceneManager.GetActiveScene().name);
    }

    void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    void OnSceneLoaded(Scene s, LoadSceneMode m) => ApplyVisibility(s.name);

    /// <summary>실제 심사 플레이 씬(Day1Scene~Day14Scene, ImmigrationScene)에서만 메뉴를 노출.</summary>
    static bool IsGameplayScene(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name == "ImmigrationScene") return true;
        return name.StartsWith("Day") && name.EndsWith("Scene");
    }

    void ApplyVisibility(string sceneName)
    {
        bool show = IsGameplayScene(sceneName);
        if (_canvas != null) _canvas.gameObject.SetActive(show);
        if (!show) CloseAll(); // 플레이 씬을 벗어나면 패널/타임스케일 정리
    }

    void Update()
    {
        if (_canvas == null || !_canvas.gameObject.activeSelf) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_confirmPanel != null && _confirmPanel.activeSelf) { _confirmPanel.SetActive(false); return; }
            SetPaused(!(_pausePanel != null && _pausePanel.activeSelf));
        }
    }

    void SetPaused(bool paused)
    {
        if (_pausePanel != null) _pausePanel.SetActive(paused);
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
        Time.timeScale = paused ? 0f : 1f; // 일시정지=게임 멈춤(폭탄 타이머 등 포함)
    }

    void CloseAll()
    {
        if (_pausePanel != null) _pausePanel.SetActive(false);
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    void OnGoMenuClicked()
    {
        if (_pausePanel != null) _pausePanel.SetActive(false);
        if (_confirmPanel != null) _confirmPanel.SetActive(true);
    }

    void OnConfirmYes()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenuScene");
    }

    void OnConfirmNo()
    {
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
        if (_pausePanel != null) _pausePanel.SetActive(true);
    }

    // ── UI 빌드(런타임 생성) ──────────────────────────────────

    void BuildUI()
    {
        var cgo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200; // 다른 모든 UI 위에
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // 메뉴 버튼 (좌측 상단, 항상 보임) — 원래 단색 버튼 그대로, 위치만 좌상단
        _menuButton = MakeButton(_canvas.transform, "PauseMenuButton", "≡ 메뉴", 20, new Color(0.12f, 0.14f, 0.18f, 0.92f));
        var mbRt = _menuButton.GetComponent<RectTransform>();
        mbRt.anchorMin = mbRt.anchorMax = new Vector2(0f, 1f);
        mbRt.pivot = new Vector2(0f, 1f);
        mbRt.sizeDelta = new Vector2(100, 42);
        mbRt.anchoredPosition = new Vector2(24, -24);
        _menuButton.GetComponent<Button>().onClick.AddListener(() => SetPaused(true));

        // 일시정지 패널
        _pausePanel = BuildModal("PausePanel", "일시정지", out var pFrame);
        var resume = MakeButton(pFrame.transform, "ResumeButton", "계속하기", 34, new Color(0.18f, 0.42f, 0.28f));
        Place(resume, new Vector2(0, 45), new Vector2(380, 92));
        resume.GetComponent<Button>().onClick.AddListener(() => SetPaused(false));
        var toMenu = MakeButton(pFrame.transform, "ToMenuButton", "메인메뉴로", 34, new Color(0.45f, 0.22f, 0.18f));
        Place(toMenu, new Vector2(0, -70), new Vector2(380, 92));
        toMenu.GetComponent<Button>().onClick.AddListener(OnGoMenuClicked);
        _pausePanel.SetActive(false);

        // 확인 패널
        _confirmPanel = BuildModal("ConfirmPanel", "메인메뉴로 가시겠어요?", out var cFrame);
        var msg = AddText(cFrame.transform, "Msg", "현재 일차는 다음에 처음부터 다시 시작됩니다.", 24, TextAlignmentOptions.Center);
        Place(msg.gameObject, new Vector2(0, 95), new Vector2(600, 80));
        msg.color = new Color(1f, 0.85f, 0.6f);
        var yes = MakeButton(cFrame.transform, "YesButton", "예, 나가기", 30, new Color(0.45f, 0.22f, 0.18f));
        Place(yes, new Vector2(-135, -35), new Vector2(240, 84));
        yes.GetComponent<Button>().onClick.AddListener(OnConfirmYes);
        var no = MakeButton(cFrame.transform, "NoButton", "아니오", 30, new Color(0.25f, 0.28f, 0.34f));
        Place(no, new Vector2(135, -35), new Vector2(240, 84));
        no.GetComponent<Button>().onClick.AddListener(OnConfirmNo);
        _confirmPanel.SetActive(false);
    }

    GameObject BuildModal(string name, string title, out GameObject frame)
    {
        var root = NewUI(name, _canvas.transform, Vector2.zero, Vector2.one);
        var dim = root.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f); // 딤(뒤 클릭 차단)

        frame = NewUI("Frame", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var frt = frame.GetComponent<RectTransform>();
        frt.sizeDelta = new Vector2(700, 470);
        var fimg = frame.AddComponent<Image>();
        fimg.color = new Color(0.10f, 0.12f, 0.16f, 0.99f);

        var t = AddText(frame.transform, "Title", title, 38, TextAlignmentOptions.Center);
        var trt = t.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(660, 90);
        trt.anchoredPosition = new Vector2(0, -34);
        t.fontStyle = FontStyles.Bold;
        t.color = new Color(1f, 0.9f, 0.6f);
        return root;
    }

    static void Place(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────

    static GameObject NewUI(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    TMP_Text AddText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        var go = NewUI(name, parent, Vector2.zero, Vector2.one);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
        if (_font != null) t.font = _font;
        return t;
    }

    GameObject MakeButton(Transform parent, string name, string label, float size, Color bg)
    {
        var go = NewUI(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        AddText(go.transform, "Label", label, size, TextAlignmentOptions.Center);
        return go;
    }

    static TMP_FontAsset FindMalgunFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (var fa in fonts)
        {
            if (fa == null || fa.name == null) continue;
            if (fa.name.ToLowerInvariant().Contains("malgun")) return fa;
        }
        return TMP_Settings.defaultFontAsset;
    }
}
