using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 상점 아이템 한 칸에서 "아이콘 영역"에만 붙는 마우스오버 감지기.
/// 아이콘과 똑같은 크기/위치로 맞춰 둔 투명 HoverArea 오브젝트에 부착한다.
///
/// 마우스가 아이콘 위(=이 HoverArea 사각형)에 들어오면 부모 ManualShopItem 에서
/// 효과 설명 문자열을 받아 공용 ShopTooltip 에 띄우고, 벗어나면 숨긴다.
///
/// 표시만 한다(규약 5장). 효과 텍스트는 부모 ManualShopItem 이 shop 데이터에서 정해 넘겨주며,
/// 이 컴포넌트는 어떤 판정·계산도 하지 않는다.
/// </summary>
public sealed class ShopItemHoverArea : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // 같은 아이템 칸의 ManualShopItem(효과 설명 출처). Awake 에서 부모에서 찾아 보관.
    private ManualShopItem _item;

    private void Awake()
    {
        _item = GetComponentInParent<ManualShopItem>();
        if (_item == null)
            Debug.LogWarning("[ShopItemHoverArea] 부모에서 ManualShopItem 을 찾지 못했습니다. 툴팁 표시 비활성.");
    }

    /// <summary>마우스가 아이콘 영역에 올라오면 효과 설명을 공용 툴팁에 띄운다.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (ShopTooltip.Instance == null || _item == null) return;

        string effect = _item.EffectDescription;
        if (string.IsNullOrEmpty(effect)) return; // 설명 없으면 굳이 안 띄움

        Vector2 screenPos = eventData != null ? eventData.position : (Vector2)Input.mousePosition;
        ShopTooltip.Instance.Show(effect, screenPos);
    }

    /// <summary>마우스가 아이콘 영역을 벗어나면 툴팁을 숨긴다.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (ShopTooltip.Instance == null) return;
        ShopTooltip.Instance.Hide();
    }
}
