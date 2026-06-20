using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 분기 시나리오(노드그래프) FSM 러너 — day12 사토 하루키(테러범)처럼 도장 판정이 아니라
/// 대사·선택지로 결말이 갈리는 손님 전용 흐름을 굴린다(SHARED-CONVENTIONS 4-A 의 특수 흐름).
///
/// 진행:
///   start 노드 → 현재 노드 lines 재생(기존 DialogueView 메커니즘 재사용) → 끝나면:
///     ① choices 있으면 → 선택 버튼 UI(RejectConfirmPopup 재활용, 항상 2지선다) → 고른 choice.next 로 전이.
///     ② outcome 있으면 → outcome 처리(테러방지=정상 해결 / 폭탄=게임오버 종착) 후 콜백으로 결과 통지.
///     ③ next 만 있으면 → 그 노드로(선형). 사토는 미사용이나 범용 지원.
///   timer/timeoutNext 는 Stage 3(타이머/폭탄사운드)용 — 이번 단계에서는 읽지 않는다.
///
/// 규약 준수(공통 규약 5장): 이 러너는 UI 를 '표시'에만 쓴다(대사 재생·선택 버튼). 진행 결정(다음 손님/종착)은
/// 생성자 콜백(<see cref="OnResolved"/>)으로 호출자(InspectionController)에 위임한다. 점수/돈은 여기서 적용하지 않는다.
///
/// scenario 가 없는(=대부분의) 손님은 InspectionController 가 이 러너를 시작하지 않으므로 기존 흐름 그대로다.
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>종착 결과 종류 — 콜백으로 호출자에게 진행 방식을 알린다.</summary>
    public enum Resolution
    {
        TerrorPrevented, // "테러방지" — 정상 해결 → 다음 손님으로 진행
        Bomb,            // "폭탄" — 게임오버 종착(진행 정지)
    }

    private readonly ScenarioData _data;
    private readonly DialogueView _dialogueView;
    private readonly Func<RejectConfirmPopup> _findChoicePopup; // 선택 UI 조회(재활용 팝업)
    private readonly Action<Resolution, ScenarioOutcome> _onResolved;
    private readonly Func<string, Action, bool> _openScanThen;  // 검사 패널 표시(노드 openScanAfter). 닫힘 시 콜백.
    private readonly string _customerNameKr;

    private bool _finished;

    /// <param name="data">손님의 scenario 그래프(유효해야 함).</param>
    /// <param name="dialogueView">대사 재생용(노드 lines). 기존 손님 흐름과 동일 메커니즘.</param>
    /// <param name="findChoicePopup">선택 버튼 팝업(RejectConfirmPopup) 조회 함수. null/미발견 시 첫 선택지로 폴백(소프트락 방지).</param>
    /// <param name="onResolved">종착 시 (결과 종류, outcome) 통지. 호출자가 다음 손님/종착을 결정한다.</param>
    /// <param name="openScanThen">노드 openScanAfter 처리: (검사종류, 닫힘콜백) → 패널을 열고 '닫힘 시' 콜백 호출하면 true,
    /// 못 열면(미지원/데이터 없음/패널 부재) false 반환(러너가 즉시 다음으로 진행). null 이면 검사 표시 생략.</param>
    /// <param name="customerNameKr">화자 표시 보정용 손님 한글 이름(현재는 데이터의 speaker 를 그대로 표시).</param>
    public ScenarioRunner(
        ScenarioData data,
        DialogueView dialogueView,
        Func<RejectConfirmPopup> findChoicePopup,
        Action<Resolution, ScenarioOutcome> onResolved,
        Func<string, Action, bool> openScanThen = null,
        string customerNameKr = null)
    {
        _data = data;
        _dialogueView = dialogueView;
        _findChoicePopup = findChoicePopup;
        _onResolved = onResolved;
        _openScanThen = openScanThen;
        _customerNameKr = customerNameKr;
    }

    /// <summary>시작 노드(start)부터 그래프를 굴린다. 데이터가 무효면 즉시 안전 종료(테러방지 폴백).</summary>
    public void Begin()
    {
        if (_data == null || !_data.IsValid)
        {
            Debug.LogWarning("[ScenarioRunner] 유효하지 않은 시나리오 — 종착 처리(테러방지 폴백)로 안전 종료.");
            Finish(Resolution.TerrorPrevented, null);
            return;
        }
        EnterNode(_data.start);
    }

    // ── 노드 진입: 대사 재생 → 끝나면 분기/종착 ────────────────────────
    private void EnterNode(string id)
    {
        if (_finished) return;
        ScenarioNode node = _data.Find(id);
        if (node == null)
        {
            Debug.LogWarning($"[ScenarioRunner] 노드 '{id}' 를 찾을 수 없음 — 종착 처리(테러방지 폴백).");
            Finish(Resolution.TerrorPrevented, null);
            return;
        }

        // 노드의 lines 를 기존 대사 메커니즘으로 재생(DialogueCaseData 로 감싸 DialogueView.Play 에 위임).
        DialogueCaseData wrap = WrapLines(node.lines);
        PlayLines(wrap, () => AfterLines(node));
    }

    /// <summary>
    /// 노드 lines 재생이 끝난 직후. openScanAfter 가 있으면 검사 패널(X-ray 등)을 띄우고
    /// '닫힐 때까지' 분기/다음노드 진행을 보류한다(없거나 못 열면 즉시 진행). 사토 intro: 검색대 입장 대사
    /// 뒤 → 전신 X-ray 표시 → 닫으면 introReveal(검문관 발각 반응)로 이어짐.
    /// </summary>
    private void AfterLines(ScenarioNode node)
    {
        if (_finished) return;

        if (!string.IsNullOrEmpty(node.openScanAfter) && _openScanThen != null)
        {
            bool opening = _openScanThen(node.openScanAfter, () => ProceedAfterLines(node));
            if (opening) return; // 패널 열림 → 닫힘 콜백에서 ProceedAfterLines 진행
        }
        ProceedAfterLines(node);
    }

    /// <summary>검사 표시(있었다면 닫힘) 이후의 실제 분기/종착 결정.</summary>
    private void ProceedAfterLines(ScenarioNode node)
    {
        if (_finished) return;

        if (node.HasChoices)
        {
            ShowChoices(node);
            return;
        }
        if (node.HasOutcome)
        {
            ResolveOutcome(node.outcome);
            return;
        }
        if (!string.IsNullOrEmpty(node.next))
        {
            EnterNode(node.next);
            return;
        }

        // 선택지·종착·다음 노드가 전부 없는 막다른 노드 — 소프트락 방지로 안전 종료.
        Debug.LogWarning($"[ScenarioRunner] 노드 '{node.id}' 에 분기/종착/next 가 없음 — 종착 처리(테러방지 폴백).");
        Finish(Resolution.TerrorPrevented, null);
    }

    // ── 선택 버튼 UI(RejectConfirmPopup 재활용, 항상 2지선다) ──────────
    private void ShowChoices(ScenarioNode node)
    {
        ScenarioChoice[] ch = node.choices;
        // 현재 데이터는 항상 2개. 방어적으로 1개/3개 이상도 다룬다(첫 2개만 사용).
        ScenarioChoice left = ch.Length > 0 ? ch[0] : null;
        ScenarioChoice right = ch.Length > 1 ? ch[1] : null;

        RejectConfirmPopup popup = _findChoicePopup != null ? _findChoicePopup() : null;
        if (popup == null)
        {
            // 팝업을 못 찾으면 소프트락 방지로 첫 선택지로 자동 진행(데이터 무손상).
            Debug.LogWarning("[ScenarioRunner] 선택 팝업(RejectConfirmPopup) 미발견 — 첫 선택지로 폴백 진행.");
            if (left != null) EnterNode(left.next);
            else Finish(Resolution.TerrorPrevented, null);
            return;
        }

        // 좌 = choices[0], 우 = choices[1]. 메시지는 대사창이 맥락을 이미 제공하므로 비운다.
        string leftLabel = left != null ? left.label : "";
        string rightLabel = right != null ? right.label : "";
        string leftNext = left != null ? left.next : null;
        string rightNext = right != null ? right.next : null;

        // 제한시간: 노드에 timer(초) + timeoutNext 가 있으면 카운트다운을 띄우고, 만료 시 timeoutNext(보통 폭탄)로 진행.
        float timer = node.timer > 0 ? node.timer : 0f;
        string timeoutNext = node.timeoutNext;
        Action onTimeout = (timer > 0f && !string.IsNullOrEmpty(timeoutNext))
            ? (Action)(() => OnChoiceChosen(timeoutNext))
            : null;

        popup.ShowConfirm(
            string.Empty,
            leftLabel,
            rightLabel,
            () => OnChoiceChosen(leftNext),
            () => OnChoiceChosen(rightNext),
            timer,
            onTimeout);
    }

    private void OnChoiceChosen(string nextId)
    {
        if (_finished) return;
        if (string.IsNullOrEmpty(nextId))
        {
            Debug.LogWarning("[ScenarioRunner] 선택지 next 가 비어 있음 — 종착 처리(테러방지 폴백).");
            Finish(Resolution.TerrorPrevented, null);
            return;
        }
        EnterNode(nextId);
    }

    // ── outcome 처리 (Stage 2 범위: 테러방지=계속 / 폭탄=종착) ───────────
    private void ResolveOutcome(ScenarioOutcome outcome)
    {
        Resolution res = IsBomb(outcome) ? Resolution.Bomb : Resolution.TerrorPrevented;

        // Stage 4 에서 소비할 메타는 로그로만 남긴다(점수 강제 연결 금지).
        Debug.Log($"[ScenarioRunner] outcome branch='{outcome.branch}' result='{outcome.result}' " +
                  $"score={outcome.score} reward='{outcome.reward}' → {res} (점수 미연결: Stage 4)");

        Finish(res, outcome);
    }

    private static bool IsBomb(ScenarioOutcome o) =>
        o != null && o.result == "폭탄";

    // ── 종료 통지 (1회 가드) ──────────────────────────────────────────
    private void Finish(Resolution res, ScenarioOutcome outcome)
    {
        if (_finished) return;
        _finished = true;
        _onResolved?.Invoke(res, outcome);
    }

    // ── 대사 재생 헬퍼 ────────────────────────────────────────────────
    private void PlayLines(DialogueCaseData wrap, Action onComplete)
    {
        if (_dialogueView != null)
        {
            _dialogueView.Play(wrap, onComplete);
        }
        else
        {
            // 대사 뷰 없으면(테스트 등) 즉시 완료 — 흐름만 진행.
            onComplete?.Invoke();
        }
    }

    /// <summary>노드 lines 를 DialogueView 가 재생할 수 있는 DialogueCaseData 로 감싼다(order 보존).</summary>
    private static DialogueCaseData WrapLines(DialogueLineData[] lines)
    {
        DialogueLineData[] safe = lines ?? Array.Empty<DialogueLineData>();
        // order 가 모두 0(데이터에 order 미부여)인 경우 입력 순서를 보존하도록 인덱스로 보정한다.
        bool allZero = true;
        for (int i = 0; i < safe.Length; i++)
            if (safe[i] != null && safe[i].order != 0) { allZero = false; break; }
        if (allZero && safe.Length > 1)
        {
            for (int i = 0; i < safe.Length; i++)
                if (safe[i] != null) safe[i].order = i;
        }
        // "시스템" 화자(지문/상황 연출 라인)는 화자 라벨 없이 나레이션으로 표시한다
        //  (화자칸에 "시스템"이 그대로 뜨던 문제 수정). 사토/심사관 라인은 그대로.
        for (int i = 0; i < safe.Length; i++)
            if (safe[i] != null && safe[i].speaker == "시스템") safe[i].speaker = "";
        return new DialogueCaseData
        {
            caseType = "시나리오",
            gameResult = "-",
            rejectCount = 0,
            lines = safe,
        };
    }
}
