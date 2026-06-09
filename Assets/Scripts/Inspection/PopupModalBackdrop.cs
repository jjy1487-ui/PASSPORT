using UnityEngine;

/// <summary>
/// 팝업 모달 처리. "Popups" 그룹(풀스크린)에 붙인다.
/// 자식 팝업(작은 박스)이 하나라도 켜지면 풀스크린 <see cref="backdrop"/>(클릭 흡수판)을 켜서,
/// 팝업 바깥을 클릭해도 뒤(서류/데스크)가 잡히지 않게 막는다. 모두 닫히면 backdrop을 끈다.
///
/// 왜 이렇게:
///  - 팝업 루트는 가운데 작은 박스라, 팝업 안에 backdrop을 두면 화면 전체를 못 덮는다.
///    그래서 풀스크린인 Popups 그룹에 backdrop 한 장을 두고 이 스크립트가 켜고 끈다.
///  - backdrop은 항상 첫 자식(맨 아래)으로 둬서 팝업들보다 뒤에 그려진다(팝업 내용은 정상 클릭).
/// </summary>
[DisallowMultipleComponent]
public sealed class PopupModalBackdrop : MonoBehaviour
{
    [Tooltip("팝업 뒤를 막는 풀스크린 판(투명 Image, RaycastTarget=on). Popups 그룹의 자식.")]
    [SerializeField] private GameObject backdrop;

    private void LateUpdate()
    {
        if (backdrop == null) return;

        bool anyOpen = false;
        int count = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child == backdrop) continue;
            if (child.activeSelf) { anyOpen = true; break; }
        }

        if (backdrop.activeSelf != anyOpen)
            backdrop.SetActive(anyOpen);

        // backdrop은 항상 맨 아래(첫 자식) → 팝업 내용이 그 위에 그려지고 클릭도 팝업이 우선.
        if (anyOpen && backdrop.transform.GetSiblingIndex() != 0)
            backdrop.transform.SetSiblingIndex(0);
    }
}
