using NUnit.Framework;

/// <summary>
/// 변이 매칭(#15/#16), 재심사 루프 1회 정산 불변식, 감액 분기 검증.
/// </summary>
public class VariantAndLoopTests
{
    private GameDatabase _db;

    [SetUp] public void SetUp() { _db = TestHelpers.InjectDatabase(); }
    [TearDown] public void TearDown() { GameDatabaseProvider.Reset(); }

    // ── 변이 매칭: 출국X 오입국 → #16 ─────────────────────────

    [Test]
    public void Variant_TouristOverstay_ApproveWrong_Triggers16()
    {
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(GameResults.Reject, playerApproved: true,
            0, false, CharacterTypes.Tourist, "출국X");
        Assert.AreEqual(BranchKeys.ApproveWrong, b.branchKey);
        Assert.AreEqual("출국X", b.defectVariant, "변이가 보존되어야 #16 행을 집는다");
        mgr.Settle(CharacterTypes.Tourist, b, wasCorrect: false);
        Assert.AreEqual(1, mgr.GetEventCount(EventIds.OverstayApprove), "#16 누적 카운터 +1");
        TestHelpers.DestroyManager(mgr);
    }

    [Test]
    public void Variant_QuarantineVaccineFail_ApproveWrong_Triggers15()
    {
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false,
            CharacterTypes.Quarantine, "1-C 백신X");
        mgr.Settle(CharacterTypes.Quarantine, b, false);
        Assert.AreEqual(1, mgr.GetEventCount(EventIds.QuarantineFail), "#15 누적 카운터 +1");
        TestHelpers.DestroyManager(mgr);
    }

    [Test]
    public void Variant_QuarantineAllMissing_ApproveWrong_Triggers15()
    {
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(GameResults.Reject, true, 0, false,
            CharacterTypes.Quarantine, "1-D 모두 미비");
        mgr.Settle(CharacterTypes.Quarantine, b, false);
        Assert.AreEqual(1, mgr.GetEventCount(EventIds.QuarantineFail), "#15 (1-D) 누적 카운터 +1");
        TestHelpers.DestroyManager(mgr);
    }

    [Test]
    public void Variant_QuarantineReject_NoEarlyEvent()
    {
        // 변이 있어도 정답(거부)이면 #15 발동 안 함(approve_wrong 만 트리거).
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(GameResults.Reject, false, 0, false,
            CharacterTypes.Quarantine, "1-C 백신X");
        Assert.AreEqual(BranchKeys.RejectCorrect, b.branchKey);
        mgr.Settle(CharacterTypes.Quarantine, b, true);
        Assert.AreEqual(0, mgr.GetEventCount(EventIds.QuarantineFail), "정답 거부엔 #15 없음");
        TestHelpers.DestroyManager(mgr);
    }

    // ── 변이 폴백: 변이 없는 정상 손님 회귀 없음 ───────────────

    [Test]
    public void NoVariant_NormalCustomer_NoRegression()
    {
        var mgr = TestHelpers.FreshManager();
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, null);
        mgr.Settle(CharacterTypes.General, b, true);
        Assert.AreEqual(3, mgr.Score);
        Assert.AreEqual(0, mgr.GetEventCount(EventIds.OverstayApprove));
        Assert.AreEqual(0, mgr.GetEventCount(EventIds.QuarantineFail));
        TestHelpers.DestroyManager(mgr);
    }

    // ── 재심사 루프 1회 정산 불변식 ───────────────────────────
    //  InspectionController.HandleDecision 의 루프 규약(4-A): 재거절 중에는
    //  Settle 을 호출하지 않고, 확정(정답/강제통과/오허가) 시점에만 1회 호출한다.

    [Test]
    public void RejectLoop_SettlesExactlyOnce_OnForcedPass()
    {
        // 정상 손님을 3회 오거부 → 강제 통과(확정). 정산은 단 1회.
        var mgr = TestHelpers.FreshManager();

        // 라운드1,2: 미확정(컨트롤러는 Settle 호출 안 함) → 테스트도 호출하지 않음.
        // 라운드3 도달 = 강제 통과(forcedPass=true) 확정 시점에만 1회.
        var b = BranchKeyResolver.Resolve(GameResults.Approve, playerApproved: true,
            wrongRejectCount: 3, forcedPass: true, CharacterTypes.General, null);
        mgr.Settle(CharacterTypes.General, b, wasCorrect: true);

        Assert.AreEqual(1, mgr.JudgedCount, "강제 통과로 정산은 단 1회만");
        TestHelpers.DestroyManager(mgr);
    }

    [Test]
    public void AccrueScale_Celebrity_AfterReject3_UsesAccrueKey()
    {
        // 감액 대상(연예인): 재거절 끝 통과 → approve_after_reject_3.
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true,
            wrongRejectCount: 3, forcedPass: true, CharacterTypes.Celebrity, "얼굴O");
        Assert.AreEqual(BranchKeys.ApproveAfterReject3, b.branchKey);

        // 비감액 대상(일반): 같은 상황이어도 평면 키.
        var bg = BranchKeyResolver.Resolve(GameResults.Approve, true, 3, true, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.ApproveAfterReject3, bg.branchKey,
            "현재 Resolve 는 wrongRejectCount>0 이면 캐릭터 무관하게 after_reject 키를 쓴다(회귀 감시)");
    }

    [Test]
    public void AccrueScale_Celebrity_ImmediateApprove_UsesImmediateKey()
    {
        var b = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.Celebrity, "얼굴O");
        Assert.AreEqual(BranchKeys.ApproveImmediate, b.branchKey, "감액 대상 즉시 승인 = approve_immediate");

        var bg = BranchKeyResolver.Resolve(GameResults.Approve, true, 0, false, CharacterTypes.General, null);
        Assert.AreEqual(BranchKeys.ApproveCorrect, bg.branchKey, "비감액 대상 즉시 승인 = approve_correct");
    }
}
