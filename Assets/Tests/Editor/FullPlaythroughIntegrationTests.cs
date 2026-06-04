using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 1일차 → 14일차 → 엔딩까지 실제 ImmigrationManager + InspectionController 를 구동해
/// "진행이 어디서 멈추는지"를 특정하는 통합 테스트(EditMode, 동기 구동).
///
/// 왜 EditMode 인가: 프로덕션 코드가 Assembly-CSharp(asmdef 없음)에 있어 별도 PlayMode
/// asmdef 가 프로덕션 타입을 참조할 수 없다. Assembly-CSharp-Editor 는 Assembly-CSharp 를
/// 자동 참조하므로 여기서 실제 컴포넌트를 인스턴스화해 동기 흐름을 끝까지 구동한다.
/// DialogueView 를 null 로 두면 PlayThen 이 onComplete 를 즉시 호출해 코루틴 없이 진행된다.
/// (대화창 'Next' 버튼 대기는 UI/UX 이며 진행 차단 버그가 아니다 — 본 테스트는 로직 차단을 잡는다.)
/// </summary>
public class FullPlaythroughIntegrationTests
{
    private ScoreEconomyManager _economy;
    private readonly List<GameObject> _spawned = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        TestHelpers.InjectDatabase();
        _economy = TestHelpers.FreshManager();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _spawned)
            if (go != null) Object.DestroyImmediate(go);
        _spawned.Clear();
        if (ScoreEconomyManager.Instance != null)
            Object.DestroyImmediate(ScoreEconomyManager.Instance.gameObject);
        GameDatabaseProvider.Reset();
    }

    private T Spawn<T>(string name) where T : MonoBehaviour
    {
        var go = new GameObject(name);
        _spawned.Add(go);
        return go.AddComponent<T>();
    }

    private static void SetPrivate(object obj, string field, object value)
    {
        FieldInfo fi = obj.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(fi, $"필드 {field} 를 {obj.GetType().Name} 에서 찾지 못함");
        fi.SetValue(obj, value);
    }

    private static T GetPrivate<T>(object obj, string field)
    {
        FieldInfo fi = obj.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(fi, $"필드 {field} 를 {obj.GetType().Name} 에서 찾지 못함");
        return (T)fi.GetValue(obj);
    }

    private static void Invoke(object obj, string method, params object[] args)
    {
        MethodInfo mi = obj.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(mi, $"메서드 {method} 를 {obj.GetType().Name} 에서 찾지 못함");
        mi.Invoke(obj, args);
    }

    /// <summary>
    /// 핵심: 1→14일 전부 매 손님 판정해 완주하고 엔딩이 떨어지는지 확인.
    /// 멈추면 어느 일차/슬롯/상태에서 멈췄는지 Assert 메시지로 특정.
    /// </summary>
    [Test]
    public void FullPlaythrough_Day1ToDay14_ReachesEnding_CorrectVerdicts()
    {
        RunPlaythrough(approveAlwaysCorrect: true);
    }

    /// <summary>아무 판정(전부 통과 버튼)으로도 진행이 막히지 않아야 한다(오판이어도 확정·진행).</summary>
    [Test]
    public void FullPlaythrough_Day1ToDay14_ReachesEnding_AllApprove()
    {
        RunPlaythrough(approveAlwaysCorrect: false);
    }

    private void RunPlaythrough(bool approveAlwaysCorrect)
    {
        // 매니저 + 컨트롤러 조립. View 는 모두 null(동기 진행). 일자완료 패널만 더미로 둔다.
        var controller = Spawn<InspectionController>("InspectionController");
        var dayCompleteRoot = new GameObject("DayCompleteRoot");
        _spawned.Add(dayCompleteRoot);
        SetPrivate(controller, "_dayCompleteRoot", dayCompleteRoot);

        var manager = Spawn<ImmigrationManager>("ImmigrationManager");
        SetPrivate(manager, "inspectionController", controller);
        SetPrivate(manager, "startDay", 1);

        // OnEnable 구독은 AddComponent 시점에 이미 호출됨. 엔딩 수신 플래그.
        bool endingRaised = false;
        EndingResult ending = default;
        manager.OnEndingResolved += e => { endingRaised = true; ending = e; };

        // ImmigrationManager.Start() 는 StartCoroutine(FadeIn) 을 호출하는데 EditMode 에서는
        // 코루틴이 돌지 않으므로, Start 가 하는 핵심 두 단계만 직접 호출한다(페이드 연출 제외).
        Invoke(manager, "EnsureEconomy");
        Invoke(manager, "BeginDay", 1, true);

        const int maxSlotsTotal = 14 * 7 + 50; // 안전 상한(오거부 루프 포함)
        int safety = 0;

        for (int expectedDay = 1; expectedDay <= 14 && !endingRaised; expectedDay++)
        {
            Assert.AreEqual(expectedDay, controller.CurrentDay,
                $"{expectedDay}일차가 시작되지 않음(CurrentDay={controller.CurrentDay}). 이전 일자 전이 실패.");

            // 그 날 손님들을 끝까지 판정(조기엔딩이 떨어지면 정상 종료로 본다).
            while (!controller.IsDayComplete && !endingRaised)
            {
                Assert.IsTrue(controller.HasActiveCustomer,
                    $"day{expectedDay} slotIndex={controller.CurrentSlotIndex}: " +
                    $"활성 손님이 없는데 일자도 완료가 아님 — 진행 교착(EnableJudgment/SetReady 또는 대화 콜백 누락).");

                bool decision = approveAlwaysCorrect ? CorrectDecisionFor(controller) : true;
                controller.TestSubmitDecision(decision);

                if (++safety > maxSlotsTotal)
                    Assert.Fail($"무한 루프 의심 — day{expectedDay} slotIndex={controller.CurrentSlotIndex} 에서 진행 안 됨.");
            }

            if (endingRaised) break; // 조기/누적 엔딩 발동 = 정상 진행 결과

            // 마지막 손님까지 끝나면 일자완료. 14일 미만이면 '다음 날' 버튼으로 진행해야 한다.
            Assert.IsTrue(controller.IsDayComplete,
                $"day{expectedDay}: 모든 손님을 판정했는데 일자완료 상태가 아님.");

            if (expectedDay < 14)
            {
                manager.OnNextDayButton();
                Assert.AreEqual(expectedDay + 1, controller.CurrentDay,
                    $"day{expectedDay} → day{expectedDay + 1} 전이 실패. " +
                    $"'다음 날'(OnNextDayButton) 호출 후에도 CurrentDay={controller.CurrentDay}. " +
                    $"BeginDay 로드 실패 또는 데이터 누락 가능.");
            }
        }

        // 14일 완주(점수구간 엔딩) 또는 조기/누적 엔딩 중 하나로 반드시 엔딩에 도달해야 한다.
        Assert.IsTrue(endingRaised, "끝까지 진행했으나 엔딩이 발행되지 않음(OnEndingResolved 미발화) — 어딘가에서 진행이 멈춤.");
        Assert.IsTrue(ending.IsValid, "발행된 엔딩이 유효하지 않음(IsValid=false).");
        ScoreEconomyManager eco = ControllerEconomy(controller);
        Debug.Log($"[FullPlaythrough] 완주 성공. 엔딩={ending.endingName} type={ending.endingType} score={eco.Score} 판정수={eco.JudgedCount}");
        if (approveAlwaysCorrect && ending.endingType == "normal")
            Assert.AreEqual(98, eco.JudgedCount, "정답 완주 시 총 98건(14×7)이 정산되어야 함.");
    }

    /// <summary>
    /// 회귀: day5 slot2(특수(연예인)★, 첫 고급 분기 캐릭터)에서 고급 분기 선택으로도 진행되어야 한다.
    /// 수정 전: AdvancedBranchPanel.Commit 이 mgr.Settle 만 하고 진행을 호출하지 않아 여기서 멈췄다(=4일차까지만 됨).
    /// 수정 후: SubmitAdvancedDecision 이 1회 정산 + 다음 손님 진행을 보장한다.
    /// </summary>
    [Test]
    public void Day5_CelebrityAdvancedChoice_AdvancesToNextCustomer()
    {
        var controller = Spawn<InspectionController>("InspectionController");
        var dayCompleteRoot = new GameObject("DayCompleteRoot");
        _spawned.Add(dayCompleteRoot);
        SetPrivate(controller, "_dayCompleteRoot", dayCompleteRoot);

        var manager = Spawn<ImmigrationManager>("ImmigrationManager");
        SetPrivate(manager, "inspectionController", controller);
        SetPrivate(manager, "startDay", 5); // 연예인 등장 일차로 점프(데이터 검증용)
        Invoke(manager, "EnsureEconomy");
        Invoke(manager, "BeginDay", 5, true);

        Assert.AreEqual(5, controller.CurrentDay, "day5 로드 실패.");

        // slot2(index 1)까지 진행: slot1 을 임의 판정.
        Assert.IsTrue(controller.HasActiveCustomer);
        controller.TestSubmitDecision(CorrectDecisionFor(controller));

        // 이제 slot2 = 연예인.
        Assert.AreEqual(1, controller.CurrentSlotIndex, "slot2 로 진행되지 않음.");
        Assert.AreEqual(CharacterTypes.Celebrity, controller.CurrentCharacterType,
            $"slot2 가 연예인이 아님: {controller.CurrentCharacterType}");

        int beforeIndex = controller.CurrentSlotIndex;
        // 고급 분기: '즉시 입국' 선택을 컨트롤러로 직접 전달(AdvancedBranchPanel.Commit 가 호출하는 경로).
        controller.SubmitAdvancedDecision(BranchKeys.ApproveImmediate, wasCorrect: true);

        Assert.AreEqual(beforeIndex + 1, controller.CurrentSlotIndex,
            "고급 분기 선택 후 다음 손님으로 진행되지 않음 — 진행 교착(수정 전 버그 재현).");
        // 정산은 컨트롤러가 실제로 쓰는 economy(EnsureEconomy/SetEconomy 로 주입된 인스턴스)에 쌓인다.
        // SetUp 의 _economy 와 다를 수 있으므로 컨트롤러가 보유한 인스턴스로 단언한다.
        ScoreEconomyManager settleTarget = ControllerEconomy(controller);
        Assert.AreEqual(2, settleTarget.JudgedCount, "연예인 손님이 정확히 1회만 정산되지 않음(중복/누락).");
    }

    /// <summary>
    /// 컨트롤러가 실제 정산에 쓰는 ScoreEconomyManager 를 반환한다.
    /// EnsureEconomy/SetEconomy 가 주입한 인스턴스가 SetUp 의 FreshManager 와 다를 수 있어
    /// (전역 Instance 가 비어 EnsureEconomy 가 별도 매니저를 생성하는 경우) 정산 카운트는
    /// 반드시 컨트롤러가 보유한 인스턴스에서 읽어야 한다.
    /// </summary>
    private static ScoreEconomyManager ControllerEconomy(InspectionController controller)
    {
        var eco = GetPrivate<ScoreEconomyManager>(controller, "_economy");
        Assert.IsNotNull(eco, "컨트롤러에 economy 가 주입되지 않음(SetEconomy/EnsureEconomy 누락).");
        return eco;
    }

    /// <summary>현재 손님의 정답 판정(정상 승인이면 true, 정상 거절이면 false).</summary>
    private static bool CorrectDecisionFor(InspectionController controller)
    {
        // CurrentDocState: 정상이면 Normal(승인), 불량이면 Defect(거절).
        return controller.CurrentDocState == DocStates.Normal;
    }
}
