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

    [Header("적발물 그림 크기 (폭발물만 크게)")]
    [Tooltip("마약·밀수품 등 기본 적발물 그림 크기.")]
    [SerializeField] private Vector2 _contrabandBaseSize = new Vector2(115f, 115f);
    [Tooltip("폭발물 적발물 그림 크기 — 이것만 크게.")]
    [SerializeField] private Vector2 _contrabandExplosiveSize = new Vector2(280f, 280f);
    [Tooltip("은닉 부위 하이라이트(글로우) 크기 — 품목 무관 고정(이전 크기).")]
    [SerializeField] private Vector2 _highlightSize = new Vector2(120f, 120f);

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

    // 적발물 항목(_contrabandItem)의 원래 배치(그림 위로 겹치기 전). 그림 없는 케이스에서 복원용.
    private Transform _itemHomeParent;
    private Vector2 _itemHomeAnchorMin, _itemHomeAnchorMax, _itemHomePivot, _itemHomeAnchoredPos, _itemHomeSizeDelta;
    private bool _itemHomeCached;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        CacheItemHome();

        // 구독은 반드시 Awake 에서(아래 _root.SetActive(false) 보다 먼저) 한다.
        //  _root 가 자기 자신이면 SetActive(false) 로 이 GameObject 가 비활성화되어 Start 가 실행되지 않는다.
        //  구독을 Start 에 두면 OnScanUnlocked(대조 잠금해제) 트리거를 영영 받지 못해 X-ray 가 자동으로 안 열린다.
        //  이벤트 구독은 GameObject 가 비활성이어도 유지되고, HandleScanUnlocked→Open→ShowRoot 가 다시 활성화한다.
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[XrayInspectionPanel] _controller 미연결");
        if (_crossCheck != null) _crossCheck.OnScanUnlocked += HandleScanUnlocked;

        SetStage(0);
        if (_root != null) _root.SetActive(false);
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
            if (detected) { _highlight.anchoredPosition = loc; _highlight.sizeDelta = _highlightSize; }
        }

        // 적발물 그림을 같은 부위에 표시(마약/금괴/폭발물). 이 그림이 곧 교차대조 클릭 항목이다.
        bool hasIcon = false;
        if (_contrabandImage != null)
        {
            Sprite icon = detected ? SpriteFor(s.detail) : null;
            hasIcon = icon != null;
            _contrabandImage.gameObject.SetActive(hasIcon);
            if (hasIcon)
            {
                _contrabandImage.sprite = icon;
                RectTransform irt = (RectTransform)_contrabandImage.transform;
                irt.anchoredPosition = loc;
                // 폭발물만 크게, 마약·밀수품 등은 기본 크기(공유 이미지라 매번 설정해 재사용 시 잔류 방지).
                bool isExplosive = !string.IsNullOrEmpty(s.detail) && s.detail.Contains("폭발");
                irt.sizeDelta = isExplosive ? _contrabandExplosiveSize : _contrabandBaseSize;
            }
        }

        // 적발물 그림 위에 대조 클릭 항목을 겹쳐 둔다 → 플레이어가 그림을 클릭해 대조한다.
        AlignItemToImage(hasIcon);
        BuildContraband(s);
    }

    /// <summary>적발물 항목의 원래 배치(부모/앵커/크기)를 1회 캐시한다(그림 위 겹치기 전 상태).</summary>
    private void CacheItemHome()
    {
        if (_itemHomeCached || _contrabandItem == null) return;
        RectTransform rt = _contrabandItem.transform as RectTransform;
        if (rt == null) return;
        _itemHomeParent     = rt.parent;
        _itemHomeAnchorMin  = rt.anchorMin;
        _itemHomeAnchorMax  = rt.anchorMax;
        _itemHomePivot      = rt.pivot;
        _itemHomeAnchoredPos= rt.anchoredPosition;
        _itemHomeSizeDelta  = rt.sizeDelta;
        _itemHomeCached     = true;
    }

    /// <summary>
    /// 적발물 교차대조 항목(<see cref="_contrabandItem"/>)을 적발물 그림(<see cref="_contrabandImage"/>) 위에 정확히 겹쳐 둔다.
    /// 그림의 자식으로 stretch 시키므로, 그림이 은닉 부위로 이동하거나 크기가 커져도 클릭 영역이 항상 그림과 일치한다.
    /// 그림이 없으면(글로우만) 항목을 원래 자리(하단)로 되돌려 글씨로 대조하게 한다.
    /// </summary>
    private void AlignItemToImage(bool overlayOnImage)
    {
        if (_contrabandItem == null || _contrabandImage == null) return;
        RectTransform itemRt = _contrabandItem.transform as RectTransform;
        if (itemRt == null) return;

        if (overlayOnImage)
        {
            RectTransform imgRt = _contrabandImage.transform as RectTransform;
            if (itemRt.parent != imgRt) itemRt.SetParent(imgRt, false);
            itemRt.anchorMin = Vector2.zero;
            itemRt.anchorMax = Vector2.one;
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.offsetMin = Vector2.zero;
            itemRt.offsetMax = Vector2.zero;
            itemRt.localScale = Vector3.one;
        }
        else if (_itemHomeCached && _itemHomeParent != null && itemRt.parent != _itemHomeParent)
        {
            // 그림이 없는 케이스: 원래 하단 자리로 복원(글씨 대조 폴백).
            itemRt.SetParent(_itemHomeParent, false);
            itemRt.anchorMin = _itemHomeAnchorMin;
            itemRt.anchorMax = _itemHomeAnchorMax;
            itemRt.pivot = _itemHomePivot;
            itemRt.anchoredPosition = _itemHomeAnchoredPos;
            itemRt.sizeDelta = _itemHomeSizeDelta;
            itemRt.localScale = Vector3.one;
        }
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
        if (has)
        {
            // 항목이 적발물 그림 위에 겹쳐져 있으면(그림이 곧 클릭 대상) 글씨 라벨은 비운다.
            // 그림이 없어 항목이 하단에 글씨로 남는 경우(폴백)에만 라벨을 표시한다.
            bool overlayedOnImage = _contrabandImage != null
                && _contrabandItem.transform.parent == _contrabandImage.transform;
            string display = overlayedOnImage ? " " : c.label;
            _contrabandItem.Bind("X-ray", c.attr, c.value, c.label, display);
        }
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
