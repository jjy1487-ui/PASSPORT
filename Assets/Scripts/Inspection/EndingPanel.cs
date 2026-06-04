using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 엔딩 화면. ImmigrationManager.OnEndingResolved 를 구독해 엔딩 패널을 띄운다.
/// 조기엔딩(#11~#14)·누적엔딩(#15/#16)·14일 종료 점수구간 모두 같은 이벤트로 들어온다.
///
/// 표시만 한다 — 엔딩 결정(점수구간/트리거/임계치)은 EndingResolver/ImmigrationManager 소유(규약 5장).
/// 패널은 강제 모달(Backdrop 클릭으로 닫히지 않음, UI-CONVENTIONS 4장: 결과/엔딩은 강제 패널).
/// 닫기 = X 우상단(타이틀 복귀). 단축키 Esc 로도 닫힘.
/// </summary>
public sealed class EndingPanel : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private GameObject _root;        // 비활성 시작
    [SerializeField] private TMP_Text _nameText;      // 엔딩명
    [SerializeField] private TMP_Text _descText;      // 엔딩 설명 + 최종 점수/호칭
    [SerializeField] private TMP_Text _typeBadgeText; // 엔딩 유형 배지(조기/누적/일반)
    [SerializeField] private Button _closeButton;     // X, 우상단 → 타이틀 복귀

    [Header("타이틀 복귀(선택)")]
    [Tooltip("닫기 시 로드할 씬 이름. 비우면 패널만 닫는다.")]
    [SerializeField] private string _titleSceneName = "";

    private ImmigrationManager _immigration;
    private bool _shown;

    private void Start()
    {
        _immigration = Object.FindFirstObjectByType<ImmigrationManager>();
        if (_immigration != null)
        {
            _immigration.OnEndingResolved += HandleEndingResolved;
            // 이미 엔딩이 결정된 뒤 생성됐다면(폴링 폴백) 즉시 반영.
            if (_immigration.LastEnding.IsValid) HandleEndingResolved(_immigration.LastEnding);
        }
        else
        {
            Debug.LogWarning("[EndingPanel] ImmigrationManager 를 찾지 못했습니다. 엔딩 표시 비활성.");
        }

        if (_closeButton != null) _closeButton.onClick.AddListener(CloseAndReturn);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_immigration != null) _immigration.OnEndingResolved -= HandleEndingResolved;
        if (_closeButton != null) _closeButton.onClick.RemoveListener(CloseAndReturn);
    }

    private void Update()
    {
        // 강제 모달이라 Backdrop 클릭으로는 안 닫힘 — Esc 로만 닫기(UI-CONVENTIONS 1장 단축키).
        if (_shown && Input.GetKeyDown(KeyCode.Escape)) CloseAndReturn();
    }

    private void HandleEndingResolved(EndingResult e)
    {
        if (!e.IsValid) return;
        _shown = true;
        if (_root != null) _root.SetActive(true);

        if (_nameText != null) _nameText.text = e.endingName;
        if (_typeBadgeText != null) _typeBadgeText.text = TypeLabel(e.endingType);
        if (_descText != null) _descText.text = BuildDescription(e);
    }

    /// <summary>엔딩 설명 + 최종 점수/획득 호칭(매니저에서 읽어 표시만).</summary>
    private string BuildDescription(EndingResult e)
    {
        var sb = new StringBuilder();

        string tableDesc = LookupEndingDescription(e.endingId);
        if (!string.IsNullOrEmpty(tableDesc)) sb.AppendLine(tableDesc).AppendLine();

        var mgr = ScoreEconomyManager.Instance;
        if (mgr != null)
        {
            sb.AppendLine($"최종 점수: {mgr.Score}");
            sb.AppendLine($"정확도: {Mathf.RoundToInt(mgr.Accuracy * 100f)}%  ({mgr.CorrectCount}/{mgr.JudgedCount})");
            if (mgr.Titles.Count > 0)
            {
                sb.Append("획득 호칭: ");
                sb.AppendLine(string.Join(", ", mgr.Titles));
            }
        }
        return sb.ToString();
    }

    /// <summary>ending 테이블에서 설명 텍스트를 찾는다(없으면 빈 문자열). 표시 보조용.</summary>
    private static string LookupEndingDescription(string endingId)
    {
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.ending : null;
        if (t == null || t.rows == null) return string.Empty;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (r.Get("ending_id") != endingId) continue;
            // 설명 후보 컬럼들(데이터에 있으면 사용, 없으면 무시).
            string desc = r.Get("ending_desc");
            if (string.IsNullOrEmpty(desc)) desc = r.Get("description");
            if (string.IsNullOrEmpty(desc)) desc = r.Get("note");
            return desc ?? string.Empty;
        }
        return string.Empty;
    }

    private static string TypeLabel(string endingType) => endingType switch
    {
        "early"      => "조기 엔딩",
        "cumulative" => "누적 엔딩",
        "normal"     => "엔딩",
        _            => "엔딩",
    };

    private void CloseAndReturn()
    {
        _shown = false;
        if (_root != null) _root.SetActive(false);
        if (!string.IsNullOrEmpty(_titleSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(_titleSceneName);
        }
    }
}
