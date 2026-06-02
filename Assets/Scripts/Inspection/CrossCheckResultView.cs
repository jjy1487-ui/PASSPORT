using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 두 필드 대조 결과 뱃지. "[서류A] 라벨: 값  ↔  [서류B] 라벨: 값 → 일치/불일치" 표시.
/// 일치=녹색, 불일치=빨강(UI-CONVENTIONS 색 시맨틱). 닫기 X는 우상단.
/// 판정 로직 없음 — 컨트롤러가 넘긴 결과를 그대로 그린다.
/// </summary>
public sealed class CrossCheckResultView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _comparisonText; // 두 필드 라벨/값
    [SerializeField] private TMP_Text _resultBadge;     // "일치" / "불일치"
    [SerializeField] private Image _badgeBackground;
    [SerializeField] private Button _closeButton;       // 우상단 X

    [Header("색(UITheme 부재 시 기본값 — UI-CONVENTIONS 2장)")]
    [SerializeField] private Color _matchColor = new Color(0.180f, 0.545f, 0.341f); // Approve/Green #2E8B57
    [SerializeField] private Color _mismatchColor = new Color(0.753f, 0.227f, 0.169f); // Reject/Red #C0392B

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Hide);
        Hide();
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Hide);
    }

    /// <summary>대조 결과를 표시한다(비교는 컨트롤러가 이미 끝낸 상태).</summary>
    public void Show(DocumentFieldView a, DocumentFieldView b, bool isMatch)
    {
        if (a == null || b == null) return;

        if (_comparisonText != null)
        {
            _comparisonText.text =
                $"[{a.DocumentType}] {a.Label}: {a.Value}\n↔\n[{b.DocumentType}] {b.Label}: {b.Value}";
        }

        Color c = isMatch ? _matchColor : _mismatchColor;
        if (_resultBadge != null)
        {
            _resultBadge.text = isMatch ? "일치" : "불일치";
            _resultBadge.color = c;
        }
        if (_badgeBackground != null)
        {
            _badgeBackground.color = new Color(c.r, c.g, c.b, 0.25f);
        }

        if (_root != null) _root.SetActive(true);
    }

    /// <summary>결과 뱃지를 숨긴다.</summary>
    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }
}
