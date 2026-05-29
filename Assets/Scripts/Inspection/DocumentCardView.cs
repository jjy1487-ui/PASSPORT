using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 1장 카드. 펼침(필드) / 접힘(국가별 표지) 두 상태의 표시 내용을 채운다.
/// 도장 자국은 펼친 여권 종이 위에 표시한다.
/// </summary>
public sealed class DocumentCardView : MonoBehaviour
{
    [Header("펼침(필드)")]
    [SerializeField] private Image _background;
    [SerializeField] private TMP_Text _typeHeader;
    [SerializeField] private TMP_Text _bodyText;

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

        if (_bodyText != null)
        {
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
