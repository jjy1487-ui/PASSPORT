using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 손님 1명을 화면에 표시한다. 초상은 플레이스홀더(색상 박스),
/// 후일 spriteRef로 실제 스프라이트 교체 가능.
///
/// 교차 대조: 이름(attr=name)·얼굴(attr=face) 두 항목을 selectable 로 노출한다(ICrossCheckProvider).
/// 국적·성별 등은 노출하지 않는다(문서 대조 시스템의 전제).
/// </summary>
public sealed class CustomerView : MonoBehaviour, ICrossCheckProvider
{
    [Header("UI 참조")]
    [SerializeField] private Image _portraitPlaceholder;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _infoText;

    [Header("교차 대조 selectable(선택 — 없으면 대조 비참여)")]
    [SerializeField] private CrossCheckItemView _nameSelectable; // 초상 영역 이름(attr=name)
    [SerializeField] private CrossCheckItemView _faceSelectable; // 초상 영역 얼굴(attr=face)

    /// <summary>selectable 구성 변경 통지(손님 교체 시).</summary>
    public event System.Action OnSelectablesChanged;

    /// <summary>손님 정보를 표시한다.</summary>
    /// <param name="customer">손님 데이터.</param>
    /// <param name="faceRef">
    /// 얼굴 이미지 키(photo_ref). 보통 그 손님 여권 문서의 spriteRef 를 넘긴다(같은 인물).
    /// 비어 있으면 customer.spriteRef 로 폴백 시도하고, 로드 실패 시 결정색 플레이스홀더를 쓴다.
    /// </param>
    public void Show(CustomerData customer, string faceRef = null)
    {
        if (customer == null)
        {
            return;
        }

        gameObject.SetActive(true);

        // 초상: 실제 이미지(photo_ref) 로드 시도, 실패하면 customerId 기반 결정색 폴백
        if (_portraitPlaceholder != null)
        {
            string key = !string.IsNullOrEmpty(faceRef) ? faceRef : customer.spriteRef;
            Sprite face = LoadFace(key);
            if (face != null)
            {
                _portraitPlaceholder.sprite = face;
                _portraitPlaceholder.color = Color.white;
                _portraitPlaceholder.preserveAspect = true;
            }
            else
            {
                // 폴백: 기존 결정색 플레이스홀더(이미지 없을 때)
                _portraitPlaceholder.sprite = null;
                _portraitPlaceholder.color = PlaceholderColor(customer.customerId);
            }
        }

        if (_nameText != null)
        {
            _nameText.text = $"{customer.nameKr} ({customer.nameEn})";
        }

        // 초상(이미지) 영역에는 이름만 표시한다.
        // 국적·성별·필요 서류·캐릭터유형 등은 노출하지 않는다 — 플레이어가 서류를 대조해
        // 직접 알아내야 하므로(문서 대조 시스템의 전제). 정보를 미리 주면 대조 의미가 사라진다.
        if (_infoText != null)
        {
            _infoText.text = string.Empty;
            _infoText.gameObject.SetActive(false);
        }

        // 교차 대조 selectable 바인딩(이름은 영문, 얼굴은 sprite_ref 키).
        // 이름 셀렉터는 CustomerName 라벨 위에 겹치는 '투명 클릭 영역'이다.
        // 표시 글자를 비워야(라벨/displayText 모두 "") 이름표와 흰 글자가 겹쳐 보이지 않는다.
        // 대조 판정은 Value(영문이름)로 하므로 표시 글자 없이도 정상 동작.
        if (_nameSelectable != null)
            _nameSelectable.Bind("캐릭터", "name", customer.nameEn, "");
        // 표시 글자는 비운다("얼굴" 라벨은 대조 멘트용으로만 유지). 실제 얼굴 이미지가 깔리므로 글자가 겹치면 안 됨.
        if (_faceSelectable != null)
            _faceSelectable.Bind("캐릭터", "face", customer.spriteRef, "얼굴", "");

        OnSelectablesChanged?.Invoke();
    }

    /// <summary>손님 표시를 숨긴다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (!gameObject.activeInHierarchy) yield break;
        if (_nameSelectable != null) yield return _nameSelectable;
        if (_faceSelectable != null) yield return _faceSelectable;
    }

    /// <summary>Resources/Characters/&lt;key&gt; 에서 얼굴 스프라이트를 로드한다(없으면 null).</summary>
    private static Sprite LoadFace(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return Resources.Load<Sprite>("Characters/" + StripExt(key));
    }

    /// <summary>이미지 참조에서 확장자를 떼어 Resources 키로 변환한다(spriteRef="김민준.png" → "김민준").</summary>
    private static string StripExt(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int dot = s.LastIndexOf('.');
        return dot > 0 ? s.Substring(0, dot) : s;
    }

    /// <summary>id로부터 안정적인 파스텔 색을 만든다.</summary>
    private static Color PlaceholderColor(int id)
    {
        float hue = (id * 0.137f) % 1f; // 황금비 비슷한 분산
        return Color.HSVToRGB(hue, 0.45f, 0.85f);
    }
}
