using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 14일 baked 경로를 정확도 가정별로 시뮬레이션해 최종 점수→엔딩 정합과
/// 페이싱(일자별 서류 수/변이/캐릭터 등장)을 리포트한다.
/// 결정론: baked JSON + 고정 시드 선택. 난수 없음.
/// </summary>
public class PacingSimulationTests
{
    [SetUp] public void SetUp() { TestHelpers.InjectDatabase(); }
    [TearDown] public void TearDown() { GameDatabaseProvider.Reset(); }

    private static List<CustomerData> LoadAll14()
    {
        var all = new List<CustomerData>();
        for (int day = 1; day <= 14; day++)
        {
            var ta = Resources.Load<TextAsset>($"GameData/day{day}");
            Assert.IsNotNull(ta, $"day{day}.json 로드 실패");
            var d = JsonUtility.FromJson<Day1Data>(ta.text);
            if (d.customers != null) all.AddRange(d.customers);
        }
        return all;
    }

    /// <summary>정확도 가정으로 전 손님을 정산. accuracy=1 이면 전부 정답, 0이면 전부 오판.</summary>
    private (int score, int correct, int judged) Simulate(float accuracy)
    {
        var mgr = TestHelpers.FreshManager();
        var customers = LoadAll14();
        int idx = 0;
        // 결정론적 정답/오판 선택: 누적 비율이 목표 정확도를 넘지 않게 균등 분배.
        int correctTarget = Mathf.RoundToInt(customers.Count * accuracy);
        int correctSoFar = 0;
        foreach (var c in customers)
        {
            bool shouldApprove = c.correctResult == GameResults.Approve;
            // 앞에서부터 correctTarget 명을 정답 처리(결정론).
            bool playCorrect = correctSoFar < correctTarget;
            if (playCorrect) correctSoFar++;
            bool approved = playCorrect ? shouldApprove : !shouldApprove;

            var b = BranchKeyResolver.Resolve(c.correctResult, approved, 0, false, c.characterType, c.defectVariant);
            mgr.Settle(c.characterType, b, wasCorrect: playCorrect);
            idx++;
        }
        var r = (mgr.Score, mgr.CorrectCount, mgr.JudgedCount);
        TestHelpers.DestroyManager(mgr);
        return r;
    }

    [Test]
    public void Pacing_AccuracyToScoreToEnding_Report()
    {
        Debug.Log("==== 14일 페이싱 시뮬레이션 (정확도 → 점수 → 엔딩) ====");
        foreach (float acc in new[] { 0f, 0.6f, 0.7f, 1.0f })
        {
            var (score, correct, judged) = Simulate(acc);
            float realAcc = judged > 0 ? (float)correct / judged : 0;
            int formula = Mathf.RoundToInt(2450f * realAcc - 1470f);
            var ending = EndingResolver.ResolveByScore(score);
            Debug.Log($"[가정 정확도 {acc:P0}] 실측 {realAcc:P1} | 캐릭터별표 누적점수={score} | 평면공식점수={formula} | 엔딩={ending.endingName}({ending.endingId})");
        }
        Assert.Pass("리포트는 콘솔 로그 참조");
    }

    [Test]
    public void Pacing_PerfectAccuracy_ReachesPositiveEnding()
    {
        var (score, _, _) = Simulate(1.0f);
        Assert.Greater(score, 0, $"100% 정확도면 누적 점수가 양수여야 함. 실제={score}");
        var e = EndingResolver.ResolveByScore(score);
        Debug.Log($"[100%] 캐릭터별표 누적점수={score} → 엔딩 {e.endingName}");
        // 설계 앵커는 평면공식 100%→980(전설 500+). 캐릭터별 표는 다를 수 있음 → 괴리 리포트.
    }

    [Test]
    public void Pacing_ZeroAccuracy_NegativeScore()
    {
        var (score, _, _) = Simulate(0f);
        Assert.Less(score, 0, $"0% 정확도면 음수 점수여야 함. 실제={score}");
        Debug.Log($"[0%] 캐릭터별표 누적점수={score} → 엔딩 {EndingResolver.ResolveByScore(score).endingName}");
    }

    [Test]
    public void Pacing_DocCountAndVariant_PerDay_Report()
    {
        Debug.Log("==== 14일 일자별 페이싱 지표 ====");
        for (int day = 1; day <= 14; day++)
        {
            var ta = Resources.Load<TextAsset>($"GameData/day{day}");
            var d = JsonUtility.FromJson<Day1Data>(ta.text);
            int n = d.customers != null ? d.customers.Length : 0;
            int normal = 0, defect = 0, variant = 0, multiDoc = 0;
            var docTypes = new HashSet<string>();
            foreach (var c in d.customers)
            {
                if (c.correctResult == GameResults.Approve) normal++; else defect++;
                if (!string.IsNullOrEmpty(c.defectVariant)) variant++;
                if (c.documents != null && c.documents.Length >= 2) multiDoc++;
                if (c.documents != null)
                    foreach (var doc in c.documents) docTypes.Add(doc.documentType);
            }
            Debug.Log($"day{day,2}: 손님 {n} | 정상 {normal} 불량 {defect} | 변이 {variant} | 다중서류 {multiDoc} | 서류종류=[{string.Join(",", docTypes)}]");
        }
        Assert.Pass("리포트는 콘솔 로그 참조");
    }
}
