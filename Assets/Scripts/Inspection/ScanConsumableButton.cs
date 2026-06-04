using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 상점 1회성 'X-ray 정밀 스캔권'(XRAY_SCAN) 사용 버튼. 보유 수량을 표시하고, 누르면 1회 소비해
/// 현재 손님의 X-ray 결과 패널을 (대조 잠금 해제 없이도) 강제로 열람한다.
///
/// 일반 ScanRequestButton 은 뉴스/규정 단서를 대조해 잠금 해제돼야 열리지만,
/// 정밀 스캔권은 그 잠금을 우회하는 1회성 아이템이다. 단, 손님이 X-ray 데이터를 가진 경우에만 의미가 있다.
///
/// 소비 판정·차감은 ShopService 가 소유(GetConsumableCount/TryConsume) — 버튼은 표시·열람 전달만.
/// 보유 0이거나 손님에게 X-ray 데이터가 없으면 흐리게(비활성, 숨기지 않음 — UI-CONVENTIONS 1장).
/// </summary>
public sealed class ScanConsumableButton : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private InspectionController _controller;
    [SerializeField] private ScanResultPanel _xrayPanel;  // 정밀 스캔권으로 강제 열람할 X-ray 패널
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _countText;         // 보유 수량(예: "정밀 스캔 x2")

    private ShopService Shop => ShopService.Instance;

    private void Awake()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);
    }

    private void Start()
    {
        if (Shop != null) Shop.OnEffectsChanged += HandleEffectsChanged;
        else Debug.LogWarning("[ScanConsumableButton] ShopService.Instance 가 없습니다. 스캔권 비활성.");

        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[ScanConsumableButton] _controller 가 연결되지 않았습니다.");

        if (_xrayPanel == null) Debug.LogWarning("[ScanConsumableButton] _xrayPanel 이 연결되지 않았습니다.");

        Refresh();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
        if (Shop != null) Shop.OnEffectsChanged -= HandleEffectsChanged;
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
    }

    private void HandleEffectsChanged() => Refresh();
    private void HandleCustomerChanged() => Refresh();

    private void HandleClick()
    {
        if (Shop == null || _xrayPanel == null) return;
        if (!_xrayPanel.HasData) return;            // 이 손님은 X-ray 데이터 없음 — 소비 낭비 방지
        if (!Shop.TryConsume(ShopService.FxXrayScan)) return; // 보유 0이면 false

        _xrayPanel.Open();                          // 잠금 우회 강제 열람
        Refresh();
    }

    /// <summary>보유 수량 + 현재 손님 X-ray 보유 여부로 표시/활성을 갱신한다.</summary>
    private void Refresh()
    {
        int count = Shop != null ? Shop.GetConsumableCount(ShopService.FxXrayScan) : 0;
        bool hasXrayCustomer = _xrayPanel != null && _xrayPanel.HasData;
        bool canUse = count > 0 && hasXrayCustomer;

        if (_button != null) _button.interactable = canUse;
        if (_countText != null) _countText.text = $"정밀 스캔 x{count}";
        // 보유 0이면 버튼을 숨긴다(상점 미구매 시 노출 안 함). 보유 후엔 흐리게라도 상태를 보인다.
        gameObject.SetActive(count > 0);
    }
}
