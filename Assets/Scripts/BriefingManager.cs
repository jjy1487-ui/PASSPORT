using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// 일차별 브리핑 슬라이드를 순서대로 표시한다.
/// 클릭/아무 키 → 다음 슬라이드, 마지막 슬라이드 → 페이드 아웃 후 다음 씬 로드.
/// </summary>
public sealed class BriefingManager : MonoBehaviour
{
    [System.Serializable]
    public struct BriefingSlide
    {
        public Sprite image;
        [TextArea(2, 5)]
        public string text;
        public Color backgroundColor;
    }

    [Header("슬라이드 목록 (클릭으로 순서대로 표시)")]
    [SerializeField] private BriefingSlide[] _slides;

    [Header("UI 참조")]
    [SerializeField] private Camera _camera;
    [SerializeField] private Image _slideImage;
    [SerializeField] private TMP_Text _slideText;
    [SerializeField] private CanvasGroup _fadePanel;
    [SerializeField] private TMP_Text _hintText;   // "클릭하여 계속" 안내

    [Header("설정")]
    [SerializeField] private string _nextScene = "ImmigrationScene";
    [SerializeField] private float _fadeDuration = 0.6f;

    private int _currentIndex = -1;
    private bool _advancing = false;

    private void Start()
    {
        if (_fadePanel != null) _fadePanel.alpha = 1f;
        ShowSlide(0);
        StartCoroutine(FadeIn());
    }

    private void Update()
    {
        if (_advancing) return;
        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool anyKey  = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        if (clicked || anyKey)
        {
            Advance();
        }
    }

    private void ShowSlide(int index)
    {
        if (_slides == null || index >= _slides.Length) return;
        _currentIndex = index;
        BriefingSlide slide = _slides[index];

        if (_slideImage != null)
        {
            _slideImage.sprite = slide.image;
            _slideImage.enabled = (slide.image != null);
        }
        if (_slideText != null)
        {
            _slideText.text = slide.text;
        }
        if (_camera != null)
        {
            _camera.backgroundColor = slide.backgroundColor;
        }
        // 마지막 슬라이드는 클릭 안내 표시
        if (_hintText != null)
        {
            _hintText.gameObject.SetActive(true);
            bool isLast = (index == _slides.Length - 1);
            _hintText.text = isLast ? "클릭하여 심사 시작!" : "클릭하여 계속";
        }
    }

    private void Advance()
    {
        int next = _currentIndex + 1;
        if (next >= _slides.Length)
        {
            // 마지막 → 페이드 아웃 후 씬 전환
            _advancing = true;
            if (_hintText != null) _hintText.gameObject.SetActive(false);
            StartCoroutine(FadeOutAndLoad());
        }
        else
        {
            ShowSlide(next);
        }
    }

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (_fadePanel != null) _fadePanel.alpha = 1f - Mathf.Clamp01(elapsed / _fadeDuration);
            yield return null;
        }
        if (_fadePanel != null) _fadePanel.alpha = 0f;
    }

    private IEnumerator FadeOutAndLoad()
    {
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (_fadePanel != null) _fadePanel.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
            yield return null;
        }
        SceneManager.LoadScene(_nextScene);
    }
}
