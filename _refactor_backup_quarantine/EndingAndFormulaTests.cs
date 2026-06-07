using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 엔딩 구간/조기/누적 결정, 점수 공식 앵커, 점수/돈 분리(상수 출처) 검증.
/// </summary>
public class EndingAndFormulaTests
{
    [SetUp] public void SetUp() { TestHelpers.InjectDatabase(); }
    [TearDown] public void TearDown() { GameDatabaseProvider.Reset(); }

    // ── 점수 구간 엔딩(EndingTable score_min~max) ──────────────

    [Test]
    public void Ending_Score245_IsGoodEmployee_200to299()
    {
        // 재산정 앵커(옵션1): 70% 정확도 → +82~+245 → 우수 사원[80,249].
        var e = EndingResolver.ResolveByScore(245);
        Assert.IsTrue(e.IsValid);
        Assert.AreEqual("우수 사원", e.endingName,
            $"245점은 우수 사원[80,249] 구간이어야 함. 실제: {e.endingName} ({e.endingId})");
    }

    [Test]
    public void Ending_Score550_IsLegend_500plus()
    {
        // 규약 앵커: 100% 정확도 → 전설의 검문관(500+).
        var e = EndingResolver.ResolveByScore(550);
        Assert.AreEqual("전설의 검문관", e.endingName,
            $"550점은 전설(500~600). 실제: {e.endingName} ({e.endingId})");
    }

    [Test]
    public void Ending_Score0_IsNeutralBand()
    {
        // 60% → 0점 → '나쁘진 않았어'(-49~99) 구간.
        var e = EndingResolver.ResolveByScore(0);
        Assert.AreEqual("나쁘진 않았어", e.endingName, $"0점 구간. 실제: {e.endingName}");
    }

    [Test]
    public void Ending_ScoreBoundaries_Exact()
    {
        // 재산정된 구간(밸런스 옵션1, 260602): 우수 사원 [80,249] / 만인의 귀감 [250,399] / 평범한 검문관 [1,79].
        Assert.AreEqual("우수 사원", EndingResolver.ResolveByScore(80).endingName, "하한 경계 80");
        Assert.AreEqual("우수 사원", EndingResolver.ResolveByScore(249).endingName, "상한 경계 249");
        Assert.AreEqual("만인의 귀감", EndingResolver.ResolveByScore(250).endingName, "250은 다음 구간");
        Assert.AreEqual("평범한 검문관", EndingResolver.ResolveByScore(79).endingName, "79는 아래 구간");
    }

    // ── EndingResolver 버그 노출: 밴드 밖 점수 ─────────────────

    [Test]
    public void Ending_AboveAllBands_601_DoesNotMisfireEarlyEnding()
    {
        // EndingTable 행11~23 은 score_min/max 가 빈 문자열이라
        // FindScoreBand 가 와일드카드로 잘못 집을 수 있다(버그 가설).
        // 601점은 정상-점수 엔딩이어야지 '공범(#11)' 같은 조기엔딩이 나오면 안 된다.
        var e = EndingResolver.ResolveByScore(601);
        Assert.AreNotEqual("early", e.endingType,
            $"601점이 조기엔딩 타입으로 잘못 매칭됨(EndingResolver.FindScoreBand 버그). 실제: {e.endingName}/{e.endingType}/{e.endingId}");
        Assert.AreEqual("score", e.triggerKey,
            $"601점은 점수 트리거여야 함. 실제 trigger={e.triggerKey}, name={e.endingName}");
    }

    [Test]
    public void Ending_BelowAllBands_Neg700_DoesNotMisfire()
    {
        var e = EndingResolver.ResolveByScore(-700);
        Assert.AreEqual("score", e.triggerKey,
            $"-700점은 점수 트리거여야 함. 실제 trigger={e.triggerKey}, name={e.endingName}");
    }

    // ── 조기엔딩(#11~#14): 즉시 발동 ──────────────────────────

    [Test]
    public void Early_CultBrainwash13_Immediate()
    {
        var mgr = TestHelpers.FreshManager();
        var e = EndingResolver.ResolveEarly(EventIds.CultBrainwash, mgr);
        Assert.IsTrue(e.IsValid, "#13 은 즉시 엔딩(점수 무관)");
        TestHelpers.DestroyManager(mgr);
    }

    // ── 누적엔딩(#15/#16): 임계치 도달 시에만 ──────────────────

    [Test]
    public void Cumulative_QuarantineFail_NeedsThreshold()
    {
        var mgr = TestHelpers.FreshManager();
        // 임계치 미만: 발동 안 함.
        for (int i = 0; i < EndingResolver.CumulativeThreshold - 1; i++)
            mgr.TriggerEvent(EventIds.QuarantineFail);
        var e1 = EndingResolver.ResolveEarly(EventIds.QuarantineFail, mgr);
        Assert.IsFalse(e1.IsValid, "임계치 미만이면 누적 엔딩 발동 안 함");

        mgr.TriggerEvent(EventIds.QuarantineFail); // 임계치 도달
        var e2 = EndingResolver.ResolveEarly(EventIds.QuarantineFail, mgr);
        Assert.IsTrue(e2.IsValid, $"임계치({EndingResolver.CumulativeThreshold}) 도달 시 발동");
        TestHelpers.DestroyManager(mgr);
    }

    // ── 점수 공식 앵커: 점수 = 2450 × 정확도 − 1470 ───────────

    [Test]
    public void Formula_Anchors_70_100_60()
    {
        Assert.AreEqual(245, Mathf.RoundToInt(2450f * 0.70f - 1470f), "70% → +245");
        Assert.AreEqual(980, Mathf.RoundToInt(2450f * 1.00f - 1470f), "100% → +980");
        Assert.AreEqual(0, Mathf.RoundToInt(2450f * 0.60f - 1470f), "60% → 0");
        Assert.AreEqual(-1470, Mathf.RoundToInt(2450f * 0.00f - 1470f), "0% → -1470");
    }

    [Test]
    public void Formula_245_MapsTo_GoodEmployee()
    {
        // 공식 점수와 엔딩 구간 정합: 70%→245→우수사원.
        int score = Mathf.RoundToInt(2450f * 0.70f - 1470f);
        var e = EndingResolver.ResolveByScore(score);
        Assert.AreEqual("우수 사원", e.endingName);
    }

    // ── ScoreModelTable 상수: 점수/돈 출처 분리 ────────────────

    [Test]
    public void ScoreModel_ScoreAndMoneyConstants_Distinct()
    {
        var sm = GameDatabaseProvider.Database.scoreModel;
        Assert.IsNotNull(sm);
        Assert.AreEqual(10, sm.GetInt("JUDGE_CORRECT"), "점수 정답 +10");
        Assert.AreEqual(-15, sm.GetInt("JUDGE_WRONG"), "점수 오판 -15");
        Assert.AreEqual(100, sm.GetInt("DAILY_BASE"), "돈 일급 +100");
        Assert.AreEqual(30, sm.GetInt("DETECTION"), "돈 적발 +30");
        Assert.AreEqual(50, sm.GetInt("PERFECT_DAY"), "돈 퍼펙트 +50");
        Assert.AreEqual(-50, sm.GetInt("WARNING"), "돈 경고 -50");
    }
}
