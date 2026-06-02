using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 헤드셋(수화기) 드래그 오브젝트.
/// DocumentArea(_dropZone) 안에 드롭하면 대화 기록 팝업을 열고 제자리로 복귀한다.
/// </summary>
public sealed class DraggableHandset : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("드롭 영역 + 팝업")]
    [SerializeField] private RectTransform _dropZone;          // DocumentArea
    [SerializeField] private DialogueLogPopup _logPopup;
    [SerializeField] private InspectionController _controller;

    private RectTransform _rt;
    private Canvas _canvas;
    private Vector2 _homeAnchoredPos;

    private void Awake()
    {
        _rt = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
    }

    private void Start()
    {
        // 씬 배치된 위치를 홈으로 기억
        _homeAnchoredPos = _rt.anchoredPosition;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        transform.SetAsLastSibling();
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

        bool inZone = _dropZone != null
            && RectTransformUtility.RectangleContainsScreenPoint(_dropZone, eventData.position, cam);

        if (inZone && _controller != null && _logPopup != null)
        {
            _logPopup.OpenLines(_controller.GetDialogueLines());
        }

        // 항상 제자리로 복귀
        _rt.anchoredPosition = _homeAnchoredPos;
    }
}
