/// <summary>
/// 판정 상태머신 결과 → (doc_state, branch_key, defect_variant) 정규화.
/// character_score / character_payout 조회 키를 만든다.
///
/// 현재 baked day JSON 경로가 실제로 만들 수 있는 분기(정상/불량 × 승인/거절 + 오거부 재심사 루프)를
/// 결정론적으로 매핑한다. 포상/자수/금괴/마약/마스크/대리/사이비/현자/꼬마 등 추가 UI 선지가 필요한
/// 분기는 <see cref="ResolveAdvanced"/> 공개 API 로 열어두고 3단계(UI)에서 호출한다.
/// </summary>
public readonly struct BranchResult
{
    public readonly string docState;       // normal | defect
    public readonly string branchKey;      // BranchKeys.*
    public readonly string defectVariant;  // 세부 변이(없으면 null → 변이 무시 매칭)
    public readonly string visitRound;     // 사이비 1/2/3 (없으면 null)

    public BranchResult(string docState, string branchKey, string defectVariant = null, string visitRound = null)
    {
        this.docState = docState;
        this.branchKey = branchKey;
        this.defectVariant = defectVariant;
        this.visitRound = visitRound;
    }
}

public static class BranchKeyResolver
{
    /// <summary>
    /// 기본 판정 분기 결정. baked 경로의 손님은 correctResult("정상 승인"/"정상 거절")로 정상/불량을 안다.
    /// </summary>
    /// <param name="correctResult">CustomerData.correctResult (GameResults.Approve/Reject).</param>
    /// <param name="playerApproved">플레이어가 승인했는가.</param>
    /// <param name="wrongRejectCount">정상 손님을 잘못 거절한 누적 라운드(0~3). 확정 시점 값.</param>
    /// <param name="forcedPass">3회 도달로 강제 통과되었는가(=approve_after_reject_3 류).</param>
    /// <param name="characterType">감액 분기(성형/연예인/정치인) 적용 대상 판별용.</param>
    /// <param name="defectVariant">손님의 세부 변이 키(CustomerData.defectVariant). ""/null이면 변이 무시 폴백.</param>
    public static BranchResult Resolve(
        string correctResult, bool playerApproved, int wrongRejectCount, bool forcedPass, string characterType,
        string defectVariant = null)
    {
        bool shouldApprove = correctResult == GameResults.Approve;
        string docState = shouldApprove ? DocStates.Normal : DocStates.Defect;

        // "" → null 정규화: 변이 무관 손님은 기존 변이 무시 매칭(Find의 null=ignore)을 그대로 탄다.
        string variant = string.IsNullOrEmpty(defectVariant) ? null : defectVariant;

        if (shouldApprove)
        {
            // 정상 손님.
            if (playerApproved)
            {
                // 승인(정답). 재거절 후 통과면 감액 분기(감액 대상 캐릭터만).
                if (wrongRejectCount <= 0)
                    return new BranchResult(docState, UsesAccrueScale(characterType) ? BranchKeys.ApproveImmediate : BranchKeys.ApproveCorrect, variant);
                return new BranchResult(docState, AfterRejectKey(wrongRejectCount), variant);
            }
            // 정상인데 거부(오판).
            return new BranchResult(docState, BranchKeys.RejectWrong, variant);
        }
        else
        {
            // 불량 손님.
            if (!playerApproved)
                return new BranchResult(docState, BranchKeys.RejectCorrect, variant);   // 거부(정답)
            return new BranchResult(docState, BranchKeys.ApproveWrong, variant);        // 입국시킴(오판)
        }
    }

    /// <summary>감액(재거절 ×0.5/0.3/0.1) 분기를 쓰는 캐릭터인가(성형 의심/연예인/정치인).</summary>
    public static bool UsesAccrueScale(string characterType) =>
        characterType == CharacterTypes.PlasticSuspect
        || characterType == CharacterTypes.Celebrity
        || characterType == CharacterTypes.Politician;

    private static string AfterRejectKey(int wrongRejectCount) => wrongRejectCount switch
    {
        1 => BranchKeys.ApproveAfterReject1,
        2 => BranchKeys.ApproveAfterReject2,
        _ => BranchKeys.ApproveAfterReject3, // 3 이상
    };

    /// <summary>
    /// UI 추가 선지가 결정한 고급 분기를 그대로 통과시킨다(예: 몽타주 적발/뇌물/테러 상태머신/사이비/현자).
    /// 3단계 UI 가 branch_key 를 확정해 호출한다. doc_state/variant/visit_round 는 호출부가 안다.
    /// </summary>
    public static BranchResult ResolveAdvanced(
        string docState, string branchKey, string defectVariant = null, string visitRound = null)
        => new BranchResult(docState, branchKey, defectVariant, visitRound);
}

/// <summary>character_type 어휘 상수(customer 시트 / baked characterType). 분기·감액 판별용.</summary>
public static class CharacterTypes
{
    public const string General          = "일반 고객";
    public const string Annoying         = "진상 고객";
    public const string Tourist          = "외국인 관광객";
    public const string PlasticSuspect   = "성형 의심 고객";
    // 검역 대상자(PCR) 폐지 → 전염병 환자 종류로 정리(260606). 5~7일 PCR 결함(양성/위조/미제출) 전용 손님.
    public const string Infected         = "전염병 환자";
    public const string LongStay         = "장기체류자";
    public const string Worker           = "취업체류자";
    // 260609 개명: 엑셀(단일소스)을 마약/밀수 범죄자로 정리 → 코드 상수도 동일하게 통일.
    public const string CriminalSmuggler = "범죄자(밀수품 범죄자)";   // 존 카터(12일). 구: 범죄자(외국 도피자)
    public const string CriminalDrug     = "범죄자(마약 범죄자)";     // 강도식(14일). 구: 범죄자(국내 유입자)
    public const string CriminalPlastic  = "범죄자(성형수술)";
    public const string Terrorist        = "테러범";
    public const string Cult             = "사이비 신도";
    public const string Kid              = "꼬마";
    public const string Celebrity        = "특수(연예인)★";
    public const string Politician       = "특수(정치인)★";
    public const string Sage             = "특수(현자)★";
}
