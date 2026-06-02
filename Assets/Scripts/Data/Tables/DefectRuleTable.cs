using UnityEngine;

/// <summary>결함 규칙. rule_id, character_type, context, defect_document, violation_field,
/// normal_example, defect_example, secondary_check, random_valid_chance, note, corruption_type, target_field.
/// 주의: random_valid_chance에 '고정(통과1/거절0)' 같은 비수치 값이 있을 수 있음(rule_id=6) -> 런타임 특수처리.</summary>
[CreateAssetMenu(fileName = "DefectRuleTable", menuName = "Passport/Data/Defect Rule Table")]
public class DefectRuleTable : DataTableAsset { }
