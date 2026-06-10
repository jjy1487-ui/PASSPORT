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

    private const int MaxWrongReject = 3; // 오거부 3회 후 강제 통과

    private int _gold; // HUD 표시 캐시(실제 누적은 ScoreEconomyManager.Money)

    // 정산 허브 참조. 진행 매니저가 명시 주입(SetEconomy)하면 그 인스턴스를 우선 사용하고,
    // 없으면 전역 싱글톤(ScoreEconomyManager.Instance)으로 폴백한다.
    // (씬 로드 타이밍/테스트 주입 순서로 Instance 해석이 흔들려도 정산이 누락되지 않게 한다.)
    private ScoreEconomyManager _economy;
    private ScoreEconomyManager Economy => _economy != null ? _economy : ScoreEconomyManager.Instance;

    /// <summary>진행 매니저가 정산 허브를 주입한다(없으면 전역 싱글톤 폴백). UI 직접 참조 아님.</summary>
    public void SetEconomy(ScoreEconomyManager economy) => _economy = economy;

    private Day1Data _data;
    private int _index;
    private int _wrongRejectCount;
    private bool _customerSettled; // 현재 손님 확정 정산 1회 가드(중복 정산·이중 진행 방지)
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

    /// <summary>현재 손님의 X-ray 검사 결과(없으면 null). 검사기 UI 표시·대조용 — 판정에는 영향 없음.</summary>
    public ScanData CurrentXray => Current?.xray;

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

    /// <summary>현재 손님의 defect_variant(없으면 null). 고급 분기 키 산출용 — 판정에는 영향 없음.</summary>
    public string CurrentDefectVariant => Current?.defectVariant;

    /// <summary>현재 손님의 정답 판정 상태(정상 손님이면 normal, 불량이면 defect). 고급 분기 doc_state 산출용.</summary>
    public string CurrentDocState =>
        Current != null ? (Current.correctResult == GameResults.Approve ? DocStates.Normal : DocStates.Defect) : null;

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
    }

    /// <summary>데이터를 받아 해당 일차를 시작한다(첫날: 골드 초기화).</summary>
    public void Initialize(Day1Data data) => Initialize(data, true);

    /// <summary>
    /// 데이터를 받아 해당 일차를 시작한다.
    /// resetGold=false 면 누적 골드를 유지(2일차 이후 재초기화용).
    /// </summary>
    public void Initialize(Day1Data data, bool resetGold)
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
        _wrongRejectCount = 0;
        _customerSettled = false;
        _dialogueLog.Clear();
        _dialogueLines.Clear();
        _requestableCase = null;
        _dialoguePlaying = false;
        RaiseRequestable();
        OnCustomerChanged?.Invoke();

        if (_customerView != null) _customerView.Show(c, FacePhotoRef(c));
        if (_documentView != null) _documentView.Show(c.documents);
        if (_slotCounterText != null) _slotCounterText.text = $"{index + 1} / {_data.customers.Length}";
        if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(false);

        // 입장 대사 후 판정 활성화
        DialogueCaseData entry = FindCaseByType(c, CaseTypes.Entry);
        if (entry != null)
        {
            PlayThen(entry, EnableJudgment);
        }
        else
        {
            EnableJudgment();
        }
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

    private void UpdateGold()
    {
        if (_goldText != null) _goldText.text = _gold.ToString();
    }

    private void HandleDecision(bool approve)
    {
        CustomerData c = Current;
        if (c == null) return;

        // 여권 종이 위에 도장 자국
        if (_documentView != null) _documentView.StampPrimary(approve);

        bool shouldApprove = c.correctResult == GameResults.Approve;

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
            DialogueCaseData guided = FindCaseByType(c, c.rejectGuidedCaseType)
                                      ?? FindCase(c, GameResults.Reject, -1);
            PlayThen(guided, AdvanceNext);
            return;
        }

        if (approve == shouldApprove)
        {
            // 정답: 정상 승인 / 정상 거절. 확정 → 정산(점수≠돈, 캐릭터별 테이블).
            //  정상 손님을 (재거절 끝에) 승인한 경우 wrongRejectCount 가 분기에 반영된다.
            SettleCustomer(c, approve, _wrongRejectCount, forcedPass: false);
            DialogueCaseData ok = FindCase(c, c.correctResult, -1);
            PlayThen(ok, AdvanceNext);
        }
        else if (!approve)
        {
            // 정상 손님을 거부(오거부). 3회 항의→강제통과 루프는 연예인/정치인(클라우트로 밀어붙이는 VIP)만.
            // 일반 손님은 즉시 오판 확정(페널티) — 거부하면 그대로 돌려보낸다(되돌림/강제통과 없음).
            // (고급 분기 손님은 위 if-체인 앞에서 이미 인터셉트되어 여기 도달하지 않는다.)
            bool usesRejectProtest = c.characterType == CharacterTypes.Celebrity
                                  || c.characterType == CharacterTypes.Politician;
            if (!usesRejectProtest)
            {
                SettleCustomer(c, approve, _wrongRejectCount, forcedPass: false); // 오거부 페널티 확정
                SpawnWrongRejectNotice(c); // 정상 서류를 거부 → 오류 고지서로 피드백
                DialogueCaseData rejectFinal = new DialogueCaseData
                {
                    caseType = "오거부 확정", gameResult = "-", rejectCount = 0,
                    lines = new[] { new DialogueLineData { order = 0, speaker = "심사관", text = "입국이 거부되었습니다." } },
                };
                PlayThen(rejectFinal, AdvanceNext);
            }
            else
            {
                // VIP(연예인/정치인): 1→3단계 항의 연출, 3회 후 강제 통과.
                _wrongRejectCount++;
                DialogueCaseData wrong = FindCase(c, GameResults.WrongReject, _wrongRejectCount);
                if (_wrongRejectCount >= MaxWrongReject || wrong == null)
                {
                    // 강제 통과 = 정정 입국. 확정 시점 1회만 정산(루프 중 중복 금지).
                    SettleCustomer(c, approved: true, _wrongRejectCount, forcedPass: true);
                    PlayThen(wrong, AdvanceNext); // 강제 통과
                }
                else
                {
                    // 같은 손님 재시도 허용(도장 자국 지움). 아직 미확정 → 정산하지 않는다.
                    PlayThen(wrong, () =>
                    {
                        if (_documentView != null) _documentView.ClearStamps();
                        if (_judgmentPanel != null) _judgmentPanel.ResetForNextCustomer(true);
                    });
                }
            }
        }
        else
        {
            // 오허가(거부해야 하는데 승인): 확정 → 오판 정산.
            SettleCustomer(c, approve, _wrongRejectCount, forcedPass: false);
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

        // 첫 결함 서류를 찾아, 사유 문구는 엑셀 inspection_notice 시트에서 가져온다(데이터 주도).
        DocumentData defectDoc = null;
        foreach (DocumentData d in c.documents)
        {
            if (d == null) continue;
            bool isDefect = (!string.IsNullOrEmpty(d.variant) && d.variant.Contains("비정상"))
                         || (!string.IsNullOrEmpty(d.violationField) && d.violationField != "없음");
            if (isDefect) { defectDoc = d; break; }
        }
        string reason = defectDoc != null
            ? NoticeReason(defectDoc.documentType, defectDoc.violationField)
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
