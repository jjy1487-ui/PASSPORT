using UnityEngine;

/// <summary>일과 스케줄(유형 기반). schedule_id, day(1~14), slot(1~7), character_type, valid_chance(정상 확률).
/// 특정 customer_id 를 고정하지 않고 슬롯별 '유형 + 정상확률'만 정의 → 실제 인물은 유형 풀에서 선택(CustomerRoster).</summary>
[CreateAssetMenu(fileName = "DayScheduleTable", menuName = "Passport/Data/Day Schedule Table")]
public class DayScheduleTable : DataTableAsset { }
