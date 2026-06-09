using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

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
    [Tooltip("현재 손님의 이름/유형을 읽어 불일치 대사 화자·반응을 맞추기 위한 참조. 비우면 자동 탐색.")]
    [SerializeField] private InspectionController _inspection;        // 현재 손님 정보 제공(읽기 전용)

    [Header("추가 공급자(캐릭터/뉴스/대화/규정 등 ICrossCheckProvider)")]
    [Tooltip("MonoBehaviour 중 ICrossCheckProvider 를 구현한 소스 뷰들을 연결.")]
    [SerializeField] private MonoBehaviour[] _extraProviders;

    [Header("동작")]
    [Tooltip("시작 시 대조 활성 여부. false면 스페이스바로만 진입.")]
    [SerializeField] private bool _enabledOnStart = false; // 스페이스바로만 대조 모드 진입
    [Tooltip("대조 모드 토글 키(새 Input System).")]
    [SerializeField] private Key _toggleKey = Key.Space;
    [Tooltip("한 번 비교(2항목 선택)하고 나면 자동으로 대조 모드 해제.")]
    [SerializeField] private bool _releaseAfterCompare = true;

    [Header("대조 모드 UI")]
    [Tooltip("대조 모드 중에만 켜지는 안내 표시(예: '대조 모드 (Space)').")]
    [SerializeField] private GameObject _modeIndicator;
    [Tooltip("불일치 시 검사관/손님 대사를 기존 하단 대사 박스와 동일한 스타일로 재생할 전용 DialogueView.\n" +
             "메인 손님 대사 흐름(InspectionController)이 쓰는 DialogueView 와 충돌하지 않도록 별도 인스턴스를 연결한다.")]
    [SerializeField] private DialogueView _mismatchDialogue;

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

    /// <summary>두 항목 비교 직후 발행(a, b, 결과). 불일치 대사 등 보조 연출용.</summary>
    public event System.Action<ICrossCheckSelectable, ICrossCheckSelectable, CrossCheckResult> OnCompared;

    private void Start()
    {
        _active = _enabledOnStart;
        if (_modeIndicator != null) _modeIndicator.SetActive(_active);
        if (_mismatchDialogue != null) _mismatchDialogue.Hide();
        if (_inspection == null) _inspection = FindObjectOfType<InspectionController>();
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

    /// <summary>대조 도구 on/off(스페이스바/버튼에서 호출). off 시 선택 초기화.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (_modeIndicator != null) _modeIndicator.SetActive(active);
        if (active)
        {
            // 새 대조 진입 → 이전 결과/대사 정리
            if (_mismatchDialogue != null) _mismatchDialogue.Hide();
            if (_connectorView != null) _connectorView.Hide();
        }
        if (!active) ClearSelection();
    }

    /// <summary>대조 도구 토글.</summary>
    public void Toggle() => SetActive(!_active);

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[_toggleKey].wasPressedThisFrame)
        {
            Toggle();
        }
    }

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
        if (_mismatchDialogue != null) _mismatchDialogue.Hide();

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
        Debug.Log($"[CrossCheckDBG] click active={_active} item={(item == null ? "null" : item.SourceType + "/" + item.AttributeKey + "/'" + item.Value + "'")}");
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

        Debug.Log($"[CrossCheckDBG] compare a={a.SourceType}/{a.AttributeKey}/'{a.Value}' b={b.SourceType}/{b.AttributeKey}/'{b.Value}' -> {result}");

        if (_connectorView != null)
        {
            _connectorView.Show(a.Rect, b.Rect, result, overrideText);
        }
        else
        {
            Debug.LogWarning("[CrossCheckController] _connectorView 가 연결되지 않았습니다.");
        }

        DetectScanUnlock(a, b, result);
        DetectFaceMismatchUnlock(a, b, result); // 얼굴↔여권사진 불일치 → 지문 잠금해제(뉴스와 이중 트리거)

        // 결과별 보조 대사:
        // - 불일치: 서류 정합 항목이면 "안 맞네요" 지적. 단, 경보(워치리스트) 단서와의 불일치는
        //   "대상 아님"을 뜻하므로 조용히 넘어간다(기계적 오발 대사 방지).
        // - 일치: 경보 단서가 손님과 일치하면 위험 경고 대사(해당 스캔 안내).
        if (result == CrossCheckResult.Mismatch)
        {
            if (!IsWatchlistInvolved(a, b)) ShowMismatchComment(a, b);
        }
        else if (result == CrossCheckResult.Match)
        {
            ShowWatchlistAlertIfAny(a, b);
        }
        OnCompared?.Invoke(a, b, result);

        // 1회 대조 완료 → 대조 모드 자동 해제(스페이스 다시 눌러 재진입).
        // 단, 방금 띄운 연결선 결과는 남겨둔다(ClearSelection 의 Hide 를 부르지 않음).
        // 결과 표시는 다음 대조 진입(SetActive(true)) 또는 손님 교체(Rebind) 시 정리된다.
        if (_releaseAfterCompare)
        {
            _active = false;
            if (_modeIndicator != null) _modeIndicator.SetActive(false);
            if (_first != null) _first.SetSelected(false);
            if (_second != null) _second.SetSelected(false);
            _first = null;
            _second = null;
        }
    }

    /// <summary>
    /// 불일치 항목에 대한 검사관 지적 + 손님 반응 2줄 대사를, 기존 하단 대사 박스와 동일한 스타일의
    /// 전용 <see cref="DialogueView"/>로 재생한다(보조 연출, 판정 무영향).
    /// 항목 종류(이름/사진/기간/번호)에 따라 손님 반응이 달라진다.
    /// 메인 손님 대사 흐름과 충돌하지 않도록 별도 DialogueView 인스턴스를 사용한다.
    /// </summary>
    private void ShowMismatchComment(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        if (_mismatchDialogue == null) return;
        string label = a != null && !string.IsNullOrEmpty(a.DisplayLabel) ? a.DisplayLabel
                     : (b != null && !string.IsNullOrEmpty(b.DisplayLabel) ? b.DisplayLabel : "정보");
        string key = NormalizeKey(a != null && !string.IsNullOrEmpty(a.AttributeKey) ? a.AttributeKey
                               : (b != null ? b.AttributeKey : string.Empty));

        // 현재 손님의 실제 이름·유형을 읽어 화자명과 반응 톤을 맞춘다(없으면 "손님"/일반 톤).
        string customerName = _inspection != null && !string.IsNullOrEmpty(_inspection.CurrentCustomerName)
            ? _inspection.CurrentCustomerName : "손님";
        string characterType = _inspection != null ? _inspection.CurrentCharacterType : null;

        string inspectorLine = InterrogationQuestionFor(key, label); // 불일치 → 필드별 취조 질문 자동 표시
        string customerLine = CustomerReactionFor(key, characterType);

        // 기존 일반 대사와 동일한 데이터 구조로 2줄(검사관 → 손님)을 만들어 같은 위치·스타일로 재생.
        // 검사관 라인은 "심사관" 표기, 손님 라인은 실제 이름(예 "박철수")으로.
        DialogueCaseData mismatchCase = new DialogueCaseData
        {
            caseType = "대조 불일치",
            gameResult = "-",
            rejectCount = 0,
            lines = new[]
            {
                new DialogueLineData { order = 0, speaker = "심사관", text = inspectorLine },
                new DialogueLineData { order = 1, speaker = customerName, text = customerLine },
            },
        };
        _mismatchDialogue.Play(mismatchCase, null);
    }

    /// <summary>불일치한 항목(key)에 대한 검사관의 취조 질문. 항목별로 추궁 질문이 다르다.
    /// (지금은 항목별 기본 질문 — 추후 손님/일차별 데이터로 교체 가능)</summary>
    private static string InterrogationQuestionFor(string key, string label)
    {
        switch (key)
        {
            case "name":         return "여권의 이름이 다른 서류와 다릅니다. 어느 쪽이 본인 정보입니까?";
            case "nationality":  return "국적 정보가 서류마다 다른데, 설명해 주시겠습니까?";
            case "passport_no":  return "여권번호가 규정 형식과 맞지 않습니다. 어떻게 된 거죠?";
            case "visa_no":      return "비자번호가 규정과 맞지 않습니다. 설명해 주시겠습니까?";
            case "birth_date":   return "생년월일이 서류마다 다릅니다. 정확한 생년월일이 어떻게 됩니까?";
            case "expiry_date":  return "여권 유효기간이 지난 것 같은데, 확인하셨습니까?";
            case "issue_date":   return "여권 발급일이 논리적으로 맞지 않습니다. 설명해 주시겠습니까?";
            case "gender":       return "성별 정보가 서류마다 다른데, 어느 게 맞습니까?";
            case "face":
            case "photo":        return "여권 사진과 지금 모습이 달라 보입니다. 본인이 맞습니까?";
            default:             return $"{label}{Josa(label, "이", "가")} 다른 서류와 일치하지 않습니다. 설명해 주시겠습니까?";
        }
    }

    /// <summary>비교 항목 중 하나라도 경보(워치리스트) 단서(UnlocksScan!="")이면 true.
    /// 경보 단서와의 "불일치"는 단순히 "대상 아님"이므로 지적 대사를 내지 않는다.</summary>
    private static bool IsWatchlistInvolved(ICrossCheckSelectable a, ICrossCheckSelectable b)
        => (a != null && !string.IsNullOrEmpty(a.UnlocksScan))
        || (b != null && !string.IsNullOrEmpty(b.UnlocksScan));

    /// <summary>경보 단서가 손님 소스와 "일치"하면 위험 경고 대사를 재생한다(해당 스캔 안내).
    /// DetectScanUnlock 이 스캔을 여는 것과 별개로, 심사관 경고 한 줄을 띄운다.</summary>
    private void ShowWatchlistAlertIfAny(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        if (TryWatchlistAlert(a, b)) return;
        TryWatchlistAlert(b, a);
    }

    // trigger 가 경보 단서이고 other 가 손님 소스(서류/캐릭터)면 경고 대사 재생. 재생했으면 true.
    private bool TryWatchlistAlert(ICrossCheckSelectable trigger, ICrossCheckSelectable other)
    {
        if (_mismatchDialogue == null || trigger == null || other == null) return false;
        if (string.IsNullOrEmpty(trigger.UnlocksScan)) return false;
        if (!IsCustomerSource(other.SourceType)) return false;

        string warn;
        switch (trigger.UnlocksScan)
        {
            case "xray":
                warn = "위험물·밀수 경보 대상과 일치합니다. X-ray 정밀 검사를 시행하세요.";
                break;
            case "fingerprint":
                warn = "수배자 경보 대상과 일치합니다. 지문 대조로 신원을 확인하세요.";
                break;
            default:
                warn = "경보 대상과 일치합니다. 추가 검사가 필요합니다.";
                break;
        }

        DialogueCaseData alertCase = new DialogueCaseData
        {
            caseType = "경보 일치",
            gameResult = "-",
            rejectCount = 0,
            lines = new[] { new DialogueLineData { order = 0, speaker = "심사관", text = warn } },
        };
        _mismatchDialogue.Play(alertCase, null);
        return true;
    }

    /// <summary>
    /// 불일치 항목 종류(key) + 손님 유형(characterType)별 반응 대사.
    /// 진상 고객은 사과 대신 거만/따지는 톤, 성형 의심 고객은 사진/머리스타일 톤, 그 외는 고분고분한 사과 톤.
    /// 표시·대사 텍스트 전용 — 판정/점수 무영향.
    /// </summary>
    private static string CustomerReactionFor(string key, string characterType)
    {
        // 진상 고객: 사과하지 않고 따지거나 거만하게 군다.
        if (characterType == CharacterTypes.Annoying)
        {
            switch (key)
            {
                case "name": case "name_en": case "name_kr":
                case "birth_date": case "gender": case "nationality":
                    return "뭐? 내 서류가 어때서. 똑바로 안 봤구먼.";
                case "photo": case "photo_ref": case "face":
                    return "내 얼굴이 뭐 어때서! 시비 거는 거야?";
                case "expiry_date": case "valid_until": case "issue_date": case "test_date":
                    return "그게 뭐 대수라고. 빨리 통과시켜.";
                case "passport_no":
                    return "번호 좀 틀릴 수도 있지, 까다롭게 구네.";
                default:
                    return "별걸 다 트집이네. 그냥 보내 줘.";
            }
        }

        // 성형 의심 고객: 사진/얼굴 항목은 "오래된 사진/머리스타일" 톤 유지.
        if (characterType == CharacterTypes.PlasticSuspect)
        {
            switch (key)
            {
                case "photo": case "photo_ref": case "face":
                    return "오래된 사진이라서요. 머리 스타일이 많이 바뀌었어요.";
                case "name": case "name_en": case "name_kr":
                case "birth_date": case "gender": case "nationality":
                    return "어… 예전 정보라 그래요. 정말이에요.";
                case "expiry_date": case "valid_until": case "issue_date": case "test_date":
                    return "날짜는… 제가 착각했나 봐요. 죄송해요.";
                default:
                    return "그… 사진이 좀 달라 보일 수 있어요.";
            }
        }

        // 일반/외국인/그 외: 고분고분한 사과 톤(기존 유지).
        switch (key)
        {
            case "name": case "name_en": case "name_kr":
            case "birth_date": case "gender": case "nationality":
                return "아, 죄송해요. 서류를 잘못 가져왔나 봐요.";
            case "photo": case "photo_ref": case "face":
                return "오래된 사진이라서요. 머리 스타일이 많이 바뀌었어요.";
            case "expiry_date": case "valid_until": case "issue_date": case "test_date":
                return "앗, 제가 날짜를 잘못 봤네요. 죄송합니다.";
            case "passport_no":
                return "사실… 제 여권이 아니에요. 사정이 있었어요.";
            default:
                return "어… 그건… 죄송합니다.";
        }
    }

    /// <summary>한글 받침 유무로 조사를 고른다(받침 있으면 withBatchim).</summary>
    private static string Josa(string word, string withBatchim, string withoutBatchim)
    {
        if (string.IsNullOrEmpty(word)) return withoutBatchim;
        char last = word[word.Length - 1];
        if (last < 0xAC00 || last > 0xD7A3) return withoutBatchim; // 한글 음절 아님
        return ((last - 0xAC00) % 28) != 0 ? withBatchim : withoutBatchim;
    }

    // ── 오늘 날짜 인식 대조 ───────────────────────────────────────
    /// <summary>
    /// 두 선택지 중 하나가 "오늘"(SourceType=="오늘" 또는 attr=="today")이고 다른 하나의 attr이
    /// 날짜 필드면, 문자열 일치 대신 날짜 유효성을 판정한다(멘트는 일반 대조와 동일하게 일치/불일치).
    /// - 만료류(expiry_date/valid_until): 오늘이 만료일 이내(오늘 ≤ 날짜) → 일치(Match), 지났으면 → 불일치(Mismatch).
    /// - 발급류(issue_date/test_date): 발급일이 오늘 이전/같음(날짜 ≤ 오늘) → 일치, 미래면 → 불일치.
    /// - 생년월일(birth_date)은 비교 대상 아님(항상 과거 → 무의미) → 관련 없음.
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

        bool valid;
        if (dateKey == "birth_date")
        {
            // 생년월일 ↔ 오늘: 나이 타당성(0~120세). 미래 출생/126세 등 불가능값 → 불일치.
            int age = todayDate.Year - fieldDate.Year;
            if (todayDate.Month < fieldDate.Month
                || (todayDate.Month == fieldDate.Month && todayDate.Day < fieldDate.Day)) age--;
            valid = age >= 0 && age <= 120;
        }
        else
        {
            bool isExpiry = dateKey == "expiry_date" || dateKey == "valid_until";
            valid = isExpiry
                ? todayDate.Date <= fieldDate.Date   // 오늘이 만료일 이내면 유효 → 일치
                : fieldDate.Date <= todayDate.Date;  // 발급일이 오늘 이전이면 정상 → 일치
        }
        result = valid ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
        return true;
    }

    private static bool IsToday(ICrossCheckSelectable s) =>
        s != null && (s.SourceType == "오늘" || NormalizeKey(s.AttributeKey) == "today");

    private static bool IsDateKey(string key) => key switch
    {
        // birth_date 포함: 오늘과 비교해 나이 타당성(0~120세)을 검사한다(1900년생=126세 같은 불가능값 적발).
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
    /// 성형수술 고객(마스크 착용 → 얼굴 확인 불가)에 한해, 얼굴↔여권 사진을 대조하면 지문 스캔 잠금 해제.
    /// 다른 종류 손님은 사진을 대조해도 지문판독기가 열리지 않는다(설계: 성형수술 전용).
    /// </summary>
    private void DetectFaceMismatchUnlock(ICrossCheckSelectable a, ICrossCheckSelectable b, CrossCheckResult result)
    {
        if (result == CrossCheckResult.Unrelated) return;   // 실제 얼굴↔사진 대조가 성립한 경우만
        if (!IsPhotoKey(a) && !IsPhotoKey(b)) return;        // 사진/얼굴 대조일 때만
        if (!IsPlasticSurgeryCustomer()) return;            // 성형수술 고객만
        OnScanUnlocked?.Invoke("fingerprint");
    }

    /// <summary>현재 손님이 성형수술 관련 종류인가(성형 의심 고객 / 범죄자(성형수술)).</summary>
    private bool IsPlasticSurgeryCustomer()
    {
        string ct = _inspection != null ? _inspection.CurrentCharacterType : null;
        return ct == CharacterTypes.PlasticSuspect || ct == CharacterTypes.CriminalPlastic;
    }

    private static bool IsPhotoKey(ICrossCheckSelectable s)
    {
        if (s == null) return false;
        string k = NormalizeKey(s.AttributeKey);
        return k == "photo" || k == "photo_ref" || k == "face";
    }

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

        // 이름 항목은 어순 무시(여권은 보통 성-이름 순, 손님 표기는 이름-성 순).
        // 예: "JAMES MILLER" == "MILLER JAMES" == "MILLER<<JAMES".
        bool isName = ka == "name" || ka == "name_en" || ka == "name_kr";
        string na = isName ? NormalizeName(a.Value) : Normalize(a.Value);
        string nb = isName ? NormalizeName(b.Value) : Normalize(b.Value);

        return na == nb ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
    }

    /// <summary>
    /// 이름 정규화: 토큰(공백/MRZ '&lt;'/구분기호 기준 분리)을 각각 정규화 후 정렬해 결합 → 어순 무시.
    /// </summary>
    private static string NormalizeName(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string[] tokens = value.Split(new[] { ' ', '<', '\t', ',', '.', '-', '/' },
            System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++) tokens[i] = Normalize(tokens[i]);
        System.Array.Sort(tokens, System.StringComparer.Ordinal);
        return string.Concat(tokens);
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
