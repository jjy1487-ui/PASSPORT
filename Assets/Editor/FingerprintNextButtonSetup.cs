using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

// ─────────────────────────────────────────────────────────────
//  FingerprintPanel 셋업 (idempotent) — 메뉴: Tools/Passport/Setup Fingerprint Panel
//  지문판독기 팝업을 "두 개의 레이어(팝업)"로 분리해 구성한다.
//   ├ Stage1_Scan   (팝업1=스캔 중)   : ScannerImage + ScanText("스캔 중...")
//   └ Stage2_Result (팝업2=DB 조회결과): DbRow_Name/Birth/Nat + ResultText(범죄기록)
//  Title/CloseButton 은 공통(패널 직속). 이렇게 나눠야 씬 편집 시 한 레이어만 켜고 끄며 따로 만질 수 있고,
//  런타임엔 ScanResultPanel 이 SetStage(1/2)로 레이어 단위 전환한다.
// ─────────────────────────────────────────────────────────────
public static class FingerprintNextButtonSetup
{
    private const string PrefabPath = "Assets/Prefabs/CoreRig.prefab";
    private const string ScannerSpritePath = "Assets/Images/UI/FingerprintScanner.png";

    [MenuItem("Tools/Passport/Setup Fingerprint Panel")]
    public static void Setup()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // 경로 대신 이름으로 탐색(계층 이름/위치가 바뀌어도 견딤).
            Transform fp = FindDeep(root.transform, "FingerprintPanel");
            if (fp == null) { Debug.LogError("[FpSetup] FingerprintPanel 못 찾음"); return; }
            var panel = fp.GetComponent<ScanResultPanel>();

            // 템플릿: 기존 claim Item (CrossCheckItemView). 숨기기 전에 먼저 확보.
            Transform claimSel = FindDeep(fp, "ClaimSelectable");
            CrossCheckItemView template = claimSel != null
                ? claimSel.GetComponentInChildren<CrossCheckItemView>(true)
                : fp.GetComponentInChildren<CrossCheckItemView>(true);
            if (template == null) { Debug.LogError("[FpSetup] CrossCheckItemView 템플릿 못 찾음"); return; }

            // ── 두 레이어 컨테이너(풀스트레치) ───────────────────────────
            Transform stage1 = EnsureContainer(fp, "Stage1_Scan");
            Transform stage2 = EnsureContainer(fp, "Stage2_Result");

            // ── 팝업1: 스캐너 이미지 ─────────────────────────────────────
            Transform exImg = FindDeep(fp, "ScannerImage");
            GameObject scanGo = exImg != null ? exImg.gameObject
                : new GameObject("ScannerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            scanGo.transform.SetParent(stage1, false);
            SetCentered((RectTransform)scanGo.transform, new Vector2(0f, 30f), new Vector2(150f, 150f));
            var scanImg = scanGo.GetComponent<Image>();
            scanImg.raycastTarget = false;     // 클릭 흡수 안 함(대조 클릭 방해 X)
            scanImg.preserveAspect = true;
            Sprite scanSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ScannerSpritePath);
            if (scanSprite != null) { scanImg.sprite = scanSprite; scanImg.color = Color.white; }
            else Debug.LogWarning($"[FpSetup] 스캐너 스프라이트 없음 → '{ScannerSpritePath}' 에 PNG 저장 후 다시 실행하면 자동 연결됩니다.");
            scanGo.SetActive(true);

            // ── 팝업1: 스캔 중 안내문(ResultText 복제로 폰트 승계) ──────────
            Transform resultT = FindDeep(fp, "ResultText");
            Transform exScan = FindDeep(fp, "ScanText");
            GameObject scanTextGo = exScan != null ? exScan.gameObject
                : (resultT != null ? Object.Instantiate(resultT.gameObject) : null);
            TMP_Text scanTmp = null;
            if (scanTextGo != null)
            {
                scanTextGo.name = "ScanText";
                scanTextGo.transform.SetParent(stage1, false);
                SetCentered((RectTransform)scanTextGo.transform, new Vector2(0f, -110f), new Vector2(520f, 90f));
                scanTextGo.SetActive(true);
                scanTmp = scanTextGo.GetComponent<TMP_Text>();
                if (scanTmp != null)
                {
                    scanTmp.alignment = TextAlignmentOptions.Center;
                    scanTmp.enableAutoSizing = false;
                    scanTmp.fontSize = 26;
                    scanTmp.text = "[ 지문 스캔 중입니다... ]\n잠시 기다려 주세요.";
                }
            }

            // ── 팝업2: DB 대조 행 3개(이름/생년월일/국적) ─────────────────
            string[] rowNames = { "DbRow_Name", "DbRow_Birth", "DbRow_Nat" };
            float[] rowY = { 95f, 47f, -1f };
            var dbRows = new CrossCheckItemView[3];
            for (int i = 0; i < 3; i++)
            {
                Transform ex = FindDeep(fp, rowNames[i]);
                GameObject row = ex != null ? ex.gameObject : Object.Instantiate(template.gameObject);
                row.name = rowNames[i];
                row.transform.SetParent(stage2, false);
                row.SetActive(true);
                SetCentered((RectTransform)row.transform, new Vector2(0f, rowY[i]), new Vector2(660f, 46f));
                dbRows[i] = row.GetComponent<CrossCheckItemView>();

                // 행 글씨: 크게(28) + 좌측 정렬 + 좌우 여백(Bind 는 텍스트만 바꾸므로 여기 설정이 유지됨)
                var lbl = row.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null)
                {
                    lbl.fontSize = 28;
                    lbl.enableAutoSizing = false;
                    lbl.alignment = TextAlignmentOptions.Left;
                    var lrt = (RectTransform)lbl.transform;
                    lrt.offsetMin = new Vector2(24f, lrt.offsetMin.y);  // 왼쪽 여백
                    lrt.offsetMax = new Vector2(-12f, lrt.offsetMax.y); // 오른쪽 여백
                }
            }

            // ── 팝업2: ResultText(범죄기록) — 행 아래, 좌상단 정렬 ─────────
            if (resultT != null)
            {
                resultT.SetParent(stage2, false);
                var rt = (RectTransform)resultT;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(60f, 70f);
                rt.offsetMax = new Vector2(-60f, -260f);
                var rtmp = resultT.GetComponent<TMP_Text>();
                if (rtmp != null) { rtmp.alignment = TextAlignmentOptions.TopLeft; rtmp.enableAutoSizing = false; rtmp.fontSize = 24; }
            }

            // ── 미사용 요소 숨김 ─────────────────────────────────────────
            if (claimSel != null) claimSel.gameObject.SetActive(false);
            Transform nb = FindDeep(fp, "NextButton");
            if (nb != null) nb.gameObject.SetActive(false);

            // ── 패널 드래그 이동(배경 잡고 끌기; 행/닫기 버튼은 자기 클릭 우선) ─
            var panelImg = fp.GetComponent<Image>();
            if (panelImg != null) panelImg.raycastTarget = true;   // 배경을 잡아야 끌리므로 켬
            if (fp.GetComponent<DraggablePanel>() == null) fp.gameObject.AddComponent<DraggablePanel>();

            // ── ScanResultPanel 직렬화 필드 연결 ─────────────────────────
            if (panel != null)
            {
                var so = new SerializedObject(panel);
                var rowsProp = so.FindProperty("_dbRows");
                if (rowsProp != null)
                {
                    rowsProp.arraySize = 3;
                    for (int i = 0; i < 3; i++) rowsProp.GetArrayElementAtIndex(i).objectReferenceValue = dbRows[i];
                }
                SetRef(so, "_scannerImage", scanGo);
                SetRef(so, "_stage1Group", stage1.gameObject);
                SetRef(so, "_stage2Group", stage2.gameObject);
                if (scanTmp != null) SetRef(so, "_scanText", scanTmp);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[FpSetup] 완료: 2레이어 분리(Stage1_Scan=이미지+안내 / Stage2_Result=DB행+범죄기록) + 필드 연결");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 풀스트레치 컨테이너(빈 RectTransform). 자식 좌표가 패널 기준 그대로 유지된다.
    private static Transform EnsureContainer(Transform parent, string name)
    {
        Transform t = FindDeep(parent, name);
        GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        go.SetActive(true);
        return go.transform;
    }

    private static void SetCentered(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = value;
    }

    // 이름으로 깊이 우선 탐색(자식 어디에 있든 찾는다 — 재실행 시 그룹 안에 들어가 있어도 OK).
    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
