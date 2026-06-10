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

    private readonly List<CrossCheckItemView> _claimViews = new List<CrossCheckItemView>();

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
        if (_root != null)
        {
            _root.SetActive(true);
            BringToFrontCentered(_root.transform);
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

                if (buildWidgets && ln.claim != null && !string.IsNullOrEmpty(ln.claim.attr))
                {
                    // 대조 가능한 진술 → 클릭 위젯(화자/대사 + 단서 라벨)
                    CrossCheckItemView v = Instantiate(_claimTemplate, _claimContainer);
                    v.gameObject.SetActive(true);
                    string disp = $"{ln.speaker}: {ln.text}";
                    v.Bind("대화", ln.claim.attr, ln.claim.value, ln.claim.label, disp);
                    _claimViews.Add(v);
                }
                else
                {
                    sb.Append("• ").Append(ln.speaker).Append(": ").Append(ln.text).Append('\n');
                }
            }
        }

        if (_logText != null) _logText.text = sb.ToString().TrimEnd('\n');
        if (_root != null)
        {
            _root.SetActive(true);
            BringToFrontCentered(_root.transform);
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
    /// 팝업을 형제 중 맨 앞(=다른 UI 위)으로 올리고 화면 중앙에 놓는다.
    /// 검사 데스크/서류·말풍선보다 항상 위에 그려지고(형제 순서), 닫기·드래그가 가려지지 않게 한다.
    /// 씬마다 다른 위치 오버라이드가 있어도 열 때마다 중앙으로 정렬된다.
    /// </summary>
    private static void BringToFrontCentered(Transform t)
    {
        if (t == null) return;
        t.SetAsLastSibling();
        if (t is RectTransform rt)
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
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        foreach (CrossCheckItemView v in _claimViews)
        {
            if (v != null) yield return v;
        }
    }
}
