using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 1장 카드. 펼침(필드) / 접힘(국가별 표지) 두 상태의 표시 내용을 채운다.
/// 펼침 상태에서는 필드를 행(<see cref="DocumentFieldView"/>) 단위로 생성해 클릭 대조가 가능하다.
/// 도장 자국은 펼친 여권 종이 위에 표시한다.
/// </summary>
public sealed class DocumentCardView : MonoBehaviour
{
    [Header("펼침(필드)")]
    [SerializeField] private Image _background;
    [SerializeField] private TMP_Text _typeHeader;
    [SerializeField] private TMP_Text _bodyText; // 폴백(필드 행 프리팹 미연결 시 한 덩어리 표시)

    [Header("필드 행(클릭 대조)")]
    [SerializeField] private Transform _fieldContainer;        // 필드 행이 놓일 부모(VerticalLayoutGroup 권장)
    [SerializeField] private DocumentFieldView _fieldTemplate; // 비활성 행 템플릿

    [Header("접힘(표지)")]
    [SerializeField] private Image _closedImage;
    [SerializeField] private TMP_Text _closedLabel;
    [SerializeField] private Sprite _coverKor;
    [SerializeField] private Sprite _coverChn;
    [SerializeField] private Sprite _coverJpn;
    [SerializeField] private Sprite _coverUsa;

    [Header("도장 자국(여권 종이 위)")]
    [SerializeField] private GameObject _stampRoot;
    [SerializeField] private TMP_Text _stampText;

    private readonly List<DocumentFieldView> _fieldRows = new List<DocumentFieldView>();

    /// <summary>이 카드가 생성한 필드 행 목록(대조 컨트롤러가 구독).</summary>
    public IReadOnlyList<DocumentFieldView> FieldRows => _fieldRows;

    /// <summary>서류 데이터를 카드에 채운다.</summary>
    public void Bind(DocumentData doc)
    {
        if (doc == null)
        {
            return;
        }

        if (_typeHeader != null)
        {
            _typeHeader.text = doc.documentType;
        }

        BuildFieldRows(doc);

        // 필드 행 프리팹이 연결돼 있으면 한 덩어리 본문은 숨긴다(중복 방지).
        if (_bodyText != null)
        {
            bool useRows = _fieldContainer != null && _fieldTemplate != null;
            if (useRows)
            {
                _bodyText.gameObject.SetActive(false);
            }
            else
            {
                _bodyText.gameObject.SetActive(true);
                StringBuilder sb = new StringBuilder();
                if (doc.fields != null)
                {
                    foreach (FieldEntry f in doc.fields)
                    {
                        sb.Append(f.label).Append(": ").Append(f.value).Append('\n');
                    }
                }
                _bodyText.text = sb.ToString().TrimEnd('\n');
            }
        }

        if (_background != null)
        {
            _background.color = new Color(0.97f, 0.96f, 0.90f);
        }

        // 접힌 표지: 국가별 여권 이미지(없으면 갈색 책자 + 종류명)
        Sprite cover = CoverFor(doc.country);
        if (cover != null && _closedImage != null)
        {
            _closedImage.sprite = cover;
            _closedImage.color = Color.white;
            _closedImage.preserveAspect = true;
            if (_closedLabel != null) _closedLabel.gameObject.SetActive(false);
        }
        else
        {
            if (_closedImage != null)
            {
                _closedImage.sprite = null;
                _closedImage.color = new Color(0.45f, 0.30f, 0.18f);
            }
            if (_closedLabel != null)
            {
                _closedLabel.gameObject.SetActive(true);
                _closedLabel.text = doc.documentType;
            }
        }

        HideStamp();
    }

    /// <summary>여권 종이 위에 도장 자국을 표시한다.</summary>
    public void ShowStamp(bool approve)
    {
        if (_stampRoot != null)
        {
            _stampRoot.SetActive(true);
            _stampRoot.transform.SetAsLastSibling();
        }
        if (_stampText != null)
        {
            _stampText.text = approve ? "입국 허가" : "입국 거부";
            _stampText.color = approve
                ? new Color(0.20f, 0.55f, 0.20f, 0.9f)
                : new Color(0.70f, 0.15f, 0.15f, 0.9f);
        }
    }

    /// <summary>도장 자국을 숨긴다.</summary>
    public void HideStamp()
    {
        if (_stampRoot != null) _stampRoot.SetActive(false);
    }

    /// <summary>fields[] 만큼 필드 행을 생성해 컨테이너에 채운다.</summary>
    private void BuildFieldRows(DocumentData doc)
    {
        ClearFieldRows();

        if (_fieldContainer == null || _fieldTemplate == null || doc.fields == null)
        {
            return;
        }

        foreach (FieldEntry f in doc.fields)
        {
            if (f == null) continue;
            DocumentFieldView row = Instantiate(_fieldTemplate, _fieldContainer);
            row.gameObject.SetActive(true);
            row.Bind(doc.documentType, f.label, f.value, f.key);
            _fieldRows.Add(row);
        }
    }

    private void ClearFieldRows()
    {
        foreach (DocumentFieldView row in _fieldRows)
        {
            if (row != null) Destroy(row.gameObject);
        }
        _fieldRows.Clear();
    }

    private Sprite CoverFor(string country)
    {
        switch (country)
        {
            case "KOR": return _coverKor;
            case "CHN": return _coverChn;
            case "JPN": return _coverJpn;
            case "USA": return _coverUsa;
            default: return null;
        }
    }
}
