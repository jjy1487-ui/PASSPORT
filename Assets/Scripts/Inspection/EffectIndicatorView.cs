using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// 상점 효과 상시 인디케이터(HUD). 구매한 영구 활성 효과(MAGNIFY/UV_LIGHT/AUTO_HIGHLIGHT/DB_UPDATE)와
/// 1회성 보유 수량(TIME_BONUS/MISTAKE_FORGIVE/XRAY_SCAN)을 작게 나열한다.
///
/// 일부 효과는 기존 검사 데스크에 연결할 메커니즘이 아직 없어(예: 심사 타이머 부재 → TIME_BONUS),
/// 여기서 '활성/보유 플래그' 표시까지만 담당한다(요구 3: 연결 불가 효과는 플래그 표시).
///
/// 표시만 한다 — 효과 보유/소비 판정은 ShopService 가 소유(규약 5장).
/// ShopService.OnEffectsChanged 구독, OnDestroy 해제.
/// </summary>
public sealed class EffectIndicatorView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;       // 효과 0개면 숨김
    [SerializeField] private TMP_Text _effectsText;  // 보유 효과 줄나열(작게)

    private ShopService Shop => ShopService.Instance;

    // (effect_type, 한글 표시명) — shop 시트 effect_type 과 1:1. 표시 전용.
    private static readonly (string fx, string label)[] PermanentEffects =
    {
        (ShopService.FxMagnify,       "확대경"),
        (ShopService.FxUvLight,       "자외선 램프"),
        (ShopService.FxAutoHighlight, "규정 자동 강조"),
        (ShopService.FxDbUpdate,      "지문 DB 갱신"),
    };

    private static readonly (string fx, string label)[] ConsumableEffects =
    {
        (ShopService.FxXrayScan,       "정밀 스캔권"),
        (ShopService.FxTimeBonus,      "추가 검사시간"),
        (ShopService.FxMistakeForgive, "커피(오판 무효)"),
    };

    private void Start()
    {
        if (Shop != null) Shop.OnEffectsChanged += HandleEffectsChanged;
        else Debug.LogWarning("[EffectIndicatorView] ShopService.Instance 가 없습니다. 효과 표시 비활성.");
        Refresh();
    }

    private void OnDestroy()
    {
        if (Shop != null) Shop.OnEffectsChanged -= HandleEffectsChanged;
    }

    private void HandleEffectsChanged() => Refresh();

    private void Refresh()
    {
        if (Shop == null)
        {
            if (_root != null) _root.SetActive(false);
            return;
        }

        var sb = new StringBuilder();
        int shown = 0;

        foreach (var (fx, label) in PermanentEffects)
        {
            if (Shop.IsEffectActive(fx)) { sb.Append("· ").AppendLine(label); shown++; }
        }
        foreach (var (fx, label) in ConsumableEffects)
        {
            int n = Shop.GetConsumableCount(fx);
            if (n > 0) { sb.Append("· ").Append(label).Append(" x").Append(n).AppendLine(); shown++; }
        }

        if (_root != null) _root.SetActive(shown > 0);
        if (_effectsText != null) _effectsText.text = sb.ToString().TrimEnd();
    }
}
