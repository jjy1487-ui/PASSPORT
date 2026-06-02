using System;
using UnityEngine;

/// <summary>
/// 이종 소스(서류/캐릭터/뉴스/대화/규정) 간 교차 대조에서 "클릭해 고를 수 있는 한 항목"의 공통 계약.
/// 표시는 한글 라벨(<see cref="DisplayLabel"/>), 비교는 속성 키(<see cref="AttributeKey"/>)로 한다.
/// 같은 속성 키끼리만 비교하며(관련성), 양쪽 값이 있으면 정규화 일치/불일치, 한쪽이 비면 "관련 있음"만 표시.
///
/// 판정 로직은 없다 — 표시·클릭 통지 전용. 정답/변조 판정은 gameplay 소유.
/// </summary>
public interface ICrossCheckSelectable
{
    /// <summary>소스 종류(한글): "서류"/"캐릭터"/"뉴스"/"대화"/"규정". 표시 보조용.</summary>
    string SourceType { get; }

    /// <summary>대조 속성 키(영문 snake_case). 빈 ""이면 대조 비대상.</summary>
    string AttributeKey { get; }

    /// <summary>비교값. 빈 ""이면 값 비교 불가 → 관련성만(규정/일부 뉴스·대화 단서).</summary>
    string Value { get; }

    /// <summary>이 항목이 손님과 Match/Related 시 잠금 해제하는 스캔("xray"|"fingerprint"). 트리거가 아니면 "".</summary>
    string UnlocksScan { get; }

    /// <summary>화면 표시용 한글 라벨.</summary>
    string DisplayLabel { get; }

    /// <summary>연결선/하이라이트 기준 RectTransform.</summary>
    RectTransform Rect { get; }

    /// <summary>클릭 시 자신을 인자로 발행.</summary>
    event Action<ICrossCheckSelectable> OnSelected;

    /// <summary>선택 하이라이트 on/off.</summary>
    void SetSelected(bool on);
}

/// <summary>
/// 한 소스 뷰가 자신의 selectable 위젯 목록을 노출하기 위한 공급자 계약.
/// <see cref="CrossCheckController"/>가 활성 공급자들을 모아 selectable 을 수집·구독한다.
/// </summary>
public interface ICrossCheckProvider
{
    /// <summary>현재 클릭 가능한 selectable 위젯들(없으면 빈 열거).</summary>
    System.Collections.Generic.IEnumerable<ICrossCheckSelectable> GetSelectables();

    /// <summary>selectable 구성이 바뀌면 발행(컨트롤러가 재수집).</summary>
    event Action OnSelectablesChanged;
}
