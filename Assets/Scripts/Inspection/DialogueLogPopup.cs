using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 음성기록(대화 기록) 팝업. 현재 손님이 한 대화를 모아 보여준다. 비활성 시작.
/// </summary>
public sealed class DialogueLogPopup : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _logText;
    [SerializeField] private Button _closeButton;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    /// <summary>대화 기록을 표시한다.</summary>
    public void Open(IReadOnlyList<string> lines)
    {
        if (_logText != null)
        {
            if (lines == null || lines.Count == 0)
            {
                _logText.text = "(아직 대화 내용이 없습니다)";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach (string line in lines)
                {
                    sb.Append("• ").Append(line).Append('\n');
                }
                _logText.text = sb.ToString().TrimEnd('\n');
            }
        }
        if (_root != null) _root.SetActive(true);
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
    }
}
