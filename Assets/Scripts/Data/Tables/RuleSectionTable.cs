using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 세분화 규정집(정적 매뉴얼). 탭(기본/서류/물품) → 좌측 항목 버튼(category) →
/// 우측 내용(title/body/image)을 데이터로 표현한다.
/// rule_book(날짜별 브리핑)·dayN.json rules·inspection_notice 와는 별개 개념(정적 매뉴얼).
/// 컬럼: section_id, tab, tab_order, category, category_order, title, body,
///       image_ref(Resources/RuleBook/&lt;key&gt;), content_type(text/image/both),
///       unlock_day(0=항상), priority(특별지시↑), related_field, note
/// </summary>
[CreateAssetMenu(fileName = "RuleSectionTable", menuName = "Passport/Data/Rule Section Table")]
public class RuleSectionTable : DataTableAsset
{
    /// <summary>탭+항목(category)으로 섹션 1건 조회. 없으면 null.</summary>
    public DataRow Find(string tab, string category)
    {
        foreach (var r in rows)
        {
            if (r == null) continue;
            if (r.Get("tab") == tab && r.Get("category") == category) return r;
        }
        return null;
    }

    /// <summary>한 탭에 속한 섹션 전부를 category_order 오름차순으로 반환(좌측 항목 버튼 목록용).</summary>
    public List<DataRow> ByTab(string tab)
    {
        var list = new List<DataRow>();
        foreach (var r in rows)
            if (r != null && r.Get("tab") == tab) list.Add(r);
        list.Sort((a, b) => a.GetInt("category_order").CompareTo(b.GetInt("category_order")));
        return list;
    }
}
