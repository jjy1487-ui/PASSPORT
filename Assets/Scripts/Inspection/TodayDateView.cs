using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 화면 좌측하단에 "오늘: yyyy-MM-dd"를 작게 표시하고, 그 날짜를 교차 대조 소스로 노출한다(ICrossCheckProvider).
/// 날짜는 <see cref="InspectionController.CurrentDate"/>에서 런타임 계산(일차당 고정).
/// 클릭 대조 selectable 1개(SourceType="오늘", attr="today", value=오늘 날짜)를 제공해
/// 만료일/발급일과 날짜 비교(만료됨/유효)를 가능하게 한다.
///
/// 판정/점수 로직 없음 — 표시·대조 단서 노출 전용. 정답 판정은 gameplay 소유.
/// </summary>
public sealed class TodayDateView : MonoBehaviour, ICrossCheckProvider
{
    [Header("대상")]
    [SerializeField] private InspectionController _controller;

    [Header("UI 참조")]
    [SerializeField] private TMP_Text _dateText;                 // "오늘: 2026-06-01"

    [Header("교차 대조 selectable")]
    [SerializeField] private CrossCheckItemView _todaySelectable; // SourceType="오늘", attr="today"

    /// <summary>selectable 구성 변경 통지(일차/손님 교체 시).</summary>
    public event System.Action OnSelectablesChanged;

    private void Start()
    {
        if (_controller != null) _controller.OnCustomerChanged += HandleCustomerChanged;
        else Debug.LogWarning("[TodayDateView] _controller 가 연결되지 않았습니다.");

        Refresh();
    }

    private void OnDestroy()
    {
        if (_controller != null) _controller.OnCustomerChanged -= HandleCustomerChanged;
    }

    private void HandleCustomerChanged() => Refresh();

    /// <summary>오늘 날짜를 다시 읽어 표시·selectable 값을 갱신한다.</summary>
    private void Refresh()
    {
        string date = _controller != null ? _controller.CurrentDate : string.Empty;

        if (_dateText != null) _dateText.text = string.IsNullOrEmpty(date) ? "오늘: -" : $"오늘: {date}";

        // selectable 은 투명 클릭 오버레이 — 표시 텍스트는 비워 _dateText 가 보이게 한다.
        if (_todaySelectable != null)
            _todaySelectable.Bind("오늘", "today", date, "오늘 날짜", " ");

        OnSelectablesChanged?.Invoke();
    }

    // ── ICrossCheckProvider ──────────────────────────────────────
    public IEnumerable<ICrossCheckSelectable> GetSelectables()
    {
        if (_todaySelectable != null && _todaySelectable.gameObject.activeInHierarchy)
            yield return _todaySelectable;
    }
}
