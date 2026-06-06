using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서류 1장 카드. 펼침/접힘 두 상태를 표시한다.
/// 종류별 카드 프리팹(여권/비자/PCR/취업증빙)은 OpenView 에 슬롯이 미리 배치돼 있고,
/// Bind 가 field.key 로 슬롯을 찾아 값만 채운다(좌표 조립 없음). 미리 배치 슬롯이 없는 카드
/// (고지서 등)는 동적 필드 행으로 폴백한다. 도장 자국은 펼친 종이 위에 표시한다.
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
    [Tooltip("종류별 카드 프리팹의 닫힘 표지 아이콘(설정 시 종류 분기보다 우선). 예: 재직증명서 닫힘 아이콘.")]
    [SerializeField] private Sprite _closedCoverOverride;
    [SerializeField] private Sprite _coverKor;
    [SerializeField] private Sprite _coverChn;
    [SerializeField] private Sprite _coverJpn;
    [SerializeField] private Sprite _coverUsa;
    [Tooltip("비자 닫힘 아이콘. 비우면 Resources/Documents/visa_closed 자동 로드.")]
    [SerializeField] private Sprite _visaClosedCover;
    [Tooltip("PCR 닫힘 아이콘. 비우면 Resources/Documents/pcr_closed 자동 로드.")]
    [SerializeField] private Sprite _pcrClosedCover;

    [Header("사진(photo_ref) — 일반/고지서 경로용")]
    [Tooltip("미리 배치 슬롯이 없는 카드의 사진칸. 종류별 프리팹은 자체 face 슬롯을 쓴다.")]
    [SerializeField] private Image _photoImage;

    [Header("도장 자국")]
    [SerializeField] private GameObject _stampRoot;
    [SerializeField] private TMP_Text _stampText;

    [Header("종류별 카드 프리팹 — 미리 배치 슬롯 채우기")]
    [Tooltip("PCR 카드 배경 Image(음성/양성 sprite 교체용). PCR 전용 프리팹에만 연결.")]
    [SerializeField] private Image _pcrBackground;
    [SerializeField] private Sprite _pcrNegativeSprite;
    [SerializeField] private Sprite _pcrPositiveSprite;
    [Tooltip("여권 파생 표시(비대조). 여권 전용 프리팹에만 연결.")]
    [SerializeField] private TMP_Text _issueCountryText;
    [SerializeField] private TMP_Text _signatureText;

    // 프리팹에 미리 배치된 슬롯(key→슬롯). Awake에서 1회 인덱싱. 비어 있으면 일반(고지서) 경로.
    private readonly Dictionary<string, DocumentFieldView> _slotByKey = new Dictionary<string, DocumentFieldView>();
    private bool _slotsIndexed;

    private readonly List<DocumentFieldView> _fieldRows = new List<DocumentFieldView>();

    /// <summary>이 카드가 표시 중인 필드 행 목록(대조 컨트롤러가 구독).</summary>
    public IReadOnlyList<DocumentFieldView> FieldRows => _fieldRows;

    /// <summary>서류 데이터를 카드에 채운다.</summary>
    public void Bind(DocumentData doc)
    {
        if (doc == null) return;
        if (_typeHeader != null) _typeHeader.text = doc.documentType;

        // ── 종류별 카드 프리팹: 미리 배치된 슬롯을 데이터 key로 채운다(여권/비자/PCR/취업증빙) ──
        if (HasAuthoredSlots())
        {
            FillAuthoredSlots(doc);
            if (_background != null) _background.color = new Color(1f, 1f, 1f, 0f);
            BuildClosedCover(doc);
            HideStamp();
            return;
        }

        // ── 일반 렌더(고지서 등 미리 배치 슬롯이 없는 카드): 동적 필드 행 + 사진 ──
        BuildFieldRows(doc);
        BuildPhoto(doc);

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

        if (_background != null) _background.color = new Color(0.97f, 0.96f, 0.90f);

        BuildClosedCover(doc);
        HideStamp();
    }

    // ── 종류별 프리팹: 미리 배치된 슬롯을 데이터 key로 채운다(좌표 조립 없음) ──────────

    private void Awake() => IndexAuthoredSlots();

    /// <summary>프리팹에 author된 슬롯(SlotKey 보유)을 key→슬롯으로 1회 인덱싱.</summary>
    private void IndexAuthoredSlots()
    {
        if (_slotsIndexed) return;
        _slotByKey.Clear();
        foreach (DocumentFieldView slot in GetComponentsInChildren<DocumentFieldView>(true))
        {
            if (slot == null) continue;
            string k = slot.SlotKey;
            if (string.IsNullOrEmpty(k)) continue;     // 템플릿/비식별 슬롯 제외
            if (!_slotByKey.ContainsKey(k)) _slotByKey[k] = slot;
        }
        _slotsIndexed = true;
    }

    /// <summary>이 카드가 미리 배치된 슬롯을 가졌는가(신 경로 사용 여부).</summary>
    private bool HasAuthoredSlots()
    {
        if (!_slotsIndexed) IndexAuthoredSlots();
        return _slotByKey.Count > 0;
    }

    /// <summary>미리 배치된 슬롯들을 데이터 field.key로 매칭해 채운다. 대조 슬롯은 _fieldRows에 등록.</summary>
    private void FillAuthoredSlots(DocumentData doc)
    {
        IndexAuthoredSlots();
        _fieldRows.Clear();

        // 1) 모든 슬롯 초기화(이전 손님 값 잔류 방지 + 숨김)
        foreach (var kv in _slotByKey)
        {
            if (kv.Value == null) continue;
            kv.Value.ClearForReuse();
            kv.Value.gameObject.SetActive(false);
        }

        // 2) 데이터 필드를 key로 매칭해 활성화 + 채움(표시=형식화 / 비교=원본 값)
        if (doc.fields != null)
        {
            foreach (FieldEntry f in doc.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.key) || f.key == "face") continue; // 사진은 3)에서 별도
                if (_slotByKey.TryGetValue(f.key, out DocumentFieldView slot) && slot != null)
                {
                    slot.gameObject.SetActive(true);
                    slot.Bind(doc.documentType, string.Empty, f.value, f.key, FormatDisplay(f.key, f.value));
                    _fieldRows.Add(slot);
                }
            }
        }

        // 3) 얼굴 사진 슬롯(여권): 값=spriteRef(대조용), 표시=실제 사진 이미지
        if (_slotByKey.TryGetValue("face", out DocumentFieldView faceSlot) && faceSlot != null
            && !string.IsNullOrEmpty(doc.spriteRef))
        {
            faceSlot.gameObject.SetActive(true);
            faceSlot.Bind(doc.documentType, string.Empty, doc.spriteRef, "face", string.Empty);
            PaintAuthoredFace(faceSlot, doc.spriteRef);
            _fieldRows.Add(faceSlot);
        }

        // 4) PCR 배경(음성/양성) 교체
        if (_pcrBackground != null && IsPcr(doc) && (_pcrNegativeSprite != null || _pcrPositiveSprite != null))
            _pcrBackground.sprite = IsPcrPositive(doc) ? _pcrPositiveSprite : _pcrNegativeSprite;

        // 5) 파생 표시(여권: 발급국가/서명) — 비대조 텍스트
        if (_issueCountryText != null) _issueCountryText.text = CountryName(FieldValue(doc, "nationality"));
        if (_signatureText != null) _signatureText.text = FieldValue(doc, "name");
    }

    /// <summary>여권 얼굴 슬롯 뒤에 실제 사진(Resources/Characters/{spriteRef})을 깐다(클릭은 슬롯 버튼이 받음).</summary>
    private void PaintAuthoredFace(DocumentFieldView faceSlot, string spriteRef)
    {
        if (faceSlot == null) return;
        Sprite photo = Resources.Load<Sprite>("Characters/" + StripExt(spriteRef));
        Transform existing = faceSlot.transform.Find("FacePhoto");
        Image img = existing != null ? existing.GetComponent<Image>() : null;
        if (img == null)
        {
            var go = new GameObject("FacePhoto", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(faceSlot.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.SetSiblingIndex(0); // 하이라이트/값 텍스트보다 뒤
            img = go.GetComponent<Image>();
            img.raycastTarget = false; // 클릭은 슬롯 버튼이 받음
        }
        if (photo != null) { img.sprite = photo; img.color = Color.white; img.preserveAspect = true; }
        else { img.sprite = null; img.color = new Color(0.85f, 0.85f, 0.85f, 0.6f); }
    }

    /// <summary>접힌 표지: 오버라이드 우선 → 비자/PCR 아이콘 → 국가별 여권 표지(없으면 갈색 책자 + 종류명).</summary>
    private void BuildClosedCover(DocumentData doc)
    {
        Sprite cover = _closedCoverOverride != null ? _closedCoverOverride
            : IsVisa(doc)
            ? (_visaClosedCover != null ? _visaClosedCover : Resources.Load<Sprite>("Documents/visa_closed"))
            : IsPcr(doc)
            ? (_pcrClosedCover != null ? _pcrClosedCover : Resources.Load<Sprite>("Documents/pcr_closed"))
            : CoverFor(doc.country);
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

    /// <summary>
    /// 일반/고지서 경로의 사진칸을 doc.spriteRef(photo_ref) 로 채운다(미리 배치 슬롯이 없는 카드용).
    /// 사진칸 Image 가 없으면 자동 생성, 스프라이트를 못 찾으면 숨긴다. 표시 전용 — 대조/판정과 무관.
    /// </summary>
    private void BuildPhoto(DocumentData doc)
    {
        Sprite photo = !string.IsNullOrEmpty(doc.spriteRef)
            ? Resources.Load<Sprite>("Characters/" + StripExt(doc.spriteRef))
            : null;

        if (photo == null)
        {
            if (_photoImage != null) _photoImage.gameObject.SetActive(false);
            return;
        }

        if (_photoImage == null)
        {
            _photoImage = CreatePhotoSlot();
            if (_photoImage == null) return;
        }

        _photoImage.gameObject.SetActive(true);
        _photoImage.sprite = photo;
        _photoImage.color = Color.white;
        _photoImage.preserveAspect = true;

        // 사진을 클릭 대조(attr="face") 가능하게 — 사진칸 위에 클릭 슬롯을 얹는다.
        AddFaceHitSlot(_photoImage.rectTransform, doc);
    }

    /// <summary>사진칸 위에 투명 클릭 슬롯(DocumentFieldView, key="face")을 얹어 얼굴 대조를 가능하게 한다.</summary>
    private void AddFaceHitSlot(RectTransform photoRect, DocumentData doc)
    {
        if (_fieldTemplate == null || photoRect == null
            || doc == null || string.IsNullOrEmpty(doc.spriteRef)) return;

        DocumentFieldView row = Instantiate(_fieldTemplate, photoRect);
        row.gameObject.SetActive(true);

        RectTransform rt = row.transform as RectTransform;
        StretchFill(rt); // 사진칸 전체를 덮는다.

        row.Bind(doc.documentType, string.Empty, doc.spriteRef, "face", string.Empty);
        _fieldRows.Add(row);
    }

    /// <summary>사진칸 Image 가 인스펙터에 없을 때 OpenView 좌상단에 자동 생성한다(폴백).</summary>
    private Image CreatePhotoSlot()
    {
        Transform parent = transform.Find("OpenView");
        if (parent == null) parent = transform; // 폴백: 카드 루트

        GameObject go = new GameObject("PhotoImage", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.08f, 0.60f);
        rt.anchorMax = new Vector2(0.40f, 0.90f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetSiblingIndex(1);

        Image img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    /// <summary>fields[] 만큼 필드 행을 생성해 컨테이너에 채운다(일반/고지서 경로).</summary>
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
            if (row != null) SafeDestroy(row.gameObject);
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

    private static bool IsVisa(DocumentData doc)
        => doc != null && doc.documentType == "비자";

    private static bool IsPcr(DocumentData doc)
        => doc != null && doc.documentType == "PCR검사서";

    /// <summary>PCR 결과가 양성인가(pcr_result 에 positive/양성 포함). 양성/음성 배경 선택용.</summary>
    private static bool IsPcrPositive(DocumentData doc)
    {
        string r = FieldValue(doc, "pcr_result");
        if (string.IsNullOrEmpty(r)) return false;
        r = r.Trim().ToLowerInvariant();
        return r.Contains("positive") || r.Contains("양성");
    }

    /// <summary>이미지 참조에서 확장자를 떼어 Resources 키로 변환한다(spriteRef="김민준.png" → "김민준").</summary>
    private static string StripExt(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int dot = s.LastIndexOf('.');
        return dot > 0 ? s.Substring(0, dot) : s;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>편집 모드/런타임 양쪽에서 안전하게 파괴한다.</summary>
    private static void SafeDestroy(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(go); return; }
#endif
        UnityEngine.Object.Destroy(go);
    }

    // ── 형식화 매핑 (표시 전용 — 비교 값은 원본 유지) ──────────────

    private static FieldEntry FindField(DocumentData doc, string key)
    {
        if (doc == null || doc.fields == null) return null;
        foreach (FieldEntry f in doc.fields)
        {
            if (f != null && f.key == key) return f;
        }
        return null;
    }

    private static string FieldValue(DocumentData doc, string key)
    {
        FieldEntry f = FindField(doc, key);
        return f != null ? f.value : string.Empty;
    }

    /// <summary>슬롯 표시 문자열 형식화(국적 코드→한글, 날짜→YYYY.MM.DD, 성별→남/여).</summary>
    private static string FormatDisplay(string key, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        switch (key)
        {
            case "nationality": return NationalityKo(raw);
            case "gender":      return GenderShort(raw);
            case "birth_date":
            case "issue_date":
            case "expiry_date": return FormatDate(raw);
            default:            return raw;
        }
    }

    /// <summary>국적 코드 → 한글(짧은 표기). 그 외는 원문.</summary>
    private static string NationalityKo(string code)
    {
        switch (code)
        {
            case "KOR": return "한국";
            case "USA": return "미국";
            case "CHN": return "중국";
            case "JPN": return "일본";
            default:    return code;
        }
    }

    /// <summary>발급 국가명(정식 명칭). 그 외는 원문.</summary>
    private static string CountryName(string code)
    {
        switch (code)
        {
            case "KOR": return "대한민국";
            case "USA": return "미국";
            case "CHN": return "중국";
            case "JPN": return "일본";
            default:    return code;
        }
    }

    /// <summary>성별 표기: "남성"→"남", "여성"→"여". 그 외 원문.</summary>
    private static string GenderShort(string g)
    {
        switch (g)
        {
            case "남성": return "남";
            case "여성": return "여";
            default:     return g;
        }
    }

    /// <summary>날짜 문자열을 YYYY.MM.DD 로 형식화. 파싱 실패 시 원문 유지.</summary>
    private static string FormatDate(string raw)
    {
        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime d))
        {
            return d.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        }
        return raw;
    }
}
