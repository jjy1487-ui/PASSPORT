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

    [Header("서류 종류별 카드 프리팹")]
    [Tooltip("documentType 에 맞는 전용 카드 프리팹을 복제한다. 매핑에 없으면 _cardTemplate(범용)을 쓴다.\n" +
             "예: 여권→PassportCard, 비자→VisaCard, PCR검사서→PcrCard, 취업증빙→EmploymentCard")]
    [SerializeField] private CardPrefabEntry[] _cardPrefabs;

    /// <summary>서류 종류 → 전용 카드 프리팹 매핑 1건.</summary>
    [System.Serializable]
    private struct CardPrefabEntry
    {
        [Tooltip("서류 종류(documentType). 예: 여권 / 비자 / PCR검사서 / 취업증빙")]
        public string documentType;
        [Tooltip("그 종류 전용 카드 프리팹")]
        public DocumentCardView prefab;
    }

    /// <summary>documentType 에 맞는 카드 프리팹을 고른다(매핑에 없으면 범용 _cardTemplate).</summary>
    private DocumentCardView ResolveCardPrefab(string documentType)
    {
        if (_cardPrefabs != null && !string.IsNullOrEmpty(documentType))
        {
            foreach (CardPrefabEntry e in _cardPrefabs)
                if (e.prefab != null && e.documentType == documentType) return e.prefab;
        }
        return _cardTemplate;
    }

    [Header("출현 배치")]
    [Tooltip("카드끼리 어긋나는 간격(px). 완전히 겹치지 않게 한다.")]
    [SerializeField] private Vector2 _spawnStagger = new Vector2(40f, 36f);
    [Tooltip("닫힌 카드 크기의 절반(영역 밖으로 안 나가게 클램프용).")]
    [SerializeField] private Vector2 _cardHalfSize = new Vector2(30f, 28f);

    [Header("고지서(오판 피드백) 배치")]
    [Tooltip("고지서 카드 기준 위치(anchoredPosition, _cardContainer 기준). 첫 고지서가 놓일 자리.")]
    [SerializeField] private Vector2 _noticeBasePos = new Vector2(-1173f, -88f);
    [Tooltip("고지서 카드 크기(sizeDelta). 닫힌 썸네일 크기.")]
    [SerializeField] private Vector2 _noticeSize = new Vector2(83.8205f, 86.6596f);
    [Tooltip("고지서가 여러 장일 때 카드끼리 어긋나는 간격(px).")]
    [SerializeField] private Vector2 _noticeStagger = new Vector2(26f, 24f);

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
                // 활성 슬롯만 대조 후보로 노출한다(미사용 슬롯은 SetActive(false)로 숨겨져 있음 →
                //  비활성 빈 슬롯이 유령 대조 후보가 되는 것을 막는다).
                if (row != null && row.gameObject.activeInHierarchy) yield return row;
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
            DocumentCardView card = Instantiate(ResolveCardPrefab(documents[i].documentType), _cardContainer);
            card.gameObject.SetActive(true);
            PositionAtSpawn(card.transform as RectTransform, i);
            card.Bind(documents[i]);
            StartClosed(card); // 스폰 시 닫힌 표지만(열림+닫힘 동시표시 방지). 책상으로 드래그하면 펼쳐진다.
            _spawned.Add(card);
        }

        OnDocumentsChanged?.Invoke();
        OnSelectablesChanged?.Invoke();
    }

    /// <summary>카드를 닫힌 상태로 시작시키고, 펼침 영역(책상=_cardContainer)을 코드로 지정한다.
    /// PassportDocument 가 있으면 그것으로, 없으면 OpenView/ClosedView 를 직접 토글한다.</summary>
    private void StartClosed(DocumentCardView card)
    {
        if (card == null) return;
        PassportDocument pd = card.GetComponent<PassportDocument>();
        if (pd != null)
        {
            if (_cardContainer is RectTransform crt) pd.ConfigureOpenZone(crt);
            pd.SetOpen(false);
            return;
        }
        Transform open = card.transform.Find("OpenView");
        Transform closed = card.transform.Find("ClosedView");
        if (open != null) open.gameObject.SetActive(false);
        if (closed != null) closed.gameObject.SetActive(true);
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

    /// <summary>고지서를 고정 기준 위치(_noticeBasePos)·크기(_noticeSize)로 배치한다.
    /// 여러 장이면 인덱스만큼 어긋나게(_noticeStagger) 누적해 겹침을 막는다.
    /// 런타임 클론이라 인스펙터로 직접 못 박으므로 스폰 시 코드로 고정한다.</summary>
    private void PositionNotice(RectTransform card, int i)
    {
        if (card == null) return;
        card.sizeDelta = _noticeSize;
        card.anchoredPosition = _noticeBasePos + new Vector2(i * _noticeStagger.x, -i * _noticeStagger.y);
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

        // 고지서도 닫힌 썸네일로 시작 → 책상(DocumentArea)으로 드래그하면 펼쳐진다(서류와 동일 규칙).
        StartClosed(card);

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
