using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 선택된 두 필드 각각의 위에 결과색 "테두리만"(채움 없음) 박스를 덧씌우고, 두 박스를 직각으로
/// 꺾이는 점선(orthogonal elbow + dash)으로 연결한다. 결과 글씨는 꺾은선 가운데의 작은 테두리
/// 박스 안에 표시한다(Papers Please의 "HIGHLIGHT DISCREPANCIES" 스타일).
/// <see cref="CrossCheckController"/>가 선택한 두 <see cref="RectTransform"/>과 결과 enum을 넘긴다.
/// 판정 로직 없음 — 받은 결과를 그대로 그린다. 새 손님/선택 취소 시 <see cref="Hide"/>로 정리.
/// </summary>
public sealed class CrossCheckConnectorView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private RectTransform _root;        // 연출 루트(박스+선+라벨의 부모). 카드 위로 최상단 정렬.
    [SerializeField] private RectTransform _boxA;         // 첫 번째 필드 위에 덧씌우는 테두리 박스.
    [SerializeField] private Image _boxAImage;            // 박스 A 색 적용용(채움 투명, 테두리만).
    [SerializeField] private RectTransform _boxB;         // 두 번째 필드 위에 덧씌우는 테두리 박스.
    [SerializeField] private Image _boxBImage;            // 박스 B 색 적용용.
    [SerializeField] private TMP_Text _resultLabel;       // 깜빡이는 결과 글씨(꺾은선 가운데).
    [SerializeField] private RectTransform _canvasRect;   // 좌표 변환 기준 캔버스 RectTransform

    [Header("박스")]
    [Tooltip("필드 rect 크기에 더해 줄 여백(px). 테두리가 필드를 감싸 보이도록 살짝 키운다.")]
    [SerializeField] private Vector2 _boxPadding = new Vector2(8f, 6f);
    [Tooltip("9-slice 테두리 스프라이트(미연결 시 Resources/UI/border_frame 자동 로드). 채움 없이 외곽선만.")]
    [SerializeField] private Sprite _borderSprite;

    [Header("꺾은선(점선)")]
    [Tooltip("점선 대시 한 칸의 길이(px).")]
    [SerializeField] private float _dashLength = 10f;
    [Tooltip("점선 대시 사이 간격(px).")]
    [SerializeField] private float _dashGap = 7f;
    [Tooltip("점선 두께(px).")]
    [SerializeField] private float _dashThickness = 3f;

    [Header("라벨 박스")]
    [Tooltip("결과 라벨을 감싸는 작은 테두리 박스의 안쪽 여백(px).")]
    [SerializeField] private Vector2 _labelBoxPadding = new Vector2(14f, 8f);
    [Tooltip("라벨 박스 배경색(불투명). 뒤의 점선을 가려 라벨이 칩(chip)처럼 깔끔히 분리되게 한다. 여권/패널 종이색 계열.")]
    [SerializeField] private Color _labelBoxFillColor = new Color(0.961f, 0.945f, 0.894f, 1f); // 종이색 #F5F1E4
    [Tooltip("라벨 박스 배경 알파(1=불투명). 뒤 점선을 확실히 가리려면 높게 유지.")]
    [SerializeField] private float _labelBoxFillAlpha = 1.0f;

    [Header("깜빡임")]
    [SerializeField] private float _blinkPeriod = 0.4f; // alpha 1↔min 토글 주기(초)
    [SerializeField] private float _blinkMinAlpha = 0.2f;

    [Header("색(UI-CONVENTIONS 2장 시맨틱)")]
    [SerializeField] private Color _matchColor = new Color(0.180f, 0.545f, 0.341f);    // 일치 #2E8B57
    [SerializeField] private Color _mismatchColor = new Color(0.753f, 0.227f, 0.169f);  // 불일치 #C0392B
    [SerializeField] private Color _unrelatedColor = new Color(0.478f, 0.510f, 0.549f); // 관련 없음 #7A828C
    [SerializeField] private Color _relatedColor = new Color(0.231f, 0.510f, 0.769f);   // 관련있음 #3B82C4 (같은 속성·값 비교 불가 / 규정 관련성)

    private Coroutine _blink;

    // 런타임 생성 점선 세그먼트 풀 + 라벨 박스.
    private readonly List<Image> _dashPool = new List<Image>();
    private RectTransform _linesRoot;     // 점선 세그먼트의 부모
    private RectTransform _labelBox;       // 결과 라벨을 감싸는 테두리 박스(라벨 칩 배경)
    private Image _labelBoxImage;          // 라벨 칩 배경(불투명 종이색, 채움 유지)
    private Image _labelBoxBorderImage;    // 라벨 칩 9-slice 테두리(결과색, 채움 없음)
    private Color _currentColor = Color.white;

    // 드래그 추종: 현재 연결 중인 두 대상·결과를 보관해 매 프레임 위치를 다시 계산한다.
    private RectTransform _targetA;
    private RectTransform _targetB;
    private CrossCheckResult _result;
    private string _overrideText;
    private float _blinkAlpha = 1f;

    private void Awake()
    {
        // 박스를 "9-slice 테두리만"으로 보이게 한다: 채움 없음(fillCenter=false), 테두리만 결과색.
        if (_borderSprite == null) _borderSprite = BorderFrame.Sprite;
        SetupBorderImage(_boxAImage);
        SetupBorderImage(_boxBImage);
        EnsureLinesRoot();
        EnsureLabelBox();
        Hide();
    }

    /// <summary>Image 를 채움 없는 9-slice 테두리로 설정한다(요청: 채움 사라지고 얇은 사각 테두리만).</summary>
    private void SetupBorderImage(Image img)
    {
        if (img == null) return;
        // 기존 Outline 컴포넌트가 있으면 비활성(4방향 오프셋 그림자 제거).
        Outline old = img.GetComponent<Outline>();
        if (old != null) old.enabled = false;

        if (_borderSprite != null)
        {
            img.sprite = _borderSprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.pixelsPerUnitMultiplier = 1f;
        }
    }

    private void EnsureLinesRoot()
    {
        if (_linesRoot != null || _root == null) return;
        var go = new GameObject("DashLines", typeof(RectTransform));
        _linesRoot = go.GetComponent<RectTransform>();
        _linesRoot.SetParent(_root, false);
        _linesRoot.anchorMin = _linesRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _linesRoot.pivot = new Vector2(0.5f, 0.5f);
        _linesRoot.anchoredPosition = Vector2.zero;
        _linesRoot.sizeDelta = Vector2.zero;
    }

    private void EnsureLabelBox()
    {
        if (_labelBox != null || _root == null) return;
        var go = new GameObject("LabelBox", typeof(RectTransform), typeof(Image));
        _labelBox = go.GetComponent<RectTransform>();
        _labelBox.SetParent(_root, false);
        _labelBox.anchorMin = _labelBox.anchorMax = new Vector2(0.5f, 0.5f);
        _labelBox.pivot = new Vector2(0.5f, 0.5f);
        _labelBoxImage = go.GetComponent<Image>();
        _labelBoxImage.raycastTarget = false;
        // 라벨 칩 배경은 불투명 종이색(채움 유지) — 뒤 점선을 가린다.

        // 9-slice 테두리는 별도 자식 Image 로(채움 없는 사각 테두리, 결과색). 배경 위에 겹친다.
        var borderGo = new GameObject("LabelBoxBorder", typeof(RectTransform), typeof(Image));
        var borderRt = borderGo.GetComponent<RectTransform>();
        borderRt.SetParent(_labelBox, false);
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = Vector2.zero;
        borderRt.offsetMax = Vector2.zero;
        _labelBoxBorderImage = borderGo.GetComponent<Image>();
        _labelBoxBorderImage.raycastTarget = false;
        SetupBorderImage(_labelBoxBorderImage);

        // 결과 라벨을 라벨 박스의 자식으로 옮겨 함께 움직이도록 한다.
        if (_resultLabel != null)
        {
            _resultLabel.rectTransform.SetParent(_labelBox, false);
            _resultLabel.rectTransform.anchorMin = _resultLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _resultLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _resultLabel.rectTransform.anchoredPosition = Vector2.zero;
            _resultLabel.alignment = TextAlignmentOptions.Center;
        }
    }

    private void OnDestroy()
    {
        StopBlink();
    }

    /// <summary>
    /// 선택된 두 필드 각각의 위에 결과색 테두리 박스를 덧씌우고, 두 박스를 직각 점선으로 잇고,
    /// 꺾은선 가운데의 작은 테두리 박스 안에 결과 글씨를 깜빡이며 표시한다.
    /// <paramref name="overrideText"/>가 있으면 enum 기본 문구 대신 그 텍스트를 표시한다(예: 날짜 대조).
    /// </summary>
    public void Show(RectTransform a, RectTransform b, CrossCheckResult result, string overrideText = null)
    {
        if (a == null || b == null) return;
        if (_root == null)
        {
            Debug.LogWarning("[CrossCheckConnectorView] _root 가 연결되지 않았습니다.");
            return;
        }

        EnsureLinesRoot();
        EnsureLabelBox();

        // 추종을 위해 대상/결과를 보관 → LateUpdate 가 매 프레임 '현재' 위치로 다시 배치한다(드래그 추종).
        _targetA = a; _targetB = b; _result = result; _overrideText = overrideText;

        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling(); // 카드 위로

        Relayout();
        StartBlink();
    }

    /// <summary>보관된 두 대상의 '현재' 위치로 박스·점선·라벨을 다시 배치한다(카드 드래그 추종).
    /// 색은 결과색으로 두고, 마지막에 현재 깜빡임 알파를 다시 적용한다(재배치가 알파를 덮어쓰지 않게).</summary>
    private void Relayout()
    {
        if (_targetA == null || _targetB == null || _root == null) return;

        Color c = ColorFor(_result);
        _currentColor = c;

        Vector2 centerA = PlaceBox(_boxA, _boxAImage, _targetA, c, out Vector2 sizeA);
        Vector2 centerB = PlaceBox(_boxB, _boxBImage, _targetB, c, out Vector2 sizeB);

        // 라벨은 가운데 세로 세그먼트 중앙에 온다. 점선을 그리기 전에 라벨 위치/크기를 먼저 확정해,
        // 라벨 박스가 덮는 영역의 대시를 건너뛴다(라벨이 점선과 겹쳐 지저분해 보이던 문제 해소).
        Vector2 midPoint = ComputeMidPoint(centerA, centerB);
        Rect labelRect = MeasureLabelRect(midPoint, _result, _overrideText);

        // 직각 꺾은선 경로(가로→세로→가로). 라벨 박스 rect 와 겹치는 대시는 스킵.
        BuildElbowDashes(centerA, sizeA, centerB, sizeB, c, labelRect);
        DrawLabel(midPoint, _result, c, _overrideText);

        SetGroupAlpha(_blinkAlpha);
    }

    /// <summary>매 프레임 대상 위치를 추종한다 — 드래그 중에도 박스·선·라벨이 카드를 따라간다.</summary>
    private void LateUpdate()
    {
        if (_root != null && _root.gameObject.activeSelf && _targetA != null && _targetB != null)
            Relayout();
    }

    /// <summary>연출을 숨기고 깜빡임을 멈춘다.</summary>
    public void Hide()
    {
        StopBlink();
        _targetA = null; _targetB = null; // 추종 중단
        HideAllDashes();
        if (_labelBox != null) _labelBox.gameObject.SetActive(false);
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── 좌표/그리기 ───────────────────────────────────────────────
    /// <summary>
    /// 대상 필드 rect 의 화면상 중심을 _root 로컬 좌표로, 크기는 rect 크기를 _root 스케일로 변환해 돌려준다.
    /// ScreenSpaceOverlay 대응(camera=null).
    /// </summary>
    private Vector2 WorldRectToLocal(RectTransform target, out Vector2 size)
    {
        Vector3 worldCenter = target.TransformPoint(target.rect.center);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);

        Vector3 lossy = target.lossyScale;
        Vector2 rootLossy = _root.lossyScale;
        float sx = rootLossy.x != 0f ? lossy.x / rootLossy.x : 1f;
        float sy = rootLossy.y != 0f ? lossy.y / rootLossy.y : 1f;
        size = new Vector2(target.rect.width * sx, target.rect.height * sy);
        return local;
    }

    /// <summary>한 박스를 대상 필드 rect 위치·크기에 맞춰 배치하고 색을 적용한다(채움 투명, 테두리만).</summary>
    private Vector2 PlaceBox(RectTransform box, Image img, RectTransform target, Color c, out Vector2 boxSize)
    {
        Vector2 center = WorldRectToLocal(target, out Vector2 size);
        boxSize = size + _boxPadding;
        if (box == null) return center;

        box.gameObject.SetActive(true);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.localRotation = Quaternion.identity;
        box.anchoredPosition = center;
        box.sizeDelta = boxSize;
        box.SetAsLastSibling();

        if (img != null)
        {
            // 9-slice 테두리만(fillCenter=false). 테두리 색 = 결과색(불투명). 채움 없음.
            if (img.sprite == null) SetupBorderImage(img);
            Color border = c; border.a = 1f;
            img.color = border;
        }
        return center;
    }

    /// <summary>라벨이 놓일 경로 중점(가운데 세로 세그먼트의 중앙).</summary>
    private static Vector2 ComputeMidPoint(Vector2 a, Vector2 b)
    {
        float midX = (a.x + b.x) * 0.5f;
        return new Vector2(midX, (a.y + b.y) * 0.5f);
    }

    /// <summary>
    /// 라벨 박스의 위치·크기를 그리기 전에 미리 측정해 Rect 로 돌려준다(대시 스킵 판정용).
    /// 실제 텍스트 렌더 크기 + 패딩으로 계산하며, 약간의 여유(margin)를 더해 점선이 박스에 닿지 않게 한다.
    /// </summary>
    private Rect MeasureLabelRect(Vector2 center, CrossCheckResult result, string overrideText)
    {
        if (_resultLabel == null) return new Rect(center, Vector2.zero);

        _resultLabel.text = string.IsNullOrEmpty(overrideText) ? TextFor(result) : overrideText;
        _resultLabel.ForceMeshUpdate();
        Vector2 textSize = _resultLabel.GetRenderedValues(false);
        Vector2 boxSize = textSize + _labelBoxPadding * 2f;

        // 점선이 박스 외곽선에 바짝 붙지 않도록 약간의 여유를 둔다.
        Vector2 margin = new Vector2(_dashGap, _dashGap);
        Vector2 full = boxSize + margin * 2f;
        return new Rect(center - full * 0.5f, full);
    }

    /// <summary>
    /// 두 박스를 직각(orthogonal)으로 꺾이는 점선으로 잇는다. 경로: A에서 수평으로 중간 x까지 →
    /// 수직으로 B의 y까지 → B로 수평. 곡선/대각선 금지. <paramref name="labelRect"/> 영역과 겹치는 대시는 그리지 않는다.
    /// </summary>
    private void BuildElbowDashes(Vector2 a, Vector2 sizeA, Vector2 b, Vector2 sizeB, Color c, Rect labelRect)
    {
        HideAllDashes();
        if (_linesRoot == null) return;
        _linesRoot.SetAsLastSibling();

        float midX = (a.x + b.x) * 0.5f;

        // 박스 가장자리에서 선이 시작/끝나도록 가로 방향으로 박스 절반만큼 물러난다.
        float dir = Mathf.Sign(midX - a.x);
        if (Mathf.Approximately(dir, 0f)) dir = 1f;
        Vector2 startA = new Vector2(a.x + dir * (sizeA.x * 0.5f), a.y);
        Vector2 endB = new Vector2(b.x - dir * (sizeB.x * 0.5f), b.y);

        Vector2 corner1 = new Vector2(midX, a.y); // 첫 꺾임점
        Vector2 corner2 = new Vector2(midX, b.y); // 둘째 꺾임점

        // 세 직각 세그먼트: 가로(A→corner1) / 세로(corner1→corner2) / 가로(corner2→B).
        DrawDashSegment(startA, corner1, c, labelRect);
        DrawDashSegment(corner1, corner2, c, labelRect);
        DrawDashSegment(corner2, endB, c, labelRect);
    }

    /// <summary>한 직선 세그먼트를 점선(대시 타일)으로 채운다. 가로/세로 모두 지원. labelRect 안에 드는 대시는 건너뛴다.</summary>
    private void DrawDashSegment(Vector2 from, Vector2 to, Color c, Rect labelRect)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.5f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float step = Mathf.Max(1f, _dashLength + _dashGap);
        int count = Mathf.Max(1, Mathf.CeilToInt(length / step));

        Vector2 unit = delta / length;
        for (int i = 0; i < count; i++)
        {
            float dist = i * step + _dashLength * 0.5f;
            if (dist > length) dist = length; // 마지막 대시가 끝을 넘지 않게.
            Vector2 pos = from + unit * dist;
            float thisLen = Mathf.Min(_dashLength, length - i * step);
            if (thisLen <= 0.5f) continue;

            // 라벨 박스(여유 포함)가 덮는 위치의 대시는 그리지 않는다 → 라벨이 점선과 겹쳐 보이지 않게.
            if (labelRect.width > 0f && labelRect.Contains(pos)) continue;

            Image dash = GetDash();
            RectTransform rt = dash.rectTransform;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(thisLen, _dashThickness);
            dash.color = c;
            dash.gameObject.SetActive(true);
        }
    }

    private Image GetDash()
    {
        foreach (Image d in _dashPool)
        {
            if (d != null && !d.gameObject.activeSelf) return d;
        }
        var go = new GameObject("Dash", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_linesRoot, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        _dashPool.Add(img);
        return img;
    }

    private void HideAllDashes()
    {
        foreach (Image d in _dashPool)
        {
            if (d != null) d.gameObject.SetActive(false);
        }
    }

    private void DrawLabel(Vector2 center, CrossCheckResult result, Color c, string overrideText = null)
    {
        if (_resultLabel == null || _labelBox == null) return;

        _resultLabel.gameObject.SetActive(true);
        _resultLabel.text = string.IsNullOrEmpty(overrideText) ? TextFor(result) : overrideText;
        _resultLabel.color = c;
        _resultLabel.ForceMeshUpdate();

        Vector2 textSize = _resultLabel.GetRenderedValues(false);
        Vector2 boxSize = textSize + _labelBoxPadding * 2f;

        _labelBox.gameObject.SetActive(true);
        _labelBox.sizeDelta = boxSize;
        _labelBox.anchoredPosition = center;
        _labelBox.SetAsLastSibling();

        if (_labelBoxImage != null)
        {
            // 결과색이 아닌 불투명 종이색 배경으로 뒤의 점선을 가린다(칩처럼 분리). 테두리만 결과색.
            Color fill = _labelBoxFillColor; fill.a = Mathf.Clamp01(_labelBoxFillAlpha);
            _labelBoxImage.color = fill;
        }
        if (_labelBoxBorderImage != null)
        {
            if (_labelBoxBorderImage.sprite == null) SetupBorderImage(_labelBoxBorderImage);
            Color border = c; border.a = 1f;
            _labelBoxBorderImage.color = border;
        }

        // 라벨 텍스트는 박스 중앙.
        _resultLabel.rectTransform.anchoredPosition = Vector2.zero;
        _resultLabel.rectTransform.SetAsLastSibling();
    }

    // ── 깜빡임 ────────────────────────────────────────────────────
    private void StartBlink()
    {
        StopBlink();
        if (isActiveAndEnabled) _blink = StartCoroutine(BlinkRoutine());
    }

    private void StopBlink()
    {
        if (_blink != null)
        {
            StopCoroutine(_blink);
            _blink = null;
        }
        _blinkAlpha = 1f;
        SetGroupAlpha(1f);
    }

    private IEnumerator BlinkRoutine()
    {
        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float phase = Mathf.PingPong(t / Mathf.Max(0.01f, _blinkPeriod), 1f);
            // 알파만 갱신 — 실제 적용은 Relayout(LateUpdate)에서 위치 재계산 직후 한다(드래그 추종과 충돌 방지).
            _blinkAlpha = Mathf.Lerp(_blinkMinAlpha, 1f, phase);
            yield return null;
        }
    }

    private void SetGroupAlpha(float a)
    {
        // 9-slice 테두리(박스/라벨 박스)·점선·라벨 글씨를 함께 깜빡인다. 채움은 없음(테두리만).
        SetImageAlpha(_boxAImage, a);
        SetImageAlpha(_boxBImage, a);
        SetImageAlpha(_labelBoxBorderImage, a);

        foreach (Image d in _dashPool)
        {
            if (d != null && d.gameObject.activeSelf) SetImageAlpha(d, a);
        }

        // 라벨 박스 배경(종이색)은 깜빡이지 않고 항상 불투명 유지 → 뒤 점선을 계속 가린다.
        // 테두리(_labelBoxOutline)와 글씨(_resultLabel)만 깜빡인다.
        if (_resultLabel != null)
        {
            Color c = _resultLabel.color;
            c.a = a;
            _resultLabel.color = c;
        }
    }

    private static void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // ── 매핑 ──────────────────────────────────────────────────────
    // 일치(초록)/불일치(빨강)/관련있음(파랑, 같은 속성이나 값 비교 불가·규정 관련성)/비교 불가(회색, 관련 없음).
    private Color ColorFor(CrossCheckResult r) => r switch
    {
        CrossCheckResult.Match => _matchColor,
        CrossCheckResult.Mismatch => _mismatchColor,
        CrossCheckResult.Related => _relatedColor, // 관련있음(파랑)
        _ => _unrelatedColor, // Unrelated → 회색(비교 불가)
    };

    private static string TextFor(CrossCheckResult r) => r switch
    {
        CrossCheckResult.Match => "일치",
        CrossCheckResult.Mismatch => "불일치",
        CrossCheckResult.Related => "관련있음", // 같은 속성이지만 값 비교 불가 / 규정이 그 항목에 관련됨
        _ => "비교 불가", // Unrelated → "비교 불가"
    };
}
