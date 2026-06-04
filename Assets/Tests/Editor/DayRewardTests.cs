using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 일자 보상 정산(ScoreEconomyManager.BeginDay / SettleDay / 4인자 Settle) 검증.
/// 규약 4·5장: 일자 보상은 "돈" 전용이며 "점수"는 불변(점수 ≠ 돈 분리).
///
/// 단언값 출처 — reward 테이블(RewardTable.asset)과 코드 폴백 상수가 동일하다:
///   DAILY_BASE=+100, DETECTION=+30, PERFECT_DAY=+50, WARNING=-50, related_ending_id=17.
/// 따라서 DB 주입(InjectDatabase) 경로든 폴백 경로든 기대값이 같다. 본 테스트는
/// 기존 SettlementTests 와 동일하게 InjectDatabase()(실제 .asset DB)로 결정론적으로 돈다.
/// SettleDay()/BeginDay() 는 public 이므로 reflection 없이 직접 호출한다.
/// 프로덕션 코드는 수정하지 않는다(테스트 전용).
/// </summary>
public class DayRewardTests
{
    // reward 테이블 = 코드 폴백 상수와 일치(위 summary 참조). 임의 숫자 아님 — .asset/소스 대조값.
    private const int DAILY_BASE  = 100;
    private const int DETECTION   = 30;
    private const int PERFECT_DAY = 50;
    private const int WARNING     = -50;
    private const string WarningEndingEvent =
        ScoreEconomyManager.WarningEndingTriggerPrefix + "17"; // related_ending_id=17

    private GameDatabase _db;
    private ScoreEconomyManager _mgr;

    [SetUp]
    public void SetUp()
    {
        _db = TestHelpers.InjectDatabase();
        _mgr = TestHelpers.FreshManager();
    }

    [TearDown]
    public void TearDown()
    {
        TestHelpers.DestroyManager(_mgr);
        GameDatabaseProvider.Reset();
    }

    // ── 헬퍼: 당일 적발(정상 거절 정답) 1건을 4인자 Settle 로 기록 ──
    //  결함 손님을 올바로 거부 = reject_correct(정답) + wasDetection=true → _dayDetectionCount++.
    private void RecordDetection()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Reject, playerApproved: false,
            wrongRejectCount: 0, forcedPass: false, CharacterTypes.General, defectVariant: null);
        _mgr.Settle(CharacterTypes.General, b, wasCorrect: true, wasDetection: true);
    }

    // ── 헬퍼: 당일 오판 1건 기록(정상 손님 잘못 거부 = reject_wrong, 적발 아님) ──
    private void RecordWrong()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Approve, playerApproved: false,
            wrongRejectCount: 0, forcedPass: false, CharacterTypes.General, defectVariant: null);
        _mgr.Settle(CharacterTypes.General, b, wasCorrect: false, wasDetection: false);
    }

    // ── 1. 오판 0 + 적발 0 → DAILY_BASE + PERFECT_DAY ──────────
    [Test]
    public void SettleDay_NoWrongNoDetection_AddsDailyBasePlusPerfect()
    {
        _mgr.BeginDay();
        int money0 = _mgr.Money;
        int delta = _mgr.SettleDay();

        Assert.AreEqual(DAILY_BASE + PERFECT_DAY, delta, "일급+퍼펙트 데이");
        Assert.AreEqual(DAILY_BASE + PERFECT_DAY, _mgr.Money - money0, "Money 증가분");
        Assert.AreEqual(0, _mgr.GetEventCount(WarningEndingEvent), "경고 엔딩 미발동");
    }

    // ── 2. 적발 N건 → += DETECTION × N (오판 0 이므로 PERFECT_DAY 도 포함) ──
    [Test]
    public void SettleDay_NDetections_AddsDetectionTimesN()
    {
        const int N = 3;
        _mgr.BeginDay();
        for (int i = 0; i < N; i++) RecordDetection();

        Assert.AreEqual(N, _mgr.DayDetectionCount, "당일 적발 집계");
        int money0 = _mgr.Money;
        int delta = _mgr.SettleDay();

        // 적발은 모두 정답이므로 당일 오판 0 → PERFECT_DAY 도 적용된다.
        int expected = DAILY_BASE + DETECTION * N + PERFECT_DAY;
        Assert.AreEqual(expected, delta, "일급 + 적발×N + 퍼펙트");
        Assert.AreEqual(expected, _mgr.Money - money0, "Money 증가분");
    }

    // ── 3. 오판 4건+ → WARNING 차감 + 엔딩 #17 트리거 + PERFECT 미적용 ──
    [Test]
    public void SettleDay_FourWrongs_AppliesWarningAndTriggersEnding_NoPerfect()
    {
        _mgr.BeginDay();
        for (int i = 0; i < 4; i++) RecordWrong();

        Assert.AreEqual(4, _mgr.DayWrongCount, "당일 오판 집계");
        int money0 = _mgr.Money;
        int delta = _mgr.SettleDay();

        // 적발 0 → DETECTION 없음, 오판 4 → PERFECT 없음 + WARNING 차감.
        int expected = DAILY_BASE + WARNING;
        Assert.AreEqual(expected, delta, "일급 + 경고차감 (퍼펙트 미적용)");
        Assert.AreEqual(expected, _mgr.Money - money0, "Money 변화분");
        Assert.AreEqual(1, _mgr.GetEventCount(WarningEndingEvent),
            "경고 누적 → 엔딩 #17(WARNING_ENDING:17) 1회 트리거");
    }

    // ── 3b. 경고 + 적발이 공존하면 DETECTION 은 합산되되 PERFECT 는 없다 ──
    [Test]
    public void SettleDay_FourWrongsPlusDetections_WarningAndDetection_NoPerfect()
    {
        _mgr.BeginDay();
        RecordDetection();          // 적발 1
        RecordDetection();          // 적발 2
        for (int i = 0; i < 4; i++) RecordWrong(); // 오판 4

        int money0 = _mgr.Money;
        int delta = _mgr.SettleDay();

        int expected = DAILY_BASE + DETECTION * 2 + WARNING; // 퍼펙트 없음
        Assert.AreEqual(expected, delta);
        Assert.AreEqual(expected, _mgr.Money - money0);
        Assert.AreEqual(1, _mgr.GetEventCount(WarningEndingEvent));
    }

    // ── 4. 오판 1~3건(경고 미만) → DAILY_BASE 만(퍼펙트·경고 모두 없음) ──
    [Test]
    public void SettleDay_OneToThreeWrongs_DailyBaseOnly_NoPerfectNoWarning()
    {
        for (int w = 1; w <= 3; w++)
        {
            var mgr = TestHelpers.FreshManager();
            mgr.BeginDay();
            for (int i = 0; i < w; i++)
            {
                var b = BranchKeyResolver.Resolve(GameResults.Approve, playerApproved: false,
                    0, false, CharacterTypes.General, null);
                mgr.Settle(CharacterTypes.General, b, wasCorrect: false, wasDetection: false);
            }
            int money0 = mgr.Money;
            int delta = mgr.SettleDay();

            Assert.AreEqual(DAILY_BASE, delta, $"오판 {w}건 → 일급만(퍼펙트X·경고X)");
            Assert.AreEqual(DAILY_BASE, mgr.Money - money0, $"오판 {w}건 Money 증가분");
            Assert.AreEqual(0, mgr.GetEventCount(WarningEndingEvent), $"오판 {w}건 → 경고 엔딩 미발동");
            TestHelpers.DestroyManager(mgr);
        }
    }

    // ── 5. 점수(Score)는 일자 보상으로 변하지 않음(돈/점수 분리) ──
    [Test]
    public void SettleDay_DoesNotChangeScore()
    {
        _mgr.BeginDay();
        RecordDetection();   // 손님 정산이 점수를 올릴 수 있으므로 그 이후를 기준점으로 잡는다.
        RecordWrong();
        int scoreBeforeDaySettle = _mgr.Score;
        int moneyBeforeDaySettle = _mgr.Money;

        int delta = _mgr.SettleDay();

        Assert.AreEqual(scoreBeforeDaySettle, _mgr.Score,
            "SettleDay 는 점수를 절대 변경하지 않는다(돈 전용).");
        Assert.AreNotEqual(0, delta, "그래도 돈은 변했어야 한다(채널 분리 확인).");
        Assert.AreNotEqual(moneyBeforeDaySettle, _mgr.Money, "Money 는 변했다.");
    }

    // ── 6. 결정론: 같은 시퀀스 2회 → 돈/점수/엔딩카운트 동일 ────
    [Test]
    public void SettleDay_Determinism_SameSequenceSameResult()
    {
        (int money, int score, int evt) Run()
        {
            var mgr = TestHelpers.FreshManager();
            // day1
            mgr.BeginDay();
            {
                var d = BranchKeyResolver.Resolve(GameResults.Reject, false, 0, false, CharacterTypes.General, null);
                mgr.Settle(CharacterTypes.General, d, true, true); // 적발 1
            }
            mgr.SettleDay();
            // day2 (오판 4 → 경고)
            mgr.BeginDay();
            for (int i = 0; i < 4; i++)
            {
                var d = BranchKeyResolver.Resolve(GameResults.Approve, false, 0, false, CharacterTypes.General, null);
                mgr.Settle(CharacterTypes.General, d, false, false);
            }
            mgr.SettleDay();

            var r = (mgr.Money, mgr.Score, mgr.GetEventCount(WarningEndingEvent));
            TestHelpers.DestroyManager(mgr);
            return r;
        }

        var a = Run();
        var b = Run();
        Assert.AreEqual(a, b, "같은 일자 시퀀스 2회 → money/score/경고엔딩카운트 완전 동일(결정론)");
    }

    // ── 7. BeginDay 가 당일 집계를 리셋(이전 일자 누설 없음) ────
    [Test]
    public void BeginDay_ResetsDailyTallies_NoLeakAcrossDays()
    {
        // day1: 적발 2 + 오판 4 를 쌓고 정산.
        _mgr.BeginDay();
        RecordDetection();
        RecordDetection();
        for (int i = 0; i < 4; i++) RecordWrong();
        Assert.AreEqual(2, _mgr.DayDetectionCount);
        Assert.AreEqual(4, _mgr.DayWrongCount);
        _mgr.SettleDay();

        // SettleDay 자체가 리셋하지만, BeginDay 가 명시적으로 0으로 만드는지도 확인.
        _mgr.BeginDay();
        Assert.AreEqual(0, _mgr.DayDetectionCount, "BeginDay 후 적발 집계 0");
        Assert.AreEqual(0, _mgr.DayWrongCount, "BeginDay 후 오판 집계 0");

        // day2: 아무 사건 없이 정산 → 이전 일자 적발/오판이 누설되지 않아야 함.
        int money0 = _mgr.Money;
        int delta = _mgr.SettleDay();
        Assert.AreEqual(DAILY_BASE + PERFECT_DAY, delta,
            "이전 일자 적발/오판 누설 없이 깨끗한 day2 = 일급+퍼펙트");
        Assert.AreEqual(DAILY_BASE + PERFECT_DAY, _mgr.Money - money0);
    }
}
