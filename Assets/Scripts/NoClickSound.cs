using UnityEngine;

/// <summary>
/// 이 컴포넌트가 붙은 버튼(또는 그 조상)은 전역 클릭 사운드(<see cref="ClickSound"/>)를 내지 않는다.
/// 예: 도장 버튼은 '도장 찍는 소리'만 내고 일반 클릭음은 제외한다.
/// </summary>
public sealed class NoClickSound : MonoBehaviour { }
