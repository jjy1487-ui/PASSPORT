using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 도장 방식 판정 패널.
/// 측면 탭(드로어) 클릭 → 입국 허가/거부 도장 2개 표시 → 도장 클릭 시
/// 여권 위에 도장 자국(임프린트)이 찍히고 결정(OnDecision)이 발생한다.
/// </summary>
public sealed class JudgmentPanel : MonoBehaviour
{
    [Header("도장 드로어")]
    [SerializeField] private Button _drawerButton;   // 측면 탭(도장 꺼내기)
    [SerializeField] private GameObject _stampTray;   // 도장 2개가 든 트레이(비활성 시작)

    [Header("도장")]
    [SerializeField] private Button _approveStampButton; // 입국 허가(녹색)
    [SerializeField] private Button _rejectStampButton;  // 입국 거부(빨강)

    /// <summary>true=승인, false=거부.</summary>
    public event Action<bool> OnDecision;

    private bool _ready;
    private bool _decided;

    private void Awake()
    {
        if (_drawerButton != null) _drawerButton.onClick.AddListener(OpenTray);
        if (_approveStampButton != null) _approveStampButton.onClick.AddListener(StampApprove);
        if (_rejectStampButton != null) _rejectStampButton.onClick.AddListener(StampReject);
        ResetForNextCustomer(false);
    }

    private void OnDestroy()
    {
        if (_drawerButton != null) _drawerButton.onClick.RemoveListener(OpenTray);
        if (_approveStampButton != null) _approveStampButton.onClick.RemoveListener(StampApprove);
        if (_rejectStampButton != null) _rejectStampButton.onClick.RemoveListener(StampReject);
    }

    /// <summary>판정 가능 상태로 전환(대화 중에는 false).</summary>
    public void SetReady(bool ready)
    {
        _ready = ready;
        if (_drawerButton != null)
        {
            _drawerButton.interactable = ready && !_decided;
        }
        if (!ready)
        {
            CloseTray();
        }
    }

    /// <summary>다음 손님(또는 오거부 재시도) 준비: 트레이 초기화.</summary>
    public void ResetForNextCustomer(bool ready)
    {
        _decided = false;
        CloseTray();
        SetReady(ready);
    }

    private void OpenTray()
    {
        if (!_ready || _decided || _stampTray == null) return;
        // 열려있으면 닫고, 닫혀있으면 연다 (토글)
        _stampTray.SetActive(!_stampTray.activeSelf);
    }

    private void CloseTray()
    {
        if (_stampTray != null) _stampTray.SetActive(false);
    }

    private void StampApprove() => Decide(true);
    private void StampReject() => Decide(false);

    private void Decide(bool approve)
    {
        if (!_ready || _decided) return;
        _decided = true;
        CloseTray();
        if (_drawerButton != null) _drawerButton.interactable = false;
        OnDecision?.Invoke(approve); // 도장 자국은 컨트롤러가 여권 위에 찍는다
    }
}
