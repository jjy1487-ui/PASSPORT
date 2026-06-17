using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1회성 안내(튜토리얼) 팝업. 풀스크린 backdrop(클릭 흡수)으로 모달 처리하고,
/// 닫기 버튼을 누르면 콜백으로 다음 단계(예: 첫 손님 입장)를 진행한다.
///
/// 흐름(하루 시작): 뉴스 닫기 → (day1) 이 튜토리얼 → 닫기 → 게임 진행.
/// 모달: _root 안에 풀스크린 backdrop(raycastTarget=on)을 두고, Canvas overrideSorting 으로 맨 위에 올려
///       팝업 밖 클릭이 뒤(데스크/버튼)에 닿지 않게 한다 — "다른 데 눌러도 안 먹힘".
///
/// 표시·입력 전달만 한다(규약 5장). 컴포넌트는 항상 활성인 오브젝트(예: Canvas)에 두고,
/// _root(팝업 본체)는 비활성으로 시작한다(Awake 가 닫기 배선 + 숨김).
/// </summary>
public sealed class TutorialPopup : MonoBehaviour
{
    [SerializeField] private GameObject _root;        // 팝업 본체(backdrop+박스). 비활성 시작.
    [SerializeField] private Button _closeButton;     // 확인/닫기

    private System.Action _onClosed;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    /// <summary>튜토리얼을 연다. 닫으면 <paramref name="onClosed"/> 를 1회 호출한다(없으면 그냥 닫힘).</summary>
    public void Open(System.Action onClosed)
    {
        _onClosed = onClosed;
        if (_root != null)
        {
            _root.SetActive(true);
            _root.transform.SetAsLastSibling(); // 형제 중 맨 앞(다른 UI 위)
        }
        else
        {
            // 팝업 본체가 없으면(미배선) 막히지 않게 곧바로 콜백 진행.
            var cb = _onClosed; _onClosed = null; cb?.Invoke();
        }
    }

    /// <summary>닫기(확인). 콜백으로 다음 단계 진행.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        var cb = _onClosed; _onClosed = null;
        cb?.Invoke();
    }
}
