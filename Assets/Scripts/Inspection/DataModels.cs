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
    public string correctResult;  // "정상 승인" | "정상 거절"
    public DocumentData[] documents;
    public DialogueCaseData[] dialogueCases;
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
[Serializable]
public sealed class FieldEntry
{
    public string label;
    public string value;
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
[Serializable]
public sealed class DialogueLineData
{
    public int order;
    public string speaker; // 캐릭터 / 심사관
    public string text;
}

/// <summary>1일차 규정 1건.</summary>
[Serializable]
public sealed class RuleData
{
    public int ruleId;
    public string title;
    public string content;
    public string relatedField;
}

/// <summary>1일차 뉴스 1건.</summary>
[Serializable]
public sealed class NewsData
{
    public int newsId;
    public string title;
    public string content;
    public string iconRef;
}
