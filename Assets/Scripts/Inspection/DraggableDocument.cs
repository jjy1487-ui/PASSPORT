using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 책상 위 서류를 마우스로 끌어 옮길 수 있게 한다(여권을 직접 만지는 느낌).
/// 카드 루트에 부착. 배경 Image가 레이캐스트 타깃이어야 한다.
/// </summary>
public sealed class DraggableDocument : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _rt;
    private Canvas _canvas;
    private PassportDocument _passport; // 같은 카드에 있으면 드래그/펼침은 그쪽이 담당 → 중복 이동(2배속) 방지

    private void Awake()
    {
        _rt = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        _passport = GetComponent<PassportDocument>();
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
    }
}
