using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 카드 안의 필드 1행(라벨 + 값). 클릭하면 자신을 대조 컨트롤러에 통지한다.
/// 선택 하이라이트 on/off를 가지며, 자신의 (documentType, label, value, key)를 보유한다.
/// 판정 로직은 없다 — 표시와 클릭 통지만 한다.
///
/// 이종 소스 대조를 위해 <see cref="ICrossCheckSelectable"/>을 구현한다(SourceType="서류",
/// AttributeKey=field.key, Value=value, DisplayLabel=label).
/// </summary>
public sealed class DocumentFieldView : MonoBehaviour, ICrossCheckSelectable
{
    [Header("UI 참조")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private TMP_Text _valueText;
    [SerializeField] private Button _button;
    [SerializeField] private Image _highlight; // 선택 표시 배경(비활성 시작)

    [Header("색(UITheme 부재 시 기본값)")]
    [SerializeField] private Color _selectedColor = new Color(0.231f, 0.510f, 0.769f, 0.45f); // Score/Blue 톤

    /// <summary>이 필드가 속한 서류 종류(예: "여권").</summary>
    public string DocumentType { get; private set; }

    /// <summary>필드 라벨(표시명).</summary>
    public string Label { get; private set; }

    /// <summary>필드 값.</summary>
    public string Value { get; private set; }

    // ── ICrossCheckSelectable ────────────────────────────────────
    public string SourceType => "서류";
    public string AttributeKey { get; private set; } = string.Empty;
    public string UnlocksScan => string.Empty; // 서류 필드는 트리거 아님(손님 소스)
    public string DisplayLabel => Label;
    public RectTransform Rect => transform as RectTransform;
    public event Action<ICrossCheckSelectable> OnSelected;

    /// <summary>클릭 시 자신을 인자로 발행(레거시 호환 — 신규는 <see cref="OnSelected"/> 사용).</summary>
    public event Action<DocumentFieldView> OnFieldClicked;

    private void Awake()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);
        SetSelected(false);
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>필드 1개를 채운다(레거시: key 없는 호출은 빈 키).</summary>
    public void Bind(string documentType, string label, string value)
        => Bind(documentType, label, value, string.Empty);

    /// <summary>필드 1개를 채운다(속성 키 포함).</summary>
    public void Bind(string documentType, string label, string value, string key)
    {
        DocumentType = documentType;
        Label = label;
        Value = value ?? string.Empty;
        AttributeKey = key ?? string.Empty;

        if (_labelText != null) _labelText.text = label;
        if (_valueText != null) _valueText.text = value;
        SetSelected(false);
    }

    /// <summary>선택 하이라이트를 켜고 끈다.</summary>
    public void SetSelected(bool selected)
    {
        if (_highlight != null)
        {
            _highlight.gameObject.SetActive(selected);
            _highlight.color = _selectedColor;
        }
    }

    private void HandleClick()
    {
        OnFieldClicked?.Invoke(this);
        OnSelected?.Invoke(this);
    }
}
