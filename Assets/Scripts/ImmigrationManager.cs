using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ImmigrationScene 컨트롤러. 페이드인 후 현재 일차 데이터를 로드해 심사 진행을 시작하고,
/// 일자 완료 통지를 받아 다음 일차로 진행한다(1→2→…→14→전체 종료). 같은 씬을 재사용한다.
/// 뉴스/규정집/음성기록 버튼은 현재 일차 데이터로 연결된다.
/// 주의: 씬 직렬 바인딩 유지를 위해 기존 필드명 fadePanel 을 보존한다.
/// </summary>
public sealed class ImmigrationManager : MonoBehaviour
{
    private const int FirstDay = 1;
    private const int LastDay = 14; // 14일 × 7슬롯 = 98건
    private const string CurrentDayKey = "CurrentDay"; // 브리핑씬/메인메뉴와 공유하는 진행 일차 키

    [Header("페이드")]
    [SerializeField] private CanvasGroup fadePanel;          // 기존 필드명 보존(씬 바인딩)
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("심사/팝업")]
    [SerializeField] private InspectionController inspectionController;
    [SerializeField] private NewsPopup newsPopup;
    [Tooltip("1일차 조작 안내(Space 대조) 튜토리얼 팝업. 뉴스 닫은 뒤 떴다가 닫으면 첫 손님이 입장한다.")]
    [SerializeField] private TutorialPopup tutorialPopup;
    [SerializeField] private RulebookPopup rulebookPopup;
    [SerializeField] private DialogueLogPopup dialogueLogPopup;
    [Tooltip("엔딩 화면. 비활성으로 시작하므로 매니저가 직접 띄운다(구독 누락=소프트락 방지).")]
    [SerializeField] private EndingPanel endingPanel;

    [Header("진행 설정")]
    [Tooltip("시작 일차(보통 1). 디버그/이어하기용으로 변경 가능.")]
    [SerializeField] private int startDay = FirstDay;

    [Tooltip("켜면 매 플레이 손님 인물을 (유형·정답 고정) 셔플한다. 끄면 데이터 원본 순서 그대로.")]
    [SerializeField] private bool randomizeCustomerIdentities = true;
    [Tooltip("셔플 시드. -1=매 실행 랜덤(=매번 다른 인물). 0 이상=고정 시드(테스트 재현용).")]
    [SerializeField] private int shuffleSeed = -1;

    [Tooltip("일차 시작 시 거치는 브리핑 씬 이름. '다음 날' → 이 씬 → 해당 일차 브리핑 → 다시 심사 씬.")]
    [SerializeField] private string briefingScene = "BriefingScene";

    private System.Random _rng; // 손님 셔플 난수원(시드 주입 가능)

    private readonly GameDataLoader _loader = new GameDataLoader();
    private Day1Data _data;
    private bool _endingTriggered; // 조기/최종 엔딩 1회 보장
    private int _dayStartMoney;    // 일자 시작 시점 잔액(ResultScene "오늘 번 돈" 산출 기준선)

    /// <summary>일자 종료 결과 씬 이름(정산/상점/엔딩). DayCompletePanel 패널을 대체.</summary>
    private const string ResultScene = "ResultScene";

    /// <summary>현재 진행 중인 일차(1~14).</summary>
    public int CurrentDay { get; private set; }

    /// <summary>
    /// 엔딩이 결정되면 발행(엔딩 결과). UI(3단계)가 구독해 엔딩 화면으로 전환한다.
    /// 조기엔딩(#11~#14)·누적엔딩(#15/#16 임계치)·14일 종료 점수구간 모두 이 이벤트로 통지된다.
    /// </summary>
    public event System.Action<EndingResult> OnEndingResolved;

    /// <summary>마지막으로 결정된 엔딩(없으면 IsValid=false). UI 가 폴링용으로도 사용 가능.</summary>
    public EndingResult LastEnding { get; private set; }

    private bool _controllerSubscribed; // OnDayCompleted 중복 구독 방지

    private void OnEnable()
    {
        WireController();
    }

    /// <summary>
    /// InspectionController 의 OnDayCompleted 구독을 보장한다(멱등).
    /// 씬에서 인스펙터 바인딩 시 OnEnable 시점에 컨트롤러가 이미 존재하지만,
    /// 코드/테스트가 inspectionController 를 OnEnable 이후에 주입하는 경우에도
    /// BeginDay/EnsureEconomy 진입 시 다시 호출되어 구독이 누락되지 않도록 한다.
    /// 구독이 빠지면 14일차 마지막 손님 후 엔딩 정산 체인이 끊긴다.
    /// </summary>
    private void WireController()
    {
        if (inspectionController == null || _controllerSubscribed) return;
        inspectionController.OnDayCompleted += HandleDayCompleted;
        _controllerSubscribed = true;
    }

    private void OnDisable()
    {
        if (inspectionController != null && _controllerSubscribed)
        {
            inspectionController.OnDayCompleted -= HandleDayCompleted;
            _controllerSubscribed = false;
        }
        if (_economy != null)
        {
            _economy.OnEarlyEndingTriggered -= HandleEarlyEnding;
        }
        if (newsPopup != null) newsPopup.OnClosed -= HandleStartNewsClosed;
    }

    private ScoreEconomyManager _economy;

    /// <summary>점수·경제 매니저가 씬에 없으면 런타임 생성하고 조기엔딩 트리거를 구독한다.</summary>
    private void EnsureEconomy()
    {
        _economy = ScoreEconomyManager.Instance;
        if (_economy == null)
        {
            var go = new GameObject("ScoreEconomyManager");
            _economy = go.AddComponent<ScoreEconomyManager>();
        }

        // 상점 백엔드(ShopService)도 씬에 없으면 런타임 보장(상점 UI/효과가 Instance 를 참조).
        //  ScoreEconomyManager 와 동일한 '매니저 인스턴스 보장' 패턴 — 백엔드 로직은 건드리지 않는다.
        if (ShopService.Instance == null)
        {
            new GameObject("ShopService").AddComponent<ShopService>();
        }
        _economy.OnEarlyEndingTriggered -= HandleEarlyEnding;
        _economy.OnEarlyEndingTriggered += HandleEarlyEnding;

        // 컨트롤러가 같은 정산 허브를 쓰도록 명시 주입(전역 Instance 해석 타이밍에 의존하지 않음).
        WireController();
        if (inspectionController != null) inspectionController.SetEconomy(_economy);
    }

    /// <summary>조기/누적 엔딩 트리거(#11~#16) 수신 → 발동 조건 충족 시 즉시 엔딩.</summary>
    private void HandleEarlyEnding(string eventId)
    {
        if (_endingTriggered) return;
        EndingResult e = EndingResolver.ResolveEarly(eventId, ScoreEconomyManager.Instance);
        if (e.IsValid) RaiseEnding(e);
    }

    private void RaiseEnding(EndingResult e)
    {
        if (_endingTriggered) return;
        _endingTriggered = true;
        LastEnding = e;
        Debug.Log($"[ImmigrationManager] 엔딩 결정: {e.endingId} ({e.endingName}) type={e.endingType} trigger={e.triggerKey}");
        // 심사 컨트롤러 정지: 다음 손님/일자완료 패널을 막아 엔딩 패널이 화면을 점유하게 한다
        //  (조기엔딩 때 DayCompletePanel 이 대신 뜨던 문제 차단).
        if (inspectionController != null) inspectionController.HaltForEnding();
        // 비활성으로 시작하는 엔딩 패널은 이벤트 구독을 못 거므로 직접 띄운다(소프트락 방지).
        if (endingPanel != null) endingPanel.Show(e);
        OnEndingResolved?.Invoke(e);
        // 엔딩 화면 전환은 UI(3단계) 가 OnEndingResolved 를 구독해 처리한다.
    }

    private void Start()
    {
        EnsureEconomy(); // 점수·경제 매니저 보장 + 조기엔딩 구독

        // 일차 결정 우선순위:
        //  1) 활성 씬 이름이 'Day{N}Scene' 패턴이면 그 N을 일차로 강제하고 PlayerPrefs 도 동기화한다.
        //     (에디터에서 DayN 씬을 직접 열어 테스트해도 그 일차로 시작 + 진행 상태 일관)
        //  2) 패턴이 아니면(폴백) 브리핑씬/메인메뉴와 공유하는 PlayerPrefs(CurrentDay), 없으면 startDay.
        int day;
        int sceneDay = ParseDayFromSceneName(SceneManager.GetActiveScene().name);
        if (sceneDay > 0)
        {
            day = sceneDay;
            PlayerPrefs.SetInt(CurrentDayKey, day); // 흐름 상태(다음 씬 로드 등)와 일관되게 동기화
            PlayerPrefs.Save();
        }
        else
        {
            // → 브리핑씬이 N일차를 띄운 뒤 이 씬으로 오면 그대로 N일차로 시작된다.
            day = PlayerPrefs.GetInt(CurrentDayKey, startDay);
        }
        CurrentDay = Mathf.Clamp(day, FirstDay, LastDay);

        // 1일차 시작 = 새 게임의 첫날 → 누적 진행(점수/정확도/누적 엔딩 카운터)은 0이어야 한다.
        //  메인 메뉴 '게임 시작'을 거치지 않고 ImmigrationScene/DayNScene 을 직접 Play 하면
        //  이전 회차 세이브가 복원돼(누적 엔딩 #16 카운터 등) 1일차에 누적 엔딩이 조기 발동한다.
        //  1일차엔 정상적으로 누적이 0이므로 여기서 리셋해도 안전(호칭/아이템 메타는 보존).
        if (CurrentDay <= FirstDay)
        {
            if (_economy != null) _economy.ResetProgressKeepMeta();
            // 새 게임 첫날 → 손님 전역 중복방지 기록 초기화(이후 14일 동안 같은 인물 재등장 방지).
            CustomerRoster.BeginPlaythrough();
        }

        BeginDay(CurrentDay, resetGold: true);
        // 뉴스 자동 표시 + "뉴스 닫으면 첫 손님 입장" 게이트는 BeginDay 안에서 처리한다.
        StartCoroutine(FadeIn()); // 페이드는 시각 연출 전용
    }

    /// <summary>
    /// 활성 씬 이름이 'Day{N}Scene'(N=1~14) 패턴이면 클램프한 N을 반환, 아니면 0(폴백).
    /// 대소문자 무시. 예: "Day5Scene" → 5, "ImmigrationScene" → 0.
    /// </summary>
    private static int ParseDayFromSceneName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return 0;
        Match m = Regex.Match(sceneName, @"^Day(\d+)Scene$", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        if (!int.TryParse(m.Groups[1].Value, out int n)) return 0;
        return Mathf.Clamp(n, FirstDay, LastDay);
    }

    /// <summary>지정 일차 데이터를 로드해 심사를 시작한다.</summary>
    /// <param name="openNews">일차 전환 시 해당 일자 뉴스를 자동으로 띄울지(검수 점프는 false 로 끈다).</param>
    private void BeginDay(int day, bool resetGold, bool openNews = true)
    {
        _data = _loader.Load(day);
        if (_data == null)
        {
            // 데이터가 아직 없는 일차(미구현). 진행 중단하고 명시적으로 로그.
            Debug.LogWarning($"[ImmigrationManager] day{day} 데이터를 로드하지 못해 진행을 멈춥니다.");
            return;
        }

        // 매 플레이 난수원(시드 ≥0이면 재현, -1이면 매번 다름). 셔플·확률변형 공용.
        _rng ??= shuffleSeed >= 0 ? new System.Random(shuffleSeed) : new System.Random();

        // 인물 셔플(옵션): 슬롯별 유형·정답은 그대로 두고 같은 풀에서 다른 인물로 교체(매 플레이 다른 얼굴).
        if (randomizeCustomerIdentities) CustomerRoster.Reassign(_data, _rng);

        // 확률 변형(항상): 인물은 고정(셔플과 독립). 박철수 등 valid_chance 손님의 서류 정상/불량만 매 플레이 굴린다.
        CustomerRoster.RollVariants(_data, _rng);

        CurrentDay = day;
        // ResultScene "오늘 번 돈" 산출 기준선: 일자 시작 시점 잔액을 캐시한다.
        //  (InspectionController.Initialize 가 BeginDay/SettleDay 로 잔액을 바꾸기 전에 기록.)
        _dayStartMoney = _economy != null ? _economy.Money : 0;
        WireController(); // 구독 누락 방지(늦은 주입 대비, 멱등)

        // 하루 시작에 그날 뉴스를 자동으로 띄울지:
        //  - 게임 첫 진입(resetGold=true): 2일차부터(1일차는 브리핑만).
        //  - 일차 전환(resetGold=false)+openNews: 띄움.  - QA 점프(openNews=false): 끔.
        bool showNews = openNews && (resetGold ? day > FirstDay : true);
        // 1일차 조작 안내 튜토리얼(QA 점프 땐 끔). 뉴스가 없는 1일차에서 첫 안내로 뜬다.
        bool showTutorial = openNews && day == FirstDay;
        bool gate = showNews || showTutorial; // 둘 중 하나라도 있으면 첫 손님은 그 뒤에 등장

        if (inspectionController != null)
        {
            if (_economy != null) inspectionController.SetEconomy(_economy);
            // 게이트가 있으면 첫 손님을 '팝업 닫은 뒤' 등장시킨다(autoShowFirst:false → BeginInspection 대기).
            inspectionController.Initialize(_data, resetGold, autoShowFirst: !gate);
        }
        Debug.Log($"[ImmigrationManager] {day}일차 시작");

        if (gate) RunStartSequence(showNews, showTutorial);
    }

    private System.Action _afterNewsAction; // 시작 뉴스 닫힘 → 실행할 다음 단계(튜토리얼 or 첫 손님)

    /// <summary>하루 시작 시퀀스: (뉴스) → (1일차 튜토리얼) → 첫 손님 입장. 각 단계는 '닫기'로 진행한다.
    /// 데이터/참조가 없으면 그 단계는 건너뛴다(소프트락 방지).</summary>
    private void RunStartSequence(bool showNews, bool showTutorial)
    {
        System.Action proceed = () => { if (inspectionController != null) inspectionController.BeginInspection(); };
        System.Action afterNews = () =>
        {
            if (showTutorial && tutorialPopup != null) tutorialPopup.Open(proceed); // 튜토리얼 닫으면 첫 손님
            else proceed();
        };

        bool hasNews = newsPopup != null && _data != null && _data.news != null && _data.news.Length > 0;
        if (showNews && hasNews)
        {
            _afterNewsAction = afterNews;
            newsPopup.OnClosed -= HandleStartNewsClosed; // 중복 구독 방지
            newsPopup.OnClosed += HandleStartNewsClosed;
            newsPopup.Open(_data.news);
        }
        else
        {
            afterNews(); // 뉴스 없으면 곧장 튜토리얼(또는 첫 손님)
        }
    }

    /// <summary>하루 시작 뉴스가 닫히면 다음 단계(튜토리얼/첫 손님) 진행. 1회만 동작.</summary>
    private void HandleStartNewsClosed()
    {
        if (newsPopup != null) newsPopup.OnClosed -= HandleStartNewsClosed;
        System.Action a = _afterNewsAction; _afterNewsAction = null;
        a?.Invoke();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ── [QA/검수 전용 · 릴리스 빌드 미포함] 손님 점프 ──────────────────
    //  처음부터 다시 플레이하지 않고 "N일차의 M번째 손님"으로 즉시 건너뛴다.
    //  같은 일차 안의 이동은 데이터를 다시 로드하지 않아(셔플/확률변형 보존) 이전/다음 손님이 그대로 유지된다.
    //  날짜를 바꿀 때만 해당 일차 데이터를 로드한다. 브리핑·뉴스·정산 흐름은 건너뛴다(정식 점수와 무관).

    /// <summary>[QA] 임의의 일차(1~14)·손님(1~7)으로 즉시 점프. 화면 오버레이(QaJumpOverlay)가 호출한다.</summary>
    public void DebugJumpTo(int day, int slot)
    {
        day = Mathf.Clamp(day, FirstDay, LastDay);
        _endingTriggered = false; // 점프 시 엔딩 차단 해제(이전 점프에서 엔딩이 떴을 수 있음)

        // 날짜가 바뀌거나 아직 데이터가 없을 때만 해당 일차를 새로 로드한다(같은 날이면 셔플/변형 보존).
        if (day != CurrentDay || _data == null)
        {
            EnsureEconomy();
            PlayerPrefs.SetInt(CurrentDayKey, day);
            PlayerPrefs.Save();
            BeginDay(day, resetGold: false, openNews: false);
        }

        if (inspectionController != null) inspectionController.DebugJumpToSlot(slot - 1);
    }

    /// <summary>[QA] 현재 일차를 다시 로드해 변이 강제 모드(CustomerRoster.ForceMode)를 즉시 반영한다(현재 손님 번호 유지).</summary>
    public void DebugReapplyCurrentDay()
    {
        if (_data == null) return;
        int slot1 = CurrentSlot1Based;
        BeginDay(CurrentDay, resetGold: false, openNews: false);
        if (inspectionController != null)
            inspectionController.DebugJumpToSlot(Mathf.Max(0, slot1 - 1));
    }

    /// <summary>[QA] 현재 손님 번호(1-base, 손님 없으면 0). 오버레이 표시용.</summary>
    public int CurrentSlot1Based => inspectionController != null ? inspectionController.CurrentSlotIndex + 1 : 0;

    /// <summary>[QA] 현재 일차 손님 수(데이터 없으면 0). 오버레이 표시용.</summary>
    public int CurrentDayCustomerCount => inspectionController != null ? inspectionController.CustomerCount : 0;
#endif

    /// <summary>
    /// InspectionController 가 일자 완료를 통지하면 결과 씬(ResultScene)으로 전환한다.
    /// 모든 일차(1~14)가 동일하게 ResultScene 으로 간다(정산/상점 표시 → '다음 날').
    /// 14일차 종료 엔딩은 ResultScene 의 '다음 날' 버튼이 점수 구간으로 결정한다(여기서 안 띄움).
    /// 단 조기 엔딩(#11~#16)이 이미 검사 씬에서 발동했으면 ResultScene 으로 가지 않는다(엔딩 패널 점유 유지).
    /// </summary>
    private void HandleDayCompleted(int completedDay)
    {
        // 조기 엔딩이 이미 발동(검사 씬에서 엔딩 패널 표시 중) → 결과 씬으로 넘어가지 않는다.
        if (_endingTriggered)
        {
            Debug.Log($"[ImmigrationManager] day{completedDay} 완료지만 엔딩 발동 상태 — ResultScene 전환 생략.");
            return;
        }

        // ResultScene 의 "오늘 번 돈" 표시용: 일자 시작 잔액 대비 증가분을 PlayerPrefs 에 적어둔다
        //  (씬 전환으로 기준선이 소실되므로 여기서 산출. 누적 잔액/점수는 ScoreEconomyManager 가 보유).
        int total = _economy != null ? _economy.Money : 0;
        PlayerPrefs.SetInt(ResultSceneManager.EarnedMoneyKey, total - _dayStartMoney);
        PlayerPrefs.Save();

        Debug.Log($"[ImmigrationManager] day{completedDay} 완료 → ResultScene 전환.");
        SceneManager.LoadScene(ResultScene);
    }

    /// <summary>
    /// [더 이상 사용 안 함] '다음 날' 진행 로직은 ResultSceneManager.OnNextDayButton 으로 이전됐다.
    /// (일자 종료가 DayCompletePanel 패널 → 별도 ResultScene 으로 바뀌면서 버튼도 그 씬으로 이동.)
    /// 하위 호환을 위해 남겨두지만 ImmigrationScene 에서는 더 이상 어떤 버튼도 이 메서드를 호출하지 않는다.
    /// </summary>
    public void OnNextDayButton()
    {
        if (_endingTriggered)
        {
            Debug.Log("[ImmigrationManager] 엔딩 발동 상태 — '다음 날' 입력 무시.");
            return;
        }
        if (CurrentDay >= LastDay)
        {
            Debug.Log("[ImmigrationManager] 더 진행할 일차가 없습니다(전체 종료).");
            return;
        }
        // 다음 일차를 저장하고 브리핑씬을 거쳐 입장한다(날짜별 브리핑 → ImmigrationScene 재진입).
        //  점수·자금·카운터는 ScoreEconomyManager(DontDestroyOnLoad)+세이브로 씬 리로드에도 유지된다.
        int next = CurrentDay + 1;
        PlayerPrefs.SetInt(CurrentDayKey, next);
        PlayerPrefs.Save();
        SceneManager.LoadScene(briefingScene);
    }

    /// <summary>뉴스 버튼: 현재 일차 뉴스 팝업.</summary>
    public void OnNewsButton() => OpenNews();

    /// <summary>현재 일차 뉴스 팝업을 연다(자동 전환·수동 버튼 공용). 데이터/참조 없으면 무시.</summary>
    private void OpenNews()
    {
        if (_data != null && newsPopup != null)
        {
            newsPopup.Open(_data.news);
        }
    }

    /// <summary>음성기록 버튼: 현재 손님 대화 기록 팝업.</summary>
    public void OnHandsetButton()
    {
        if (inspectionController != null && dialogueLogPopup != null)
        {
            dialogueLogPopup.OpenLines(inspectionController.GetDialogueLines());
        }
    }

    /// <summary>규정집 버튼: 현재 일차에 적용되는 규정 팝업(기초 규정 + 활성 이벤트 규정).</summary>
    public void OnRulebookButton()
    {
        if (rulebookPopup != null)
        {
            rulebookPopup.Open(BuildActiveRules());
        }
    }

    /// <summary>
    /// 규정집에 표시할 규정을 만든다 = day1..현재일차에 "도입"된 규정 중 "아직 유효한" 것만(ruleId 중복 제거, 등장 순서 유지).
    /// - 도입: 각 dayN.json 은 그날 새로 생기는 규정만 담으므로(day2/14 는 비기도 한다) day1..현재까지 훑어 누적한다.
    ///   누적하지 않으면 day1 의 기초 규정(여권/신분/사진/금지품)이 2일차부터 사라진다.
    /// - 종료: 이벤트성 규정(endDay>0, 예 PCR=7)은 그 일차가 지나면 제외한다(시작 전엔 도입 전이라 자연히 없음).
    ///   → 기초 규정은 항상, 이벤트 규정은 활성 기간에만 표시된다.
    /// </summary>
    private List<RuleData> BuildActiveRules()
    {
        var list = new List<RuleData>();
        var seen = new HashSet<int>();
        for (int d = FirstDay; d <= CurrentDay; d++)
        {
            // 현재 일차는 이미 로드된 _data 를 재사용하고, 이전 일차는 그때그때 로드한다(규정집 열 때만, 가벼움).
            Day1Data dd = (d == CurrentDay && _data != null) ? _data : _loader.Load(d);
            if (dd?.rules == null) continue;
            foreach (RuleData r in dd.rules)
            {
                if (r == null) continue;
                if (r.endDay > 0 && CurrentDay > r.endDay) continue; // 이벤트 종료 규정 제외(예: PCR 은 8일차부터 안 보임)
                if (r.ruleId != 0 && !seen.Add(r.ruleId)) continue;  // 같은 규정 재등장 시 1회만(ruleId 0=미지정은 그대로 추가)
                list.Add(r);
            }
        }
        return list;
    }

    private IEnumerator FadeIn()
    {
        if (fadePanel == null)
        {
            yield break;
        }

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
}
