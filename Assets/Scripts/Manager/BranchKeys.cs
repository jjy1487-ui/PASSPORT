/// <summary>
/// character_score / character_payout 의 branch_key 정규화 enum 문자열 상수.
/// 데이터(BRANCH_CATALOG.md)의 키와 반드시 1:1로 일치해야 한다. 오타 방지를 위해 한 곳에 모은다.
/// branch_key 를 "결정"하는 것은 게임플레이(상태머신)이고, 데이터는 키별 점수/돈/트리거만 제공한다.
/// </summary>
public static class BranchKeys
{
    // ── 공통 기본 판정 ─────────────────────────────────────────
    public const string ApproveCorrect = "approve_correct"; // 정상 입국(정답)
    public const string RejectCorrect  = "reject_correct";  // 불량 거부(정답)
    public const string ApproveWrong   = "approve_wrong";   // 불량 입국(오판)
    public const string RejectWrong    = "reject_wrong";    // 정상 거부(오판, 단순화 벌금)

    // ── 재거절 감액(성형/연예인/정치인) — 정상 손님 ────────────
    public const string ApproveImmediate    = "approve_immediate";      // 즉시 입국(×1.0)
    public const string ApproveAfterReject1 = "approve_after_reject_1"; // 1회 거절 후(×0.5)
    public const string ApproveAfterReject2 = "approve_after_reject_2"; // 2회 거절 후(×0.3)
    public const string ApproveAfterReject3 = "approve_after_reject_3"; // 3회 강제 입국(×0.1)
    public const string RejectAccrue1       = "reject_accrue_1";        // 거부 1회 누적(점수 감점)
    public const string RejectAccrue2       = "reject_accrue_2";        // 거부 2회 누적
    public const string RejectAccrue3       = "reject_accrue_3";        // 거부 3회 누적

    // ── 범죄자/성형범죄자 적발 ─────────────────────────────────
    public const string RejectLucky               = "reject_lucky";                // 단순 거부(포상 X)
    public const string DetectMontageReject       = "detect_montage_reject";       // 몽타주 + 거부(포상 O)
    public const string DetectMontageXrayReject   = "detect_montage_xray_reject";  // 몽타주 + X-ray + 거부(최선)
    public const string ApproveWrongNoMontage     = "approve_wrong_no_montage";    // 몽타주 못 보고 입국(오판)
    public const string CorruptAcceptGold         = "corrupt_accept_gold";         // 뇌물+금괴+입국(부패)
    public const string CorruptAcceptDrugs        = "corrupt_accept_drugs";        // 뇌물+마약+입국(부패)

    // ── 테러범(상담/신고/제압 상태머신) ───────────────────────
    public const string TerrorPersuadeConfess            = "terror_persuade_confess";
    public const string TerrorPersuadeReportOk           = "terror_persuade_report_ok";
    public const string TerrorPersuadeThenBomb           = "terror_persuade_then_bomb";
    public const string TerrorPersuadeReportCaughtBomb   = "terror_persuade_report_caught_bomb";
    public const string TerrorIgnoreThenPersuade         = "terror_ignore_then_persuade";
    public const string TerrorIgnoreThenBomb             = "terror_ignore_then_bomb";
    public const string TerrorIgnoreSubdueOk             = "terror_ignore_subdue_ok";
    public const string TerrorIgnoreSubdueFail           = "terror_ignore_subdue_fail";
    public const string TerrorIgnoreReportCaughtBomb     = "terror_ignore_report_caught_bomb";
    public const string TerrorXrayBombReject             = "terror_xray_bomb_reject";

    // ── 사이비 신도(visit_round로 1/2/3회차) ──────────────────
    public const string CultYyy           = "cult_yyy";
    public const string CultPartialYes    = "cult_partial_yes";
    public const string CultAllNo         = "cult_all_no";
    public const string CultOtherCombo    = "cult_other_combo";
    public const string CultBestCombo     = "cult_best_combo";
    public const string CultReject        = "cult_reject";
    public const string CultYyyBrainwash  = "cult_yyy_brainwash";  // #13
    public const string CultNyyFollower   = "cult_nyy_follower";   // #14

    // ── 연예인/정치인 마스크·대리 ─────────────────────────────
    public const string MaskRequestTurn1 = "mask_request_turn_1";
    public const string MaskRequestTurn2 = "mask_request_turn_2";
    public const string ScandalThirdTurn = "scandal_third_turn";
    public const string ProxySelfRequest = "proxy_self_request";

    // ── 현자/꼬마 ──────────────────────────────────────────────
    public const string ApproveSageItem = "approve_sage_item";
    public const string ApproveWithStep = "approve_with_step";
    public const string ApproveNoStep   = "approve_no_step";
}

/// <summary>doc_state 상수. character_score/payout 조인 키.</summary>
public static class DocStates
{
    public const string Normal = "normal";
    public const string Defect = "defect";
}

/// <summary>조기/누적 엔딩 이벤트 id 상수(character_score.event_id / character_payout.early_ending).</summary>
public static class EventIds
{
    public const string CorruptGoldDrugs = "#11"; // 범죄자 부패(즉시)
    public const string PlasticDrugs     = "#12"; // 성형범죄자 마약(즉시)
    public const string CultBrainwash    = "#13"; // 사이비 포교 성공(즉시)
    public const string CultFollower     = "#14"; // 사이비 추종(즉시)
    public const string QuarantineFail   = "#15"; // 방역 실패 누적
    public const string OverstayApprove  = "#16"; // 등잔 밑이 어둡다: 외국인 결함 오승인 누적
    public const string UnfitJob         = "#18"; // 자넨 적성에 안 맞는 것 같네: 단일 일자 오판 4명+(1~3일) 즉시
    public const string TerrorBomb       = "#TERROR_BOMB"; // 퍼엉!(테러범 시한폭탄 투척) 즉시 엔딩 — 숫자id 비충돌용 전용 키
}
