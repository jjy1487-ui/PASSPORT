using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 외 소스(캐릭터/뉴스/대화/규정)에서 클릭 가능한 대조 항목 1개를 표시하는 범용 위젯.
/// <see cref="ICrossCheckSelectable"/> 구현 — 라벨(한글) 표시, 속성 키/값으로 대조.
/// 판정 로직 없음 — 표시·클릭 통지 전용.
/// </summary>
public sealed class CrossCheckItemView : MonoBehaviour, ICrossCheckSelectable
{
    [Header("UI 참조")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private Button _button;
    [SerializeField] private Image _highlight; // 선택 표시(비활성 시작) — 채움 없이 테두리만으로 표시

    [Header("색(UITheme 부재 시 기본값)")]
    [SerializeField] private Color _selectedColor = new Color(0.231f, 0.510f, 0.769f, 1f); // Score/Blue 톤(테두리 색)

    [Header("선택 테두리")]
    [Tooltip("9-slice 테두리 스프라이트(미연결 시 Resources/UI/border_frame 자동 로드). 채움 없이 외곽선만.")]
    [SerializeField] private Sprite _borderSprite;

    public string SourceType { get; private set; } = string.Empty;
    public string AttributeKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string UnlocksScan { get; private set; } = string.Empty;
    /// <summary>규정 항목 전용: 이 규정이 '다루는' 항목 키들(쉼표 구분). 비규정 항목은 빈 문자열. 대조 시 "관련있음" 판정에 사용.</summary>
    public string CoveredAttrs { get; private set; } = string.Empty;
    public string DisplayLabel { get; private set; } = string.Empty;
    public RectTransform Rect => transform as RectTransform;
    public event Action<ICrossCheckSelectable> OnSelected;

    private void Awake()
    {
        if (_button != null)
        {
            _button.onClick.AddListener(HandleClick);
            StretchToFill(_button.transform as RectTransform); // 클릭영역(Hit)을 항목 전체로 — 어긋난 pivot/offset 보정
        }
        EnsureBorderSprite();
        SetSelected(false);
    }

    /// <summary>RectTransform 을 부모 전체로 펼친다(클릭영역이 항목과 어긋나지 않게).</summary>
    private static void StretchToFill(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>선택 하이라이트 Image 를 9-slice 테두리(채움 없음)로 설정한다(텍스트 가림 방지). 색 시맨틱은 _selectedColor 유지.</summary>
    private void EnsureBorderSprite()
    {
        if (_highlight == null) return;
        if (_borderSprite == null) _borderSprite = BorderFrame.Sprite;
        if (_borderSprite != null)
        {
            _highlight.sprite = _borderSprite;
            _highlight.type = Image.Type.Sliced;
            _highlight.fillCenter = false;
            _highlight.pixelsPerUnitMultiplier = 1f;
        }
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>항목을 채운다. displayText 가 없으면 label 을 그대로 보여준다. unlocksScan 은 스캔 잠금 해제 트리거 단서일 때만 채운다.</summary>
    public void Bind(string sourceType, string attributeKey, string value, string label, string displayText = null, string unlocksScan = null, string coveredAttrs = null)
    {
        SourceType = sourceType ?? string.Empty;
        AttributeKey = attributeKey ?? string.Empty;
        Value = value ?? string.Empty;
        UnlocksScan = unlocksScan ?? string.Empty;
        CoveredAttrs = coveredAttrs ?? string.Empty;
        DisplayLabel = label ?? string.Empty;

        if (_labelText != null) _labelText.text = string.IsNullOrEmpty(displayText) ? DisplayLabel : displayText;
        SetSelected(false);
    }

    public void SetSelected(bool on)
    {
        if (_highlight == null) return;

        _highlight.gameObject.SetActive(on);

        if (_borderSprite == null) EnsureBorderSprite();

        // 9-slice 테두리만(fillCenter=false). 테두리 색만 _selectedColor(불투명). 항목 텍스트를 가리지 않는다.
        Color border = _selectedColor; border.a = 1f;
        _highlight.color = border;
    }

    private void HandleClick()
    {
        Debug.Log($"[CrossCheckDBG] HandleClick fired src={SourceType} attr={AttributeKey} listeners={(OnSelected != null)}");
        OnSelected?.Invoke(this);
    }
}
