using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 책상 위 서류를 마우스로 끌어 옮길 수 있게 한다(여권을 직접 만지는 느낌).
/// 카드 루트에 부착. 배경 Image가 레이캐스트 타깃이어야 한다.
/// </summary>
public sealed class DraggableDocument : MonoBehaviour, IBeginDragHandler, IDragHandler, IDragBoundsReceiver
{
    private RectTransform _rt;
    private Canvas _canvas;
    private PassportDocument _passport; // 같은 카드에 있으면 드래그/펼침은 그쪽이 담당 → 중복 이동(2배속) 방지

    // 상태별 드래그 영역(월드 사각형). DocumentView 가 SpawnArea(닫힘)·DocumentArea(펼침)를 주입한다.
    // 비어 있으면(_hasDragBounds=false) 클램프하지 않는다(예전 동작).
    private Rect _spawnZone;     // 닫힘(ClosedView)이 머무는 영역
    private Rect _documentZone;  // 펼침(OpenView)이 머무는 영역(책상)
    private bool _hasDragBounds;

    private void Awake()
    {
        _rt = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        _passport = GetComponent<PassportDocument>();
    }

    /// <summary>상태별 드래그 허용 영역(월드 사각형)을 코드로 주입한다.
    /// 닫힘=spawnZone, 펼침=documentZone 에 카드가 머문다.</summary>
    public void ConfigureDragBounds(Rect spawnZone, Rect documentZone)
    {
        _spawnZone = spawnZone;
        _documentZone = documentZone;
        _hasDragBounds = true;
    }

    /// <summary>끌기 시작 시 맨 앞으로 올린다.</summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        // PassportDocument 가 같은 오브젝트에 있으면 그쪽이 드래그(이동+펼침/접힘)를 전담한다.
        // 둘 다 IDragHandler 라 함께 처리하면 같은 이벤트로 위치를 두 번 더해 2배 속도로 움직인다.
        if (_passport != null) return;
        transform.SetAsLastSibling();
    }

    /// <summary>포인터 이동량만큼 위치를 옮긴다(캔버스 스케일 보정).</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (_passport != null) return; // PassportDocument 가 이미 이동을 처리 → 중복 방지
        if (_rt == null)
        {
            return;
        }
        float scale = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
        _rt.anchoredPosition += eventData.delta / scale;
        // 드래그 중엔 영역 클램프 안 함('벽' 느낌 제거). 정렬은 PassportDocument.OnEndDrag 가 손 뗄 때만 처리.
    }
}
