using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>대조 결과 4-상태. 속성 키(AttributeKey)로 1차 관련성, 값 정규화 비교로 2차 판정.</summary>
public enum CrossCheckResult
{
    /// <summary>두 항목의 속성 키가 다름 → 비교 대상이 아님(값 비교 안 함).</summary>
    Unrelated,
    /// <summary>속성 키 같음 + 양쪽 값 있음 + 정규화 값 동일.</summary>
    Match,
    /// <summary>속성 키 같음 + 양쪽 값 있음 + 값 다름.</summary>
    Mismatch,
    /// <summary>속성 키 같지만 한쪽 값이 비어 값 비교 불가(규정/일부 뉴스·대화) → 관련성만.</summary>
    Related,
}

/// <summary>
/// 이종 소스(서류/캐릭터/뉴스/대화/규정) 교차 대조 컨트롤러. 여러 <see cref="ICrossCheckProvider"/>에서
/// <see cref="ICrossCheckSelectable"/>을 모아 구독하고, 선택된 두 항목을 속성 키(관련성)·값(정규화)으로
/// 비교해 4-상태(관련없음/일치/불일치/관련있음)를 <see cref="CrossCheckConnectorView"/>로 통지한다.
///
/// 중요: 이 비교는 "표시"일 뿐 게임 규칙(판정/점수)에 영향을 주지 않는다.
/// 정답·변조 판정은 gameplay(JudgmentPanel/InspectionController)가 소유한다.
/// </summary>
public sealed class CrossCheckController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private DocumentView _documentView;             // 서류(기본 공급자, 자동 등록)
    [SerializeField] private CrossCheckConnectorView _connectorView; // 점선+깜빡임 인플레이스 연출

    [Header("추가 공급자(캐릭터/뉴스/대화/규정 등 ICrossCheckProvider)")]
    [Tooltip("MonoBehaviour 중 ICrossCheckProvider 를 구현한 소스 뷰들을 연결.")]
    [SerializeField] private MonoBehaviour[] _extraProviders;

    [Header("동작")]
    [SerializeField] private bool _enabledOnStart = true; // 상시 클릭선택(도구 버튼에 연결하면 토글 가능)

    private readonly List<ICrossCheckProvider> _providers = new List<ICrossCheckProvider>();
    private readonly List<ICrossCheckSelectable> _subscribed = new List<ICrossCheckSelectable>();
    private ICrossCheckSelectable _first;
    private ICrossCheckSelectable _second;
    private bool _active;

    /// <summary>
    /// 잠금 해제 트리거 발생 통지. 인자 = 잠금 해제할 스캔 종류("xray"|"fingerprint").
    /// 뉴스/규정 단서(UnlocksScan!="")가 손님 소스(서류/캐릭터)와 Match/Related 되면 발행한다.
    /// 판정/점수에는 영향 없음 — UI 게이팅 전용.
    /// </summary>
    public event System.Action<string> OnScanUnlocked;

    private void Start()
    {
        _active = _enabledOnStart;
        CollectProviders();

        foreach (ICrossCheckProvider p in _providers)
        {
            p.OnSelectablesChanged += Rebind;
        }

        Rebind();
    }

    private void OnDestroy()
    {
        foreach (ICrossCheckProvider p in _providers)
        {
            if (p != null) p.OnSelectablesChanged -= Rebind;
        }
        _providers.Clear();
        UnsubscribeAll();
    }

    /// <summary>대조 도구 on/off(도구바 버튼에서 호출 가능). off 시 선택 초기화.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (!active) ClearSelection();
    }

    /// <summary>대조 도구 토글.</summary>
    public void Toggle() => SetActive(!_active);

    /// <summary>런타임에 공급자를 추가 등록한다(예: 팝업 열림 시).</summary>
    public void RegisterProvider(ICrossCheckProvider provider)
    {
        if (provider == null || _providers.Contains(provider)) return;
        _providers.Add(provider);
        provider.OnSelectablesChanged += Rebind;
        Rebind();
    }

    /// <summary>공급자를 해제한다(예: 팝업 닫힘 시).</summary>
    public void UnregisterProvider(ICrossCheckProvider provider)
    {
        if (provider == null || !_providers.Contains(provider)) return;
        provider.OnSelectablesChanged -= Rebind;
        _providers.Remove(provider);
        Rebind();
    }

    // ── 공급자 수집 ──────────────────────────────────────────────
    private void CollectProviders()
    {
        _providers.Clear();

        if (_documentView != null) _providers.Add(_documentView);
        else Debug.LogWarning("[CrossCheckController] _documentView 가 연결되지 않았습니다.");

        if (_extraProviders != null)
        {
            foreach (MonoBehaviour mb in _extraProviders)
            {
                if (mb is ICrossCheckProvider p && !_providers.Contains(p)) _providers.Add(p);
                else if (mb != null && !(mb is ICrossCheckProvider))
                    Debug.LogWarning($"[CrossCheckController] {mb.GetType().Name} 은 ICrossCheckProvider 가 아닙니다.");
            }
        }
    }

    // ── selectable 구독 관리 ─────────────────────────────────────
    private void Rebind()
    {
        UnsubscribeAll();
        ClearSelection();

        foreach (ICrossCheckProvider p in _providers)
        {
            if (p == null) continue;
            IEnumerable<ICrossCheckSelectable> items = p.GetSelectables();
            if (items == null) continue;
            foreach (ICrossCheckSelectable item in items)
            {
                if (item == null) continue;
                item.OnSelected += HandleSelected;
                _subscribed.Add(item);
            }
        }
    }

    private void UnsubscribeAll()
    {
        foreach (ICrossCheckSelectable item in _subscribed)
        {
            if (item != null) item.OnSelected -= HandleSelected;
        }
        _subscribed.Clear();
    }

    // ── 선택/비교 ────────────────────────────────────────────────
    private void HandleSelected(ICrossCheckSelectable item)
    {
        if (!_active || item == null) return;

        // 같은 항목 재선택 → 선택 취소
        if (ReferenceEquals(item, _first))
        {
            item.SetSelected(false);
            _first = _second;
            _second = null;
            return;
        }
        if (ReferenceEquals(item, _second))
        {
            item.SetSelected(false);
            _second = null;
            return;
        }

        if (_first == null)
        {
            _first = item;
            item.SetSelected(true);
            return;
        }

        if (_second == null)
        {
            _second = item;
            item.SetSelected(true);
            Compare(_first, _second);
            return;
        }

        // 이미 두 개 선택됨 → 새 비교 시작(이전 선택 해제 후 이 항목을 첫 선택으로)
        ClearSelection();
        _first = item;
        item.SetSelected(true);
    }

    private void Compare(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        // 오늘 날짜 ↔ 날짜 필드면 문자열 일치 대신 날짜 비교(만료됨/유효 등). 성립하면 라벨 텍스트 오버라이드.
        // 보조 표시 — 판정/점수 무영향.
        string overrideText = null;
        CrossCheckResult result =
            TryEvaluateDate(a, b, out CrossCheckResult dateResult, out overrideText)
                ? dateResult
                : Evaluate(a, b);

        if (_connectorView != null)
        {
            _connectorView.Show(a.Rect, b.Rect, result, overrideText);
        }
        else
        {
            Debug.LogWarning("[CrossCheckController] _connectorView 가 연결되지 않았습니다.");
        }

        DetectScanUnlock(a, b, result);
    }

    // ── 오늘 날짜 인식 대조 ───────────────────────────────────────
    /// <summary>
    /// 두 선택지 중 하나가 "오늘"(SourceType=="오늘" 또는 attr=="today")이고 다른 하나의 attr이
    /// 날짜 필드면, 문자열 일치 대신 날짜 유효성을 판정한다(멘트는 일반 대조와 동일하게 일치/불일치).
    /// - 만료류(expiry_date/valid_until): 오늘이 만료일 이내(오늘 ≤ 날짜) → 일치(Match), 지났으면 → 불일치(Mismatch).
    /// - 발급류(issue_date/test_date/birth_date): 발급일이 오늘 이전/같음(날짜 ≤ 오늘) → 일치, 미래면 → 불일치.
    /// 날짜 필드가 아니면(예: 여권번호) false → 일반 로직(→ 관련 없음).
    /// 표시 보조 — 판정/점수 무영향.
    /// </summary>
    private static bool TryEvaluateDate(ICrossCheckSelectable a, ICrossCheckSelectable b,
        out CrossCheckResult result, out string overrideText)
    {
        result = CrossCheckResult.Unrelated;
        overrideText = null; // 날짜도 일반 대조와 동일한 일치/불일치 멘트 사용(특수 문구 없음).

        ICrossCheckSelectable today = IsToday(a) ? a : (IsToday(b) ? b : null);
        ICrossCheckSelectable other = ReferenceEquals(today, a) ? b : a;
        if (today == null || other == null) return false;

        string dateKey = NormalizeKey(other.AttributeKey);
        if (!IsDateKey(dateKey)) return false;

        if (!TryParseDate(today.Value, out System.DateTime todayDate)) return false;
        if (!TryParseDate(other.Value, out System.DateTime fieldDate)) return false;

        bool isExpiry = dateKey == "expiry_date" || dateKey == "valid_until";
        bool valid = isExpiry
            ? todayDate.Date <= fieldDate.Date   // 오늘이 만료일 이내면 유효 → 일치
            : fieldDate.Date <= todayDate.Date;  // 발급일이 오늘 이전이면 정상 → 일치
        result = valid ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
        return true;
    }

    private static bool IsToday(ICrossCheckSelectable s) =>
        s != null && (s.SourceType == "오늘" || NormalizeKey(s.AttributeKey) == "today");

    private static bool IsDateKey(string key) => key switch
    {
        "expiry_date" or "valid_until" or "issue_date" or "test_date" or "birth_date" => true,
        _ => false,
    };

    private static bool TryParseDate(string value, out System.DateTime date)
    {
        date = default;
        if (string.IsNullOrEmpty(value)) return false;
        return System.DateTime.TryParse(value.Trim(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// 잠금 해제 트리거 감지: 결과가 Match/Related 이고, 두 항목 중 하나가 트리거 단서(UnlocksScan!="")이며
    /// 다른 하나가 손님 소스("서류" 또는 "캐릭터")면 해당 스캔 잠금 해제 이벤트를 발행한다.
    /// (판정/점수 무영향 — 표시·게이팅 전용.)
    /// </summary>
    private void DetectScanUnlock(ICrossCheckSelectable a, ICrossCheckSelectable b, CrossCheckResult result)
    {
        if (result != CrossCheckResult.Match && result != CrossCheckResult.Related) return;

        if (TryUnlockPair(a, b)) return;
        TryUnlockPair(b, a);
    }

    // trigger 가 트리거 단서이고 other 가 손님 소스면 발행. 발행했으면 true.
    private bool TryUnlockPair(ICrossCheckSelectable trigger, ICrossCheckSelectable other)
    {
        if (trigger == null || other == null) return false;
        if (string.IsNullOrEmpty(trigger.UnlocksScan)) return false;
        if (!IsCustomerSource(other.SourceType)) return false;

        OnScanUnlocked?.Invoke(trigger.UnlocksScan);
        return true;
    }

    private static bool IsCustomerSource(string sourceType) =>
        sourceType == "서류" || sourceType == "캐릭터";

    /// <summary>
    /// 4-상태 평가:
    /// - 속성 키 다름 또는 한쪽 비어있음 → 관련 없음.
    /// - 속성 키 같고 한쪽 Value="" → 관련 있음(값 비교 불가).
    /// - 속성 키 같고 양쪽 Value 있음 → 정규화 일치/불일치.
    /// </summary>
    private static CrossCheckResult Evaluate(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        string ka = NormalizeKey(a.AttributeKey);
        string kb = NormalizeKey(b.AttributeKey);

        if (ka.Length == 0 || kb.Length == 0 || ka != kb) return CrossCheckResult.Unrelated;

        bool va = !string.IsNullOrEmpty(a.Value);
        bool vb = !string.IsNullOrEmpty(b.Value);
        if (!va || !vb) return CrossCheckResult.Related; // 같은 속성, 값 비교 불가

        return Normalize(a.Value) == Normalize(b.Value)
            ? CrossCheckResult.Match
            : CrossCheckResult.Mismatch;
    }

    /// <summary>속성 키 정규화: 공백 제거 + 소문자(영문 snake_case 가정).</summary>
    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return key.Trim().ToLowerInvariant();
    }

    private void ClearSelection()
    {
        if (_first != null) _first.SetSelected(false);
        if (_second != null) _second.SetSelected(false);
        _first = null;
        _second = null;
        if (_connectorView != null) _connectorView.Hide();
    }

    /// <summary>
    /// 대조용 값 정규화: 대소문자 무시, 모든 공백 제거, MRZ 채움문자 '&lt;' 및 흔한 구분기호 제거.
    /// 예) "KIM&lt;&lt;MINJUN" ↔ "KIM MINJUN" 을 일치로 처리.
    /// </summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        StringBuilder sb = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '<':
                case ' ':
                case '\t':
                case '\n':
                case '\r':
                case '-':
                case '.':
                case ',':
                case '/':
                case ':':
                    continue; // 구분/채움 문자 제거
                default:
                    sb.Append(char.ToUpperInvariant(ch));
                    break;
            }
        }
        return sb.ToString();
    }
}
