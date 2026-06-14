using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 음성기록(대화 기록) 팝업. 현재 손님이 한 대화를 모아 보여준다. 비활성 시작.
/// 대화 각 줄을 그대로 클릭 가능한 대조 항목(CrossCheckItemView)으로 표시한다.
/// claim 이 있는 줄은 그 attr/value 로 서류·규정과 대조되고, 없는 줄은 관련성만 노출된다.
/// </summary>
public sealed class DialogueLogPopup : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Button _closeButton;

    [Header("교차 대조 단서(선택)")]
    [SerializeField] private Transform _claimContainer;        // 단서 위젯 부모(VerticalLayoutGroup 권장)
    [SerializeField] private CrossCheckItemView _claimTemplate; // 비활성 템플릿

    [Header("교차 대조(본문 항목)")]
    [Tooltip("뉴스/규정과 동일하게, 대화 본문 자체를 항상 클릭 가능한 대조 항목으로 노출한다.\n" +
             "라인 중 claim 이 있으면 그 attr/value 를 부여하고(예: 방문목적↔비자 대조), 없으면 관련성만 표시한다.")]
    [SerializeField] private CrossCheckItemView _contentSelectable; // 본문(대화) 자체가 클릭 대조 항목

    private readonly List<CrossCheckItemView> _claimViews = new List<CrossCheckItemView>();
    private bool _centeredOnce;  // 최초 1회만 중앙 정렬. 이후엔 드래그한 위치를 유지.

    /// <summary>selectable 구성 변경 통지.</summary>
    public event System.Action OnSelectablesChanged;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    /// <summary>대화 구조 라인을 표시한다. 각 줄을 그대로 클릭 가능한 대조 항목으로 만든다
    /// (claim 있는 줄은 그 attr/value 로 대조, 없는 줄은 관련성만). 본문 전체를 ClaimContainer 가 채운다.</summary>
    public void OpenLines(IReadOnlyList<DialogueLineData> lines)
    {
        ClearClaims();

        if (_claimContainer == null || _claimTemplate == null)
        {
            Debug.LogWarning("[DialogueLogPopup] _claimContainer/_claimTemplate 미연결 — 대화 항목을 표시할 수 없습니다.");
        }
        else if (lines == null || lines.Count == 0)
        {
            AddLineWidget("(아직 대화 내용이 없습니다)", null);
        }
        else
        {
            foreach (DialogueLineData ln in lines)
            {
                if (ln == null) continue;
                AddLineWidget($"{ln.speaker}: {ln.text}", ln.claim);
            }
        }

        if (_root != null)
        {
            _root.SetActive(true);
            // 최초 1회만 중앙 정렬, 이후엔 드래그한 위치 유지(맨 앞으로 올리기는 매번).
            BringToFront(_root.transform, !_centeredOnce);
            _centeredOnce = true;
        }
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>대화 한 줄을 클릭 가능한 대조 항목(CrossCheckItemView)으로 인스턴스화한다.
    /// claim 이 있으면 그 attr/value/라벨/잠금단서를 부여하고, 없으면 본문 자체를 라벨로 쓴다(관련성만).</summary>
    private void AddLineWidget(string disp, Claim claim)
    {
        CrossCheckItemView v = Instantiate(_claimTemplate, _claimContainer);
        v.gameObject.SetActive(true);
        bool hasClaim = claim != null && !string.IsNullOrEmpty(claim.attr);
        string label = hasClaim && !string.IsNullOrEmpty(claim.label) ? claim.label : disp;
        v.Bind("대화",
            hasClaim ? claim.attr : string.Empty,
            hasClaim ? claim.value : string.Empty,
            label,
            disp,
            hasClaim ? claim.unlocksScan : string.Empty);
        _claimViews.Add(v);
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Close()
    {
        ClearClaims();
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>
    /// 팝업을 형제 중 맨 앞(=다른 UI 위)으로 올린다. 닫기·드래그가 가려지지 않게 항상 맨 앞.
    /// 위치/앵커 중앙 정렬은 <paramref name="recenter"/>가 true 일 때(=최초 1회)만 한다 —
    /// 그 뒤엔 플레이어가 드래그(<see cref="DraggablePanel"/>)로 옮긴 위치를 유지한다.
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

    private void ClearClaims()
    {
        foreach (CrossCheckItemView v in _claimViews)
        {
            if (v != null) Destroy(v.gameObject);
        }
        _claimViews.Clear();
        if (_contentSelectable != null) _contentSelectable.gameObject.SetActive(false);
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        if (_contentSelectable != null && _contentSelectable.gameObject.activeInHierarchy)
            yield return _contentSelectable; // 본문(대화) 자체가 대조 항목
        foreach (CrossCheckItemView v in _claimViews)
        {
            if (v != null) yield return v;
        }
    }
}
