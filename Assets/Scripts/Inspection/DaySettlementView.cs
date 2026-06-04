using UnityEngine;
using TMPro;

/// <summary>
/// 일일 정산(일자 완료) 표시. 날짜가 넘어갈 때만 유저에게 돈을 보여준다(상시 HUD 아님).
/// InspectionController.OnDayCompleted 수신 시 "오늘 번 돈 / 누적 잔액"을 갱신한다.
///
/// 하루 단위 증가분은 일자 시작 시점 잔액(직전 정산 시 캐시)과의 차액으로 계산한다.
/// 점수는 표시하지 않는다(요구 1·2: 점수는 엔딩까지 비공개).
///
/// 표시만 한다 — 돈 누적/정산 계산은 ScoreEconomyManager 가 소유(규약 5장).
/// </summary>
public sealed class DaySettlementView : MonoBehaviour
{
    [Header("정산 표시")]
    [SerializeField] private TMP_Text _earnedText;   // 오늘 번 돈
    [SerializeField] private TMP_Text _balanceText;  // 누적 잔액

    [Header("진행 통지원")]
    [SerializeField] private InspectionController _controller;

    private ScoreEconomyManager _mgr;
    private int _dayStartMoney; // 직전 정산(또는 게임 시작) 시점 잔액 — 일일 증가분 기준선

    private void Start()
    {
        _mgr = ScoreEconomyManager.Instance;
        if (_mgr != null) _dayStartMoney = _mgr.Money; // 첫 일차 기준선(이어하기 복원값 포함)
        else Debug.LogWarning("[DaySettlementView] ScoreEconomyManager.Instance 가 없습니다. 정산 금액 표시 비활성.");

        if (_controller != null) _controller.OnDayCompleted += HandleDayCompleted;
        else Debug.LogWarning("[DaySettlementView] InspectionController 참조가 없습니다.");
    }

    private void OnDestroy()
    {
        if (_controller != null) _controller.OnDayCompleted -= HandleDayCompleted;
    }

    /// <summary>일자 완료 시점에 호출(정산 패널이 표시되기 직전 발행됨). 금액만 갱신한다.</summary>
    private void HandleDayCompleted(int completedDay)
    {
        int total = _mgr != null ? _mgr.Money : 0;
        int earned = total - _dayStartMoney;

        if (_earnedText != null)
            _earnedText.text = (earned >= 0 ? "+" : string.Empty) + earned;
        if (_balanceText != null)
            _balanceText.text = total.ToString();

        _dayStartMoney = total; // 다음 날 기준선 갱신
    }
}
