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
             "예: 여권→PassportCard, 비자→VisaCard, PCR검사서→PcrCard, 취업증빙→EmploymentCard, 심사 오류 고지서→NoticeCard")]
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
    [SerializeField] private Vector2 _noticeBasePos = new Vector2(-300f, 300f);
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
                // 사용 슬롯(activeSelf=true)만 대조 후보로 노출한다. activeInHierarchy 가 아니라 activeSelf 를
                // 쓰는 이유: 카드가 닫혀 있으면(부모 OpenView 비활성) 사용 슬롯도 activeInHierarchy=false 가 되어
                // 손님 등장 시점(closed) 구독에서 빠지고, 펼쳐도 재구독이 없어 영영 클릭이 안 잡혔다.
                // activeSelf 는 카드 개폐와 무관하게 "이 슬롯이 사용 슬롯인지"만 보므로, 닫힌 채 미리 구독되고
                // 펼치면 바로 클릭된다(비활성 카드의 버튼은 어차피 레이캐스트 안 됨). 미사용 빈 슬롯(activeSelf=false)은 그대로 제외.
                if (row != null && row.gameObject.activeSelf) yield return row;
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
            card.Bind(documents[i]);
            // 닫힌 표지(ClosedView)를 먼저 활성화한 뒤 배치한다 → PositionAtSpawn 이 실제로 보이는
            // 닫힌 표지 크기로 클램프할 수 있다(루트는 329×440이지만 보이는 표지는 60×55라 루트 기준 클램프는 빗나간다).
            // StartClosed 는 표지 토글만 할 뿐 루트 위치를 옮기지 않으므로 PositionAtSpawn 보다 먼저 호출해도 안전.
            StartClosed(card); // 스폰 시 닫힌 표지만(열림+닫힘 동시표시 방지). 책상으로 드래그하면 펼쳐진다.
            PositionAtSpawn(card.transform as RectTransform, i);
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

        // 상태별 드래그 허용 영역을 카드에 주입한다(닫힘=SpawnArea, 펼침=DocumentArea).
        // 여권/일반 카드 둘 다 IDragBoundsReceiver 를 구현하므로 한 줄로 처리.
        // 둘 중 하나라도 유효하면 주입한다(닫힘만/펼침만 있어도 그 상태는 가둬짐).
        IDragBoundsReceiver bounds = card.GetComponent<IDragBoundsReceiver>();
        if (bounds != null)
        {
            Rect spawnZone = WorldRectOf(_spawnArea);
            Rect documentZone = WorldRectOf(_cardContainer as RectTransform);
            if ((spawnZone.width > 0f && spawnZone.height > 0f)
                || (documentZone.width > 0f && documentZone.height > 0f))
                bounds.ConfigureDragBounds(spawnZone, documentZone);
        }

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
    /// RectTransform 의 월드 사각형을 구한다(상태별 클램프 영역 주입용).
    /// _spawnArea → 닫힘 영역, _cardContainer → 펼침(책상) 영역으로 따로 쓴다.
    /// null 이면 빈 Rect(클램프 미적용 신호).
    /// </summary>
    private static Rect WorldRectOf(RectTransform rt)
    {
        if (rt == null) return new Rect(0f, 0f, 0f, 0f);
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c); // 0=좌하 1=좌상 2=우상 3=우하
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[1].y);
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

            // 클램프 기준은 '루트(329×440)'가 아니라 '실제로 보이는 닫힌 표지(ClosedView, 보통 60×55)'다.
            // 닫힌 표지의 월드 반(半)크기와 표지 중심이 루트 위치에서 얼마나 떨어졌는지(offset)를 구해,
            // (루트위치 + offset ± 반크기)가 영역 안에 들도록 클램프한다. ClosedView 가 없으면 _cardHalfSize 폴백.
            Vector2 half, offset;
            GetVisibleClosedExtents(card, out half, out offset);

            // 표지가 영역 안에 완전히 들어오도록, 표지 중심이 놓일 수 있는 범위를 계산한다.
            float minX = c[0].x + half.x - offset.x, maxX = c[2].x - half.x - offset.x;
            float minY = c[0].y + half.y - offset.y, maxY = c[1].y - half.y - offset.y;

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

    /// <summary>
    /// 카드에서 '보이는 닫힌 표지(ClosedView)'의 월드 반(半)크기 half 와, 표지 중심이 카드 루트 위치에서
    /// 떨어진 월드 offset 을 구한다. ClosedView 를 찾을 수 없으면 기존 _cardHalfSize / offset 0 으로 폴백.
    /// (Show() 에서 StartClosed 를 먼저 호출해 ClosedView 가 활성화된 뒤에 불려야 GetWorldCorners 가 유효.)
    /// </summary>
    private void GetVisibleClosedExtents(RectTransform card, out Vector2 half, out Vector2 offset)
    {
        Transform closed = card != null ? card.Find("ClosedView") : null;
        if (closed != null && closed.gameObject.activeInHierarchy && closed is RectTransform crt)
        {
            Vector3[] cc = new Vector3[4];
            crt.GetWorldCorners(cc); // 0=좌하 1=좌상 2=우상 3=우하
            half = new Vector2((cc[3].x - cc[0].x) * 0.5f, (cc[1].y - cc[0].y) * 0.5f);
            Vector3 center = (cc[0] + cc[2]) * 0.5f;
            offset = new Vector2(center.x - card.position.x, center.y - card.position.y);
            return;
        }
        // 폴백: 보이는 표지를 못 찾으면 루트 중심 기준의 추정 반크기를 쓴다(예전 동작).
        half = _cardHalfSize;
        offset = Vector2.zero;
    }

    /// <summary>
    /// 고지서를 일반 서류와 같은 출현 영역(_spawnArea = 검사 데스크의 서류 스폰 자리)에 띄운다.
    /// 서있는 캐릭터(CustomerArea) 위/중앙을 가리지 않도록, 서류가 뜨는 곳과 동일한 위치에 둔다.
    /// _spawnArea 가 없으면 기존 고정 기준 위치(_noticeBasePos) 폴백.
    /// 여러 장이면 인덱스만큼 어긋나게(_noticeStagger) 누적해 겹침을 막는다.
    /// 런타임 클론이라 인스펙터로 직접 못 박으므로 스폰 시 코드로 고정한다.</summary>
    private void PositionNotice(RectTransform card, int i)
    {
        if (card == null) return;
        card.sizeDelta = _noticeSize;

        if (_spawnArea != null)
        {
            // 일반 서류 스폰과 동일한 영역(SpawnArea) 중앙을 기준으로, 누적 인덱스만큼만 어긋나게.
            Vector3[] c = new Vector3[4];
            _spawnArea.GetWorldCorners(c); // 0=좌하 1=좌상 2=우상 3=우하

            // 고지서도 루트 추정치(_cardHalfSize)가 아니라 '보이는 닫힌 표지' 크기(_noticeSize, 중심 정렬)로 클램프한다.
            // 고지서 카드의 ClosedView 는 루트에 꽉 차게 늘어나 있어, 방금 루트를 _noticeSize 로 잡았으니 표지=_noticeSize.
            Vector2 half = _noticeSize * 0.5f;
            float minX = c[0].x + half.x, maxX = c[2].x - half.x;
            float minY = c[0].y + half.y, maxY = c[1].y - half.y;

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float x = cx + i * _noticeStagger.x;
            float y = cy - i * _noticeStagger.y;
            if (maxX > minX) x = Mathf.Clamp(x, minX, maxX);
            if (maxY > minY) y = Mathf.Clamp(y, minY, maxY);

            card.position = new Vector3(x, y, _spawnArea.position.z);
            return;
        }

        // 폴백: SpawnArea 미연결 시 기존 고정 기준 위치.
        card.anchoredPosition = _noticeBasePos + new Vector2(i * _noticeStagger.x, -i * _noticeStagger.y);
    }

    /// <summary>
    /// 오판 피드백용 '고지서' 카드를 스폰한다(여권처럼 카드로 등장, 즉시 펼친 상태).
    /// 무엇이 틀렸는지 fields 로 나열된 notice 를 받아 표시한다.
    /// </summary>
    public DocumentCardView SpawnNotice(DocumentData notice)
    {
        if (notice == null || _cardContainer == null) return null;

        // 고지서도 다른 서류 카드와 동일하게 _cardPrefabs 매핑(documentType→프리팹)으로 해석한다.
        // "심사 오류 고지서" 항목이 등록돼 있으면 NoticeCard, 없으면 범용 _cardTemplate으로 폴백.
        DocumentCardView prefab = ResolveCardPrefab(notice.documentType);
        Debug.Log($"[NoticeDBG] SpawnNotice type='{notice.documentType}' prefab={(prefab == null ? "NULL(스폰 불가)" : prefab.name)} container={(_cardContainer != null ? _cardContainer.name : "null")}");
        if (prefab == null) return null;

        DocumentCardView card = Instantiate(prefab, _cardContainer);
        card.gameObject.SetActive(true);
        card.Bind(notice);

        // 고지서는 '접힌(닫힌) 표지'로 책상 좌하단 코너에 띄운다(작은 접힌 쪽지). 펼쳐 읽으려면 책상 안쪽으로 드래그.
        PassportDocument pd = card.GetComponent<PassportDocument>();
        if (pd != null)
        {
            if (_cardContainer is RectTransform crt) pd.ConfigureOpenZone(crt);
            pd.SetOpen(false); // 접힌 상태로 등장(닫힘 표지 = '오류 고지서(접힌버전)')
        }
        if (card.transform is RectTransform nrt)
        {
            nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.sizeDelta = _noticeSize; // 닫힌 썸네일 크기(인스펙터 _noticeSize) 적용 — 런타임 클론이라 코드로 고정.
            // 코너 기준 위치(_noticeBasePos)에서 누적 장수만큼 어긋나게 → 2장 이상도 서로 겹치지 않게.
            nrt.anchoredPosition = _noticeBasePos + new Vector2(_notices.Count * _noticeStagger.x, _notices.Count * _noticeStagger.y);
        }

        // _spawned 가 아니라 _notices 에 보관 → Clear()(손님 교체) 에도 사라지지 않는다.
        _notices.Add(card);
        Debug.Log($"[NoticeDBG] 고지서 스폰됨: pos={((RectTransform)card.transform).position} activeInHierarchy={card.gameObject.activeInHierarchy} 누적={_notices.Count}");

        // '닫기' 버튼: 누르면 이 고지서를 책상에서 제거한다.
        if (card.CloseButton != null)
        {
            DocumentCardView self = card;
            card.CloseButton.onClick.AddListener(() =>
            {
                _notices.Remove(self);
                if (self != null) Destroy(self.gameObject);
                OnSelectablesChanged?.Invoke();
            });
        }
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
