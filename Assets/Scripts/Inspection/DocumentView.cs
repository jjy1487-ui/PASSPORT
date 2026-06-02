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
    [SerializeField] private RectTransform _restSlot;        // 접힌 채 처음 놓일 거치 슬롯

    private readonly List<DocumentCardView> _spawned = new List<DocumentCardView>();

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
            // 거치 슬롯에 접힌 채로 살짝 겹쳐 놓는다(드래그로 책상에 올리면 펼쳐짐).
            if (_restSlot != null)
            {
                card.transform.position = _restSlot.position + new Vector3(i * 28f, i * 22f, 0f);
            }
            card.Bind(documents[i]);
            _spawned.Add(card);
        }

        OnDocumentsChanged?.Invoke();
        OnSelectablesChanged?.Invoke();
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
