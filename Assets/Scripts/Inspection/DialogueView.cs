using System;
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

    private DialogueLineData[] _lines;
    private int _index;
    private Action _onComplete;

    private void Awake()
    {
        // ▶ 버튼도 동작하게 두되, 화면 아무 곳이나 클릭해도 진행되도록 Update 에서 처리한다.
        if (_nextButton != null)
        {
            _nextButton.onClick.AddListener(ShowNext);
        }
    }

    private void OnDestroy()
    {
        if (_nextButton != null)
        {
            _nextButton.onClick.RemoveListener(ShowNext);
        }
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
        if (_root != null)
        {
            _root.SetActive(false);
        }
    }

    private void ShowNext()
    {
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
            _bodyText.text = line.text;
        }
    }

    private static DialogueLineData[] SortByOrder(DialogueLineData[] src)
    {
        List<DialogueLineData> list = new List<DialogueLineData>(src);
        list.Sort((a, b) => a.order.CompareTo(b.order));
        return list.ToArray();
    }
}
