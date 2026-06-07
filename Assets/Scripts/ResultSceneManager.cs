using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ResultScene 컨트롤러(일자 종료 결과 화면). 기존 DayCompletePanel + DaySettlementView 가
/// 하던 정산 표시·[상점]·[다음 날] 동작을 별도 씬으로 옮긴 것이다.
///
/// 흐름: ImmigrationScene(N 검사) → [일자 종료] → ResultScene(N 결과)
///       → "다음 날"(day&lt;14) → BriefingScene(N+1)  /  (day&gt;=14) → 엔딩 패널.
///
/// 표시만 한다 — 정산 계산/엔딩 결정은 ScoreEconomyManager/EndingResolver 소유(규약 5장).
/// ScoreEconomyManager(DontDestroyOnLoad)에서 누적 잔액·점수를 읽고, "오늘 번 돈"은
/// ImmigrationManager 가 일자 종료 직전 PlayerPrefs 에 적어둔 값을 표시한다.
/// </summary>
public sealed class ResultSceneManager : MonoBehaviour
{
    private const int LastDay = 14; // 14일 × 7슬롯 = 98건
    private const int ShopUnlockDay = 7; // 상점 해금: 8일차 진입 전(=7일차 완료 결과)부터 상점 버튼 노출. 그 전 일자엔 숨김.
    private const string CurrentDayKey = "CurrentDay";           // 진행 일차(브리핑/메인메뉴 공유)
    public const string EarnedMoneyKey = "ResultEarnedMoney";    // 오늘 번 돈(ImmigrationManager 가 적음)

    [Header("정산 표시(TMP)")]
    [Tooltip("오늘 번 돈(증가분). +/- 부호 포함 표시.")]
    [SerializeField] private TMP_Text _earnedText;
    [Tooltip("누적 잔액(ScoreEconomyManager.Money).")]
    [SerializeField] private TMP_Text _balanceText;
    [Tooltip("누적 점수(ScoreEconomyManager.Score).")]
    [SerializeField] private TMP_Text _scoreText;
    [Tooltip("일자 표시(예: 'N일차 완료'). 선택 — 없어도 됨.")]
    [SerializeField] private TMP_Text _dayText;

    [Header("패널/버튼 참조")]
    [Tooltip("상점 패널(ImmigrationScene 과 동일 ShopPanel 프리팹 인스턴스).")]
    [SerializeField] private ShopPanelView _shopPanel;
    [Tooltip("14일차 종료 엔딩 패널(비활성 시작).")]
    [SerializeField] private EndingPanel _endingPanel;
    [Tooltip("상점 진입 버튼. 8일차 진입 전(=7일차 완료 결과)부터 보이고, 그 전 일자엔 숨긴다.")]
    [SerializeField] private Button _shopButton;

    private ScoreEconomyManager _economy;
    private int _day;
    private bool _endingShown; // 14일차 엔딩 중복 발동 가드

    private void Start()
    {
        EnsureEconomy(); // ResultScene 단독 실행에도 안전하도록 매니저 보장(ImmigrationManager 패턴 모방)

        _day = PlayerPrefs.GetInt(CurrentDayKey, 1);

        // 상점 구매로 잔액이 바뀌면 정산 표시를 갱신(DaySettlementView 가 구독하던 이벤트와 동일).
        if (_economy != null) _economy.OnMoneyChanged += HandleMoneyChanged;
        else Debug.LogWarning("[ResultSceneManager] ScoreEconomyManager.Instance 가 없습니다. 정산 표시 비활성.");

        UpdateSettlement();
        UpdateDayLabel();
        UpdateShopButtonVisibility();
    }

    private void OnDestroy()
    {
        if (_economy != null) _economy.OnMoneyChanged -= HandleMoneyChanged;
    }

    /// <summary>
    /// 점수·경제 매니저가 없으면 런타임 생성(ImmigrationManager.EnsureEconomy 패턴 모방).
    /// ResultScene 을 에디터에서 단독 실행해도 표시가 깨지지 않게 한다. 로직은 건드리지 않는다.
    /// </summary>
    private void EnsureEconomy()
    {
        _economy = ScoreEconomyManager.Instance;
        if (_economy == null)
        {
            var go = new GameObject("ScoreEconomyManager");
            _economy = go.AddComponent<ScoreEconomyManager>();
        }
        // 상점 백엔드도 씬에 없으면 보장(상점 UI/효과가 Instance 를 참조).
        if (ShopService.Instance == null)
        {
            new GameObject("ShopService").AddComponent<ShopService>();
        }
    }

    // ── 정산 표시 ─────────────────────────────────────────────

    /// <summary>잔액 변동(상점 구매 등) 시 표시 갱신.</summary>
    private void HandleMoneyChanged(int total, int delta) => UpdateSettlement();

    /// <summary>오늘 번 돈 / 누적 잔액 / 점수를 표시한다(읽기 전용 — 계산은 매니저 소유).</summary>
    private void UpdateSettlement()
    {
        int total = _economy != null ? _economy.Money : 0;
        int score = _economy != null ? _economy.Score : 0;
        // "오늘 번 돈"은 일자 종료 직전 ImmigrationManager 가 적어둔 증가분(없으면 0).
        int earned = PlayerPrefs.GetInt(EarnedMoneyKey, 0);

        if (_earnedText != null)
            _earnedText.text = (earned >= 0 ? "+" : string.Empty) + earned;
        if (_balanceText != null)
            _balanceText.text = total.ToString();
        if (_scoreText != null)
            _scoreText.text = score.ToString();
    }

    private void UpdateDayLabel()
    {
        if (_dayText != null) _dayText.text = $"{_day}일차 완료";
    }

    /// <summary>상점 진입 버튼 노출 제어: 8일차 진입 전(=7일차 완료 결과, _day&gt;=7)부터 보이고 그 전 일자엔 숨긴다.
    /// 참조가 비어 있으면(인스펙터 미연결) 아무것도 하지 않는다(NRE 방지).</summary>
    private void UpdateShopButtonVisibility()
    {
        if (_shopButton != null) _shopButton.gameObject.SetActive(_day >= ShopUnlockDay);
    }

    // ── 버튼 핸들러(인스펙터 onClick 바인딩) ───────────────────

    /// <summary>[상점] 버튼: 독립 ShopScene 으로 전환한다(상점에서 [돌아가기] 시 ResultScene 복귀).
    /// 돈·아이템은 DontDestroyOnLoad 싱글톤이 보유하므로 씬 전환에도 유지된다.</summary>
    public void OnShopButton()
    {
        SceneManager.LoadScene("ShopScene");
    }

    /// <summary>
    /// [다음 날] 버튼:
    ///  - day &lt; 14: CurrentDay+1 저장 후 BriefingScene 로드(브리핑 → 다음 일차 심사).
    ///  - day &gt;= 14: 14일 종료 → 누적 점수로 엔딩 결정해 엔딩 패널 표시(브리핑 로드 안 함).
    /// 점수·자금은 ScoreEconomyManager(DontDestroyOnLoad)+세이브로 씬 전환에도 유지된다.
    /// </summary>
    public void OnNextDayButton()
    {
        if (_day >= LastDay)
        {
            if (_endingShown) return; // 중복 발동 가드
            _endingShown = true;

            int score = _economy != null ? _economy.Score : 0;
            EndingResult e = EndingResolver.ResolveByScore(score);
            if (_endingPanel != null) _endingPanel.Show(e);
            else Debug.LogWarning("[ResultSceneManager] EndingPanel 참조가 없습니다. 엔딩 표시 비활성.");
            return;
        }

        int next = _day + 1;
        PlayerPrefs.SetInt(CurrentDayKey, next);
        PlayerPrefs.DeleteKey(EarnedMoneyKey); // 다음 일자에 이월되지 않도록 정리
        PlayerPrefs.Save();
        SceneManager.LoadScene("BriefingScene");
    }
}
