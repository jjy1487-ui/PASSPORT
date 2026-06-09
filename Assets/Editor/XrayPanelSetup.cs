using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

// ─────────────────────────────────────────────────────────────
//  XrayPanelSetup — 메뉴: Tools/Passport/Setup Xray Panel  (idempotent, 이름 탐색)
//  XrayPanel(기존 ScanResultPanel kind=Xray) → 전신 X-ray 전용 패널로 전환:
//   • ScanResultPanel 제거 → XrayInspectionPanel 추가
//   • Skeleton(전신 X-ray 이미지) + Highlight(은닉부위 글로우) + InfoBar(하단 박힌텍스트 가림)
//   • Header/Result/ScanId 텍스트(기존 Title/ResultText/ExtraText 재사용·재배치) + Contraband 대조항목
//   • DraggablePanel + CrossCheckController._extraProviders 재배선
//  ※ 중첩 프리팹 제약으로 기존 자식은 reparent 안 함(위치만). 세부 레이아웃은 에디터에서 수동 튜닝.
// ─────────────────────────────────────────────────────────────
public static class XrayPanelSetup
{
    private const string PrefabPath = "Assets/Prefabs/CoreRig.prefab";
    private const string SkeletonSpritePath = "Assets/Images/UI/XraySkeleton.png";

    [MenuItem("Tools/Passport/Setup Xray Panel")]
    public static void Setup()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform xp = FindDeep(root.transform, "XrayPanel");
            if (xp == null) { Debug.LogError("[XraySetup] XrayPanel 못 찾음"); return; }

            // 적발물 항목 템플릿(기존 CrossCheckItemView)
            CrossCheckItemView contraband = xp.GetComponentInChildren<CrossCheckItemView>(true);

            // 패널: 세로형(스켈레톤 816:1304 ≈ 0.626)
            var prt = (RectTransform)xp;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(440f, 703f);
            prt.anchoredPosition = Vector2.zero;
            var panelImg = xp.GetComponent<Image>();
            if (panelImg != null) { panelImg.raycastTarget = true; panelImg.color = new Color(0.04f, 0.06f, 0.09f, 1f); }

            // ScanResultPanel 제거 → XrayInspectionPanel
            var oldSrp = xp.GetComponent<ScanResultPanel>();
            if (oldSrp != null) Object.DestroyImmediate(oldSrp);
            var panel = xp.GetComponent<XrayInspectionPanel>();
            if (panel == null) panel = xp.gameObject.AddComponent<XrayInspectionPanel>();

            // Skeleton (전신 X-ray, 패널 가득)
            GameObject skel = EnsureChild(xp, "Skeleton", typeof(CanvasRenderer), typeof(Image));
            var skrt = (RectTransform)skel.transform;
            skrt.anchorMin = Vector2.zero; skrt.anchorMax = Vector2.one;
            skrt.offsetMin = Vector2.zero; skrt.offsetMax = Vector2.zero;
            skrt.pivot = new Vector2(0.5f, 0.5f);
            var skImg = skel.GetComponent<Image>();
            skImg.raycastTarget = false; skImg.preserveAspect = true;
            var skSpr = AssetDatabase.LoadAssetAtPath<Sprite>(SkeletonSpritePath);
            if (skSpr != null) { skImg.sprite = skSpr; skImg.color = Color.white; }
            else Debug.LogWarning($"[XraySetup] 스켈레톤 스프라이트 없음 → '{SkeletonSpritePath}'");
            skel.transform.SetAsFirstSibling(); // 맨 뒤(배경)

            // Highlight (스켈레톤 자식, 빨간 글로우 원) — 기본 복부
            GameObject hl = EnsureChild(skel.transform, "Highlight", typeof(CanvasRenderer), typeof(Image));
            var hlrt = (RectTransform)hl.transform;
            hlrt.anchorMin = new Vector2(0.5f, 0.5f); hlrt.anchorMax = new Vector2(0.5f, 0.5f); hlrt.pivot = new Vector2(0.5f, 0.5f);
            hlrt.sizeDelta = new Vector2(120f, 120f);
            hlrt.anchoredPosition = Vector2.zero;
            var hlImg = hl.GetComponent<Image>();
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (knob != null) hlImg.sprite = knob;
            hlImg.color = new Color(1f, 0.18f, 0.18f, 0.5f); // 반투명 빨강 글로우
            hlImg.raycastTarget = false;
            hl.SetActive(false);

            // ContrabandImage (스켈레톤 자식, 적발물 그림 — 글로우 위에 표시)
            GameObject ci = EnsureChild(skel.transform, "ContrabandImage", typeof(CanvasRenderer), typeof(Image));
            var cirt = (RectTransform)ci.transform;
            cirt.anchorMin = new Vector2(0.5f, 0.5f); cirt.anchorMax = new Vector2(0.5f, 0.5f); cirt.pivot = new Vector2(0.5f, 0.5f);
            cirt.sizeDelta = new Vector2(115f, 115f);
            cirt.anchoredPosition = Vector2.zero;
            var ciImg = ci.GetComponent<Image>();
            ciImg.preserveAspect = true; ciImg.raycastTarget = false;
            ci.transform.SetAsLastSibling(); // Highlight 위(글로우 위에 그림)
            ci.SetActive(false);

            // InfoBar (하단 어두운 띠 — 박힌 텍스트 가림). Skeleton 다음(텍스트 아래)
            GameObject bar = EnsureChild(xp, "InfoBar", typeof(CanvasRenderer), typeof(Image));
            var barrt = (RectTransform)bar.transform;
            barrt.anchorMin = new Vector2(0f, 0f); barrt.anchorMax = new Vector2(1f, 0f); barrt.pivot = new Vector2(0.5f, 0f);
            barrt.offsetMin = new Vector2(0f, 0f); barrt.offsetMax = new Vector2(0f, 0f);
            barrt.sizeDelta = new Vector2(0f, 96f);
            var barImg = bar.GetComponent<Image>();
            barImg.color = new Color(0.03f, 0.05f, 0.08f, 0.92f); barImg.raycastTarget = false;
            bar.transform.SetSiblingIndex(1); // 스켈레톤(0) 위, 텍스트 아래

            // 텍스트들(기존 재사용·재배치, reparent 안 함). 모두 InfoBar 위에 그려지도록 맨 앞으로.
            var header = ConfigText(FindDeep(xp, "Title"), "FULL BODY X-RAY",
                22, TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(20f, 64f), new Vector2(300f, 30f));
            var result = ConfigText(FindDeep(xp, "ResultText"), "RESULT: [ ... ]",
                20, TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(20f, 30f), new Vector2(400f, 30f));
            var scanId = ConfigText(FindDeep(xp, "ExtraText"), "SCAN ID: ###-PX",
                16, TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-20f, 30f), new Vector2(200f, 26f));
            Transform detail = FindDeep(xp, "DetailText");
            if (detail != null) detail.gameObject.SetActive(false);

            // Contraband 대조 항목(적발물) — InfoBar 위쪽에
            if (contraband != null)
            {
                Transform cont = contraband.transform;
                // ClaimSelectable 컨테이너가 부모면 그걸 재배치(자식 Item이 contraband)
                Transform target = (cont.parent != null && cont.parent != xp) ? cont.parent : cont;
                var crt = (RectTransform)target;
                crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(0.5f, 0f); crt.pivot = new Vector2(0.5f, 0f);
                crt.anchoredPosition = new Vector2(0f, 110f);
                crt.sizeDelta = new Vector2(380f, 42f);
                contraband.gameObject.SetActive(false);
            }

            // 텍스트/버튼/적발물을 맨 앞으로(InfoBar 위에)
            foreach (var nm in new[] { "Title", "ResultText", "ExtraText", "ClaimSelectable", "ContrabandItem", "CloseButton" })
            {
                Transform t = FindDeep(xp, nm);
                if (t != null && t.parent == xp) t.SetAsLastSibling();
            }

            // DraggablePanel
            if (xp.GetComponent<DraggablePanel>() == null) xp.gameObject.AddComponent<DraggablePanel>();

            // 컨트롤러 참조
            var ic = root.GetComponentInChildren<InspectionController>(true);
            var cc = root.GetComponentInChildren<CrossCheckController>(true);

            // XrayInspectionPanel 필드 연결
            var so = new SerializedObject(panel);
            SetRef(so, "_root", xp.gameObject);
            SetRef(so, "_skeleton", skImg);
            SetRef(so, "_headerText", header);
            SetRef(so, "_resultText", result);
            SetRef(so, "_scanIdText", scanId);
            SetRef(so, "_closeButton", GetComp<Button>(FindDeep(xp, "CloseButton")));
            SetRef(so, "_highlight", hlrt);
            SetRef(so, "_contrabandImage", ciImg);
            SetRef(so, "_spriteDrug", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/UI/XrayContraband_Drug.png"));
            SetRef(so, "_spriteSmuggle", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/UI/XrayContraband_Gold.png"));
            // _spriteExplosive: 폭발물 이미지 아직 없음(나중에 연결) → 글로우만 표시
            SetRef(so, "_contrabandItem", contraband);
            SetRef(so, "_controller", ic);
            SetRef(so, "_crossCheck", cc);
            so.ApplyModifiedPropertiesWithoutUndo();

            // CrossCheckController._extraProviders 재배선: 파괴된 옛 xray ScanResultPanel(null 슬롯) → XrayInspectionPanel
            if (cc != null)
            {
                var cso = new SerializedObject(cc);
                var arr = cso.FindProperty("_extraProviders");
                if (arr != null)
                {
                    bool has = false;
                    for (int i = 0; i < arr.arraySize; i++)
                        if (arr.GetArrayElementAtIndex(i).objectReferenceValue == panel) has = true;
                    if (!has)
                    {
                        int nullSlot = -1;
                        for (int i = 0; i < arr.arraySize; i++)
                            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == null) { nullSlot = i; break; }
                        if (nullSlot >= 0) arr.GetArrayElementAtIndex(nullSlot).objectReferenceValue = panel;
                        else { arr.arraySize++; arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = panel; }
                    }
                    cso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[XraySetup] 완료: XrayInspectionPanel + Skeleton + Highlight + InfoBar + 적발물 항목 + 재배선");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static GameObject EnsureChild(Transform parent, string name, params System.Type[] comps)
    {
        Transform t = null;
        for (int i = 0; i < parent.childCount; i++) if (parent.GetChild(i).name == name) { t = parent.GetChild(i); break; }
        GameObject go = t != null ? t.gameObject : null;
        if (go == null)
        {
            var all = new System.Collections.Generic.List<System.Type> { typeof(RectTransform) };
            all.AddRange(comps);
            go = new GameObject(name, all.ToArray());
            go.transform.SetParent(parent, false);
        }
        return go;
    }

    private static TMP_Text ConfigText(Transform t, string text, int size, TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 sz)
    {
        if (t == null) return null;
        var rt = (RectTransform)t;
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.anchoredPosition = pos; rt.sizeDelta = sz;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align;
            tmp.enableAutoSizing = false; tmp.color = new Color(0.8f, 0.95f, 1f, 1f);
            tmp.gameObject.SetActive(true);
        }
        return tmp;
    }

    private static T GetComp<T>(Transform t) where T : Component => t != null ? t.GetComponent<T>() : null;
    private static void SetRef(SerializedObject so, string prop, Object val) { var p = so.FindProperty(prop); if (p != null) p.objectReferenceValue = val; }

    private static Transform FindDeep(Transform r, string name)
    {
        if (r.name == name) return r;
        for (int i = 0; i < r.childCount; i++) { var x = FindDeep(r.GetChild(i), name); if (x != null) return x; }
        return null;
    }
}
