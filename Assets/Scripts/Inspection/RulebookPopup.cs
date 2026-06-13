using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 규정집 팝업(2단 구조). 왼쪽 = 규정 제목 리스트(위→아래), 오른쪽 = 선택한 규정의 제목+내용.
/// 왼쪽 행을 클릭하면 오른쪽 내용이 그 규정으로 갱신된다. 비활성으로 시작.
///
/// 왼쪽 행은 각각 <see cref="CrossCheckItemView"/>(=<see cref="ICrossCheckSelectable"/>)라서
/// 교차 대조(Space)에 그대로 참여한다 — 대조 모드가 아니면 클릭은 내용 표시만 하고,
/// 대조 모드면 <see cref="CrossCheckController"/>가 OnSelected 를 받아 선택 처리한다(두 동작 공존).
/// 보이는(활성) 모든 행을 selectable 로 노출한다(<see cref="ICrossCheckProvider"/>).
///
/// 판정 로직 없음 — 표시·클릭 통지 전용. 정답/변조 판정은 gameplay 소유.
/// </summary>
public sealed class RulebookPopup : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조 — 공통")]
    [SerializeField] private GameObject _root;
    [Tooltip("닫기 버튼(X·우상단). BringToFront/overrideSorting 동작 유지.")]
    [SerializeField] private Button _closeButton;

    [Header("오른쪽 패널 — 선택한 규정의 제목/내용")]
    [SerializeField] private TMP_Text _titleText;   // 우측 제목(기존 _titleText 재사용)
    [SerializeField] private TMP_Text _contentText; // 우측 본문(기존 _contentText 재사용)
    [Tooltip("오른쪽 내용 패널을 대조 항목으로 노출(Button + CrossCheckItemView, 하이라이트는 선택). " +
             "연결되면 왼쪽 리스트 행 대신 이 Content 가 대조 대상이 된다(왼쪽 행은 내용 표시 네비게이션 전용).")]
    [SerializeField] private CrossCheckItemView _contentSelectable;

    [Header("왼쪽 리스트 — 규정 제목 행(미리 14개 배치, 남는 건 숨김)")]
    [Tooltip("DocumentCardView/PcrCard 방식: 수동 배치한 행을 들어온 규정 수만큼 채우고 나머지는 비활성화.")]
    [SerializeField] private CrossCheckItemView[] _ruleRows;

    [Header("미사용(숨김) — 기존 단일창 잔재")]
    [Tooltip("리스트 클릭으로 대체됨. 연결돼 있으면 Open 시 숨긴다.")]
    [SerializeField] private GameObject _prevButton;
    [SerializeField] private GameObject _nextButton;
    [SerializeField] private GameObject _pageText;

    private IReadOnlyList<RuleData> _items;
    private int _visibleCount;   // 현재 채워진(활성) 행 수
    private int _selectedIndex = -1;
    private bool _centeredOnce;  // 최초 1회만 중앙 정렬. 이후엔 드래그한 위치를 유지.

    /// <summary>selectable 구성 변경 통지.</summary>
    public event System.Action OnSelectablesChanged;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);

        // 각 행 클릭 → 오른쪽 내용 갱신(대조 처리는 CrossCheckController 가 별도로 받음).
        // 람다 대신 명명 핸들러를 써서 OnDestroy 에서 정확히 짝 해제한다.
        if (_ruleRows != null)
        {
            foreach (CrossCheckItemView row in _ruleRows)
            {
                if (row != null) row.OnSelected += HandleRowSelected;
            }
        }

        HideLegacyControls();
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);

        if (_ruleRows != null)
        {
            foreach (CrossCheckItemView row in _ruleRows)
            {
                if (row != null) row.OnSelected -= HandleRowSelected;
            }
        }
    }

    /// <summary>이전/다음/페이지 등 단일창 잔재 컨트롤을 숨긴다(연결돼 있을 때만).</summary>
    private void HideLegacyControls()
    {
        if (_prevButton != null) _prevButton.SetActive(false);
        if (_nextButton != null) _nextButton.SetActive(false);
        if (_pageText != null) _pageText.SetActive(false);
    }

    /// <summary>규정 목록으로 팝업을 연다. 첫 규정을 기본 선택해 오른쪽에 표시한다.</summary>
    public void Open(IReadOnlyList<RuleData> items)
    {
        _items = items;
        if (_root != null)
        {
            _root.SetActive(true);
            // 최초 1회만 중앙 정렬, 이후엔 드래그한 위치 유지(맨 앞으로 올리기는 매번).
            BringToFront(_root.transform, !_centeredOnce);
            _centeredOnce = true;
        }
        HideLegacyControls();
        FillRows();
        ShowRule(_visibleCount > 0 ? 0 : -1);
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>
    /// 팝업을 형제 중 맨 앞(=다른 UI 위)으로 올린다. 닫기·드래그가 가려지지 않게 항상 맨 앞.
    /// 위치/앵커 중앙 정렬은 <paramref name="recenter"/>가 true 일 때(=최초 1회)만 한다 —
    /// 그 뒤엔 플레이어가 드래그(<see cref="DraggablePanel"/>)로 옮긴 위치를 유지한다.
    /// (NewsPopup/DialogueLogPopup 과 동일 패턴.)
    /// </summary>
    private static void BringToFront(Transform t, bool recenter)
    {
        if (t == null) return;
        t.SetAsLastSibling();
        if (recenter && t is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>왼쪽 리스트 행을 규정 수만큼 채우고 나머지는 비활성화한다.</summary>
    private void FillRows()
    {
        _visibleCount = 0;
        if (_ruleRows == null) return;

        int count = _items?.Count ?? 0;
        for (int i = 0; i < _ruleRows.Length; i++)
        {
            CrossCheckItemView row = _ruleRows[i];
            if (row == null) continue;

            if (i < count)
            {
                RuleData item = _items[i];
                string label = string.IsNullOrEmpty(item.title) ? item.relatedField : item.title;
                // 표시=제목 라벨, 대조=attr(값 비교 없음 → 관련성만). attr 없으면 "" → 대조 비대상이나 클릭/표시는 정상.
                row.Bind("규정", item.attr ?? string.Empty, string.Empty, label, label);
                row.gameObject.SetActive(true);
                _visibleCount++;
            }
            else
            {
                row.gameObject.SetActive(false);
            }
        }

        if (count > _ruleRows.Length)
            Debug.LogWarning($"[RulebookPopup] 규정 {count}개 > 배치된 행 {_ruleRows.Length}개. 초과분은 표시되지 않습니다(행 추가 필요).");
    }

    /// <summary>index 규정을 오른쪽 패널에 표시하고 그 행을 선택 하이라이트한다. index<0 이면 비운다.</summary>
    private void ShowRule(int index)
    {
        // 이전 선택 해제
        if (_selectedIndex >= 0 && _ruleRows != null && _selectedIndex < _ruleRows.Length)
        {
            CrossCheckItemView prev = _ruleRows[_selectedIndex];
            if (prev != null) prev.SetSelected(false);
        }

        _selectedIndex = index;

        if (_items == null || index < 0 || index >= _items.Count)
        {
            if (_titleText != null) _titleText.text = string.Empty;
            if (_contentText != null) _contentText.text = string.Empty;
            if (_contentSelectable != null) _contentSelectable.SetSelected(false);
            return;
        }

        RuleData item = _items[index];
        if (_titleText != null) _titleText.text = item.title;
        if (_contentText != null) _contentText.text = item.content;

        // 오른쪽 Content 를 대조 항목으로 — 현재 표시 중인 규정의 attr/제목으로 바인딩한다.
        // (라벨 텍스트는 _contentSelectable 내부 _labelText 가 없으면 무시되고, 화면 본문은 _contentText 가 담당)
        if (_contentSelectable != null)
            _contentSelectable.Bind("규정", item.attr ?? string.Empty, string.Empty, item.title, item.title);

        if (_ruleRows != null && index < _ruleRows.Length)
        {
            CrossCheckItemView row = _ruleRows[index];
            if (row != null) row.SetSelected(true);
        }
    }

    /// <summary>리스트 행 클릭 처리 — 그 행의 인덱스를 찾아 오른쪽 내용을 갱신한다.</summary>
    private void HandleRowSelected(ICrossCheckSelectable item)
    {
        if (_ruleRows == null || item == null) return;
        for (int i = 0; i < _ruleRows.Length; i++)
        {
            if (ReferenceEquals(_ruleRows[i], item))
            {
                ShowRule(i);
                return;
            }
        }
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;

        // 오른쪽 Content 패널이 대조 항목으로 연결돼 있으면 그것만 노출한다.
        // (왼쪽 리스트 행은 내용 표시용 네비게이션 전용 — 대조 후보에서 제외.)
        if (_contentSelectable != null && _contentSelectable.gameObject.activeInHierarchy)
        {
            yield return _contentSelectable;
            yield break;
        }

        // 폴백(_contentSelectable 미연결): 기존처럼 왼쪽 규정 행들을 대조 항목으로 노출.
        if (_ruleRows == null) yield break;
        foreach (CrossCheckItemView row in _ruleRows)
        {
            if (row != null && row.gameObject.activeInHierarchy)
                yield return row;
        }
    }
}
