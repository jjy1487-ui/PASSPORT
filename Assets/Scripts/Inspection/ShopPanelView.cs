using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 상점 패널. 일자 종료 정산(DayCompletePanel) 시점에 열 수 있다. shop 테이블의 구매 가능 품목을
/// 카드로 나열하고(이름·분류·가격·효과), 구매 버튼으로 ShopService.TryBuy 를 호출한다.
/// 보유 돈(Money)만 노출한다(점수 비공개 — 규약 4·5장).
///
/// 표시·입력 전달만 한다. 잠금/돈부족/중복 검증과 차감은 ShopService/ScoreEconomyManager 가 소유.
/// 닫기 = X·우상단(UI-CONVENTIONS 1장). 비활성 카드는 숨기지 않고 흐리게.
///
/// 구독: ShopService.OnItemPurchased / OnEffectsChanged, ScoreEconomyManager.OnMoneyChanged.
/// 해제: OnDestroy(공통 규약 1장).
/// </summary>
public sealed class ShopPanelView : MonoBehaviour
{
    [Header("진행 통지원(현재 일차 산출)")]
    [SerializeField] private InspectionController _controller;

    [Header("UI 참조")]
    [SerializeField] private GameObject _root;            // 비활성 시작(정산 시점에 열기)
    [SerializeField] private Button _openButton;          // 정산 패널의 '상점' 진입 버튼
    [SerializeField] private Button _closeButton;         // X, 우상단
    [SerializeField] private TMP_Text _moneyText;         // 보유 자금(Money/Gold 색)
    [SerializeField] private TMP_Text _emptyText;         // 구매 가능 품목 없을 때 안내

    [Header("아이템 카드")]
    [SerializeField] private Transform _cardContainer;    // 카드가 놓일 부모(Layout Group)
    [SerializeField] private ShopItemCardView _cardTemplate; // 비활성 템플릿(복제 원본)

    // UI-CONVENTIONS 2장: 돈 = Money/Gold(#D4A017). 점수색과 구분(하드코딩 회피 — 프로젝트 UITheme 부재).
    private static readonly Color MoneyGold = new Color32(0xD4, 0xA0, 0x17, 0xFF);

    private readonly List<ShopItemCardView> _cards = new List<ShopItemCardView>();

    private ShopService Shop => ShopService.Instance;
    private ScoreEconomyManager Economy => ScoreEconomyManager.Instance;

    private int CurrentDay => _controller != null ? _controller.CurrentDay : 0;

    private void Awake()
    {
        if (_openButton != null) _openButton.onClick.AddListener(Open);
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_cardTemplate != null) _cardTemplate.gameObject.SetActive(false);
        if (_root != null) _root.SetActive(false);
    }

    private void Start()
    {
        if (Shop != null)
        {
            Shop.OnItemPurchased += HandleItemPurchased;
            Shop.OnEffectsChanged += HandleEffectsChanged;
        }
        else Debug.LogWarning("[ShopPanelView] ShopService.Instance 가 없습니다. 상점 비활성.");

        if (Economy != null) Economy.OnMoneyChanged += HandleMoneyChanged;
        else Debug.LogWarning("[ShopPanelView] ScoreEconomyManager.Instance 가 없습니다. 자금 표시 비활성.");

        if (_controller == null) Debug.LogWarning("[ShopPanelView] InspectionController 참조가 없습니다. 현재 일차 산출 불가.");
    }

    private void OnDestroy()
    {
        if (_openButton != null) _openButton.onClick.RemoveListener(Open);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (Shop != null)
        {
            Shop.OnItemPurchased -= HandleItemPurchased;
            Shop.OnEffectsChanged -= HandleEffectsChanged;
        }
        if (Economy != null) Economy.OnMoneyChanged -= HandleMoneyChanged;
    }

    // ── 열기/닫기 ──────────────────────────────────────────────

    /// <summary>상점을 연다(정산 패널의 '상점' 버튼). 현재 일차 기준으로 품목을 다시 빌드한다.</summary>
    public void Open()
    {
        if (_root != null) _root.SetActive(true);
        Rebuild();
    }

    /// <summary>상점을 닫는다(X / Esc).</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
    }

    private void Update()
    {
        // Esc 로 닫기(UI-CONVENTIONS 1장: 닫기 단축키 Esc).
        if (_root != null && _root.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    // ── 빌드/갱신 ──────────────────────────────────────────────

    /// <summary>현재 일차 기준 구매 가능 품목 카드를 다시 생성한다.</summary>
    private void Rebuild()
    {
        ClearCards();
        UpdateMoney();

        if (Shop == null || _cardContainer == null || _cardTemplate == null)
        {
            if (_emptyText != null) _emptyText.gameObject.SetActive(true);
            return;
        }

        List<DataRow> items = Shop.GetAvailableItems(CurrentDay);
        if (_emptyText != null) _emptyText.gameObject.SetActive(items.Count == 0);

        foreach (DataRow row in items)
        {
            if (row == null) continue;
            ShopItemCardView card = Instantiate(_cardTemplate, _cardContainer);
            card.gameObject.SetActive(true);
            card.Bind(row, RequestBuy);
            _cards.Add(card);
        }

        RefreshStates();
    }

    /// <summary>모든 카드의 구매 상태(구매가능/보유/돈부족)를 갱신한다.</summary>
    private void RefreshStates()
    {
        if (Shop == null) return;
        int money = Economy != null ? Economy.Money : 0;

        foreach (ShopItemCardView card in _cards)
        {
            if (card == null) continue;
            DataRow row = ResolveRow(card.ShopItemId);
            if (row == null) continue;

            string effectType = row.Get("effect_type");
            int price = row.GetInt("price", 0);

            ShopItemCardView.State state;
            if (Shop.IsOwned(effectType)) state = ShopItemCardView.State.Owned;       // 영구 효과 보유
            else if (money < price) state = ShopItemCardView.State.NotEnoughMoney;     // 자금 부족
            else state = ShopItemCardView.State.Buyable;

            card.SetState(state);
        }
    }

    /// <summary>카드 클릭 → 구매 요청. 검증·차감은 ShopService 가 수행, UI 는 결과(이벤트)로 갱신.</summary>
    private void RequestBuy(string shopItemId)
    {
        if (Shop == null || string.IsNullOrEmpty(shopItemId)) return;
        bool ok = Shop.TryBuy(shopItemId, CurrentDay);
        if (!ok)
        {
            // 실패(잠금/돈부족/중복)는 백엔드가 판정. UI 는 상태만 다시 칠한다(피드백은 상태 라벨로).
            RefreshStates();
        }
        // 성공 시 OnItemPurchased/OnEffectsChanged/OnMoneyChanged 가 와서 자동 갱신된다.
    }

    private DataRow ResolveRow(string shopItemId)
    {
        if (Shop == null || string.IsNullOrEmpty(shopItemId)) return null;
        // GetAvailableItems 재조회로 행을 다시 찾는다(테이블 직접 접근 없이 공개 API 만 사용).
        foreach (DataRow r in Shop.GetAvailableItems(CurrentDay))
            if (r != null && r.Get("shop_item_id") == shopItemId) return r;
        return null;
    }

    private void UpdateMoney()
    {
        if (_moneyText == null) return;
        _moneyText.text = (Economy != null ? Economy.Money : 0).ToString();
        _moneyText.color = MoneyGold;
    }

    private void ClearCards()
    {
        foreach (ShopItemCardView card in _cards)
            if (card != null) Destroy(card.gameObject);
        _cards.Clear();
    }

    // ── 이벤트 핸들러(구독) ─────────────────────────────────────

    private void HandleItemPurchased(string effectType) => RefreshStates();
    private void HandleEffectsChanged() => RefreshStates();

    private void HandleMoneyChanged(int total, int delta)
    {
        UpdateMoney();
        RefreshStates(); // 돈이 줄면 다른 품목이 '자금 부족'으로 바뀔 수 있음
    }
}
