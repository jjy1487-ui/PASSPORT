using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 손님 1명을 화면에 표시한다. 초상은 플레이스홀더(색상 박스),
/// 후일 spriteRef로 실제 스프라이트 교체 가능.
/// </summary>
public sealed class CustomerView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private Image _portraitPlaceholder;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _infoText;

    /// <summary>손님 정보를 표시한다.</summary>
    public void Show(CustomerData customer)
    {
        if (customer == null)
        {
            return;
        }

        gameObject.SetActive(true);

        // 플레이스홀더 초상: customerId 기반 결정색 (실제 아트 없을 때)
        if (_portraitPlaceholder != null)
        {
            _portraitPlaceholder.color = PlaceholderColor(customer.customerId);
            // TODO: 실제 아트 적용 시 Resources.Load<Sprite>(customer.spriteRef)
        }

        if (_nameText != null)
        {
            _nameText.text = $"{customer.nameKr} ({customer.nameEn})";
        }

        if (_infoText != null)
        {
            _infoText.text = $"{customer.nationality} · {customer.gender} · {customer.characterType}";
        }
    }

    /// <summary>손님 표시를 숨긴다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>id로부터 안정적인 파스텔 색을 만든다.</summary>
    private static Color PlaceholderColor(int id)
    {
        float hue = (id * 0.137f) % 1f; // 황금비 비슷한 분산
        return Color.HSVToRGB(hue, 0.45f, 0.85f);
    }
}
