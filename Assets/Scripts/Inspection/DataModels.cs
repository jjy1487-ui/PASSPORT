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
