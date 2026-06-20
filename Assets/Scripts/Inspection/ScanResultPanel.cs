using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보조검사(X-ray/지문) 결과 패널. 현재 손님의 <see cref="ScanData"/>(없으면 null) 하나를 한글로 표시한다.
/// 결과의 <see cref="Claim"/>을 클릭 가능한 대조 항목(CrossCheckItemView)으로 1개 노출한다(ICrossCheckProvider).
/// 검사기 버튼이 Toggle 로 여닫고, 손님이 바뀌면 컨트롤러 이벤트로 갱신한다.
///
/// 3단계 연출(지문): <see cref="_useSequence"/> 가 켜지면 Open 시 곧장 결과를 띄우지 않고
///   ①지문 스캔 중 → ②DB 조회 중 → ③대조 결과(일치/불일치) 순서로 보여준다.
/// 자립 재생: <see cref="PreviewPlay"/> 로 ScanData 를 직접 주입하면 게임 없이도(UIPreview) 단독 재생된다.
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
    [Tooltip("지문 전용: DB 조회 결과 → 지문 대조 결과로 수동 진행하는 버튼. X-ray는 비워둠.")]
    [SerializeField] private Button _nextButton;
    [Tooltip("지문 스캐너 기기 이미지(GameObject). 스캔 중 단계에서 표시, DB 결과 단계/닫힘에선 숨김. 비우면 미사용.")]
    [SerializeField] private GameObject _scannerImage;

    [Header("팝업 레이어 (2단계 분리)")]
    [Tooltip("팝업1 = 스캔 중 레이어(스캐너 이미지 + 안내문). 스캔 단계에만 켜짐. 비우면 개별 요소로 폴백.")]
    [SerializeField] private GameObject _stage1Group;
    [Tooltip("팝업2 = DB 조회 결과 레이어(이름/생년월일/국적 행 + 범죄기록). 결과 단계에만 켜짐.")]
    [SerializeField] private GameObject _stage2Group;
    [Tooltip("팝업1 레이어 안의 '스캔 중' 안내문. 비우면 _resultText 로 폴백.")]
    [SerializeField] private TMP_Text _scanText;

    [Header("교차 대조 단서")]
    [SerializeField] private CrossCheckItemView _claimSelectable; // X-ray claim 1개(비활성 시작)
    [Tooltip("지문 DB 결과의 대조 행들(이름/생년월일/국적). 플레이어가 여권·서류와 직접 비교.")]
    [SerializeField] private CrossCheckItemView[] _dbRows;

    [Header("3단계 연출 (지문용)")]
    [Tooltip("켜면 Open 시 스캔중→DB조회→결과 순으로 단계 연출 후 결과를 보여준다.")]
    [SerializeField] private bool _useSequence = false;
    [Tooltip("각 단계(스캔중/DB조회) 표시 시간(초).")]
    [SerializeField] private float _stageSeconds = 1.1f;
    [Tooltip("수배자 지문(윤서린): DB 결과를 보여준 뒤 자동으로 닫고 적발 대사를 띄우기까지의 시간(초). 대조 불필요.")]
    [SerializeField] private float _wantedAutoRevealSeconds = 2.5f;
    [SerializeField] private Color _stageColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Color _matchColor = new Color(0.88f, 0.22f, 0.22f);   // 일치=수배=빨강
    [SerializeField] private Color _noMatchColor = new Color(0.20f, 0.70f, 0.35f); // 불일치=정상=초록

    /// <summary>selectable 구성 변경 통지.</summary>
    public event System.Action OnSelectablesChanged;

    private ScanData _injected;   // 자립 재생용 주입 데이터(있으면 컨트롤러 대신 사용)
    private Coroutine _seq;       // 진행 중 단계 연출 코루틴
    private Coroutine _autoReveal; // 수배자 지문: N초 후 자동 닫고 적발 대사 재생하는 코루틴
    private bool _dbShown;        // 현재 손님에 대해 DB 조회 결과(③단계)를 이미 표시했는가

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_nextButton != null) _nextButton.gameObject.SetActive(false); // 자동판정 폐지로 미사용(숨김)

        // ⚠ 구독은 반드시 Awake 에서(아래 _root.SetActive(false) 전에) 한다.
        //   _root 가 이 패널 자신이면 SetActive(false) 로 자기 자신이 꺼져 Start 가 호출되지 않는다
        //   (X-ray 패널과 동일한 함정). 그러면 OnScanUnlocked 를 구독하지 못해, 대조로 지문이 잠금
        //   해제돼도 패널이 열리지 않는다. C# 이벤트 구독은 GameObject 비활성과 무관하게 유지되므로,
        //   여기서 구독해 두면 비활성 상태에서도 HandleScanUnlocked 가 호출돼 Open() 으로 다시 켜진다.
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[ScanResultPanel] _controller 가 연결되지 않았습니다.");
        if (_crossCheck != null) _crossCheck.OnScanUnlocked += HandleScanUnlocked;

        SetStage(0);                                                      // 두 레이어 모두 끔(스캔 시작 시 1로 켬)
        if (_root != null) _root.SetActive(false);
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
            if (_injected != null) return IsEmpty(_injected) ? null : _injected;  // 자립 재생 우선
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

    /// <summary>검사 결과 패널을 연다(데이터 없으면 무시). _useSequence 면 3단계 연출.</summary>
    public void Open()
    {
        if (CurrentScan == null)
        {
            Debug.LogWarning("[ScanResultPanel] 현재 손님에게 해당 검사 데이터가 없습니다.");
            return;
        }
        ShowRoot();

        // 같은 손님에 대해 이미 한 번 연 상태에서 다시 Open 이 들어오면(잠금 해제 트리거가
        // 두 구독자에서 두 번 호출되거나, 손님이 이름/사진을 다시 대조해 재트리거하는 경우)
        // 스캔 연출을 처음부터 재생하지 않는다. 이미 DB 결과를 보여줬다면 곧장 DB 화면으로,
        // 아직 스캔 중이면 진행 중 코루틴을 끊지 않고 그대로 둔다(③단계가 사라지는 버그 방지).
        if (_useSequence)
        {
            if (_dbShown) { RenderDbLookup(CurrentScan); return; }  // 이미 DB 화면 → 그대로 다시 보여줌
            if (_seq != null) return;                              // 스캔 연출 진행 중 → 재시작 금지
            StartSequence();
        }
        else
        {
            Render();
        }
    }

    /// <summary>자립 재생: ScanData 를 주입해 3단계 연출을 처음부터 재생한다(UIPreview 단독 테스트).</summary>
    public void PreviewPlay(ScanData data)
    {
        _injected = data;
        ShowRoot();
        StartSequence();
    }

    // _root 가 패널 자신일 때, 첫 활성화로 Awake 가 실행되며 _root 를 다시 끄는 자기모순을 흡수한다.
    // (첫 SetActive(true) 가 Awake 를 부르고 Awake 가 self 를 끄면, 한 번 더 켜서 확정 — Awake 는 재실행 안 됨)
    private void ShowRoot()
    {
        if (_root == null) return;
        _root.SetActive(true);
        if (!_root.activeSelf) _root.SetActive(true);
        // 대조하려면 뉴스 팝업이 열려 있어야 하는데, 뉴스 팝업은 맨 앞(SetAsLastSibling)에 온다.
        // 그 뒤에 열리면 지문 검사 화면이 가려지므로, 열 때 이 패널을 형제 중 맨 앞으로 올린다.
        _root.transform.SetAsLastSibling();
    }

    /// <summary>패널을 닫는다.</summary>
    public void Close()
    {
        if (_seq != null) { StopCoroutine(_seq); _seq = null; }
        if (_autoReveal != null) { StopCoroutine(_autoReveal); _autoReveal = null; } // 수동 닫기 시 자동 적발 연출 취소
        if (_nextButton != null) _nextButton.gameObject.SetActive(false);
        SetStage(0);
        HideDbSelectables();
        _dbShown = false;
        _injected = null;
        if (_root != null) _root.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    // 손님이 바뀌면: 패널이 열려 있으면 갱신, 데이터가 사라졌으면 닫는다.
    private void HandleCustomerChanged()
    {
        if (_root != null && _root.activeSelf)
        {
            if (CurrentScan == null) Close();
            else Render(); // 갱신은 연출 없이 즉시(연출은 Open 시에만)
        }
        else
        {
            OnSelectablesChanged?.Invoke();
        }
    }

    /// <summary>
    /// 팝업 레이어 전환. 1=스캔 중 레이어만, 2=DB 결과 레이어만, 0=둘 다 끔.
    /// 그룹이 연결돼 있으면 그룹 단위로(권장), 미연결이면 스캐너 이미지만 개별 토글(구버전 폴백).
    /// </summary>
    private void SetStage(int stage)
    {
        if (_stage1Group != null) _stage1Group.SetActive(stage == 1);
        if (_stage2Group != null) _stage2Group.SetActive(stage == 2);
        if (_stage1Group == null && _scannerImage != null) _scannerImage.SetActive(stage == 1);
        // ResultText(범죄기록)는 FingerprintPanel 중첩프리팹의 원본이라 Stage2 그룹 안으로 못 옮긴다.
        // 그룹을 쓰는 패널(지문)에 한해 결과 단계와 활성 상태를 동기화한다(X-ray 패널은 _stage2Group=null → 영향 없음).
        if (_stage2Group != null && _resultText != null) _resultText.gameObject.SetActive(stage == 2);
    }

    // ── 3단계 연출 ───────────────────────────────────────────────
    private void StartSequence()
    {
        if (_seq != null) StopCoroutine(_seq);
        _dbShown = false; // 새 연출 시작 → DB 결과 표시 플래그 초기화
        _seq = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        ScanData s = CurrentScan;
        if (s == null) { Close(); yield break; }

        HideDbSelectables();
        if (_nextButton != null) _nextButton.gameObject.SetActive(false);

        // ① 스캔 중 — 팝업1 레이어(스캐너 이미지 + 안내문)만 표시.
        SetStage(1);
        if (_titleText != null) _titleText.text = "지문 판독";
        TMP_Text scanLabel = _scanText != null ? _scanText : _resultText; // 그룹 안내문, 없으면 결과텍스트로 폴백
        if (scanLabel != null) { scanLabel.color = _stageColor; scanLabel.text = "[ 지문 스캔 중입니다... ]\n\n잠시 기다려 주세요."; }
        if (_detailText != null) _detailText.text = "";
        if (_extraText != null) _extraText.text = "";
        OnSelectablesChanged?.Invoke();
        yield return new WaitForSeconds(_stageSeconds);

        // ② DB 조회 결과(최종) — 시스템 판정 없음. 플레이어가 DB 이름을 여권과 대조한다.
        RenderDbLookup(s);
        _seq = null;
    }

    // DB 결과 행: 이름/생년월일/국적 (순서대로, 각각 대조 가능)
    private static readonly string[] DbRowLabels = { "이름", "생년월일", "국적" };
    private static readonly string[] DbRowAttrs  = { "name", "birth_date", "nationality" };

    /// <summary>
    /// DB 조회 결과 = 지문으로 식별한 "진짜 신원". 시스템 판정 없음.
    /// 이름/생년월일/국적을 각각 교차대조 행으로 노출 → 플레이어가 여권·서류와 직접 비교.
    /// 범죄기록은 정보로 표시(대조 대상 아님).
    /// </summary>
    private void RenderDbLookup(ScanData s)
    {
        SetStage(2); // 결과 단계 — 팝업2 레이어(DB 행 + 범죄기록)만 표시
        _dbShown = true; // ③단계(DB 화면) 도달 표시 — 재Open 시 스캔 연출로 되돌아가지 않게
        if (_titleText != null) _titleText.text = "지문 DB 조회 결과";
        if (_detailText != null) _detailText.text = "";
        if (_extraText != null) _extraText.text = "";
        FingerprintRecord r = s != null ? s.record : null;

        if (r == null)
        {
            if (_resultText != null) { _resultText.color = _stageColor; _resultText.text = "조회 결과 없음 (미등록 지문)"; }
            HideDbSelectables();
            OnSelectablesChanged?.Invoke();
            return;
        }

        // 이름/생년월일/국적 → 각 대조 행에 바인딩
        string[] vals = { Safe(r.dbName), Safe(r.dbBirth), Safe(r.dbNationality) };
        if (_dbRows != null)
        {
            for (int i = 0; i < _dbRows.Length; i++)
            {
                if (_dbRows[i] == null) continue;
                bool has = i < DbRowLabels.Length;
                _dbRows[i].gameObject.SetActive(has);
                if (has)
                    _dbRows[i].Bind("지문DB", DbRowAttrs[i], vals[i],
                        $"{DbRowLabels[i]}    {vals[i]}", $"{DbRowLabels[i]}    {vals[i]}");
            }
        }

        // 범죄기록(정보)
        if (_resultText != null)
        {
            _resultText.color = _stageColor;
            _resultText.text = r.IsWanted
                ? $"<color=#ff5a5a>범죄 기록    {Safe(r.criminalRecord)}\n수배 번호    {Safe(r.wantedNo)}</color>"
                : "범죄 기록    없음";
        }
        OnSelectablesChanged?.Invoke();

        // 수배자 지문(윤서린): 플레이어 대조 없이 N초 후 자동으로 닫고 적발 대사를 재생(가이드 연출).
        // 수배 기록 없는 일반 성형 고객은 기존대로 플레이어가 직접 대조한다.
        if (_kind == ScanKind.Fingerprint && r.IsWanted && _crossCheck != null)
        {
            if (_autoReveal != null) StopCoroutine(_autoReveal);
            _autoReveal = StartCoroutine(AutoCloseAndReveal());
        }
    }

    /// <summary>수배자 지문 결과를 N초 보여준 뒤 자동으로 닫고, 적발 대사를 재생한다.
    /// 적발 대사가 끝나면 도장 없이 자동 연행(거부 정산 + 체념 대사 → 다음 손님)으로 이어진다.</summary>
    private IEnumerator AutoCloseAndReveal()
    {
        yield return new WaitForSeconds(_wantedAutoRevealSeconds);
        _autoReveal = null;
        Close();
        _crossCheck.PlayFingerprintReveal(() =>
        {
            if (_controller != null) _controller.AutoDetainWantedCustomer(); // 도장 안 찍고 끌려감
        });
    }

    private void HideDbSelectables()
    {
        if (_dbRows != null)
            foreach (var row in _dbRows) if (row != null) row.gameObject.SetActive(false);
        if (_claimSelectable != null) _claimSelectable.gameObject.SetActive(false);
    }

    private void Render()
    {
        ScanData s = CurrentScan;
        if (s == null) { Close(); return; }

        if (_kind == ScanKind.Xray)
        {
            if (_titleText != null) _titleText.text = "X-ray 검사";
            if (_resultText != null) _resultText.text = $"결과: {Safe(s.result)}";
            if (_detailText != null) _detailText.text = $"적발물: {Safe(s.detail)}";
            if (_extraText != null) _extraText.text = $"은닉 위치: {Safe(s.extra)}";
            BuildClaim(s);
        }
        else
        {
            RenderDbLookup(s); // 지문: DB 조회 결과 화면(갱신)
        }
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
        if (_dbRows != null)
            foreach (var row in _dbRows)
                if (row != null && row.gameObject.activeInHierarchy) yield return row;
        if (_claimSelectable != null && _claimSelectable.gameObject.activeInHierarchy)
            yield return _claimSelectable;
    }
}
