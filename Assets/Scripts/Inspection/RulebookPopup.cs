using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 1일차 규정집 팝업. 이전/다음으로 규정 넘김, 닫기로 종료. 비활성 시작.
/// 현재 규정을 클릭 가능한 대조 항목(attr=rule.attr, value="" → 관련성만)으로 노출한다(ICrossCheckProvider).
/// </summary>
public sealed class RulebookPopup : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _contentText;
    [SerializeField] private TMP_Text _pageText;
    [SerializeField] private Button _prevButton;
    [SerializeField] private Button _nextButton;
    [SerializeField] private Button _closeButton;

    [Header("교차 대조 항목(선택)")]
    [SerializeField] private CrossCheckItemView _ruleSelectable; // 현재 규정을 대조 항목으로(관련성 표시)

    private IReadOnlyList<RuleData> _items;
    private int _index;

    /// <summary>selectable 구성 변경 통지.</summary>
    public event System.Action OnSelectablesChanged;

    private void Awake()
    {
        if (_prevButton != null) _prevButton.onClick.AddListener(Prev);
        if (_nextButton != null) _nextButton.onClick.AddListener(Next);
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_prevButton != null) _prevButton.onClick.RemoveListener(Prev);
        if (_nextButton != null) _nextButton.onClick.RemoveListener(Next);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    /// <summary>규정 목록으로 팝업을 연다.</summary>
    public void Open(IReadOnlyList<RuleData> items)
    {
        _items = items;
        _index = 0;
        if (_root != null) _root.SetActive(true);
        Render();
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    private void Prev()
    {
        if (_items == null || _items.Count == 0) return;
        _index = Mathf.Max(0, _index - 1);
        Render();
    }

    private void Next()
    {
        if (_items == null || _items.Count == 0) return;
        _index = Mathf.Min(_items.Count - 1, _index + 1);
        Render();
    }

    private void Render()
    {
        if (_items == null || _items.Count == 0) return;
        RuleData item = _items[_index];
        if (_titleText != null) _titleText.text = item.title;
        if (_contentText != null) _contentText.text = item.content;
        if (_pageText != null) _pageText.text = $"{_index + 1} / {_items.Count}";

        // 현재 규정을 대조 항목으로(값 비교 없음 → 관련성만). attr 없으면 비활성.
        if (_ruleSelectable != null)
        {
            bool hasAttr = !string.IsNullOrEmpty(item.attr);
            _ruleSelectable.gameObject.SetActive(hasAttr);
            if (hasAttr)
            {
                string label = string.IsNullOrEmpty(item.relatedField) ? item.title : item.relatedField;
                _ruleSelectable.Bind("규정", item.attr, string.Empty, label, $"규정: {label}");
            }
        }

        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        if (_ruleSelectable != null && _ruleSelectable.gameObject.activeInHierarchy)
            yield return _ruleSelectable;
    }
}
