using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 가방(인벤토리) 한 칸. 보유 아이템 1개를 아이콘으로 표시하고, 드래그할 수 있다.
///
/// 칸은 화면상 '고정 자리'다. 드래그는 시각 피드백일 뿐, 놓은 결과(사용/이동/취소)는
/// 컨트롤러(CounselBookButton)가 드롭 위치로 판정한다 — 이 컴포넌트는 표시·입력 전달만 한다(규약 5장).
///  - 드래그 끝나면 항상 제 칸 자리로 복귀하고, 드롭 위치(slotIndex, effectType, 스크린좌표)를 콜백으로 넘긴다.
///  - 컨트롤러가 "얼굴 위 → 사용 / 다른 칸 위 → 배치 이동 / 그 외 → 그대로"를 결정한 뒤 다시 그린다.
/// 빈 칸(아이콘 없음)은 드래그를 잡지 않는다.
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class InventoryItemIcon : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _icon;   // 비우면 자기 Image 사용

    private RectTransform _rt;
    private Canvas _canvas;
    private int _slotIndex = -1;
    private string _effectType;
    private string _description;   // 마우스오버 툴팁에 띄울 설명(shop effect)
    private bool _draggable;
    private System.Action<int, string, Vector2> _onDropped;
    private Vector2 _home;
    private bool _dragging;

    private void Awake()
    {
        _rt = transform as RectTransform;
        if (_icon == null) _icon = GetComponent<Image>();
        _canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>이 칸의 고정 정보(칸 번호 + 드롭 통지 콜백)를 1회 설정한다.</summary>
    public void Init(int slotIndex, System.Action<int, string, Vector2> onDropped)
    {
        _slotIndex = slotIndex;
        _onDropped = onDropped;
    }

    /// <summary>이 칸을 채운다. sprite=null 이면 빈 칸(아이콘 숨김·드래그/툴팁 안 잡음).</summary>
    public void Bind(Sprite sprite, string effectType, string description)
    {
        _effectType = effectType;
        _description = description;
        bool filled = sprite != null;
        _draggable = filled;
        if (_icon != null)
        {
            _icon.sprite = sprite;
            _icon.enabled = filled;
            _icon.preserveAspect = true;
            _icon.raycastTarget = filled;
        }
    }

    /// <summary>빈 칸으로 비운다.</summary>
    public void Clear() => Bind(null, null, null);

    // ── 마우스오버 툴팁(상점과 동일한 ShopTooltip 재사용) ───────────
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_draggable || string.IsNullOrEmpty(_description) || ShopTooltip.Instance == null) return;
        Vector2 pos = eventData != null ? eventData.position : (Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
        ShopTooltip.Instance.Show(_description, pos);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (ShopTooltip.Instance != null) ShopTooltip.Instance.Hide();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!_draggable) return;
        if (ShopTooltip.Instance != null) ShopTooltip.Instance.Hide(); // 드래그 중엔 툴팁 숨김
        if (_rt == null) _rt = transform as RectTransform;
        _dragging = true;
        _home = _rt.anchoredPosition;
        _rt.SetAsLastSibling(); // 드래그 중 다른 칸 위로
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _rt == null) return;
        float scale = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
        _rt.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_rt != null) _rt.anchoredPosition = _home; // 항상 칸 자리로 복귀(실제 이동은 레이아웃으로 반영)
        _onDropped?.Invoke(_slotIndex, _effectType, eventData.position);
    }
}
