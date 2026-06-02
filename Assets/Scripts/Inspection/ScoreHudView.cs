using UnityEngine;
using TMPro;

/// <summary>
/// 점수 HUD. ScoreEconomyManager.OnScoreChanged 를 구독해 누적 점수(엔딩 기준)를 표시한다.
/// 돈(골드)과는 별개 — 점수는 판정/엔딩용, 돈은 상점용(공통 규약 4장: 점수 ≠ 돈).
/// UI-CONVENTIONS 2장: 점수=Score/Blue(#3B82C4) 톤으로 골드(#D4A017)와 색으로도 구분한다.
///
/// 표시만 한다. 점수 계산·판정 로직은 매니저가 소유(규약 5장).
/// </summary>
public sealed class ScoreHudView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private TMP_Text _scoreText;

    [Header("연출(선택)")]
    [Tooltip("점수 변동 시 잠깐 색을 강조했다 복귀(증가=점수색, 감소=거절색).")]
    [SerializeField] private TMP_Text _deltaText; // 이번 변동(+10/-15 등) 짧게 표시(없어도 됨)

    // UI-CONVENTIONS 색 토큰(하드코딩 회피 — 이 프로젝트는 UITheme 부재라 상수로 둠).
    private static readonly Color ScoreBlue = new Color32(0x3B, 0x82, 0xC4, 0xFF);
    private static readonly Color RejectRed = new Color32(0xC0, 0x39, 0x2B, 0xFF);

    private ScoreEconomyManager _mgr;

    private void Start()
    {
        _mgr = ScoreEconomyManager.Instance;
        if (_mgr == null)
        {
            Debug.LogWarning("[ScoreHudView] ScoreEconomyManager.Instance 가 없습니다. 점수 표시 비활성.");
            UpdateScore(0, 0);
            return;
        }
        _mgr.OnScoreChanged += HandleScoreChanged;
        UpdateScore(_mgr.Score, 0); // 초기값(이어하기 복원값 포함)
    }

    private void OnDestroy()
    {
        if (_mgr != null) _mgr.OnScoreChanged -= HandleScoreChanged;
    }

    private void HandleScoreChanged(int total, int delta) => UpdateScore(total, delta);

    private void UpdateScore(int total, int delta)
    {
        if (_scoreText != null)
        {
            _scoreText.text = total.ToString();
            _scoreText.color = ScoreBlue;
        }
        if (_deltaText != null)
        {
            if (delta == 0)
            {
                _deltaText.text = string.Empty;
            }
            else
            {
                _deltaText.text = (delta > 0 ? "+" : string.Empty) + delta;
                _deltaText.color = delta > 0 ? ScoreBlue : RejectRed;
            }
        }
    }
}
