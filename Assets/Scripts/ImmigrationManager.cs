using System.Collections;
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
        if (CurrentDay <= FirstDay && _economy != null) _economy.ResetProgressKeepMeta();

        BeginDay(CurrentDay, resetGold: true);

        // 2일차 이후에는 그날 뉴스를 자동 표시(브리핑 다음 단계). 1일차는 브리핑만.
        if (CurrentDay > FirstDay) OpenNews();

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
    private void BeginDay(int day, bool resetGold)
    {
        _data = _loader.Load(day);
        if (_data == null)
        {
            // 데이터가 아직 없는 일차(미구현). 진행 중단하고 명시적으로 로그.
            Debug.LogWarning($"[ImmigrationManager] day{day} 데이터를 로드하지 못해 진행을 멈춥니다.");
            return;
        }

        // 인물 셔플: 슬롯별 유형·정답은 그대로 두고 같은 풀에서 다른 인물로 교체(매 플레이 다른 얼굴).
        if (randomizeCustomerIdentities)
        {
            _rng ??= shuffleSeed >= 0 ? new System.Random(shuffleSeed) : new System.Random();
            CustomerRoster.Reassign(_data, _rng);
        }

        CurrentDay = day;
        // ResultScene "오늘 번 돈" 산출 기준선: 일자 시작 시점 잔액을 캐시한다.
        //  (InspectionController.Initialize 가 BeginDay/SettleDay 로 잔액을 바꾸기 전에 기록.)
        _dayStartMoney = _economy != null ? _economy.Money : 0;
        WireController(); // 구독 누락 방지(늦은 주입 대비, 멱등)
        if (inspectionController != null)
        {
            if (_economy != null) inspectionController.SetEconomy(_economy);
            inspectionController.Initialize(_data, resetGold);
        }
        Debug.Log($"[ImmigrationManager] {day}일차 시작");

        // 일차 전환(이전 일차 종료 → 다음 일차 진입) 사이에 해당 일자 뉴스 자동 표시.
        // 최초 게임 시작(resetGold=true)에는 띄우지 않는다(수동 뉴스 버튼/브리핑으로 처리).
        if (!resetGold)
        {
            OpenNews();
        }
    }

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

    /// <summary>규정집 버튼: 현재 일차 규정 팝업.</summary>
    public void OnRulebookButton()
    {
        if (_data != null && rulebookPopup != null)
        {
            rulebookPopup.Open(_data.rules);
        }
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
