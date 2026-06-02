using UnityEngine;

// ─────────────────────────────────────────────────────────────
//  GameDatabase — 19개 테이블 SO를 한 곳에 모은 진입점 (data-tools 소유)
//  gameplay/ui는 이 SO 하나만 참조하면 전 테이블에 접근 가능.
//  임포터가 .asset 생성 시 자동으로 모든 테이블 참조를 연결한다.
// ─────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "GameDatabase", menuName = "Passport/Data/Game Database")]
public class GameDatabase : ScriptableObject
{
    [Header("고객 & 진실 서류")]
    public CustomerTable customer;
    public PassportTable passport;
    public VisaTable visa;
    public PcrTestTable pcrTest;
    public EmploymentCertTable employmentCert;

    [Header("진행 & 규칙")]
    public DayScheduleTable daySchedule;
    public DocumentRequirementTable documentRequirement;
    public RuleBookTable ruleBook;
    public NewsTable news;

    [Header("변조 (규칙 데이터)")]
    public DefectRuleTable defectRule;
    public FakeValuePoolTable fakeValuePool;

    [Header("보조 검사")]
    public XrayTable xray;
    public FingerprintTable fingerprint;

    [Header("대사")]
    public DialogueCaseTable dialogueCase;
    public DialogueLineTable dialogueLine;

    [Header("경제 & 엔딩")]
    public ShopTable shop;
    public RewardTable reward;
    public EndingTable ending;
    public ScoreModelTable scoreModel;

    [Header("캐릭터 분기 점수/금액 (260602 분기표)")]
    public CharacterScoreTable characterScore;
    public CharacterPayoutTable characterPayout;
}
