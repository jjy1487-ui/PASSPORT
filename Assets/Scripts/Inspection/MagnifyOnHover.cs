using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 확대경(MAGNIFY, 영구 효과) 표시 연결. 상점에서 확대경을 구매(ShopService.IsEffectActive)한 경우에만,
/// 서류 카드에 마우스를 올리면 카드를 일시 확대해 작은 글씨를 읽기 쉽게 한다.
///
/// 순수 표시 효과다 — 결함 여부/정답 정보를 일절 보지 않는다(판정 로직 없음, 규약 5장).
/// 확대경 미보유 시 아무 동작도 하지 않는다(효과 게이팅은 ShopService 가 소유).
/// 서류 카드 루트(DraggableDocument 와 같은 오브젝트 또는 카드 프리팹)에 부착한다.
/// </summary>
public sealed class MagnifyOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("확대 배율")]
    [Tooltip("호버 시 곱할 스케일(1.0 = 원본). 확대경 보유 시에만 적용.")]
    [SerializeField] private float _zoomScale = 1.6f;

    private RectTransform _rt;
    private Vector3 _baseScale = Vector3.one;
    private bool _zoomed;

    private ShopService Shop => ShopService.Instance;
    private bool MagnifyOwned => Shop != null && Shop.IsEffectActive(ShopService.FxMagnify);

    private void Awake()
    {
        _rt = transform as RectTransform;
        if (_rt != null) _baseScale = _rt.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_rt == null || _zoomed || !MagnifyOwned) return;
        _baseScale = _rt.localScale;           // 드래그/레이아웃으로 바뀌었을 수 있어 진입 시점 기준
        _rt.localScale = _baseScale * _zoomScale;
        _rt.SetAsLastSibling();                // 확대 카드가 가려지지 않게 맨 앞으로
        _zoomed = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_rt == null || !_zoomed) return;
        _rt.localScale = _baseScale;
        _zoomed = false;
    }

    private void OnDisable()
    {
        // 카드 제거/비활성 시 스케일 원복(템플릿 복제 안전).
        if (_rt != null && _zoomed) { _rt.localScale = _baseScale; _zoomed = false; }
    }
}
