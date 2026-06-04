using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 「여권 주세요」 점수·경제·호칭·아이템·엔딩 카운터의 단일 정산 허브(싱글톤, 씬 전역 유지).
///
/// 규약 4장 — 점수 ≠ 돈 분리:
///  - 점수(엔딩용): character_score.score 를 branch_key 로 조회해 누적. A안 = 고정 감점(데이터 음수 그대로).
///  - 돈(상점용):  character_payout.payout 을 조회해 누적. B안 = 정상 손님 오거부 시 받을 돈의 절반 벌금(payout 음수, 데이터 그대로).
///
/// UI 를 직접 참조하지 않는다. 상태 변화는 이벤트로만 통지한다(규약 5장).
/// 결정론: 외부 입력(branch_key)만으로 정산하므로 같은 입력 → 같은 결과.
/// </summary>
public sealed class ScoreEconomyManager : MonoBehaviour
{
    public static ScoreEconomyManager Instance { get; private set; }

    // ── 코드 폴백 상수(ScoreModelTable/RewardTable 미임포트 시) ───
    //  data-tools 가 score_model/reward/character 테이블을 채우면 그 값이 우선한다.
    //  테이블이 비어 있을 때만 이 상수로 폴백(동작 동일·데이터 주도).
    private const int FallbackJudgeCorrect = 10;  // 정답(JUDGE_CORRECT)
    private const int FallbackJudgeWrong   = -15; // 오판(JUDGE_WRONG)
    private const int FallbackDailyBase    = 100; // 일급(DAILY_BASE)
    private const int FallbackDetection    = 30;  // 적발 보너스(DETECTION)
    private const int FallbackPerfectDay   = 50;  // 무사고 보너스(PERFECT_DAY)
    private const int FallbackWarning      = -50; // 경고 벌금(WARNING)
    private const int FallbackWarningEnding = 17; // 경고 누적 → 엔딩 #17

    /// <summary>과반수 오판 임계치(이상이면 WARNING 차감 + 엔딩 #17 트리거). reward.trigger_condition "4건+".</summary>
    private const int WarningWrongThreshold = 4;

    /// <summary>WARNING 엔딩 트리거 id 접두(진행 매니저가 이걸 보고 ending_id 로 엔딩 해석). 예: "WARNING_ENDING:17".</summary>
    public const string WarningEndingTriggerPrefix = "WARNING_ENDING:";

    // ── 테이블 우선 조회(없으면 코드 폴백) ─────────────────────
    /// <summary>score_model 의 item/code 값(우선) → 없으면 fallback. 점수 종류 데이터 주도화.</summary>
    private int ScoreModelInt(string code, int fallback)
        => Db != null && Db.scoreModel != null ? Db.scoreModel.GetInt(code, fallback) : fallback;

    /// <summary>reward 의 trigger_type amount(우선) → 없으면 score_model → 없으면 fallback(돈 보상 단일 소스).</summary>
    private int RewardAmount(string triggerType, int fallback)
    {
        if (Db != null && Db.reward != null)
        {
            var row = Db.reward.FindByTrigger(triggerType);
            if (row != null)
            {
                var v = row.Get("amount");
                if (!string.IsNullOrWhiteSpace(v))
                {
                    v = v.Replace("+", "").Trim();
                    if (int.TryParse(v, out int n)) return n;
                }
            }
        }
        return ScoreModelInt(triggerType, fallback);
    }

    // ── 누적 상태(세이브 대상) ─────────────────────────────────
    /// <summary>누적 점수(엔딩 결정용).</summary>
    public int Score { get; private set; }
    /// <summary>누적 돈(상점용).</summary>
    public int Money { get; private set; }
    /// <summary>판정 정답 수 / 전체 판정 수(정확도·공식용).</summary>
    public int CorrectCount { get; private set; }
    public int JudgedCount { get; private set; }

    /// <summary>획득 호칭(중복 없이).</summary>
    public IReadOnlyCollection<string> Titles => _titles;
    private readonly HashSet<string> _titles = new HashSet<string>();

    /// <summary>보유 아이템(획득 순, 중복 허용 — 같은 마법아이템 여러 개 가능).</summary>
    public IReadOnlyList<string> Items => _items;
    private readonly List<string> _items = new List<string>();

    /// <summary>조기/누적 엔딩 카운터. event_id("#15"/"#16"...) → 누적 횟수.</summary>
    private readonly Dictionary<string, int> _eventCounters = new Dictionary<string, int>();

    // ── 당일 집계(일자 보상용 — 세이브 불필요, 하루 내 휘발) ───
    /// <summary>당일 오판 건수(PERFECT_DAY/WARNING 판정용). BeginDay 에서 0으로 리셋.</summary>
    public int DayWrongCount => _dayWrongCount;
    private int _dayWrongCount;
    /// <summary>당일 적발 건수(DETECTION 보너스 합산용).</summary>
    public int DayDetectionCount => _dayDetectionCount;
    private int _dayDetectionCount;

    /// <summary>현자 입국 누적(8회 도달 시 '해탈한 자' 호칭).</summary>
    public int SageApproveCount { get; private set; }
    private const int SageEnlightenThreshold = 8;
    private const string SageEnlightenTitle = "해탈한 자";

    // ── 이벤트(UI/엔딩 구독) ───────────────────────────────────
    /// <summary>점수가 바뀌면(누적 점수, 이번 델타).</summary>
    public event Action<int, int> OnScoreChanged;
    /// <summary>돈이 바뀌면(누적 돈, 이번 델타).</summary>
    public event Action<int, int> OnMoneyChanged;
    /// <summary>호칭 신규 획득(호칭명).</summary>
    public event Action<string> OnTitleEarned;
    /// <summary>아이템 신규 획득(아이템명).</summary>
    public event Action<string> OnItemEarned;
    /// <summary>조기/즉시 엔딩 트리거 발동(event_id). 진행 매니저가 구독해 즉시 엔딩 진입.</summary>
    public event Action<string> OnEarlyEndingTriggered;
    /// <summary>한 손님 정산 완료(정답 여부). UI 피드백용.</summary>
    public event Action<bool> OnVerdictResolved;

    /// <summary>
    /// '다음 오판 1회 무효'(커피) 소비 훅. ShopService 가 등록한다. 오판 정산 직전 호출해
    /// true 면 그 오판의 점수/벌금을 적용하지 않는다(당일 오판 카운트에도 미반영). null 이면 무시.
    /// </summary>
    public Func<bool> MistakeForgiveHook { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        GameProgressSave.LoadInto(this); // 이어하기: 저장값 복원
    }

    private GameDatabase Db => GameDatabaseProvider.Database;

    // ── 정산 진입점 ────────────────────────────────────────────

    /// <summary>
    /// 한 손님 확정 시 1회 호출(루프 중 중복 금지). branch_key 로 점수표/금액표를 조회해
    /// 점수·돈·호칭·아이템·조기엔딩을 한 번에 정산한다.
    /// </summary>
    /// <param name="characterType">손님 character_type.</param>
    /// <param name="branch">상태머신이 결정한 분기(doc_state/branch_key/variant/visit_round).</param>
    /// <param name="wasCorrect">이번 판정이 정답인가(정확도 집계용).</param>
    public void Settle(string characterType, BranchResult branch, bool wasCorrect)
        => Settle(characterType, branch, wasCorrect, false);

    /// <summary>
    /// 정산(적발 보너스 동반판). 결함 손님을 올바로 적발(거부)했으면 wasDetection=true 로 호출해
    /// 당일 적발 카운트를 올린다(DETECTION 보너스는 일자 정산 시 합산).
    /// </summary>
    /// <param name="wasDetection">이번 확정이 위조/밀수/지명수배 적발(정상 거절 정답)인가.</param>
    public void Settle(string characterType, BranchResult branch, bool wasCorrect, bool wasDetection)
    {
        JudgedCount++;

        // 오판이면 '오판 1회 무효'(커피) 소비 시도 — 성공하면 이번 오판을 정상 정산처럼 면제한다
        //  (점수/벌금 미적용 + 당일 오판 카운트 미반영). 정확도(엔딩)에는 정직하게 오판으로 남긴다.
        bool forgiven = !wasCorrect && MistakeForgiveHook != null && MistakeForgiveHook();

        if (wasCorrect) CorrectCount++;
        else if (!forgiven) _dayWrongCount++;
        if (wasDetection) _dayDetectionCount++;

        int scoreDelta = LookupScore(characterType, branch, wasCorrect, out string scoreEvent, out string title);
        int moneyDelta = LookupPayout(characterType, branch, out string itemDrop, out string payoutEvent);

        // 면제 시 음수(감점/벌금)만 0으로 깎는다(보상은 그대로 유지 — 보통 오판이라 보상은 없다).
        if (forgiven)
        {
            if (scoreDelta < 0) scoreDelta = 0;
            if (moneyDelta < 0) moneyDelta = 0;
        }

        ApplyScore(scoreDelta);
        ApplyMoney(moneyDelta);

        if (!string.IsNullOrEmpty(title)) EarnTitle(title);
        if (!string.IsNullOrEmpty(itemDrop)) EarnItem(characterType, branch, itemDrop);

        // 현자 누적(8회 → 해탈한 자).
        if (characterType == CharacterTypes.Sage && branch.branchKey == BranchKeys.ApproveSageItem)
        {
            SageApproveCount++;
            if (SageApproveCount >= SageEnlightenThreshold) EarnTitle(SageEnlightenTitle);
        }

        // 조기/누적 엔딩 트리거: 두 테이블(score.event_id / payout.early_ending)의 합집합.
        //  (#15/#16 은 score 표에만, 일부 #11 은 payout 표에만 있어 양쪽을 모두 본다.)
        string evt = !string.IsNullOrEmpty(scoreEvent) ? scoreEvent : payoutEvent;
        if (!string.IsNullOrEmpty(evt)) TriggerEvent(evt);

        OnVerdictResolved?.Invoke(wasCorrect);
        GameProgressSave.SaveFrom(this);
    }

    // ── 일자(day) 단위 보상 — reward 테이블(DAILY_BASE/PERFECT_DAY/WARNING/DETECTION) ──

    /// <summary>일자 보상이 적용되면(일급/적발/무사고/경고 합산 델타). 정산 표시 UI 가 구독.</summary>
    public event Action<int> OnDaySettled;

    /// <summary>하루 시작 시 당일 집계(오판/적발)를 리셋한다. 진행 매니저가 일차 시작 시 호출.</summary>
    public void BeginDay()
    {
        _dayWrongCount = 0;
        _dayDetectionCount = 0;
    }

    /// <summary>
    /// 하루 종료 시 1회 호출(per-customer 정산과 별도, 중복 금지). reward 테이블 기준으로
    /// 일급(DAILY_BASE) + 적발 보너스(DETECTION×건수) + 무사고(PERFECT_DAY, 당일 오판 0) 또는
    /// 경고(WARNING, 오판 4건+ → 엔딩 #17 트리거)를 **돈**에 합산한다(점수 불변 — 규약 5장 분리).
    /// </summary>
    /// <returns>이번 일자 보상 합계 델타(돈).</returns>
    public int SettleDay()
    {
        int delta = 0;

        // 일급(고정 지급).
        delta += RewardAmount("DAILY_BASE", FallbackDailyBase);

        // 적발 보너스(당일 적발 건수 × DETECTION).
        if (_dayDetectionCount > 0)
            delta += _dayDetectionCount * RewardAmount("DETECTION", FallbackDetection);

        // 무사고 vs 경고: 둘은 배타적(당일 오판 수로 분기).
        if (_dayWrongCount <= 0)
        {
            delta += RewardAmount("PERFECT_DAY", FallbackPerfectDay);
        }
        else if (_dayWrongCount >= WarningWrongThreshold)
        {
            delta += RewardAmount("WARNING", FallbackWarning); // 음수(차감)
            // 경고 누적 → 관련 엔딩 트리거(reward.related_ending_id, 기본 17).
            //  TriggerEvent 는 카운터 증가 + OnEarlyEndingTriggered 통지를 한다. 진행 매니저가
            //  이 id 를 EndingResolver 로 해석해(해당 행 존재 시) 엔딩 진입 여부를 결정한다.
            string endingId = Db != null && Db.reward != null
                ? Db.reward.GetRelatedEndingId("WARNING") : string.Empty;
            if (string.IsNullOrEmpty(endingId)) endingId = FallbackWarningEnding.ToString();
            TriggerEvent(WarningEndingTriggerPrefix + endingId);
        }

        if (delta != 0) ApplyMoney(delta);
        OnDaySettled?.Invoke(delta);

        // 다음 날을 위한 집계 리셋(BeginDay 가 또 리셋해도 무해 — 멱등).
        _dayWrongCount = 0;
        _dayDetectionCount = 0;

        GameProgressSave.SaveFrom(this);
        return delta;
    }

    // ── 테이블 조회 ────────────────────────────────────────────

    /// <summary>
    /// character_score 조회: score(A안 고정 감점 포함) + event_id(#11~#16) + title(호칭).
    /// 점수 행이 없으면 코드 폴백(정답/오판 평면 점수). score 가 비고 score_range/이벤트만 있으면 0점
    /// (범위 점수는 3단계 UI 미니게임이 확정).
    /// </summary>
    private int LookupScore(string characterType, BranchResult b, bool wasCorrect, out string eventId, out string title)
    {
        eventId = null; title = null;
        var table = Db != null ? Db.characterScore : null;
        if (table != null)
        {
            // 1차: 변이까지 매칭 → 2차: 변이 무시(데이터에 변이 행이 하나뿐이거나 baked가 변이를 모를 때).
            DataRow row = table.Find(characterType, b.docState, b.branchKey, b.defectVariant, b.visitRound)
                          ?? table.Find(characterType, b.docState, b.branchKey, null, b.visitRound);
            if (row != null)
            {
                eventId = NullIfEmpty(row.Get("event_id"));
                title = NullIfEmpty(row.Get("title"));
                if (int.TryParse(row.Get("score"), out int s)) return s;
                return 0; // 범위/이벤트 행
            }
        }
        return wasCorrect
            ? ScoreModelInt("JUDGE_CORRECT", FallbackJudgeCorrect)
            : ScoreModelInt("JUDGE_WRONG", FallbackJudgeWrong);
    }

    /// <summary>
    /// character_payout 조회: payout(B안 절반 벌금 포함) + item_drop + early_ending.
    /// 행이 없으면 코드 폴백(정답=일급, 오판=−절반).
    /// </summary>
    private int LookupPayout(string characterType, BranchResult b, out string itemDrop, out string earlyEnding)
    {
        itemDrop = null; earlyEnding = null;
        var pTable = Db != null ? Db.characterPayout : null;
        if (pTable != null)
        {
            DataRow row = pTable.Find(characterType, b.docState, b.branchKey, b.defectVariant, b.visitRound)
                          ?? pTable.Find(characterType, b.docState, b.branchKey, null, b.visitRound);
            if (row != null)
            {
                itemDrop = NullIfEmpty(row.Get("item_drop"));
                earlyEnding = NullIfEmpty(row.Get("early_ending"));
                if (int.TryParse(row.Get("payout"), out int p)) return p;
            }
        }
        // 폴백: 정답이면 일급, 오판이면 -절반(테이블 없을 때만).
        bool correct = b.branchKey == BranchKeys.ApproveCorrect || b.branchKey == BranchKeys.RejectCorrect
                       || b.branchKey == BranchKeys.ApproveImmediate;
        int dailyBase = RewardAmount("DAILY_BASE", FallbackDailyBase);
        return correct ? dailyBase : -(dailyBase / 2);
    }

    // ── 적용 + 통지 ────────────────────────────────────────────

    private void ApplyScore(int delta)
    {
        if (delta == 0) { OnScoreChanged?.Invoke(Score, 0); return; }
        Score += delta;
        OnScoreChanged?.Invoke(Score, delta);
    }

    private void ApplyMoney(int delta)
    {
        Money += delta;
        OnMoneyChanged?.Invoke(Money, delta);
    }

    private void EarnTitle(string title)
    {
        if (_titles.Add(title)) OnTitleEarned?.Invoke(title);
    }

    /// <summary>아이템 부여. 현자 '마법 아이템'은 defect_variant 로 8종 중 1종을 1:1 결정.</summary>
    private void EarnItem(string characterType, BranchResult b, string itemDrop)
    {
        string resolved = itemDrop;
        if (characterType == CharacterTypes.Sage)
            resolved = SageItemName(b.defectVariant) ?? itemDrop;
        _items.Add(resolved);
        OnItemEarned?.Invoke(resolved);
    }

    /// <summary>조기/누적 엔딩 이벤트 카운트 +1 후 통지.</summary>
    public void TriggerEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;
        _eventCounters.TryGetValue(eventId, out int n);
        _eventCounters[eventId] = n + 1;
        OnEarlyEndingTriggered?.Invoke(eventId);
    }

    /// <summary>이벤트(event_id) 누적 횟수.</summary>
    public int GetEventCount(string eventId)
        => _eventCounters.TryGetValue(eventId, out int n) ? n : 0;

    /// <summary>정확도(0~1). 판정 없으면 0.</summary>
    public float Accuracy => JudgedCount > 0 ? (float)CorrectCount / JudgedCount : 0f;

    // ── 상점/직접 조정 API(3단계 UI 가 호출) ───────────────────

    /// <summary>상점 구매 등으로 돈을 직접 차감/가산(양수=수입, 음수=지출).</summary>
    public bool TrySpend(int amount)
    {
        if (amount > Money) return false;
        ApplyMoney(-amount);
        GameProgressSave.SaveFrom(this);
        return true;
    }

    public void AddMoney(int amount) { ApplyMoney(amount); GameProgressSave.SaveFrom(this); }

    // ── 세이브 직렬화 헬퍼(GameProgressSave 전용) ──────────────

    internal void RestoreState(int score, int money, int correct, int judged,
        IEnumerable<string> titles, IEnumerable<string> items,
        IDictionary<string, int> events, int sageCount)
    {
        Score = score; Money = money; CorrectCount = correct; JudgedCount = judged;
        _titles.Clear(); if (titles != null) foreach (var t in titles) _titles.Add(t);
        _items.Clear(); if (items != null) _items.AddRange(items);
        _eventCounters.Clear(); if (events != null) foreach (var kv in events) _eventCounters[kv.Key] = kv.Value;
        SageApproveCount = sageCount;
    }

    internal IReadOnlyDictionary<string, int> EventCounters => _eventCounters;

    /// <summary>새 게임 시작 시 누적 초기화.</summary>
    public void ResetAll()
    {
        Score = 0; Money = 0; CorrectCount = 0; JudgedCount = 0; SageApproveCount = 0;
        _titles.Clear(); _items.Clear(); _eventCounters.Clear();
        OnScoreChanged?.Invoke(0, 0);
        OnMoneyChanged?.Invoke(0, 0);
    }

    // ── 유틸 ───────────────────────────────────────────────────

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>현자 가치관조합(defect_variant) → 마법 아이템 8종 1:1 매핑(점수표 note 근거).</summary>
    private static string SageItemName(string variant) => variant switch
    {
        "물질+성취+세계"   => "마법의 수정구슬",
        "물질+성취+우리나라" => "마법의 구슬",
        "물질+사랑+가족"   => "마법의 손목시계",
        "물질+사랑+나"     => "마법의 거울",
        "정신+자유+세계"   => "마법의 손수건",
        "정신+자유+우리나라" => "마법의 주머니",
        "정신+돈+가족"     => "마법의 와인",
        "정신+돈+나"       => "마법의 지갑",
        _ => null,
    };
}
