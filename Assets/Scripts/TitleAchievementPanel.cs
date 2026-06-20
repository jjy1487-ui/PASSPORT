using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// 메인 메뉴 '업적' 패널. 회차를 넘어 누적된 보유 호칭을 영속 세이브에서 읽어 표시한다.
/// (플레이 중 호칭 토스트는 제거 — 호칭은 여기서만 확인한다. 요구 3)
///
/// 매니저 인스턴스 없이도 동작하도록 GameProgressSave 영속값을 직접 읽는다.
/// 표시만 한다 — 호칭 획득 판정은 ScoreEconomyManager 가 소유(규약 5장).
/// 닫기 = X, 우상단 / Esc(UI-CONVENTIONS 1장).
/// </summary>
public sealed class TitleAchievementPanel : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;     // 비활성 시작
    [SerializeField] private TMP_Text _listText;   // 호칭 목록
    [SerializeField] private Button _closeButton;  // X, 우상단

    private bool _shown;

    private void Start()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    private void Update()
    {
        if (_shown && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
    }

    /// <summary>패널 열기(메인 메뉴 업적 버튼). 호칭 목록을 새로 읽어 갱신한다.</summary>
    public void Open()
    {
        Refresh();
        _shown = true;
        if (_root != null) _root.SetActive(true);
    }

    /// <summary>열려 있으면 닫고, 닫혀 있으면 연다(버튼 토글).</summary>
    public void Toggle()
    {
        if (_shown) Close();
        else Open();
    }

    private void Close()
    {
        _shown = false;
        if (_root != null) _root.SetActive(false);
    }

    private void Refresh()
    {
        if (_listText == null) return;

        // 매니저가 있으면 그 값을, 없으면(메인 메뉴) 영속 세이브를 읽는다.
        var mgr = ScoreEconomyManager.Instance;
        System.Collections.Generic.IEnumerable<string> titles =
            mgr != null ? mgr.Titles : GameProgressSave.LoadTitles();

        var sb = new StringBuilder();
        sb.AppendLine("<b>획득 호칭</b>");
        sb.AppendLine();

        bool any = false;
        foreach (string t in titles)
        {
            if (string.IsNullOrEmpty(t)) continue;
            sb.Append("· ").AppendLine(t);
            any = true;
        }
        if (!any) sb.AppendLine("(아직 획득한 호칭이 없습니다)");

        _listText.text = sb.ToString().TrimEnd();
    }
}
