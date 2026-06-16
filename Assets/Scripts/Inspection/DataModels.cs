using System;

/// <summary>
/// 1일차 데이터(day1.json)를 JsonUtility로 역직렬화하기 위한 DTO 모음.
/// JsonUtility 제약: 루트는 객체여야 하고(배열 불가), 모든 필드는 public, 다형성 불가.
/// 따라서 서류는 공통 fields[] 구조로 평탄화한다.
/// </summary>
[Serializable]
public sealed class Day1Data
{
    public int day;
    public CustomerData[] customers;
    public RuleData[] rules;
    public NewsData[] news;
}

/// <summary>손님 1명 + 제출 서류 + 대화 케이스.</summary>
[Serializable]
public sealed class CustomerData
{
    public int customerId;
    public int slot;
    public string nameKr;
    public string nameEn;
    public string nationality;
    public string gender;
    public string birthDate;
    public int age;
    public string spriteRef;      // 후일 실제 초상 스프라이트 키
    public string characterType;
    public string defectVariant;  // 결함/상태 세부 분기 키(BRANCH_CATALOG 어휘: 예 관광객 "분실"/"출국X", 전염병 "1-A"~"1-D"). 정상/변이 없으면 "". 데이터 운반용(로직 변경 없음).
    public string correctResult;  // "정상 승인" | "정상 거절"
    public string rejectAdvancedBranchKey; // 비어있지 않으면: 이 손님 거부 시 오거부 3회 루프 대신 이 branch_key(예 "detect_montage_xray_reject")로 1회 정산하고 가이드 대사를 재생. 일반 손님은 "".
    public string rejectGuidedCaseType;    // 가이드 거부 대사 케이스의 caseType(예 "분기 거부"). 비면 폴백.
    public DocumentData[] documents;
    public DialogueCaseData[] dialogueCases;
    public ScanData xray;          // 보조검사 결과(없으면 null)
    public ScanData fingerprint;   // 보조검사 결과(없으면 null)
    public CrossCheckLine[] crossCheckLines; // 교차 대조 불일치 시 이 손님 전용 대사(없으면 빈/널 → 일반 문구 폴백)

    // 입장 시 수하물 X-ray 자동 검사(day11 "보안 강화" 흐름). true 면 입장 대사 직후 X-ray 패널이 자동으로 열린다.
    //  - day11 처럼 그날 전원 검사받는 날에만 켠다(기본 false). 다른 날 X-ray 손님(사토 등)은 false 라 입장 자동 오픈 안 됨.
    //  - 자동 오픈은 그 손님이 xray 데이터를 가질 때만 실제로 열린다(없으면 무시).
    //  - 변형 손님(altVariant)에도 적용하려면 손님 레벨에 두므로 RollVariants 오버레이(서류/검사만 교체)와 독립적으로 보존된다.
    public bool autoScanOnEntry;

    // ── 확률 변형(박철수처럼 매 플레이 서류 정상/불량이 갈리는 손님) ──
    public float validChance;          // 정상(승인) 확률 0~1. 0/1 또는 altVariant 없음 → 굴리지 않음(고정).
    public CustomerVariant altVariant; // 반대 변형(없으면 null). 런타임에 validChance로 굴려 이 손님 위에 오버레이한다.
}

/// <summary>
/// 확률 손님의 '반대 변형' 묶음(정상↔불량). 신원(이름·얼굴·생년월일 등)은 그대로 두고,
/// 런타임 굴림 결과가 baked 와 다르면 아래 항목만 손님 위에 덮어쓴다(서류/대사/정답/검사).
/// JsonUtility 호환: 자기참조 없음(altVariant 미포함).
/// </summary>
[Serializable]
public sealed class CustomerVariant
{
    public string correctResult;           // "정상 승인" | "정상 거절"
    public string defectVariant;
    public string rejectAdvancedBranchKey;
    public string rejectGuidedCaseType;
    public DocumentData[] documents;
    public DialogueCaseData[] dialogueCases;
    public ScanData xray;
    public ScanData fingerprint;
    public CrossCheckLine[] crossCheckLines; // 변형이 적용되면 손님 위에 함께 덮어쓴다(불량 변형의 대조 대사).
}

/// <summary>
/// 교차 대조(서류↔서류 등)에서 특정 속성이 불일치할 때 재생할, 그 손님 전용 검사관·손님 대사 1쌍.
/// 대사_스크립트.xlsx 의 "서류 대조" 단계 대사를 손님별로 운반한다.
/// 대조 결과가 Mismatch 이고 <see cref="attr"/>(속성 키)가 일치하는 항목이 있으면 일반 문구 대신 이걸 쓴다.
/// 비면 CrossCheckController 가 항목별 일반 취조 문구로 폴백한다(데이터 없어도 동작 보장).
/// </summary>
[Serializable]
public sealed class CrossCheckLine
{
    public string attr;       // 불일치 속성 키(passport_no/gender/expiry_date/name/nationality/company_name/lab_name/pcr_result/test_date/hire_date 등). FieldEntry.key 어휘.
    public string inspector;  // 검사관 지적 대사(예: "비자에 적힌 여권번호와 여권의 번호가 다른데요? 본인 여권이 맞습니까?")
    public string customer;   // 손님 반응 대사(예: "…(말없이 주위를 살핀다)")
    public string inspectorClose; // (선택) 손님 반응 뒤 검사관 마무리 한 줄(예: "성별이 본인하고 같아야 들여보내 드릴 수 있어요."). 비면 2줄로 끝.
}

/// <summary>
/// 보조검사(xray/fingerprint) 결과 1건. 검사기 버튼으로 열람하고, <see cref="claim"/>을 다른 소스(여권 이름/뉴스 등)와 대조한다.
/// </summary>
/// <remarks>
/// xray: detail=detected_item(적발물), extra=hidden_location(은닉 위치), claim.attr="contraband".
/// fingerprint: detail=match_status(대조 상태), extra=matched_person(일치 인물), claim.attr="name".
/// SHARED-CONVENTIONS 3.8 참조.
/// </remarks>
[Serializable]
public sealed class ScanData
{
    public string type;    // "xray" | "fingerprint"
    public string result;  // "적발" / "정상" / "위험" 등
    public string detail;  // xray=detected_item / fingerprint=match_status
    public string extra;   // xray=hidden_location / fingerprint=matched_person
    public Claim claim;    // 대조용 단서(여권 이름/뉴스 등과 같은 속성 키로 비교)
    public FingerprintRecord record; // fingerprint 전용: DB 조회 레코드(스캔 3단계 표시용). 없으면 null.
}

/// <summary>
/// 지문판독기 DB 조회 레코드(성형수술 손님용). 지문으로 식별한 "진짜 신원" + 범죄기록.
/// 판정은 시스템이 하지 않는다 — 플레이어가 이 DB 신원을 여권 정보와 직접 대조한다(교차 대조).
///   DB 이름/생년월일 ↔ 여권 이름/생년월일 → 다르면 도용. 범죄기록 있으면 수배자.
/// </summary>
[Serializable]
public sealed class FingerprintRecord
{
    public string mode;           // "성형" | "수배자" (지문 시트 mode 컬럼). 본인/도용 갈림과 무관한 등장 유형.
    public string dbName;         // 지문으로 식별한 실제 이름 (본인=여권 영문이름 동일 / 도용·수배=다른 신원)
    public string dbBirth;        // 실제 생년월일 (여권 형식 YYYY-MM-DD)
    public string dbNationality;  // 실제 국적 (여권 형식 국가코드 KOR 등)
    public string criminalRecord; // 범죄/수배 기록 ("없음" 또는 "성형 위장 / 지명수배 중")
    public string wantedNo;       // 수배 번호 (수배자만, 없으면 "")

    /// <summary>DB에 범죄/수배 기록이 있는가(표시·경고용. 판정은 플레이어가 함).</summary>
    public bool IsWanted =>
        !string.IsNullOrEmpty(criminalRecord) && criminalRecord.Trim() != "없음";
}

/// <summary>서류 1장(여권/비자/PCR/취업증빙 공통). 세부 항목은 fields[]로.</summary>
[Serializable]
public sealed class DocumentData
{
    public string documentType;
    public string variant;        // "정상" | "비정상"
    public string violationField; // "없음" 또는 위반 항목
    public string spriteRef;      // 후일 실제 사진 스프라이트 키
    public string country;        // 여권 표지 선택용 국가코드(KOR/CHN/JPN/USA 등). 비여권은 빈 값
    public FieldEntry[] fields;
}

/// <summary>서류 항목 한 줄 (라벨: 값).</summary>
/// <remarks>
/// <see cref="key"/>는 이종 소스 간 대조용 속성 키(영문, snake_case). 표시는 <see cref="label"/>(한글), 비교는 <see cref="key"/>로 한다.
/// 속성 키 어휘는 SHARED-CONVENTIONS 3.8 참조. 빈 문자열이면 대조 대상 아님.
/// </remarks>
[Serializable]
public sealed class FieldEntry
{
    public string label;
    public string value;
    public string key;     // 속성 키(예: name/nationality/expiry_date). 비교 기준. 없으면 "".
}

/// <summary>
/// 이종 소스(뉴스/대화 등)에서 사람·서류와 대조 가능한 단서 한 건.
/// attr=속성 키(FieldEntry.key와 같은 어휘), value=대조할 값(없으면 ""), label=한글 표시.
/// </summary>
[Serializable]
public sealed class Claim
{
    public string attr;    // 속성 키 (name/nationality/expiry_date ...)
    public string value;   // 대조 값 (예: "VNM"). 값 비교가 무의미하면 "".
    public string label;   // 한글 표시 라벨 (예: "주의 국적")
    public string unlocksScan; // 이 단서가 손님과 Match/Related 시 잠금 해제하는 스캔("xray"|"fingerprint"). 트리거 아니면 "".
}

/// <summary>한 상황에 대한 대화 케이스 + 대사 라인.</summary>
[Serializable]
public sealed class DialogueCaseData
{
    public string caseType;    // 입장 / 일반 심사 / 여권 없음 / 입국 도장 없음
    public string gameResult;  // - / 정상 승인 / 정상 거절 / 잘못 허가 / 잘못 거절
    public int rejectCount;    // 오거부 단계(1~3), 그 외 0
    public DialogueLineData[] lines;
}

/// <summary>대사 한 줄.</summary>
/// <remarks><see cref="claim"/>가 null이 아니면 이 발화가 서류와 대조 가능한 진술임을 뜻한다(주로 손님 발화).</remarks>
[Serializable]
public sealed class DialogueLineData
{
    public int order;
    public string speaker; // 캐릭터 / 심사관
    public string text;
    public Claim claim;    // 대조 가능한 진술이면 채움. 없으면 null.
}

/// <summary>1일차 규정 1건.</summary>
/// <remarks><see cref="attr"/>는 <see cref="relatedField"/>(한글)를 속성 키로 매핑한 값. "이 속성에 관한 규정"이라는 관련성 표시용(값 비교 아님).</remarks>
[Serializable]
public sealed class RuleData
{
    public int ruleId;
    public string title;
    public string content;
    public string relatedField; // 한글 필드명(표시용)
    public string attr;         // 속성 키. 매핑 불가/없으면 "".
    public int endDay;          // 이벤트성 규정의 종료 일차(이날까지만 규정집에 표시). 0=종료 없음(도입 후 계속). 예: PCR 규정=7.
    public string coveredAttrs; // 이 규정 글이 '다루는' 항목 키들(쉼표 구분). 규정 ↔ 이 항목을 대조하면 관련없음 대신 관련있음(파랑)으로 표시. 핵심 판정 항목(attr)은 특수 평가기가 일치/불일치로 먼저 처리. 예: "name,nationality,valid_until".
}

/// <summary>1일차 뉴스 1건.</summary>
/// <remarks><see cref="claims"/>는 본문에서 도출한, 사람·서류와 대조 가능한 단서 0~n건.</remarks>
[Serializable]
public sealed class NewsData
{
    public int newsId;
    public string title;
    public string content;
    public string iconRef;
    public Claim[] claims;   // 본문 도출 단서. 없으면 빈 배열.
}
