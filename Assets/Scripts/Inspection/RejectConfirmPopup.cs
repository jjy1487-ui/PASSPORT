using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 거절 확인 팝업 (연예인 '재거절 감액' 등 특수 손님 전용).
/// 거절 도장을 찍으면 InspectionController 가 ShowConfirm 으로 띄운다 — 손님의 (단계별) 반응 문구 +
/// [다시 검토](취소) / [그래도 거절](확정) 두 버튼. 한 번 누르면 자동으로 닫히고 콜백을 호출한다.
///
/// 규약: UI 는 표시·입력만 한다. 단계 상승/강제입국/정산은 InspectionController(콜백)가 결정한다.
/// 팝업 렌더: Canvas 직속 + overrideSorting(책상/HUD 위) + GraphicRaycaster (씬 배선은 UI가 담당).
/// 스크립트는 '항상 활성'인 컨테이너에 두고, 실제 표시는 자식 _root 토글로 한다(FindObjectOfType 탐색 보장).
/// </summary>
public sealed class RejectConfirmPopup : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;       // 팝업 본체(비활성 시작)
    [SerializeField] private TMP_Text _messageText;  // 손님 반응 문구(단계별)
    [SerializeField] private Button _reviewButton;   // 좌: 다시 검토(취소)
    [SerializeField] private TMP_Text _reviewLabel;
    [SerializeField] private Button _confirmButton;  // 우: 그래도 거절(확정)
    [SerializeField] private TMP_Text _confirmLabel;

    [Header("타이머 틱 사운드")]
    [Range(0f, 1f)]
    [SerializeField] private float _tickVolume = 0.7f;

    private Action _onReview;
    private Action _onConfirm;
    private Action _onTimeout;      // 제한시간 만료 시(시나리오 타이머). null이면 타이머 없음.
    private Coroutine _timerCo;
    private string _baseMessage;    // 카운트다운 표시 시 합쳐 보일 원본 메시지
    private AudioSource _sfx;       // 틱 사운드용 2D AudioSource(지연 생성)
    private AudioClip _tickClip;    // Resources/Audio/Tick (지연 로드)

    private void Awake()
    {
        if (_reviewButton != null) _reviewButton.onClick.AddListener(HandleReview);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(HandleConfirm);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_reviewButton != null) _reviewButton.onClick.RemoveListener(HandleReview);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(HandleConfirm);
    }

    /// <summary>팝업을 띄운다. message=안내문, reviewLabel=좌버튼(취소), confirmLabel=우버튼(확정) 문구.</summary>
    public void ShowConfirm(string message, string reviewLabel, string confirmLabel, Action onReview, Action onConfirm)
        => ShowConfirm(message, reviewLabel, confirmLabel, onReview, onConfirm, 0f, null);

    /// <summary>
    /// 팝업을 띄운다(+선택 제한시간). timerSeconds &gt; 0 이고 onTimeout 이 있으면 메시지 영역에 "⏳ N초"를
    /// 카운트다운 표시하고, 시간 만료 시 버튼 입력 없이 onTimeout 을 호출한다(시나리오 타이머 분기). 0 이면 기존 동작.
    /// </summary>
    public void ShowConfirm(string message, string reviewLabel, string confirmLabel,
        Action onReview, Action onConfirm, float timerSeconds, Action onTimeout)
    {
        _onReview = onReview;
        _onConfirm = onConfirm;
        _onTimeout = onTimeout;
        // 안내문이 비어 있으면(예: 사토 시나리오) 기본 선택 프롬프트를 띄운다 — 빈 화면 방지.
        _baseMessage = string.IsNullOrEmpty(message) ? "어떻게 하시겠습니까?" : message;
        if (_messageText != null) _messageText.text = _baseMessage;
        if (_reviewLabel != null && !string.IsNullOrEmpty(reviewLabel)) _reviewLabel.text = reviewLabel;
        if (_confirmLabel != null && !string.IsNullOrEmpty(confirmLabel)) _confirmLabel.text = confirmLabel;
        if (_root != null) _root.SetActive(true);

        StopTimer();
        if (timerSeconds > 0f && onTimeout != null && isActiveAndEnabled)
            _timerCo = StartCoroutine(CountdownRoutine(timerSeconds));
    }

    /// <summary>현재 팝업이 열려 있는가(중복 표시 가드용).</summary>
    public bool IsOpen => _root != null && _root.activeSelf;

    private void Close() { StopTimer(); if (_root != null) _root.SetActive(false); }

    private void StopTimer()
    {
        if (_timerCo != null) { StopCoroutine(_timerCo); _timerCo = null; }
    }

    private void SetCountdownText(int secondsLeft)
    {
        if (_messageText == null) return;
        string clock = $"남은 시간 {secondsLeft}초"; // ⏳ 모래시계는 MalgunGothic에 없어 □ 깨짐 → 글자로
        _messageText.text = string.IsNullOrEmpty(_baseMessage) ? clock : _baseMessage + "\n" + clock;
    }

    private IEnumerator CountdownRoutine(float seconds)
    {
        float remaining = seconds;
        int lastSec = Mathf.CeilToInt(remaining);
        SetCountdownText(lastSec);
        PlayTick(); // 시작 틱
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            int sec = Mathf.Max(0, Mathf.CeilToInt(remaining));
            SetCountdownText(sec);
            if (sec != lastSec && sec > 0) { PlayTick(); lastSec = sec; } // 1초마다 틱
            yield return null;
        }
        _timerCo = null;
        Close();
        Action cb = _onTimeout; _onReview = _onConfirm = _onTimeout = null;
        cb?.Invoke();
    }

    /// <summary>카운트다운 1초마다 틱 사운드 재생(2D PlayOneShot). 클립 없으면 조용히 무시.</summary>
    private void PlayTick()
    {
        if (_sfx == null)
        {
            _sfx = GetComponent<AudioSource>();
            if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f; // 2D
        }
        if (_tickClip == null) _tickClip = Resources.Load<AudioClip>("Audio/Tick");
        if (_tickClip == null) return;
        _sfx.PlayOneShot(_tickClip, _tickVolume);
    }

    private void HandleReview()
    {
        Close();
        Action cb = _onReview; _onReview = _onConfirm = _onTimeout = null;
        cb?.Invoke();
    }

    private void HandleConfirm()
    {
        Close();
        Action cb = _onConfirm; _onReview = _onConfirm = _onTimeout = null;
        cb?.Invoke();
    }
}
