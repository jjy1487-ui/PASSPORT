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

    // 검사후 흐름 흡수용 패널 참조(없으면 1회 FindObjectOfType(true)로 캐시). UI를 직접 참조하지 않는 규약에 따라
    // 컨트롤러를 직접 알지 않고, '검사후' 손님의 금지물품 Match 때만 공개 Close API로 창을 닫는 데 쓴다.
    private XrayInspectionPanel _xrayPanelCache;
    private bool _xrayPanelSearched;
    private RulebookPopup _rulebookPopupCache;
    private bool _rulebookPopupSearched;

    /// <summary>
    /// 잠금 해제 트리거 발생 통지. 인자 = 잠금 해제할 스캔 종류("xray"|"fingerprint").
    /// 뉴스/규정 단서(UnlocksScan!="")가 손님 소스(서류/캐릭터)와 Match/Related 되면 발행한다.
    /// 판정/점수에는 영향 없음 — UI 게이팅 전용.
    /// </summary>
    public event System.Action<string> OnScanUnlocked;

    /// <summary>
    /// 외부(InspectionController 등)에서 스캔 잠금 해제를 명시적으로 요청한다(예: day11 입장 시 X-ray 자동 검사).
    /// event 는 선언 클래스 밖에서 Invoke 할 수 없으므로, 같은 발행 경로를 공개 메서드로 노출한다.
    /// 대조(워치리스트/여권번호 불일치) 경로와 동일하게 <see cref="OnScanUnlocked"/> 구독자(XrayInspectionPanel 등)가 받는다.
    /// 판정/점수에는 영향 없음 — 표시·게이팅 전용.
    /// </summary>
    public void RequestScanUnlock(string scanType)
    {
        if (string.IsNullOrEmpty(scanType)) return;
        OnScanUnlocked?.Invoke(scanType);
    }

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

    // 시나리오 손님(사토 등)처럼 대조 판정이 의미 없는 손님 동안 대조를 잠근다(스페이스/클릭/비교 무효).
    // 잠겨 있으면 SetActive 가 항상 off 로 강제되므로 토글·키 입력도 켜지지 않는다.
    private bool _locked;

    /// <summary>대조 도구 잠금(시나리오 손님 등). 잠그면 즉시 끄고, 이후 켜기 요청도 무시한다.</summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (locked) SetActive(false);
    }

    /// <summary>대조 도구 on/off(스페이스바/버튼에서 호출). off 시 선택 초기화. 잠금 중이면 항상 off.</summary>
    public void SetActive(bool active)
    {
        if (_locked) active = false; // 잠금 중에는 어떤 경로로도 켜지지 않음
        _active = active;
        if (_modeIndicator != null) _modeIndicator.SetActive(active);
        if (active)
        {
            // 새 대조 진입 → 이전 결과/대사 정리
            if (_mismatchDialogue != null) _mismatchDialogue.Hide();
            if (_connectorView != null) _connectorView.Hide();
            // 진입 시점의 모든 선택 가능 항목을 다시 구독한다(현재 선택은 보존). 규정집/서류 카드가
            // 공급자 변경 알림 없이 늦게 활성화되면 첫 클릭이 구독 누락(listeners=False)으로 불발돼
            // '첫 대조만 결과가 안 뜨는' 증상이 생긴다 — 진입마다 Rebind 로 그 틈을 없앤다.
            Rebind();
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
        // 진행 중인 대조 선택은 보존한다. 제공자 구성 변경(규정집/뉴스 팝업 개폐 등)만으로는 지우지 않는다.
        // 예전엔 여기서 무조건 ClearSelection() 을 호출해서, 규정을 고르고 규정집을 '닫는 순간'
        // (RulebookPopup.Close 가 OnSelectablesChanged 를 발행 → Rebind) 첫 선택이 날아가
        // 규정 ↔ 서류 대조를 영영 못 맞췄다. 손님 교체로 서류 카드가 파괴된 항목만 버린다(아래).
        PruneDeadSelection();
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

        // 재바인딩 후에도 살아남은 선택의 하이라이트를 복원(규정집을 닫아도 선택 표시 유지).
        if (_first != null) _first.SetSelected(true);
        if (_second != null) _second.SetSelected(true);
    }

    /// <summary>파괴된(손님 교체로 Destroy 된 서류 카드 등) 선택만 비우고, 단지 비활성화된
    /// (규정집/뉴스 팝업 닫힘 등) 살아있는 선택은 보존한다. _first 가 비고 _second 만 남으면 앞으로 당긴다.</summary>
    private void PruneDeadSelection()
    {
        if (!IsSelectableAlive(_first)) _first = null;
        if (!IsSelectableAlive(_second)) _second = null;
        if (_first == null && _second != null) { _first = _second; _second = null; }
        if (_first == null && _second == null && _connectorView != null) _connectorView.Hide();
    }

    /// <summary>선택 항목이 아직 유효한가. Unity 오브젝트(MonoBehaviour)면 파괴 여부까지 본다
    /// (파괴된 컴포넌트는 == null 이 true). 단순 비활성(SetActive(false))은 '살아있음'으로 본다.</summary>
    private static bool IsSelectableAlive(ICrossCheckSelectable s)
    {
        if (s == null) return false;
        if (s is UnityEngine.Object obj) return obj != null; // Unity 파괴 감지 == 연산자
        return true;
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
        // 플레이어가 입장 대사를 기다리지 않고 먼저 대조를 시작했다면, 대기 중이던 입장 대사를 취소한다
        // (추궁 대사 뒤에 인삿말이 뒤늦게 재출력되는 문제 방지). 입장 대사가 이미 떴으면 무영향.
        _inspection?.NotifyInspectionStarted();

        // 보조 평가(판정/점수 무영향) 우선순위:
        //  1) 규정(여권번호 규정) ↔ 여권번호: 앞 2자리=발급국 코드면 일치(정상), 다르면 불일치(위조 의심).
        //  2) 오늘 날짜 ↔ 날짜 필드: 문자열 일치 대신 날짜 유효성(만료됨/유효 등). 성립 시 라벨 오버라이드.
        //  3) 그 외: 일반 4-상태 평가(속성 키 관련성 + 값 정규화 비교).
        string overrideText = null;
        CrossCheckResult result;
        if (TryEvaluatePassportRule(a, b, out CrossCheckResult passportResult))
            result = passportResult;
        else if (TryEvaluateNationalityPassportRule(a, b, out CrossCheckResult natPassResult))
            result = natPassResult; // 국적/발급국 ↔ 여권번호: 앞2자리 국가코드 다르면 불일치(위조)
        else if (TryEvaluateIdentityGenderRule(a, b, out CrossCheckResult genderResult))
            result = genderResult;
        else if (TryEvaluateFaceGenderRule(a, b, out CrossCheckResult faceGenderResult))
            result = faceGenderResult; // 여권사진(얼굴) ↔ 성별: 사진 성별 ≠ 성별 필드면 불일치(성별 위조)
        else if (TryEvaluatePcrResultRule(a, b, out CrossCheckResult pcrResult))
            result = pcrResult;
        else if (TryEvaluateLabNameRule(a, b, out CrossCheckResult labResult))
            result = labResult;
        else if (TryEvaluateBannedCompanyRule(a, b, out CrossCheckResult bannedCompanyResult))
            result = bannedCompanyResult; // 입국금지회사 규정 ↔ 취업증빙 고용회사: 금지 명단과 일치=적발(일치), 아니면 관련있음
        else if (TryEvaluateContrabandRule(a, b, out CrossCheckResult contrabandResult))
            result = contrabandResult; // 금지물품 규정 ↔ X-ray 적발물: 적발물 있으면 일치(입국 불허 사유)
        else if (TryEvaluateScanRefusalRule(a, b, out CrossCheckResult scanRefusalResult))
            result = scanRefusalResult; // 전신검사 거부 음성 ↔ 거부=입국불가 규정: 위반 확정(일치) → 거절(윤정호)
        else if (TryEvaluateDate(a, b, out CrossCheckResult dateResult, out overrideText))
            result = dateResult;
        else if (TryEvaluateDateLogic(a, b, out CrossCheckResult dateLogicResult))
            result = dateLogicResult; // 발급일↔만료일: 만료가 발급보다 빠르면 논리적 위조(불일치)
        else if (TryEvaluateRuleScope(a, b, out CrossCheckResult scopeResult))
            result = scopeResult; // 규정 글이 '다루는' 항목 → 관련없음 대신 관련있음(내용상 관련 표시)
        else
            result = Evaluate(a, b);

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

        // 보조검사(X-ray/지문)는 "대사 먼저, 그 다음 검사 패널" 순서로 연다. 대조하자마자 패널이 튀어나와
        // 대사보다 먼저 뜨면 어색하므로, 불일치 지적 대사가 끝난 뒤(onComplete) 연다.
        bool xrayPending = ShouldUnlockXrayOnPassportMismatch(a, b, result);            // 여권번호 불일치(비자↔여권) → X-ray
        bool fingerprintPending = ShouldUnlockFingerprintOnFaceMismatch(a, b, result);  // 얼굴↔여권사진 불일치(성형 고객) → 지문
        System.Action openScans = (xrayPending || fingerprintPending) ? (System.Action)(() =>
        {
            if (xrayPending) OnScanUnlocked?.Invoke("xray");
            if (fingerprintPending) OnScanUnlocked?.Invoke("fingerprint");
        }) : null;

        // 결과별 보조 대사:
        // - 불일치: 서류 정합 항목이면 "안 맞네요" 지적. 단, 경보(워치리스트) 단서와의 불일치는
        //   "대상 아님"을 뜻하므로 조용히 넘어간다(기계적 오발 대사 방지).
        // - 일치: 경보 단서가 손님과 일치하면 위험 경고 대사(해당 스캔 안내).
        if (result == CrossCheckResult.Mismatch)
        {
            if (!IsWatchlistInvolved(a, b)) ShowMismatchComment(a, b, openScans); // 대사 재생 끝나면 보조검사(X-ray/지문) 열기
            else openScans?.Invoke();                                            // 대사 생략(워치리스트)이면 즉시
        }
        else if (result == CrossCheckResult.Match)
        {
            // 전신검사 거부 음성 ↔ 규정 일치(윤정호): '일치' 잠깐 보여준 뒤 규정집 닫고 거절('정말 거절?' 팝업)로.
            if (IsScanRefusalPair(a, b) && _inspection != null && _inspection.IsScanRefusalRejectActive)
                StartCoroutine(CloseWindowsAndScanRefusalReject());
            // 입국 금지 회사 명단과 일치 → 추궁 대사("재직 회사가 입국 금지 명단에…"). crossCheckLine 재생을 그대로 재사용.
            else if (IsBannedCompanyMatch(a, b)) ShowMismatchComment(a, b);
            // 금지물품 규정 ↔ X-ray 적발물 일치 → 적발 고지 대사. 아니면 경보(워치리스트) 일치 대사.
            else if (!TryShowContrabandComment(a, b)) ShowWatchlistAlertIfAny(a, b);
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
    private void ShowMismatchComment(ICrossCheckSelectable a, ICrossCheckSelectable b, System.Action onComplete = null)
    {
        if (_mismatchDialogue == null) { onComplete?.Invoke(); return; } // 대사 못 띄우면 후속(예: X-ray)만 즉시
        // 불일치 항목의 "실질 속성 키"를 고른다. 한쪽이 보조 소스('today'/규정)면 다른 쪽(서류 필드)의
        // 키를 쓴다 — 예: 만료일↔오늘 대조는 'today' 가 아니라 'expiry_date' 로 보고 대사를 고른다.
        string key = ResolveMismatchKey(a, b);
        string label = LabelForKey(a, b, key);

        // 현재 손님의 실제 이름·유형을 읽어 화자명과 반응 톤을 맞춘다(없으면 "손님"/일반 톤).
        string customerName = _inspection != null && !string.IsNullOrEmpty(_inspection.CurrentCustomerName)
            ? _inspection.CurrentCustomerName : "손님";
        string characterType = _inspection != null ? _inspection.CurrentCharacterType : null;

        // 1순위: 이 손님 + 이 속성(attr)에 맞는 대사_스크립트 대조 대사가 데이터에 있으면 그걸 쓴다.
        // 2순위(폴백): 없으면 기존 항목별 일반 취조 문구/일반 반응.
        CrossCheckLine scripted = FindCrossCheckLine(key);
        string inspectorLine = scripted != null && !string.IsNullOrEmpty(scripted.inspector)
            ? scripted.inspector
            : InterrogationQuestionFor(key, label); // 불일치 → 필드별 취조 질문 자동 표시(폴백)
        string customerLine = scripted != null && !string.IsNullOrEmpty(scripted.customer)
            ? scripted.customer
            : CustomerReactionFor(key, characterType); // 일반 반응(폴백)

        // 기존 일반 대사와 동일한 데이터 구조로 검사관 → 손님(→ 검사관 마무리) 순으로 만들어 같은 위치·스타일로 재생.
        // 검사관 라인은 "심사관" 표기, 손님 라인은 실제 이름(예 "박철수")으로.
        // 대사_스크립트에 검사관 마무리 줄(inspectorClose)이 있으면 3줄(지적→반응→마무리)로 재생한다(생략 방지).
        List<DialogueLineData> lines = new List<DialogueLineData>
        {
            new DialogueLineData { order = 0, speaker = "심사관", text = inspectorLine },
            new DialogueLineData { order = 1, speaker = customerName, text = customerLine },
        };
        if (scripted != null && !string.IsNullOrEmpty(scripted.inspectorClose))
            lines.Add(new DialogueLineData { order = 2, speaker = "심사관", text = scripted.inspectorClose });

        DialogueCaseData mismatchCase = new DialogueCaseData
        {
            caseType = "대조 불일치",
            gameResult = "-",
            rejectCount = 0,
            lines = lines.ToArray(),
        };
        _mismatchDialogue.Play(mismatchCase, onComplete); // 대사 재생이 끝나면 후속(예: X-ray 열기) 실행
    }

    /// <summary>
    /// 지문 자동 적발 연출용: 현재 손님의 "name" 대조대사(수배 적발: "지문 조회에 수배자로 걸렸습니다…")를
    /// 플레이어의 Space 대조 없이 곧바로 재생한다. 윤서린처럼 지문에 수배 기록이 있는 손님 전용(ScanResultPanel 자동닫기 후 호출).
    /// </summary>
    public void PlayFingerprintReveal(System.Action onComplete = null)
    {
        CrossCheckLine scripted = FindCrossCheckLine("name");
        if (scripted == null || _mismatchDialogue == null) { onComplete?.Invoke(); return; }

        if (_inspection != null) _inspection.MarkFingerprintRevealed(); // 적발됨 → 거부 시 체념/연행 대사로 분기

        string customerName = _inspection != null && !string.IsNullOrEmpty(_inspection.CurrentCustomerName)
            ? _inspection.CurrentCustomerName : "손님";
        List<DialogueLineData> lines = new List<DialogueLineData>();
        if (!string.IsNullOrEmpty(scripted.inspector))
            lines.Add(new DialogueLineData { order = 0, speaker = "심사관", text = scripted.inspector });
        if (!string.IsNullOrEmpty(scripted.customer))
            lines.Add(new DialogueLineData { order = 1, speaker = customerName, text = scripted.customer });
        if (!string.IsNullOrEmpty(scripted.inspectorClose))
            lines.Add(new DialogueLineData { order = 2, speaker = "심사관", text = scripted.inspectorClose });
        if (lines.Count == 0) { onComplete?.Invoke(); return; }

        _mismatchDialogue.Play(new DialogueCaseData
        {
            caseType = "지문 적발",
            gameResult = "-",
            rejectCount = 0,
            lines = lines.ToArray(),
        }, onComplete);
    }

    /// <summary>
    /// 불일치 두 항목에서 "실질 속성 키"를 고른다. 한쪽이 보조 소스('today' 또는 규정)면 다른 쪽(서류 필드)
    /// 키를 우선한다. 그래야 만료일↔오늘 대조가 'today' 가 아니라 'expiry_date' 로 대사를 선택한다.
    /// 둘 다 실질 키면 a 우선(기존 동작 보존). 둘 다 비면 빈 문자열.
    /// </summary>
    private static string ResolveMismatchKey(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        string ka = a != null ? NormalizeKey(a.AttributeKey) : string.Empty;
        string kb = b != null ? NormalizeKey(b.AttributeKey) : string.Empty;

        // 국적(또는 발급국)↔여권번호 불일치는 '번호 앞자리'가 발급국과 안 맞는 문제이므로 passport_no 대사를 쓴다
        // ("국적 정보가 서류마다 다른데" 가 아니라 "여권번호 앞자리가… 발급국이랑 안 맞는데요").
        if ((ka == "passport_no" && (kb == "nationality" || kb == "issue_country"))
            || (kb == "passport_no" && (ka == "nationality" || ka == "issue_country")))
            return "passport_no";

        // 여권사진(얼굴)↔성별 불일치는 성별 위조이므로 성별 대사를 쓴다(사진 대사 아님).
        if ((ka == "gender" && IsPhotoKeyName(kb)) || (kb == "gender" && IsPhotoKeyName(ka)))
            return "gender";

        bool aGeneric = IsGenericKey(ka) || IsRuleSource(a);
        bool bGeneric = IsGenericKey(kb) || IsRuleSource(b);

        // 한쪽만 보조 소스면 실질 쪽 키를 쓴다.
        if (aGeneric && !bGeneric && kb.Length > 0) return kb;
        if (bGeneric && !aGeneric && ka.Length > 0) return ka;

        // 그 외: a 우선(비었으면 b).
        if (ka.Length > 0) return ka;
        return kb;
    }

    /// <summary>대사·라벨 선택에서 제외할 보조(비실질) 속성 키. 'today' 는 날짜 대조의 기준일.</summary>
    private static bool IsGenericKey(string key) => key == "today";

    /// <summary>규정집 항목(SourceType="규정")인가. 규정은 "이 속성에 관한 규정" 관련성 표시용이라 실질 키로 보지 않는다.</summary>
    private static bool IsRuleSource(ICrossCheckSelectable s) => s != null && s.SourceType == "규정";

    /// <summary>선택된 두 항목 중 <paramref name="key"/>(실질 키)에 해당하는 항목의 표시 라벨을 고른다. 없으면 a/b/"정보".</summary>
    private static string LabelForKey(ICrossCheckSelectable a, ICrossCheckSelectable b, string key)
    {
        if (a != null && NormalizeKey(a.AttributeKey) == key && !string.IsNullOrEmpty(a.DisplayLabel)) return a.DisplayLabel;
        if (b != null && NormalizeKey(b.AttributeKey) == key && !string.IsNullOrEmpty(b.DisplayLabel)) return b.DisplayLabel;
        if (a != null && !string.IsNullOrEmpty(a.DisplayLabel)) return a.DisplayLabel;
        if (b != null && !string.IsNullOrEmpty(b.DisplayLabel)) return b.DisplayLabel;
        return "정보";
    }

    /// <summary>
    /// 현재 손님의 교차 대조 전용 대사(<see cref="CrossCheckLine"/>) 중, 불일치 속성 키(<paramref name="key"/>)와
    /// 일치하는 항목을 찾는다. 데이터(대사_스크립트)가 있으면 일반 문구 대신 그 손님 구체 대사를 쓰기 위함.
    /// 없으면 null(→ 호출부가 일반 취조 문구로 폴백). 표시 전용 — 판정/점수 무영향.
    /// 속성 키는 정규화(소문자·트림) 후 비교하므로 데이터의 대소문자/공백 차이를 흡수한다.
    /// </summary>
    private CrossCheckLine FindCrossCheckLine(string key)
    {
        if (_inspection == null || string.IsNullOrEmpty(key)) return null;
        CrossCheckLine[] lines = _inspection.CurrentCrossCheckLines;
        if (lines == null) return null;
        foreach (CrossCheckLine ln in lines)
        {
            if (ln == null) continue;
            if (NormalizeKey(ln.attr) == key) return ln; // key 는 호출부에서 이미 NormalizeKey 됨
        }
        return null;
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
                // 수배자 경보 단서가 손님과 일치하면 지문 검사 화면이 자동으로 열린다
                // (DetectScanUnlock → ScanResultPanel 자동 Open). 안내 대사("지문 대조로 신원을 확인하세요")는
                // 화면과 중복이라 띄우지 않고, 곧장 지문 검사 화면만 뜨게 한다. (handled → true 반환)
                return true;
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
    /// X-ray 적발물이 금지물품 규정과 "일치"할 때 검사관 적발 고지 + 손님 반응 2~3줄 대사를 재생한다(보조 연출, 판정 무영향).
    /// 데이터(crossCheckLines attr="contraband")가 있으면 그 손님 구체 대사를, 없으면 일반 적발 문구로 폴백한다.
    /// 선택 두 항목 중 적발물(attr="contraband")이 없으면(=다른 일치) 아무것도 안 하고 false 를 돌려준다.
    /// 처리했으면 true(→ 호출부가 워치리스트 경고 대사를 생략).
    /// </summary>
    private bool TryShowContrabandComment(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        if (_mismatchDialogue == null) return false;
        string item = ContrabandValue(a, b);
        if (item == null) return false; // 선택지에 적발물 항목이 없음 → 비대상

        // ── '검사후' 케이스 보유 손님(현재 존 카터): 금지물품 Match 를 저작된 검사후 흐름으로 흡수 ──
        // 자동 생성 추궁 라인을 띄우지 않고(억제), 규정집·X-ray 창을 닫는다. X-ray 는 Close()(notify=true)로 닫아
        // InspectionController 의 기존 검사후 지연 핸들러(DeferPostScanUntilPanelClosed)가 발화하게 한다 →
        // 저작된 '검사후' 대사(가슴 쪽 물품 지적 + 뇌물 제안)가 재생되고 끝나면 판정 활성화로 이어진다.
        // 검사후 케이스가 없는 손님(사토·강도식 등)은 이 분기를 건너뛰어 기존 자동 적발 대사 동작을 유지한다(회귀 금지).
        if (_inspection != null && _inspection.CurrentHasPostScanCase)
        {
            // '일치' 결과(연결선)를 잠깐 보여준 뒤 규정집·X-ray 창을 닫고 검사후 흐름으로 넘긴다.
            // 즉시 닫으면 플레이어가 일치 표시를 못 보므로 짧은 딜레이를 둔다(X-ray Close→기존 검사후 트리거).
            StartCoroutine(CloseWindowsForPostScanAfterMatch());
            return true;                      // 자동 적발 대사 억제(여기서 Play 하지 않음)
        }

        // ── 분기 시나리오 손님(사토 등): 위험물 일치를 '진행 트리거'로만 쓴다 ──
        // 자동 적발 멘트("적발…입국 불허")는 억제하고, 발각 반응 대사는 시나리오(introReveal)가 주도한다.
        // 진행은 InspectionController 가 OnCompared(이 메서드 직후 발행)를 구독해 다음 노드로 잇는다.
        if (_inspection != null && _inspection.IsScenarioActive)
            return true;                      // 자동 적발 대사 억제(시나리오가 대사 주도)

        string customerName = _inspection != null && !string.IsNullOrEmpty(_inspection.CurrentCustomerName)
            ? _inspection.CurrentCustomerName : "손님";
        string characterType = _inspection != null ? _inspection.CurrentCharacterType : null;

        // 1순위: 이 손님의 contraband 대조 대사(crossCheckLines)가 있으면 그것. 2순위: 일반 적발 문구.
        CrossCheckLine scripted = FindCrossCheckLine("contraband");
        string shown = string.IsNullOrEmpty(item) ? "금지 물품" : item;
        string inspectorLine = scripted != null && !string.IsNullOrEmpty(scripted.inspector)
            ? scripted.inspector
            : $"X-ray 검사에서 {shown}{Josa(shown, "이", "가")} 적발되었습니다. 규정상 입국 불허 대상입니다.";
        string customerLine = scripted != null && !string.IsNullOrEmpty(scripted.customer)
            ? scripted.customer
            : CustomerReactionFor("contraband", characterType);

        List<DialogueLineData> lines = new List<DialogueLineData>
        {
            new DialogueLineData { order = 0, speaker = "심사관", text = inspectorLine },
            new DialogueLineData { order = 1, speaker = customerName, text = customerLine },
        };
        if (scripted != null && !string.IsNullOrEmpty(scripted.inspectorClose))
            lines.Add(new DialogueLineData { order = 2, speaker = "심사관", text = scripted.inspectorClose });

        DialogueCaseData contrabandCase = new DialogueCaseData
        {
            caseType = "금지물품 적발",
            gameResult = "-",
            rejectCount = 0,
            lines = lines.ToArray(),
        };
        _mismatchDialogue.Play(contrabandCase, null);
        return true;
    }

    /// <summary>
    /// 규정집(RulebookPopup)이 열려 있으면 닫는다. 참조가 없으면 1회 FindObjectOfType(true)로 캐시(기존 컨벤션).
    /// 저작된 '검사후' 대사가 규정집 창에 가리지 않게 하기 위함.
    /// </summary>
    private void CloseRulebookPopupIfOpen()
    {
        if (!_rulebookPopupSearched)
        {
            _rulebookPopupCache = FindObjectOfType<RulebookPopup>(true);
            _rulebookPopupSearched = true;
        }
        if (_rulebookPopupCache != null) _rulebookPopupCache.Close();
    }

    /// <summary>
    /// X-ray 패널을 닫아 기존 검사후(PostScan) 흐름을 트리거한다. 참조가 없으면 1회 FindObjectOfType(true)로 캐시.
    /// 반드시 공개 <see cref="XrayInspectionPanel.Close"/>(내부 notify=true)로 닫아야 InspectionController 의
    /// OnClosed 구독(DeferPostScanUntilPanelClosed)이 발화해 저작 검사후 대사가 재생된다(손님이 직접 닫은 것과 동일 경로).
    /// 패널이 닫혀 있으면 Close 가 OnClosed 를 발행하지 않으므로(가드) 안전 — 이중 트리거 없음.
    /// </summary>
    private void CloseXrayPanelForPostScan()
    {
        if (!_xrayPanelSearched)
        {
            _xrayPanelCache = FindObjectOfType<XrayInspectionPanel>(true);
            _xrayPanelSearched = true;
        }
        if (_xrayPanelCache != null) _xrayPanelCache.Close(); // notify=true → 검사후 트리거
    }

    // 대조 '일치' 표시를 보여주는 시간(초). 이후 창을 닫고 저작 검사후 대사로 넘어간다.
    private const float PostScanMatchRevealSeconds = 3.5f;

    /// <summary>대조 '일치'를 잠깐 보여준 뒤(딜레이) 규정집·X-ray 창을 닫아 저작 검사후 대사로 넘어가게 한다.</summary>
    private System.Collections.IEnumerator CloseWindowsForPostScanAfterMatch()
    {
        yield return new WaitForSeconds(PostScanMatchRevealSeconds);
        CloseRulebookPopupIfOpen();      // 규정집 닫기(저작 검사후 대사를 가리지 않게)
        CloseXrayPanelForPostScan();     // X-ray Close(notify=true) → 기존 검사후 트리거 발화
    }

    /// <summary>
    /// 규정집 "전신 검색 거부 시 입국 불가"(SourceType="규정", attr="xray") ↔ 손님 검사거부 음성(claim attr="xray") 대조.
    /// 현재 손님이 '전신검사 거부 거절' 대상(윤정호)일 때만 '일치'(위반 확정)로 판정 → 거절 트리거.
    /// 그 외 손님은 비대상(false) → 기존 RuleScope 가 '관련있음'(힌트)으로 처리.
    /// </summary>
    private bool TryEvaluateScanRefusalRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = default;
        if (_inspection == null || !_inspection.IsScanRefusalRejectActive) return false;
        if (!IsScanRefusalPair(a, b)) return false;
        result = CrossCheckResult.Match; // 거부 음성 ↔ '거부=입국불가' 규정 = 위반 확정(일치)
        return true;
    }

    /// <summary>한쪽=규정(attr xray), 다른쪽=손님 음성 claim(attr xray) 짝인가.</summary>
    private static bool IsScanRefusalPair(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        bool aRule = IsRuleSource(a), bRule = IsRuleSource(b);
        if (aRule == bRule) return false; // 정확히 한쪽만 규정
        ICrossCheckSelectable rule = aRule ? a : b;
        ICrossCheckSelectable claim = aRule ? b : a;
        return NormalizeKey(rule.AttributeKey) == "xray" && NormalizeKey(claim.AttributeKey) == "xray";
    }

    /// <summary>'일치' 잠깐 보여준 뒤 규정집 닫고 거절 트리거('정말 거절?' 팝업 → 라운드). 도장 거절과 공존.</summary>
    private System.Collections.IEnumerator CloseWindowsAndScanRefusalReject()
    {
        yield return new WaitForSeconds(PostScanMatchRevealSeconds);
        CloseRulebookPopupIfOpen();
        if (_inspection != null) _inspection.TriggerScanRefusalReject();
    }

    /// <summary>선택 두 항목 중 X-ray 적발물(attr="contraband") 값을 고른다. 적발물 항목이 없으면 null(=비대상).</summary>
    private static string ContrabandValue(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        if (a != null && NormalizeKey(a.AttributeKey) == "contraband") return a.Value ?? string.Empty;
        if (b != null && NormalizeKey(b.AttributeKey) == "contraband") return b.Value ?? string.Empty;
        return null;
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
                case "contraband":
                    return "그게 뭐? 증거 있어? 함부로 사람 잡지 마.";
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
            case "contraband":
                return "그… 그건 제 것이 아니에요. 누가 넣었는지 몰라요!";
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

    // ── 규정(여권번호) ↔ 여권번호 대조 ────────────────────────────
    /// <summary>
    /// 규정집의 "여권번호 규정"(SourceType="규정", attr="passport_no") ↔ 손님 여권번호 특수 대조.
    /// 여권번호 앞 2자리가 발급 국가코드(KOR→KO / USA→US / CHN→CN / JPN→JP …)와 같으면 일치(정상),
    /// 다르면 불일치(위조 의심)로 본다. 날짜 대조(TryEvaluateDate)와 같은 보조 표시 — 판정/점수 무영향.
    /// 한쪽이 규정의 여권번호 항목일 때만 성립한다(여권↔비자 번호 일치 같은 일반 값 대조는 가로채지 않음).
    /// 번호·발급국은 클릭한 항목 값이 아니라 현재 손님 여권 본문(InspectionController)에서 읽는다.
    /// </summary>
    private bool TryEvaluatePassportRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;

        ICrossCheckSelectable rule = IsPassportRuleSelectable(a) ? a : (IsPassportRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "passport_no") return false;

        // 규정집의 "여권번호 규정"↔여권번호 = 관련있음(Related). 규정은 가이드일 뿐이라 그 자체로 불일치 판정하지 않는다.
        // 실제 위조(앞 2자리 ≠ 발급국 코드) 판정은 국적↔여권번호 대조(TryEvaluateNationalityPassportRule)가 담당한다.
        result = CrossCheckResult.Related;
        return true;
    }

    private static bool IsPassportRuleSelectable(ICrossCheckSelectable s)
        => s != null && s.SourceType == "규정" && NormalizeKey(s.AttributeKey) == "passport_no";

    // ── 국적/발급국 ↔ 여권번호 대조 ──────────────────────────────
    /// <summary>
    /// 손님 서류의 국적(또는 발급국가) ↔ 여권번호 특수 대조. 여권번호 앞 2자리가 국적 코드의 발급
    /// prefix(KOR→KO/USA→US/CHN→CN/JPN→JP)와 같으면 일치, 다르면 불일치(위조 의심).
    /// 예: 국적 CHN(→CN) vs 여권번호 JP6667788(JP) → 불일치. 규정↔번호(TryEvaluatePassportRule)와 별개.
    /// 손님 서류끼리일 때만 성립. 표시 보조 — 판정/점수 무영향.
    /// </summary>
    private bool TryEvaluateNationalityPassportRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;

        ICrossCheckSelectable num = NormalizeKey(a.AttributeKey) == "passport_no" ? a
                                  : (NormalizeKey(b.AttributeKey) == "passport_no" ? b : null);
        if (num == null) return false;
        ICrossCheckSelectable nat = ReferenceEquals(num, a) ? b : a;
        if (nat == null) return false;
        string natKey = NormalizeKey(nat.AttributeKey);
        if (natKey != "nationality" && natKey != "issue_country") return false;
        if (!IsCustomerSource(num.SourceType) || !IsCustomerSource(nat.SourceType)) return false; // 규정↔번호는 별도 평가기

        string expect = ExpectedPassportPrefix(CountryCode(nat.Value));
        string number = num.Value;
        if (string.IsNullOrEmpty(expect) || string.IsNullOrEmpty(number)) return false;
        string actual = number.Trim().ToUpperInvariant();
        if (actual.Length >= 2) actual = actual.Substring(0, 2);
        result = actual == expect ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
        return true;
    }

    /// <summary>국적 값에서 국가코드 추출: "중국(CHN)"→CHN, "CHN"→CHN.</summary>
    private static string CountryCode(string nationality)
    {
        if (string.IsNullOrEmpty(nationality)) return null;
        int lp = nationality.LastIndexOf('(');
        int rp = nationality.LastIndexOf(')');
        if (lp >= 0 && rp > lp) return nationality.Substring(lp + 1, rp - lp - 1).Trim();
        return nationality.Trim();
    }

    // ── 신분확인 규정 ↔ 여권 성별 대조 ────────────────────────────
    /// <summary>
    /// 규정집의 "신분 확인" 규정(SourceType="규정", attr="gender") ↔ 손님 여권의 성별 항목 특수 대조.
    /// 여권에 적힌 성별이 손님 본인(데이터상 신원 성별)과 같으면 일치, 다르면 불일치(신분 위조 의심)로 본다.
    /// 여권번호 규정 대조(TryEvaluatePassportRule)와 같은 보조 표시 — 판정/점수 무영향.
    /// 한쪽이 신분확인 규정(attr=gender)일 때만 성립한다(여권 성별 ↔ 다른 서류 성별 같은 일반 값 대조는 가로채지 않음).
    /// 성별 값은 클릭한 항목이 아니라 현재 손님 여권 본문/신원(InspectionController)에서 읽는다.
    /// </summary>
    private bool TryEvaluateIdentityGenderRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;

        ICrossCheckSelectable rule = IsIdentityGenderRuleSelectable(a) ? a : (IsIdentityGenderRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "gender") return false;

        string passportGender = _inspection != null ? _inspection.CurrentPassportGender : null;
        if (string.IsNullOrEmpty(passportGender)) passportGender = other.Value; // 폴백: 선택 항목 값
        string trueGender = _inspection != null ? _inspection.CurrentCustomerGender : null;
        if (string.IsNullOrEmpty(passportGender) || string.IsNullOrEmpty(trueGender)) return false; // 못 읽으면 일반 로직으로

        result = Normalize(passportGender) == Normalize(trueGender) ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
        return true;
    }

    private static bool IsIdentityGenderRuleSelectable(ICrossCheckSelectable s)
        => s != null && s.SourceType == "규정" && NormalizeKey(s.AttributeKey) == "gender";

    // ── 여권사진(얼굴) ↔ 성별 대조 ────────────────────────────────
    /// <summary>
    /// 손님 서류의 여권사진(얼굴) ↔ 성별 특수 대조. 사진 속 인물의 실제 성별(본인 성별)과 성별 필드가
    /// 다르면 불일치(성별 위조). 예: 사진=남성인데 성별 "여" → 불일치. 규정↔성별(TryEvaluateIdentityGenderRule)과 별개.
    /// 본인/여권 성별은 클릭 값이 아니라 현재 손님 데이터(InspectionController)에서 읽는다. 표시 보조 — 판정/점수 무영향.
    /// </summary>
    private bool TryEvaluateFaceGenderRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;

        bool pair = (IsPhotoKey(a) && NormalizeKey(b.AttributeKey) == "gender")
                 || (IsPhotoKey(b) && NormalizeKey(a.AttributeKey) == "gender");
        if (!pair) return false;
        if (!IsCustomerSource(a.SourceType) || !IsCustomerSource(b.SourceType)) return false; // 손님 서류끼리만

        string passportGender = _inspection != null ? _inspection.CurrentPassportGender : null;
        string trueGender = _inspection != null ? _inspection.CurrentCustomerGender : null;
        if (string.IsNullOrEmpty(passportGender) || string.IsNullOrEmpty(trueGender)) return false;

        result = Normalize(passportGender) == Normalize(trueGender) ? CrossCheckResult.Match : CrossCheckResult.Mismatch;
        return true;
    }

    // ── PCR 검사서 규정 ↔ 검사결과(양성) 대조 ──────────────────────
    /// <summary>
    /// 규정집 "PCR 검사서" 규정(SourceType="규정", attr="pcr_result") ↔ PCR 검사결과/진술 항목 특수 대조.
    /// 검사결과가 양성(Positive/양성)이거나 미제출(검역 대상이 PCR 을 안 냄)이면 불일치(거부 대상), 음성/정상이면 일치로 본다.
    /// 성별·여권번호 규정과 같은 보조 표시 — 판정/점수 무영향. 한쪽이 PCR 규정일 때만 성립한다.
    /// (PCR 만료·이름·국적 불일치는 날짜/필드 대조가 이미 처리하므로 여기선 결과값만 본다.)
    /// 미제출 손님(예: 조지호)은 PCR 카드가 없어, 대화 진술 줄의 claim(attr=pcr_result, value="미제출")이 대조 대상이 된다.
    /// </summary>
    private bool TryEvaluatePcrResultRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        ICrossCheckSelectable rule = IsPcrRuleSelectable(a) ? a : (IsPcrRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "pcr_result") return false;
        string res = other.Value;
        if (string.IsNullOrEmpty(res)) return false;
        string r = res.Trim().ToLowerInvariant();
        bool positive = r.Contains("positive") || r.Contains("양성");
        // 미제출(진술/표기): 검역 대상자가 PCR 검사서를 안 냄 → 규정 위반(거부 대상). ToLowerInvariant 는 한글 불변.
        bool unsubmitted = r.Contains("미제출") || r.Contains("none") || r.Contains("unsubmitted") || r.Contains("not submitted");
        result = (positive || unsubmitted) ? CrossCheckResult.Mismatch : CrossCheckResult.Match; // 양성·미제출 = 불일치(거부 대상)
        return true;
    }

    private static bool IsPcrRuleSelectable(ICrossCheckSelectable s)
        => s != null && s.SourceType == "규정" && NormalizeKey(s.AttributeKey) == "pcr_result";

    // ── PCR 인증 검사 기관 규정 ↔ 검사 기관(lab_name) 대조 ──────────
    /// <summary>
    /// 규정집 "PCR 인증 검사 기관" 규정(SourceType="규정", attr="lab_name") ↔ PCR 검사 기관 항목 대조.
    /// 공인 기관 명단(<see cref="ApprovedLabs"/>)에 있으면 일치(유효), 없으면(사설·무허가) 불일치(거부 대상)로 본다.
    /// 여권번호·성별·PCR결과 규정과 같은 보조 표시 — 판정/점수 무영향. 한쪽이 검사기관 규정일 때만 성립한다.
    /// (rule_book "PCR 인증 검사 기관" 규정의 명단과 동기화 — 명단이 바뀌면 ApprovedLabs 도 같이 수정.)
    /// </summary>
    private bool TryEvaluateLabNameRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        ICrossCheckSelectable rule = IsLabRuleSelectable(a) ? a : (IsLabRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "lab_name") return false;
        string lab = other.Value;
        if (string.IsNullOrEmpty(lab)) return false;
        result = IsApprovedLab(lab) ? CrossCheckResult.Match : CrossCheckResult.Mismatch; // 명단에 있으면 일치, 없으면 불일치
        return true;
    }

    private static bool IsLabRuleSelectable(ICrossCheckSelectable s)
        => s != null && s.SourceType == "규정" && NormalizeKey(s.AttributeKey) == "lab_name";

    // ── 입국 금지 회사 규정 ↔ 취업증빙 고용회사(company_name) 대조 ──────
    /// <summary>
    /// 규정집 "입국 금지 회사" 규정(SourceType="규정", coveredAttrs 에 "company_name") ↔ 취업증빙 고용회사 대조.
    /// 고용회사가 금지 명단(<see cref="BannedCompanies"/>)에 있으면 불일치(서류 정상이어도 입국 불허), 없으면 일치.
    /// 이 규정은 attr="" / coveredAttrs="company_name" 라서 coveredAttrs 까지 본다(금지물품 규정과 같은 식별).
    /// (rule_book "입국 금지 회사" 규정의 명단과 동기화 — 명단이 바뀌면 BannedCompanies 도 같이 수정.)
    /// </summary>
    private bool TryEvaluateBannedCompanyRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        ICrossCheckSelectable rule = IsBannedCompanyRuleSelectable(a) ? a : (IsBannedCompanyRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "company_name") return false;
        string company = other.Value;
        if (string.IsNullOrEmpty(company)) return false;
        // 금지 명단과 '일치'하면 적발(=입국 불허) → 일치. 명단에 없으면 위반 아님 → 관련있음(빨강 불일치로 오해 방지).
        // (금지물품/워치리스트와 같은 극성: 금지 목록에 매칭되면 '일치'가 곧 적발 신호다.)
        result = IsBannedCompany(company) ? CrossCheckResult.Match : CrossCheckResult.Related;
        return true;
    }

    /// <summary>이번 대조 쌍이 '입국 금지 회사 규정 ↔ 금지 명단에 있는 고용회사'인가(일치 분기에서 추궁 대사를 띄울지 판단).</summary>
    private bool IsBannedCompanyMatch(ICrossCheckSelectable a, ICrossCheckSelectable b)
    {
        ICrossCheckSelectable rule = IsBannedCompanyRuleSelectable(a) ? a : (IsBannedCompanyRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        return other != null && NormalizeKey(other.AttributeKey) == "company_name" && IsBannedCompany(other.Value);
    }

    /// <summary>입국 금지 회사 규정 항목인가(SourceType="규정" + attr 또는 coveredAttrs 가 "company_name").
    /// "입국 금지 회사" 규정은 attr="" / coveredAttrs="company_name" 라서 coveredAttrs 까지 본다.</summary>
    private static bool IsBannedCompanyRuleSelectable(ICrossCheckSelectable s)
    {
        if (s == null || s.SourceType != "규정") return false;
        if (NormalizeKey(s.AttributeKey) == "company_name") return true;
        string covered = (s as CrossCheckItemView)?.CoveredAttrs;
        if (string.IsNullOrEmpty(covered)) return false;
        foreach (string cov in covered.Split(','))
            if (NormalizeKey(cov.Trim()) == "company_name") return true;
        return false;
    }

    /// <summary>입국 금지 회사 명단(rule_book "입국 금지 회사" 규정과 동기화). 명단이 바뀌면 같이 수정.</summary>
    private static readonly string[] BannedCompanies =
    {
        "신우통상(주)", "한성물류(주)", "대명건설(주)", "정원산업(주)",
    };

    /// <summary>고용회사명이 입국 금지 명단에 있는가(정규화 비교 — 공백·기호 무시).</summary>
    private static bool IsBannedCompany(string company)
    {
        string n = Normalize(company);
        foreach (string banned in BannedCompanies)
            if (Normalize(banned) == n) return true;
        return false;
    }

    // ── 금지물품 규정 ↔ X-ray 적발물 대조 ──────────────────────────
    /// <summary>
    /// 규정집 "금지 물품" 규정(SourceType="규정", attr 또는 coveredAttrs 가 "contraband") ↔ X-ray 적발물(attr="contraband") 대조.
    /// X-ray 에 적발물이 잡혔으면(value 비어있지 않음) 규정과 일치(=입국 불허 사유), 안 잡혔으면 관련있음으로 본다.
    /// 다른 규정 특수 평가기(여권번호/성별/PCR/검사기관)와 같은 보조 표시 — 판정/점수 무영향.
    /// (X-ray 항목은 손님 소스가 아니라 TryEvaluateRuleScope/일반 Evaluate 로는 '관련없음'만 떠서, 전용 평가기로 처리한다.)
    /// </summary>
    private bool TryEvaluateContrabandRule(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        ICrossCheckSelectable rule = IsContrabandRuleSelectable(a) ? a : (IsContrabandRuleSelectable(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        if (other == null || NormalizeKey(other.AttributeKey) != "contraband") return false;
        // 적발물이 실제로 검출됐으면(값 있음) 금지물품 규정과 일치, 없으면 관련성만.
        result = !string.IsNullOrEmpty(other.Value) ? CrossCheckResult.Match : CrossCheckResult.Related;
        return true;
    }

    /// <summary>금지물품 규정 항목인가(SourceType="규정" + attr 또는 coveredAttrs 가 "contraband").
    /// day11 "금지 물품" 규정은 attr="" / coveredAttrs="contraband" 라서 coveredAttrs 까지 본다.</summary>
    private static bool IsContrabandRuleSelectable(ICrossCheckSelectable s)
    {
        if (s == null || s.SourceType != "규정") return false;
        if (NormalizeKey(s.AttributeKey) == "contraband") return true;
        string covered = (s as CrossCheckItemView)?.CoveredAttrs;
        if (string.IsNullOrEmpty(covered)) return false;
        foreach (string cov in covered.Split(','))
            if (NormalizeKey(cov.Trim()) == "contraband") return true;
        return false;
    }

    // ── 규정이 '다루는' 항목 ↔ 서류 필드 = 관련있음 ──────────────────
    /// <summary>
    /// 규정(SourceType="규정")의 coveredAttrs(글이 다루는 항목 키 목록)에 상대 서류 필드의 attr이 들면
    /// 관련없음(회색) 대신 관련있음(파랑)을 돌려준다. "규정 글엔 국적이 적혀 있는데 대보니 관련없음"이라는
    /// 혼란을 없애기 위함 — '이 규정이 다루는 항목 맞다'는 힌트만 줄 뿐, 일치/불일치(정답)는 알려주지 않는다.
    /// 핵심 판정 항목(passport_no/gender/pcr_result/lab_name)은 위 특수 평가기가 먼저 일치/불일치로 처리하므로
    /// 여기까지 오지 않는다. 표시 보조 — 판정/점수 무영향.
    /// </summary>
    private bool TryEvaluateRuleScope(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        ICrossCheckSelectable rule = IsRuleSource(a) ? a : (IsRuleSource(b) ? b : null);
        if (rule == null) return false;
        ICrossCheckSelectable other = ReferenceEquals(rule, a) ? b : a;
        // 규정 ↔ 손님 서류/캐릭터, 또는 규정 ↔ 음성기록(대화 진술)일 때 관련성 매칭 허용
        //  (검사 거부 같은 '행동 진술'도 규정 범위에 들어가면 관련있음으로 표시 — 값 비교 대상 없는 행동 위반용).
        if (other == null || !(IsCustomerSource(other.SourceType) || other.SourceType == "대화")) return false;
        string covered = (rule as CrossCheckItemView)?.CoveredAttrs;
        if (string.IsNullOrEmpty(covered)) return false;
        string key = NormalizeKey(other.AttributeKey);
        if (string.IsNullOrEmpty(key)) return false;
        foreach (string cov in covered.Split(','))
        {
            if (NormalizeKey(cov.Trim()) == key) { result = CrossCheckResult.Related; return true; }
        }
        return false;
    }

    /// <summary>PCR 검사서를 발급할 수 있는 공인 검사 기관 명단(rule_book "PCR 인증 검사 기관" 규정과 동기화).</summary>
    private static readonly string[] ApprovedLabs =
    {
        "국립검역소", "인천공항검역소", "질병관리청진단검사센터", "부산국제공항검역소", "삼성서울병원진단검사의학과",
    };

    /// <summary>검사 기관명이 공인 명단에 있는가(정규화 비교 — 공백·기호 무시).</summary>
    private static bool IsApprovedLab(string lab)
    {
        string n = Normalize(lab);
        foreach (string approved in ApprovedLabs)
            if (Normalize(approved) == n) return true;
        return false;
    }

    /// <summary>발급 국가코드(KOR 등) → 여권번호 앞 2자리 규정 코드. day1 ruleId3 매핑(대한민국 KO / 미국 US / 중국 CN / 일본 JP). 미등록 국가는 국가코드 앞 2자리.</summary>
    private static string ExpectedPassportPrefix(string country)
    {
        if (string.IsNullOrEmpty(country)) return string.Empty;
        string c = country.Trim().ToUpperInvariant();
        switch (c)
        {
            case "KOR": return "KO";
            case "USA": return "US";
            case "CHN": return "CN";
            case "JPN": return "JP";
            default: return c.Length >= 2 ? c.Substring(0, 2) : c;
        }
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

    // ── 발급일 ↔ 만료일 논리 대조 ────────────────────────────────
    /// <summary>
    /// 같은 서류의 발급일 ↔ 만료일 대조. 만료일이 발급일보다 빠르면(만료 &lt; 발급) 논리적 위조 → 불일치.
    /// 정상(만료 ≥ 발급)이면 일치. 손님 서류끼리일 때만(오늘↔날짜는 TryEvaluateDate 담당).
    /// 예: 비자 발급 2026-03-03 / 만료 2025-08-03 → 불일치. 표시 보조 — 판정/점수 무영향.
    /// </summary>
    private bool TryEvaluateDateLogic(ICrossCheckSelectable a, ICrossCheckSelectable b, out CrossCheckResult result)
    {
        result = CrossCheckResult.Unrelated;
        string ka = NormalizeKey(a.AttributeKey), kb = NormalizeKey(b.AttributeKey);
        bool aIssue = ka == "issue_date" || ka == "test_date";
        bool aExpiry = ka == "expiry_date" || ka == "valid_until";
        bool bIssue = kb == "issue_date" || kb == "test_date";
        bool bExpiry = kb == "expiry_date" || kb == "valid_until";

        string issueVal, expiryVal;
        if (aIssue && bExpiry) { issueVal = a.Value; expiryVal = b.Value; }
        else if (bIssue && aExpiry) { issueVal = b.Value; expiryVal = a.Value; }
        else return false;
        if (!IsCustomerSource(a.SourceType) || !IsCustomerSource(b.SourceType)) return false;
        if (!TryParseDate(issueVal, out System.DateTime issue) || !TryParseDate(expiryVal, out System.DateTime expiry)) return false;

        result = expiry.Date < issue.Date ? CrossCheckResult.Mismatch : CrossCheckResult.Match;
        return true;
    }

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
    /// 성형수술 고객(마스크 착용 → 얼굴 확인 불가)에 한해, 얼굴↔여권 사진을 대조했을 때 지문 스캔을 열어야 하는지 여부.
    /// 실제 열기(OnScanUnlocked)는 호출부가 '불일치 지적 대사가 끝난 뒤'에 한다 — 대사보다 패널이 먼저 뜨지 않게.
    /// 다른 종류 손님은 사진을 대조해도 지문판독기가 열리지 않는다(설계: 성형수술 전용).
    /// </summary>
    private bool ShouldUnlockFingerprintOnFaceMismatch(ICrossCheckSelectable a, ICrossCheckSelectable b, CrossCheckResult result)
    {
        if (result == CrossCheckResult.Unrelated) return false;   // 실제 얼굴↔사진 대조가 성립한 경우만
        if (!IsPhotoKey(a) || !IsPhotoKey(b)) return false;        // 양쪽 다 얼굴/사진 항목일 때만
        if (!IsCustomerSource(a.SourceType) || !IsCustomerSource(b.SourceType)) return false; // 캐릭터 얼굴 ↔ 여권 사진(서류)만
        if (!IsPlasticSurgeryCustomer()) return false;            // 성형수술 고객만
        return true;
    }

    /// <summary>현재 손님이 성형수술 관련 종류인가(성형 의심 고객 / 범죄자(성형수술)).</summary>
    private bool IsPlasticSurgeryCustomer()
    {
        string ct = _inspection != null ? _inspection.CurrentCharacterType : null;
        return ct == CharacterTypes.PlasticSuspect || ct == CharacterTypes.CriminalPlastic;
    }

    private static bool IsPhotoKey(ICrossCheckSelectable s)
        => s != null && IsPhotoKeyName(NormalizeKey(s.AttributeKey));

    /// <summary>정규화된 키가 얼굴/사진 항목인가.</summary>
    private static bool IsPhotoKeyName(string k) => k == "photo" || k == "photo_ref" || k == "face";

    /// <summary>
    /// 여권번호 불일치(서류↔서류, 예 비자 ↔ 여권 의 passport_no 가 다름)가 X-ray 잠금해제 조건을 만족하는가.
    /// 그 손님에게 X-ray 데이터가 있으면(=위험물 의심: 테러범 등) true. 실제 열기는 호출부가 '대사 재생 후'로 지연한다.
    /// X-ray 데이터가 없는 손님(단순 여권번호 불일치)은 false — 그건 그 자체로 거절 사유.
    /// 규정↔여권 대조(SourceType="규정")는 제외(서류끼리일 때만). 표시·게이팅 전용 — 판정/점수 무영향.
    /// </summary>
    private bool ShouldUnlockXrayOnPassportMismatch(ICrossCheckSelectable a, ICrossCheckSelectable b, CrossCheckResult result)
    {
        if (result != CrossCheckResult.Mismatch) return false;
        if (a == null || b == null) return false;
        if (!IsCustomerSource(a.SourceType) || !IsCustomerSource(b.SourceType)) return false; // 서류↔서류만(규정 제외)
        if (NormalizeKey(a.AttributeKey) != "passport_no" || NormalizeKey(b.AttributeKey) != "passport_no") return false;
        if (_inspection == null || _inspection.CurrentXray == null) return false; // X-ray 보유 손님만(위험물 의심 = 테러범 등)
        return true;
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
