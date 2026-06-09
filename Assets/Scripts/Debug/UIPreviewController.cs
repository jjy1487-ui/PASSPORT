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

    [Header("지문판독기(3단계 연출 — 데이터 주입식 단독 재생)")]
    [Tooltip("비워두면 씬에서 fingerprint 종류 패널을 자동으로 찾는다.")]
    [SerializeField] private ScanResultPanel fingerprintPanel;
    [Tooltip("샘플 여권(대조용). 비워두면 자동으로 찾는다.")]
    [SerializeField] private PreviewPassport samplePassport;

    [Header("X-ray 전신 검사(데이터 주입식 단독 재생)")]
    [Tooltip("비워두면 씬에서 XrayInspectionPanel 을 자동으로 찾는다.")]
    [SerializeField] private XrayInspectionPanel xrayPanel;

    [Header("그 외 팝업 루트 (드래그해 넣으면 켜기/끄기 버튼 생성)")]
    [Tooltip("NewsPanel/FingerprintPanel/XrayPanel/EndingPanel 등의 루트 GameObject")]
    [SerializeField] private List<GameObject> extraPanels = new List<GameObject>();

    private void Awake()
    {
        // 단독 재생 보장: CoreRig 를 그대로 올리면 ImmigrationManager.Start 가 BeginDay 로
        // 1일차 손님을 로드하며 InspectionController.OnCustomerChanged 를 계속 쏜다.
        // 그 이벤트가 ScanResultPanel.HandleCustomerChanged 를 깨워 주입한 미리보기 데이터를
        // 덮어쓰거나 닫아버린다(= DB 대조행이 사라져 클릭 불가). 미리보기에서는 실제 게임 루프를
        // 끈다. Awake 단계에서 끄면(모든 Awake 는 어떤 Start 보다 먼저 실행) 그 Start 가 스킵된다.
        DisableLiveGameLoop();

        if (newsPopup == null)
            newsPopup = FindFirstObjectByType<NewsPopup>(FindObjectsInactive.Include);

        if (fingerprintPanel == null)
        {
            foreach (var p in FindObjectsByType<ScanResultPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p.ScanKindKey == "fingerprint") { fingerprintPanel = p; break; }
        }
        if (samplePassport == null)
            samplePassport = FindFirstObjectByType<PreviewPassport>(FindObjectsInactive.Include);
        if (xrayPanel == null)
            xrayPanel = FindFirstObjectByType<XrayInspectionPanel>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// 미리보기 단독 재생을 위해 실제 게임 진행(일차 시작·손님 로드)을 멈춘다.
    /// ImmigrationManager 를 비활성화하면 그 Start 의 BeginDay 가 실행되지 않아 손님이 로드되지 않고,
    /// 따라서 OnCustomerChanged 도 발생하지 않는다. 팝업·캔버스(CoreRig)는 그대로 살아 있어
    /// 주입식 미리보기가 깨끗하게 단독으로 돈다. (디버그 씬 전용 — 프리팹은 변경하지 않는다.)
    /// </summary>
    private void DisableLiveGameLoop()
    {
        var manager = FindFirstObjectByType<ImmigrationManager>(FindObjectsInactive.Include);
        if (manager != null) manager.enabled = false;
    }

    private void OnGUI()
    {
        GUI.skin.button.fontSize = 14;
        // 디버그 버튼은 오른쪽에 둔다(왼쪽의 여권/대조 항목을 가려 클릭을 가로채지 않도록).
        GUILayout.BeginArea(new Rect(Screen.width - 272, 12, 260, Screen.height - 24), GUI.skin.box);
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
        GUILayout.Label("[지문판독기 3단계 연출]");
        if (fingerprintPanel != null)
        {
            if (GUILayout.Button("지문 ▶ 수배자 (윤서린)")) ShowFingerprint(FpCase.Wanted);
            if (GUILayout.Button("지문 ▶ 본인 (성형/정상)")) ShowFingerprint(FpCase.Self);
            if (GUILayout.Button("지문 ▶ 도용 (가짜)")) ShowFingerprint(FpCase.Stolen);
        }
        else
        {
            GUILayout.Label("FingerprintPanel 없음 — CoreRig를 올렸나요?");
        }

        GUILayout.Space(10);
        GUILayout.Label("[X-ray 전신 검사]");
        if (xrayPanel != null)
        {
            if (GUILayout.Button("X-ray ▶ 밀수품 (존카터)")) ShowXray(XrayCase.Smuggle);
            if (GUILayout.Button("X-ray ▶ 폭발물 (사토)")) ShowXray(XrayCase.Explosive);
            if (GUILayout.Button("X-ray ▶ 마약 (강도식)")) ShowXray(XrayCase.Drug);
            if (GUILayout.Button("X-ray ▶ 정상 (깨끗)")) ShowXray(XrayCase.Clean);
        }
        else
        {
            GUILayout.Label("XrayPanel 없음 — CoreRig를 올렸나요?");
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

    private enum FpCase { Wanted, Self, Stolen }

    /// <summary>
    /// 지문판독기(스캔→DB조회)를 케이스별로 재생하고, 같은 케이스의 "여권(주장 신원)"도 띄운다.
    /// 플레이어가 DB(진짜 신원) ↔ 여권(주장 신원)을 직접 대조 → 다르면 도용, 범죄기록 있으면 수배자.
    /// </summary>
    private void ShowFingerprint(FpCase c)
    {
        FingerprintRecord rec;            // DB(지문) = 진짜 신원
        string pN, pB;                    // 여권 = 주장 신원
        const string KOR = "대한민국 (KOR)";
        switch (c)
        {
            case FpCase.Wanted: // 여권은 '윤서린'인데 지문 진짜 신원은 수배자 '김서린' → 불일치 + 범죄기록
                rec = new FingerprintRecord { dbName = "김서린", dbBirth = "1988.04.12", dbNationality = KOR,
                    criminalRecord = "성형 위장 / 지명수배 중", wantedNo = "WA-2023-001192" };
                pN = "윤서린"; pB = "1990.07.15";
                break;
            case FpCase.Self: // 여권=지문 신원 동일(정유나), 범죄기록 없음 → 본인
                rec = new FingerprintRecord { dbName = "정유나", dbBirth = "1995.06.20", dbNationality = KOR,
                    criminalRecord = "없음", wantedNo = "" };
                pN = "정유나"; pB = "1995.06.20";
                break;
            default: // 여권='노가은'인데 지문 진짜 신원은 '박도윤' → 불일치(도용)
                rec = new FingerprintRecord { dbName = "박도윤", dbBirth = "1992.08.01", dbNationality = KOR,
                    criminalRecord = "없음", wantedNo = "" };
                pN = "노가은"; pB = "1993.11.02";
                break;
        }
        if (samplePassport != null) samplePassport.SetPassport(pN, pB, KOR);
        fingerprintPanel.PreviewPlay(new ScanData { type = "fingerprint", record = rec });
    }

    private enum XrayCase { Smuggle, Explosive, Drug, Clean }

    /// <summary>
    /// 전신 X-ray 검사를 케이스별로 단독 재생한다(실제 데이터와 동일: 밀수품/가슴·폭발물/다리·마약/복부).
    /// 적발물은 claim(attr=contraband)로 노출되어 뉴스/규정과 교차대조 가능.
    /// </summary>
    private void ShowXray(XrayCase c)
    {
        string result = "적발", detail = "", extra = "";
        switch (c)
        {
            case XrayCase.Smuggle:   detail = "밀수품";      extra = "가슴"; break;
            case XrayCase.Explosive: detail = "폭발물 부품"; extra = "다리"; break;
            case XrayCase.Drug:      detail = "마약";        extra = "복부"; break;
            default:                 result = "정상";        detail = ""; extra = ""; break; // 깨끗(NO ABNORMALITIES)
        }
        var data = new ScanData
        {
            type = "xray", result = result, detail = detail, extra = extra,
            claim = string.IsNullOrEmpty(detail) ? null
                  : new Claim { attr = "contraband", value = detail, label = "X-ray 적발물", unlocksScan = "" },
        };
        xrayPanel.PreviewPlay(data);
    }
}
