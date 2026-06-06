using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 대조(Space) 튜토리얼. 최초 1회만 표시한다(PlayerPrefs 플래그).
/// 화면 클릭 또는 확인 버튼으로 닫는다. (스페이스 키는 닫기에 쓰지 않는다 — 대조 모드 토글과 충돌 방지)
/// </summary>
public sealed class CrossCheckTutorial : MonoBehaviour
{
    [SerializeField] private GameObject _root;        // 튜토리얼 오버레이 루트
    [SerializeField] private Button _closeButton;     // "확인" 버튼(선택)
    [Tooltip("true면 한 번 본 뒤로는 표시하지 않는다.")]
    [SerializeField] private bool _showOnce = true;

    private const string SeenKey = "tut_crosscheck_seen";

    private void Start()
    {
        bool isDay1 = GetCurrentDay() == 1;
        bool seen = _showOnce && PlayerPrefs.GetInt(SeenKey, 0) == 1;
        if (_root != null) _root.SetActive(isDay1 && !seen); // 1일차에만 표시
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
    }

    /// <summary>현재 일차. 씬 이름 'Day{N}Scene' 우선, 없으면 PlayerPrefs "CurrentDay"(기본 1).</summary>
    private static int GetCurrentDay()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var m = System.Text.RegularExpressions.Regex.Match(sceneName, "^Day(\\d+)Scene$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) return n;
        return PlayerPrefs.GetInt("CurrentDay", 1);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
    }

    private void Update()
    {
        if (_root == null || !_root.activeSelf) return;
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Close();
        }
    }

    /// <summary>튜토리얼을 닫고 본 것으로 기록한다.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>다시 보기(설정 등에서 호출). 플래그 무시하고 표시.</summary>
    public void ShowAgain()
    {
        if (_root != null) _root.SetActive(true);
    }
}
