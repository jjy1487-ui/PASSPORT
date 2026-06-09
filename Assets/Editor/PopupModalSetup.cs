using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

// ─────────────────────────────────────────────────────────────
//  PopupModalSetup — CoreRig 팝업 모달화 (1회 셋업, idempotent)
//  메뉴: Tools/Passport/Setup Popup Modal Backdrop
//  하는 일:
//   1) Canvas/Day1Root/Popups 에 풀스크린 투명 'ModalBackdrop'(RaycastTarget) 추가(없으면).
//   2) Popups 에 PopupModalBackdrop 컨트롤러 추가 + backdrop 참조 연결.
//   3) InventoryPanel(토글 팝업)을 기본 꺼짐으로.
//  → 팝업 열렸을 때 바깥 클릭이 뒤 서류로 새지 않게 막는다. 공유 프리팹이라 14일 전체 반영.
// ─────────────────────────────────────────────────────────────
public static class PopupModalSetup
{
    private const string PrefabPath = "Assets/Prefabs/CoreRig.prefab";

    [MenuItem("Tools/Passport/Setup Popup Modal Backdrop")]
    public static void Setup()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // 경로 대신 이름으로 탐색(계층 이름/위치가 바뀌어도 견딤).
            Transform popups = FindDeep(root.transform, "Popups");
            if (popups == null)
            {
                Debug.LogError("[PopupModalSetup] 'Popups' 못 찾음");
                return;
            }

            // 1) ModalBackdrop (풀스크린 투명 클릭 흡수판)
            Transform bdT = popups.Find("ModalBackdrop");
            GameObject backdrop;
            if (bdT == null)
            {
                backdrop = new GameObject("ModalBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                backdrop.transform.SetParent(popups, false);
            }
            else
            {
                backdrop = bdT.gameObject;
            }
            var rt = (RectTransform)backdrop.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = backdrop.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // 완전 투명(화면 변화 없음, 클릭만 흡수)
            img.raycastTarget = true;
            backdrop.transform.SetSiblingIndex(0); // 맨 아래 → 팝업 뒤
            backdrop.SetActive(false);             // 평소엔 꺼짐(컨트롤러가 켬)

            // 2) 컨트롤러 + 참조 연결
            var ctrl = popups.GetComponent<PopupModalBackdrop>();
            if (ctrl == null) ctrl = popups.gameObject.AddComponent<PopupModalBackdrop>();
            var so = new SerializedObject(ctrl);
            so.FindProperty("backdrop").objectReferenceValue = backdrop;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 3) InventoryPanel 기본 꺼짐(토글 팝업이 시작부터 데스크를 가리던 문제)
            Transform inv = popups.Find("InventoryPanel");
            if (inv != null) inv.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[PopupModalSetup] 완료: ModalBackdrop + 컨트롤러 설치, InventoryPanel 기본 꺼짐.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 이름으로 깊이 우선 탐색(자식 어디에 있든 찾는다 — 이름/위치가 바뀌어도 OK).
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
