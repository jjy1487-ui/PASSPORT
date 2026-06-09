using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 고급 분기 선택지 패널. 특정 character_type(범죄자/테러범/사이비/연예인·정치인) 손님이 등장하면
/// 그 캐릭터에 맞는 선택 버튼을 띄운다. 플레이어가 고르면 branch_key 를 확정해
/// BranchKeyResolver.ResolveAdvanced → ScoreEconomyManager.Settle 로 전달한다(점수/돈/호칭/아이템/조기엔딩은 매니저가 처리).
///
/// UI 는 "어떤 선택지를 보여줄지"와 "고른 선택지의 branch_key"만 안다.
/// 판정·정산·엔딩 트리거는 전부 매니저 소유(규약 5장: UI 는 입력 전달만).
///
/// 버튼 배치 규약(UI-CONVENTIONS 1장): 호의/수령(긍정 결과) = 왼쪽, 거부/신고(부정 결과) = 오른쪽 통일.
///
/// 데이터 등장 현황: 범죄자(밀수품/마약/성형)·테러범은 정상 baked 경로엔 일반 거절 손님으로만 들어오고,
/// 사이비/꼬마/현자/연예인/정치인은 day_schedule 미등장이라 현재는 실제 트리거되지 않는다(컴포넌트는 대기).
/// 데이터가 채워지면 OnCustomerChanged 시점에 자동 작동한다(추가 코드 불필요).
/// </summary>
public sealed class AdvancedBranchPanel : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private InspectionController _controller;

    [Header("UI 참조")]
    [SerializeField] private GameObject _root;            // 비활성 시작
    [SerializeField] private TMP_Text _promptText;        // 상황 안내
    [SerializeField] private Button _leftButton;          // 긍정/수령 선택(좌)
    [SerializeField] private TMP_Text _leftLabel;
    [SerializeField] private Button _rightButton;         // 부정/거부/신고 선택(우)
    [SerializeField] private TMP_Text _rightLabel;
    [SerializeField] private Button _closeButton;         // X, 우상단(선택 보류)

    /// <summary>한 선택지 정의(표시 문구 + 확정 branch_key).</summary>
    private readonly struct Choice
    {
        public readonly string label;
        public readonly string branchKey;
        public Choice(string label, string branchKey) { this.label = label; this.branchKey = branchKey; }
    }

    private Choice _left;
    private Choice _right;
    private bool _hasChoices;

    private void Start()
    {
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[AdvancedBranchPanel] InspectionController 미연결 — 고급 분기 비활성.");

        if (_leftButton != null) _leftButton.onClick.AddListener(ChooseLeft);
        if (_rightButton != null) _rightButton.onClick.AddListener(ChooseRight);
        if (_closeButton != null) _closeButton.onClick.AddListener(Hide);

        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
        if (_leftButton != null) _leftButton.onClick.RemoveListener(ChooseLeft);
        if (_rightButton != null) _rightButton.onClick.RemoveListener(ChooseRight);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Hide);
    }

    /// <summary>
    /// 고급 분기 기능 활성화 플래그. 현재 false — 특수 캐릭터(연예인/정치인/범죄자/테러범/사이비)도
    /// 일반 판정(통과/거절)으로만 진행한다. 이 패널이 5일차 연예인부터 정상 판정을 가로막아
    /// 게임이 막혔으므로(=4일차까지만 됨) 분기 UI가 완성될 때까지 끈다.
    /// 분기 연출/세부 결과가 완성되면 true 로 되돌리면 OnCustomerChanged 시점에 다시 작동한다.
    /// </summary>
    private const bool AdvancedBranchesEnabled = false;

    private void HandleCustomerChanged()
    {
        _hasChoices = false;
        // 기능 비활성: 어떤 캐릭터든 패널을 띄우지 않고 일반 판정 흐름에 맡긴다(진행 막힘 방지).
        if (!AdvancedBranchesEnabled) { Hide(); return; }

        string type = _controller != null ? _controller.CurrentCharacterType : null;
        if (string.IsNullOrEmpty(type)) { Hide(); return; }

        if (!TryBuildChoices(type, out string prompt, out Choice left, out Choice right))
        {
            Hide(); // 이 캐릭터는 고급 선택지 없음 → 기존 통과/거절 도장 흐름만.
            return;
        }

        _left = left; _right = right; _hasChoices = true;
        if (_promptText != null) _promptText.text = prompt;
        if (_leftLabel != null) _leftLabel.text = left.label;
        if (_rightLabel != null) _rightLabel.text = right.label;
        if (_root != null) _root.SetActive(true);
    }

    /// <summary>
    /// character_type → (안내문, 좌/우 선택지) 매핑. branch_key 는 BranchKeys 상수 사용.
    /// 좌=긍정/수령(부패·추종 결과), 우=거부/신고/적발(정의 결과)로 위치 통일.
    /// </summary>
    private static bool TryBuildChoices(string type, out string prompt, out Choice left, out Choice right)
    {
        prompt = null; left = default; right = default;
        switch (type)
        {
            // 범죄자(밀수품/마약): 금괴 뇌물 제안. 좌=수령+입국(부패, 조기엔딩#11), 우=거부(정의).
            case CharacterTypes.CriminalSmuggler:
            case CharacterTypes.CriminalDrug:
                prompt = "손님이 금괴를 슬쩍 내밉니다. 어떻게 하시겠습니까?";
                left = new Choice("뇌물 수령 + 입국", BranchKeys.CorruptAcceptGold);
                right = new Choice("거부 (신고)", BranchKeys.DetectMontageReject);
                return true;

            // 범죄자(성형수술): 마약 동반 뇌물. 좌=묵인+입국(조기엔딩#12), 우=거부.
            case CharacterTypes.CriminalPlastic:
                prompt = "성형 흔적이 보입니다. 마약 동반 뇌물을 제안합니다.";
                left = new Choice("묵인 + 입국", BranchKeys.CorruptAcceptDrugs);
                right = new Choice("거부 (적발)", BranchKeys.DetectMontageReject);
                return true;

            // 테러범: 상담/제압. 좌=달램→설득, 우=신고(X-ray 폭발물 적발 거부).
            case CharacterTypes.Terrorist:
                prompt = "테러 용의자입니다. 대응을 선택하십시오.";
                left = new Choice("달래기 (설득)", BranchKeys.TerrorPersuadeConfess);
                right = new Choice("신고 / 거부", BranchKeys.TerrorXrayBombReject);
                return true;

            // 사이비 신도: 3문답(예/아니오). 좌=동조(추종, #14), 우=거부.
            case CharacterTypes.Cult:
                prompt = "사이비 신도가 포교를 시도합니다.";
                left = new Choice("동조", BranchKeys.CultNyyFollower);
                right = new Choice("거부", BranchKeys.CultReject);
                return true;

            // 연예인/정치인: 마스크 벗기 / 본인 확인. 좌=즉시 입국, 우=마스크 요청(턴 누적).
            case CharacterTypes.Celebrity:
            case CharacterTypes.Politician:
                prompt = "신원 확인이 필요합니다.";
                left = new Choice("즉시 입국", BranchKeys.ApproveImmediate);
                right = new Choice("마스크 벗기 요청", BranchKeys.MaskRequestTurn1);
                return true;
        }
        return false;
    }

    private void ChooseLeft() => Commit(_left);
    private void ChooseRight() => Commit(_right);

    /// <summary>
    /// 선택 확정 → branch_key 를 InspectionController 에 전달한다.
    /// 컨트롤러가 정산(1회 가드)과 **다음 손님 진행**을 모두 수행하므로 특수 캐릭터에서 진행이 막히지 않는다.
    /// (이전 구현은 매니저에 직접 정산만 하고 진행을 호출하지 않아 5일차 연예인에서 게임이 멈췄다.)
    /// UI 는 결과를 계산하지 않는다 — branch_key 전달만 한다(규약 5장).
    /// </summary>
    private void Commit(Choice choice)
    {
        if (!_hasChoices || _controller == null) { Hide(); return; }

        // 정답 여부는 정확도 집계용 — 고급 분기는 "정의 선택(거부/신고/적발)"을 정답으로 본다.
        bool wasCorrect = IsJusticeKey(choice.branchKey);

        Hide();
        _controller.SubmitAdvancedDecision(choice.branchKey, wasCorrect);
    }

    /// <summary>정의(거부/신고/적발) 계열 키인가 — 정확도 집계용 표시값(판정 권위는 데이터 점수표).</summary>
    private static bool IsJusticeKey(string branchKey)
    {
        switch (branchKey)
        {
            case BranchKeys.DetectMontageReject:
            case BranchKeys.DetectMontageXrayReject:
            case BranchKeys.TerrorXrayBombReject:
            case BranchKeys.CultReject:
            case BranchKeys.RejectCorrect:
                return true;
            default:
                return false;
        }
    }

    private void Hide()
    {
        _hasChoices = false;
        if (_root != null) _root.SetActive(false);
    }
}
