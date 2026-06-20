using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// 현재 일차(PlayerPrefs "CurrentDay")의 브리핑 슬라이드를 순서대로 표시한다.
/// 이미지는 Resources/Briefing/{일차}day  (예: 2day) 또는 {일차}day_{순번} (예: 1day_1, 1day_2)
/// 규칙으로 자동 로드된다.
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

    [Header("폴백 슬라이드 (Resources에서 못 찾을 때만 사용)")]
    [SerializeField] private BriefingSlide[] _slides;

    [Header("UI 참조")]
    [SerializeField] private Camera _camera;
    [SerializeField] private Image _slideImage;
    [SerializeField] private TMP_Text _slideText;
    [SerializeField] private CanvasGroup _fadePanel;
    [SerializeField] private TMP_Text _hintText;   // "클릭하여 계속" 안내
    [SerializeField] private TMP_Text _dayLabelText;   // "N일차" 표시

    [Header("설정")]
    [SerializeField] private string _nextScene = "ImmigrationScene";
    [SerializeField] private float _fadeDuration = 0.6f;
    [Tooltip("Resources 아래 브리핑 이미지가 있는 폴더 경로")]
    [SerializeField] private string _resourceFolder = "Briefing";
    [Tooltip("이미지에 멘트가 그려져 있어 별도 자막이 필요 없으면 체크")]
    [SerializeField] private bool _imageOnly = true;
    [SerializeField] private Color _defaultBackground = new Color(0.94f, 0.94f, 0.94f);

    // 현재 일차를 보관하는 PlayerPrefs 키 (MainMenuManager / ImmigrationManager 와 동일 규칙)
    private const string CurrentDayKey = "CurrentDay";

    // 1~14일차 클램프 범위(DayN씬 분리에 맞춤). ImmigrationManager 와 동일한 진행 한계.
    private const int FirstDay = 1;
    private const int LastDay = 14;

    private int _currentIndex = -1;
    private bool _advancing = false;
    private BriefingSlide[] _activeSlides;

    // 브리핑 종료 후 로드할 일차별 심사 씬 이름(Start 에서 결정). 폴백은 _nextScene.
    private string _targetScene;
    // 이 브리핑이 책임지는 일차(Day씬 로드 직전 '브리핑 완료' 표식으로 기록 → Day씬이 브리핑 경유 여부 판단).
    private int _briefedDay = 1;

    private void Start()
    {
        int day = Mathf.Max(1, PlayerPrefs.GetInt(CurrentDayKey, 1));
        _activeSlides = BuildSlidesForDay(day);

        // 브리핑 종료 후 일차별 심사 씬으로 진입한다(단일 ImmigrationScene → DayN씬 분리).
        //  유효 범위(1~14)면 'Day{N}Scene', 아니면 기존 _nextScene 으로 폴백.
        int clampedDay = Mathf.Clamp(day, FirstDay, LastDay);
        _targetScene = (clampedDay >= FirstDay && clampedDay <= LastDay)
            ? $"Day{clampedDay}Scene"
            : _nextScene;
        _briefedDay = clampedDay; // 이 브리핑이 책임지는 일차(Day씬 진입 시 '브리핑 경유' 확인용)

        if (_dayLabelText != null) _dayLabelText.text = $"{day}일차";

        if (_fadePanel != null) _fadePanel.alpha = 1f;

        if (_activeSlides == null || _activeSlides.Length == 0)
        {
            // 표시할 슬라이드가 전혀 없으면 브리핑을 건너뛰고 바로 다음 씬으로.
            Debug.LogWarning($"[BriefingManager] {day}일차 브리핑 이미지를 찾지 못해 건너뜁니다.");
            _advancing = true;
            StartCoroutine(FadeOutAndLoad());
            return;
        }

        ApplyFullscreenLayout(); // 이미지 전체화면 + 텍스트 하단 오버레이(엔딩 컷씬 스타일)
        ShowSlide(0);
        StartCoroutine(FadeIn());
    }

    /// <summary>브리핑 이미지를 엔딩 컷씬처럼 전체화면으로, 안내 텍스트는 하단에 반투명 띠로 겹쳐 표시한다.
    /// 씬을 직접 수정하지 않고 런타임에 레이아웃만 조정한다(가역적).</summary>
    private void ApplyFullscreenLayout()
    {
        // 이미지: 화면 꽉 채우기(stretch) + 맨 뒤로(텍스트·일차·힌트가 위에 보이게).
        if (_slideImage != null && _slideImage.transform is RectTransform irt)
        {
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            _slideImage.preserveAspect = true; // 비율 유지하며 화면에 최대로(왜곡 방지)
            irt.SetAsFirstSibling();
        }

        // 자막 모드일 때만 하단 오버레이/검은 띠 구성(_imageOnly면 생략 → 빈 검은 띠 방지)
        if (!_imageOnly && _slideText != null && _slideText.transform is RectTransform trt)
        {
            Transform parent = trt.parent;
            // 가독성용 하단 반투명 띠(최초 1회 생성, 텍스트 바로 뒤).
            if (parent != null && parent.Find("BriefingTextBackdrop") == null)
            {
                var bg = new GameObject("BriefingTextBackdrop", typeof(RectTransform), typeof(Image));
                var brt = bg.GetComponent<RectTransform>();
                brt.SetParent(parent, false);
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(1f, 0.33f);
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
                var bimg = bg.GetComponent<Image>();
                bimg.color = new Color(0f, 0f, 0f, 0.5f);
                bimg.raycastTarget = false;
                brt.SetSiblingIndex(trt.GetSiblingIndex()); // 텍스트 바로 뒤(이미지 위)
            }
            // 텍스트: 하단 중앙 오버레이(흰색).
            trt.anchorMin = new Vector2(0.08f, 0.03f);
            trt.anchorMax = new Vector2(0.92f, 0.3f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            _slideText.alignment = TMPro.TextAlignmentOptions.Center;
            _slideText.color = Color.white;
        }
    }

    /// <summary>
    /// 해당 일차의 슬라이드 배열을 만든다.
    /// Resources/{폴더}/{day}day 또는 {day}day_{n} 패턴을 모두 모아 n 순서로 정렬.
    /// 번호가 없으면 0번(단일 이미지)으로 취급. 하나도 없으면 _slides 폴백을 반환.
    /// </summary>
    private BriefingSlide[] BuildSlidesForDay(int day)
    {
        Sprite[] all = Resources.LoadAll<Sprite>(_resourceFolder);
        var matched = new List<(int order, Sprite sprite)>();
        Regex pattern = new Regex($@"^{day}day(?:_?(\d+))?$");

        if (all != null)
        {
            foreach (Sprite spr in all)
            {
                if (spr == null) continue;
                Match m = pattern.Match(spr.name);
                if (!m.Success) continue;
                int order = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
                matched.Add((order, spr));
            }
        }

        if (matched.Count == 0)
        {
            return _slides; // 폴백 (없으면 null)
        }

        matched.Sort((a, b) => a.order.CompareTo(b.order));
        var result = new BriefingSlide[matched.Count];
        for (int i = 0; i < matched.Count; i++)
        {
            result[i] = new BriefingSlide
            {
                image = matched[i].sprite,
                text = string.Empty,
                backgroundColor = _defaultBackground,
            };
        }
        return result;
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
        if (_activeSlides == null || index >= _activeSlides.Length) return;
        _currentIndex = index;
        BriefingSlide slide = _activeSlides[index];

        if (_slideImage != null)
        {
            _slideImage.sprite = slide.image;
            _slideImage.enabled = (slide.image != null);
        }
        if (_slideText != null)
        {
            _slideText.text = _imageOnly ? string.Empty : slide.text;
            _slideText.gameObject.SetActive(!_imageOnly);
        }
        if (_camera != null)
        {
            _camera.backgroundColor = slide.backgroundColor;
        }
        // 마지막 슬라이드는 심사 시작 안내 표시
        if (_hintText != null)
        {
            _hintText.gameObject.SetActive(true);
            bool isLast = (index == _activeSlides.Length - 1);
            _hintText.text = isLast ? "클릭하여 심사 시작!" : "클릭하여 계속";
        }
    }

    private void Advance()
    {
        int next = _currentIndex + 1;
        if (next >= _activeSlides.Length)
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
        // 일차별 심사 씬으로 진입(Start 에서 결정). 미설정 시 기존 _nextScene 폴백.
        string target = string.IsNullOrEmpty(_targetScene) ? _nextScene : _targetScene;
        // 'Day씬 = 브리핑 먼저' 성립용: 심사 씬 로드 직전 이 날을 '브리핑 완료'로 표식한다.
        //  (ImmigrationManager.Start 가 이 값과 그날을 비교 → 미경유면 브리핑으로 돌려보냄)
        PlayerPrefs.SetInt(ImmigrationManager.BriefedForDayKey, _briefedDay);
        PlayerPrefs.Save();
        SceneManager.LoadScene(target);
    }
}
