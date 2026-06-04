using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// uGUI 패널을 드래그로 이동시킨다. 패널 배경(raycastTarget=true)을 잡고 끌면 움직인다.
/// 자식 버튼(닫기/이전/다음)은 자기 클릭을 먼저 처리하므로 정상 동작한다.
/// 드래그 시작 시 형제 중 맨 앞으로 올려 다른 패널 위에 표시한다.
/// </summary>
public sealed class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    [Tooltip("이동할 대상. 비우면 이 오브젝트 자신을 이동한다.")]
    [SerializeField] private RectTransform _target;

    private Canvas _canvas;

    private void Awake()
    {
        if (_target == null) _target = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_target != null) _target.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null) return;
        float scale = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
        _target.anchoredPosition += eventData.delta / scale;
    }
}
