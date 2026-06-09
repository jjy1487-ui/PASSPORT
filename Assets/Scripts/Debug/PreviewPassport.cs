using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미리보기 전용 샘플 여권. 이름/생년월일/국적을 교차대조 항목으로 제공한다(ICrossCheckProvider).
/// 지문 DB 결과(진짜 신원)와 이 여권(주장 신원)을 플레이어가 직접 대조해볼 수 있게 한다.
/// 행은 템플릿(CrossCheckItemView)을 런타임에 복제해 만든다. UIPreviewController 가 값을 주입한다.
/// </summary>
public sealed class PreviewPassport : MonoBehaviour, ICrossCheckProvider
{
    [SerializeField] private CrossCheckController _crossCheck;
    [SerializeField] private CrossCheckItemView _rowTemplate; // 복제할 행 템플릿(지문 DB 행 등)
    [SerializeField] private RectTransform _container;        // 행을 담을 곳(비우면 자신)
    [SerializeField] private float _rowHeight = 52f;

    public event System.Action OnSelectablesChanged;

    private static readonly string[] Labels = { "이름", "생년월일", "국적" };
    private static readonly string[] Attrs = { "name", "birth_date", "nationality" };
    private readonly CrossCheckItemView[] _rows = new CrossCheckItemView[3];
    private bool _built;

    private void Start()
    {
        Build();
        if (_crossCheck != null) _crossCheck.RegisterProvider(this);
        else Debug.LogWarning("[PreviewPassport] _crossCheck 미연결");
    }

    private void Build()
    {
        if (_built || _rowTemplate == null) return;
        RectTransform parent = _container != null ? _container : (RectTransform)transform;
        for (int i = 0; i < 3; i++)
        {
            CrossCheckItemView row = Instantiate(_rowTemplate, parent);
            row.name = "PassRow_" + Attrs[i];
            var rt = (RectTransform)row.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -58f - i * _rowHeight); // 타이틀 아래부터
            rt.sizeDelta = new Vector2(360f, 46f);
            row.gameObject.SetActive(false);
            _rows[i] = row;
        }
        _built = true;
    }

    /// <summary>여권에 적힌 "주장 신원"을 설정해 표시한다(UIPreviewController 가 케이스별로 호출).</summary>
    public void SetPassport(string name, string birth, string nationality)
    {
        Build();
        string[] vals = { name, birth, nationality };
        for (int i = 0; i < 3; i++)
        {
            if (_rows[i] == null) continue;
            _rows[i].gameObject.SetActive(true);
            _rows[i].Bind("여권", Attrs[i], vals[i], $"{Labels[i]}    {vals[i]}", $"{Labels[i]}    {vals[i]}");
        }
        OnSelectablesChanged?.Invoke();
    }

    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        foreach (var r in _rows)
            if (r != null && r.gameObject.activeInHierarchy) yield return r;
    }
}
