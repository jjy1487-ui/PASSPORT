using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 전신(신체) X-ray 검사 패널 — 지문판독기(<see cref="ScanResultPanel"/>)와 **별개의 전용 구현**.
///
/// 흐름(뉴스 단서 ↔ 손님 정보 일치 → <see cref="CrossCheckController.OnScanUnlocked"/>("xray") 발행 → 이 패널이 열림):
///   ① SCANNING  : 전신 스켈레톤 + "SCANNING..." 잠깐
///   ② RESULT    : 스켈레톤 + "적발: {물품} @ {부위}" + 은닉 부위 하이라이트 + SCAN ID
/// 적발물(<see cref="ScanData.claim"/>, attr="contraband")은 클릭 가능한 교차대조 항목으로 노출(<see cref="ICrossCheckProvider"/>).
/// 판정은 시스템이 하지 않는다 — 플레이어가 적발물을 보고 [거절] 버튼으로 직접 판정한다.
///
/// 데이터(<see cref="ScanData"/>)는 지문과 공유하되 표시 컴포넌트만 분리: xray는 result/detail(적발물)/extra(은닉 위치)/claim(contraband).
/// </summary>
public sealed class XrayInspectionPanel : MonoBehaviour, ICrossCheckProvider
{
    [Header("연결")]
    [SerializeField] private InspectionController _controller;   // 현재 손님 X-ray 데이터 제공
    [SerializeField] private CrossCheckController _crossCheck;   // 잠금 해제 트리거 수신

    [Header("UI 참조")]
    [SerializeField] private GameObject _root;          // 패널 루트(보통 자기 자신)
    [SerializeField] private Image _skeleton;           // 전신 스켈레톤 X-ray 이미지(상시 배경)
    [SerializeField] private TMP_Text _headerText;      // "FULL BODY X-RAY"
    [SerializeField] private TMP_Text _resultText;      // "RESULT: [ 적발: 마약 @ 복부 ]" / "[ NO ABNORMALITIES ]"
    [SerializeField] private TMP_Text _scanIdText;      // "SCAN ID: ###-PX"
    [SerializeField] private Button _closeButton;
    [Tooltip("은닉 부위 하이라이트(빨간 원/글로우, 적발물 그림 뒤 배경). 스켈레톤 자식, anchoredPosition 으로 부위 이동.")]
    [SerializeField] private RectTransform _highlight;
    [Tooltip("부위에 표시되는 적발물 그림(마약/금괴 등). detail 로 스프라이트 선택. 폭발물처럼 그림 없으면 글로우만.")]
    [SerializeField] private Image _contrabandImage;
    [SerializeField] private Sprite _spriteDrug;       // 마약
    [SerializeField] private Sprite _spriteSmuggle;    // 밀수품(금괴)
    [SerializeField] private Sprite _spriteExplosive;  // 폭발물(없으면 글로우만)
    [Tooltip("적발물 교차대조 항목(attr=contraband). 결과 단계에만 노출.")]
    [SerializeField] private CrossCheckItemView _contrabandItem;

    [Header("2단계 레이어 (선택)")]
    [Tooltip("스캔 중 레이어(있으면). 없으면 텍스트만 전환.")]
    [SerializeField] private GameObject _scanningGroup;
    [Tooltip("결과 레이어(있으면).")]
    [SerializeField] private GameObject _resultGroup;
    [SerializeField] private float _scanSeconds = 1.2f;

    [Header("은닉 부위 좌표 (스켈레톤 기준 anchoredPosition, 에디터에서 미세조정)")]
    [SerializeField] private Vector2 _posChest = new Vector2(0f, 120f);  // 가슴
    [SerializeField] private Vector2 _posBelly = new Vector2(0f, 0f);    // 복부
    [SerializeField] private Vector2 _posLeg   = new Vector2(0f, -260f); // 다리

    /// <summary>selectable 구성 변경 통지(ICrossCheckProvider).</summary>
    public event System.Action OnSelectablesChanged;

    private ScanData _injected;  // 자립 재생용 주입 데이터(UIPreview)
    private Coroutine _seq;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        SetStage(0);
        if (_root != null) _root.SetActive(false);
    }

    private void Start()
    {
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[XrayInspectionPanel] _controller 미연결");
        if (_crossCheck != null) _crossCheck.OnScanUnlocked += HandleScanUnlocked;
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
        if (_crossCheck != null) _crossCheck.OnScanUnlocked -= HandleScanUnlocked;
    }

    /// <summary>잠금 해제 트리거 종류 매칭용.</summary>
    public string ScanKindKey => "xray";
    public bool HasData => CurrentScan != null;

    // JsonUtility 가 null 중첩객체를 빈 인스턴스로 만드므로, 내용 비면 "없음"으로 취급.
    private ScanData CurrentScan
    {
        get
        {
            if (_injected != null) return IsEmpty(_injected) ? null : _injected;
            if (_controller == null) return null;
            ScanData s = _controller.CurrentXray;
            return IsEmpty(s) ? null : s;
        }
    }

    private static bool IsEmpty(ScanData s) =>
        s == null
        || (string.IsNullOrEmpty(s.type)
            && string.IsNullOrEmpty(s.result)
            && string.IsNullOrEmpty(s.detail)
            && string.IsNullOrEmpty(s.extra)
            && (s.claim == null || string.IsNullOrEmpty(s.claim.attr)));

    /// <summary>대조로 X-ray 가 잠금 해제되면 자동으로 연다.</summary>
    private void HandleScanUnlocked(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return;
        if (!string.Equals(kind, "xray", System.StringComparison.OrdinalIgnoreCase)) return;
        Open();
    }

    public void Toggle()
    {
        if (_root == null) return;
        if (_root.activeSelf) Close(); else Open();
    }

    /// <summary>X-ray 검사를 연다(데이터 없으면 무시). 스캔 연출부터 시작.</summary>
    public void Open()
    {
        if (CurrentScan == null) { Debug.LogWarning("[XrayInspectionPanel] 현재 손님에게 X-ray 데이터 없음"); return; }
        ShowRoot();
        StartSequence();
    }

    /// <summary>자립 재생(UIPreview): ScanData 주입 후 스캔 연출 재생.</summary>
    public void PreviewPlay(ScanData data)
    {
        _injected = data;
        ShowRoot();
        StartSequence();
    }

    private void ShowRoot()
    {
        if (_root == null) return;
        _root.SetActive(true);
        if (!_root.activeSelf) _root.SetActive(true);
    }

    public void Close()
    {
        if (_seq != null) { StopCoroutine(_seq); _seq = null; }
        SetStage(0);
        HideContraband();
        if (_highlight != null) _highlight.gameObject.SetActive(false);
        if (_contrabandImage != null) _contrabandImage.gameObject.SetActive(false);
        _injected = null;
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    private void HandleCustomerChanged()
    {
        if (_root != null && _root.activeSelf)
        {
            if (CurrentScan == null) Close();
            else RenderResult(CurrentScan); // 갱신은 즉시
        }
        else OnSelectablesChanged?.Invoke();
    }

    // ── 단계 ────────────────────────────────────────────────────
    private void SetStage(int stage)
    {
        if (_scanningGroup != null) _scanningGroup.SetActive(stage == 1);
        if (_resultGroup != null) _resultGroup.SetActive(stage == 2);
    }

    private void StartSequence()
    {
        if (_seq != null) StopCoroutine(_seq);
        _seq = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        ScanData s = CurrentScan;
        if (s == null) { Close(); yield break; }

        HideContraband();
        if (_highlight != null) _highlight.gameObject.SetActive(false);
        if (_contrabandImage != null) _contrabandImage.gameObject.SetActive(false);

        // ① 스캔 중
        SetStage(1);
        if (_headerText != null) _headerText.text = "FULL BODY X-RAY";
        if (_resultText != null) _resultText.text = "RESULT: [ SCANNING... ]";
        if (_scanIdText != null) _scanIdText.text = "SCAN ID: " + MakeScanId(s);
        yield return new WaitForSeconds(_scanSeconds);

        // ② 결과
        RenderResult(s);
        _seq = null;
    }

    private void RenderResult(ScanData s)
    {
        SetStage(2);
        if (_headerText != null) _headerText.text = "FULL BODY X-RAY";
        if (_scanIdText != null) _scanIdText.text = "SCAN ID: " + MakeScanId(s);

        bool detected = s != null && !string.IsNullOrEmpty(s.detail);
        if (_resultText != null)
            _resultText.text = detected
                ? $"RESULT: [ 적발: {Safe(s.detail)} @ {Safe(s.extra)} ]"
                : "RESULT: [ NO ABNORMALITIES ]";

        // 은닉 부위 하이라이트(은닉 위치 텍스트 → 스켈레톤 좌표)
        Vector2 loc = detected ? LocFor(s.extra) : Vector2.zero;
        if (_highlight != null)
        {
            _highlight.gameObject.SetActive(detected);
            if (detected) _highlight.anchoredPosition = loc;
        }

        // 적발물 그림을 같은 부위에 표시(마약/금괴 등). 그림 없으면(폭발물) 글로우만.
        if (_contrabandImage != null)
        {
            Sprite icon = detected ? SpriteFor(s.detail) : null;
            _contrabandImage.gameObject.SetActive(icon != null);
            if (icon != null)
            {
                _contrabandImage.sprite = icon;
                ((RectTransform)_contrabandImage.transform).anchoredPosition = loc;
            }
        }

        BuildContraband(s);
    }

    /// <summary>적발물 이름(detail) → 표시 스프라이트 매핑.</summary>
    private Sprite SpriteFor(string detail)
    {
        if (string.IsNullOrEmpty(detail)) return null;
        if (detail.Contains("마약")) return _spriteDrug;
        if (detail.Contains("밀수") || detail.Contains("금괴") || detail.Contains("녹용")) return _spriteSmuggle;
        if (detail.Contains("폭발")) return _spriteExplosive;
        return null;
    }

    /// <summary>은닉 위치 텍스트를 스켈레톤 좌표로 매핑(가슴/복부/다리).</summary>
    private Vector2 LocFor(string loc)
    {
        if (string.IsNullOrEmpty(loc)) return _posChest;
        if (loc.Contains("복부") || loc.Contains("배")) return _posBelly;
        if (loc.Contains("다리") || loc.Contains("발") || loc.Contains("허벅")) return _posLeg;
        if (loc.Contains("가슴") || loc.Contains("흉")) return _posChest;
        return _posChest;
    }

    /// <summary>SCAN ID 연출 문자열(데이터에 없으면 적발물 기반 결정론적 의사난수).</summary>
    private static string MakeScanId(ScanData s)
    {
        int n = 137;
        if (s != null && !string.IsNullOrEmpty(s.detail)) foreach (char c in s.detail) n = n * 31 + c;
        if (s != null && !string.IsNullOrEmpty(s.extra)) foreach (char c in s.extra) n = n * 31 + c;
        return (System.Math.Abs(n) % 900 + 100) + "-PX";
    }

    // ── 적발물 교차대조 항목 ─────────────────────────────────────
    private void BuildContraband(ScanData s)
    {
        if (_contrabandItem == null) { OnSelectablesChanged?.Invoke(); return; }
        Claim c = s != null ? s.claim : null;
        bool has = c != null && !string.IsNullOrEmpty(c.attr);
        _contrabandItem.gameObject.SetActive(has);
        if (has) _contrabandItem.Bind("X-ray", c.attr, c.value, c.label, c.label);
        OnSelectablesChanged?.Invoke();
    }

    private void HideContraband()
    {
        if (_contrabandItem != null) _contrabandItem.gameObject.SetActive(false);
    }

    private static string Safe(string v) => string.IsNullOrEmpty(v) ? "-" : v;

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        if (_contrabandItem != null && _contrabandItem.gameObject.activeInHierarchy)
            yield return _contrabandItem;
    }
}
