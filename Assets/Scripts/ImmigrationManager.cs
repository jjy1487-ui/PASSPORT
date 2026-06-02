using System.Collections;
using UnityEngine;

/// <summary>
/// ImmigrationScene 컨트롤러. 페이드인 후 현재 일차 데이터를 로드해 심사 진행을 시작하고,
/// 일자 완료 통지를 받아 다음 일차로 진행한다(1→2→…→14→전체 종료). 같은 씬을 재사용한다.
/// 뉴스/규정집/음성기록 버튼은 현재 일차 데이터로 연결된다.
/// 주의: 씬 직렬 바인딩 유지를 위해 기존 필드명 fadePanel 을 보존한다.
/// </summary>
public sealed class ImmigrationManager : MonoBehaviour
{
    private const int FirstDay = 1;
    private const int LastDay = 14; // 14일 × 7슬롯 = 98건

    [Header("페이드")]
    [SerializeField] private CanvasGroup fadePanel;          // 기존 필드명 보존(씬 바인딩)
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("심사/팝업")]
    [SerializeField] private InspectionController inspectionController;
    [SerializeField] private NewsPopup newsPopup;
    [SerializeField] private RulebookPopup rulebookPopup;
    [SerializeField] private DialogueLogPopup dialogueLogPopup;

    [Header("진행 설정")]
    [Tooltip("시작 일차(보통 1). 디버그/이어하기용으로 변경 가능.")]
    [SerializeField] private int startDay = FirstDay;

    private readonly GameDataLoader _loader = new GameDataLoader();
    private Day1Data _data;

    /// <summary>현재 진행 중인 일차(1~14).</summary>
    public int CurrentDay { get; private set; }

    private void OnEnable()
    {
        if (inspectionController != null)
        {
            inspectionController.OnDayCompleted += HandleDayCompleted;
        }
    }

    private void OnDisable()
    {
        if (inspectionController != null)
        {
            inspectionController.OnDayCompleted -= HandleDayCompleted;
        }
    }

    private void Start()
    {
        CurrentDay = Mathf.Clamp(startDay, FirstDay, LastDay);
        BeginDay(CurrentDay, resetGold: true);

        StartCoroutine(FadeIn()); // 페이드는 시각 연출 전용
    }

    /// <summary>지정 일차 데이터를 로드해 심사를 시작한다.</summary>
    private void BeginDay(int day, bool resetGold)
    {
        _data = _loader.Load(day);
        if (_data == null)
        {
            // 데이터가 아직 없는 일차(미구현). 진행 중단하고 명시적으로 로그.
            Debug.LogWarning($"[ImmigrationManager] day{day} 데이터를 로드하지 못해 진행을 멈춥니다.");
            return;
        }

        CurrentDay = day;
        if (inspectionController != null)
        {
            inspectionController.Initialize(_data, resetGold);
        }
        Debug.Log($"[ImmigrationManager] {day}일차 시작");
    }

    /// <summary>InspectionController 가 일자 완료를 통지하면 다음 일차로 진행하거나 종료한다.</summary>
    private void HandleDayCompleted(int completedDay)
    {
        if (completedDay >= LastDay)
        {
            // TODO(엔딩): 점수 정산·ending 테이블 score_min~score_max 구간 분기로 엔딩 화면 연결.
            Debug.Log($"[ImmigrationManager] 전체 일정({LastDay}일) 종료. 엔딩 처리 대기(TODO).");
            return;
        }

        // 같은 씬 재사용: 다음 일차 데이터 로드 → 컨트롤러 재초기화(골드 누적 유지).
        // 현재는 즉시 진행. 완료 패널의 "다음 날" 버튼을 쓰려면 OnNextDayButton 을 바인딩한다.
        BeginDay(completedDay + 1, resetGold: false);
    }

    /// <summary>완료 패널 "다음 날" 버튼용(선택). 현재 일차 다음으로 진행.</summary>
    public void OnNextDayButton()
    {
        if (CurrentDay >= LastDay)
        {
            Debug.Log("[ImmigrationManager] 더 진행할 일차가 없습니다(전체 종료).");
            return;
        }
        BeginDay(CurrentDay + 1, resetGold: false);
    }

    /// <summary>뉴스 버튼: 현재 일차 뉴스 팝업.</summary>
    public void OnNewsButton()
    {
        if (_data != null && newsPopup != null)
        {
            newsPopup.Open(_data.news);
        }
    }

    /// <summary>음성기록 버튼: 현재 손님 대화 기록 팝업.</summary>
    public void OnHandsetButton()
    {
        if (inspectionController != null && dialogueLogPopup != null)
        {
            dialogueLogPopup.OpenLines(inspectionController.GetDialogueLines());
        }
    }

    /// <summary>규정집 버튼: 현재 일차 규정 팝업.</summary>
    public void OnRulebookButton()
    {
        if (_data != null && rulebookPopup != null)
        {
            rulebookPopup.Open(_data.rules);
        }
    }

    private IEnumerator FadeIn()
    {
        if (fadePanel == null)
        {
            yield break;
        }

        fadePanel.alpha = 1f;
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            fadePanel.alpha = 1f - Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        fadePanel.alpha = 0f;
    }
}
