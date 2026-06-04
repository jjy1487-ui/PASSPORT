using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// '대화/심문' 요청 버튼. 누르면 현재 손님의 요청 가능한 대사를 재생한다(입장/오거부 항의 등).
/// 활성/비활성은 <see cref="InspectionController.CanRequestDialogue"/> 로 결정 — 표시·입력 전달만 한다.
/// 판정/오거부 루프 로직은 컨트롤러 소유.
/// </summary>
public sealed class DialogueRequestButton : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private InspectionController _controller;
    [SerializeField] private Button _button;
    [SerializeField] private CanvasGroup _canvasGroup; // 비활성 시 흐리게(숨기지 않음)

    [Header("표시")]
    [SerializeField] private float _disabledAlpha = 0.4f;

    private void Start()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);

        if (_controller != null)
        {
            _controller.OnRequestableChanged += UpdateInteractable;
            UpdateInteractable(_controller.CanRequestDialogue);
        }
        else
        {
            Debug.LogWarning("[DialogueRequestButton] _controller 가 연결되지 않았습니다.");
            UpdateInteractable(false);
        }
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
        if (_controller != null) _controller.OnRequestableChanged -= UpdateInteractable;
    }

    private void HandleClick()
    {
        if (_controller != null) _controller.RequestDialogue();
    }

    /// <summary>요청 가능 여부에 따라 버튼 상호작용/투명도를 갱신한다.</summary>
    private void UpdateInteractable(bool canRequest)
    {
        if (_button != null) _button.interactable = canRequest;
        if (_canvasGroup != null) _canvasGroup.alpha = canRequest ? 1f : _disabledAlpha;
    }
}
