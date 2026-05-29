using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class TitleManager : MonoBehaviour
{
    [SerializeField] CanvasGroup fadePanel;
    [SerializeField] float fadeInDuration = 0.8f;
    [SerializeField] float fadeOutDuration = 0.5f;

    bool isTransitioning = false;

    void Start()
    {
        StartCoroutine(FadeIn());
    }

    void Update()
    {
        if (isTransitioning) return;

        bool anyInput = false;
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            anyInput = true;
        if (Mouse.current != null && (
            Mouse.current.leftButton.wasPressedThisFrame ||
            Mouse.current.rightButton.wasPressedThisFrame))
            anyInput = true;

        if (anyInput)
            StartCoroutine(TransitionToGame());
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

    IEnumerator TransitionToGame()
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
        SceneManager.LoadScene("MainMenuScene");
    }
}
