/// <summary>
/// day1.json 의 gameResult 문자열 값. 데이터 표기와 반드시 1:1로 일치해야 한다.
/// (코드 곳곳의 한국어 매직스트링을 한 곳으로 모아 오타를 방지)
/// </summary>
public static class GameResults
{
    public const string Approve = "정상 승인";      // 올바른 승인
    public const string Reject = "정상 거절";        // 올바른 거절
    public const string WrongReject = "잘못 거절";   // 승인해야 하는데 거부(오거부)
    public const string WrongApprove = "잘못 허가";  // 거부해야 하는데 승인(오허가)
}

/// <summary>day1.json 의 caseType 문자열 값.</summary>
public static class CaseTypes
{
    public const string Entry = "입장";              // 손님 등장 인사
    public const string NormalCheck = "일반 심사";   // 일반 심사 진행
    public const string NoPassport = "여권 없음";    // 여권 미제출
    public const string NoEntryStamp = "입국 도장 없음"; // 입국 도장 누락
}
