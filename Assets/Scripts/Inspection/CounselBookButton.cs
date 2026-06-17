using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 가방(인벤토리) 버튼 + 심리상담집(COUNSEL_BOOK) 사용 흐름.
///
/// 흐름(사용자 승인):
///   1) 상점에서 심리상담집 구매 → ShopService 가 1회성 소비 수량으로 보유(GetConsumableCount).
///   2) 플레이어가 가방 버튼을 누르면 가방 패널이 열리고, 보유한 심리상담집과 수량이 보인다.
///   3) '사용' 버튼을 누르면 현재 손님에게 1회 사용 → 1개 소비(ShopService.TryConsume).
///   4) 그 손님의 결함(위반) 유무를 팝업 문구로 보여준다 — "결함 있음 / 결함 없음".
///
/// 핵심: 이 아이템은 '결함 유무를 알려주는 힌트 도구'다. 판정(승인/거절)·점수에는 영향 없음(정보 제공 전용).
/// 결함 신호는 InspectionController.CurrentCustomerHasDefect 를 그대로 읽는다
/// (확률 변형 altVariant 가 이미 반영된 correctResult 기준 → 이번 플레이의 실제 변형으로 판정).
///
/// 소비 판정·차감은 ShopService 가 소유(GetConsumableCount/TryConsume) — 이 컴포넌트는 표시·사용 전달만.
/// 보유 0이면 버튼을 숨긴다(상점 미구매 시 노출 안 함). ScanConsumableButton 패턴 계승.
/// 팝업은 같은 패널을 재사용한다(가방 목록 -> 사용 -> 결과). DialogueLogPopup 의 BringToFront 규약 따름.
/// </summary>
public sealed class CounselBookButton : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private InspectionController _controller;
    [SerializeField] private Button _bagButton;        // 가방 열기 버튼(이 컴포넌트가 붙는 오브젝트)

    [Header("가방 패널")]
    [SerializeField] private GameObject _panelRoot;     // 가방/결과 패널(비활성 시작)
    [SerializeField] private TMP_Text _titleText;       // 패널 제목
    [SerializeField] private TMP_Text _resultText;      // 사용 결과 / 안내 문구
    [SerializeField] private Button _closeButton;       // 닫기(X)

    [Header("아이템 칸(6칸) + 사용 대상")]
    [Tooltip("6개의 칸. 보유 아이템 아이콘을 순서대로 채운다(남는 칸은 빈 칸).")]
    [SerializeField] private InventoryItemIcon[] _slots;
    [Tooltip("손님 얼굴 영역(CustomerArea). 여기로 아이콘을 끌어 놓으면 사용된다.")]
    [SerializeField] private RectTransform _faceTarget;

    [Header("사용 토스트(화면에 잠깐 떴다 사라짐)")]
    [SerializeField] private GameObject _toastRoot;     // 토스트 루트(비활성 시작)
    [SerializeField] private TMP_Text _toastText;       // 토스트 문구
    [SerializeField] private float _toastSeconds = 2.5f;

    [Header("(구) 텍스트 UI — 미사용, 비활성)")]
    [SerializeField] private TMP_Text _listText;        // 옛 보유 목록(아이콘 칸으로 대체됨)
    [SerializeField] private Button _useButton;         // 옛 '사용' 버튼(얼굴 드래그로 대체됨)
    [SerializeField] private TMP_Text _useButtonLabel;

    private ShopService Shop => ShopService.Instance;
    private Coroutine _toastCo;

    private bool _centeredOnce;  // 최초 1회만 중앙 정렬. 이후엔 드래그한 위치 유지(NewsPopup 규약).

    private void Awake()
    {
        if (_bagButton != null) _bagButton.onClick.AddListener(OpenBag);
        if (_closeButton != null) _closeButton.onClick.AddListener(CloseBag);
        if (_panelRoot != null) _panelRoot.SetActive(false);
        if (_toastRoot != null) _toastRoot.SetActive(false);

        // 각 칸에 번호 + 드롭 콜백 1회 주입(자유 배치 + 얼굴 사용).
        if (_slots != null)
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].Init(i, OnIconDropped);
    }

    private void Start()
    {
        if (Shop != null) Shop.OnEffectsChanged += HandleEffectsChanged;
        else Debug.LogWarning("[CounselBookButton] ShopService.Instance 가 없습니다. 가방 비활성.");

        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[CounselBookButton] _controller 가 연결되지 않았습니다.");

        RefreshBagButton();
    }

    private void OnDestroy()
    {
        if (_bagButton != null) _bagButton.onClick.RemoveListener(OpenBag);
        if (_useButton != null) _useButton.onClick.RemoveListener(UseOnCurrentCustomer);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(CloseBag);
        if (Shop != null) Shop.OnEffectsChanged -= HandleEffectsChanged;
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
    }

    // 보유 수량/손님이 바뀌면: 가방 버튼 노출 갱신 + 패널이 열려 있으면 목록도 다시 그린다.
    private void HandleEffectsChanged()
    {
        RefreshBagButton();
        if (_panelRoot != null && _panelRoot.activeSelf) RenderBag();
    }

    private void HandleCustomerChanged()
    {
        if (_panelRoot != null && _panelRoot.activeSelf) RenderBag();
    }

    /// <summary>가방을 연다(목록 상태). 항상 보유 목록부터 보여준다.</summary>
    private void OpenBag()
    {
        if (_panelRoot == null) return;
        _panelRoot.SetActive(true);
        EnsureContentActive();
        BringToFront(_panelRoot.transform, !_centeredOnce);  // 최초 1회만 중앙, 이후 드래그 위치 유지
        _centeredOnce = true;
        RenderBag();
    }

    /// <summary>패널 내부 표시 요소들을 켠다(프리팹/씬마다 authored active 상태가 달라도 항상 보이게).</summary>
    private void EnsureContentActive()
    {
        if (_titleText != null) _titleText.gameObject.SetActive(true);
        if (_resultText != null) _resultText.gameObject.SetActive(true);
        if (_slots != null) foreach (var s in _slots) if (s != null) s.gameObject.SetActive(true);
    }

    /// <summary>가방을 닫는다(X).</summary>
    private void CloseBag()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
    }

    /// <summary>가방을 그린다: 6칸에 보유 아이콘을 채우고, 심리상담집 사용 안내 문구를 갱신한다.</summary>
    private void RenderBag()
    {
        if (_titleText != null) _titleText.text = "가방";
        RefreshSlots();

        int counsel = Shop != null ? Shop.GetConsumableCount(ShopService.FxCounselBook) : 0;
        bool hasCustomer = _controller != null && _controller.HasCurrentCustomer;

        // 안내 문구(심리상담집 보유 시에만; 미보유면 비워 잔재 글씨 없음).
        if (_resultText != null)
        {
            if (counsel <= 0) _resultText.text = "";
            else if (!hasCustomer) _resultText.text = "손님이 있을 때 심리상담집을 얼굴로 끌어 사용하세요.";
            else _resultText.text = "심리상담집을 손님 얼굴로 끌어 결함 유무를 확인하세요.";
        }
    }

    /// <summary>보유 아이템을 6칸에 아이콘으로 채운다(없는 칸은 비움). 사용 가능 = 심리상담집(보유&gt;0).
    /// 아이콘은 Resources/Shop/&lt;icon&gt; 에서 로드(ShopItemCardView 와 동일 규약).</summary>
    private void RefreshSlots()
    {
        if (_slots == null) return;
        ShopService.OwnedItem[] items = Shop != null ? Shop.GetSlotItems() : null;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) continue;
            if (items != null && i < items.Length && !string.IsNullOrEmpty(items[i].EffectType))
            {
                ShopService.OwnedItem it = items[i];
                Sprite sprite = string.IsNullOrEmpty(it.Icon) ? null : Resources.Load<Sprite>("Shop/" + it.Icon);
                _slots[i].Bind(sprite, it.EffectType);
            }
            else
            {
                _slots[i].Clear();
            }
        }
    }

    /// <summary>아이콘을 놓았을 때(InventoryItemIcon 콜백): 손님 얼굴 위→사용, 다른 칸 위→자유 배치 이동, 그 외→그대로.</summary>
    private void OnIconDropped(int slotIndex, string effectType, Vector2 screenPos)
    {
        if (string.IsNullOrEmpty(effectType)) return;
        Camera cam = EventCamera();

        // 1) 손님 얼굴 위 → 사용(현재 사용 가능 = 심리상담집)
        if (_faceTarget != null && RectTransformUtility.RectangleContainsScreenPoint(_faceTarget, screenPos, cam))
        {
            if (effectType == ShopService.FxCounselBook) UseOnCurrentCustomer();
            return;
        }

        // 2) 다른 칸 위 → 자유 배치 이동/교환
        int target = SlotIndexAt(screenPos, cam);
        if (target >= 0 && target != slotIndex && Shop != null)
        {
            Shop.MoveItemToSlot(effectType, target); // OnEffectsChanged → RenderBag 가 다시 그림
            RefreshSlots();                          // 즉시 반영
        }
    }

    /// <summary>스크린 좌표가 어느 칸 위인지 반환(없으면 -1).</summary>
    private int SlotIndexAt(Vector2 screenPos, Camera cam)
    {
        if (_slots == null) return -1;
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] != null &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_slots[i].transform, screenPos, cam))
                return i;
        return -1;
    }

    /// <summary>RectangleContainsScreenPoint 용 카메라(스크린 오버레이 캔버스=null).</summary>
    private Camera EventCamera()
    {
        Canvas c = _panelRoot != null ? _panelRoot.GetComponentInParent<Canvas>() : null;
        if (c == null) return null;
        return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
    }

    /// <summary>
    /// 현재 손님에게 심리상담집을 1회 사용한다. 보유분을 1개 소비하고,
    /// 그 손님의 결함(위반) 유무를 결과 문구로 보여준다. 판정/점수에는 영향 없음.
    /// </summary>
    private void UseOnCurrentCustomer()
    {
        if (Shop == null || _controller == null) return;
        if (!_controller.HasCurrentCustomer)
        {
            if (_resultText != null) _resultText.text = "심사 중인 손님이 없습니다.";
            return;
        }
        // 심리상담집은 장기체류자 고객에게만 사용한다.
        if (_controller.CurrentCharacterType != CharacterTypes.LongStay)
        {
            if (_resultText != null) _resultText.text = "심리상담집은 장기체류자에게만 사용할 수 있습니다.";
            ShowToast("심리상담집은 장기체류자에게만 사용할 수 있습니다.");
            return;
        }
        if (Shop.GetConsumableCount(ShopService.FxCounselBook) <= 0)
        {
            if (_resultText != null) _resultText.text = "심리상담집을 보유하고 있지 않습니다.";
            return;
        }

        // 결함 유무는 소비 '전에' 읽어 둔다(소비로 손님이 바뀌지는 않지만, 신호를 먼저 확정).
        bool hasDefect = _controller.CurrentCustomerHasDefect;

        if (!Shop.TryConsume(ShopService.FxCounselBook)) return; // 보유 0이면 false(이중 클릭 가드)

        ShowResult(hasDefect);
    }

    /// <summary>결과 문구를 같은 패널에 표시한다(소비 1회 = 결과 1회). 남은 수량으로 칸도 갱신.</summary>
    private void ShowResult(bool hasDefect)
    {
        if (_panelRoot != null) { _panelRoot.SetActive(true); EnsureContentActive(); BringToFront(_panelRoot.transform, false); }
        if (_titleText != null) _titleText.text = "심리 상담 결과";

        RefreshSlots(); // 소비 후 남은 수량으로 칸 다시 채움

        string msg = hasDefect ? "결함 있음 — 이상 소견" : "결함 없음 — 정상";
        if (_resultText != null) _resultText.text = msg;
        ShowToast(msg);
    }

    /// <summary>화면에 잠깐 떴다 사라지는 토스트. 같은 토스트를 다시 띄우면 타이머를 재시작한다.</summary>
    private void ShowToast(string message)
    {
        if (_toastText != null) _toastText.text = message;
        if (_toastRoot != null) _toastRoot.SetActive(true);
        if (_toastCo != null) StopCoroutine(_toastCo);
        if (isActiveAndEnabled) _toastCo = StartCoroutine(HideToastAfter());
    }

    private System.Collections.IEnumerator HideToastAfter()
    {
        yield return new WaitForSeconds(_toastSeconds);
        if (_toastRoot != null) _toastRoot.SetActive(false);
        _toastCo = null;
    }

    /// <summary>가방 버튼은 항상 노출한다(비어 있어도). 상점 해금 전/미구매여도 보이고,
    /// 열면 보유 수량 또는 "(가방이 비어 있습니다)" + 구매 안내를 보여준다(빈 가방 UX는 RenderBag 가 처리).
    /// (이전: 보유 0이면 숨김 → 상점 해금 전엔 가방이 안 보여 혼란스러웠던 동작을 바로잡음.)</summary>
    private void RefreshBagButton()
    {
        if (_bagButton != null) _bagButton.gameObject.SetActive(true);
    }

    /// <summary>팝업을 형제 중 맨 앞(=다른 UI 위)으로 올린다. 닫기·드래그가 가려지지 않게 항상 맨 앞.
    /// 위치/앵커 중앙 정렬은 <paramref name="recenter"/>가 true 일 때(=최초 1회)만 한다 —
    /// 그 뒤엔 플레이어가 드래그(<see cref="DraggablePanel"/>)로 옮긴 위치를 유지한다(NewsPopup 규약).</summary>
    private static void BringToFront(Transform t, bool recenter)
    {
        if (t == null) return;
        t.SetAsLastSibling();
        if (recenter && t is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
