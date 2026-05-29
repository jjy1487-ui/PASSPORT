using System.Collections;
using UnityEngine;

/// <summary>
/// ImmigrationScene 컨트롤러. 페이드인 후 1일차 데이터를 로드해 심사 진행을 시작하고,
/// 뉴스/규정집 버튼으로 팝업을 연다.
/// 주의: 씬 직렬 바인딩 유지를 위해 기존 필드명 fadePanel 을 보존한다.
/// </summary>
public sealed class ImmigrationManager : MonoBehaviour
{
    [Header("페이드")]
    [SerializeField] private CanvasGroup fadePanel;          // 기존 필드명 보존(씬 바인딩)
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("심사/팝업")]
    [SerializeField] private InspectionController inspectionController;
    [SerializeField] private NewsPopup newsPopup;
    [SerializeField] private RulebookPopup rulebookPopup;
    [SerializeField] private DialogueLogPopup dialogueLogPopup;

    private Day1Data _data;

    private void Start()
    {
        _data = new GameDataLoader().Load();

        // 심사 시작은 페이드 연출과 독립적으로 즉시 수행(연출 타이밍에 게임 로직을 묶지 않는다).
        if (inspectionController != null && _data != null)
        {
            inspectionController.Initialize(_data);
        }

        StartCoroutine(FadeIn()); // 페이드는 시각 연출 전용
    }

    /// <summary>뉴스 버튼: 1일차 뉴스 팝업.</summary>
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
            dialogueLogPopup.Open(inspectionController.GetDialogueLog());
        }
    }

    /// <summary>규정집 버튼: 1일차 규정 팝업.</summary>
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
