using UnityEngine;

/// <summary>점수/돈 상수 config (key-value). 행: item, value, desc.
/// 예: 정답(JUDGE_CORRECT)=+10, 오판(JUDGE_WRONG)=-15, 일급(DAILY_BASE)=+100 ...</summary>
[CreateAssetMenu(fileName = "ScoreModelTable", menuName = "Passport/Data/Score Model Table")]
public class ScoreModelTable : DataTableAsset
{
    /// <summary>config 값 조회: item 문자열로 value 반환. 괄호 안 코드(JUDGE_CORRECT 등)로도 매칭.</summary>
    public string GetValue(string itemOrCode)
    {
        foreach (var r in rows)
        {
            if (r == null) continue;
            var item = r.Get("item");
            if (item == null) continue;
            if (item == itemOrCode || item.Contains(itemOrCode)) return r.Get("value");
        }
        return null;
    }

    /// <summary>+10 / -15 같은 부호 포함 정수 파싱.</summary>
    public int GetInt(string itemOrCode, int fallback = 0)
    {
        var v = GetValue(itemOrCode);
        if (v == null) return fallback;
        v = v.Replace("+", "").Trim();
        return int.TryParse(v, out var n) ? n : fallback;
    }
}
