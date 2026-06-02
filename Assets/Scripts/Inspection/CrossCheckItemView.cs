using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 외 소스(캐릭터/뉴스/대화/규정)에서 클릭 가능한 대조 항목 1개를 표시하는 범용 위젯.
/// <see cref="ICrossCheckSelectable"/> 구현 — 라벨(한글) 표시, 속성 키/값으로 대조.
/// 판정 로직 없음 — 표시·클릭 통지 전용.
/// </summary>
public sealed class CrossCheckItemView : MonoBehaviour, ICrossCheckSelectable
{
    [Header("UI 참조")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private Button _button;
    [SerializeField] private Image _highlight; // 선택 표시(비활성 시작)

    [Header("색(UITheme 부재 시 기본값)")]
    [SerializeField] private Color _selectedColor = new Color(0.231f, 0.510f, 0.769f, 0.45f); // Score/Blue 톤

    public string SourceType { get; private set; } = string.Empty;
    public string AttributeKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string UnlocksScan { get; private set; } = string.Empty;
    public string DisplayLabel { get; private set; } = string.Empty;
    public RectTransform Rect => transform as RectTransform;
    public event Action<ICrossCheckSelectable> OnSelected;

    private void Awake()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);
        SetSelected(false);
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>항목을 채운다. displayText 가 없으면 label 을 그대로 보여준다. unlocksScan 은 스캔 잠금 해제 트리거 단서일 때만 채운다.</summary>
    public void Bind(string sourceType, string attributeKey, string value, string label, string displayText = null, string unlocksScan = null)
    {
        SourceType = sourceType ?? string.Empty;
        AttributeKey = attributeKey ?? string.Empty;
        Value = value ?? string.Empty;
        UnlocksScan = unlocksScan ?? string.Empty;
        DisplayLabel = label ?? string.Empty;

        if (_labelText != null) _labelText.text = string.IsNullOrEmpty(displayText) ? DisplayLabel : displayText;
        SetSelected(false);
    }

    public void SetSelected(bool on)
    {
        if (_highlight != null)
        {
            _highlight.gameObject.SetActive(on);
            _highlight.color = _selectedColor;
        }
    }

    private void HandleClick() => OnSelected?.Invoke(this);
}
