using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 손님 1명을 화면에 표시한다. 초상은 플레이스홀더(색상 박스),
/// 후일 spriteRef로 실제 스프라이트 교체 가능.
///
/// 교차 대조: 이름(attr=name)·얼굴(attr=face) 두 항목을 selectable 로 노출한다(ICrossCheckProvider).
/// 국적·성별 등은 노출하지 않는다(문서 대조 시스템의 전제).
/// </summary>
public sealed class CustomerView : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private Image _portraitPlaceholder;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _infoText;

    [Header("교차 대조 selectable(선택 — 없으면 대조 비참여)")]
    [SerializeField] private CrossCheckItemView _nameSelectable; // 초상 영역 이름(attr=name)
    [SerializeField] private CrossCheckItemView _faceSelectable; // 초상 영역 얼굴(attr=face)

    /// <summary>selectable 구성 변경 통지(손님 교체 시).</summary>
    public event System.Action OnSelectablesChanged;

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

        // 초상(이미지) 영역에는 이름만 표시한다.
        // 국적·성별·필요 서류·캐릭터유형 등은 노출하지 않는다 — 플레이어가 서류를 대조해
        // 직접 알아내야 하므로(문서 대조 시스템의 전제). 정보를 미리 주면 대조 의미가 사라진다.
        if (_infoText != null)
        {
            _infoText.text = string.Empty;
            _infoText.gameObject.SetActive(false);
        }

        // 교차 대조 selectable 바인딩(이름은 영문, 얼굴은 sprite_ref 키).
        if (_nameSelectable != null)
            _nameSelectable.Bind("캐릭터", "name", customer.nameEn, "이름", customer.nameKr);
        if (_faceSelectable != null)
            _faceSelectable.Bind("캐릭터", "face", customer.spriteRef, "얼굴", "얼굴");

        OnSelectablesChanged?.Invoke();
    }

    /// <summary>손님 표시를 숨긴다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (!gameObject.activeInHierarchy) yield break;
        if (_nameSelectable != null) yield return _nameSelectable;
        if (_faceSelectable != null) yield return _faceSelectable;
    }

    /// <summary>id로부터 안정적인 파스텔 색을 만든다.</summary>
    private static Color PlaceholderColor(int id)
    {
        float hue = (id * 0.137f) % 1f; // 황금비 비슷한 분산
        return Color.HSVToRGB(hue, 0.45f, 0.85f);
    }
}
