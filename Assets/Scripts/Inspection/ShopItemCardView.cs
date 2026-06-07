using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 상점 아이템 카드 한 장. shop 테이블 행(DataRow) 하나를 받아 이름/분류/가격/효과를 표시하고
/// 구매 버튼을 노출한다. 구매·잠금 판정은 ShopService 가 소유 — 카드는 표시와 클릭 전달만 한다(규약 5장).
///
/// 상태는 외부(ShopPanelView)가 결정해 주입한다: 구매 가능 / 보유 완료 / 돈 부족 / 잠김.
/// 비활성 버튼은 숨기지 않고 흐리게 표시(UI-CONVENTIONS 1장).
/// </summary>
public sealed class ShopItemCardView : MonoBehaviour
{
    /// <summary>카드의 표시/구매 상태.</summary>
    public enum State { Buyable, Owned, NotEnoughMoney, Locked }

    [Header("UI 참조")]
    [SerializeField] private Image    _iconImage;      // icon (Resources/Shop/<icon>) — 없으면 숨김
    [SerializeField] private TMP_Text _nameText;       // item_name
    [SerializeField] private TMP_Text _categoryText;   // category
    [SerializeField] private TMP_Text _priceText;      // price (Money/Gold 색)
    [SerializeField] private TMP_Text _effectText;     // effect(설명)
    [SerializeField] private Button   _buyButton;
    [SerializeField] private TMP_Text _buyButtonLabel; // "구매" / "보유" / "잠김" / "돈 부족"

    // UI-CONVENTIONS 2장 색 토큰(이 프로젝트는 UITheme 부재 — ScoreHudView 와 동일하게 상수로 둠).
    private static readonly Color MoneyGold  = new Color32(0xD4, 0xA0, 0x17, 0xFF); // 가격
    private static readonly Color Neutral    = new Color32(0x7A, 0x82, 0x8C, 0xFF); // 비활성/보유
    private static readonly Color Reject     = new Color32(0xC0, 0x39, 0x2B, 0xFF); // 돈 부족 경고
    private static readonly Color TextOnDark = new Color32(0xEC, 0xEC, 0xEC, 0xFF);

    private string _shopItemId;
    private Action<string> _onBuy; // ShopPanelView 가 주입(구매 요청 콜백)

    private void Awake()
    {
        if (_buyButton != null) _buyButton.onClick.AddListener(HandleBuyClicked);
    }

    private void OnDestroy()
    {
        if (_buyButton != null) _buyButton.onClick.RemoveListener(HandleBuyClicked);
    }

    /// <summary>shop 행과 구매 콜백을 바인딩한다. 상태는 <see cref="SetState"/>로 별도 갱신.</summary>
    public void Bind(DataRow row, Action<string> onBuy)
    {
        if (row == null) return;
        _shopItemId = row.Get("shop_item_id");
        _onBuy = onBuy;

        BindIcon(row.Get("icon"));

        if (_nameText != null) _nameText.text = Safe(row.Get("item_name"));
        if (_categoryText != null) _categoryText.text = Safe(row.Get("category"));
        if (_priceText != null)
        {
            _priceText.text = row.GetInt("price", 0).ToString();
            _priceText.color = MoneyGold;
        }
        if (_effectText != null) _effectText.text = Safe(row.Get("effect"));
    }

    /// <summary>
    /// shop 행의 icon 키로 아이콘 스프라이트를 로드해 표시한다(규약: Resources/Shop/&lt;icon&gt;).
    /// 아이콘 키가 비었거나 로드 실패면 아이콘 Image 를 숨긴다. 비율 유지(preserveAspect).
    /// </summary>
    private void BindIcon(string iconKey)
    {
        if (_iconImage == null) return;

        Sprite sprite = string.IsNullOrEmpty(iconKey)
            ? null
            : Resources.Load<Sprite>("Shop/" + iconKey);

        if (sprite != null)
        {
            _iconImage.sprite = sprite;
            _iconImage.preserveAspect = true;
            _iconImage.enabled = true;
            _iconImage.gameObject.SetActive(true);
        }
        else
        {
            if (!string.IsNullOrEmpty(iconKey))
                Debug.LogWarning($"[ShopItemCardView] 아이콘 로드 실패: Resources/Shop/{iconKey}");
            _iconImage.gameObject.SetActive(false);
        }
    }

    /// <summary>이 카드의 shop_item_id(상위 패널의 상태 갱신 매칭용).</summary>
    public string ShopItemId => _shopItemId;

    /// <summary>구매/잠금 상태를 반영한다. 버튼 라벨·활성·색을 한 번에 갱신한다.</summary>
    public void SetState(State state)
    {
        if (_buyButton != null) _buyButton.interactable = state == State.Buyable;

        if (_buyButtonLabel == null) return;
        switch (state)
        {
            case State.Buyable:
                _buyButtonLabel.text = "구매";
                _buyButtonLabel.color = TextOnDark;
                break;
            case State.Owned:
                _buyButtonLabel.text = "보유 중";
                _buyButtonLabel.color = Neutral;
                break;
            case State.NotEnoughMoney:
                _buyButtonLabel.text = "자금 부족";
                _buyButtonLabel.color = Reject;
                break;
            case State.Locked:
                _buyButtonLabel.text = "잠김";
                _buyButtonLabel.color = Neutral;
                break;
        }
    }

    private void HandleBuyClicked()
    {
        if (!string.IsNullOrEmpty(_shopItemId)) _onBuy?.Invoke(_shopItemId);
    }

    private static string Safe(string v) => string.IsNullOrEmpty(v) ? "-" : v;
}
