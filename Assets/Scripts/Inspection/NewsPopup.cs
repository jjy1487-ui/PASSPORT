using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 1일차 뉴스 팝업. 이전/다음으로 항목 넘김, 닫기로 종료. 비활성 시작.
/// 현재 뉴스의 claims[] 를 클릭 가능한 대조 항목(CrossCheckItemView)으로 노출한다(ICrossCheckProvider).
/// </summary>
public sealed class NewsPopup : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _contentText;
    [SerializeField] private TMP_Text _pageText;
    [SerializeField] private Image _newsImage;          // 뉴스별 이미지(iconRef 로 로드). 비면 자동 숨김.
    [SerializeField] private Button _prevButton;
    [SerializeField] private Button _nextButton;
    [SerializeField] private Button _closeButton;

    [Header("교차 대조 단서(선택)")]
    [SerializeField] private Transform _claimContainer;       // 단서 위젯 부모(VerticalLayoutGroup 권장)
    [SerializeField] private CrossCheckItemView _claimTemplate; // 비활성 템플릿
    [SerializeField] private CrossCheckItemView _contentSelectable; // 본문 자체를 클릭 대조 항목으로(있으면 본문 클릭 가능)

    private IReadOnlyList<NewsData> _items;
    private int _index;
    private readonly List<CrossCheckItemView> _claimViews = new List<CrossCheckItemView>();

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

    /// <summary>뉴스 목록으로 팝업을 연다.</summary>
    public void Open(IReadOnlyList<NewsData> items)
    {
        _items = items;
        _index = 0;
        if (_root != null) _root.SetActive(true);
        Render();
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Close()
    {
        ClearClaims();
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
        NewsData item = _items[_index];
        if (_titleText != null) _titleText.text = item.title;
        if (_contentText != null) _contentText.text = item.content;
        if (_pageText != null) _pageText.text = $"{_index + 1} / {_items.Count}";
        if (_newsImage != null)
        {
            Sprite s = string.IsNullOrEmpty(item.iconRef) ? null
                     : Resources.Load<Sprite>("News/" + StripExt(item.iconRef));
            _newsImage.sprite = s;
            _newsImage.enabled = s != null;   // 이미지 없으면 칸 자동 숨김
        }
        if (_contentSelectable != null)
        {
            // 뉴스에 단서가 있으면 그 속성/값을 본문 항목에 부여(없으면 관련성만).
            Claim c0 = (item.claims != null && item.claims.Length > 0) ? item.claims[0] : null;
            _contentSelectable.Bind("뉴스",
                c0 != null ? c0.attr : "news_content",
                c0 != null ? c0.value : "",
                item.title,
                null,                                   // displayText null → 본문 텍스트 안 덮어씀
                c0 != null ? c0.unlocksScan : "");
        }

        BuildClaims(item);
    }

    /// <summary>iconRef 에서 확장자를 떼어 Resources 키로 만든다("spr_news_001.png" → "spr_news_001").</summary>
    private static string StripExt(string s)
    {
        int dot = s.LastIndexOf('.');
        return dot > 0 ? s.Substring(0, dot) : s;
    }

    // ── 교차 대조 단서 ───────────────────────────────────────────
    private void BuildClaims(NewsData item)
    {
        ClearClaims();

        if (_claimContainer == null || _claimTemplate == null || item.claims == null)
        {
            OnSelectablesChanged?.Invoke();
            return;
        }

        foreach (Claim c in item.claims)
        {
            if (c == null || string.IsNullOrEmpty(c.attr)) continue;
            CrossCheckItemView v = Instantiate(_claimTemplate, _claimContainer);
            v.gameObject.SetActive(true);
            v.Bind("뉴스", c.attr, c.value, c.label, c.label, c.unlocksScan);
            _claimViews.Add(v);
        }

        OnSelectablesChanged?.Invoke();
    }

    private void ClearClaims()
    {
        foreach (CrossCheckItemView v in _claimViews)
        {
            if (v != null) Destroy(v.gameObject);
        }
        _claimViews.Clear();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        if (_contentSelectable != null) yield return _contentSelectable; // 본문 항목
        foreach (CrossCheckItemView v in _claimViews)
        {
            if (v != null) yield return v;
        }
    }
}
