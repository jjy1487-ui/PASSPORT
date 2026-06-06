using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 정산(ScoreEconomyManager.Settle) + 분기(BranchKeyResolver) 검증.
/// 규약 4장: 정답=isNormal?Approve:Reject, 점수≠돈 분리, 결정론.
/// 모든 케이스는 주입된 실제 GameDatabase 로 결정론적으로 돈다(난수 없음).
/// </summary>
public class SettlementTests
{
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

    // ── 규칙 3: 정답 = isNormal ? Approve : Reject ─────────────

    [Test]
    public void Correct_NormalApprove_GeneralCustomer_Scores3()
    {
        // 일반 고객 정상 손님 승인 = approve_correct, score=3 (데이터 .asset 행).
        var b = BranchKeyResolver.Resolve(GameResults.Approve, playerApproved: true,
            wrongRejectCount: 0, forcedPass: false, CharacterTypes.General, defectVariant: null);
        Assert.AreEqual(DocStates.Normal, b.docState);
        Assert.AreEqual(BranchKeys.ApproveCorrect, b.branchKey);

        _mgr.Settle(CharacterTypes.General, b, wasCorrect: true);
        Assert.AreEqual(3, _mgr.Score, "일반 고객 approve_correct score=3 (.asset 행)");
        Assert.AreEqual(1, _mgr.CorrectCount);
        Assert.AreEqual(1, _mgr.JudgedCount);
    }

    [Test]
    public void Correct_DefectReject_GeneralCustomer_Scores3()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Reject, playerApproved: false,
            0, false, CharacterTypes.General, null);
        Assert.AreEqual(DocStates.Defect, b.docState);
        Assert.AreEqual(BranchKeys.RejectCorrect, b.branchKey);
        _mgr.Settle(CharacterTypes.General, b, true);
        Assert.AreEqual(3, _mgr.Score, "일반 고객 reject_correct score=3");
    }

    [Test]
    public void Wrong_DefectApprove_GeneralCustomer_NegScore()
    {
        // 불량인데 입국 = approve_wrong, score=-5.
        var b = BranchKeyResolver.Resolve(GameResults.Reject, playerApproved: true,
            0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.ApproveWrong, b.branchKey);
        _mgr.Settle(CharacterTypes.General, b, wasCorrect: false);
        Assert.AreEqual(-5, _mgr.Score, "일반 고객 approve_wrong score=-5");
        Assert.AreEqual(0, _mgr.CorrectCount);
    }

    [Test]
    public void Wrong_NormalReject_GeneralCustomer_NegScore()
    {
        // 정상인데 거부 = reject_wrong, score=-6.
        var b = BranchKeyResolver.Resolve(GameResults.Approve, playerApproved: false,
            0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.RejectWrong, b.branchKey);
        _mgr.Settle(CharacterTypes.General, b, false);
        Assert.AreEqual(-6, _mgr.Score, "일반 고객 reject_wrong score=-6");
    }

    // ── 캐릭터별 .asset 1:1 대조 (대표 5종) ────────────────────

    [Test]
    public void ScoreTable_RepresentativeRows_MatchAsset()
    {
        // (캐릭터, doc_state, branch_key, variant, 기대 score)
        AssertScore(CharacterTypes.Tourist, GameResults.Approve, true, null, BranchKeys.ApproveCorrect, 5);
        AssertScore(CharacterTypes.Tourist, GameResults.Reject, true, "출국X", BranchKeys.ApproveWrong, -6);  // #16
        AssertScore(CharacterTypes.Infected, GameResults.Reject, false, "1-C 백신X", BranchKeys.RejectCorrect, 3);
        AssertScore(CharacterTypes.Infected, GameResults.Reject, true, "1-C 백신X", BranchKeys.ApproveWrong, -6); // #15
        AssertScore(CharacterTypes.PlasticSuspect, GameResults.Reject, true, "마스크 미요청", BranchKeys.ApproveWrong, -11);
    }

    private void AssertScore(string charType, string correctResult, bool approved, string variant,
        string expectedBranch, int expectedScore)
    {
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(correctResult, approved, 0, false, charType, variant);
        Assert.AreEqual(expectedBranch, b.branchKey, $"{charType}/{variant} branch_key");
        int before = mgr.Score;
        mgr.Settle(charType, b, wasCorrect: (approved == (correctResult == GameResults.Approve)));
        Assert.AreEqual(expectedScore, mgr.Score - before,
            $"{charType}/{b.branchKey}/{variant} 점수 .asset 대조");
        TestHelpers.DestroyManager(mgr);
    }

    // ── 규칙 5: 점수 ≠ 돈 분리 ─────────────────────────────────

    [Test]
    public void ScoreAndMoney_AreIndependentChannels()
    {
        // 정상 일반 손님 승인: 점수는 score표, 돈은 payout표에서 따로 온다.
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, null);
        int score0 = _mgr.Score, money0 = _mgr.Money;
        _mgr.Settle(CharacterTypes.General, b, true);
        int dScore = _mgr.Score - score0;
        int dMoney = _mgr.Money - money0;
        // 점수 델타와 돈 델타가 동일 상수로 묶이지 않았음을 확인(분리 채널).
        Assert.AreEqual(3, dScore, "점수는 score표(=3)");
        Assert.AreNotEqual(dScore, dMoney,
            "점수 델타와 돈 델타가 같으면 한 채널로 섞였다는 신호(분리 위반 의심). 실제 dMoney=" + dMoney);
    }

    // ── 결정론: 같은 입력 두 번 → 동일 산출 ────────────────────

    [Test]
    public void Determinism_SameInputs_SameTotals()
    {
        var seq = new (string ch, string correct, bool app, string var_)[]
        {
            (CharacterTypes.General, GameResults.Approve, true, null),
            (CharacterTypes.Tourist, GameResults.Reject, false, "출국X"),
            (CharacterTypes.Infected, GameResults.Reject, true, "1-C 백신X"),
            (CharacterTypes.General, GameResults.Reject, true, null),
        };

        (int s, int m, int c, int j) Run()
        {
            var mgr = TestHelpers.FreshManager();
            foreach (var x in seq)
            {
                var b = BranchKeyResolver.Resolve(x.correct, x.app, 0, false, x.ch, x.var_);
                bool ok = x.app == (x.correct == GameResults.Approve);
                mgr.Settle(x.ch, b, ok);
            }
            var r = (mgr.Score, mgr.Money, mgr.CorrectCount, mgr.JudgedCount);
            TestHelpers.DestroyManager(mgr);
            return r;
        }

        var a = Run();
        var c = Run();
        Assert.AreEqual(a, c, "같은 입력 시퀀스 두 번 실행 → score/money/correct/judged 완전 동일해야 함(결정론)");
    }

    // ── "" → null 변이 정규화: 변이 없는 정상 손님 회귀 없음 ────

    [Test]
    public void EmptyVariant_NormalizedToNull_NoWrongRowPicked()
    {
        // defectVariant="" 인 일반 정상 손님은 변이 무시 매칭으로 approve_correct(3) 를 정확히 집는다.
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, "");
        Assert.IsNull(b.defectVariant, "빈 문자열은 null 로 정규화되어야 함");
        _mgr.Settle(CharacterTypes.General, b, true);
        Assert.AreEqual(3, _mgr.Score);
    }
}
