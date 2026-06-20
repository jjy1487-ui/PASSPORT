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

    private int _gold; // HUD 표시 캐시(실제 누적은 ScoreEconomyManager.Money)

    // 정산 허브 참조. 진행 매니저가 명시 주입(SetEconomy)하면 그 인스턴스를 우선 사용하고,
    // 없으면 전역 싱글톤(ScoreEconomyManager.Instance)으로 폴백한다.
    // (씬 로드 타이밍/테스트 주입 순서로 Instance 해석이 흔들려도 정산이 누락되지 않게 한다.)
    private ScoreEconomyManager _economy;
    private ScoreEconomyManager Economy => _economy != null ? _economy : ScoreEconomyManager.Instance;

    // 교차대조 컨트롤러(X-ray 잠금해제 발행처). 입장 시 X-ray 자동 검사(day11 보안 강화)에 쓴다.
    // 씬 배선 없이 런타임 1회 조회로 캐시한다(Update 아님) — day1~14 모든 씬의 InspectionController 를 수정하지 않기 위함.
    private CrossCheckController _crossCheck;
    private bool _crossCheckResolved;
    private CrossCheckController CrossCheck
    {
        get
        {
            if (_crossCheck == null && !_crossCheckResolved)
            {
                _crossCheck = FindObjectOfType<CrossCheckController>();
                _crossCheckResolved = true; // 씬에 없으면 매번 탐색하지 않도록(없는 씬에서도 조용히 동작).
            }
            return _crossCheck;
        }
    }

    /// <summary>진행 매니저가 정산 허브를 주입한다(없으면 전역 싱글톤 폴백). UI 직접 참조 아님.</summary>
    public void SetEconomy(ScoreEconomyManager economy) => _economy = economy;

    // X-ray 패널(입장 자동 검사 개방처). 검사후 대사 흐름에서 '실제로 열렸는지' 확인하고 '닫힘'을 1회 구독하기 위해
    // 런타임 1회 탐색해 캐시한다(비활성 포함). 씬에 없으면 조용히 폴백(검사후 대사 즉시 재생).
    // UI 직접 참조가 아니라 패널의 OnClosed 이벤트만 구독한다(공통 규약 5장 — 이벤트 통신).
    private XrayInspectionPanel _xrayPanel;
    private bool _xrayPanelResolved;
    private XrayInspectionPanel XrayPanel
    {
        get
        {
            if (_xrayPanel == null && !_xrayPanelResolved)
            {
                _xrayPanel = FindObjectOfType<XrayInspectionPanel>(true); // true = 비활성 포함(루트가 꺼져 있을 수 있음)
                _xrayPanelResolved = true;
            }
            return _xrayPanel;
        }
    }

    private Day1Data _data;
    private int _index;
    private bool _customerSettled; // 현재 손님 확정 정산 1회 가드(중복 정산·이중 진행 방지)
    private int _rejectRound;       // 연예인 재거절 '티키타카' 단계(0=아직, 1~3). 일반 손님은 항상 0.
    private const int VipForceEntryRound = 3; // 이 횟수째 거부 → 강제입국(감액: approve_after_reject_3 → 평판 -6/돈 +5). 그 전엔 재제출(티키타카).
    private bool _ended;           // 엔딩 확정 시 true → 손님 진행/일자완료 패널 차단(엔딩 패널이 화면 점유)
    private readonly List<string> _dialogueLog = new List<string>(); // 현재 손님의 대화 기록(표시용 문자열)
    private readonly List<DialogueLineData> _dialogueLines = new List<DialogueLineData>(); // 구조 라인(대조 단서용)

    // 대화 요청 버튼용: 현재 손님의 "다시 들을 수 있는" 대사 케이스(입장/오거부 항의 등).
    // PlayThen 으로 흐른 마지막 케이스를 보관해 두고, 요청 시 그대로 재생한다.
    private DialogueCaseData _requestableCase;
    private bool _dialoguePlaying;

    /// <summary>현재 일차의 마지막 손님까지 끝나면 발행(인자: 방금 끝난 일차). 진행 매니저가 구독.</summary>
    public event System.Action<int> OnDayCompleted;

    /// <summary>현재 로드된 일차(데이터의 day). 데이터 없으면 0.</summary>
    public int CurrentDay => _data != null ? _data.day : 0;

    /// <summary>오늘 날짜 기준일(day1 = 2026-06-01). 이후 하루씩 증가.</summary>
    private static readonly System.DateTime DateBase = new System.DateTime(2026, 6, 1);

    /// <summary>
    /// 오늘 날짜("yyyy-MM-dd"). 기준일에 (현재 day - 1)일을 더한다(day1=2026-06-01).
    /// 런타임 계산 — 데이터 변경 없음. 일차당 고정(손님이 바뀌어도 같은 날 동일).
    /// 표시·대조 보조용 — 판정/점수에 영향 없음. 데이터 없으면 빈 문자열.
    /// </summary>
    public string CurrentDate =>
        _data != null ? DateBase.AddDays(System.Math.Max(0, _data.day - 1)).ToString("yyyy-MM-dd") : string.Empty;

    /// <summary>음성기록 팝업용: 현재 손님이 한 대화 목록(표시용 문자열).</summary>
    public IReadOnlyList<string> GetDialogueLog() => _dialogueLog;

    /// <summary>음성기록 팝업용(대조): 현재 손님이 한 대화의 구조 라인(claim 포함, 순서대로).</summary>
    public IReadOnlyList<DialogueLineData> GetDialogueLines() => _dialogueLines;

    /// <summary>대화 요청 버튼 활성 상태가 바뀌면 발행(버튼 뷰가 구독).</summary>
    public event System.Action<bool> OnRequestableChanged;

    /// <summary>현재 손님이 바뀌면 발행(검사기 버튼/패널이 구독해 갱신). 손님 없으면 false 시점에도 발행될 수 있음.</summary>
    public event System.Action OnCustomerChanged;

    // 지문으로 수배자를 적발(자동 연출)했는가 — 거부 대사를 체념/연행(적발) vs 항의(그냥 거부)로 가른다.
    private bool _fingerprintRevealed;
    /// <summary>지문 자동 적발 연출(CrossCheckController.PlayFingerprintReveal)이 현재 손님에게 재생됐음을 표시.</summary>
    public void MarkFingerprintRevealed() => _fingerprintRevealed = true;

    /// <summary>
    /// 지문으로 수배자를 적발한 뒤(적발 대사 종료 시) 호출: 플레이어가 도장을 찍지 않아도
    /// 자동으로 거부 정산(적발 보너스) + 체념/연행 대사 재생 → 다음 손님(그냥 끌려감).
    /// 이미 확정됐거나 엔딩이면 무시. PlayFingerprintReveal 의 onComplete 로 배선된다.
    /// </summary>
    public void AutoDetainWantedCustomer()
    {
        CustomerData c = Current;
        if (c == null || _customerSettled || _ended) return;
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false); // 도장 비활성 — 적발됐으니 끌려간다(도장 안 찍음)

        string advVariant = string.IsNullOrEmpty(c.defectVariant) ? null : c.defectVariant;
        string key = string.IsNullOrEmpty(c.rejectAdvancedBranchKey) ? BranchKeys.DetectMontageReject : c.rejectAdvancedBranchKey;
        BranchResult advBranch = BranchKeyResolver.ResolveAdvanced(DocStates.Defect, key, advVariant);
        SettleBranch(c.characterType, advBranch, wasCorrect: true, wasDetection: true); // 적발 보너스
        DialogueCaseData guided = FindCaseByType(c, c.rejectGuidedCaseType) ?? FindCase(c, GameResults.Reject, -1);
        PlayThen(guided, AdvanceNext); // 체념/연행 대사 → 다음 손님
    }

    /// <summary>day11 '보안 강화' 이후 전원 입장 시 수하물 X-ray 자동검사를 적용하는 시작 일차.</summary>
    private const int SecurityScanFromDay = 11;

    /// <summary>X-ray 데이터가 없는 손님을 위한 '이상 없음' 합성 결과(day11+ 보안 강화 전원 검사용, 읽기 전용 공유).</summary>
    private static readonly ScanData CleanXray = new ScanData
    {
        type = "xray", result = "정상", detail = "", extra = "", claim = null,
    };

    /// <summary>현재 손님의 X-ray 검사 결과(없으면 null). 검사기 UI 표시·대조용 — 판정에는 영향 없음.
    /// day11 '보안 강화' 이후에는 X-ray 데이터가 없는 손님도 '이상 없음'으로 합성한다(전원 수하물 검사).</summary>
    public ScanData CurrentXray
    {
        get
        {
            ScanData x = Current?.xray;
            if (!IsEmptyScan(x)) return x;                                              // 적발/실데이터 있으면 그대로
            if (Current != null && CurrentDay >= SecurityScanFromDay) return CleanXray; // 전원검사 → 이상없음 합성
            return x;                                                                   // 이전 날: 기존대로(null/빈)
        }
    }

    /// <summary>ScanData 가 비었는가(type/result/detail/extra/claim 모두 빔). XrayInspectionPanel.IsEmpty 와 동일 기준.</summary>
    private static bool IsEmptyScan(ScanData s) =>
        s == null
        || (string.IsNullOrEmpty(s.type)
            && string.IsNullOrEmpty(s.result)
            && string.IsNullOrEmpty(s.detail)
            && string.IsNullOrEmpty(s.extra)
            && (s.claim == null || string.IsNullOrEmpty(s.claim.attr)));

    /// <summary>현재 손님의 지문 검사 결과(없으면 null). 검사기 UI 표시·대조용 — 판정에는 영향 없음.</summary>
    public ScanData CurrentFingerprint => Current?.fingerprint;

    /// <summary>현재 손님의 character_type(없으면 null). 고급 분기 선택지 UI 표시·게이팅용 — 판정에는 영향 없음.</summary>
    public string CurrentCharacterType => Current?.characterType;

    /// <summary>현재 손님의 한글 이름(없으면 빈 문자열). 대조 불일치 대사 화자명 등 표시용 — 판정에는 영향 없음.</summary>
    public string CurrentCustomerName => Current != null && !string.IsNullOrEmpty(Current.nameKr) ? Current.nameKr : string.Empty;

    /// <summary>현재 손님의 customerId(없으면 -1). 취조 등 결정론적 보조 로직의 시드 산출용 — 판정에는 영향 없음.</summary>
    public int CurrentCustomerId => Current != null ? Current.customerId : -1;

    /// <summary>현재 손님의 제출 서류(없으면 null). 취조 힌트 산출용 읽기 전용 — 판정에는 영향 없음.</summary>
    public DocumentData[] CurrentDocuments => Current?.documents;

    /// <summary>현재 손님의 교차 대조 전용 대사(없으면 null). 대조 불일치 시 그 손님 전용 검사관·손님 대사 — 표시 전용, 판정 무영향.</summary>
    public CrossCheckLine[] CurrentCrossCheckLines => Current?.crossCheckLines;

    /// <summary>현재 손님 여권의 여권번호(passport_no 필드 값). 없으면 "". 규정↔여권번호 대조 보조용 — 판정 무영향.</summary>
    public string CurrentPassportNumber => PassportFieldValue("passport_no");

    /// <summary>현재 손님 여권의 발급 국가코드(여권 country, 없으면 nationality 필드). 없으면 "". 규정↔여권번호 대조 보조용 — 판정 무영향.</summary>
    public string CurrentPassportCountry
    {
        get
        {
            DocumentData p = FindPassportDocument();
            if (p == null) return string.Empty;
            if (!string.IsNullOrEmpty(p.country)) return p.country;
            return PassportFieldValue("nationality");
        }
    }

    /// <summary>현재 손님 본인의 성별(데이터상 신원 — 변형이 적용돼도 보존된다). 신분확인 규정↔여권 성별 대조 보조용 — 판정 무영향.</summary>
    public string CurrentCustomerGender => Current?.gender;

    /// <summary>현재 손님 여권의 성별(gender 필드 값). 없으면 "". 신분확인 규정↔여권 성별 대조 보조용 — 판정 무영향.</summary>
    public string CurrentPassportGender => PassportFieldValue("gender");

    /// <summary>현재 손님의 여권 문서를 찾는다(documentType 에 "여권" 포함). 없으면 null.</summary>
    private DocumentData FindPassportDocument()
    {
        DocumentData[] docs = Current?.documents;
        if (docs == null) return null;
        foreach (DocumentData d in docs)
            if (d != null && !string.IsNullOrEmpty(d.documentType) && d.documentType.Contains("여권")) return d;
        return null;
    }

    /// <summary>여권 문서에서 주어진 key 의 항목 값을 읽는다. 없으면 "".</summary>
    private string PassportFieldValue(string key)
    {
        DocumentData p = FindPassportDocument();
        if (p?.fields == null) return string.Empty;
        foreach (FieldEntry f in p.fields)
            if (f != null && f.key == key) return f.value ?? string.Empty;
        return string.Empty;
    }

    /// <summary>현재 손님의 defect_variant(없으면 null). 고급 분기 키 산출용 — 판정에는 영향 없음.</summary>
    public string CurrentDefectVariant => Current?.defectVariant;

    /// <summary>현재 손님의 정답 판정 상태(정상 손님이면 normal, 불량이면 defect). 고급 분기 doc_state 산출용.</summary>
    public string CurrentDocState =>
        Current != null ? (Current.correctResult == GameResults.Approve ? DocStates.Normal : DocStates.Defect) : null;

    /// <summary>지금 심사 중인 손님이 있는가(없으면 false). 심리상담집 등 '현재 손님 대상' 아이템의 사용 가능 판단용.</summary>
    public bool HasCurrentCustomer => Current != null;

    /// <summary>
    /// 현재 손님이 '검사후' 대사(CaseTypes.PostScan)를 가지는가(없으면 false).
    /// 이 손님(현재 존 카터)만 'X-ray 패널 닫힘 → 저작된 검사후 대사 재생' 지연 흐름을 탄다.
    /// CrossCheckController 가 금지물품 Match 시 자동 추궁 라인을 억제하고 창을 닫아 검사후 흐름으로 흡수할지
    /// 판단하는 게이트로 쓴다 — 표시·흐름 게이팅 전용, 판정/점수 무영향.
    /// </summary>
    public bool CurrentHasPostScanCase => FindCaseByType(Current, CaseTypes.PostScan) != null;

    /// <summary>
    /// 현재 손님이 결함(위반)이 있는가 = 정답 판정이 '거절'이면 true, '승인'이면 false.
    /// 확률 변형(altVariant)은 이미 RollVariants 가 이 데이터(correctResult)에 반영해 두므로,
    /// 이번 플레이에 실제 적용된 변형 기준으로 판정된다. 손님이 없으면 false.
    /// 정보 제공용(심리상담집 등) — 판정/점수에는 영향 없음.
    /// </summary>
    public bool CurrentCustomerHasDefect =>
        Current != null && Current.correctResult != GameResults.Approve;

    /// <summary>지금 '대화/심문' 버튼으로 대사를 요청할 수 있는가(재생 중이 아니고 요청 케이스 존재).</summary>
    public bool CanRequestDialogue =>
        !_dialoguePlaying && _requestableCase != null
        && _requestableCase.lines != null && _requestableCase.lines.Length > 0;

    /// <summary>
    /// 플레이어가 '대화/심문' 버튼을 눌렀을 때 호출. 현재 상태에 맞는 대사(입장/오거부 항의 등)를
    /// 다시 재생한다. 판정·오거부 루프·점수 로직은 건드리지 않는다(요청은 재생만).
    /// </summary>
    public void RequestDialogue()
    {
        if (!CanRequestDialogue) return;

        DialogueCaseData c = _requestableCase;
        _dialoguePlaying = true;
        RaiseRequestable();
        if (_dialogueView != null)
        {
            _dialogueView.Play(c, () =>
            {
                _dialoguePlaying = false;
                RaiseRequestable();
            });
        }
        else
        {
            _dialoguePlaying = false;
            RaiseRequestable();
        }
    }

    private void RaiseRequestable() => OnRequestableChanged?.Invoke(CanRequestDialogue);

    [Header("효과음")]
    [Tooltip("손님이 입장(등장)할 때 재생할 발자국 소리. 비워두면 Resources/Audio/Footstep 에서 자동 로드.")]
    [SerializeField] private AudioClip _footstepClip;
    [Range(0f, 1f)]
    [SerializeField] private float _footstepVolume = 1f;
    [Tooltip("손님 입장 후 발자국만 들리다가 대사가 자동으로 뜨기까지의 간격(초). 작을수록 대사가 빨리 뜸. 이 시점에 발자국은 멈춤(겹침 방지). 키우면 발자국이 더 길게 들린 뒤 대사.")]
    [SerializeField] private float _entryDialogueDelay = 0.8f;
    [Tooltip("테러범 폭탄 투척(시나리오 폭탄 결말) 시 재생할 폭발음. 비워두면 Resources/Audio/Bomb 에서 자동 로드.")]
    [SerializeField] private AudioClip _bombClip;
    [Range(0f, 1f)]
    [SerializeField] private float _bombVolume = 1f;
    [Tooltip("뇌물 손님(존 카터·강도식) 선택 제한시간(초). 0=무제한. 카운트다운 중 1초마다 틱, 시간초과 시 수락(공범 #11 조기엔딩)으로 자동 처리.")]
    [SerializeField] private float _bribeTimerSeconds = 15f;
    private AudioSource _sfx;

    private void Awake()
    {
        // 발자국 등 효과음용 2D AudioSource(없으면 추가). 같은 클립을 PlayOneShot 으로 매번 재생.
        _sfx = GetComponent<AudioSource>();
        if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0f; // 2D
        if (_footstepClip == null) _footstepClip = Resources.Load<AudioClip>("Audio/Footstep");
    }

    /// <summary>손님 입장 시 발자국 소리 1회 재생. 재생한 클립 길이(초)를 반환(없으면 0).
    /// 실행 순서(Awake 전에 Initialize→ShowCustomer 가 불릴 수 있음)에 안전하도록 여기서도 지연 초기화한다.</summary>
    private float PlayFootstep()
    {
        if (_sfx == null)
        {
            _sfx = GetComponent<AudioSource>();
            if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
        }
        if (_footstepClip == null) _footstepClip = Resources.Load<AudioClip>("Audio/Footstep");
        if (_footstepClip == null) return 0f;
        _sfx.PlayOneShot(_footstepClip, _footstepVolume);
        return _footstepClip.length;
    }

    /// <summary>테러범 폭탄 투척(시나리오 폭탄 결말) 시 폭발음 1회 재생. 클립 없으면 조용히 무시.</summary>
    private void PlayBombSfx()
    {
        if (_sfx == null)
        {
            _sfx = GetComponent<AudioSource>();
            if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
        }
        if (_bombClip == null) _bombClip = Resources.Load<AudioClip>("Audio/Bomb");
        if (_bombClip == null) { Debug.LogWarning("[InspectionController] 폭발음(Audio/Bomb) 로드 실패"); return; }
        _sfx.Stop(); // 발자국 등 잔여음 중단 후 폭발음
        _sfx.PlayOneShot(_bombClip, _bombVolume);
    }

    private Coroutine _entryCo;

    /// <summary>입장 후 짧은 간격(_entryDialogueDelay) 뒤 발자국을 멈추고 입장 대사를 자동 시작한다.
    /// 클릭 없이도 대사가 뜨며, 그 시점에 발자국을 멈춰 '발소리 + 말소리' 겹침을 막는다.</summary>
    private System.Collections.IEnumerator BeginEntryDialogueAfter(DialogueCaseData entry)
    {
        if (_entryDialogueDelay > 0f) yield return new WaitForSeconds(_entryDialogueDelay);
        if (_sfx != null) _sfx.Stop(); // 발자국 멈춤 → 대사와 겹치지 않게
        _entryCo = null;
        if (entry != null) PlayThen(entry, OnEntryDialogueDone);
        else OnEntryDialogueDone();
    }

    /// <summary>플레이어가 입장 대사를 기다리지 않고 먼저 검사(대조 등)를 시작했을 때 호출한다.
    /// 아직 '재생 대기 중'인 입장 대사 코루틴이 있으면 취소한다 — 추궁 대사 뒤에 인삿말이 뒤늦게 뜨는 버그 방지.
    /// 발자국도 멈추고, 판정/자동검사는 정상 활성화(OnEntryDialogueDone)해 소프트락을 막는다.
    /// 입장 대사가 이미 떴거나 진행 중이면(_entryCo==null) 아무것도 하지 않는다.</summary>
    public void NotifyInspectionStarted()
    {
        if (_entryCo == null) return;
        StopCoroutine(_entryCo);
        _entryCo = null;
        if (_sfx != null) _sfx.Stop();
        OnEntryDialogueDone();
    }

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
        UnsubscribePostScan(); // 컨트롤러 비활성 시 X-ray 닫힘 구독이 남지 않게 정리
    }

    private bool _awaitingFirstCustomer; // true면 첫 손님이 BeginInspection() 호출 전까지 대기(뉴스 닫기 게이트)

    /// <summary>데이터를 받아 해당 일차를 시작한다(첫날: 골드 초기화).</summary>
    public void Initialize(Day1Data data) => Initialize(data, true, true);

    /// <summary>데이터를 받아 해당 일차를 시작한다(resetGold=false 면 누적 골드 유지).</summary>
    public void Initialize(Day1Data data, bool resetGold) => Initialize(data, resetGold, true);

    /// <summary>
    /// 데이터를 받아 해당 일차를 시작한다.
    /// resetGold=false 면 누적 골드를 유지(2일차 이후 재초기화용).
    /// autoShowFirst=false 면 첫 손님을 바로 등장시키지 않고 <see cref="BeginInspection"/> 호출을 기다린다
    /// (하루 시작 시 뉴스를 먼저 띄우고, 뉴스를 닫아야 첫 손님이 입장하도록 게이트).
    /// </summary>
    public void Initialize(Day1Data data, bool resetGold, bool autoShowFirst)
    {
        if (data == null || data.customers == null || data.customers.Length == 0)
        {
            Debug.LogError("[InspectionController] 유효한 데이터가 없어 시작할 수 없습니다.");
            return;
        }

        _data = data;
        _index = 0;
        _ended = false; // 새 일차 시작 — 엔딩 차단 해제
        // 골드는 ScoreEconomyManager.Money 가 권위. 매니저가 있으면 그 값을 HUD 캐시에 반영한다.
        var mgr = Economy;
        if (mgr != null)
        {
            mgr.BeginDay();   // 당일 오판/적발 집계 리셋(일자 보상 기준)
            _gold = mgr.Money;
        }
        else if (resetGold) _gold = 0;
        UpdateGold();
        if (_dayCompleteRoot != null) _dayCompleteRoot.SetActive(false);
        if (_documentView != null) _documentView.ClearNotices(); // 날짜 넘어가면 누적 고지서 정리

        if (autoShowFirst)
        {
            _awaitingFirstCustomer = false;
            ShowCustomer(0);
        }
        else
        {
            // 뉴스를 먼저 띄우는 흐름: 첫 손님은 BeginInspection() 이 불릴 때(뉴스 닫은 뒤) 등장한다.
            _awaitingFirstCustomer = true;
            if (_customerView != null) _customerView.Hide();   // 손님 자리 비움(발자국/대사 전)
            if (_documentView != null) _documentView.Clear();  // 서류도 비움
            if (_dialogueView != null) _dialogueView.Hide();
        }
    }

    /// <summary>보류했던 첫 손님을 등장시킨다(뉴스 닫은 뒤 1회). autoShowFirst=false 로 시작했을 때만 동작.</summary>
    public void BeginInspection()
    {
        if (!_awaitingFirstCustomer) return;
        _awaitingFirstCustomer = false;
        ShowCustomer(0);
    }

    private CustomerData Current =>
        (_data != null && _index >= 0 && _index < _data.customers.Length)
            ? _data.customers[_index] : null;

    private void ShowCustomer(int index)
    {
        if (_ended) return; // 엔딩 확정됨 → 다음 손님/일자완료로 진행하지 않는다.
        _index = index;
        if (_data == null || index >= _data.customers.Length)
        {
            ShowDayComplete();
            return;
        }

        CustomerData c = _data.customers[index];
        _customerSettled = false;
        _rejectRound = 0;
        _fingerprintRevealed = false; // 새 손님 = 아직 지문 적발 안 됨(거부 대사 항의/체념 분기용)
        _scenarioRunner = null; // 이전 손님의 시나리오 러너 폐기(스테일 콜백/진행 방지)
        UnsubscribePostScan(); // 이전 손님의 검사후 대사 대기 구독이 남아 있으면 정리(다음 손님에게 오발화 방지)
        UnsubscribeScenarioScan(); // 이전 손님의 시나리오 검사 닫힘 구독 정리
        _dialogueLog.Clear();
        _dialogueLines.Clear();
        _requestableCase = null;
        _dialoguePlaying = false;
        if (_dialogueView != null) _dialogueView.Hide(); // 발자국 동안 대화창 숨김 → 발소리 끝난 뒤 다시 출력
        RaiseRequestable();
        OnCustomerChanged?.Invoke();

        if (_customerView != null) _customerView.Show(c, FacePhotoRef(c));
        PlayFootstep(); // 손님 등장 발자국 소리(잠시 뒤 대사 시작 시 멈춤)
        if (_documentView != null) _documentView.Show(c.documents);
        if (_slotCounterText != null) _slotCounterText.text = $"{index + 1} / {_data.customers.Length}";
        if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(false);

        // ── 분기 시나리오(노드그래프) 손님(예: day12 사토 하루키) ──
        //  scenario 가 유효하면 도장 판정·입장 대사·자동 X-ray 흐름을 전부 우회하고 ScenarioRunner 가 진행을 대신한다.
        //  (intro 노드가 인삿말~X-ray 적발 대사까지 대사로 서술하므로 입장 케이스/자동 검색을 띄우지 않는다.)
        //  scenario 없는 손님은 아래 기존 입장 대사 흐름 그대로 — 다른 손님 불변.
        if (c.scenario != null && c.scenario.IsValid)
        {
            if (CrossCheck != null) CrossCheck.SetLocked(true); // 시나리오 손님: 대조 사용 안 함(X-ray 자동닫힘으로 진행). 대조 멘트 오발화 방지.
            BeginScenario(c);
            return;
        }
        if (CrossCheck != null) CrossCheck.SetLocked(false); // 일반 손님: 대조 사용 가능(잠금 해제)

        // 입장 대사: 짧은 간격 뒤 클릭 없이 자동 시작(그때 발자국 멈춤). 끝나면 OnEntryDialogueDone 에서 판정 활성화.
        DialogueCaseData entry = FindCaseByType(c, CaseTypes.Entry);
        if (_entryCo != null) StopCoroutine(_entryCo);
        _entryCo = StartCoroutine(BeginEntryDialogueAfter(entry));
    }

    // 진행 중인 시나리오 러너(노드그래프 손님 1명당 1개). 다음 손님으로 넘어가면 폐기.
    private ScenarioRunner _scenarioRunner;

    /// <summary>지금 분기 시나리오(사토 등)가 진행 중인가. CrossCheckController 가 적발 멘트 억제 판단에 쓴다.</summary>
    public bool IsScenarioActive => _scenarioRunner != null;

    /// <summary>
    /// 분기 시나리오 손님(사토 등)의 진행을 ScenarioRunner 에 위임한다.
    ///  - 도장 판정 비활성(판정은 시나리오 결과가 대신).
    ///  - 발자국이 끝나도록 짧은 간격을 두고 start 노드부터 굴린다(입장 발소리 ↔ 대사 겹침 방지, 기존 입장 흐름과 톤 일치).
    ///  - 러너는 UI(대사 재생/선택 버튼)만 쓰고, 결과(테러방지=계속 / 폭탄=종착)는 콜백으로 돌려준다.
    /// </summary>
    private void BeginScenario(CustomerData c)
    {
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false); // 도장 판정 우회(시나리오가 판정을 대신)
        _scenarioRunner = new ScenarioRunner(
            c.scenario,
            _dialogueView,
            FindRejectPopup,                 // 선택 버튼 UI 재활용(2지선다)
            OnScenarioResolved,
            OpenScenarioScanThenProceed,     // 노드 openScanAfter("xray") → X-ray 표시 + 위험물 대조 일치(또는 닫기) 시 다음 노드 진행
            c.nameKr);

        if (_entryCo != null) StopCoroutine(_entryCo);
        _entryCo = StartCoroutine(BeginScenarioAfterFootstep());
    }

    private System.Collections.IEnumerator BeginScenarioAfterFootstep()
    {
        if (_entryDialogueDelay > 0f) yield return new WaitForSeconds(_entryDialogueDelay);
        if (_sfx != null) _sfx.Stop(); // 발자국 멈춤 → 대사와 겹치지 않게(기존 입장 흐름과 동일)
        _entryCo = null;
        if (_scenarioRunner != null) _scenarioRunner.Begin();
    }

    /// <summary>
    /// 시나리오 종착 콜백(Stage 2 범위).
    ///   - 테러방지 → 정상 해결: 기존 진행처럼 다음 손님으로(AdvanceNext).
    ///   - 폭탄     → 게임오버 종착: 진행을 정지(HaltForEnding)해 다음 손님/일자완료를 막는다.
    ///     (폭발 연출·사운드=Stage 3, 점수·조기엔딩 연결=Stage 4. 여기서는 명확한 종착 + 소프트락 방지만.)
    /// 점수/돈(outcome.score/reward)은 이번 단계에서 적용하지 않는다(로그는 ScenarioRunner 가 남김).
    /// </summary>
    private void OnScenarioResolved(ScenarioRunner.Resolution res, ScenarioOutcome outcome)
    {
        _scenarioRunner = null;
        if (res == ScenarioRunner.Resolution.Bomb)
        {
            Debug.Log("[InspectionController] 시나리오 종착: 폭탄 → 퍼엉! 조기엔딩 트리거.");
            if (_dialogueView != null) _dialogueView.Hide();
            PlayBombSfx(); // 폭발음(Audio/Bomb)

            // 폭탄 → 퍼엉! 조기엔딩: ScoreEconomyManager.TriggerEvent → ImmigrationManager.HandleEarlyEnding
            //  → EndingResolver.ResolveEarly(TerrorBomb) → RaiseEnding(엔딩 패널 표시 + HaltForEnding).
            ScoreEconomyManager economy = Economy;
            if (economy != null) economy.TriggerEvent(EventIds.TerrorBomb);
            // 엔딩이 안 떠도(매니저 부재 등) 진행은 정지(소프트락 방지). 엔딩이 떴으면 이미 _ended=true 라 중복 없음.
            if (!_ended) HaltForEnding();
            return;
        }

        Debug.Log("[InspectionController] 시나리오 종착: 테러방지(정상 해결) — 다음 손님으로 진행.");
        AdvanceNext();
    }

    /// <summary>
    /// 입장 대사가 끝난 직후 호출. 이 손님이 입장 자동 X-ray 검사 대상(autoScanOnEntry/day11+)이고 X-ray 데이터를 가지면
    /// X-ray 패널을 자동으로 연다.
    ///
    /// 손님이 '검사후 대사'(CaseTypes.PostScan, 예: 존 카터의 적발 지적 + 뇌물 제안)를 가지면:
    ///   ① 패널이 실제로 열렸으면 → 판정 활성화를 보류하고 패널 닫힘(OnClosed)을 1회 구독한다.
    ///      플레이어가 X-ray 를 보고 닫으면 그때 검사후 대사를 재생하고, 끝나면 판정을 활성화한다(원하는 순서).
    ///   ② 패널이 안 열렸으면(패널 미존재/X-ray 없음/검색 면제) → 소프트락 방지로 검사후 대사를 즉시 재생 후 판정 활성화.
    /// 검사후 대사가 없는 손님(일반 손님·윤정호 등)은 기존 동작 그대로 — 입장 끝나면 바로 판정.
    /// </summary>
    private void OnEntryDialogueDone()
    {
        TryAutoOpenEntryScan();

        CustomerData c = Current;
        DialogueCaseData postScan = FindCaseByType(c, CaseTypes.PostScan);
        if (postScan == null)
        {
            // 검사후 대사 없는 손님: 기존 동작 — 바로 판정 활성화(X-ray 는 오버레이라 판정과 독립적으로 보고 닫음).
            EnableJudgment();
            return;
        }

        XrayInspectionPanel panel = XrayPanel;
        if (panel != null && panel.IsOpen)
        {
            // 패널이 실제로 열렸다 → 닫힐 때 검사후 대사 재생 후 판정. (구독은 1회만)
            DeferPostScanUntilPanelClosed(panel, postScan);
        }
        else
        {
            // 폴백: 패널이 안 열림 → 검사후 대사를 즉시 재생하고 검사후 완료 라우팅(소프트락 방지).
            PlayThen(postScan, OnPostScanDone);
        }
    }

    /// <summary>
    /// 검사후 대사 종료 후 라우팅. 뇌물 손님(bribeOnReject)은 도장 판정 대신 자동 뇌물 팝업(2버튼)으로
    /// 직행하고, 그 외(검사후 케이스를 가진 일반 손님, 현재 없음)는 기존대로 판정을 활성화한다.
    /// </summary>
    private void OnPostScanDone()
    {
        CustomerData c = Current;
        if (c != null && c.bribeOnReject) ShowBribePopup(c);
        else EnableJudgment(); // 검사후 케이스 있는 일반 손님(현재 없음) 대비
    }

    /// <summary>
    /// 뇌물 팝업(자동, 도장 아님): "뇌물을 받으시겠습니까?" [안 받는다]/[받는다].
    ///   - [안 받는다](좌) = 정답: 밀수품 적발 거절(ResolveBribeRefuse).
    ///   - [받는다](우)   = 공범: 입국 허가 + 금괴 + 조기엔딩 #11(ResolveBribeAccept).
    /// 팝업을 못 찾으면 소프트락 방지로 안전하게 정답 거절로 폴백한다.
    /// </summary>
    private void ShowBribePopup(CustomerData c)
    {
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
        RejectConfirmPopup popup = FindRejectPopup();
        if (popup == null) { ResolveBribeRefuse(c); return; } // 폴백: 안전하게 정답 거절(소프트락 없음)
        popup.ShowConfirm(
            "뇌물을 받으시겠습니까?",
            "안 받는다",                  // 좌(onReview): 거부 — 정답 적발 거절
            "받는다",                     // 우(onConfirm): 수락 — 공범(금괴/조기엔딩)
            () => ResolveBribeRefuse(c),  // 좌 = 안 받는다 = 정답 거절
            () => ResolveBribeAccept(c),  // 우 = 받는다 = 공범
            _bribeTimerSeconds,           // 제한시간(카운트다운+1초틱)
            () => ResolveBribeAccept(c)); // 시간초과 → 수락(공범 #11): 시간 내 결정 못 하면 묵인=공범으로 간주
    }

    // 검사후 대사 지연 흐름의 1회 구독 가드(중복 구독·이중 발화 방지).
    private XrayInspectionPanel _postScanPanel;
    private System.Action _postScanHandler;

    /// <summary>
    /// X-ray 패널 닫힘(OnClosed)을 1회 구독해, 닫히면 검사후 대사를 재생하고 판정을 활성화한다.
    /// 손님이 바뀌어 구독이 정리되지 않은 채로 남지 않게, 발화 시 즉시 해제한다(1회성).
    /// </summary>
    private void DeferPostScanUntilPanelClosed(XrayInspectionPanel panel, DialogueCaseData postScan)
    {
        UnsubscribePostScan(); // 혹시 남아 있던 이전 구독 정리(같은 손님 재진입 등)

        _postScanPanel = panel;
        _postScanHandler = () =>
        {
            UnsubscribePostScan();
            PlayThen(postScan, OnPostScanDone);
        };
        panel.OnClosed += _postScanHandler;
    }

    /// <summary>검사후 대사용 X-ray 닫힘 구독을 해제한다(있으면). 1회 발화 후·손님 교체 시 호출.</summary>
    private void UnsubscribePostScan()
    {
        if (_postScanPanel != null && _postScanHandler != null)
            _postScanPanel.OnClosed -= _postScanHandler;
        _postScanPanel = null;
        _postScanHandler = null;
    }

    /// <summary>
    /// 입장 자동 X-ray 검사 트리거. 손님이 autoScanOnEntry 이고 X-ray 데이터가 있으면
    /// CrossCheckController 의 잠금해제 발행 경로로 X-ray 패널을 연다(XrayInspectionPanel 이 구독해 Open).
    /// CrossCheckController 가 씬에 없으면 조용히 무시(기존 동작 유지).
    /// </summary>
    private void TryAutoOpenEntryScan()
    {
        CustomerData c = Current;
        if (c == null) return;
        if (c.skipAutoScan) return; // 검색 면제 손님(검사 거부 특혜 등): day11+ 라도 자동 검색 안 띄움.
        // day11 '보안 강화' 이후: 전원 입장 자동 X-ray. 이전 날: 손님별 autoScanOnEntry 플래그만.
        if (CurrentDay < SecurityScanFromDay && !c.autoScanOnEntry) return;
        if (CurrentXray == null) return; // day11+ 는 합성 이상없음이라 null 아님(데이터 없는 이전 날만 차단)
        CrossCheckController cc = CrossCheck;
        if (cc != null) cc.RequestScanUnlock("xray");
    }

    // ── 시나리오(사토) 노드 중간 X-ray 표시 ──────────────────────────────
    // 시나리오 손님은 입장 자동검사를 우회하므로, 노드 openScanAfter("xray") 지점에서 X-ray 를 띄운다.
    // 적발물이 뜨면 X-ray 가 N초 뒤 자동으로 닫히고(또는 수동 닫기), 닫히면 다음 노드(검문관 발각 대사)로 진행한다.
    // (대조 불필요 — 존카터/강도식의 '검사후 대사' 흐름과 동일하게 X-ray 닫힘이 트리거.)
    private XrayInspectionPanel _scnPanel;
    private System.Action _scnProceed;
    private System.Action _scnClosedHandler;

    /// <summary>
    /// 시나리오 노드 openScanAfter 처리(현재 "xray"). X-ray 를 열고, 닫히면(자동 N초/수동) onProceed 로 다음 노드 진행.
    /// 패널 부재·데이터 없음(안 열림)이면 false → 러너가 즉시 다음으로 진행(소프트락 방지).
    /// </summary>
    private bool OpenScenarioScanThenProceed(string kind, System.Action onProceed)
    {
        if (string.IsNullOrEmpty(kind)) return false;
        if (!string.Equals(kind, "xray", System.StringComparison.OrdinalIgnoreCase)) return false;

        XrayInspectionPanel panel = XrayPanel;
        if (panel == null) return false;

        panel.Open();                    // X-ray 표시(적발 시 N초 뒤 자동 닫힘)
        if (!panel.IsOpen) return false; // 데이터 없음/미개방 → 진행 위임

        UnsubscribeScenarioScan(); // 혹시 남은 이전 구독 정리
        _scnPanel = panel;
        _scnProceed = onProceed;
        _scnClosedHandler = () => ScenarioScanProceed(); // 닫히면(자동/수동) 다음 노드 진행
        panel.OnClosed += _scnClosedHandler;
        return true;
    }

    /// <summary>시나리오 X-ray 닫힘 → 다음 노드 진행(1회 가드).</summary>
    private void ScenarioScanProceed()
    {
        System.Action proceed = _scnProceed;
        UnsubscribeScenarioScan();
        proceed?.Invoke();
    }

    /// <summary>시나리오 X-ray 닫힘 구독 해제. 발화 후·손님 교체 시 호출.</summary>
    private void UnsubscribeScenarioScan()
    {
        if (_scnPanel != null && _scnClosedHandler != null) _scnPanel.OnClosed -= _scnClosedHandler;
        _scnPanel = null;
        _scnProceed = null;
        _scnClosedHandler = null;
    }

    private void EnableJudgment()
    {
        if (_judgmentPanel != null) _judgmentPanel.SetReady(true);
    }

    /// <summary>
    /// 손님 얼굴 이미지 키(photo_ref)를 그 손님의 여권 문서 spriteRef 에서 얻는다.
    /// 같은 인물이므로 얼굴=여권사진 동일 키를 쓴다. 여권이 없으면 첫 문서, 그것도 없으면 "".
    /// 표시 전용 — 대조/판정 로직과 무관하다.
    /// </summary>
    private static string FacePhotoRef(CustomerData c)
    {
        if (c == null) return "";
        // 초상(데스크 앞 인물)은 항상 '실제 인물'(customer.spriteRef)을 보여준다.
        //  여권 사진(passport.spriteRef)은 위조 시 다른 값(photo_mismatch)이라, 그걸 쓰면
        //  사진 위조 손님의 초상이 빈칸이 된다 → 손님 본인 키를 우선한다.
        if (!string.IsNullOrEmpty(c.spriteRef)) return c.spriteRef;
        // 폴백: 손님 spriteRef 가 비면 보유 문서(여권 등)의 첫 spriteRef.
        if (c.documents != null)
            foreach (DocumentData d in c.documents)
                if (d != null && !string.IsNullOrEmpty(d.spriteRef)) return d.spriteRef;
        return "";
    }

#if UNITY_EDITOR
    // [디버그/QA 전용 · 빌드 미포함] 검수 편의 단축키.
    //   [ / ]  = 이전/다음 손님,   숫자 1~7 = N번째 손님으로 점프.
    //   ※ 점프는 현재 손님 판정·정산을 건너뛴다(검수용). 정식 진행/점수와 무관.
    private void Update()
    {
        if (_data == null || _ended) return;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        int last = _data.customers.Length;
        if (kb.rightBracketKey.wasPressedThisFrame) ShowCustomer(Mathf.Min(last, _index + 1));
        else if (kb.leftBracketKey.wasPressedThisFrame) ShowCustomer(Mathf.Max(0, _index - 1));
        else if (kb.digit1Key.wasPressedThisFrame && last >= 1) ShowCustomer(0);
        else if (kb.digit2Key.wasPressedThisFrame && last >= 2) ShowCustomer(1);
        else if (kb.digit3Key.wasPressedThisFrame && last >= 3) ShowCustomer(2);
        else if (kb.digit4Key.wasPressedThisFrame && last >= 4) ShowCustomer(3);
        else if (kb.digit5Key.wasPressedThisFrame && last >= 5) ShowCustomer(4);
        else if (kb.digit6Key.wasPressedThisFrame && last >= 6) ShowCustomer(5);
        else if (kb.digit7Key.wasPressedThisFrame && last >= 7) ShowCustomer(6);
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// [QA/검수 전용] 현재 로드된 일차 안에서 index(0-base) 손님으로 즉시 점프한다.
    /// 판정·정산·대사를 건너뛰고 그 손님 화면을 바로 보여준다(정식 진행/점수와 무관).
    /// 화면 오버레이(QaJumpOverlay)·ImmigrationManager.DebugJumpTo 가 호출한다.
    /// </summary>
    public void DebugJumpToSlot(int index)
    {
        if (_data == null || _data.customers == null || _data.customers.Length == 0) return;
        _ended = false; // 엔딩/일자완료로 막혀 있었어도 검수 점프는 허용
        index = Mathf.Clamp(index, 0, _data.customers.Length - 1);
        ShowCustomer(index);
    }
#endif

    private void UpdateGold()
    {
        if (_goldText != null) _goldText.text = _gold.ToString();
    }

    /// <summary>손님이 외국인인가(국적이 한국이 아니면 외국인). #16 등잔 밑이 어둡다 판정용.
    /// 국적 필드는 "대한민국(KOR)"/"미국(USA)"/"중국(CHN)" 형식.</summary>
    private static bool IsForeignCustomer(CustomerData c)
    {
        if (c == null) return false;
        string n = (c.nationality ?? string.Empty).ToUpperInvariant();
        return !(n.Contains("대한민국") || n.Contains("한국") || n.Contains("KOR"));
    }

    private void HandleDecision(bool approve)
    {
        CustomerData c = Current;
        if (c == null) return;

        // 판정(도장)을 찍으면 X-ray 창을 닫는다 — 도장이 X-ray 창에 가려지지 않게(검사후 트리거 OnClosed 는 발화 안 함).
        if (XrayPanel != null) XrayPanel.HideSilently();

        // 여권 종이 위에 도장 자국
        if (_documentView != null) _documentView.StampPrimary(approve);

        bool shouldApprove = c.correctResult == GameResults.Approve;
        Debug.Log($"[NoticeDBG] 판정: approve={approve} shouldApprove={shouldApprove} advBranch='{c.rejectAdvancedBranchKey}' → {(approve == shouldApprove ? "정답(고지서없음)" : (!approve ? "오거부→WrongRejectNotice" : "오허가→ViolationNotice"))}");

        // #16 등잔 밑이 어둡다: 외국인 + 결함(거절 대상) 손님을 잘못 승인 → 누적(BlindspotThreshold 회 도달 시 엔딩).
        if (approve && !shouldApprove && IsForeignCustomer(c))
            Economy?.TriggerEvent(EventIds.OverstayApprove);

        // 고급 분기 손님(예: 성형 수술 지명수배 범죄자): 거부(approve=false) 시 정답 극성과 무관하게
        // 항상 데이터 지정 최선 분기(detect_montage_xray_reject 등)로 1회 정산 + 가이드 대사 재생.
        // (시드에 따라 correctResult 가 '정상 거절'로 바뀌어도 인터셉트가 누락되지 않도록 if-체인 앞에 둔다.)
        if (!approve && !string.IsNullOrEmpty(c.rejectAdvancedBranchKey))
        {
            if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
            string advVariant = string.IsNullOrEmpty(c.defectVariant) ? null : c.defectVariant;
            // docState 를 Defect 로 명시: 점수/지급표가 doc_state=defect 키이므로(정상 승인 손님이라도) 강제 매칭.
            BranchResult advBranch = BranchKeyResolver.ResolveAdvanced(
                DocStates.Defect, c.rejectAdvancedBranchKey, advVariant);
            // 적발(apprehension): wasCorrect=true(오판 미집계·WARNING 회피), wasDetection=true(일자 DETECTION 보너스).
            SettleBranch(c.characterType, advBranch, wasCorrect: true, wasDetection: true);
            // 거부 대사 분리: 지문으로 적발한 뒤 거부 = 체념/연행(분기 거부 케이스),
            //                그냥 거부(조사 안 함) = 일반 손님처럼 항의(정상 거절 케이스).
            DialogueCaseData guided = _fingerprintRevealed
                ? (FindCaseByType(c, c.rejectGuidedCaseType) ?? FindCase(c, GameResults.Reject, -1))
                : (FindCase(c, GameResults.Reject, -1) ?? FindCaseByType(c, c.rejectGuidedCaseType));
            PlayThen(guided, AdvanceNext);
            return;
        }

        // ── 연예인/정치인 등 '재거절 감액' 캐릭터: 정상 서류를 거부하면 단발 오거부 대신 '3번 티키타카' ──
        //  거부할 때마다 단계↑. 1~2회 = 그 단계 대사(심사관 안내 + 한지원 반응)를 대사창에 재생하고 서류를
        //  다시 내밀어(재제출) 또 도장 찍게 한다(정산·진행 보류). VipForceEntryRound(3)회째 = 항의 대사 +
        //  상급자 호출 → 강제입국 + 감액 정산(approve_after_reject_3 → 평판 -6 / 돈 +5).
        //  전용 단계 대사(잘못 거절 rejectCount=1)가 저작된 손님만 — 미저작 연예인은 아래 일반 오거부로 폴백.
        if (!approve && shouldApprove
            && BranchKeyResolver.UsesAccrueScale(c.characterType)
            && FindCase(c, GameResults.WrongReject, 1) != null)
        {
            // 거절 도장 → '정말 거절?' 확인 팝업. [예]=이번 단계 진행(단계 대사 + 재제출/강제입국), [아니오]=취소.
            if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
            RejectConfirmPopup popup = FindRejectPopup();
            Debug.Log($"[VipReject] {c.nameKr} 거절 도장 — 팝업={(popup == null ? "NULL(못찾음→팝업없이 대사로 진행)" : "FOUND(표시 시도)")}");
            if (popup == null)
            {
                AdvanceVipReject(c); // 팝업 못 찾으면 팝업 없이 단계 진행(대사 티키타카만)
                return;
            }
            popup.ShowConfirm(
                "정말 거절하시겠습니까?",
                "아니오",
                "예",
                RearmForRejectRetry,        // 아니오: 취소(재무장, 단계 변화 없음)
                () => AdvanceVipReject(c));  // 예: 이번 단계 진행
            return;
        }

        // ── 결함 VIP(연예인/정치인 + 거절 정답): 거절(정답)해도 순순히 안 물러나고 3번 버티다 입국 거부 확정 ──
        //  정상서류 루프(위)의 대칭. 거부할 때마다 단계↑. 1~2회 = 그 단계 대사(심사관↔VIP 티키타카) 재생 + 재제출.
        //  VipForceEntryRound(3)회째 = 거절 확정(reject_correct 정산, 적발 보너스) + 최종 대사 → 다음 손님.
        //  전용 단계 대사(정상 거절 rejectCount=1)가 저작된 손님만 — 미저작 결함 손님은 아래 일반 거절로 폴백.
        if (!approve && !shouldApprove
            && BranchKeyResolver.UsesAccrueScale(c.characterType)
            && FindCase(c, GameResults.Reject, 1) != null)
        {
            // 거절 도장 → '정말 거절?' 확인 팝업(대조 거절과 공용). [예]=이번 단계 진행, [아니오]=취소.
            ShowGuiltyRejectConfirm(c);
            return;
        }

        // ── 뇌물 제안 손님(밀수품 범죄자 존 카터 등): 도장 판정을 쓰지 않는다 ──
        //  검사후 대사가 끝나면 OnPostScanDone 이 자동으로 뇌물 팝업(2버튼)을 띄워 판정한다(ShowBribePopup).
        //  [안 받는다]=정답 거절(ResolveBribeRefuse) / [받는다]=공범(ResolveBribeAccept). 오판/거절팝업 도장 트리거는 제거됨.

        if (approve == shouldApprove)
        {
            // 정답: 정상 승인 / 정상 거절. 확정 → 정산(점수≠돈, 캐릭터별 테이블).
            //  연예인이 1~2회 거절 뒤 승인하면 _rejectRound>0 → approve_after_reject_N 감액(돈 25/15).
            SettleCustomer(c, approve, _rejectRound, forcedPass: false);
            DialogueCaseData ok = FindCase(c, c.correctResult, -1);
            PlayThen(ok, AdvanceNext);
        }
        else if (!approve)
        {
            // 일반 손님을 거부(오판). 손글 스크립트 모델대로 작성된 항의 대사 1교환
            // (검문관 거부 안내 → 방문객 항의)을 그대로 재생하고, 페널티 확정 후 다음 손님으로 넘어간다.
            // 단발 처리 — 재제출/강제통과 없음(연예인/정치인 '재거절 감액' 루프는 위 블록에서 이미 처리·return).
            // (고급 분기 손님도 위 if-체인 앞에서 인터셉트되어 여기 도달하지 않는다.)
            SettleCustomer(c, approve, 0, forcedPass: false); // 오거부 페널티 확정
            SpawnWrongRejectNotice(c);                        // 정상 서류를 거부 → 오류 고지서로 피드백
            DialogueCaseData wrongReject = FindCase(c, GameResults.WrongReject, -1) ?? new DialogueCaseData
            {
                caseType = "오거부 확정", gameResult = "-", rejectCount = 0,
                lines = new[] { new DialogueLineData { order = 1, speaker = "심사관", text = "확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다." } },
            };
            PlayThen(wrongReject, AdvanceNext);
        }
        else
        {
            // 오허가(거부해야 하는데 승인): 확정 → 오판 정산.
            SettleCustomer(c, approve, 0, forcedPass: false);
            SpawnViolationNotice(c); // 무엇이 틀렸는지 '고지서' 서류로 알림
            DialogueCaseData wrong = FindCase(c, GameResults.WrongApprove, -1);
            PlayThen(wrong, AdvanceNext);
        }
    }

    /// <summary>
    /// 오판(결함 손님을 통과)으로 확정됐을 때, 무엇이 틀렸는지 적힌 '고지서' 서류를 스폰한다.
    /// 손님 서류 중 결함(비정상/violationField)인 항목을 나열한다. 판정/점수에는 영향 없음(피드백 표시 전용).
    /// </summary>
    private void SpawnViolationNotice(CustomerData c)
    {
        if (_documentView == null || c?.documents == null) return;

        // 결함 서류를 '전부' 찾아, 각 서류의 결함 항목(필드)·값·사유를 한 줄씩 구체적으로 적는다.
        // (유저가 어떤 항목으로 틀렸는지 명확히 알 수 있게 — 일반 문구 대신 상세 표기.)
        var lines = new System.Collections.Generic.List<string>();
        foreach (DocumentData d in c.documents)
        {
            if (d == null) continue;
            bool isDefect = (!string.IsNullOrEmpty(d.variant) && d.variant.Contains("비정상"))
                         || (!string.IsNullOrEmpty(d.violationField) && d.violationField != "없음");
            if (isDefect) lines.Add(DetailedViolationLine(d));
        }
        string reason = lines.Count > 0
            ? "입국 거부 대상을 허가했습니다. 결함 항목:\n" + string.Join("\n", lines)
            : "입국 거부 대상인 손님을 통과시켰습니다.";

        var notice = new DocumentData
        {
            documentType = "심사 오류 고지서",
            variant = "정상",
            violationField = "없음",
            country = "",
            spriteRef = "",
            fields = new[]
            {
                new FieldEntry { label = "판정", value = "입국 거부 대상", key = "" },
                new FieldEntry { label = "사유", value = reason, key = "" },
            },
        };

        _documentView.SpawnNotice(notice);
    }

    /// <summary>정상 서류 손님을 거부(오거부)했을 때, "정상 서류였다"는 고지서를 스폰한다(피드백 표시 전용).</summary>
    private void SpawnWrongRejectNotice(CustomerData c)
    {
        if (_documentView == null) return;
        var notice = new DocumentData
        {
            documentType = "심사 오류 고지서",
            variant = "정상",
            violationField = "없음",
            country = "",
            spriteRef = "",
            fields = new[]
            {
                new FieldEntry { label = "판정", value = "입국 허가 대상", key = "" },
                new FieldEntry { label = "사유", value = "제출한 서류가 모두 정상이었으나 입국을 거부하였습니다.", key = "" },
            },
        };
        _documentView.SpawnNotice(notice);
    }

    /// <summary>위반 서류 종류·항목으로 엑셀 inspection_notice 시트에서 거부 사유 문구를 가져온다(없으면 일반 문장).
    /// body 의 {field}/{document} 치환자를 실제 항목/서류명으로 치환한다.</summary>
    private string NoticeReason(string docType, string violationField)
    {
        string field = string.IsNullOrEmpty(violationField) || violationField == "없음" ? "" : violationField;
        string errorType = NoticeErrorType(docType, field);

        var db = GameDatabaseProvider.Database;
        InspectionNoticeTable table = db != null ? db.inspectionNotice : null;
        DataRow row = null;
        if (table != null)
        {
            row = table.Find(errorType, docType);
            if (row == null)
            {
                // 같은 error_type 의 다른 서류 행: 서류명이 {document} 치환자인 행만 재사용(여권 전용 문구를 다른 서류에 오용 방지).
                DataRow any = table.Find(errorType, null);
                if (any != null && (any.Get("body") ?? "").Contains("{document}")) row = any;
            }
        }

        string fieldLabel = string.IsNullOrEmpty(field) ? docType : field;
        if (row == null)
            return $"{docType}의 {fieldLabel} 항목이 규정에 부합하지 않는 서류였으나 입국을 허가하였습니다.";

        string body = row.Get("body") ?? "";
        return body.Replace("{field}", fieldLabel).Replace("{document}", docType);
    }

    /// <summary>결함 서류 1건 → "· {사람이 바로 이해할 사유}" 한 줄. 항목/값 나열 대신 쉬운 문장으로.</summary>
    private string DetailedViolationLine(DocumentData d)
    {
        string field = string.IsNullOrEmpty(d.violationField) || d.violationField == "없음" ? "" : d.violationField;
        return "· " + ViolationReason(d.documentType, NoticeErrorType(d.documentType, field), field);
    }

    /// <summary>error_type(+필드/서류) → 사람이 바로 이해하는 짧은 거부 사유 한 줄. (띄어쓰기 정규화)</summary>
    private static string ViolationReason(string docType, string errorType, string field)
    {
        string f = (field ?? "").Replace(" ", "");
        switch (errorType)
        {
            case "위조":      return "여권번호가 위조되었습니다.";
            case "기간만료":  return $"{docType} 유효기간이 지났습니다.";
            case "사진불일치": return "여권 사진과 본인 외모가 다릅니다.";
            case "검사부적합":
                if (f == "검사결과") return "PCR 검사 결과가 양성입니다.";
                if (f == "검사일")   return "PCR 검사일이 유효 기간을 벗어났습니다.";
                if (f == "검사기관") return "공인되지 않은 검사기관입니다.";
                return "PCR 검사서가 규정에 맞지 않습니다.";
            default:
                if (f == "성별") return "여권 성별이 본인과 다릅니다.";
                return string.IsNullOrEmpty(field)
                    ? $"{docType} 정보가 규정과 맞지 않습니다."
                    : $"{field} 정보가 규정과 맞지 않습니다.";
        }
    }

    /// <summary>서류에서 violationField(라벨/키)에 해당하는 값을 찾는다. 없으면 빈 문자열.</summary>
    private static string FindFieldValue(DocumentData d, string violationField)
    {
        if (d?.fields == null || string.IsNullOrEmpty(violationField)) return "";
        foreach (FieldEntry f in d.fields)
            if (f != null && (f.label == violationField || f.key == violationField)) return f.value ?? "";
        return "";
    }

    /// <summary>위반 항목(한글 라벨)/서류 종류 → inspection_notice 의 error_type 으로 매핑.</summary>
    private static string NoticeErrorType(string docType, string field)
    {
        if (docType == "PCR검사서") return "검사부적합";
        switch (field)
        {
            case "만료일": case "유효기간": return "기간만료";
            case "사진": case "얼굴": return "사진불일치";
            case "여권번호": return "위조";
            default: return "정보불일치";
        }
    }

    /// <summary>
    /// 한 손님 확정 시 1회 정산. branch_key 산출 → ScoreEconomyManager 가 점수/돈/호칭/아이템/조기엔딩 처리.
    /// ScoreEconomyManager 가 없으면(테스트/씬 미배치) 조용히 스킵하고 기존 동작을 유지한다.
    /// </summary>
    private void SettleCustomer(CustomerData c, bool approved, int wrongRejectCount, bool forcedPass)
    {
        bool shouldApprove = c.correctResult == GameResults.Approve;
        bool wasCorrect = (approved == shouldApprove) || forcedPass;

        // 적발 = 결함(거부 정답) 손님을 올바로 거부했을 때(위조/밀수/지명수배). 일자 DETECTION 보너스용.
        bool wasDetection = !shouldApprove && wasCorrect && !approved;

        BranchResult branch = BranchKeyResolver.Resolve(
            c.correctResult, approved, wrongRejectCount, forcedPass, c.characterType, c.defectVariant);
        SettleBranch(c.characterType, branch, wasCorrect, wasDetection);
    }

    /// <summary>
    /// 손님 1명 확정 정산을 한 번만 수행한다(중복 가드). 기본 판정과 고급 분기가 같은 손님을
    /// 이중 정산하지 않도록 _customerSettled 로 1회 보장한다. 매니저 없으면 조용히 스킵.
    /// </summary>
    private void SettleBranch(string characterType, BranchResult branch, bool wasCorrect, bool wasDetection = false)
    {
        if (_customerSettled) return; // 이미 확정된 손님 — 중복 정산 금지
        _customerSettled = true;

        var mgr = Economy;
        if (mgr == null) return; // 씬에 매니저가 없으면 점수/경제 비활성(기존 동작 유지)

        mgr.Settle(characterType, branch, wasCorrect, wasDetection);

        // HUD 골드 캐시 동기화(표시용).
        _gold = mgr.Money;
        UpdateGold();
    }

    /// <summary>
    /// 고급 분기 패널(특수 캐릭터 선택지)이 선택을 확정하면 호출한다.
    /// 해당 손님을 그 branch_key 로 **1회 정산**하고 다음 손님으로 진행시킨다.
    /// 기본 판정(도장)과 동일하게 InspectionController 가 진행 권위를 갖는다 → 특수 캐릭터에서 진행이 막히지 않는다.
    /// 이미 도장 등으로 확정된 손님이면 정산은 가드되고 진행만 보장한다.
    /// </summary>
    /// <param name="branchKey">AdvancedBranchPanel 이 고른 BranchKeys.* 키.</param>
    /// <param name="wasCorrect">정확도 집계용 정답 여부(정의 선택=정답).</param>
    public void SubmitAdvancedDecision(string branchKey, bool wasCorrect)
    {
        CustomerData c = Current;
        if (c == null) return;
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);

        string docState = c.correctResult == GameResults.Approve ? DocStates.Normal : DocStates.Defect;
        BranchResult branch = BranchKeyResolver.ResolveAdvanced(docState, branchKey, c.defectVariant);
        SettleBranch(c.characterType, branch, wasCorrect);

        DialogueCaseData ok = FindCase(c, c.correctResult, -1);
        PlayThen(ok, AdvanceNext);
    }

    private void PlayThen(DialogueCaseData dialogueCase, System.Action onComplete)
    {
        // 엔딩 확정(HaltForEnding → _ended) 후엔 결과 대사를 재생하지 않는다.
        // 오판 승인은 SettleCustomer(정산)에서 조기엔딩을 띄운 '직후' 이 대사(잘못 허가 등)를 호출하므로,
        // 가드가 없으면 엔딩 컷씬 위로 대사가 계속 재생된다. 진행(onComplete=AdvanceNext)도 막는다(엔딩=종착).
        if (_ended)
        {
            if (_dialogueView != null) { _dialogueView.StopSpeaking(); _dialogueView.Hide(); }
            return;
        }

        AppendLog(dialogueCase);

        // 이 케이스를 "요청 가능한 대사"로 보관(입장/오거부 항의 등 현재 손님 맥락 대사).
        _requestableCase = dialogueCase;
        _dialoguePlaying = true;
        RaiseRequestable();

        System.Action wrapped = () =>
        {
            _dialoguePlaying = false;
            RaiseRequestable();
            onComplete?.Invoke();
        };

        if (_dialogueView != null)
        {
            _dialogueView.Play(dialogueCase, wrapped);
        }
        else
        {
            wrapped();
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
            _dialogueLines.Add(ln);
        }
    }

    private void AdvanceNext()
    {
        ShowCustomer(_index + 1);
    }

    /// <summary>
    /// 거절 팝업 [아니오] 처리: 거부 도장을 지우고(서류를 돌려받음) 다시 판정 가능하게 재무장한다.
    /// 같은 손님이 그대로 남아 다음 판정을 기다린다(페널티·진행 없음).
    /// </summary>
    private void RearmForRejectRetry()
    {
        if (_documentView != null) _documentView.ClearStamps();          // 거부 도장 자국 제거(다시 드릴게요)
        if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(true); // 다시 도장 찍을 수 있게
    }

    /// <summary>
    /// 팝업 [예](또는 팝업 미발견 폴백): 거절 단계를 한 칸 올린다.
    /// 1~2회 = 그 단계 대사(심사관 안내 + 한지원 반응)를 대사창에 재생하고 서류 재제출(다시 도장 가능).
    /// VipForceEntryRound(3)회째 = 항의 대사 → 강제입국 + 감액 정산(approve_after_reject_3 → 평판 -6 / 돈 +5).
    /// </summary>
    private void AdvanceVipReject(CustomerData c)
    {
        _rejectRound++;
        Debug.Log($"[VipReject] {c.nameKr} {_rejectRound}회 확정 → {(_rejectRound >= VipForceEntryRound ? "강제입국" : "재제출(티키타카)")}");
        if (_rejectRound < VipForceEntryRound)
        {
            DialogueCaseData step = FindCase(c, GameResults.WrongReject, _rejectRound)
                                    ?? FindCase(c, GameResults.WrongReject, -1);
            PlayThen(step, RearmForRejectRetry);
            return;
        }
        SettleCustomer(c, approved: true, wrongRejectCount: _rejectRound, forcedPass: true);
        DialogueCaseData protest = FindCase(c, GameResults.WrongReject, _rejectRound)
                                   ?? FindCase(c, GameResults.WrongReject, -1);
        PlayThen(protest, AdvanceNext);
    }

    /// <summary>정상 거절 + 재거절 손님(윤정호 등)의 '정말 거절?' 확인 팝업. 도장·대조 거절 공용. [예]=라운드 진행, [아니오]=취소.</summary>
    private void ShowGuiltyRejectConfirm(CustomerData c)
    {
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
        RejectConfirmPopup popup = FindRejectPopup();
        Debug.Log($"[VipRejectGuilty] {c.nameKr} 거절 확인 — 팝업={(popup == null ? "NULL→대사로 진행" : "표시")}");
        if (popup == null) { AdvanceGuiltyReject(c); return; }
        popup.ShowConfirm("정말 거절하시겠습니까?", "아니오", "예",
            RearmForRejectRetry, () => AdvanceGuiltyReject(c));
    }

    /// <summary>현재 손님이 '전신검사 거부 → 정상 거절 + 재거절' 대상(윤정호 등)인가. 대조 거절 트리거 게이트.</summary>
    public bool IsScanRefusalRejectActive
    {
        get
        {
            CustomerData c = Current;
            return c != null
                && c.correctResult == GameResults.Reject
                && BranchKeyResolver.UsesAccrueScale(c.characterType)
                && FindCase(c, GameResults.Reject, 1) != null;
        }
    }

    /// <summary>대조(전신검사 거부 음성 ↔ 규정 일치)로 거절을 트리거(도장과 공존, 같은 라운드 흐름).</summary>
    public void TriggerScanRefusalReject()
    {
        if (IsScanRefusalRejectActive) ShowGuiltyRejectConfirm(Current);
    }

    /// <summary>
    /// 팝업 [예](또는 팝업 미발견 폴백): 결함 VIP 거절 단계를 한 칸 올린다.
    /// 1~2회 = 그 단계 대사(심사관↔VIP 티키타카) 재생 + 재제출. 3회째 = 거절 확정(reject_correct + 적발) → 다음 손님.
    /// </summary>
    private void AdvanceGuiltyReject(CustomerData c)
    {
        _rejectRound++;
        Debug.Log($"[VipRejectGuilty] {c.nameKr} {_rejectRound}회 확정 → {(_rejectRound >= VipForceEntryRound ? "입국 거부 확정" : "재제출(티키타카)")}");
        if (_rejectRound < VipForceEntryRound)
        {
            DialogueCaseData step = FindCase(c, GameResults.Reject, _rejectRound)
                                    ?? FindCase(c, GameResults.Reject, -1);
            PlayThen(step, RearmForRejectRetry);
            return;
        }
        SettleCustomer(c, approved: false, wrongRejectCount: 0, forcedPass: false);
        DialogueCaseData last = FindCase(c, GameResults.Reject, _rejectRound)
                                ?? FindCase(c, GameResults.Reject, -1);
        PlayThen(last, AdvanceNext);
    }

    /// <summary>
    /// 뇌물 팝업 [거절한다](또는 팝업 미발견 폴백) = 정답: 밀수품 적발 거절.
    /// bribeRefuseBranchKey(기본 detect_montage_reject)로 1회 정산(정답·적발 보너스) + 거부 대사 → 다음 손님.
    /// </summary>
    private void ResolveBribeRefuse(CustomerData c)
    {
        string key = string.IsNullOrEmpty(c.bribeRefuseBranchKey)
            ? BranchKeys.DetectMontageReject : c.bribeRefuseBranchKey;
        // 밀수품 적발 거절은 결함(거부 정답) 손님이므로 doc_state=Defect 로 명시 매칭.
        BranchResult branch = BranchKeyResolver.ResolveAdvanced(DocStates.Defect, key, NullIfEmpty(c.defectVariant));
        // 적발: wasCorrect=true(오판 미집계), wasDetection=true(일자 DETECTION 보너스).
        SettleBranch(c.characterType, branch, wasCorrect: true, wasDetection: true);

        // 거부 대사: 전용 caseType(bribeRefuseCaseType) 우선, 없으면 정답 거절(정상 거절) 케이스.
        DialogueCaseData refuse = FindCaseByType(c, c.bribeRefuseCaseType)
                                  ?? FindCase(c, GameResults.Reject, -1);
        PlayThen(refuse, AdvanceNext);
    }

    /// <summary>
    /// 뇌물 팝업 [받는다] = 공범: 수락 대사 재생 후 corrupt_accept_gold(기본)로 부패 정산.
    /// 정산 시 ScoreEconomyManager 가 금괴 + 조기엔딩(#11)을 발동한다(엔딩 패널이 화면 점유).
    /// 수락 대사를 먼저 재생하고 '완료 시' 정산해, 정산이 트리거하는 엔딩 패널이 대사 위로 뜨지 않게 한다.
    /// </summary>
    private void ResolveBribeAccept(CustomerData c)
    {
        DialogueCaseData accept = FindCaseByType(c, c.bribeAcceptCaseType);
        PlayThen(accept, () => SettleBribeAccept(c));
    }

    /// <summary>[받는다] 수락 대사 종료 후 1회 정산(금괴/조기엔딩 #11). 엔딩이 진행을 정지하므로 AdvanceNext 하지 않는다.</summary>
    private void SettleBribeAccept(CustomerData c)
    {
        string key = string.IsNullOrEmpty(c.bribeAcceptBranchKey)
            ? BranchKeys.CorruptAcceptGold : c.bribeAcceptBranchKey;
        // 부패 입국: 결함 손님을 들여보냄 → doc_state=Defect, wasCorrect=false(오판), 적발 아님.
        BranchResult branch = BranchKeyResolver.ResolveAdvanced(DocStates.Defect, key, NullIfEmpty(c.defectVariant));
        SettleBranch(c.characterType, branch, wasCorrect: false);
        // 정산이 #11 조기엔딩을 트리거하면 ImmigrationManager.HaltForEnding 가 진행을 멈추고 엔딩 패널을 띄운다.
        //  엔딩이 안 떠도(테이블 미로드 등) 소프트락이 없도록 다음 손님으로 진행한다.
        if (!_ended) AdvanceNext();
    }

    private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    // 거절 확인 팝업(CoreRig). 인스펙터 배선 의존 없이 1회 탐색(비활성 포함)해 캐시. 못 찾으면 팝업 없이 폴백.
    private RejectConfirmPopup _rejectPopup;
    private bool _rejectPopupResolved;
    private RejectConfirmPopup FindRejectPopup()
    {
        if (!_rejectPopupResolved)
        {
            _rejectPopup = FindObjectOfType<RejectConfirmPopup>(true); // true = 비활성 오브젝트도 포함
            _rejectPopupResolved = true;
        }
        return _rejectPopup;
    }

    private void ShowDayComplete()
    {
        if (_customerView != null) _customerView.Hide();
        if (_documentView != null) _documentView.Clear();
        if (_dialogueView != null) _dialogueView.Hide();
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
        // 일자 종료 결과는 별도 ResultScene 으로 이동(DayCompletePanel 패널 제거).
        //  → 여기서 _dayCompleteRoot 를 활성화하지 않는다. 씬 전환은 ImmigrationManager 가 처리.
        //  (_dayCompleteRoot 필드는 Initialize/HaltForEnding/IsDayComplete 테스트 훅에서 유지 사용.)
        OnCustomerChanged?.Invoke(); // 손님 종료 → 검사기 패널/버튼 비활성화

        // 일자 보상(일급/적발/무사고/경고) 1회 정산 — OnDayCompleted 통지 '전에' 적용해
        // 모든 구독자(정산 표시 UI 등)가 확정된 잔액을 읽게 한다(점수 불변, 돈만 변동).
        var mgr = Economy;
        if (mgr != null)
        {
            mgr.SettleDay();
            _gold = mgr.Money;
            UpdateGold();
        }

        Debug.Log($"[InspectionController] {CurrentDay}일차 완료");

        // 진행 매니저(ImmigrationManager 등)에 일자 완료 통지. UI 직접 참조 없음.
        OnDayCompleted?.Invoke(CurrentDay);
    }

    /// <summary>
    /// 엔딩이 확정되면 진행 매니저(ImmigrationManager.RaiseEnding)가 호출한다.
    /// 이후 손님 진행/일자완료 패널을 막고, 이미 떠 있을 수 있는 일자완료 패널을 숨긴다
    /// → 엔딩 패널이 화면을 점유한다(조기엔딩 시 DayCompletePanel 이 대신 뜨던 문제 차단).
    /// 14일 종료 엔딩처럼 ShowDayComplete 도중 엔딩이 결정돼 패널이 잠깐 켜진 경우도 여기서 끈다.
    /// </summary>
    public void HaltForEnding()
    {
        _ended = true;
        if (_dayCompleteRoot != null) _dayCompleteRoot.SetActive(false);
        if (_judgmentPanel != null) _judgmentPanel.SetReady(false);
    }

    // ── 테스트 훅(통합 PlayMode 테스트 전용) ─────────────────────
    // 프로덕션 흐름은 JudgmentPanel.OnDecision → HandleDecision 으로 동일하게 탄다.
    // 테스트가 JudgmentPanel 없이도 판정을 주입하고 진행 상태를 관찰할 수 있게 최소 노출한다.

    /// <summary>현재 표시 중인 손님 인덱스(0-base). 손님 없으면 customers.Length.</summary>
    public int CurrentSlotIndex => _index;

    /// <summary>현재 일차 손님 수(데이터 없으면 0).</summary>
    public int CustomerCount => _data != null && _data.customers != null ? _data.customers.Length : 0;

    /// <summary>일자완료 패널이 떠 있는가(= 마지막 손님까지 끝났는가).</summary>
    public bool IsDayComplete => _dayCompleteRoot != null && _dayCompleteRoot.activeSelf;

    /// <summary>지금 판정 입력을 받을 수 있는 손님이 있는가(테스트 진행 가드).</summary>
    public bool HasActiveCustomer => Current != null;

    /// <summary>
    /// 테스트 전용: JudgmentPanel.OnDecision 과 동일한 경로로 판정을 주입한다.
    /// 실제 프로덕션 흐름(HandleDecision)을 그대로 호출하므로 분기/정산/대화 루프가 동일하게 작동한다.
    /// </summary>
    public void TestSubmitDecision(bool approve) => HandleDecision(approve);

    private static DialogueCaseData FindCaseByType(CustomerData c, string caseType)
    {
        if (c?.dialogueCases == null) return null;
        if (string.IsNullOrEmpty(caseType)) return null;
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
