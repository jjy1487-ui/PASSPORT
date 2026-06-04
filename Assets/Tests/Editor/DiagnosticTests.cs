using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 런타임/에셋 로드 무결성 검증.
///
/// ★ 발견된 P0 버그 (data-tools 영역, QA 는 리포트만):
///   Assets/GameData/ 의 테이블 .asset 19개가 m_Script: {fileID: 0} (스크립트 미연결)이라
///   해당 ScriptableObject 타입으로 인스턴스화되지 않는다. CustomerTable/GameDatabase 만 정상.
///   결과: GameDatabase.characterScore/payout/ending/scoreModel/... 가 런타임에 전부 NULL →
///   점수/금액/엔딩이 항상 코드 폴백(+10/-15/+100)으로 떨어지고 1~3단계 데이터가 죽는다.
///   이 테스트들이 GREEN 이 되려면 data-tools 가 .asset 의 m_Script 를 재연결(재임포트)해야 한다.
/// </summary>
public class RuntimeLoadPathTests
{
    [TearDown] public void TearDown() { GameDatabaseProvider.Reset(); }

    [Test]
    public void Assets_AllTablesLoadAsTheirType()
    {
        // 개별 .asset 이 선언 타입으로 로드되는가(m_Script 연결 무결성).
        AssertLoads<CharacterScoreTable>("CharacterScoreTable");
        AssertLoads<CharacterPayoutTable>("CharacterPayoutTable");
        AssertLoads<ScoreModelTable>("ScoreModelTable");
        AssertLoads<EndingTable>("EndingTable");
        AssertLoads<DayScheduleTable>("DayScheduleTable");
        AssertLoads<DefectRuleTable>("DefectRuleTable");
    }

    private static void AssertLoads<T>(string name) where T : DataTableAsset
    {
        var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"Assets/GameData/{name}.asset");
        Assert.IsNotNull(a,
            $"{name}.asset 이 {typeof(T).Name} 타입으로 로드되지 않음(m_Script: fileID 0 미연결 의심). " +
            "data-tools 재임포트로 스크립트 재연결 필요.");
    }

    [Test]
    public void Runtime_GameDatabase_AllTablesResolve()
    {
        GameDatabaseProvider.Reset();
        var db = GameDatabaseProvider.Database;
        Assert.IsNotNull(db, "런타임 GameDatabase 로드 실패");

        Assert.IsNotNull(db.characterScore,  "characterScore 가 런타임 NULL → 점수 코드 폴백(+10/-15)");
        Assert.IsNotNull(db.characterPayout, "characterPayout 가 런타임 NULL → 돈 코드 폴백(+100)");
        Assert.IsNotNull(db.ending,          "ending 이 런타임 NULL → 엔딩 구간 코드 폴백");
        Assert.IsNotNull(db.scoreModel,      "scoreModel 이 런타임 NULL");
        Assert.IsNotNull(db.daySchedule,     "daySchedule 이 런타임 NULL");
        Assert.IsNotNull(db.defectRule,      "defectRule 이 런타임 NULL");
    }
}
