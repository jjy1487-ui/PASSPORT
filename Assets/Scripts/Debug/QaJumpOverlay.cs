#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// [QA/검수 전용 · 릴리스 빌드 미포함] 손님 점프 오버레이.
///
/// 왜 필요한가:
///  - 검수할 때 특정 손님(예: 7일차 5번째)을 보려고 매번 1일차부터 다시 플레이하는 게 불편했다.
///  - 이 패널은 게임플레이 중 "원하는 일차 + 손님 번호"로 즉시 건너뛰게 해준다.
///
/// 특징:
///  - 씬에 손댈 필요 없음: 플레이 시작 시 자동으로 떠서 ImmigrationManager 를 찾아 붙는다.
///  - 백틱( ` ) 키로 패널을 켜고 끈다. 패널 제목줄을 끌어 위치를 옮길 수 있다.
///  - UNITY_EDITOR / 개발빌드에서만 컴파일된다(정식 릴리스 빌드엔 포함되지 않음).
///  - 같은 일차 안의 이동은 데이터를 다시 로드하지 않아 셔플/확률변형이 보존된다.
///    (날짜를 바꿀 때만 그 일차 데이터를 새로 로드한다 — ImmigrationManager.DebugJumpTo)
/// </summary>
public sealed class QaJumpOverlay : MonoBehaviour
{
    private static QaJumpOverlay _instance;

    // 플레이 시작 시(첫 씬 로드 후) 자동으로 오버레이 오브젝트를 만든다 — 씬 배치 불필요.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (_instance != null) return;
        var go = new GameObject("[QA] JumpOverlay");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<QaJumpOverlay>();
    }

    private bool _visible = true;
    private int _day = 1;  // 점프 목표 일차(1~14)
    private int _slot = 1; // 점프 목표 손님(1~7)
    private ImmigrationManager _mgr;
    private Rect _win = new Rect(12, 12, 248, 0);
    private const int WinId = 0x5141; // 'QA'

    private ImmigrationManager Mgr
    {
        get
        {
            if (_mgr == null) _mgr = FindFirstObjectByType<ImmigrationManager>();
            return _mgr;
        }
    }

    private void OnGUI()
    {
        // 심사 씬이 아닐 때(메뉴/결과 씬 등)는 화면을 가리지 않도록 아무것도 그리지 않는다.
        if (Mgr == null) return;

        // 백틱( ` )으로 패널 토글.
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.BackQuote)
        {
            _visible = !_visible;
            e.Use();
        }

        if (!_visible)
        {
            if (GUI.Button(new Rect(12, 12, 104, 24), "검수 ` 열기")) _visible = true;
            return;
        }

        _win = GUILayout.Window(WinId, _win, DrawWindow, "검수 점프  ( ` 토글 )");
    }

    private void DrawWindow(int id)
    {
        ImmigrationManager mgr = Mgr;

        GUILayout.Label($"현재: {mgr.CurrentDay}일차 · 손님 {mgr.CurrentSlot1Based}/{mgr.CurrentDayCustomerCount}");
        GUILayout.Space(4);

        // 목표 일차 선택(1~14)
        GUILayout.BeginHorizontal();
        GUILayout.Label("일차", GUILayout.Width(36));
        if (GUILayout.Button("◀", GUILayout.Width(30))) _day = Mathf.Max(1, _day - 1);
        GUILayout.Label(_day.ToString(), GUILayout.Width(30));
        if (GUILayout.Button("▶", GUILayout.Width(30))) _day = Mathf.Min(14, _day + 1);
        GUILayout.EndHorizontal();

        // 목표 손님 선택(1~7)
        GUILayout.BeginHorizontal();
        GUILayout.Label("손님", GUILayout.Width(36));
        if (GUILayout.Button("◀", GUILayout.Width(30))) _slot = Mathf.Max(1, _slot - 1);
        GUILayout.Label(_slot.ToString(), GUILayout.Width(30));
        if (GUILayout.Button("▶", GUILayout.Width(30))) _slot = Mathf.Min(7, _slot + 1);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        if (GUILayout.Button($"▶ {_day}일차 {_slot}번째 손님으로 이동", GUILayout.Height(30)))
        {
            mgr.DebugJumpTo(_day, _slot);
        }

        GUILayout.Space(2);
        // 현재 일차 안에서 빠른 이전/다음(데이터 리로드 없음 → 셔플/변형 보존).
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ 이전 손님"))
            mgr.DebugJumpTo(mgr.CurrentDay, Mathf.Max(1, mgr.CurrentSlot1Based - 1));
        if (GUILayout.Button("다음 손님 ▶"))
            mgr.DebugJumpTo(mgr.CurrentDay, mgr.CurrentSlot1Based + 1);
        GUILayout.EndHorizontal();

        // 제목줄을 끌어 패널 이동.
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
}
#endif
