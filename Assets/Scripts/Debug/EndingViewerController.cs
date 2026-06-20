using UnityEngine;

/// <summary>
/// 엔딩 컷씬 뷰어(QA 도구). UIPreviewController 패턴 — CoreRig + EventSystem + 이 스크립트가 올라간
/// EndingViewerScene 에서 Play → 우측 버튼으로 각 엔딩 컷씬을 바로 재생(좌클릭으로만 다음 장 넘김).
///
/// 일일이 플레이하지 않고 엔딩만 골라 보기 위한 도구. 실제 게임 진행(ImmigrationManager)은 끈다.
/// 컷씬이 끝나면 씬 전환 없이 버튼 목록으로 돌아온다(EndingPanel.SetTitleScene("")).
/// </summary>
public sealed class EndingViewerController : MonoBehaviour
{
    [Tooltip("비워두면 씬(CoreRig)에서 EndingPanel 을 자동으로 찾는다.")]
    [SerializeField] private EndingPanel endingPanel;

    // (버튼 라벨, 엔딩 이름) — 이름은 EndingPanel.CutsceneKeys 매핑 키. 노말은 미매핑 이름(→ Normal 공용 컷씬).
    private static readonly string[,] Endings =
    {
        { "① 퍼엉! (테러 폭탄)",            "퍼엉!" },
        { "② 공범 (뇌물 수락)",             "공범" },
        { "③ 순진한 녀석 (성형 범죄)",      "순진한 녀석" },
        { "④ 방역 실패",                    "방역 실패" },
        { "⑤ 등잔 밑이 어둡다",             "등잔 밑이 어둡다" },
        { "⑥ 자넨 적성에 안 맞는 것 같네",  "자넨 적성에 안 맞는 것 같네" },
        { "⑦ 노말 엔딩 (점수 공용)",        "우수 사원" },
    };

    private void Awake()
    {
        // 컷씬만 단독 재생: 실제 진행(일차 시작·손님 로드)을 끈다(UIPreview와 동일).
        var manager = FindFirstObjectByType<ImmigrationManager>(FindObjectsInactive.Include);
        if (manager != null) manager.enabled = false;

        if (endingPanel == null)
            endingPanel = FindFirstObjectByType<EndingPanel>(FindObjectsInactive.Include);
        if (endingPanel != null)
        {
            if (!endingPanel.gameObject.activeSelf) endingPanel.gameObject.SetActive(true); // Show 호출 가능하게
            endingPanel.SetTitleScene(string.Empty); // 컷씬 끝 → 씬 전환 없이 목록 복귀
        }
    }

    private void OnGUI()
    {
        if (endingPanel != null && endingPanel.IsCutscenePlaying)
        {
            return; // 컷씬 중엔 안내문·버튼 숨김 — 좌클릭으로만 진행(컷씬 위로 아무것도 안 그림)
        }

        GUI.skin.button.fontSize = 14;
        // 우측 패널(왼쪽 화면을 가리지 않게 — UIPreview와 동일).
        GUILayout.BeginArea(new Rect(Screen.width - 312, 12, 300, Screen.height - 24), GUI.skin.box);
        GUILayout.Label("<b>=== 엔딩 뷰어 (QA) ===</b>");
        GUILayout.Label("버튼 클릭 → 해당 엔딩 컷씬 재생");
        GUILayout.Space(8);

        if (endingPanel == null)
        {
            GUILayout.Label("EndingPanel 없음 — 씬에 CoreRig를 올렸나요?");
        }
        else
        {
            int n = Endings.GetLength(0);
            for (int i = 0; i < n; i++)
                if (GUILayout.Button(Endings[i, 0]))
                    endingPanel.Show(new EndingResult("END_VIEWER", Endings[i, 1], "early", "viewer"));
        }
        GUILayout.EndArea();
    }
}
