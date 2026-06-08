using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 팝업 "미리보기" 디버그 컨트롤러.
/// 게임 전체(ImmigrationManager + 14일 데이터)를 돌리지 않고, 팝업 하나만 처음~끝까지 확인하기 위한 도구.
///
/// 왜 필요한가:
///  - 뉴스/검사 같은 팝업은 "매니저 + 이벤트 + 데이터"로 구동된다. 그래서 프리팹만 열면
///    비활성/빈 상태(투명해 보임)라서 동작을 못 본다. 이 컨트롤러가 그 매니저 역할을 대신해
///    샘플 데이터를 직접 주입해 팝업을 열어준다.
///
/// 사용법:
///  1) 빈 씬을 만든다(또는 Assets/Scenes/UIPreview.unity).
///  2) CoreRig 프리팹 + EventSystem + 이 스크립트를 올린다.
///  3) 인스펙터에서 extraPanels 에 보고 싶은 팝업 루트(NewsPanel/FingerprintPanel/EndingPanel...)를 드래그.
///  4) Play → 화면 좌상단 버튼 클릭 → 팝업이 샘플 데이터와 함께 처음~끝까지 동작.
/// </summary>
public sealed class UIPreviewController : MonoBehaviour
{
    [Header("뉴스(데이터 직접 주입 — 완전 단독 재생)")]
    [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
    [SerializeField] private NewsPopup newsPopup;

    [Header("그 외 팝업 루트 (드래그해 넣으면 켜기/끄기 버튼 생성)")]
    [Tooltip("NewsPanel/FingerprintPanel/XrayPanel/EndingPanel 등의 루트 GameObject")]
    [SerializeField] private List<GameObject> extraPanels = new List<GameObject>();

    private void Awake()
    {
        if (newsPopup == null)
            newsPopup = FindFirstObjectByType<NewsPopup>(FindObjectsInactive.Include);
    }

    private void OnGUI()
    {
        GUI.skin.button.fontSize = 14;
        GUILayout.BeginArea(new Rect(12, 12, 260, Screen.height - 24), GUI.skin.box);
        GUILayout.Label("<b>=== UI 미리보기 ===</b>");

        GUILayout.Space(6);
        GUILayout.Label("[뉴스]");
        if (newsPopup != null)
        {
            if (GUILayout.Button("뉴스 팝업 열기 (샘플)")) ShowSampleNews();
            if (GUILayout.Button("뉴스 팝업 닫기")) newsPopup.Close();
        }
        else
        {
            GUILayout.Label("NewsPopup 없음 — CoreRig를 올렸나요?");
        }

        GUILayout.Space(10);
        GUILayout.Label("[그 외 팝업 토글]");
        if (extraPanels.Count == 0)
            GUILayout.Label("인스펙터 extraPanels 에 팝업을 드래그하세요.");
        foreach (var go in extraPanels)
        {
            if (go == null) continue;
            bool on = go.activeSelf;
            if (GUILayout.Button((on ? "■ 끄기  " : "▶ 켜기  ") + go.name))
                go.SetActive(!on);
        }
        GUILayout.EndArea();
    }

    /// <summary>샘플 뉴스 2건을 넣어 뉴스 팝업을 연다(이전/다음/닫기까지 실제 동작).</summary>
    private void ShowSampleNews()
    {
        var sample = new List<NewsData>
        {
            new NewsData
            {
                newsId = 1,
                title = "[속보] 위조 여권 적발 급증",
                content = "최근 위조 여권을 이용한 입국 시도가 늘고 있습니다. 만료일과 출국 도장을 반드시 확인하십시오.",
                iconRef = "",
                claims = new[] { new Claim { attr = "nationality", value = "VNM", label = "주의 국적", unlocksScan = "" } }
            },
            new NewsData
            {
                newsId = 2,
                title = "[수배] 성형 위장 범죄자 주의",
                content = "성형으로 얼굴을 바꾼 수배자가 입국을 시도할 수 있습니다. 의심 시 지문 대조를 실시하세요.",
                iconRef = "",
                claims = new[] { new Claim { attr = "name", value = "", label = "수배 단서", unlocksScan = "fingerprint" } }
            },
        };
        newsPopup.Open(sample);
    }
}
