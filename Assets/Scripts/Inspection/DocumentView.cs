using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 손님의 서류들을 카드로 펼쳐 표시한다(다중 서류 손님 대응).
/// 카드 템플릿을 복제해 컨테이너(VerticalLayoutGroup)에 채운다.
/// </summary>
public sealed class DocumentView : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private Transform _cardContainer;       // 카드가 놓일 부모(책상)
    [SerializeField] private DocumentCardView _cardTemplate; // 비활성 템플릿
    [SerializeField] private RectTransform _restSlot;        // (폴백) 접힌 채 처음 놓일 거치 슬롯
    [SerializeField] private RectTransform _spawnArea;        // 처음 출현 영역 — 이 사각형 안에서만 카드가 뜬다

    [Header("출현 배치")]
    [Tooltip("카드끼리 어긋나는 간격(px). 완전히 겹치지 않게 한다.")]
    [SerializeField] private Vector2 _spawnStagger = new Vector2(40f, 36f);
    [Tooltip("닫힌 카드 크기의 절반(영역 밖으로 안 나가게 클램프용).")]
    [SerializeField] private Vector2 _cardHalfSize = new Vector2(30f, 28f);

    private readonly List<DocumentCardView> _spawned = new List<DocumentCardView>();
    // 오판 고지서: 손님 교체(Clear)에도 사라지지 않고 누적된다.
    private readonly List<DocumentCardView> _notices = new List<DocumentCardView>();

    /// <summary>현재 표시 중인 서류 카드 목록(대조 컨트롤러가 필드 행을 수집).</summary>
    public IReadOnlyList<DocumentCardView> SpawnedCards => _spawned;

    /// <summary>새 손님 서류가 표시될 때마다 발행(대조 컨트롤러가 재바인딩).</summary>
    public event System.Action OnDocumentsChanged;

    // ── ICrossCheckProvider ──────────────────────────────────────
    /// <summary>selectable 구성 변경 통지(서류 표시/제거 시).</summary>
    public event System.Action OnSelectablesChanged;

    /// <summary>현재 카드들의 필드 행을 selectable 로 노출한다.</summary>
    public System.Collections.Generic.IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        foreach (DocumentCardView card in _spawned)
        {
            if (card == null) continue;
            foreach (DocumentFieldView row in card.FieldRows)
            {
                if (row != null) yield return row;
            }
        }
    }

    /// <summary>서류 목록을 표시한다.</summary>
    public void Show(IReadOnlyList<DocumentData> documents)
    {
        Clear();

        if (documents == null || _cardContainer == null || _cardTemplate == null)
        {
            return;
        }

        int count = documents.Count;
        for (int i = 0; i < count; i++)
        {
            DocumentCardView card = Instantiate(_cardTemplate, _cardContainer);
            card.gameObject.SetActive(true);
            PositionAtSpawn(card.transform as RectTransform, i);
            card.Bind(documents[i]);
            _spawned.Add(card);
        }

        OnDocumentsChanged?.Invoke();
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>
    /// 카드 i를 처음 출현 위치에 놓는다.
    /// _spawnArea 가 있으면 그 사각형 안에서 인덱스별로 어긋나게(겹치지 않게) 배치하고,
    /// 영역 밖으로 나가지 않도록 클램프한다. 없으면 _restSlot 폴백.
    /// </summary>
    private void PositionAtSpawn(RectTransform card, int i)
    {
        if (card == null) return;

        if (_spawnArea != null)
        {
            Vector3[] c = new Vector3[4];
            _spawnArea.GetWorldCorners(c); // 0=좌하 1=좌상 2=우상 3=우하
            float minX = c[0].x + _cardHalfSize.x, maxX = c[2].x - _cardHalfSize.x;
            float minY = c[0].y + _cardHalfSize.y, maxY = c[1].y - _cardHalfSize.y;

            // 영역 정중앙을 기준으로 배치(첫 장은 정확히 중앙, 여러 장은 중앙에서 약간씩 어긋나게).
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float x = cx + i * _spawnStagger.x;
            float y = cy - i * _spawnStagger.y;
            if (maxX > minX) x = Mathf.Clamp(x, minX, maxX);
            if (maxY > minY) y = Mathf.Clamp(y, minY, maxY);

            card.position = new Vector3(x, y, _spawnArea.position.z);
        }
        else if (_restSlot != null)
        {
            card.position = _restSlot.position + new Vector3(i * _spawnStagger.x, i * _spawnStagger.y, 0f);
        }
    }

    /// <summary>고지서를 출현 영역 좌상단부터 인덱스만큼 어긋나게 누적 배치한다(손님 서류와 겹침 최소화).</summary>
    private void PositionNotice(RectTransform card, int i)
    {
        if (card == null) return;
        RectTransform area = _spawnArea != null ? _spawnArea : _restSlot;
        if (area == null) return;
        Vector3[] c = new Vector3[4];
        area.GetWorldCorners(c); // 0=좌하 1=좌상 2=우상 3=우하
        float x = c[1].x + _cardHalfSize.x + 8f + i * 26f;
        float y = c[1].y - _cardHalfSize.y - 8f - i * 24f;
        card.position = new Vector3(x, y, area.position.z);
    }

    /// <summary>
    /// 오판 피드백용 '고지서' 카드를 스폰한다(여권처럼 카드로 등장, 즉시 펼친 상태).
    /// 무엇이 틀렸는지 fields 로 나열된 notice 를 받아 표시한다.
    /// </summary>
    public DocumentCardView SpawnNotice(DocumentData notice)
    {
        if (notice == null || _cardContainer == null || _cardTemplate == null) return null;

        DocumentCardView card = Instantiate(_cardTemplate, _cardContainer);
        card.gameObject.SetActive(true);
        PositionNotice(card.transform as RectTransform, _notices.Count); // 좌상단부터 누적
        card.Bind(notice);

        // 고지서는 접지 않고 즉시 펼쳐서 내용을 보여준다.
        Transform open = card.transform.Find("OpenView");
        Transform closed = card.transform.Find("ClosedView");
        if (open != null) open.gameObject.SetActive(true);
        if (closed != null) closed.gameObject.SetActive(false);

        // _spawned 가 아니라 _notices 에 보관 → Clear()(손님 교체) 에도 사라지지 않는다.
        _notices.Add(card);
        return card;
    }

    /// <summary>누적된 오판 고지서를 모두 제거한다(날짜가 넘어갈 때 호출).</summary>
    public void ClearNotices()
    {
        foreach (DocumentCardView notice in _notices)
        {
            if (notice != null) Destroy(notice.gameObject);
        }
        _notices.Clear();
    }

    /// <summary>표시된 카드를 모두 제거한다.</summary>
    public void Clear()
    {
        foreach (DocumentCardView card in _spawned)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }
        _spawned.Clear();
        OnDocumentsChanged?.Invoke();
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>대표 서류(첫 장, 보통 여권)에 도장 자국을 찍는다.</summary>
    public void StampPrimary(bool approve)
    {
        if (_spawned.Count > 0 && _spawned[0] != null)
        {
            _spawned[0].ShowStamp(approve);
        }
    }

    /// <summary>모든 서류의 도장 자국을 지운다.</summary>
    public void ClearStamps()
    {
        foreach (DocumentCardView card in _spawned)
        {
            if (card != null) card.HideStamp();
        }
    }
}
