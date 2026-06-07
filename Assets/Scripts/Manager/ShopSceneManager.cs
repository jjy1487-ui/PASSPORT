using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 독립 ShopScene 컨트롤러. ResultScene 에서 [상점] 선택 시 진입한다.
/// 상점 백엔드(ShopService)/경제(ScoreEconomyManager) 싱글톤을 보장하고,
/// 상점 패널을 열며, [돌아가기]로 ResultScene 으로 복귀한다.
/// 돈·아이템 상태는 DontDestroyOnLoad 싱글톤이 보유하므로 씬 전환에도 유지된다.
/// </summary>
public sealed class ShopSceneManager : MonoBehaviour
{
    [SerializeField] private ShopPanelView _shopPanel;   // 씬에 둔 상점 패널(프리팹 인스턴스)
    [SerializeField] private Button _backButton;         // [돌아가기] 버튼
    [Tooltip("[돌아가기] 시 복귀할 씬 이름.")]
    [SerializeField] private string _returnScene = "ResultScene";
    [Tooltip("진입 시 ShopPanelView 의 자동 카드 생성을 열지 여부. 수동 배치 아이템을 쓰는 ShopScene 에서는 끈다.")]
    [SerializeField] private bool _autoOpenShopPanel = true;

    private void Awake()
    {
        // 직접 ShopScene 을 열어 테스트해도 동작하도록 싱글톤 보장(정상 흐름에선 이미 존재).
        if (ScoreEconomyManager.Instance == null)
            new GameObject("ScoreEconomyManager").AddComponent<ScoreEconomyManager>();
        if (ShopService.Instance == null)
            new GameObject("ShopService").AddComponent<ShopService>();
    }

    private void Start()
    {
        // _autoOpenShopPanel 이 true 일 때만 자동 카드 생성을 연다.
        // 수동 배치 아이템 방식의 ShopScene 에서는 false 로 두어 중복 카드 생성을 막는다.
        if (_autoOpenShopPanel && _shopPanel != null) _shopPanel.Open(); // 진입하면 바로 상점 표시
        if (_backButton != null) _backButton.onClick.AddListener(Back);
    }

    private void OnDestroy()
    {
        if (_backButton != null) _backButton.onClick.RemoveListener(Back);
    }

    /// <summary>[돌아가기] 버튼: ResultScene 으로 복귀.</summary>
    public void Back()
    {
        SceneManager.LoadScene(_returnScene);
    }
}
