using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 1회성 안내(튜토리얼) 팝업. 풀스크린 backdrop(클릭 흡수)으로 모달 처리하고,
/// 닫기 버튼으로 페이지를 넘기다가 마지막 페이지에서 콜백(예: 첫 손님 입장)을 진행한다.
///
/// 페이지 순서: [인트로(게임 방법) …] → 프리팹 1페이지(스페이스 대조) → [추가(규정집) …]
///   - 프리팹에 그려진 페이지(Box/Title·Body 원본 = 대조 안내)는 손대지 않는다.
///   - 인트로/추가 페이지에서만 Title·Body 글씨를 갈아끼운다.
///   - 닫기 버튼 라벨은 다음 페이지가 있으면 "다음", 마지막이면 "확인".
/// 모달: _root 안에 풀스크린 backdrop(raycastTarget=on)을 두고, Canvas overrideSorting 으로 맨 위에 올려
///       팝업 밖 클릭이 뒤(데스크/버튼)에 닿지 않게 한다.
///
/// 표시·입력 전달만 한다(규약 5장). 컴포넌트는 항상 활성인 오브젝트(예: Canvas)에 두고,
/// _root(팝업 본체)는 비활성으로 시작한다.
/// </summary>
public sealed class TutorialPopup : MonoBehaviour
{
    [System.Serializable]
    public struct TutorialPage
    {
        public string title;
        [TextArea(2, 6)] public string body;
    }

    [SerializeField] private GameObject _root;        // 팝업 본체(backdrop+박스). 비활성 시작.
    [SerializeField] private Button _closeButton;     // 다음/확인

    [Tooltip("대조(프리팹 페이지) '앞'에 보여줄 안내. 비우면 아래 기본 인트로(게임 방법)가 쓰인다.")]
    [SerializeField] private TutorialPage[] _introPages;

    [Tooltip("대조(프리팹 페이지) '뒤'에 이어 보여줄 안내. 비우면 아래 기본 안내(규정집)가 쓰인다.")]
    [SerializeField] private TutorialPage[] _extraPages;

    // 인스펙터에서 비워두면 쓰이는 기본 페이지(채우면 그쪽이 우선).
    private static readonly TutorialPage[] DefaultIntroPages =
    {
        new TutorialPage
        {
            title = "입국 심사 방법",
            body  = "당신은 입국 심사관입니다.\n\n" +
                    "손님의 여권·서류를 살펴보고,\n정상이면 승인 · 위조면 거절하세요."
        }
    };

    private static readonly TutorialPage[] DefaultExtraPages =
    {
        new TutorialPage
        {
            title = "규정집 확인",
            body  = "날마다 규정집 내용이 바뀝니다.\n\n" +
                    "책상의 규정집 버튼으로\n그날의 규정을 꼭 확인하세요."
        }
    };

    private TMP_Text _title, _body, _closeLabel;   // Box 안의 글씨(이름으로 찾음)
    private string _page1Title, _page1Body;        // 프리팹에 그려진 대조 페이지 원본(복원·보존)
    private int _page;                             // 0..: 인트로 → 대조(프리팹) → 추가
    private System.Action _onClosed;

    private TutorialPage[] Intro => (_introPages != null && _introPages.Length > 0) ? _introPages : DefaultIntroPages;
    private TutorialPage[] Extra => (_extraPages != null && _extraPages.Length > 0) ? _extraPages : DefaultExtraPages;
    private int IntroCount => Intro.Length;
    private int TotalPages => Intro.Length + 1 + Extra.Length; // 인트로 + 대조(프리팹 1장) + 추가

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(OnCloseClicked);
        if (_root != null)
        {
            // 박스 안 글씨를 이름으로 찾는다(프리팹 구조: TutorialPanel/Box/Title·Body·CloseButton/Label).
            _title      = _root.transform.Find("Box/Title")?.GetComponent<TMP_Text>();
            _body       = _root.transform.Find("Box/Body")?.GetComponent<TMP_Text>();
            _closeLabel = _root.transform.Find("Box/CloseButton/Label")?.GetComponent<TMP_Text>();
            if (_title != null) _page1Title = _title.text; // 대조 페이지 원본 보존
            if (_body  != null) _page1Body  = _body.text;
            _root.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    /// <summary>튜토리얼을 첫 페이지부터 연다. 마지막 페이지에서 닫으면 <paramref name="onClosed"/> 1회 호출.</summary>
    public void Open(System.Action onClosed)
    {
        _onClosed = onClosed;
        if (_root == null)
        {
            // 팝업 본체가 없으면(미배선) 막히지 않게 곧바로 콜백.
            var cb = _onClosed; _onClosed = null; cb?.Invoke();
            return;
        }
        _page = 0;
        ShowPage(_page);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling(); // 형제 중 맨 앞(다른 UI 위)
    }

    /// <summary>닫기 버튼: 남은 페이지가 있으면 다음 페이지로, 마지막이면 진짜 닫고 콜백.</summary>
    private void OnCloseClicked()
    {
        if (_page < TotalPages - 1)
        {
            _page++;
            ShowPage(_page);
            return;
        }
        Close();
    }

    /// <summary>현재 페이지 글씨를 채운다. 대조 페이지(인트로 다음)는 프리팹 원본을 복원한다.</summary>
    private void ShowPage(int p)
    {
        if (p < IntroCount)               ApplyText(Intro[p]);                  // 인트로(게임 방법)
        else if (p == IntroCount)         RestoreAuthoredPage();               // 대조(프리팹 원본)
        else                              ApplyText(Extra[p - IntroCount - 1]); // 추가(규정집)
        UpdateCloseLabel();
    }

    private void ApplyText(TutorialPage pg)
    {
        if (_title != null) _title.text = pg.title;
        if (_body  != null) _body.text  = pg.body;
    }

    private void RestoreAuthoredPage()
    {
        if (_title != null && _page1Title != null) _title.text = _page1Title;
        if (_body  != null && _page1Body  != null) _body.text  = _page1Body;
    }

    private void UpdateCloseLabel()
    {
        if (_closeLabel == null) return;
        _closeLabel.text = (_page < TotalPages - 1) ? "다음" : "확인";
    }

    /// <summary>실제 닫기. 다음 단계(첫 손님 등) 콜백 진행.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        var cb = _onClosed; _onClosed = null;
        cb?.Invoke();
    }
}
