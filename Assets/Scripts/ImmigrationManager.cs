using System.Collections;
using UnityEngine;

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

    [Header("페이드")]
    [SerializeField] private CanvasGroup fadePanel;          // 기존 필드명 보존(씬 바인딩)
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("심사/팝업")]
    [SerializeField] private InspectionController inspectionController;
    [SerializeField] private NewsPopup newsPopup;
    [SerializeField] private RulebookPopup rulebookPopup;
    [SerializeField] private DialogueLogPopup dialogueLogPopup;

    [Header("진행 설정")]
    [Tooltip("시작 일차(보통 1). 디버그/이어하기용으로 변경 가능.")]
    [SerializeField] private int startDay = FirstDay;

    private readonly GameDataLoader _loader = new GameDataLoader();
    private Day1Data _data;
    private bool _endingTriggered; // 조기/최종 엔딩 1회 보장

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
        OnEndingResolved?.Invoke(e);
        // 엔딩 화면 전환은 UI(3단계) 가 OnEndingResolved 를 구독해 처리한다.
    }

    private void Start()
    {
        EnsureEconomy(); // 점수·경제 매니저 보장 + 조기엔딩 구독

        CurrentDay = Mathf.Clamp(startDay, FirstDay, LastDay);
        BeginDay(CurrentDay, resetGold: true);

        StartCoroutine(FadeIn()); // 페이드는 시각 연출 전용
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

        CurrentDay = day;
        WireController(); // 구독 누락 방지(늦은 주입 대비, 멱등)
        if (inspectionController != null)
        {
            if (_economy != null) inspectionController.SetEconomy(_economy);
            inspectionController.Initialize(_data, resetGold);
        }
        Debug.Log($"[ImmigrationManager] {day}일차 시작");
    }

    /// <summary>InspectionController 가 일자 완료를 통지하면 다음 일차로 진행하거나 종료한다.</summary>
    private void HandleDayCompleted(int completedDay)
    {
        if (completedDay >= LastDay)
        {
            // 14일 종료: 누적 점수 → ending 구간 분기. 조기엔딩이 이미 발동했으면 그대로 둔다.
            if (!_endingTriggered)
            {
                int finalScore = ScoreEconomyManager.Instance != null ? ScoreEconomyManager.Instance.Score : 0;
                RaiseEnding(EndingResolver.ResolveByScore(finalScore));
            }
            return;
        }

        // 14일 미만: 자동 진행하지 않는다. InspectionController.ShowDayComplete 가 띄운
        // 일자완료(정산) 패널을 그대로 유지한 채 "다음 날" 버튼(→ OnNextDayButton) 입력을 기다린다.
        // 이렇게 해야 유저가 "오늘 번 돈/누적 잔액"을 정산 패널에서 확인한 뒤 진행한다.
        // 패널 비활성화는 다음 날 BeginDay() 시작 시 일괄 처리된다(InspectionController.Initialize).
        Debug.Log($"[ImmigrationManager] day{completedDay} 정산 대기 — '다음 날' 버튼(OnNextDayButton) 입력 대기.");
    }

    /// <summary>
    /// 일자완료(정산) 패널의 "다음 날" 버튼에 바인딩되는 공개 메서드.
    /// 정산 패널 확인 후 호출되어 다음 일차를 시작한다(골드 누적 유지).
    /// 엔딩이 이미 발동(조기/누적/14일)했으면 무시한다.
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
        // BeginDay → InspectionController.Initialize 가 _dayCompleteRoot.SetActive(false) 로
        // 정산 패널을 닫고 새 일차 첫 손님을 띄운다.
        BeginDay(CurrentDay + 1, resetGold: false);
    }

    /// <summary>뉴스 버튼: 현재 일차 뉴스 팝업.</summary>
    public void OnNewsButton()
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
