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

    [Tooltip("두 도장(허가/거부) 한 변 크기(px). 이 숫자만 바꾸면 양쪽 도장이 같이 커지고, 아래 안내문구와 안 겹치게 줄 높이도 자동 조절됩니다. 0 이하면 건드리지 않음(프리팹 값 유지). 에디터에서 바꾸면(플레이 안 해도) 즉시 반영.")]
    [SerializeField] private float _stampSize = 110f;

    /// <summary>true=승인, false=거부.</summary>
    public event Action<bool> OnDecision;

    private bool _ready;
    private bool _decided;

    private void Awake()
    {
        if (_drawerButton != null) _drawerButton.onClick.AddListener(OpenTray);
        if (_approveStampButton != null) _approveStampButton.onClick.AddListener(StampApprove);
        if (_rejectStampButton != null) _rejectStampButton.onClick.AddListener(StampReject);
        // (도장 크기는 Awake 에서 강제 적용하지 않는다 — 그러면 수동으로 옮기거나 키운 도장이 플레이마다
        //  _stampSize 값으로 덮어써져 "되돌아감". 크기 조절은 에디터에서 OnValidate 로만 반영(아래) → 직렬화 값이 유지됨.)
        ResetForNextCustomer(false);
    }

    /// <summary>두 도장 크기를 _stampSize 로 맞추고, 도장이 든 가로줄 높이도 키워 안내문구와 겹치지 않게 한다.</summary>
    private void ApplyStampSize()
    {
        if (_stampSize <= 0f) return;
        Vector2 sz = new Vector2(_stampSize, _stampSize);
        if (_approveStampButton != null && _approveStampButton.transform is RectTransform ar) ar.sizeDelta = sz;
        if (_rejectStampButton != null && _rejectStampButton.transform is RectTransform rr) rr.sizeDelta = sz;
        // 도장이 든 가로줄(StampRow = 도장 버튼의 부모)의 LayoutElement 높이도 키운다(있으면).
        Transform row = _rejectStampButton != null ? _rejectStampButton.transform.parent
                      : (_approveStampButton != null ? _approveStampButton.transform.parent : null);
        if (row != null && row.TryGetComponent(out LayoutElement le)) le.preferredHeight = _stampSize + 12f;
    }

#if UNITY_EDITOR
    /// <summary>에디터에서 _stampSize 를 바꾸면 플레이하지 않아도 즉시 도장 크기를 반영(직접 조절 편의).</summary>
    private void OnValidate()
    {
        if (!Application.isPlaying) ApplyStampSize();
    }
#endif

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
