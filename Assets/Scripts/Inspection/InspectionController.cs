using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 1일차 심사 진행 드라이버. 7손님 큐를 돌며 서류 표시 → 판정 → 대사 피드백 → 다음 손님,
/// 마지막 손님 후 1일차 완료 패널을 띄운다.
/// </summary>
public sealed class InspectionController : MonoBehaviour
{
    [Header("뷰 참조")]
    [SerializeField] private CustomerView _customerView;
    [SerializeField] private DocumentView _documentView;
    [SerializeField] private DialogueView _dialogueView;
    [SerializeField] private JudgmentPanel _judgmentPanel;

    [Header("기타 UI")]
    [SerializeField] private TMP_Text _slotCounterText;
    [SerializeField] private TMP_Text _goldText;
    [SerializeField] private GameObject _dayCompleteRoot;

    private const int MaxWrongReject = 3; // 오거부 3회 후 강제 통과
    private const int CorrectBonus = 10;  // 정답 보너스
    private const int WrongPenalty = 15;  // 오판 벌금

    private int _gold;

    private Day1Data _data;
    private int _index;
    private int _wrongRejectCount;
    private readonly List<string> _dialogueLog = new List<string>(); // 현재 손님의 대화 기록

    /// <summary>음성기록 팝업용: 현재 손님이 한 대화 목록.</summary>
    public IReadOnlyList<string> GetDialogueLog() => _dialogueLog;

    private void OnEnable()
    {
        if (_judgmentPanel != null)
        {
            _judgmentPanel.OnDecision += HandleDecision;
        }
    }

    private void OnDisable()
    {
        if (_judgmentPanel != null)
        {
            _judgmentPanel.OnDecision -= HandleDecision;
        }
    }

    /// <summary>데이터를 받아 1일차를 시작한다.</summary>
    public void Initialize(Day1Data data)
    {
        if (data == null || data.customers == null || data.customers.Length == 0)
        {
            Debug.LogError("[InspectionController] 유효한 데이터가 없어 시작할 수 없습니다.");
            return;
        }

        _data = data;
        _index = 0;
        _gold = 0;
        UpdateGold();
        if (_dayCompleteRoot != null) _dayCompleteRoot.SetActive(false);
        ShowCustomer(0);
    }

    private CustomerData Current =>
        (_data != null && _index >= 0 && _index < _data.customers.Length)
            ? _data.customers[_index] : null;

    private void ShowCustomer(int index)
    {
        _index = index;
        if (_data == null || index >= _data.customers.Length)
        {
            ShowDayComplete();
            return;
        }

        CustomerData c = _data.customers[index];
        _wrongRejectCount = 0;
        _dialogueLog.Clear();

        if (_customerView != null) _customerView.Show(c);
        if (_documentView != null) _documentView.Show(c.documents);
        if (_slotCounterText != null) _slotCounterText.text = $"{index + 1} / {_data.customers.Length}";
        if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(false);

        // 입장 대사 후 판정 활성화
        DialogueCaseData entry = FindCaseByType(c, CaseTypes.Entry);
        if (entry != null)
        {
            PlayThen(entry, EnableJudgment);
        }
        else
        {
            EnableJudgment();
        }
    }

    private void EnableJudgment()
    {
        if (_judgmentPanel != null) _judgmentPanel.SetReady(true);
    }

    private void UpdateGold()
    {
        if (_goldText != null) _goldText.text = _gold.ToString();
    }

    private void HandleDecision(bool approve)
    {
        CustomerData c = Current;
        if (c == null) return;

        // 여권 종이 위에 도장 자국
        if (_documentView != null) _documentView.StampPrimary(approve);

        bool shouldApprove = c.correctResult == GameResults.Approve;

        // 골드 정산(정답 보너스 / 오판 벌금)
        _gold += (approve == shouldApprove) ? CorrectBonus : -WrongPenalty;
        UpdateGold();

        if (approve == shouldApprove)
        {
            // 정답: 정상 승인 / 정상 거절 케이스 재생 후 진행
            DialogueCaseData ok = FindCase(c, c.correctResult, -1);
            PlayThen(ok, AdvanceNext);
        }
        else if (!approve)
        {
            // 오거부(승인해야 하는데 거부): 1→3단계 연출, 3회 후 강제 통과
            _wrongRejectCount++;
            DialogueCaseData wrong = FindCase(c, GameResults.WrongReject, _wrongRejectCount);
            if (_wrongRejectCount >= MaxWrongReject || wrong == null)
            {
                PlayThen(wrong, AdvanceNext); // 강제 통과
            }
            else
            {
                // 같은 손님 재시도 허용(도장 자국 지움)
                PlayThen(wrong, () =>
                {
                    if (_documentView != null) _documentView.ClearStamps();
                    if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(true);
                });
            }
        }
        else
        {
            // 오허가(거부해야 하는데 승인): 피드백 후 진행(MVP 패널티 없음)
            DialogueCaseData wrong = FindCase(c, GameResults.WrongApprove, -1);
            PlayThen(wrong, AdvanceNext);
        }
    }

    private void PlayThen(DialogueCaseData dialogueCase, System.Action onComplete)
    {
        AppendLog(dialogueCase);
        if (_dialogueView != null)
        {
            _dialogueView.Play(dialogueCase, onComplete);
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    private void AppendLog(DialogueCaseData dialogueCase)
    {
        if (dialogueCase == null || dialogueCase.lines == null)
        {
            return;
        }
        DialogueLineData[] arr = (DialogueLineData[])dialogueCase.lines.Clone();
        System.Array.Sort(arr, (a, b) => a.order.CompareTo(b.order));
        foreach (DialogueLineData ln in arr)
        {
            _dialogueLog.Add(ln.speaker + ": " + ln.text);
        }
    }

    private void AdvanceNext()
    {
        ShowCustomer(_index + 1);
    }

    private void ShowDayComplete()
    {
        if (_customerView != null) _customerView.Hide();
        if (_documentView != null) _documentView.Clear();
        if (_dialogueView != null) _dialogueView.Hide();
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
        if (_dayCompleteRoot != null) _dayCompleteRoot.SetActive(true);
        Debug.Log("[InspectionController] 1일차 완료");
    }

    private static DialogueCaseData FindCaseByType(CustomerData c, string caseType)
    {
        if (c?.dialogueCases == null) return null;
        foreach (DialogueCaseData dc in c.dialogueCases)
        {
            if (dc.caseType == caseType) return dc;
        }
        return null;
    }

    private static DialogueCaseData FindCase(CustomerData c, string gameResult, int rejectCount)
    {
        if (c?.dialogueCases == null) return null;
        foreach (DialogueCaseData dc in c.dialogueCases)
        {
            if (dc.gameResult != gameResult) continue;
            if (rejectCount >= 0 && dc.rejectCount != rejectCount) continue;
            return dc;
        }
        return null;
    }
}
