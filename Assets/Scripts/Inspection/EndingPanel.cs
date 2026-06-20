using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 엔딩 화면. ImmigrationManager.OnEndingResolved 를 구독해 엔딩 패널을 띄운다.
/// 조기엔딩(#11~#14)·누적엔딩(#15/#16)·14일 종료 점수구간 모두 같은 이벤트로 들어온다.
///
/// 표시만 한다 — 엔딩 결정(점수구간/트리거/임계치)은 EndingResolver/ImmigrationManager 소유(규약 5장).
/// 패널은 강제 모달(Backdrop 클릭으로 닫히지 않음, UI-CONVENTIONS 4장: 결과/엔딩은 강제 패널).
/// 컷씬은 좌클릭으로만 다음 장으로 넘긴다(키보드 미사용). 텍스트 폴백 패널은 X 우상단으로 닫는다.
/// </summary>
public sealed class EndingPanel : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;        // 비활성 시작
    [SerializeField] private TMP_Text _nameText;      // 엔딩명
    [SerializeField] private TMP_Text _descText;      // 엔딩 설명 + 최종 점수/호칭
    [SerializeField] private TMP_Text _typeBadgeText; // 엔딩 유형 배지(조기/누적/일반)
    [SerializeField] private Button _closeButton;     // X, 우상단 → 타이틀 복귀

    [Header("타이틀 복귀(선택)")]
    [Tooltip("닫기 시 로드할 씬 이름. 비우면 패널만 닫는다.")]
    [SerializeField] private string _titleSceneName = "";

    private ImmigrationManager _immigration;
    private bool _shown;

    // ── 전체화면 컷씬(엔딩별 이미지 시퀀스) ──────────────────────────────
    // 텍스트 패널 대신, 엔딩 이름에 맞는 Resources/Endings/<key>_N.png 를 전체화면으로 한 장씩 넘긴다.
    // 좌클릭으로 한 장씩 진행, 마지막 장 다음 클릭 → 타이틀 복귀(CloseAndReturn). 씬 편집 없이 런타임으로 오버레이 생성.
    private static readonly System.Collections.Generic.Dictionary<string, string> CutsceneKeys =
        new System.Collections.Generic.Dictionary<string, string>
        {
            { "퍼엉!", "Bomb" },
            { "공범", "Accomplice" },
            { "순진한 녀석", "Naive" },
            { "방역 실패", "Quarantine" },
            { "등잔 밑이 어둡다", "Blindspot" },
            { "자넨 적성에 안 맞는 것 같네", "Unfit" },
        };
    private const string NormalCutsceneKey = "Normal"; // 점수(노멀) 엔딩 공용

    // 컷씬 키 → 엔딩 배경음악(Resources/Audio/*). 일부 엔딩은 한 곡을 공유한다.
    private static readonly System.Collections.Generic.Dictionary<string, string> CutsceneMusic =
        new System.Collections.Generic.Dictionary<string, string>
        {
            { "Bomb",       "Audio/Ending_Bomb" },
            { "Accomplice", "Audio/Ending_Accomplice" },
            { "Naive",      "Audio/Ending_NaiveBlindspotUnfit" },
            { "Blindspot",  "Audio/Ending_NaiveBlindspotUnfit" },
            { "Unfit",      "Audio/Ending_NaiveBlindspotUnfit" },
            { "Quarantine", "Audio/Ending_Quarantine" },
            { "Normal",     "Audio/Ending_Normal" },
        };

    private GameObject _cutsceneRoot;   // 런타임 생성 풀스크린 오버레이
    private Image _cutsceneImage;       // 현재 프레임 표시
    private Sprite[] _frames;
    private int _frameIndex;
    private bool _inCutscene;
    private float _lastAdvanceTime = -1f; // 폴링+버튼 클릭이 1회 클릭에 둘 다 들어와 두 장 넘는 것 방지
    private AudioSource _cutsceneAudio;    // 컷씬 배경음악(오버레이에 런타임 생성, 엔딩별 곡)

    /// <summary>컷씬 재생 중인가(엔딩 뷰어가 QA 버튼 숨김 판단에 사용).</summary>
    public bool IsCutscenePlaying => _inCutscene;
    /// <summary>닫기 시 이동할 씬 지정(엔딩 뷰어: ""=씬 전환 없이 목록 복귀).</summary>
    public void SetTitleScene(string scene) => _titleSceneName = scene;

    private void Start()
    {
        _immigration = Object.FindFirstObjectByType<ImmigrationManager>();
        if (_immigration != null)
        {
            _immigration.OnEndingResolved += HandleEndingResolved;
            // 이미 엔딩이 결정된 뒤 생성됐다면(폴링 폴백) 즉시 반영.
            if (_immigration.LastEnding.IsValid) HandleEndingResolved(_immigration.LastEnding);
        }
        else
        {
            Debug.LogWarning("[EndingPanel] ImmigrationManager 를 찾지 못했습니다. 엔딩 표시 비활성.");
        }

        if (_closeButton != null) _closeButton.onClick.AddListener(CloseAndReturn);
        // 이미 Show()로 표시된 상태면 다시 숨기지 않는다(비활성 시작 → 외부 활성화 시 Start가 뒤늦게 돌아도 안전).
        if (_root != null && !_shown) _root.SetActive(false);
    }

    /// <summary>
    /// 외부(ImmigrationManager)에서 직접 호출해 엔딩 패널을 띄운다.
    /// 패널 GameObject 가 비활성으로 시작하면 Awake/OnEnable/Start 가 실행되지 않아
    /// OnEndingResolved 구독 자체가 걸리지 않는다(=엔딩 결정돼도 화면이 안 뜸 → 소프트락).
    /// 그래서 항상 활성인 진행 매니저가 이 메서드로 패널을 직접 활성화·표시한다.
    /// </summary>
    public void Show(EndingResult e)
    {
        if (!e.IsValid) return;
        if (!gameObject.activeSelf) gameObject.SetActive(true); // 비활성 시작 시 활성화(Start/구독 트리거)
        HandleEndingResolved(e);
    }

    private void OnDestroy()
    {
        if (_immigration != null) _immigration.OnEndingResolved -= HandleEndingResolved;
        if (_closeButton != null) _closeButton.onClick.RemoveListener(CloseAndReturn);
    }

    private void Update()
    {
        // 컷씬 중에만 입력 처리: 좌클릭 = 다음 장. 키보드(스페이스/엔터/Esc)는 사용하지 않는다.
        // 새 Input System(이 프로젝트 표준) 마우스 폴링 — CrossCheck/Inspection 등과 동일 패턴.
        if (!_inCutscene) return;
        Mouse ms = Mouse.current;
        if (ms != null && ms.leftButton.wasPressedThisFrame) AdvanceCutscene();
    }

    private void HandleEndingResolved(EndingResult e)
    {
        if (!e.IsValid) return;
        _shown = true;

        // 엔딩 이름 → 컷씬 키 → 프레임 로드. 이미지가 있으면 전체화면 컷씬, 없으면 기존 텍스트 패널 폴백.
        string cutsceneKey = CutsceneKeyFor(e);
        Sprite[] frames = LoadFrames(cutsceneKey);
        if (frames != null && frames.Length > 0)
        {
            if (_root != null) _root.SetActive(false); // 텍스트 패널 숨김
            BeginCutscene(frames, cutsceneKey);
            return;
        }

        if (_root != null) _root.SetActive(true);
        if (_nameText != null) _nameText.text = e.endingName;
        if (_typeBadgeText != null) _typeBadgeText.text = TypeLabel(e.endingType);
        if (_descText != null) _descText.text = BuildDescription(e);
    }

    // ── 전체화면 컷씬 ────────────────────────────────────────────────
    private static string CutsceneKeyFor(EndingResult e)
    {
        if (e.endingName != null && CutsceneKeys.TryGetValue(e.endingName.Trim(), out string k)) return k;
        return NormalCutsceneKey; // 점수(노멀) 엔딩 등은 공용 노멀 컷씬
    }

    private static Sprite[] LoadFrames(string key)
    {
        var list = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; i <= 30; i++)
        {
            Sprite s = Resources.Load<Sprite>($"Endings/{key}_{i}");
            if (s == null) break;
            list.Add(s);
        }
        return list.ToArray();
    }

    private void BeginCutscene(Sprite[] frames, string cutsceneKey)
    {
        _frames = frames;
        _frameIndex = 0;
        _inCutscene = true;
        EnsureOverlay();
        _cutsceneRoot.SetActive(true);
        _cutsceneRoot.transform.SetAsLastSibling();
        ShowFrame();

        // 엔딩 진입 시 심사 씬 사운드 정리: 진행 중이던 대사 말소리(타자기 블립)·군중 앰비언스를 멈춰
        // 엔딩 음악만 깔끔히 들리게 한다(엔딩이 대사 타이핑 도중 떠도 소리가 안 남는다).
        var dialogueView = FindObjectOfType<DialogueView>();
        if (dialogueView != null) dialogueView.StopSpeaking();
        AmbienceManager.Suspend();

        PlayCutsceneMusic(cutsceneKey);
    }

    /// <summary>컷씬 키에 맞는 엔딩 배경음악을 재생(루프). 매핑 없으면 노멀 곡으로 폴백.</summary>
    private void PlayCutsceneMusic(string cutsceneKey)
    {
        if (_cutsceneAudio == null) return;
        if (cutsceneKey == null || !CutsceneMusic.TryGetValue(cutsceneKey, out string path))
            path = CutsceneMusic[NormalCutsceneKey];
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) { Debug.LogWarning($"[EndingPanel] 엔딩 음악을 찾지 못함: Resources/{path}"); return; }
        _cutsceneAudio.clip = clip;
        _cutsceneAudio.Play();
    }

    /// <summary>씬 편집 없이 런타임으로 풀스크린 컷씬 오버레이(검은 배경 + 프레임 이미지)를 만든다(최상단 Canvas).</summary>
    private void EnsureOverlay()
    {
        if (_cutsceneRoot != null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform;

        _cutsceneRoot = new GameObject("EndingCutscene", typeof(RectTransform), typeof(Canvas), typeof(Image));
        _cutsceneRoot.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)_cutsceneRoot.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        Canvas oc = _cutsceneRoot.GetComponent<Canvas>();
        oc.overrideSorting = true; oc.sortingOrder = 200; // 모든 UI 위
        Image bg = _cutsceneRoot.GetComponent<Image>();
        bg.color = Color.black; bg.raycastTarget = true;  // 검은 배경 + 클릭 차단

        // 클릭으로 다음 장: EventSystem 경로(폴링이 안 먹는 환경 대비, 본 게임 uGUI 버튼과 동일 경로).
        // 전체화면 bg 를 버튼으로 만들어 어디를 클릭해도 다음 장으로 넘어가게 한다.
        _cutsceneRoot.AddComponent<GraphicRaycaster>();
        Button advBtn = _cutsceneRoot.AddComponent<Button>();
        advBtn.transition = Selectable.Transition.None; // 검은 배경 틴트 깜빡임 방지
        advBtn.targetGraphic = bg;
        advBtn.onClick.AddListener(AdvanceCutscene);

        // 엔딩 배경음악용 AudioSource(2D, 루프) — 컷씬 시작 시 곡 지정.
        _cutsceneAudio = _cutsceneRoot.AddComponent<AudioSource>();
        _cutsceneAudio.loop = true;
        _cutsceneAudio.playOnAwake = false;
        _cutsceneAudio.spatialBlend = 0f;
        _cutsceneAudio.volume = 0.7f;

        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        frameGo.transform.SetParent(_cutsceneRoot.transform, false);
        RectTransform frt = (RectTransform)frameGo.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _cutsceneImage = frameGo.GetComponent<Image>();
        _cutsceneImage.preserveAspect = true;   // 비율 유지(레터박스)
        _cutsceneImage.raycastTarget = false;
    }

    private void ShowFrame()
    {
        if (_cutsceneImage != null && _frames != null && _frameIndex < _frames.Length)
            _cutsceneImage.sprite = _frames[_frameIndex];
    }

    private void AdvanceCutscene()
    {
        if (!_inCutscene) return;
        // 폴링(누름 프레임)과 버튼(뗌 프레임)이 한 번의 클릭에 둘 다 호출돼도 한 장만 넘긴다.
        if (Time.unscaledTime - _lastAdvanceTime < 0.2f) return;
        _lastAdvanceTime = Time.unscaledTime;

        _frameIndex++;
        Debug.Log($"[EndingPanel] 컷씬 다음 장 → {_frameIndex + 1}/{(_frames != null ? _frames.Length : 0)}");
        if (_frames == null || _frameIndex >= _frames.Length) { EndCutscene(); return; }
        ShowFrame();
    }

    private void EndCutscene()
    {
        _inCutscene = false;
        if (_cutsceneAudio != null) _cutsceneAudio.Stop();
        if (_cutsceneRoot != null) _cutsceneRoot.SetActive(false);
        CloseAndReturn();
    }

    /// <summary>엔딩 설명 + 최종 점수/획득 호칭(매니저에서 읽어 표시만).</summary>
    private string BuildDescription(EndingResult e)
    {
        var sb = new StringBuilder();

        string tableDesc = LookupEndingDescription(e.endingId);
        if (!string.IsNullOrEmpty(tableDesc)) sb.AppendLine(tableDesc).AppendLine();

        var mgr = ScoreEconomyManager.Instance;
        if (mgr != null)
        {
            sb.AppendLine($"최종 점수: {mgr.Score}");
            sb.AppendLine($"정확도: {Mathf.RoundToInt(mgr.Accuracy * 100f)}%  ({mgr.CorrectCount}/{mgr.JudgedCount})");
            if (mgr.Titles.Count > 0)
            {
                sb.Append("획득 호칭: ");
                sb.AppendLine(string.Join(", ", mgr.Titles));
            }
        }

        // 처음으로 복귀 안내(닫기 = 타이틀로). _titleSceneName 이 설정돼 있을 때만 표시.
        if (!string.IsNullOrEmpty(_titleSceneName))
        {
            sb.AppendLine();
            sb.AppendLine("닫기(X)를 누르면 처음으로 돌아갑니다.");
        }
        return sb.ToString();
    }

    /// <summary>ending 테이블에서 설명 텍스트를 찾는다(없으면 빈 문자열). 표시 보조용.</summary>
    private static string LookupEndingDescription(string endingId)
    {
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.ending : null;
        if (t == null || t.rows == null) return string.Empty;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (r.Get("ending_id") != endingId) continue;
            // 설명 후보 컬럼들(데이터에 있으면 사용, 없으면 무시).
            string desc = r.Get("ending_desc");
            if (string.IsNullOrEmpty(desc)) desc = r.Get("description");
            if (string.IsNullOrEmpty(desc)) desc = r.Get("note");
            return desc ?? string.Empty;
        }
        return string.Empty;
    }

    private static string TypeLabel(string endingType) => endingType switch
    {
        "early"      => "조기 엔딩",
        "cumulative" => "누적 엔딩",
        "normal"     => "엔딩",
        _            => "엔딩",
    };

    private void CloseAndReturn()
    {
        _shown = false;
        if (_root != null) _root.SetActive(false);
        if (!string.IsNullOrEmpty(_titleSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(_titleSceneName);
        }
    }
}
