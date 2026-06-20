using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// 대화 케이스의 라인을 순서대로 재생한다. 진행은 Next 버튼(레거시 Input 미사용).
/// </summary>
public sealed class DialogueView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _speakerText;
    [SerializeField] private TMP_Text _bodyText;
    [SerializeField] private Button _nextButton;

    [Header("타자기 효과 (언더테일식)")]
    [Tooltip("글자 하나가 나오는 간격(초). 작을수록 빠르게 타이핑.")]
    [SerializeField] private float _charInterval = 0.07f;
    [Tooltip("타이핑 '진행 중' 표시(예: ...). SpeechBubble 우측하단. 비우면 미사용.")]
    [SerializeField] private GameObject _typingIndicator;
    [Tooltip("대사 '완료' 표시(엔터 아이콘). SpeechBubble 우측하단. 비우면 미사용.")]
    [SerializeField] private GameObject _doneIndicator;

    [Header("말소리 (타자기 블립)")]
    [Tooltip("타이핑 중 글자마다 낼 말소리(랜덤 재생). 비우면 Resources/Audio/Talk1~3 자동 로드.")]
    [SerializeField] private AudioClip[] _talkClips;
    [Range(0f, 1f)]
    [SerializeField] private float _talkVolume = 0.5f;
    [Tooltip("몇 글자마다 말소리를 낼지(2=두 글자마다 한 번).")]
    [SerializeField] private int _blipEvery = 2;
    private AudioSource _talkSource;

    private DialogueLineData[] _lines;
    private int _index;
    private Action _onComplete;
    private Coroutine _typingCo;
    private bool _isTyping;

    private void Awake()
    {
        // ▶ 버튼도 동작하게 두되, 화면 아무 곳이나 클릭해도 진행되도록 Update 에서 처리한다.
        if (_nextButton != null)
        {
            _nextButton.onClick.AddListener(ShowNext);
        }

        // 타자기 말소리용 2D AudioSource + 클립 자동 로드(인스펙터 미연결 시 Resources 폴백).
        _talkSource = gameObject.AddComponent<AudioSource>();
        _talkSource.playOnAwake = false;
        _talkSource.spatialBlend = 0f;
        if (_talkClips == null || _talkClips.Length == 0)
        {
            _talkClips = new[]
            {
                Resources.Load<AudioClip>("Audio/Talk1"),
                Resources.Load<AudioClip>("Audio/Talk2"),
                Resources.Load<AudioClip>("Audio/Talk3"),
            };
        }
        if (_blipEvery < 1) _blipEvery = 1;
    }

    private void OnDestroy()
    {
        if (_nextButton != null)
        {
            _nextButton.onClick.RemoveListener(ShowNext);
        }
    }

    /// <summary>타이핑·말소리(타자기 블립)를 즉시 중단한다(엔딩 컷씬 진입 등 외부 인터럽트용).</summary>
    public void StopSpeaking()
    {
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        if (_talkSource != null && _talkSource.isPlaying) _talkSource.Stop();
    }

    private void Update()
    {
        // 대사 표시 중, '대화 창(_root)'을 직접 클릭했을 때만 다음 줄로 진행한다(아무 곳/팝업 클릭으론 안 넘어감).
        if (_root == null || !_root.activeSelf) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        // ▶ 버튼 위 클릭은 버튼이 처리하므로 중복 진행 방지.
        if (_nextButton != null && _nextButton.gameObject.activeInHierarchy
            && IsPointerOver(_nextButton.gameObject)) return;

        // 클릭의 '맨 위' 대상이 대화 창(_root)일 때만 진행 — 책상·배경·팝업을 클릭하면 넘어가지 않는다.
        if (!IsTopmostOver(_root)) return;

        ShowNext();
    }

    /// <summary>현재 포인터 아래 '맨 위' UI가 해당 오브젝트(또는 그 자식)인지.
    /// 다른 UI(팝업 등)가 위에 겹쳐 있으면 false — 그 위를 클릭한 것이므로 대사를 넘기지 않는다.</summary>
    private static bool IsTopmostOver(GameObject go)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || Mouse.current == null) return false;
        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            position = Mouse.current.position.ReadValue()
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(data, results);
        if (results.Count == 0) return false;
        GameObject top = results[0].gameObject;
        return top == go || top.transform.IsChildOf(go.transform);
    }

    /// <summary>현재 포인터가 해당 UI 위에 있는지(레이캐스트).</summary>
    private static bool IsPointerOver(GameObject go)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || Mouse.current == null) return false;
        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            position = Mouse.current.position.ReadValue()
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(data, results);
        foreach (var r in results)
        {
            if (r.gameObject == go || r.gameObject.transform.IsChildOf(go.transform)) return true;
        }
        return false;
    }

    /// <summary>케이스를 재생한다. 끝나면 onComplete 호출.</summary>
    public void Play(DialogueCaseData dialogueCase, Action onComplete)
    {
        _onComplete = onComplete;

        if (dialogueCase == null || dialogueCase.lines == null || dialogueCase.lines.Length == 0)
        {
            // 대사가 없으면 즉시 완료 처리(케이스 누락 가드).
            Hide();
            _onComplete?.Invoke();
            return;
        }

        _lines = SortByOrder(dialogueCase.lines);
        _index = 0;

        if (_root != null)
        {
            _root.SetActive(true);
        }
        RenderCurrent();
    }

    /// <summary>대화창을 숨긴다.</summary>
    public void Hide()
    {
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        _isTyping = false;
        if (_typingIndicator != null) _typingIndicator.SetActive(false);
        if (_doneIndicator != null) _doneIndicator.SetActive(false);
        if (_root != null)
        {
            _root.SetActive(false);
        }
    }

    private void ShowNext()
    {
        // 타이핑 중이면 먼저 전체 문장만 드러내고(스킵), 다음 줄로는 넘어가지 않는다.
        if (_isTyping)
        {
            RevealAll();
            return;
        }
        _index++;
        if (_lines == null || _index >= _lines.Length)
        {
            Hide();
            Action cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
            return;
        }
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        DialogueLineData line = _lines[_index];
        if (_speakerText != null)
        {
            _speakerText.text = line.speaker;
        }
        if (_bodyText != null)
        {
            if (_typingCo != null) StopCoroutine(_typingCo);
            _typingCo = StartCoroutine(TypeLine(line.text ?? string.Empty));
        }
    }

    /// <summary>한 글자씩 드러내는 타자기 연출(언더테일식). 진행 중엔 '...' 표시, 끝나면 엔터 아이콘.</summary>
    private IEnumerator TypeLine(string full)
    {
        _isTyping = true;
        SetIndicator(typing: true);
        _bodyText.text = full;
        _bodyText.maxVisibleCharacters = 0;
        _bodyText.ForceMeshUpdate();
        int total = _bodyText.textInfo.characterCount;
        for (int shown = 0; shown <= total; shown++)
        {
            _bodyText.maxVisibleCharacters = shown;
            // 새 글자가 드러날 때 일정 간격마다 말소리 블립(언더테일식). 공백 글자엔 안 냄.
            if (shown >= 1 && shown % _blipEvery == 0) PlayBlip(shown - 1);
            if (shown < total && _charInterval > 0f) yield return new WaitForSeconds(_charInterval);
        }
        _typingCo = null;
        _isTyping = false;
        SetIndicator(typing: false);
    }

    /// <summary>지정한 글자 인덱스가 공백이 아니면 말소리 클립 하나를 랜덤 재생한다.</summary>
    private void PlayBlip(int charIndex)
    {
        if (_talkSource == null || _talkClips == null || _talkClips.Length == 0) return;
        if (_bodyText != null && charIndex >= 0 && charIndex < _bodyText.textInfo.characterCount)
        {
            char ch = _bodyText.textInfo.characterInfo[charIndex].character;
            if (char.IsWhiteSpace(ch)) return; // 공백엔 말소리 X
        }
        AudioClip clip = _talkClips[UnityEngine.Random.Range(0, _talkClips.Length)];
        if (clip != null) _talkSource.PlayOneShot(clip, _talkVolume);
    }

    /// <summary>타이핑을 즉시 끝내 전체 문장을 보여준다(타이핑 중 클릭 1회 = 스킵).</summary>
    private void RevealAll()
    {
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        if (_bodyText != null)
        {
            _bodyText.ForceMeshUpdate();
            _bodyText.maxVisibleCharacters = _bodyText.textInfo.characterCount;
        }
        _isTyping = false;
        SetIndicator(typing: false);
    }

    /// <summary>진행 표시 전환: 타이핑 중 = '...'(_typingIndicator) / 완료 = 엔터 아이콘(_doneIndicator).</summary>
    private void SetIndicator(bool typing)
    {
        if (_typingIndicator != null) _typingIndicator.SetActive(typing);
        if (_doneIndicator != null) _doneIndicator.SetActive(!typing);
    }

    private static DialogueLineData[] SortByOrder(DialogueLineData[] src)
    {
        List<DialogueLineData> list = new List<DialogueLineData>(src);
        list.Sort((a, b) => a.order.CompareTo(b.order));
        return list.ToArray();
    }
}
