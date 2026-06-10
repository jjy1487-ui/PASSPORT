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
    [Header("지문판독기(3단계 연출 — 데이터 주입식 단독 재생)")]
    [Tooltip("비워두면 씬에서 fingerprint 종류 패널을 자동으로 찾는다.")]
    [SerializeField] private ScanResultPanel fingerprintPanel;
    [Tooltip("샘플 여권(대조용). 비워두면 자동으로 찾는다.")]
    [SerializeField] private PreviewPassport samplePassport;

    [Header("X-ray 전신 검사(데이터 주입식 단독 재생)")]
    [Tooltip("비워두면 씬에서 XrayInspectionPanel 을 자동으로 찾는다.")]
    [SerializeField] private XrayInspectionPanel xrayPanel;

    [Header("서류 카드 미리보기(종류별 드래그 확인)")]
    [Tooltip("비워두면 씬에서 DocumentView(검사 데스크)를 자동으로 찾는다.")]
    [SerializeField] private DocumentView documentView;

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

        if (fingerprintPanel == null)
        {
            foreach (var p in FindObjectsByType<ScanResultPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p.ScanKindKey == "fingerprint") { fingerprintPanel = p; break; }
        }
        if (samplePassport == null)
            samplePassport = FindFirstObjectByType<PreviewPassport>(FindObjectsInactive.Include);
        if (xrayPanel == null)
            xrayPanel = FindFirstObjectByType<XrayInspectionPanel>(FindObjectsInactive.Include);
        if (documentView == null)
            documentView = FindFirstObjectByType<DocumentView>(FindObjectsInactive.Include);
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
        GUILayout.Label("[서류 카드 — 종류별 드래그 확인]");
        if (documentView != null)
        {
            if (GUILayout.Button("여권 띄우기")) ShowDocument(SampleDoc.Passport);
            if (GUILayout.Button("비자 띄우기")) ShowDocument(SampleDoc.Visa);
            if (GUILayout.Button("PCR검사서 띄우기")) ShowDocument(SampleDoc.Pcr);
            if (GUILayout.Button("취업증빙 띄우기")) ShowDocument(SampleDoc.Employment);
            if (GUILayout.Button("심사오류고지서 발부")) ShowNotice();
            if (GUILayout.Button("서류 치우기")) ClearDocuments();
        }
        else
        {
            GUILayout.Label("DocumentView 없음 — CoreRig를 올렸나요?");
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

    // ── 서류 카드 미리보기 ────────────────────────────────────────
    // 각 서류 양식에 맞는 그럴듯한 샘플 데이터를 DocumentView 에 주입해
    // 종류별 카드를 띄운다. 사용자가 닫힌 썸네일을 책상으로 드래그하면 펼쳐지고,
    // 마우스를 올리면 확대된다(MagnifyOnHover). 판정/변조 로직은 없다 — 표시 전용.

    private enum SampleDoc { Passport, Visa, Pcr, Employment }

    /// <summary>
    /// 한 종류의 서류 카드만 책상에 띄운다(Show 는 기존 카드를 지우고 새로 깐다).
    /// 필드 라벨/키는 실제 양식·속성 키 어휘(SHARED-CONVENTIONS 3.8)와 맞춘다.
    /// </summary>
    private void ShowDocument(SampleDoc kind)
    {
        DocumentData doc = kind switch
        {
            SampleDoc.Passport   => SamplePassportDoc(),
            SampleDoc.Visa       => SampleVisaDoc(),
            SampleDoc.Pcr        => SamplePcrDoc(),
            SampleDoc.Employment => SampleEmploymentDoc(),
            _                    => SamplePassportDoc(),
        };
        documentView.Show(new[] { doc });
    }

    /// <summary>심사 오류 고지서를 발부한다(SpawnNotice — 누적·드래그 가능). 전용 NoticeCard 확인용.</summary>
    private void ShowNotice()
    {
        var notice = new DocumentData
        {
            documentType = "심사 오류 고지서",
            variant = "정상",
            violationField = "없음",
            country = "",
            spriteRef = "",
            fields = new[]
            {
                new FieldEntry { label = "판정", value = "입국 거부 대상이었습니다", key = "" },
                new FieldEntry { label = "여권",  value = "유효기간 항목이 올바르지 않습니다", key = "" },
                new FieldEntry { label = "사유",  value = "만료된 서류로 입국을 시도했습니다", key = "" },
            },
        };
        documentView.SpawnNotice(notice);
    }

    /// <summary>띄운 서류 카드를 모두 치운다(고지서 포함).</summary>
    private void ClearDocuments()
    {
        documentView.Clear();
        documentView.ClearNotices();
    }

    private static DocumentData SamplePassportDoc() => new DocumentData
    {
        documentType = "여권",
        variant = "정상", violationField = "없음", country = "KOR", spriteRef = "김민준.png",
        fields = new[]
        {
            new FieldEntry { label = "이름",     value = "KIM MINJUN",  key = "name" },
            new FieldEntry { label = "성별",     value = "남성",         key = "gender" },
            new FieldEntry { label = "생년월일", value = "1990-07-15",   key = "birth_date" },
            new FieldEntry { label = "국적",     value = "KOR",          key = "nationality" },
            new FieldEntry { label = "여권번호", value = "KO1010053",    key = "passport_no" },
            new FieldEntry { label = "발급일",   value = "2020-03-10",   key = "issue_date" },
            new FieldEntry { label = "유효기간", value = "2030-03-09",   key = "expiry_date" },
        },
    };

    private static DocumentData SampleVisaDoc() => new DocumentData
    {
        documentType = "비자",
        variant = "정상", violationField = "없음", country = "", spriteRef = "",
        fields = new[]
        {
            new FieldEntry { label = "이름",     value = "KIM MINJUN", key = "name" },
            new FieldEntry { label = "국적",     value = "KOR",        key = "nationality" },
            new FieldEntry { label = "여권번호", value = "KO1010053",  key = "passport_no" },
            new FieldEntry { label = "비자번호", value = "V-2024-0451", key = "visa_no" },
            new FieldEntry { label = "비자종류", value = "단기방문(C-3)", key = "visa_type" },
            new FieldEntry { label = "발급일",   value = "2024-01-12", key = "issue_date" },
            new FieldEntry { label = "유효기간", value = "2024-07-12", key = "expiry_date" },
        },
    };

    private static DocumentData SamplePcrDoc() => new DocumentData
    {
        documentType = "PCR검사서",
        variant = "정상", violationField = "없음", country = "", spriteRef = "",
        fields = new[]
        {
            new FieldEntry { label = "이름",     value = "KIM MINJUN", key = "name" },
            new FieldEntry { label = "국적",     value = "KOR",        key = "nationality" },
            new FieldEntry { label = "검사번호", value = "PCR-77120",  key = "test_no" },
            new FieldEntry { label = "검사결과", value = "음성",        key = "pcr_result" },
            new FieldEntry { label = "검사일",   value = "2024-05-01", key = "issue_date" },
            new FieldEntry { label = "유효기한", value = "2024-05-04", key = "valid_until" },
            new FieldEntry { label = "검사기관", value = "서울중앙검사소", key = "lab_name" },
        },
    };

    private static DocumentData SampleEmploymentDoc() => new DocumentData
    {
        documentType = "취업증빙",
        variant = "정상", violationField = "없음", country = "", spriteRef = "",
        fields = new[]
        {
            new FieldEntry { label = "이름",     value = "KIM MINJUN",  key = "name" },
            new FieldEntry { label = "직책",     value = "소프트웨어 엔지니어", key = "job_title" },
            new FieldEntry { label = "회사명",   value = "한빛테크 주식회사",  key = "company_name" },
            new FieldEntry { label = "증명번호", value = "EMP-2024-318", key = "cert_no" },
            new FieldEntry { label = "입사일",   value = "2022-04-01",   key = "hire_date" },
            new FieldEntry { label = "발급일",   value = "2024-05-20",   key = "issue_date" },
        },
    };
}
