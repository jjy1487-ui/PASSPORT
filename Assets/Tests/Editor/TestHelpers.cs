using UnityEngine;
using UnityEditor;

/// <summary>
/// EditMode 테스트 공용 헬퍼. 데이터 값 정합 검증을 위해 개별 테이블 .asset 을
/// AssetDatabase 로 직접 로드해 GameDatabase 를 조립 주입한다.
/// (런타임 Resources.Load 경로는 일부 테이블 참조가 NULL 로 풀리는 별도 버그가 있어
///  그 경로는 RuntimeLoadPathTests 에서 따로 검증한다. 여기선 데이터 값 자체를 본다.)
/// 프로덕션 코드는 수정하지 않는다(테스트 전용).
/// </summary>
public static class TestHelpers
{
    public const string TableDir = "Assets/GameData/";

    private static T Load<T>(string name) where T : DataTableAsset
        => AssetDatabase.LoadAssetAtPath<T>(TableDir + name + ".asset");

    /// <summary>개별 테이블 .asset 을 직접 로드해 조립한 신뢰 DB(데이터 값 검증용).</summary>
    public static GameDatabase BuildDatabaseFromAssets()
    {
        var db = ScriptableObject.CreateInstance<GameDatabase>();
        db.customer            = Load<CustomerTable>("CustomerTable");
        db.passport            = Load<PassportTable>("PassportTable");
        db.visa                = Load<VisaTable>("VisaTable");
        db.pcrTest             = Load<PcrTestTable>("PcrTestTable");
        db.employmentCert      = Load<EmploymentCertTable>("EmploymentCertTable");
        db.daySchedule         = Load<DayScheduleTable>("DayScheduleTable");
        db.documentRequirement = Load<DocumentRequirementTable>("DocumentRequirementTable");
        db.ruleBook            = Load<RuleBookTable>("RuleBookTable");
        db.news                = Load<NewsTable>("NewsTable");
        db.defectRule          = Load<DefectRuleTable>("DefectRuleTable");
        db.fakeValuePool       = Load<FakeValuePoolTable>("FakeValuePoolTable");
        db.xray                = Load<XrayTable>("XrayTable");
        db.fingerprint         = Load<FingerprintTable>("FingerprintTable");
        db.dialogueCase        = Load<DialogueCaseTable>("DialogueCaseTable");
        db.dialogueLine        = Load<DialogueLineTable>("DialogueLineTable");
        db.shop                = Load<ShopTable>("ShopTable");
        db.reward              = Load<RewardTable>("RewardTable");
        db.ending              = Load<EndingTable>("EndingTable");
        db.scoreModel          = Load<ScoreModelTable>("ScoreModelTable");
        db.characterScore      = Load<CharacterScoreTable>("CharacterScoreTable");
        db.characterPayout     = Load<CharacterPayoutTable>("CharacterPayoutTable");
        return db;
    }

    /// <summary>신뢰 DB 를 Provider 에 주입.</summary>
    public static GameDatabase InjectDatabase()
    {
        var db = BuildDatabaseFromAssets();
        GameDatabaseProvider.Override(db);
        return db;
    }

    /// <summary>격리된 ScoreEconomyManager 생성(PlayerPrefs 초기화 + ResetAll).</summary>
    public static ScoreEconomyManager FreshManager()
    {
        if (ScoreEconomyManager.Instance != null)
            Object.DestroyImmediate(ScoreEconomyManager.Instance.gameObject);

        foreach (var k in new[] { "SE_Score", "SE_Money", "SE_Correct", "SE_Judged",
                                  "SE_Titles", "SE_Items", "SE_Events", "SE_SageCount" })
            PlayerPrefs.DeleteKey(k);

        var go = new GameObject("ScoreEconomyManager_Test");
        var mgr = go.AddComponent<ScoreEconomyManager>(); // Awake → Instance 설정
        mgr.ResetAll();
        return mgr;
    }

    public static void DestroyManager(ScoreEconomyManager mgr)
    {
        if (mgr != null) Object.DestroyImmediate(mgr.gameObject);
    }
}
