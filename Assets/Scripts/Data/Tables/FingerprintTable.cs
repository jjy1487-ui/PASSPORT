using UnityEngine;

/// <summary>지문 신원(옵션B). fingerprint_id, customer_id, mode(성형|수배자), alt_name, alt_birth, alt_nationality, criminal_record, wanted_no.
/// 본인/도용 갈림은 day_schedule.valid_chance 재사용(별도 확률 컬럼 없음). build_days 가 등장마다 본인=여권신원 복제 / 도용·수배=alt 신원으로 record 생성.</summary>
[CreateAssetMenu(fileName = "FingerprintTable", menuName = "Passport/Data/Fingerprint Table")]
public class FingerprintTable : DataTableAsset { }
