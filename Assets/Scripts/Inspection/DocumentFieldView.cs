using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 카드 안의 필드 1행(라벨 + 값). 클릭하면 자신을 대조 컨트롤러에 통지한다.
/// 선택 하이라이트 on/off를 가지며, 자신의 (documentType, label, value, key)를 보유한다.
/// 판정 로직은 없다 — 표시와 클릭 통지만 한다.
///
/// 이종 소스 대조를 위해 <see cref="ICrossCheckSelectable"/>을 구현한다(SourceType="서류",
/// AttributeKey=field.key, Value=value, DisplayLabel=label).
/// </summary>
public sealed class DocumentFieldView : MonoBehaviour, ICrossCheckSelectable
{
    [Header("UI 참조")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private TMP_Text _valueText;
    [SerializeField] private Button _button;
    [SerializeField] private Image _highlight; // 선택 표시(비활성 시작) — 채움 없이 테두리만으로 표시

    [Header("색(UITheme 부재 시 기본값)")]
    [SerializeField] private Color _selectedColor = new Color(0.231f, 0.510f, 0.769f, 1f); // Score/Blue 톤(테두리 색)

    [Header("선택 테두리")]
    [Tooltip("9-slice 테두리 스프라이트(미연결 시 Resources/UI/border_frame 자동 로드). 채움 없이 외곽선만.")]
    [SerializeField] private Sprite _borderSprite;

    [Header("슬롯 정체성(프리팹에 미리 배치된 슬롯용)")]
    [Tooltip("이 슬롯이 담당하는 field.key(예: name/birth_date/face). 프리팹에 미리 배치한 슬롯에서 사용.\n" +
             "빈 key 로 Bind 하면 이 값으로 폴백한다(슬롯이 자기 정체성을 보유).")]
    [SerializeField] private string _fieldKey = string.Empty;

    /// <summary>프리팹에 author 된 슬롯 키(빈 key Bind 시 폴백/검증용). 미리 배치 슬롯 식별에 사용.</summary>
    public string SlotKey => _fieldKey;

    /// <summary>이 필드가 속한 서류 종류(예: "여권").</summary>
    public string DocumentType { get; private set; }

    /// <summary>필드 라벨(표시명).</summary>
    public string Label { get; private set; }

    /// <summary>필드 값.</summary>
    public string Value { get; private set; }

    // ── ICrossCheckSelectable ────────────────────────────────────
    public string SourceType => "서류";
    public string AttributeKey { get; private set; } = string.Empty;
    public string UnlocksScan => string.Empty; // 서류 필드는 트리거 아님(손님 소스)
    public string DisplayLabel => Label;
    public RectTransform Rect => transform as RectTransform;
    public event Action<ICrossCheckSelectable> OnSelected;

    /// <summary>클릭 시 자신을 인자로 발행(레거시 호환 — 신규는 <see cref="OnSelected"/> 사용).</summary>
    public event Action<DocumentFieldView> OnFieldClicked;

    private void Awake()
    {
        if (_button != null) _button.onClick.AddListener(HandleClick);
        EnsureBorderSprite();
        SetSelected(false);
    }

    /// <summary>
    /// 선택 하이라이트 Image 를 9-slice 테두리(채움 없음)로 설정한다.
    /// 반투명 채움이 필드 값(예: 1900-01-01)을 가리던 문제를 해소한다. 색 시맨틱은 _selectedColor 로 유지.
    /// </summary>
    private void EnsureBorderSprite()
    {
        if (_highlight == null) return;
        if (_borderSprite == null) _borderSprite = BorderFrame.Sprite;
        if (_borderSprite != null)
        {
            _highlight.sprite = _borderSprite;
            _highlight.type = Image.Type.Sliced;
            _highlight.fillCenter = false; // 중앙 채움 없음 → 얇은 사각 테두리만
            _highlight.pixelsPerUnitMultiplier = 1f;
        }
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>필드 1개를 채운다(레거시: key 없는 호출은 빈 키).</summary>
    public void Bind(string documentType, string label, string value)
        => Bind(documentType, label, value, string.Empty);

    /// <summary>필드 1개를 채운다(속성 키 포함). 표시 글자 = 비교 값과 동일.</summary>
    public void Bind(string documentType, string label, string value, string key)
        => Bind(documentType, label, value, key, value);

    /// <summary>
    /// 필드 1개를 채운다(표시 글자와 대조 비교 값을 분리).
    /// 여권 템플릿처럼 화면에는 형식화된 값(예: "한국", "1995.03.21", "남")을 보이되,
    /// 교차 대조 비교는 원본 데이터(예: "KOR", "1995-03-21", "남성")로 하기 위함.
    /// <paramref name="value"/>=대조 비교 값(원본), <paramref name="displayValue"/>=화면 표시 글자.
    /// 표시·형식화 전용 — 판정/비교 기준은 바뀌지 않는다.
    /// </summary>
    public void Bind(string documentType, string label, string value, string key, string displayValue)
    {
        DocumentType = documentType;
        Label = label;
        Value = value ?? string.Empty;           // 대조 비교 값(원본 유지)

        // 데이터 key 가 비어 있으면 프리팹에 author 된 슬롯 키(_fieldKey)로 폴백한다.
        //  미리 배치된 슬롯이 자기 정체성을 갖고 있으므로 호출자가 key 를 안 줘도 대조가 작동.
        if (string.IsNullOrEmpty(key)) key = _fieldKey;
#if UNITY_EDITOR
        else if (!string.IsNullOrEmpty(_fieldKey) && key != _fieldKey)
            Debug.LogWarning($"[DocumentFieldView] 슬롯 key 불일치: prefab='{_fieldKey}' data='{key}' (type={documentType}). 프리팹 슬롯 key 를 확인하세요.");
#endif
        AttributeKey = key ?? string.Empty;

        if (_labelText != null) _labelText.text = label;
        if (_valueText != null) _valueText.text = displayValue ?? value ?? string.Empty; // 표시는 형식화 값
        SetSelected(false);
    }

    /// <summary>
    /// 슬롯을 미사용 상태로 비운다(이전 손님 값 잔류 방지). 프리팹에 미리 배치한 슬롯을
    /// 손님마다 재사용할 때, 이번 손님에게 없는 필드 슬롯이 직전 값을 들고 잘못 대조되는 것을 막는다.
    /// AttributeKey 는 슬롯 고유 key(_fieldKey)로 되돌린다. 표시 텍스트도 비운다.
    /// </summary>
    public void ClearForReuse()
    {
        Value = string.Empty;
        AttributeKey = _fieldKey ?? string.Empty;
        if (_valueText != null) _valueText.text = string.Empty;
        SetSelected(false);
    }

    /// <summary>선택 하이라이트를 켜고 끈다(9-slice 테두리만, 채움 없음 — 필드 값 텍스트를 가리지 않음).</summary>
    public void SetSelected(bool selected)
    {
        if (_highlight == null) return;

        _highlight.gameObject.SetActive(selected);

        if (_borderSprite == null) EnsureBorderSprite();

        // 9-slice 테두리만(fillCenter=false). 테두리 색만 _selectedColor(불투명).
        Color border = _selectedColor; border.a = 1f;
        _highlight.color = border;
    }

    private void HandleClick()
    {
        OnFieldClicked?.Invoke(this);
        OnSelected?.Invoke(this);
    }
}
