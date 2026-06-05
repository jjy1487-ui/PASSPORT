using System;
using System.Collections.Generic;
using System.Globalization;
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

    [Header("여권 사진(photo_ref)")]
    [Tooltip("펼친 여권의 사진칸. 여권답게 좌측/상단에 배치한다.")]
    [SerializeField] private Image _photoImage; // doc.spriteRef(photo_ref) 로 채움. 없으면 플레이스홀더 유지

    [Header("도장 자국(여권 종이 위)")]
    [SerializeField] private GameObject _stampRoot;
    [SerializeField] private TMP_Text _stampText;

    [Header("여권 펼침 템플릿(documentType==\"여권\" 전용)")]
    [Tooltip("여권 문서는 이 빈 템플릿(라벨이 인쇄된 펼침 이미지)을 깔고 값만 슬롯 위치에 동적 표시한다.\n" +
             "비우면 Resources/Documents/passport_open 을 자동 로드한다. 못 찾으면 기존 일반 렌더로 폴백.")]
    [SerializeField] private Sprite _passportTemplate;
    [Tooltip("여권 슬롯 값 폰트(미연결 시 _fieldTemplate 의 값 폰트를 상속). 서명용 손글씨 폰트는 별도.")]
    [SerializeField] private TMP_FontAsset _passportValueFont;
    [SerializeField] private TMP_FontAsset _passportSignatureFont;
    [Tooltip("여권 값 텍스트 크기(인스펙터에서 조절). 0 이하면 템플릿 기본 크기 유지.")]
    [SerializeField] private float _passportValueFontSize = 15f;

    [Header("여권 펼침뷰 — 필드 위치 (좌상단 0~1 비율, 인스펙터에서 직접 조정 가능)")]
    [Tooltip("각 값/파생텍스트의 위치를 비율로 지정. 에디트 모드에서 바꾸면 다음 플레이에 반영된다.")]
    [SerializeField] private Vector2 _posName         = new Vector2(0.126f, 0.476f);
    [SerializeField] private Vector2 _posBirthDate    = new Vector2(0.126f, 0.624f);
    [SerializeField] private Vector2 _posGender       = new Vector2(0.126f, 0.769f);
    [SerializeField] private Vector2 _posPassportNo   = new Vector2(0.580f, 0.196f);
    [SerializeField] private Vector2 _posNationality  = new Vector2(0.580f, 0.320f);
    [SerializeField] private Vector2 _posIssueDate    = new Vector2(0.580f, 0.453f);
    [SerializeField] private Vector2 _posExpiryDate   = new Vector2(0.758f, 0.453f);
    [SerializeField] private Vector2 _posIssueCountry = new Vector2(0.580f, 0.611f); // 발급 국가(Derived)
    [SerializeField] private Vector2 _posSignature    = new Vector2(0.580f, 0.769f); // 서명(Signature)
    [Tooltip("발급 국가(Derived) 텍스트 미세 위치 보정(anchoredPosition px).")]
    [SerializeField] private Vector2 _offsetIssueCountry = new Vector2(53f, -10f);  // Derived
    [Tooltip("서명(Signature) 텍스트 미세 위치 보정(anchoredPosition px).")]
    [SerializeField] private Vector2 _offsetSignature    = new Vector2(49f, -3f);   // Signature
    [Tooltip("발급일/만료일 슬롯 폭(px). 좁혀서 겹침 방지.")]
    [SerializeField] private float _dateSlotWidth     = 120f;

    [Header("비자 펼침 템플릿(documentType==\"비자\" 전용)")]
    [Tooltip("비자 문서는 이 템플릿(visa_open) 위에 값만 슬롯 위치에 표시. 비우면 Resources/Documents/visa_open 자동 로드.")]
    [SerializeField] private Sprite _visaTemplate;
    [Tooltip("비자 닫힘(트레이) 아이콘. 비우면 Resources/Documents/visa_closed 자동 로드.")]
    [SerializeField] private Sprite _visaClosedCover;

    [Header("비자 펼침뷰 — 필드 위치 (좌상단 0~1 비율, 인스펙터에서 직접 조정 가능)")]
    [SerializeField] private Vector2 _posVisaName        = new Vector2(0.42f, 0.305f); // 이름
    [SerializeField] private Vector2 _posVisaNationality = new Vector2(0.42f, 0.380f); // 국적
    [SerializeField] private Vector2 _posVisaPassportNo  = new Vector2(0.42f, 0.455f); // 여권번호
    [SerializeField] private Vector2 _posVisaNo          = new Vector2(0.42f, 0.530f); // 비자번호
    [SerializeField] private Vector2 _posVisaType        = new Vector2(0.42f, 0.605f); // 방문목적(visa_type)
    [SerializeField] private Vector2 _posVisaIssueDate   = new Vector2(0.42f, 0.680f); // 발급일
    [SerializeField] private Vector2 _posVisaExpiryDate  = new Vector2(0.42f, 0.755f); // 만료일
    [Tooltip("비자 일반 값(이름·국적·여권번호·비자번호·방문목적) 가로 위치 보정(anchoredPosition.x px).")]
    [SerializeField] private float _visaValueOffsetX = -100f;
    [Tooltip("비자 발급일 가로 위치 보정(anchoredPosition.x px).")]
    [SerializeField] private float _visaIssueOffsetX = -66f;
    [Tooltip("비자 만료일 가로 위치 보정(anchoredPosition.x px).")]
    [SerializeField] private float _visaExpiryOffsetX = -115f;
    [Tooltip("비자 발급일·만료일 슬롯 폭(px).")]
    [SerializeField] private float _visaDateWidth = 180f;

    [Header("PCR 펼침 템플릿(documentType==\"PCR검사서\" — 결과별 2종)")]
    [Tooltip("음성 템플릿. 비우면 Resources/Documents/pcr_open_negative 자동 로드.")]
    [SerializeField] private Sprite _pcrTemplateNegative;
    [Tooltip("양성 템플릿. 비우면 Resources/Documents/pcr_open_positive 자동 로드.")]
    [SerializeField] private Sprite _pcrTemplatePositive;
    [Tooltip("PCR 닫힘 아이콘. 비우면 Resources/Documents/pcr_closed 자동 로드.")]
    [SerializeField] private Sprite _pcrClosedCover;

    [Header("PCR 펼침뷰 — 필드 위치 (좌상단 0~1 비율, 검사결과는 이미지 체크박스라 미표시)")]
    [SerializeField] private Vector2 _posPcrName        = new Vector2(0.42f, 0.298f); // 이름
    [SerializeField] private Vector2 _posPcrNationality = new Vector2(0.42f, 0.366f); // 국적
    [SerializeField] private Vector2 _posPcrTestNo      = new Vector2(0.42f, 0.432f); // 검사번호
    [SerializeField] private Vector2 _posPcrIssueDate   = new Vector2(0.42f, 0.501f); // 검사일
    [SerializeField] private Vector2 _posPcrValidUntil  = new Vector2(0.42f, 0.727f); // 유효기간
    [SerializeField] private Vector2 _posPcrLabName     = new Vector2(0.42f, 0.793f); // 검사기관
    [Tooltip("PCR 값 가로 위치 보정(anchoredPosition.x px).")]
    [SerializeField] private float _pcrValueOffsetX = 0f;

    private readonly List<DocumentFieldView> _fieldRows = new List<DocumentFieldView>();

    // 여권 전용으로 생성한 슬롯들의 부모(재바인딩 시 통째로 정리).
    private GameObject _passportSlotRoot;

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

        // 여권 전 슬롯/사진을 매 바인딩마다 정리(손님 교체 대응).
        ClearPassportSlots();

        // ── 여권 전용 펼침 렌더 ──────────────────────────────────────
        // documentType=="여권" 이고 템플릿 스프라이트를 확보하면, 빈 템플릿을 깔고
        // 각 값을 라벨 위치에 맞춰 동적 슬롯(DocumentFieldView)으로 표시한다.
        // 슬롯은 그대로 클릭 대조 대상이며, 표시는 형식화·비교는 원본 값으로 분리한다.
        if (IsPassport(doc) && TryBuildPassportOpenView(doc))
        {
            // 일반 렌더 경로(사진/필드행/본문/배경색)는 건너뛰고 도장만 정리.
            if (_background != null) _background.color = new Color(1f, 1f, 1f, 0f); // 여권 배경 투명(살색 제거)
            BuildClosedCover(doc);
            HideStamp();
            return;
        }

        // ── 비자 전용 펼침 렌더 ──────────────────────────────────────
        // documentType=="비자" 이고 템플릿을 확보하면, 여권과 동일하게 빈 폼 위에 값만 슬롯으로 표시.
        if (IsVisa(doc) && TryBuildVisaOpenView(doc))
        {
            if (_background != null) _background.color = new Color(1f, 1f, 1f, 0f); // 비자 배경 투명(살색 제거)
            BuildClosedCover(doc);
            HideStamp();
            return;
        }

        // ── PCR 검사서 전용 펼침 렌더(결과별 양성/음성 템플릿) ──────────
        if (IsPcr(doc) && TryBuildPcrOpenView(doc))
        {
            if (_background != null) _background.color = new Color(1f, 1f, 1f, 0f); // 배경 투명
            BuildClosedCover(doc);
            HideStamp();
            return;
        }

        // ── 일반(비여권) 렌더: 기존 동작 그대로 ─────────────────────
        // 필드행을 먼저 만든 뒤 사진 슬롯을 얹는다 — BuildFieldRows 가 _fieldRows 를 비우므로,
        // 사진(얼굴) 대조 슬롯은 그 뒤에 추가해야 살아남는다.
        BuildFieldRows(doc);

        BuildPhoto(doc);

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

        BuildClosedCover(doc);

        HideStamp();
    }

    /// <summary>접힌 표지: 여권=국가별 이미지, 비자=비자 아이콘(없으면 갈색 책자 + 종류명).</summary>
    private void BuildClosedCover(DocumentData doc)
    {
        Sprite cover = IsVisa(doc)
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
    /// 여권 사진칸을 doc.spriteRef(photo_ref) 로 채운다.
    /// 사진칸 Image 가 인스펙터에 연결돼 있지 않으면 펼친 여권 좌상단에 자동 생성한다(씬 수정 불필요).
    /// 스프라이트를 못 찾으면 사진칸을 숨겨 기존 모습을 유지한다(가드).
    /// 표시 전용 — 대조/판정과 무관하다.
    /// </summary>
    private void BuildPhoto(DocumentData doc)
    {
        Sprite photo = !string.IsNullOrEmpty(doc.spriteRef)
            ? Resources.Load<Sprite>("Characters/" + StripExt(doc.spriteRef))
            : null;

        if (photo == null)
        {
            // 사진이 없으면 사진칸을 숨긴다(고지서·비여권 등은 사진칸 불필요).
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

    /// <summary>
    /// 사진칸(_photoImage) 위에 투명 클릭 슬롯(DocumentFieldView)을 얹어 attr="face" 대조를 가능하게 한다.
    /// value=doc.spriteRef / key="face" → 손님 얼굴(attr="face", value=customer.spriteRef)과 일치/불일치 대조.
    /// 템플릿 루트가 전면 raycast(투명) 영역이라 사진칸 전체가 클릭된다.
    /// _fieldRows 에 등록되어 DocumentView.GetSelectables() 가 수집한다. 표시 전용 — 판정/점수 무영향.
    /// </summary>
    private void AddFaceHitSlot(RectTransform photoRect, DocumentData doc)
    {
        if (_fieldTemplate == null || photoRect == null
            || doc == null || string.IsNullOrEmpty(doc.spriteRef)) return;

        DocumentFieldView row = Instantiate(_fieldTemplate, photoRect);
        row.gameObject.SetActive(true);

        RectTransform rt = row.transform as RectTransform;
        StretchFill(rt); // 사진칸 전체를 덮는다.

        // 라벨/표시 글자는 비운다(사진 위 글자 겹침 방지). 비교: value=spriteRef / key="face".
        row.Bind(doc.documentType, string.Empty, doc.spriteRef, "face", string.Empty);

        _fieldRows.Add(row);
    }

    /// <summary>
    /// 펼친 여권(OpenView)이 있으면 그 좌상단에, 없으면 카드 루트에 사진칸 Image 를 자동 생성한다.
    /// 여권답게 좌측 상단(앵커 0.08~0.40 / 0.60~0.90)에 배치. 클릭 대조(필드 행)를 막지 않도록 raycast off.
    /// </summary>
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

        // 종이 배경 바로 위(필드 텍스트 아래)에 깔아 글자를 가리지 않게 한다.
        rt.SetSiblingIndex(1);

        Image img = go.GetComponent<Image>();
        img.raycastTarget = false; // 사진 위 클릭이 필드 대조를 가리지 않게
        return img;
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

    // ════════════════════════════════════════════════════════════════
    //  여권 전용 펼침 렌더 (documentType=="여권")
    //  빈 템플릿(passport_open) 위에 값만 슬롯 위치에 동적 표시.
    //  표시는 형식화, 대조 비교는 원본 값(DocumentFieldView 표시/비교 분리).
    // ════════════════════════════════════════════════════════════════

    private static bool IsPassport(DocumentData doc)
        => doc != null && doc.documentType == "여권";

    private static bool IsVisa(DocumentData doc)
        => doc != null && doc.documentType == "비자";

    private static bool IsPcr(DocumentData doc)
        => doc != null && doc.documentType == "PCR검사서";

    /// <summary>PCR 결과가 양성인가(pcr_result 에 positive/양성 포함). 양성/음성 템플릿 선택용.</summary>
    private static bool IsPcrPositive(DocumentData doc)
    {
        string r = FieldValue(doc, "pcr_result");
        if (string.IsNullOrEmpty(r)) return false;
        r = r.Trim().ToLowerInvariant();
        return r.Contains("positive") || r.Contains("양성");
    }

    /// <summary>여권 펼침 템플릿 + 동적 슬롯을 구성한다. 성공하면 true(일반 렌더 생략).</summary>
    private bool TryBuildPassportOpenView(DocumentData doc)
    {
        Sprite template = _passportTemplate != null
            ? _passportTemplate
            : Resources.Load<Sprite>("Documents/passport_open");
        if (template == null)
        {
            // 템플릿을 못 찾으면 일반 렌더로 폴백(false 반환).
            Debug.LogWarning("[DocumentCardView] 여권 펼침 템플릿(Resources/Documents/passport_open)을 찾지 못해 일반 렌더로 폴백합니다.");
            return false;
        }

        Transform openView = transform.Find("OpenView");
        if (openView == null) openView = transform; // 폴백: 카드 루트

        // 일반 렌더가 만든 잔재(필드행/본문/자동 사진)를 숨겨 템플릿만 보이게 한다.
        ClearFieldRows();
        if (_fieldContainer != null) _fieldContainer.gameObject.SetActive(false);
        if (_bodyText != null) _bodyText.gameObject.SetActive(false);
        if (_photoImage != null) _photoImage.gameObject.SetActive(false);

        // 슬롯 루트(템플릿 배경 + 모든 값 슬롯). OpenView 를 꽉 채운다.
        _passportSlotRoot = new GameObject("PassportOpen", typeof(RectTransform));
        RectTransform rootRt = _passportSlotRoot.GetComponent<RectTransform>();
        rootRt.SetParent(openView, false);
        StretchFill(rootRt);
        rootRt.SetSiblingIndex(0); // 종이 배경처럼 맨 뒤(다른 UI 아래)

        // 템플릿 배경 이미지.
        GameObject bg = new GameObject("Template", typeof(RectTransform), typeof(Image));
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.SetParent(rootRt, false);
        StretchFill(bgRt);
        // 템플릿을 컨테이너 안에 '비율 유지'로 맞춘다(넘침 방지). 이렇게 하면 bgRt 자체가
        // 여권 그림과 같은 비율의 사각형이 되므로, 값 슬롯을 bgRt 비율좌표에 얹으면 라벨과 정확히 정렬된다.
        AspectRatioFitter bgFit = bg.AddComponent<AspectRatioFitter>();
        bgFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        bgFit.aspectRatio = template.rect.height > 0f ? template.rect.width / template.rect.height : 1.5f;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = template;
        bgImg.color = Color.white;
        bgImg.preserveAspect = false; // bgRt 비율이 이미 그림과 동일하므로 꽉 채워도 왜곡 없음
        bgImg.raycastTarget = false;

        // ── 사진(좌상 사진칸) ─ 데이터 파생, 비대조 ────────────────
        Sprite photo = !string.IsNullOrEmpty(doc.spriteRef)
            ? Resources.Load<Sprite>("Characters/" + StripExt(doc.spriteRef))
            : null;
        // 사진/값 슬롯은 모두 '템플릿(bgRt)'의 자식으로 얹어, 라벨이 인쇄된 그림과 같은 좌표계를 공유한다.
        BuildPassportPhoto(bgRt, doc, photo);

        // ── 대조 가능한 값 슬롯 ──────────────────────────────────
        // (normalized 좌상 원점 비율; AddPassportSlot 내부에서 Unity y 상향으로 변환)
        // 라벨 텍스트는 템플릿에 인쇄돼 있으므로 빈 문자열, 표시 글자는 값만.
        // 각 값을 템플릿의 해당 라벨 위치에 맞춰 얹는다(라벨 자리를 값이 덮음).
        AddPassportSlot(bgRt, doc, "name",        _posName);        // 이름
        AddPassportSlot(bgRt, doc, "birth_date",  _posBirthDate);   // 생년월일
        AddPassportSlot(bgRt, doc, "gender",      _posGender);      // 성별
        AddPassportSlot(bgRt, doc, "passport_no", _posPassportNo);  // 여권 번호
        AddPassportSlot(bgRt, doc, "nationality", _posNationality); // 국적
        AddPassportSlot(bgRt, doc, "issue_date",  _posIssueDate,  _dateSlotWidth); // 발급일
        AddPassportSlot(bgRt, doc, "expiry_date", _posExpiryDate, _dateSlotWidth); // 만료일

        // ── 데이터 파생값(비대조) ─ 발급 국가 / 서명 ───────────────
        string nat = FieldValue(doc, "nationality");
        AddDerivedText(bgRt, CountryName(nat), _posIssueCountry, false, _offsetIssueCountry); // 발급 국가(Derived)
        AddDerivedText(bgRt, FieldValue(doc, "name"), _posSignature, true, _offsetSignature); // 서명(Signature)

        return true;
    }

    /// <summary>비자 펼침 템플릿(visa_open) + 동적 슬롯을 구성한다. 성공하면 true(일반 렌더 생략).
    /// 여권과 동일 방식 — 빈 폼 위에 값만 라벨 위치에 얹는다. 비자엔 사진칸이 없다.</summary>
    private bool TryBuildVisaOpenView(DocumentData doc)
    {
        Sprite template = _visaTemplate != null
            ? _visaTemplate
            : Resources.Load<Sprite>("Documents/visa_open");
        if (template == null)
        {
            Debug.LogWarning("[DocumentCardView] 비자 펼침 템플릿(Resources/Documents/visa_open)을 찾지 못해 일반 렌더로 폴백합니다.");
            return false;
        }

        Transform openView = transform.Find("OpenView");
        if (openView == null) openView = transform;

        ClearFieldRows();
        if (_fieldContainer != null) _fieldContainer.gameObject.SetActive(false);
        if (_bodyText != null) _bodyText.gameObject.SetActive(false);
        if (_photoImage != null) _photoImage.gameObject.SetActive(false);

        _passportSlotRoot = new GameObject("VisaOpen", typeof(RectTransform));
        RectTransform rootRt = _passportSlotRoot.GetComponent<RectTransform>();
        rootRt.SetParent(openView, false);
        StretchFill(rootRt);
        rootRt.SetSiblingIndex(0); // 종이 배경처럼 맨 뒤

        GameObject bg = new GameObject("Template", typeof(RectTransform), typeof(Image));
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.SetParent(rootRt, false);
        StretchFill(bgRt);
        AspectRatioFitter bgFit = bg.AddComponent<AspectRatioFitter>();
        bgFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        bgFit.aspectRatio = template.rect.height > 0f ? template.rect.width / template.rect.height : 1f;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = template;
        bgImg.color = Color.white;
        bgImg.preserveAspect = false;
        bgImg.raycastTarget = false;

        // 폼 7행 값 슬롯(라벨은 템플릿 인쇄, 값만 얹음 / 클릭 대조 가능).
        AddPassportSlot(bgRt, doc, "name",        _posVisaName,        width: 320f, anchoredOffsetX: -157f); // 이름
        AddPassportSlot(bgRt, doc, "nationality", _posVisaNationality, anchoredOffsetX: _visaValueOffsetX); // 국적
        AddPassportSlot(bgRt, doc, "passport_no", _posVisaPassportNo,  anchoredOffsetX: _visaValueOffsetX); // 여권번호
        AddPassportSlot(bgRt, doc, "visa_no",     _posVisaNo,          anchoredOffsetX: _visaValueOffsetX); // 비자번호
        AddPassportSlot(bgRt, doc, "visa_type",   _posVisaType,        anchoredOffsetX: _visaValueOffsetX); // 방문목적
        AddPassportSlot(bgRt, doc, "issue_date",  _posVisaIssueDate,  _visaDateWidth, _visaIssueOffsetX);  // 발급일
        AddPassportSlot(bgRt, doc, "expiry_date", _posVisaExpiryDate, _visaDateWidth, _visaExpiryOffsetX); // 만료일

        return true;
    }

    /// <summary>PCR 검사서 펼침뷰. 결과(양성/음성)에 따라 템플릿이 다르고 V체크는 템플릿에 포함된다.
    /// 이름·국적·검사번호·검사일·유효기간·검사기관 값만 폼 위에 올린다(검사결과는 이미지 체크박스로 표시).</summary>
    private bool TryBuildPcrOpenView(DocumentData doc)
    {
        bool positive = IsPcrPositive(doc);
        Sprite template = positive
            ? (_pcrTemplatePositive != null ? _pcrTemplatePositive : Resources.Load<Sprite>("Documents/pcr_open_positive"))
            : (_pcrTemplateNegative != null ? _pcrTemplateNegative : Resources.Load<Sprite>("Documents/pcr_open_negative"));
        if (template == null)
        {
            Debug.LogWarning("[DocumentCardView] PCR 펼침 템플릿(Resources/Documents/pcr_open_*)을 찾지 못해 일반 렌더로 폴백합니다.");
            return false;
        }

        Transform openView = transform.Find("OpenView");
        if (openView == null) openView = transform;

        ClearFieldRows();
        if (_fieldContainer != null) _fieldContainer.gameObject.SetActive(false);
        if (_bodyText != null) _bodyText.gameObject.SetActive(false);
        if (_photoImage != null) _photoImage.gameObject.SetActive(false);

        _passportSlotRoot = new GameObject("PcrOpen", typeof(RectTransform));
        RectTransform rootRt = _passportSlotRoot.GetComponent<RectTransform>();
        rootRt.SetParent(openView, false);
        StretchFill(rootRt);
        rootRt.SetSiblingIndex(0);

        GameObject bg = new GameObject("Template", typeof(RectTransform), typeof(Image));
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.SetParent(rootRt, false);
        StretchFill(bgRt);
        AspectRatioFitter bgFit = bg.AddComponent<AspectRatioFitter>();
        bgFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        bgFit.aspectRatio = template.rect.height > 0f ? template.rect.width / template.rect.height : 1f;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = template;
        bgImg.color = Color.white;
        bgImg.preserveAspect = false;
        bgImg.raycastTarget = false;

        AddPassportSlot(bgRt, doc, "name",        _posPcrName,        anchoredOffsetX: _pcrValueOffsetX); // 이름
        AddPassportSlot(bgRt, doc, "nationality", _posPcrNationality, anchoredOffsetX: _pcrValueOffsetX); // 국적
        AddPassportSlot(bgRt, doc, "test_no",     _posPcrTestNo,      anchoredOffsetX: _pcrValueOffsetX); // 검사번호
        AddPassportSlot(bgRt, doc, "issue_date",  _posPcrIssueDate,   anchoredOffsetX: _pcrValueOffsetX); // 검사일
        AddPassportSlot(bgRt, doc, "valid_until", _posPcrValidUntil,  anchoredOffsetX: _pcrValueOffsetX); // 유효기간
        AddPassportSlot(bgRt, doc, "lab_name",    _posPcrLabName,     anchoredOffsetX: _pcrValueOffsetX); // 검사기관

        return true;
    }

    /// <summary>
    /// 여권 사진칸(좌상). 사진을 표시하면서 동시에 클릭 대조(attr="face") 슬롯으로 만든다.
    /// _fieldTemplate(DocumentFieldView)을 사진칸 위치에 얹어 value=doc.spriteRef / key="face" 로 바인딩하면,
    /// CustomerView 의 얼굴 selectable(attr="face", value=customer.spriteRef)과 대조된다 —
    /// 같은 인물이면 spriteRef 가 같아 Match, 사진 바꿔치기면 Mismatch.
    /// spriteRef 가 없으면(사진 불필요 서류) 기존처럼 비대조 플레이스홀더만 둔다. 표시 전용 — 판정/점수 무영향.
    /// </summary>
    private void BuildPassportPhoto(RectTransform root, DocumentData doc, Sprite photo)
    {
        // 사진칸 중심 ≈(0.19,0.31), 크기 ≈(0.14×0.25) — 좌상 원점 비율 → Unity 앵커(좌하 원점).
        float halfW = 0.14f * 0.5f, halfH = 0.25f * 0.5f;
        float cx = 0.19f, cyTop = 0.31f;
        Vector2 aMin = new Vector2(cx - halfW, 1f - (cyTop + halfH));
        Vector2 aMax = new Vector2(cx + halfW, 1f - (cyTop - halfH));

        // spriteRef 가 있으면 사진칸 자체를 클릭 대조 슬롯으로 만든다(템플릿 루트가 전면 raycast 영역).
        if (doc != null && !string.IsNullOrEmpty(doc.spriteRef) && _fieldTemplate != null)
        {
            DocumentFieldView row = Instantiate(_fieldTemplate, root);
            row.gameObject.SetActive(true);

            RectTransform rrt = row.transform as RectTransform;
            rrt.anchorMin = aMin;
            rrt.anchorMax = aMax;
            rrt.offsetMin = Vector2.zero;
            rrt.offsetMax = Vector2.zero;

            // 라벨/표시 글자는 비운다(사진 이미지로 대체, 글자 겹침 방지). 비교: value=spriteRef / key="face".
            row.Bind(doc.documentType, string.Empty, doc.spriteRef, "face", string.Empty);

            // 사진(또는 플레이스홀더)을 슬롯 위에 깔아 보여준다. 클릭은 템플릿 루트의 전면 raycast 가 받는다.
            PaintFacePhoto(rrt, photo);

            _fieldRows.Add(row);
            return;
        }

        // 폴백: spriteRef 없음/템플릿 미연결 → 기존처럼 비대조 사진/플레이스홀더만 표시.
        var go = new GameObject("Photo", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = go.GetComponent<Image>();
        img.raycastTarget = false;
        PaintPhotoImage(img, photo);
    }

    /// <summary>사진칸 슬롯 안에 사진(또는 플레이스홀더) 이미지를 깐다(선택 테두리/값 텍스트보다 뒤).</summary>
    private static void PaintFacePhoto(RectTransform slot, Sprite photo)
    {
        var go = new GameObject("PhotoImage", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(slot, false);
        StretchFill(rt);
        rt.SetSiblingIndex(0); // 선택 테두리(Highlight)·값 텍스트보다 뒤로(사진이 가리지 않게)

        Image img = go.GetComponent<Image>();
        img.raycastTarget = true; // 사진칸 클릭 = 대조 선택(템플릿 루트 raycast 와 함께)
        PaintPhotoImage(img, photo);
    }

    /// <summary>사진 스프라이트가 있으면 표시, 없으면 옅은 회색 플레이스홀더로 칠한다.</summary>
    private static void PaintPhotoImage(Image img, Sprite photo)
    {
        if (img == null) return;
        if (photo != null)
        {
            img.sprite = photo;
            img.color = Color.white;
            img.preserveAspect = true;
        }
        else
        {
            // 플레이스홀더: 옅은 회색 빈칸.
            img.sprite = null;
            img.color = new Color(0.85f, 0.85f, 0.85f, 0.6f);
        }
    }

    /// <summary>
    /// 대조 가능한 여권 값 슬롯 1개를 생성한다(DocumentFieldView 클론).
    /// 라벨은 빈 문자열(템플릿 인쇄), 표시는 형식화 값, 비교는 원본 값.
    /// 해당 key 의 필드가 없으면 슬롯을 만들지 않는다(가드).
    /// </summary>
    private void AddPassportSlot(RectTransform root, DocumentData doc, string key, Vector2 topLeftNorm, float width = 260f, float anchoredOffsetX = 0f)
    {
        FieldEntry f = FindField(doc, key);
        if (f == null || _fieldTemplate == null) return;

        DocumentFieldView row = Instantiate(_fieldTemplate, root);
        row.gameObject.SetActive(true);

        RectTransform rt = row.transform as RectTransform;
        PlaceAtNorm(rt, topLeftNorm, width);
        if (anchoredOffsetX != 0f) rt.anchoredPosition = new Vector2(anchoredOffsetX, rt.anchoredPosition.y); // 가로 미세 보정

        // 라벨 비움(템플릿 인쇄), 표시=형식화 / 비교=원본 값.
        row.Bind(doc.documentType, string.Empty, f.value, f.key, FormatDisplay(key, f.value));
        ApplyPassportValueFont(row, _passportValueFont);
        ApplyPassportValueStyle(row, _passportValueFontSize); // 크기 + 왼쪽 정렬
        _fieldRows.Add(row);
    }

    /// <summary>여권 값 텍스트의 크기/정렬을 통일한다(크기 0 이하면 크기 유지).</summary>
    private static void ApplyPassportValueStyle(DocumentFieldView row, float size)
    {
        if (row == null) return;
        foreach (TMP_Text t in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.IsNullOrEmpty(t.text)) continue; // 라벨(빈칸)은 건너뜀
            if (size > 0f) t.fontSize = size;
            t.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }

    /// <summary>이미지 참조에서 확장자를 떼어 Resources 키로 변환한다(spriteRef="김민준.png" → "김민준").</summary>
    private static string StripExt(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int dot = s.LastIndexOf('.');
        return dot > 0 ? s.Substring(0, dot) : s;
    }

    /// <summary>데이터 파생값(발급 국가/서명) — 비대조 순수 텍스트.</summary>
    private void AddDerivedText(RectTransform root, string text, Vector2 topLeftNorm, bool signature, Vector2 anchoredOffset)
    {
        if (string.IsNullOrEmpty(text)) return;

        var go = new GameObject(signature ? "Signature" : "Derived", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        PlaceAtNorm(rt, topLeftNorm);
        rt.anchoredPosition = anchoredOffset; // 미세 위치 보정(px)

        TMP_Text label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = signature ? 20f : 15f;
        label.color = new Color(0.10f, 0.10f, 0.10f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        TMP_FontAsset font = signature ? _passportSignatureFont : _passportValueFont;
        if (font == null) font = TemplateValueFont(); // 한글 폰트 폴백(기본 LiberationSans 는 한글 글리프 없음 → 발급국가 빈칸 방지)
        if (font != null) label.font = font;
    }

    /// <summary>대조 슬롯 템플릿(_fieldTemplate)의 값 폰트를 가져온다(한글 지원 폰트 폴백용). 없으면 null.</summary>
    private TMP_FontAsset TemplateValueFont()
    {
        if (_fieldTemplate == null) return null;
        foreach (TMP_Text t in _fieldTemplate.GetComponentsInChildren<TMP_Text>(true))
            if (t != null && t.font != null) return t.font;
        return null;
    }

    /// <summary>좌상 원점 비율 좌표를 Unity(좌하 원점) 앵커로 변환해 슬롯을 좌측 정렬 배치한다.</summary>
    private static void PlaceAtNorm(RectTransform rt, Vector2 topLeftNorm, float width = 260f)
    {
        if (rt == null) return;
        float ax = topLeftNorm.x;
        float ay = 1f - topLeftNorm.y; // y 상향 변환
        rt.anchorMin = new Vector2(ax, ay);
        rt.anchorMax = new Vector2(ax, ay);
        rt.pivot = new Vector2(0f, 0.5f); // 좌측 정렬(라벨 바로 아래, 좌측 기준)
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, 40f);
    }

    /// <summary>슬롯의 값 TMP 폰트를 여권용으로 교체(미지정 시 템플릿 폰트 유지).</summary>
    private static void ApplyPassportValueFont(DocumentFieldView row, TMP_FontAsset font)
    {
        if (row == null || font == null) return;
        foreach (TMP_Text t in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!string.IsNullOrEmpty(t.text)) t.font = font;
        }
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>여권 전용 슬롯 루트를 통째로 제거한다(재바인딩/손님 교체 대응).</summary>
    private void ClearPassportSlots()
    {
        if (_passportSlotRoot != null)
        {
            SafeDestroy(_passportSlotRoot);
            _passportSlotRoot = null;
        }
        // 추적되지 않은 잔재(에디터 미리보기/이전 빌드)도 제거해 중복 표시를 막는다.
        Transform ov = transform.Find("OpenView");
        if (ov != null)
        {
            for (int i = ov.childCount - 1; i >= 0; i--)
            {
                Transform c = ov.GetChild(i);
                if (c != null && c.name == "PassportOpen") SafeDestroy(c.gameObject);
            }
        }
        // 슬롯 안의 DocumentFieldView 도 _fieldRows 에 들어가 있으므로 함께 정리.
        ClearFieldRows();

        // 일반 렌더 잔재를 다시 켤 수 있게 활성화 복구(비여권 손님 대응).
        if (_fieldContainer != null) _fieldContainer.gameObject.SetActive(true);
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

#if UNITY_EDITOR
    // ── 에디터 전용: 여권 펼침뷰를 샘플 데이터로 미리보기(보면서 편집용) ──
    // 컴포넌트 우클릭 메뉴에서 호출. 런타임 Bind 시 ClearPassportSlots 가 자동 정리하므로 안전.
    [ContextMenu("여권 펼침뷰 미리보기 (샘플)")]
    private void EditorPreviewPassport()
    {
        var doc = new DocumentData
        {
            documentType = "여권",
            country = "KOR",
            spriteRef = "",
            fields = new FieldEntry[]
            {
                new FieldEntry { key = "name",        label = "이름",     value = "홍길동" },
                new FieldEntry { key = "birth_date",  label = "생년월일", value = "1990.01.01" },
                new FieldEntry { key = "gender",      label = "성별",     value = "남성" },
                new FieldEntry { key = "passport_no", label = "여권번호", value = "AB1234567" },
                new FieldEntry { key = "nationality", label = "국적",     value = "KOR" },
                new FieldEntry { key = "issue_date",  label = "발급일",   value = "2020.01.01" },
                new FieldEntry { key = "expiry_date", label = "만료일",   value = "2030.01.01" },
            }
        };
        Bind(doc);

        // 미리보기는 '펼친' 상태로 보여준다(접힌 표지 ClosedView 숨김).
        var pd = GetComponent<PassportDocument>();
        if (pd != null)
        {
            pd.SetOpen(true);
        }
        else
        {
            Transform open = transform.Find("OpenView");
            Transform closed = transform.Find("ClosedView");
            if (open != null) open.gameObject.SetActive(true);
            if (closed != null) closed.gameObject.SetActive(false);
        }
        UnityEditor.SceneView.RepaintAll();
    }

    [ContextMenu("여권 펼침뷰 미리보기 지우기")]
    private void EditorClearPassportPreview()
    {
        ClearPassportSlots();
        UnityEditor.SceneView.RepaintAll();
    }
#endif

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
