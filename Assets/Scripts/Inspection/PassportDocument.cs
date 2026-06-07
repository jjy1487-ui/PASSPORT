using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 책상 위 서류(여권). 드래그로 옮기며, 놓는 위치에 따라 자동으로 펼침/접힘 전환.
/// - 책상(_openZone) 안에 놓으면 펼쳐짐(필드 표시)
/// - 책상 밖에 놓으면 접힘(표지만)
/// </summary>
public sealed class PassportDocument : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("상태 뷰")]
    [SerializeField] private GameObject _openView;    // 펼친 여권(필드)
    [SerializeField] private GameObject _closedView;  // 접힌 표지

    [Header("펼침 영역")]
    [SerializeField] private RectTransform _openZone;  // 책상. 이 안=펼침, 밖=접힘

    private RectTransform _rt;
    private Canvas _canvas;

    private void Awake()
    {
        _rt = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
    }

    private void Start()
    {
        SetOpen(false); // 제시 시 접힌 상태로 시작
    }

    /// <summary>펼침/접힘 상태를 전환한다. 인스펙터 참조가 비어도 자식(OpenView/ClosedView)을 찾아 동작.</summary>
    public void SetOpen(bool open)
    {
        GameObject openV = _openView != null ? _openView : ChildGo("OpenView");
        GameObject closedV = _closedView != null ? _closedView : ChildGo("ClosedView");
        if (openV != null) openV.SetActive(open);
        if (closedV != null) closedV.SetActive(!open);
    }

    /// <summary>펼침 영역(책상)이 인스펙터에서 비어 있으면 코드로 지정한다(이미 있으면 유지).</summary>
    public void ConfigureOpenZone(RectTransform zone)
    {
        if (zone != null && _openZone == null) _openZone = zone;
    }

    private GameObject ChildGo(string n)
    {
        Transform t = transform.Find(n);
        return t != null ? t.gameObject : null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        transform.SetAsLastSibling(); // 집어 든 서류를 맨 앞으로
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_rt == null) return;
        float scale = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
        _rt.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera : null;

        // 펼침 영역(책상)이 비어 있으면 부모 Canvas 를 폴백으로 써서 "화면 위라면 펼침"을 보장한다.
        // (정상 경로: DocumentView.StartClosed 가 ConfigureOpenZone 으로 실제 책상을 주입)
        RectTransform zone = _openZone != null ? _openZone
            : (_canvas != null ? _canvas.transform as RectTransform : null);

        bool onDesk = zone != null
            && RectTransformUtility.RectangleContainsScreenPoint(zone, eventData.position, cam);
        SetOpen(onDesk); // 책상 위면 펼침, 밖이면 접힘
    }
}
