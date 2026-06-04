using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보조검사(X-ray/지문) 결과 패널. 현재 손님의 <see cref="ScanData"/>(없으면 null) 하나를 한글로 표시한다.
/// 결과의 <see cref="Claim"/>을 클릭 가능한 대조 항목(CrossCheckItemView)으로 1개 노출한다(ICrossCheckProvider).
/// 검사기 버튼이 Toggle 로 여닫고, 손님이 바뀌면 컨트롤러 이벤트로 갱신한다.
///
/// 판정/점수/변조 로직 없음 — 표시·대조 단서 노출 전용. 정답 판정은 gameplay 소유.
/// </summary>
public sealed class ScanResultPanel : MonoBehaviour, ICrossCheckProvider
{
    /// <summary>이 패널이 표시할 검사 종류.</summary>
    public enum ScanKind { Xray, Fingerprint }

    [Header("대상")]
    [SerializeField] private InspectionController _controller;
    [SerializeField] private CrossCheckController _crossCheck; // 대조로 잠금 해제되면 자동 표시
    [SerializeField] private ScanKind _kind = ScanKind.Xray;

    [Header("UI 참조")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private TMP_Text _detailText;
    [SerializeField] private TMP_Text _extraText;
    [SerializeField] private Button _closeButton;

    [Header("교차 대조 단서")]
    [SerializeField] private CrossCheckItemView _claimSelectable; // 검사 결과의 claim(비활성 시작)

    /// <summary>selectable 구성 변경 통지.</summary>
    public event System.Action OnSelectablesChanged;

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_root != null) _root.SetActive(false);
    }

    private void Start()
    {
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[ScanResultPanel] _controller 가 연결되지 않았습니다.");

        // 대조로 내 검사 종류가 잠금 해제되면 결과를 자동으로 연다(버튼 없이도 동작).
        if (_crossCheck != null) _crossCheck.OnScanUnlocked += HandleScanUnlocked;
    }

    /// <summary>대조로 내 검사 종류가 잠금 해제되면 결과 패널을 자동으로 연다.</summary>
    private void HandleScanUnlocked(string scanKind)
    {
        if (string.IsNullOrEmpty(scanKind)) return;
        if (!string.Equals(scanKind, ScanKindKey, System.StringComparison.OrdinalIgnoreCase)) return;
        Open(); // 데이터 없으면 Open 내부 가드가 무시
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
        if (_crossCheck != null) _crossCheck.OnScanUnlocked -= HandleScanUnlocked;
    }

    /// <summary>현재 손님이 이 검사 데이터를 가지고 있는가(버튼 활성 판단용).</summary>
    public bool HasData => CurrentScan != null;

    /// <summary>이 패널의 검사 종류(잠금 해제 트리거 매칭용). "xray"|"fingerprint".</summary>
    public string ScanKindKey => _kind == ScanKind.Xray ? "xray" : "fingerprint";

    // JsonUtility 는 null 중첩 객체를 빈 인스턴스로 역직렬화하므로(필드가 모두 "" 인 ScanData),
    // 내용이 비어 있으면 "데이터 없음(null)"으로 취급한다.
    private ScanData CurrentScan
    {
        get
        {
            if (_controller == null) return null;
            ScanData s = _kind == ScanKind.Xray ? _controller.CurrentXray : _controller.CurrentFingerprint;
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

    /// <summary>검사 결과 패널을 토글한다. 데이터가 없으면 열지 않는다.</summary>
    public void Toggle()
    {
        if (_root == null) return;
        if (_root.activeSelf) Close();
        else Open();
    }

    /// <summary>검사 결과 패널을 연다(데이터 없으면 무시).</summary>
    public void Open()
    {
        if (CurrentScan == null)
        {
            Debug.LogWarning("[ScanResultPanel] 현재 손님에게 해당 검사 데이터가 없습니다.");
            return;
        }
        if (_root != null) _root.SetActive(true);
        Render();
    }

    /// <summary>패널을 닫는다.</summary>
    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    // 손님이 바뀌면: 패널이 열려 있으면 갱신, 데이터가 사라졌으면 닫는다.
    private void HandleCustomerChanged()
    {
        if (_root != null && _root.activeSelf)
        {
            if (CurrentScan == null) Close();
            else Render();
        }
        else
        {
            OnSelectablesChanged?.Invoke();
        }
    }

    private void Render()
    {
        ScanData s = CurrentScan;
        if (s == null) { Close(); return; }

        bool isXray = _kind == ScanKind.Xray;
        if (_titleText != null) _titleText.text = isXray ? "X-ray 검사" : "지문 대조";
        if (_resultText != null) _resultText.text = $"결과: {Safe(s.result)}";
        if (_detailText != null)
            _detailText.text = isXray ? $"적발물: {Safe(s.detail)}" : $"대조 상태: {Safe(s.detail)}";
        if (_extraText != null)
            _extraText.text = isXray ? $"은닉 위치: {Safe(s.extra)}" : $"일치 인물: {Safe(s.extra)}";

        BuildClaim(s);
    }

    private static string Safe(string v) => string.IsNullOrEmpty(v) ? "-" : v;

    // ── 교차 대조 단서 ───────────────────────────────────────────
    private void BuildClaim(ScanData s)
    {
        if (_claimSelectable == null)
        {
            OnSelectablesChanged?.Invoke();
            return;
        }

        Claim c = s != null ? s.claim : null;
        bool hasClaim = c != null && !string.IsNullOrEmpty(c.attr);
        _claimSelectable.gameObject.SetActive(hasClaim);
        if (hasClaim)
        {
            string src = _kind == ScanKind.Xray ? "X-ray" : "지문";
            _claimSelectable.Bind(src, c.attr, c.value, c.label, c.label);
        }

        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_root == null || !_root.activeInHierarchy) yield break;
        if (_claimSelectable != null && _claimSelectable.gameObject.activeInHierarchy)
            yield return _claimSelectable;
    }
}
