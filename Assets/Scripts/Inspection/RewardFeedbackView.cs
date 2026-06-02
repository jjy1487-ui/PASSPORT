using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 호칭·아이템 획득 피드백.
///  - 토스트: ScoreEconomyManager.OnTitleEarned / OnItemEarned 수신 시 짧게 떠오른다.
///  - 보유 목록 패널: 버튼으로 열어 현재까지 모은 호칭·아이템을 본다(닫기 = X, 우상단; UI-CONVENTIONS 1장).
///
/// 표시만 한다. 획득 판정은 매니저가 소유(규약 5장).
/// </summary>
public sealed class RewardFeedbackView : MonoBehaviour
{
    [Header("토스트")]
    [SerializeField] private GameObject _toastRoot;   // 비활성 시작
    [SerializeField] private TMP_Text _toastText;
    [SerializeField] private float _toastSeconds = 2.2f;

    [Header("보유 목록 패널")]
    [SerializeField] private GameObject _listRoot;    // 비활성 시작
    [SerializeField] private TMP_Text _listText;
    [SerializeField] private Button _openButton;
    [SerializeField] private Button _closeButton;     // X, 우상단

    private ScoreEconomyManager _mgr;
    private Coroutine _toastRoutine;
    private readonly Queue<string> _toastQueue = new Queue<string>();

    private void Start()
    {
        _mgr = ScoreEconomyManager.Instance;
        if (_mgr != null)
        {
            _mgr.OnTitleEarned += HandleTitleEarned;
            _mgr.OnItemEarned += HandleItemEarned;
        }
        else
        {
            Debug.LogWarning("[RewardFeedbackView] ScoreEconomyManager.Instance 가 없습니다. 획득 피드백 비활성.");
        }

        if (_openButton != null) _openButton.onClick.AddListener(OpenList);
        if (_closeButton != null) _closeButton.onClick.AddListener(CloseList);

        if (_toastRoot != null) _toastRoot.SetActive(false);
        if (_listRoot != null) _listRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_mgr != null)
        {
            _mgr.OnTitleEarned -= HandleTitleEarned;
            _mgr.OnItemEarned -= HandleItemEarned;
        }
        if (_openButton != null) _openButton.onClick.RemoveListener(OpenList);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(CloseList);
    }

    // ── 획득 수신 → 토스트 ─────────────────────────────────────
    private void HandleTitleEarned(string title) => EnqueueToast($"호칭 획득: {title}");
    private void HandleItemEarned(string item) => EnqueueToast($"아이템 획득: {item}");

    private void EnqueueToast(string message)
    {
        _toastQueue.Enqueue(message);
        if (_toastRoutine == null) _toastRoutine = StartCoroutine(ToastLoop());
    }

    private IEnumerator ToastLoop()
    {
        while (_toastQueue.Count > 0)
        {
            string msg = _toastQueue.Dequeue();
            if (_toastRoot != null) _toastRoot.SetActive(true);
            if (_toastText != null) _toastText.text = msg;
            yield return new WaitForSeconds(_toastSeconds);
        }
        if (_toastRoot != null) _toastRoot.SetActive(false);
        _toastRoutine = null;
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

        var sb = new StringBuilder();
        sb.AppendLine("<b>호칭</b>");
        if (_mgr.Titles.Count == 0) sb.AppendLine("  (없음)");
        else foreach (string t in _mgr.Titles) sb.AppendLine($"  · {t}");

        sb.AppendLine();
        sb.AppendLine("<b>아이템</b>");
        if (_mgr.Items.Count == 0) sb.AppendLine("  (없음)");
        else foreach (string it in _mgr.Items) sb.AppendLine($"  · {it}");

        _listText.text = sb.ToString();
    }
}
