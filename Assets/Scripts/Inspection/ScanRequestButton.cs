using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보조검사기 버튼(X-ray / 지문). 누르면 연결된 <see cref="ScanResultPanel"/>을 토글한다.
/// 기본은 잠금(비활성) — 손님이 검사 데이터를 가지고 있어도 누를 수 없다.
/// 뉴스/규정 단서를 손님 정보와 대조해 잠금 해제된 경우에만(검사 데이터 보유 + 잠금 해제) 활성.
/// 잠금 해제는 <see cref="CrossCheckController.OnScanUnlocked"/>(내 종류와 일치 시),
/// 재잠금은 손님 교체(<see cref="InspectionController.OnCustomerChanged"/>) 시 일어난다.
///
/// 판정/변조 로직 없음 — 표시·열람 토글 전용. <see cref="DialogueRequestButton"/> 패턴 계승.
/// </summary>
public sealed class ScanRequestButton : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private InspectionController _controller;
    [SerializeField] private ScanResultPanel _panel;
    [SerializeField] private CrossCheckController _crossCheck; // 잠금 해제 트리거 소스
    [SerializeField] private Button _button;
    [SerializeField] private CanvasGroup _canvasGroup; // 잠금 시 완전히 숨김(트리거 시 출력)

    private bool _unlocked; // 대조로 잠금 해제됨(손님마다 초기화)

    private void Start()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);

        if (_controller != null)
        {
            _controller.OnCustomerChanged += HandleCustomerChanged;
        }
        else
        {
            Debug.LogWarning("[ScanRequestButton] _controller 가 연결되지 않았습니다.");
        }

        if (_crossCheck != null)
        {
            _crossCheck.OnScanUnlocked += HandleScanUnlocked;
        }
        else
        {
            Debug.LogWarning("[ScanRequestButton] _crossCheck 가 연결되지 않았습니다.");
        }

        if (_panel == null) Debug.LogWarning("[ScanRequestButton] _panel 이 연결되지 않았습니다.");

        UpdateInteractable();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
        if (_crossCheck != null) _crossCheck.OnScanUnlocked -= HandleScanUnlocked;
    }

    private void HandleClick()
    {
        if (_panel != null) _panel.Toggle();
    }

    /// <summary>손님이 바뀌면 다시 잠금하고 상태를 갱신한다.</summary>
    private void HandleCustomerChanged()
    {
        _unlocked = false;
        UpdateInteractable();
    }

    /// <summary>대조 잠금 해제 통지. 내 검사 종류와 일치하면 잠금 해제한다.</summary>
    private void HandleScanUnlocked(string scanKind)
    {
        if (_panel == null || string.IsNullOrEmpty(scanKind)) return;
        if (!string.Equals(scanKind, _panel.ScanKindKey, System.StringComparison.OrdinalIgnoreCase)) return;
        _unlocked = true;
        UpdateInteractable();

        // 대조로 잠금 해제되는 순간 결과 패널을 자동으로 연다.
        // 버튼은 그대로 남아 "다시 보기"(토글) 용도로 동작한다. (데이터 없으면 Open 내부 가드가 무시)
        _panel.Open();
    }

    /// <summary>검사 데이터 보유 + 대조 잠금 해제 시에만 "출력"(보이고 누를 수 있음).
    /// 그 외엔 완전히 숨긴다(alpha 0 + 클릭 차단). GameObject 는 활성 유지해 이벤트 구독을 보장.</summary>
    private void UpdateInteractable()
    {
        bool canUse = _panel != null && _panel.HasData && _unlocked;
        if (_button != null) _button.interactable = canUse;
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = canUse ? 1f : 0f;   // 잠금 시 안 보임 → 트리거 시 출력
            _canvasGroup.interactable = canUse;
            _canvasGroup.blocksRaycasts = canUse;
        }
    }
}
