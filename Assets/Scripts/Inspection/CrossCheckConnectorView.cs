using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 두 필드 행을 점선으로 잇고, 선 중점에 결과 글씨를 깜빡이며 표시하는 인플레이스 연출.
/// <see cref="CrossCheckController"/>가 선택한 두 <see cref="RectTransform"/>과 결과 enum을 넘긴다.
/// 판정 로직 없음 — 받은 결과를 그대로 그린다. 새 손님/선택 취소 시 <see cref="Hide"/>로 정리.
/// </summary>
public sealed class CrossCheckConnectorView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private RectTransform _root;        // 연출 루트(점선+라벨의 부모). 카드 위로 최상단 정렬.
    [SerializeField] private RectTransform _dashLine;     // 점선 Image(타일). 두 점 사이에 배치/회전.
    [SerializeField] private Image _dashImage;            // 점선 색 적용용(타일 스프라이트)
    [SerializeField] private TMP_Text _resultLabel;       // 깜빡이는 결과 글씨
    [SerializeField] private RectTransform _canvasRect;   // 좌표 변환 기준 캔버스 RectTransform

    [Header("점선")]
    [SerializeField] private float _lineThickness = 3f;

    [Header("깜빡임")]
    [SerializeField] private float _blinkPeriod = 0.4f; // alpha 1↔0.2 토글 주기(초)
    [SerializeField] private float _blinkMinAlpha = 0.2f;

    [Header("색(UI-CONVENTIONS 2장 시맨틱)")]
    [SerializeField] private Color _matchColor = new Color(0.180f, 0.545f, 0.341f);    // 일치 #2E8B57
    [SerializeField] private Color _mismatchColor = new Color(0.753f, 0.227f, 0.169f);  // 불일치 #C0392B
    [SerializeField] private Color _unrelatedColor = new Color(0.478f, 0.510f, 0.549f); // 관련 없음 #7A828C
    [SerializeField] private Color _relatedColor = new Color(0.231f, 0.510f, 0.769f);   // 관련 있음(값 확인 불가) #3B82C4

    private Coroutine _blink;

    private void Awake()
    {
        Hide();
    }

    private void OnDestroy()
    {
        StopBlink();
    }

    /// <summary>
    /// 두 필드 행을 점선으로 잇고 결과 글씨를 깜빡이며 표시한다.
    /// <paramref name="overrideText"/>가 있으면 enum 기본 문구 대신 그 텍스트를 표시한다
    /// (예: 날짜 대조 "만료됨"/"유효"). 색은 result enum 기준 그대로.
    /// </summary>
    public void Show(RectTransform a, RectTransform b, CrossCheckResult result, string overrideText = null)
    {
        if (a == null || b == null) return;
        if (_root == null)
        {
            Debug.LogWarning("[CrossCheckConnectorView] _root 가 연결되지 않았습니다.");
            return;
        }

        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling(); // 카드 위로

        Color c = ColorFor(result);

        Vector2 ca = WorldCenterToLocal(a);
        Vector2 cb = WorldCenterToLocal(b);

        DrawDashLine(ca, cb, c);
        DrawLabel((ca + cb) * 0.5f, result, c, overrideText);

        StartBlink();
    }

    /// <summary>연출을 숨기고 깜빡임을 멈춘다.</summary>
    public void Hide()
    {
        StopBlink();
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── 좌표/그리기 ───────────────────────────────────────────────
    /// <summary>대상 행의 화면상 중심을 _root 로컬 좌표로 변환한다(ScreenSpaceOverlay 대응).</summary>
    private Vector2 WorldCenterToLocal(RectTransform target)
    {
        Vector3 worldCenter = target.TransformPoint(target.rect.center);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
        return local;
    }

    private void DrawDashLine(Vector2 from, Vector2 to, Color c)
    {
        if (_dashLine == null) return;

        Vector2 dir = to - from;
        float dist = dir.magnitude;

        _dashLine.gameObject.SetActive(true);
        _dashLine.sizeDelta = new Vector2(dist, _lineThickness);
        _dashLine.anchoredPosition = (from + to) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _dashLine.localRotation = Quaternion.Euler(0f, 0f, angle);

        if (_dashImage != null)
        {
            _dashImage.color = c;
            // 타일 점선: 거리에 맞춰 가로로 반복(스프라이트 type=Tiled 가정).
            if (_dashImage.type == Image.Type.Tiled && _dashImage.sprite != null)
            {
                _dashImage.pixelsPerUnitMultiplier = 1f;
            }
        }
    }

    private void DrawLabel(Vector2 center, CrossCheckResult result, Color c, string overrideText = null)
    {
        if (_resultLabel == null) return;

        _resultLabel.gameObject.SetActive(true);
        _resultLabel.text = string.IsNullOrEmpty(overrideText) ? TextFor(result) : overrideText;
        _resultLabel.color = c;

        RectTransform rt = _resultLabel.rectTransform;
        rt.anchoredPosition = center + new Vector2(0f, 18f); // 선 위쪽으로 살짝
        rt.SetAsLastSibling();
    }

    // ── 깜빡임 ────────────────────────────────────────────────────
    private void StartBlink()
    {
        StopBlink();
        if (isActiveAndEnabled && _resultLabel != null) _blink = StartCoroutine(BlinkRoutine());
    }

    private void StopBlink()
    {
        if (_blink != null)
        {
            StopCoroutine(_blink);
            _blink = null;
        }
        if (_resultLabel != null) SetLabelAlpha(1f);
    }

    private IEnumerator BlinkRoutine()
    {
        float t = 0f;
        while (true)
        {
            // 0..period 구간을 삼각파로: 1 → min → 1 반복
            t += Time.unscaledDeltaTime;
            float phase = Mathf.PingPong(t / Mathf.Max(0.01f, _blinkPeriod), 1f);
            float alpha = Mathf.Lerp(_blinkMinAlpha, 1f, phase);
            SetLabelAlpha(alpha);
            yield return null;
        }
    }

    private void SetLabelAlpha(float a)
    {
        Color c = _resultLabel.color;
        c.a = a;
        _resultLabel.color = c;
    }

    // ── 매핑 ──────────────────────────────────────────────────────
    private Color ColorFor(CrossCheckResult r) => r switch
    {
        CrossCheckResult.Match => _matchColor,
        CrossCheckResult.Mismatch => _mismatchColor,
        CrossCheckResult.Related => _relatedColor,
        _ => _unrelatedColor,
    };

    private static string TextFor(CrossCheckResult r) => r switch
    {
        CrossCheckResult.Match => "일치",
        CrossCheckResult.Mismatch => "불일치",
        CrossCheckResult.Related => "관련 있음",
        _ => "관련 없음",
    };
}
