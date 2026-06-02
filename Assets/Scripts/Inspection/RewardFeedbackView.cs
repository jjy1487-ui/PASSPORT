using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보유 아이템 목록 패널(버튼으로 열람). 닫기 = X, 우상단(UI-CONVENTIONS 1장).
///
/// 표시 정책 변경(요구 3·4):
///  - 호칭: 플레이 중 토스트·목록에서 제거 → 메인 메뉴 '업적' 패널(TitleAchievementPanel)에서만 확인.
///  - 아이템: 토스트 제거 → 좌상단 상시 위젯(ItemIndicatorView)으로 상시 표시. 이 패널은 전체 목록 열람용 보조.
///
/// 표시만 한다. 획득 판정은 ScoreEconomyManager 가 소유(규약 5장).
/// </summary>
public sealed class RewardFeedbackView : MonoBehaviour
{
    [Header("보유 아이템 목록 패널")]
    [SerializeField] private GameObject _listRoot;    // 비활성 시작
    [SerializeField] private TMP_Text _listText;
    [SerializeField] private Button _openButton;
    [SerializeField] private Button _closeButton;     // X, 우상단

    private ScoreEconomyManager _mgr;

    private void Start()
    {
        _mgr = ScoreEconomyManager.Instance;
        if (_mgr == null)
            Debug.LogWarning("[RewardFeedbackView] ScoreEconomyManager.Instance 가 없습니다. 보유 목록 비활성.");

        if (_openButton != null) _openButton.onClick.AddListener(OpenList);
        if (_closeButton != null) _closeButton.onClick.AddListener(CloseList);

        if (_listRoot != null) _listRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_openButton != null) _openButton.onClick.RemoveListener(OpenList);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(CloseList);
    }

    // ── 보유 목록 ──────────────────────────────────────────────
    private void OpenList()
    {
        RefreshList();
        if (_listRoot != null) _listRoot.SetActive(true);
    }

    private void CloseList()
    {
        if (_listRoot != null) _listRoot.SetActive(false);
    }

    private void RefreshList()
    {
        if (_listText == null) return;
        if (_mgr == null) { _listText.text = "(데이터 없음)"; return; }

        // 호칭은 플레이 중 비공개(메인 메뉴 업적 패널 전용) — 여기선 아이템만 표시.
        var sb = new StringBuilder();
        sb.AppendLine("<b>보유 아이템</b>");
        if (_mgr.Items.Count == 0) sb.AppendLine("  (없음)");
        else foreach (string it in _mgr.Items) sb.AppendLine($"  · {it}");

        _listText.text = sb.ToString();
    }
}
