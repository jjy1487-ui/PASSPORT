using UnityEngine;

/// <summary>보상(점수/돈 트리거). reward_id, reward_type, trigger_type, trigger_condition, amount, related_ending_id, memo</summary>
[CreateAssetMenu(fileName = "RewardTable", menuName = "Passport/Data/Reward Table")]
public class RewardTable : DataTableAsset
{
    /// <summary>trigger_type(DAILY_BASE/DETECTION/PERFECT_DAY/WARNING…)으로 행 조회. 없으면 null.</summary>
    public DataRow FindByTrigger(string triggerType) => FindBy("trigger_type", triggerType);

    /// <summary>trigger_type 의 amount(금액). 행/값 없으면 fallback.</summary>
    public int GetAmount(string triggerType, int fallback = 0)
    {
        var row = FindByTrigger(triggerType);
        if (row == null) return fallback;
        var v = row.Get("amount");
        if (string.IsNullOrEmpty(v)) return fallback;
        v = v.Replace("+", "").Trim();
        return int.TryParse(v, out var n) ? n : fallback;
    }

    /// <summary>trigger_type 의 related_ending_id(엔딩 연계). 없으면 빈 문자열.</summary>
    public string GetRelatedEndingId(string triggerType)
    {
        var row = FindByTrigger(triggerType);
        var v = row?.Get("related_ending_id");
        return string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim();
    }
}
