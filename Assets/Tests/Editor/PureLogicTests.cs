using NUnit.Framework;
using UnityEngine;

/// <summary>
/// DB 데이터에 의존하지 않는 순수 로직 검증.
/// (현재 테이블 .asset 배선이 깨져 데이터 조회는 폴백으로 떨어지지만,
///  분기 결정/점수 공식/엔딩 코드폴백 밴드 같은 로직 자체는 데이터와 무관하게 옳아야 한다.)
/// 결정론적이며 난수 의존 없음.
/// </summary>
public class PureLogicTests
{
    [TearDown] public void TearDown() { GameDatabaseProvider.Reset(); }

    // ── 규칙 3: 정답 = isNormal ? Approve : Reject → branch_key ─

    [Test]
    public void Branch_Normal_Approve_IsApproveCorrect()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(DocStates.Normal, b.docState);
        Assert.AreEqual(BranchKeys.ApproveCorrect, b.branchKey);
    }

    [Test]
    public void Branch_Normal_Reject_IsRejectWrong()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Approve, false, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.RejectWrong, b.branchKey);
    }

    [Test]
    public void Branch_Defect_Reject_IsRejectCorrect()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Reject, false, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(DocStates.Defect, b.docState);
        Assert.AreEqual(BranchKeys.RejectCorrect, b.branchKey);
    }

    [Test]
    public void Branch_Defect_Approve_IsApproveWrong()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.ApproveWrong, b.branchKey);
    }

    // ── 변이 보존 + "" → null 정규화 ──────────────────────────

    [Test]
    public void Branch_PreservesVariant()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false, CharacterTypes.Tourist, "출국X");
        Assert.AreEqual("출국X", b.defectVariant, "변이가 분기 결과에 보존되어야 #16 행을 집는다");
    }

    [Test]
    public void Branch_EmptyVariant_NormalizedToNull()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, "");
        Assert.IsNull(b.defectVariant, "빈 문자열 변이는 null 로 정규화(변이 무시 매칭)");
    }

    // ── 감액 분기(성형/연예인/정치인) ─────────────────────────

    [Test]
    public void Branch_AccrueScale_ImmediateVsCorrect()
    {
        Assert.IsTrue(BranchKeyResolver.UsesAccrueScale(CharacterTypes.Celebrity));
        Assert.IsTrue(BranchKeyResolver.UsesAccrueScale(CharacterTypes.Politician));
        Assert.IsTrue(BranchKeyResolver.UsesAccrueScale(CharacterTypes.PlasticSuspect));
        Assert.IsFalse(BranchKeyResolver.UsesAccrueScale(CharacterTypes.General));

        var bc = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.Celebrity, "얼굴O");
        Assert.AreEqual(BranchKeys.ApproveImmediate, bc.branchKey);
        var bg = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.ApproveCorrect, bg.branchKey);
    }

    [Test]
    public void Branch_AfterReject_Keys()
    {
        Assert.AreEqual(BranchKeys.ApproveAfterReject1,
            BranchKeyResolver.Resolve(GameResults.Approve, true, 1, false, CharacterTypes.Celebrity, "얼굴O").branchKey);
        Assert.AreEqual(BranchKeys.ApproveAfterReject2,
            BranchKeyResolver.Resolve(GameResults.Approve, true, 2, false, CharacterTypes.Celebrity, "얼굴O").branchKey);
        Assert.AreEqual(BranchKeys.ApproveAfterReject3,
            BranchKeyResolver.Resolve(GameResults.Approve, true, 3, true, CharacterTypes.Celebrity, "얼굴O").branchKey);
    }

    // ── 점수 공식 앵커: 점수 = 2450 × 정확도 − 1470 ───────────

    [Test]
    public void Formula_Anchors()
    {
        Assert.AreEqual(245, Mathf.RoundToInt(2450f * 0.70f - 1470f), "70% → +245 (우수 사원)");
        Assert.AreEqual(980, Mathf.RoundToInt(2450f * 1.00f - 1470f), "100% → +980 (전설)");
        Assert.AreEqual(0, Mathf.RoundToInt(2450f * 0.60f - 1470f), "60% → 0");
        Assert.AreEqual(-1470, Mathf.RoundToInt(2450f * 0.00f - 1470f), "0% → -1470");
    }

    // ── 엔딩 코드 폴백 밴드(DB 없을 때): 규약 4장 앵커 ─────────

    [Test]
    public void Ending_CodeFallbackBands_MatchSpecAnchors()
    {
        // DB(ending 테이블) 미주입 상태 → EndingResolver 코드 폴백 밴드.
        GameDatabaseProvider.Reset();
        GameDatabaseProvider.Override(null);

        Assert.AreEqual("우수 사원", EndingResolver.ResolveByScore(245).endingName, "70%→245→우수 사원(200+)");
        Assert.AreEqual("전설의 검문관", EndingResolver.ResolveByScore(550).endingName, "100%→전설(500+)");
        Assert.AreEqual("우수 사원", EndingResolver.ResolveByScore(200).endingName, "하한 200");
        Assert.AreEqual("우수 사원", EndingResolver.ResolveByScore(299).endingName, "상한 299");
        Assert.AreEqual("평범한 검문관", EndingResolver.ResolveByScore(199).endingName, "199 아래 구간");
    }

    [Test]
    public void Ending_CodeFallback_NoWildcardMisfire_OnExtremeScores()
    {
        // 코드 폴백은 극단 점수에도 점수 트리거로 정상 분기해야 한다(와일드카드 오발 없음).
        GameDatabaseProvider.Reset();
        GameDatabaseProvider.Override(null);
        Assert.AreEqual("score", EndingResolver.ResolveByScore(601).triggerKey);
        Assert.AreEqual("score", EndingResolver.ResolveByScore(-700).triggerKey);
    }

    [Test]
    public void Determinism_Resolve_SameInputsSameOutput()
    {
        for (int i = 0; i < 5; i++)
        {
            var a = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false, CharacterTypes.Infected, "1-C 백신X");
            var b = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false, CharacterTypes.Infected, "1-C 백신X");
            Assert.AreEqual(a.branchKey, b.branchKey);
            Assert.AreEqual(a.docState, b.docState);
            Assert.AreEqual(a.defectVariant, b.defectVariant);
        }
    }
}
