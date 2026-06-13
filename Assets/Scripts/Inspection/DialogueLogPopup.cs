using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 음성기록(대화 기록) 팝업. 현재 손님이 한 대화를 모아 보여준다. 비활성 시작.
/// claim != null 인 라인은 클릭 가능한 대조 항목(CrossCheckItemView)으로, 나머지는 일반 텍스트로 표시한다.
/// (구조 라인 미연결 시 문자열 폴백을 사용한다.)
/// </summary>
public sealed class DialogueLogPopup : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _logText;     // 일반(비-단서) 라인 폴백/표시
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

    /// <summary>대화 기록을 문자열로 표시한다(폴백 — 대조 단서 없음).</summary>
    public void Open(IReadOnlyList<string> lines)
    {
        ClearClaims();
        if (_logText != null)
        {
            if (lines == null || lines.Count == 0)
            {
                _logText.text = "(아직 대화 내용이 없습니다)";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach (string line in lines)
                {
                    sb.Append("• ").Append(line).Append('\n');
                }
                _logText.text = sb.ToString().TrimEnd('\n');
            }
        }
        BindContentSelectable(null); // 문자열 폴백 — claim 정보 없음, 관련성만 노출
        if (_root != null)
        {
            _root.SetActive(true);
            // 최초 1회만 중앙 정렬, 이후엔 드래그한 위치 유지(맨 앞으로 올리기는 매번).
            BringToFront(_root.transform, !_centeredOnce);
            _centeredOnce = true;
        }
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>대화 구조 라인을 표시한다. claim 있는 라인은 클릭 가능한 대조 항목으로.</summary>
    public void OpenLines(IReadOnlyList<DialogueLineData> lines)
    {
        ClearClaims();

        StringBuilder sb = new StringBuilder();
        bool buildWidgets = _claimContainer != null && _claimTemplate != null;

        if (lines == null || lines.Count == 0)
        {
            sb.Append("(아직 대화 내용이 없습니다)");
        }
        else
        {
            foreach (DialogueLineData ln in lines)
            {
                if (ln == null) continue;

                if (buildWidgets)
                {
                    // 대화 '한 줄'을 그대로 클릭 가능한 대조 항목으로 만든다(라벨/표시=대사 본문).
                    //  - claim 이 있는 진술(예: 방문목적 "관광") → 그 attr/value 부여 → 서류와 일치/불일치 대조.
                    //  - claim 이 없는 줄(인삿말 등) → attr 없음 → 클릭은 되지만 비교 대상 아님(관련없음).
                    //  ※ 옛 "대화 기록" 한 덩어리 항목 대신, 사용자가 실제 대사 줄을 골라 대조하게 한다.
                    CrossCheckItemView v = Instantiate(_claimTemplate, _claimContainer);
                    v.gameObject.SetActive(true);
                    string disp = $"{ln.speaker}: {ln.text}";
                    bool hasClaim = ln.claim != null && !string.IsNullOrEmpty(ln.claim.attr);
                    string label = hasClaim && !string.IsNullOrEmpty(ln.claim.label) ? ln.claim.label : disp;
                    v.Bind("대화",
                        hasClaim ? ln.claim.attr : string.Empty,
                        hasClaim ? ln.claim.value : string.Empty,
                        label,
                        disp,
                        hasClaim ? ln.claim.unlocksScan : string.Empty);
                    _claimViews.Add(v);
                }
                else
                {
                    sb.Append("• ").Append(ln.speaker).Append(": ").Append(ln.text).Append('\n');
                }
            }
        }

        if (_logText != null) _logText.text = sb.ToString().TrimEnd('\n');
        // 줄 단위 항목으로 노출하므로 옛 generic "대화 기록" 항목(_contentSelectable)은 쓰지 않는다(ClearClaims 가 비활성 유지).
        if (_root != null)
        {
            _root.SetActive(true);
            // 최초 1회만 중앙 정렬, 이후엔 드래그한 위치 유지(맨 앞으로 올리기는 매번).
            BringToFront(_root.transform, !_centeredOnce);
            _centeredOnce = true;
        }
        OnSelectablesChanged?.Invoke();
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

    /// <summary>
    /// 대화 본문 자체를 클릭 가능한 대조 항목으로 노출한다(뉴스 _contentSelectable / 규정 _ruleSelectable 과 동일 패턴).
    /// claim 이 있으면 그 속성/값을 부여(예: 방문목적 attr=visa_type ↔ 비자 대조), 없으면 관련성만(값 비교 불가).
    /// </summary>
    private void BindContentSelectable(Claim claim)
    {
        if (_contentSelectable == null) return;
        _contentSelectable.gameObject.SetActive(true);
        _contentSelectable.Bind("대화",
            claim != null ? claim.attr : "dialogue_content",
            claim != null ? claim.value : string.Empty,
            claim != null && !string.IsNullOrEmpty(claim.label) ? claim.label : "대화 기록",
            null,                                   // displayText null → 라벨 텍스트 유지
            claim != null ? claim.unlocksScan : string.Empty);
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
