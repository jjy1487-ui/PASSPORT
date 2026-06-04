using System.Collections.Generic;
using UnityEngine;

/// <summary>상점. shop_item_id, item_name, category, price, unlock_day, effect_type, effect_value, effect</summary>
[CreateAssetMenu(fileName = "ShopTable", menuName = "Passport/Data/Shop Table")]
public class ShopTable : DataTableAsset
{
    /// <summary>shop_item_id 로 행 조회. 없으면 null.</summary>
    public DataRow FindById(string shopItemId) => FindBy("shop_item_id", shopItemId);

    /// <summary>effect_type 으로 첫 행 조회. 없으면 null.</summary>
    public DataRow FindByEffect(string effectType) => FindBy("effect_type", effectType);

    /// <summary>해당 일차에 구매 가능한(unlock_day &lt;= day) 모든 품목 행을 반환한다.</summary>
    public List<DataRow> AvailableOn(int day)
    {
        var list = new List<DataRow>();
        foreach (var r in rows)
        {
            if (r == null) continue;
            if (r.GetInt("unlock_day", int.MaxValue) <= day) list.Add(r);
        }
        return list;
    }
}
