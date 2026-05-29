using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] GameObject achieveButton;
    [SerializeField] GameObject endingButton;
    [SerializeField] CanvasGroup fadePanel;
    [SerializeField] float fadeInDuration = 0.8f;
    [SerializeField] float fadeOutDuration = 0.5f;

    const string SAVE_KEY = "HasSaveData";

    bool isTransitioning = false;

    void Start()
    {
        bool hasSave = PlayerPrefs.HasKey(SAVE_KEY);
        achieveButton.SetActive(hasSave);
        endingButton.SetActive(hasSave);
        StartCoroutine(FadeIn());
    }

    public void OnStartGame()
    {
        if (isTransitioning) return;
        PlayerPrefs.SetInt(SAVE_KEY, 1);
        PlayerPrefs.SetInt("CurrentDay", 1);  // 1일차 시작
        PlayerPrefs.Save();
        StartCoroutine(TransitionToScene("BriefingScene")); // 브리핑 → 심사 씬
    }

    public void OnAchievement()
    {
        // placeholder
    }

    public void OnEnding()
    {
        // placeholder
    }

    IEnumerator FadeIn()
    {
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

    IEnumerator TransitionToScene(string sceneName)
    {
        isTransitioning = true;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            fadePanel.alpha = Mathf.Clamp01(elapsed / fadeOutDuration);
            yield return null;
        }
        fadePanel.alpha = 1f;
        SceneManager.LoadScene(sceneName);
    }
}
