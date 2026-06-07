using UnityEngine;
using TMPro;

/// <summary>
/// ShopScene 전용 공용 마우스오버 툴팁. 씬에 하나만 배치하고, 평소에는 숨어 있다가
/// ManualShopItem 같은 호출자가 Show(text, 화면위치) 로 띄우고 Hide() 로 감춘다.
///
/// 표시만 한다(규약 5장). 상점 데이터/효과 텍스트는 호출자가 정해서 넘긴다 — 이 컴포넌트는
/// 받은 문자열을 그릴 뿐, 어떤 판정·계산도 하지 않는다.
///
/// 정적 Instance 로 노출하므로 아이템 4개가 각자 참조를 박지 않아도 ShopTooltip.Instance 로 부른다.
/// </summary>
public sealed class ShopTooltip : MonoBehaviour
{
    /// <summary>씬에 하나뿐인 활성 툴팁. ManualShopItem 등이 직접 참조 없이 호출한다.</summary>
    public static ShopTooltip Instance { get; private set; }

    [Header("UI 참조")]
    [Tooltip("켜고 끌 툴팁 본체(보통 이 컴포넌트가 붙은 오브젝트). 비우면 자기 자신을 사용.")]
    [SerializeField] private GameObject _root;

    [Tooltip("효과 설명을 그릴 TMP 텍스트(한글: MalgunGothic Dynamic SDF).")]
    [SerializeField] private TMP_Text _text;

    [Header("배치 옵션")]
    [Tooltip("마우스/지정 위치에서 얼마나 떨어뜨릴지(화면 픽셀). 위쪽으로 살짝 띄운다.")]
    [SerializeField] private Vector2 _screenOffset = new Vector2(0f, 28f);

    [Tooltip("화면 가장자리에서 최소로 남길 여백(픽셀). 툴팁이 밖으로 안 나가게 잡아준다.")]
    [SerializeField] private float _edgePadding = 8f;

    private RectTransform _rect;          // 이 툴팁의 RectTransform(이동용)
    private RectTransform _canvasRect;     // 부모 Canvas RectTransform(좌표 변환 기준)
    private Canvas _canvas;               // 카메라 모드 판별용

    private void Awake()
    {
        Instance = this;
        _rect = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null) _canvasRect = _canvas.rootCanvas.transform as RectTransform;
        if (_root == null) _root = gameObject;

        HideImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>지정한 화면 좌표 근처에 효과 설명을 띄운다. 빈 문자열이면 그냥 숨긴다.</summary>
    public void Show(string message, Vector2 screenPosition)
    {
        if (string.IsNullOrEmpty(message)) { Hide(); return; }

        if (_text != null) _text.text = message;
        if (_root != null) _root.SetActive(true);

        // 레이아웃이 한 프레임 뒤에 갱신될 수 있어, 위치 보정 전에 강제로 한 번 다시 계산.
        if (_text != null) LayoutRebuild(_text.rectTransform);
        if (_rect != null) LayoutRebuild(_rect);

        Reposition(screenPosition);
        BringToFront();
    }

    /// <summary>툴팁을 숨긴다.</summary>
    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }

    private void HideImmediate()
    {
        if (_root != null) _root.SetActive(false);
    }

    /// <summary>화면 좌표를 Canvas 로컬 좌표로 바꿔 배치하고, 화면 밖으로 안 나가게 보정한다.</summary>
    private void Reposition(Vector2 screenPosition)
    {
        if (_rect == null || _canvasRect == null) return;

        Camera cam = (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : (_canvas != null ? _canvas.worldCamera : null);

        Vector2 target = screenPosition + _screenOffset;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, target, cam, out Vector2 local))
        {
            local = ClampToCanvas(local);
            _rect.anchoredPosition = local;
        }
    }

    /// <summary>툴팁 박스가 Canvas 영역 안에 머물도록 로컬 좌표를 잘라준다.</summary>
    private Vector2 ClampToCanvas(Vector2 local)
    {
        Vector2 canvasSize = _canvasRect.rect.size;
        Vector2 boxSize = _rect.rect.size;
        Vector2 pivot = _rect.pivot;

        // Canvas 피벗(가운데 0.5,0.5) 기준 로컬 좌표계에서의 허용 범위 계산.
        float halfCanvasX = canvasSize.x * 0.5f;
        float halfCanvasY = canvasSize.y * 0.5f;

        float minX = -halfCanvasX + boxSize.x * pivot.x + _edgePadding;
        float maxX =  halfCanvasX - boxSize.x * (1f - pivot.x) - _edgePadding;
        float minY = -halfCanvasY + boxSize.y * pivot.y + _edgePadding;
        float maxY =  halfCanvasY - boxSize.y * (1f - pivot.y) - _edgePadding;

        if (minX <= maxX) local.x = Mathf.Clamp(local.x, minX, maxX);
        if (minY <= maxY) local.y = Mathf.Clamp(local.y, minY, maxY);
        return local;
    }

    /// <summary>다른 UI 에 안 가리도록 형제들 중 맨 마지막(맨 앞)으로 보낸다.</summary>
    private void BringToFront()
    {
        if (_root != null) _root.transform.SetAsLastSibling();
    }

    private static void LayoutRebuild(RectTransform rt)
    {
        if (rt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }
}
