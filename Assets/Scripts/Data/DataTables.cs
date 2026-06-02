using UnityEngine;

// ─────────────────────────────────────────────────────────────
//  DataTables — 19개 테이블 ScriptableObject 정의 (data-tools 소유)
//  모두 DataTableAsset(키-값) 상속. 기존 Inspection 프로토타입의
//  CustomerData/DialogueCaseData 등과 이름 충돌 피하려 *Table 접미사 사용.
//  필드는 키-값으로 보존하므로 컬럼 변경 시 임포터만 수정하면 된다(규약 6장).
// ─────────────────────────────────────────────────────────────

// ── 3.1 고객 & 진실 서류 ──────────────────────────────────────

/// <summary>고객 마스터. customer_id, name_kr, name_en, nationality, gender, birth_date, age, sprite_ref, character_type</summary>
[CreateAssetMenu(fileName = "CustomerTable", menuName = "Passport/Data/Customer Table")]
public class CustomerTable : DataTableAsset { }

/// <summary>여권(진실). passport_id, customer_id, passport_no, name_en, gender, birth_date, nationality, issue_date, expiry_date, photo_ref</summary>
[CreateAssetMenu(fileName = "PassportTable", menuName = "Passport/Data/Passport Table")]
public class PassportTable : DataTableAsset { }

/// <summary>비자(진실). visa_id, customer_id, visa_no, visa_type, nationality, issue_date, expiry_date, entry_type, memo</summary>
[CreateAssetMenu(fileName = "VisaTable", menuName = "Passport/Data/Visa Table")]
public class VisaTable : DataTableAsset { }

/// <summary>PCR 검사서(진실). pcr_id, customer_id, test_no, test_date, result, valid_until, lab_name, memo</summary>
[CreateAssetMenu(fileName = "PcrTestTable", menuName = "Passport/Data/PCR Test Table")]
public class PcrTestTable : DataTableAsset { }

/// <summary>취업증빙(진실). employment_id, customer_id, cert_no, company_name, job_title, issue_date, expiry_date</summary>
[CreateAssetMenu(fileName = "EmploymentCertTable", menuName = "Passport/Data/Employment Cert Table")]
public class EmploymentCertTable : DataTableAsset { }

// ── 3.2 진행 & 규칙 ──────────────────────────────────────────

/// <summary>일과 스케줄. schedule_id, day(1~14), slot(1~7), customer_id, valid_chance</summary>
[CreateAssetMenu(fileName = "DayScheduleTable", menuName = "Passport/Data/Day Schedule Table")]
public class DayScheduleTable : DataTableAsset { }

/// <summary>요구 서류. req_id, document_type, day_from, day_to, applies_to, note</summary>
[CreateAssetMenu(fileName = "DocumentRequirementTable", menuName = "Passport/Data/Document Requirement Table")]
public class DocumentRequirementTable : DataTableAsset { }

/// <summary>규정집(날짜별 브리핑). rule_id, day, rule_title, rule_content, related_field</summary>
[CreateAssetMenu(fileName = "RuleBookTable", menuName = "Passport/Data/Rule Book Table")]
public class RuleBookTable : DataTableAsset { }

/// <summary>뉴스(날짜별 연출). news_id, day, news_title, news_content, icon_ref</summary>
[CreateAssetMenu(fileName = "NewsTable", menuName = "Passport/Data/News Table")]
public class NewsTable : DataTableAsset { }

// ── 3.3 변조 (규칙 데이터, 진실 아님) ─────────────────────────

/// <summary>결함 규칙. rule_id, character_type, context, defect_document, violation_field,
/// normal_example, defect_example, secondary_check, random_valid_chance, note, corruption_type, target_field.
/// 주의: random_valid_chance에 '고정(통과1/거절0)' 같은 비수치 값이 있을 수 있음(rule_id=6) -> 런타임 특수처리.</summary>
[CreateAssetMenu(fileName = "DefectRuleTable", menuName = "Passport/Data/Defect Rule Table")]
public class DefectRuleTable : DataTableAsset { }

/// <summary>가짜값 풀. pool_id, field, fake_value, note</summary>
[CreateAssetMenu(fileName = "FakeValuePoolTable", menuName = "Passport/Data/Fake Value Pool Table")]
public class FakeValuePoolTable : DataTableAsset { }

// ── 3.4 보조 검사 ────────────────────────────────────────────

/// <summary>X-ray 결과. xray_id, customer_id, result, detected_item, hidden_location</summary>
[CreateAssetMenu(fileName = "XrayTable", menuName = "Passport/Data/Xray Table")]
public class XrayTable : DataTableAsset { }

/// <summary>지문 결과. fingerprint_id, customer_id, result, match_status, matched_person</summary>
[CreateAssetMenu(fileName = "FingerprintTable", menuName = "Passport/Data/Fingerprint Table")]
public class FingerprintTable : DataTableAsset { }

// ── 3.5 대사 ─────────────────────────────────────────────────

/// <summary>대화 흐름 케이스. dialogue_case_id, schedule_id, customer_id, case_type, character_type,
/// document_type, doc_valid, violation_field, game_result, reject_count</summary>
[CreateAssetMenu(fileName = "DialogueCaseTable", menuName = "Passport/Data/Dialogue Case Table")]
public class DialogueCaseTable : DataTableAsset { }

/// <summary>대사 라인. line_id, dialogue_case_id, line_order, speaker, text_kr</summary>
[CreateAssetMenu(fileName = "DialogueLineTable", menuName = "Passport/Data/Dialogue Line Table")]
public class DialogueLineTable : DataTableAsset { }

// ── 3.6 경제 & 엔딩 ─────────────────────────────────────────

/// <summary>상점. shop_item_id, item_name, category, price, unlock_day, effect_type, effect_value, effect</summary>
[CreateAssetMenu(fileName = "ShopTable", menuName = "Passport/Data/Shop Table")]
public class ShopTable : DataTableAsset { }

/// <summary>보상(점수/돈 트리거). reward_id, reward_type, trigger_type, trigger_condition, amount, related_ending_id, memo</summary>
[CreateAssetMenu(fileName = "RewardTable", menuName = "Passport/Data/Reward Table")]
public class RewardTable : DataTableAsset { }

/// <summary>엔딩. ending_id, ending_type, ending_name, score_min, score_max, end_timing, trigger_type, trigger_condition</summary>
[CreateAssetMenu(fileName = "EndingTable", menuName = "Passport/Data/Ending Table")]
public class EndingTable : DataTableAsset { }

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
