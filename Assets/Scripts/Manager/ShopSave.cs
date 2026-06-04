using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 구매 내역(영구 활성 효과 + 1회성 소비 수량)의 PlayerPrefs 영속화.
/// GameProgressSave 의 구분자 직렬화 패턴을 그대로 따른다(JsonUtility 컬렉션 제약 회피).
/// </summary>
public static class ShopSave
{
    private const string KActive     = "SHOP_Active";      // '|' 구분(effect_type 집합)
    private const string KConsumable = "SHOP_Consumable";  // "effect:count|effect:count"
    private const char Sep = '|';

    /// <summary>현재 상태를 PlayerPrefs 에 저장.</summary>
    public static void SaveFrom(ShopService s)
    {
        if (s == null) return;
        PlayerPrefs.SetString(KActive, Join(s.ActiveEffectsRaw));
        PlayerPrefs.SetString(KConsumable, JoinCounts(s.ConsumablesRaw));
        PlayerPrefs.Save();
    }

    /// <summary>저장값을 서비스로 복원(없으면 빈 상태 유지).</summary>
    public static void LoadInto(ShopService s)
    {
        if (s == null) return;
        var active = Split(PlayerPrefs.GetString(KActive, ""));
        var consumables = SplitCounts(PlayerPrefs.GetString(KConsumable, ""));
        s.RestoreState(active, consumables);
    }

    /// <summary>세이브 삭제(새 게임).</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(KActive);
        PlayerPrefs.DeleteKey(KConsumable);
        PlayerPrefs.Save();
    }

    // ── 직렬화 헬퍼 ────────────────────────────────────────────

    private static string Join(IReadOnlyCollection<string> values)
        => values == null ? "" : string.Join(Sep.ToString(), values);

    private static List<string> Split(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(Sep))
            if (!string.IsNullOrEmpty(part)) list.Add(part);
        return list;
    }

    private static string JoinCounts(IReadOnlyDictionary<string, int> counts)
    {
        if (counts == null || counts.Count == 0) return "";
        var parts = new List<string>(counts.Count);
        foreach (var kv in counts) if (kv.Value > 0) parts.Add($"{kv.Key}:{kv.Value}");
        return string.Join(Sep.ToString(), parts);
    }

    private static Dictionary<string, int> SplitCounts(string s)
    {
        var d = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(s)) return d;
        foreach (var part in s.Split(Sep))
        {
            int idx = part.LastIndexOf(':');
            if (idx <= 0) continue;
            string key = part.Substring(0, idx);
            if (int.TryParse(part.Substring(idx + 1), out int n) && n > 0) d[key] = n;
        }
        return d;
    }
}
