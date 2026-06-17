using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 책상 위 서류(여권). 드래그로 옮기며, 놓는 위치에 따라 자동으로 펼침/접힘 전환.
/// - 책상(_openZone) 안에 놓으면 펼쳐짐(필드 표시)
/// - 책상 밖에 놓으면 접힘(표지만)
/// </summary>
public sealed class PassportDocument : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDragBoundsReceiver
{
    [Header("상태 뷰")]
    [SerializeField] private GameObject _openView;    // 펼친 여권(필드)
    [SerializeField] private GameObject _closedView;  // 접힌 표지

    [Header("펼침 영역")]
    [SerializeField] private RectTransform _openZone;  // 책상. 이 안=펼침, 밖=접힘

    private RectTransform _rt;
    private Canvas _canvas;

    // 상태별 드래그 영역(월드 사각형). DocumentView 가 SpawnArea(닫힘)·DocumentArea(펼침)를 주입한다.
    // 비어 있으면(_hasDragBounds=false) 클램프하지 않는다(예전 동작).
    private Rect _spawnZone;     // 닫힘(ClosedView)이 머무는 영역
    private Rect _documentZone;  // 펼침(OpenView)이 머무는 영역(책상)
    private bool _hasDragBounds;

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

    /// <summary>상태별 드래그 허용 영역(월드 사각형)을 코드로 주입한다.
    /// 닫힘=spawnZone, 펼침=documentZone 에 카드가 머문다.</summary>
    public void ConfigureDragBounds(Rect spawnZone, Rect documentZone)
    {
        _spawnZone = spawnZone;
        _documentZone = documentZone;
        _hasDragBounds = true;
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
        // 드래그 중엔 영역 클램프를 하지 않는다 — 손가락 따라 자유롭게 움직여 '벽에 막히는' 느낌 제거.
        // 영역(스폰=닫힘 / 책상=펼침) 정렬은 손을 뗄 때(OnEndDrag)만 부드럽게 처리한다.
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

        // 상태가 막 바뀌었으니(특히 펼침→닫힘) 새 상태에 맞는 영역으로 즉시 끌어들인다.
        // 책상 밖에서 놓아 닫힘이 되면 작은 표지를 스폰 영역 안으로 되당기고,
        // 책상 위에서 놓아 펼침이 되면 큰 루트를 책상 안으로 정렬한다.
        DragBoundsClamp.ClampForState(_rt, _hasDragBounds, _spawnZone, _documentZone);
    }
}
