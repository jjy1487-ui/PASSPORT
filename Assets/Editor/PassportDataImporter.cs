using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
//  PassportDataImporter — xlsx 중간 JSON -> ScriptableObject .asset (data-tools 소유)
//  메뉴: Tools/Passport/Import Data
//  입력: Assets/GameData/_source/GameData.source.json (python 변환기 산출)
//  출력: Assets/GameData/*.asset (19 테이블 + GameDatabase)
//  idempotent: 같은 입력 -> 같은 .asset (기존 에셋 재사용, 내용만 갱신).
//  규약 6장: 변경 흡수 단일 지점. 시트->SO 매핑이 여기 한 곳에만 있다.
// ─────────────────────────────────────────────────────────────
public static class PassportDataImporter
{
    private const string SourceJson = "Assets/GameData/_source/GameData.source.json";
    private const string OutDir = "Assets/GameData";

    /// <summary>시트명 -> SO 타입 매핑 (변경 흡수 단일 지점).</summary>
    private static readonly Dictionary<string, Type> SheetToType = new Dictionary<string, Type>
    {
        { "customer", typeof(CustomerTable) },
        { "passport", typeof(PassportTable) },
        { "visa", typeof(VisaTable) },
        { "pcr_test", typeof(PcrTestTable) },
        { "employment_cert", typeof(EmploymentCertTable) },
        { "day_schedule", typeof(DayScheduleTable) },
        { "document_requirement", typeof(DocumentRequirementTable) },
        { "rule_book", typeof(RuleBookTable) },
        { "news", typeof(NewsTable) },
        { "defect_rule", typeof(DefectRuleTable) },
        { "fake_value_pool", typeof(FakeValuePoolTable) },
        { "xray", typeof(XrayTable) },
        { "fingerprint", typeof(FingerprintTable) },
        { "dialogue_case", typeof(DialogueCaseTable) },
        { "dialogue_line", typeof(DialogueLineTable) },
        { "shop", typeof(ShopTable) },
        { "reward", typeof(RewardTable) },
        { "ending", typeof(EndingTable) },
        { "score_model", typeof(ScoreModelTable) },
        // 260602 분기표 (2번째 소스 xlsx). 시트 없으면 LoadOrCreate가 빈 .asset 만들지 않게 아래서 가드.
        { "character_score", typeof(CharacterScoreTable) },
        { "character_payout", typeof(CharacterPayoutTable) },
    };

    [MenuItem("Tools/Passport/Import Data")]
    public static void ImportData()
    {
        if (!File.Exists(SourceJson))
        {
            EditorUtility.DisplayDialog("Passport Import",
                $"중간 JSON이 없습니다:\n{SourceJson}\n\n먼저 Tools/DataImport/xlsx_to_json.py 를 실행하세요.", "확인");
            return;
        }

        string text = File.ReadAllText(SourceJson, System.Text.Encoding.UTF8);
        var root = MiniJson.AsObj(MiniJson.Parse(text));
        if (root == null)
        {
            Debug.LogError("[PassportDataImporter] JSON 파싱 실패");
            return;
        }

        var sheets = MiniJson.AsObj(root["sheets"]);
        if (sheets == null)
        {
            Debug.LogError("[PassportDataImporter] 'sheets' 없음");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets", "GameData");

        var created = new Dictionary<string, DataTableAsset>();
        int sheetCount = 0, rowCount = 0;

        foreach (var kv in SheetToType)
        {
            string sheetName = kv.Key;
            Type soType = kv.Value;
            if (!sheets.ContainsKey(sheetName))
            {
                Debug.LogWarning($"[PassportDataImporter] 시트 누락(스킵): {sheetName}");
                continue;
            }

            var sheet = MiniJson.AsObj(sheets[sheetName]);
            var asset = LoadOrCreate(soType, sheetName);
            FillTable(asset, sheetName, sheet);
            created[sheetName] = asset;
            sheetCount++;
            rowCount += asset.rows.Count;
            EditorUtility.SetDirty(asset);
        }

        // ── GameDatabase 연결 ──
        var db = LoadOrCreateDatabase();
        WireDatabase(db, created);
        EditorUtility.SetDirty(db);

        // ── 런타임 Resources 사본 동기화 ──
        // GameDatabaseProvider가 Resources/GameData/GameDatabase 를 로드한다.
        // 사본도 같은 테이블 .asset(GUID 동일)을 참조하도록 항상 재연결해 둔다(idempotent).
        var runtimeDb = LoadOrCreateRuntimeDatabase();
        if (runtimeDb != null)
        {
            WireDatabase(runtimeDb, created);
            EditorUtility.SetDirty(runtimeDb);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PassportDataImporter] 완료: 시트 {sheetCount}개, 행 {rowCount}개 -> {OutDir}/*.asset (GameDatabase + Resources 사본 연결됨)");
    }

    /// <summary>JSON 시트 -> SO(columns + rows) 채우기. 키-값 그대로 보존.</summary>
    private static void FillTable(DataTableAsset asset, string sheetName, Dictionary<string, object> sheet)
    {
        asset.sheetName = sheetName;
        asset.columns = new List<FieldMeta>();
        asset.rows = new List<DataRow>();

        var cols = MiniJson.AsArr(sheet["columns"]);
        if (cols != null)
        {
            foreach (var c in cols)
            {
                var co = MiniJson.AsObj(c);
                if (co == null) continue;
                asset.columns.Add(new FieldMeta
                {
                    key = MiniJson.AsStr(co.GetValueOrDefault("key")),
                    label = MiniJson.AsStr(co.GetValueOrDefault("label")),
                    type = MiniJson.AsStr(co.GetValueOrDefault("type")),
                });
            }
        }

        var rows = MiniJson.AsArr(sheet["rows"]);
        if (rows != null)
        {
            foreach (var r in rows)
            {
                var ro = MiniJson.AsObj(r);
                if (ro == null) continue;
                var row = new DataRow();
                // 컬럼 순서대로 채워 안정적 직렬화(idempotent)
                if (asset.columns.Count > 0)
                {
                    foreach (var col in asset.columns)
                        row.Set(col.key, ro.TryGetValue(col.key, out var v) ? MiniJson.AsStr(v) : null);
                }
                else
                {
                    foreach (var pair in ro)
                        row.Set(pair.Key, MiniJson.AsStr(pair.Value));
                }
                asset.rows.Add(row);
            }
        }
    }

    private static DataTableAsset LoadOrCreate(Type soType, string sheetName)
    {
        string fileName = SheetToAssetName(sheetName);
        string path = $"{OutDir}/{fileName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath(path, soType) as DataTableAsset;
        if (existing != null) return existing;

        // 기존 파일이 있는데 위 로드가 null이면 m_Script 미연결(fileID:0) 등으로
        // 타입 매핑이 깨진 상태(P0). 파일을 지우고 올바른 타입으로 재생성한다.
        if (File.Exists(path))
        {
            AssetDatabase.DeleteAsset(path);
            Debug.LogWarning($"[PassportDataImporter] 손상 .asset 재생성(m_Script 미연결 추정): {path}");
        }

        var so = (DataTableAsset)ScriptableObject.CreateInstance(soType);
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static GameDatabase LoadOrCreateDatabase()
    {
        string path = $"{OutDir}/GameDatabase.asset";
        var existing = AssetDatabase.LoadAssetAtPath<GameDatabase>(path);
        if (existing != null) return existing;
        var db = ScriptableObject.CreateInstance<GameDatabase>();
        AssetDatabase.CreateAsset(db, path);
        return db;
    }

    private const string RuntimeDbDir = "Assets/Resources/GameData";

    /// <summary>런타임 로드용 Resources 사본. 없으면 생성, 있으면 재사용(GUID 유지).</summary>
    private static GameDatabase LoadOrCreateRuntimeDatabase()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(RuntimeDbDir))
            AssetDatabase.CreateFolder("Assets/Resources", "GameData");

        string path = $"{RuntimeDbDir}/GameDatabase.asset";
        var existing = AssetDatabase.LoadAssetAtPath<GameDatabase>(path);
        if (existing != null) return existing;
        var db = ScriptableObject.CreateInstance<GameDatabase>();
        AssetDatabase.CreateAsset(db, path);
        return db;
    }

    private static void WireDatabase(GameDatabase db, Dictionary<string, DataTableAsset> t)
    {
        db.customer = Get<CustomerTable>(t, "customer");
        db.passport = Get<PassportTable>(t, "passport");
        db.visa = Get<VisaTable>(t, "visa");
        db.pcrTest = Get<PcrTestTable>(t, "pcr_test");
        db.employmentCert = Get<EmploymentCertTable>(t, "employment_cert");
        db.daySchedule = Get<DayScheduleTable>(t, "day_schedule");
        db.documentRequirement = Get<DocumentRequirementTable>(t, "document_requirement");
        db.ruleBook = Get<RuleBookTable>(t, "rule_book");
        db.news = Get<NewsTable>(t, "news");
        db.defectRule = Get<DefectRuleTable>(t, "defect_rule");
        db.fakeValuePool = Get<FakeValuePoolTable>(t, "fake_value_pool");
        db.xray = Get<XrayTable>(t, "xray");
        db.fingerprint = Get<FingerprintTable>(t, "fingerprint");
        db.dialogueCase = Get<DialogueCaseTable>(t, "dialogue_case");
        db.dialogueLine = Get<DialogueLineTable>(t, "dialogue_line");
        db.shop = Get<ShopTable>(t, "shop");
        db.reward = Get<RewardTable>(t, "reward");
        db.ending = Get<EndingTable>(t, "ending");
        db.scoreModel = Get<ScoreModelTable>(t, "score_model");
        db.characterScore = Get<CharacterScoreTable>(t, "character_score");
        db.characterPayout = Get<CharacterPayoutTable>(t, "character_payout");
    }

    private static T Get<T>(Dictionary<string, DataTableAsset> t, string key) where T : DataTableAsset
        => t.TryGetValue(key, out var v) ? v as T : null;

    /// <summary>시트명 -> .asset 파일명 (PascalCase + Table). 예: pcr_test -> PcrTestTable</summary>
    private static string SheetToAssetName(string sheetName)
    {
        var parts = sheetName.Split('_');
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p.Substring(1));
        }
        sb.Append("Table");
        return sb.ToString();
    }
}
