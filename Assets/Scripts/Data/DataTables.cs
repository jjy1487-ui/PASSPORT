using UnityEngine;

// ─────────────────────────────────────────────────────────────
//  DataTables — 고객 테이블 정의 (data-tools 소유)
//  주의: Unity는 .cs 1개당 MonoScript 1개(파일 첫 클래스)만 노출한다.
//  과거 이 파일에 19+개 테이블을 몰아넣어 CustomerTable(첫 클래스) 외
//  전부 m_Script={fileID:0}로 깨졌다(P0). 그래서 테이블마다 .cs를 분리했다.
//  이 파일은 CustomerTable 전용으로 남겨 기존 .asset 참조(guid) 보존.
// ─────────────────────────────────────────────────────────────

/// <summary>고객 마스터. customer_id, name_kr, name_en, nationality, gender, birth_date, age, sprite_ref, character_type</summary>
[CreateAssetMenu(fileName = "CustomerTable", menuName = "Passport/Data/Customer Table")]
public class CustomerTable : DataTableAsset { }
