using UnityEngine;

/// <summary>
/// 심사 오류 고지서(거절 시 발부하는 citation). 위반 유형별 고지 문구 템플릿.
/// rejection_templates / defect_rule / rule_book 과 정합.
/// 컬럼: notice_id, error_type, document_type, violation_field, title, body,
///       legal_basis, severity, rule_ref(→rule_book.rule_id), note
/// </summary>
[CreateAssetMenu(fileName = "InspectionNoticeTable", menuName = "Passport/Data/Inspection Notice Table")]
public class InspectionNoticeTable : DataTableAsset
{
    /// <summary>위반 유형(error_type) + 대상 서류(document_type)로 고지서 1건 조회. 없으면 null.</summary>
    public DataRow Find(string errorType, string documentType = null)
    {
        foreach (var r in rows)
        {
            if (r == null || r.Get("error_type") != errorType) continue;
            if (documentType == null || r.Get("document_type") == documentType) return r;
        }
        return null;
    }
}
