using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진행 상태(점수/돈/정확도/호칭/아이템/엔딩카운터/현자카운터)의 PlayerPrefs 영속화.
/// 기존 MainMenuManager 의 PlayerPrefs 세이브 패턴을 계승한다.
/// 컬렉션은 구분자 문자열로 직렬화(JsonUtility 의 컬렉션 제약 회피).
/// </summary>
public static class GameProgressSave
{
    private const string KScore   = "SE_Score";
    private const string KMoney   = "SE_Money";
    private const string KCorrect = "SE_Correct";
    private const string KJudged  = "SE_Judged";
    private const string KTitles  = "SE_Titles";   // '|' 구분
    private const string KItems   = "SE_Items";    // '|' 구분
    private const string KEvents  = "SE_Events";   // "id:count|id:count"
    private const string KSage    = "SE_SageCount";
    private const char Sep = '|';

    /// <summary>저장된 진행이 있는가.</summary>
    public static bool HasSave() => PlayerPrefs.HasKey(KScore);

    /// <summary>현재 상태를 PlayerPrefs 에 저장.</summary>
    public static void SaveFrom(ScoreEconomyManager m)
    {
        if (m == null) return;
        PlayerPrefs.SetInt(KScore, m.Score);
        PlayerPrefs.SetInt(KMoney, m.Money);
        PlayerPrefs.SetInt(KCorrect, m.CorrectCount);
        PlayerPrefs.SetInt(KJudged, m.JudgedCount);
        PlayerPrefs.SetInt(KSage, m.SageApproveCount);
        PlayerPrefs.SetString(KTitles, Join(m.Titles));
        PlayerPrefs.SetString(KItems, Join(m.Items));
        PlayerPrefs.SetString(KEvents, JoinEvents(m.EventCounters));
        PlayerPrefs.Save();
    }

    /// <summary>저장값을 매니저로 복원(없으면 초기 상태 유지).</summary>
    public static void LoadInto(ScoreEconomyManager m)
    {
        if (m == null || !HasSave()) return;
        int score = PlayerPrefs.GetInt(KScore, 0);
        int money = PlayerPrefs.GetInt(KMoney, 0);
        int correct = PlayerPrefs.GetInt(KCorrect, 0);
        int judged = PlayerPrefs.GetInt(KJudged, 0);
        int sage = PlayerPrefs.GetInt(KSage, 0);
        var titles = Split(PlayerPrefs.GetString(KTitles, ""));
        var items = Split(PlayerPrefs.GetString(KItems, ""));
        var events = SplitEvents(PlayerPrefs.GetString(KEvents, ""));
        m.RestoreState(score, money, correct, judged, titles, items, events, sage);
    }

    /// <summary>세이브 삭제(새 게임).</summary>
    public static void Clear()
    {
        foreach (var k in new[] { KScore, KMoney, KCorrect, KJudged, KTitles, KItems, KEvents, KSage })
            PlayerPrefs.DeleteKey(k);
        PlayerPrefs.Save();
    }

    // ── 직렬화 헬퍼 ────────────────────────────────────────────

    private static string Join(IEnumerable<string> values)
        => values == null ? "" : string.Join(Sep.ToString(), values);

    private static List<string> Split(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(Sep))
            if (!string.IsNullOrEmpty(part)) list.Add(part);
        return list;
    }

    private static string JoinEvents(IReadOnlyDictionary<string, int> events)
    {
        if (events == null || events.Count == 0) return "";
        var parts = new List<string>(events.Count);
        foreach (var kv in events) parts.Add($"{kv.Key}:{kv.Value}");
        return string.Join(Sep.ToString(), parts);
    }

    private static Dictionary<string, int> SplitEvents(string s)
    {
        var d = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(s)) return d;
        foreach (var part in s.Split(Sep))
        {
            int idx = part.LastIndexOf(':');
            if (idx <= 0) continue;
            string id = part.Substring(0, idx);
            if (int.TryParse(part.Substring(idx + 1), out int n)) d[id] = n;
        }
        return d;
    }
}
