using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// 좌측 상단 상시 아이템 인디케이터. 플레이 중 획득한 아이템을 작게 나열한다.
/// ScoreEconomyManager.OnItemEarned 를 구독하고, 시작 시 보유 목록(이어하기 복원분 포함)을 반영한다.
///
/// 표시만 한다 — 아이템 획득 판정은 매니저가 소유(규약 5장).
/// 토스트가 아닌 상시 위젯이다(요구 4: 좌상단 작게 상시 표시).
/// </summary>
public sealed class ItemIndicatorView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;     // 아이템 0개면 숨김
    [SerializeField] private TMP_Text _itemsText;  // 보유 아이템 줄나열(작게)

    private ScoreEconomyManager _mgr;

    private void Start()
    {
        _mgr = ScoreEconomyManager.Instance;
        if (_mgr == null)
        {
            Debug.LogWarning("[ItemIndicatorView] ScoreEconomyManager.Instance 가 없습니다. 아이템 표시 비활성.");
            Refresh();
            return;
        }
        _mgr.OnItemEarned += HandleItemEarned;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_mgr != null) _mgr.OnItemEarned -= HandleItemEarned;
    }

    private void HandleItemEarned(string item) => Refresh();

    private void Refresh()
    {
        int count = _mgr != null ? _mgr.Items.Count : 0;
        if (_root != null) _root.SetActive(count > 0);

        if (_itemsText == null) return;
        if (count == 0) { _itemsText.text = string.Empty; return; }

        var sb = new StringBuilder();
        foreach (string it in _mgr.Items)
            sb.Append("· ").AppendLine(it);
        _itemsText.text = sb.ToString().TrimEnd();
    }
}
