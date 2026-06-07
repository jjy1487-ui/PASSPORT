using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 수동(에디터 직접 배치) 상점 아이템 한 칸. 선반 위에 개별 GameObject 로 배치되는
/// 아이콘·이름·가격·구매 버튼 묶음이다. 카드 자동생성(ShopPanelView)을 쓰지 않는 ShopScene 전용.
///
/// 표시·입력 전달만 한다(규약 5장). 구매 검증/돈 차감/중복 차단은 ShopService 가 소유한다.
///  - 클릭 → ShopService.TryBuy(_shopItemId, 현재 일차).
///  - 구매/돈 변화 이벤트를 구독해 버튼 라벨(구매/보유 중/자금 부족)만 다시 칠한다.
///
/// 구독: ShopService.OnItemPurchased / OnEffectsChanged, ScoreEconomyManager.OnMoneyChanged.
/// 해제: OnDestroy(공통 규약 1장).
/// </summary>
public sealed class ManualShopItem : MonoBehaviour
{
    [Header("상점 행 식별")]
    [Tooltip("shop 테이블 shop_item_id (1~4). 이 칸이 어떤 품목인지 식별한다.")]
    [SerializeField] private string _shopItemId = "1";

    [Header("UI 참조")]
    [SerializeField] private Button   _buyButton;       // 구매 버튼
    [SerializeField] private TMP_Text _buyButtonLabel;  // "구매" / "보유 중" / "자금 부족"
    [SerializeField] private Image    _iconImage;        // 아이콘(구매=보유 시 어둡게)

    // UI-CONVENTIONS 2장 색 토큰(이 프로젝트는 UITheme 부재 — ShopItemCardView 와 동일 상수).
    private static readonly Color Neutral    = new Color32(0x7A, 0x82, 0x8C, 0xFF); // 비활성/보유
    private static readonly Color Reject     = new Color32(0xC0, 0x39, 0x2B, 0xFF); // 자금 부족 경고
    private static readonly Color TextOnDark = new Color32(0xEC, 0xEC, 0xEC, 0xFF); // 구매 가능

    private ShopService Shop => ShopService.Instance;
    private ScoreEconomyManager Economy => ScoreEconomyManager.Instance;

    // 현재 일차: 진행 일차 PlayerPrefs("CurrentDay") 폴백(ShopScene 에는 검사 컨트롤러가 없음).
    private int CurrentDay => PlayerPrefs.GetInt("CurrentDay", 1);

    private void Awake()
    {
        if (_buyButton != null) _buyButton.onClick.AddListener(HandleBuyClicked);
    }

    private void Start()
    {
        if (Shop != null)
        {
            Shop.OnItemPurchased += HandleItemPurchased;
            Shop.OnEffectsChanged += HandleEffectsChanged;
        }
        else Debug.LogWarning("[ManualShopItem] ShopService.Instance 가 없습니다. 구매 비활성.");

        if (Economy != null) Economy.OnMoneyChanged += HandleMoneyChanged;
        else Debug.LogWarning("[ManualShopItem] ScoreEconomyManager.Instance 가 없습니다. 자금 표시 비활성.");

        RefreshState();
    }

    private void OnDestroy()
    {
        if (_buyButton != null) _buyButton.onClick.RemoveListener(HandleBuyClicked);
        if (Shop != null)
        {
            Shop.OnItemPurchased -= HandleItemPurchased;
            Shop.OnEffectsChanged -= HandleEffectsChanged;
        }
        if (Economy != null) Economy.OnMoneyChanged -= HandleMoneyChanged;
    }

    /// <summary>구매 버튼 클릭 → 구매 요청. 검증·차감은 ShopService 가 수행, UI 는 결과(이벤트)로 갱신.</summary>
    private void HandleBuyClicked()
    {
        if (Shop == null || string.IsNullOrEmpty(_shopItemId)) return;
        bool ok = Shop.TryBuy(_shopItemId, CurrentDay);
        // 성공 시 OnItemPurchased/OnMoneyChanged 가 와서 자동 갱신된다. 실패도 상태만 다시 칠한다.
        if (!ok) RefreshState();
    }

    // ── 마우스오버 툴팁 효과 설명 출처(표시만) ───────────────────
    // 호버 감지는 아이콘 크기 자식(HoverArea)에 붙은 ShopItemHoverArea 가 맡고,
    // 그 컴포넌트가 아래 EffectDescription 을 읽어 공용 ShopTooltip 에 넘긴다.
    // 여기서는 shop 데이터의 effect 문자열을 그대로 돌려줄 뿐, 어떤 판정·계산도 하지 않는다.

    /// <summary>이 칸의 효과 설명(shop 데이터 effect). 없으면 null. ShopItemHoverArea 가 읽는다.</summary>
    public string EffectDescription
    {
        get
        {
            DataRow r = ResolveRow();
            return r != null ? r.Get("effect") : null;
        }
    }

    // ── 이벤트 핸들러(구독) ─────────────────────────────────────

    private void HandleItemPurchased(string effectType) => RefreshState();
    private void HandleEffectsChanged() => RefreshState();
    private void HandleMoneyChanged(int total, int delta) => RefreshState();

    /// <summary>이 칸의 구매 상태(구매가능/보유/자금부족)를 버튼 라벨·활성·색에 반영한다.</summary>
    private void RefreshState()
    {
        if (Shop == null) return;

        DataRow row = ResolveRow();
        if (row == null) return; // unlock 전이거나 행 없음 — 라벨 유지

        string effectType = row.Get("effect_type");
        int price = row.GetInt("price", 0);
        int money = Economy != null ? Economy.Money : 0;

        bool owned = Shop.IsOwned(effectType);
        bool poor  = !owned && money < price;

        // 구매(보유) 시 아이콘을 어둡게 칠해 "선택됨/보유 중"을 시각으로 표시(판정 아님 — 표시만).
        if (_iconImage != null) _iconImage.color = owned ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;

        if (_buyButton != null) _buyButton.interactable = !owned && !poor;

        if (_buyButtonLabel == null) return;
        if (owned)      { _buyButtonLabel.text = "보유 중";   _buyButtonLabel.color = Neutral; }
        else if (poor)  { _buyButtonLabel.text = "자금 부족"; _buyButtonLabel.color = Reject; }
        else            { _buyButtonLabel.text = "구매";       _buyButtonLabel.color = TextOnDark; }
    }

    /// <summary>현재 일차 기준 구매 가능 목록에서 이 칸의 shop_item_id 행을 찾는다(공개 API 만 사용).</summary>
    private DataRow ResolveRow()
    {
        if (Shop == null || string.IsNullOrEmpty(_shopItemId)) return null;
        foreach (DataRow r in Shop.GetAvailableItems(CurrentDay))
            if (r != null && r.Get("shop_item_id") == _shopItemId) return r;
        return null;
    }
}
