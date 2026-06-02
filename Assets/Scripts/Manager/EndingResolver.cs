using UnityEngine;

/// <summary>엔딩 결정 결과(엔딩 id/이름/유형/트리거 근거).</summary>
public readonly struct EndingResult
{
    public readonly string endingId;    // ending 테이블 ending_id (없으면 코드 폴백 id)
    public readonly string endingName;  // 표시명
    public readonly string endingType;  // normal / early / cumulative
    public readonly string triggerKey;  // 발동 근거(event_id 또는 "score")

    public EndingResult(string id, string name, string type, string trigger)
    {
        endingId = id; endingName = name; endingType = type; triggerKey = trigger;
    }

    public bool IsValid => !string.IsNullOrEmpty(endingId);
}

/// <summary>
/// 엔딩 분기 결정 로직(점수 구간 / 조기 트리거 / 누적 임계치).
///
/// 우선순위:
///  1) 조기엔딩(#11~#14): 발동 즉시 그 엔딩으로(점수 무관).
///  2) 누적엔딩(#15 방역실패 / #16 출국X 입국): 임계치 도달 시.
///  3) 14일 종료 점수 구간(ending 테이블 score_min~score_max; 없으면 코드 폴백 밴드).
///
/// 주의: ending 테이블이 비어 있어(EndingTable 0행) 현재는 코드 폴백 밴드를 쓴다.
/// data-tools 가 ending 시트를 임포트하면 그 score_min/score_max 가 우선한다.
/// event_id ↔ ending_id 연결도 데이터가 채워지면 그쪽을 우선(현재는 코드 매핑).
/// </summary>
public static class EndingResolver
{
    /// <summary>#15/#16 누적 엔딩 발동 임계치(임의 기본값 — 결정 필요/보고 명시).</summary>
    public const int CumulativeThreshold = 3;

    /// <summary>조기엔딩 트리거 발동 시 즉시 엔딩 결정. 트리거가 아니면 IsValid=false.</summary>
    public static EndingResult ResolveEarly(string eventId, ScoreEconomyManager m)
    {
        switch (eventId)
        {
            case EventIds.CorruptGoldDrugs: return Data(eventId) ?? new EndingResult("END_CORRUPT", "부패한 검문관", "early", eventId);
            case EventIds.PlasticDrugs:     return Data(eventId) ?? new EndingResult("END_DRUG_BRIBE", "마약 밀반입 묵인", "early", eventId);
            case EventIds.CultBrainwash:    return Data(eventId) ?? new EndingResult("END_CULT_CONVERT", "포교당한 검문관", "early", eventId);
            case EventIds.CultFollower:     return Data(eventId) ?? new EndingResult("END_CULT_MONEY", "난 돈을 믿어", "early", eventId);

            // 누적형: 임계치 도달 시에만 발동.
            case EventIds.QuarantineFail:
                if (m != null && m.GetEventCount(eventId) >= CumulativeThreshold)
                    return Data(eventId) ?? new EndingResult("END_QUARANTINE_FAIL", "방역 붕괴", "cumulative", eventId);
                return default;
            case EventIds.OverstayApprove:
                if (m != null && m.GetEventCount(eventId) >= CumulativeThreshold)
                    return Data(eventId) ?? new EndingResult("END_OVERSTAY", "불법체류 범람", "cumulative", eventId);
                return default;
        }
        return default;
    }

    /// <summary>14일 종료 시 누적 점수 구간으로 엔딩 결정.</summary>
    public static EndingResult ResolveByScore(int score)
    {
        EndingResult fromTable = FindScoreBand(score);
        if (fromTable.IsValid) return fromTable;

        // 테이블에 유효 밴드가 하나라도 있으면(데이터 로드됨) 점수가 어느 밴드에도 안 들 때
        // 코드 폴백이 아니라 가장 가까운 경계 밴드로 클램프한다. 와일드카드 오발 금지.
        EndingResult clamped = ClampToNearestBand(score);
        if (clamped.IsValid) return clamped;

        // 코드 폴백 밴드(규약 4장: 200~299 우수 사원, 500+ 전설). 음수/저점 구간은 합리적 기본값.
        if (score >= 500) return new EndingResult("END_LEGEND", "전설의 검문관", "normal", "score");
        if (score >= 300) return new EndingResult("END_EXCELLENT", "최우수 검문관", "normal", "score");
        if (score >= 200) return new EndingResult("END_GOOD", "우수 사원", "normal", "score");
        if (score >= 100) return new EndingResult("END_AVERAGE", "평범한 검문관", "normal", "score");
        if (score >= 0)   return new EndingResult("END_POOR", "위태로운 검문관", "normal", "score");
        return new EndingResult("END_FIRED", "해고", "normal", "score");
    }

    // ── ending 테이블 조회(채워지면 우선) ──────────────────────

    private static EndingResult? Data(string eventId)
    {
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.ending : null;
        if (t == null) return null;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (r.Get("trigger_condition") == eventId || r.Get("trigger_type") == eventId)
                return new EndingResult(r.Get("ending_id"), r.Get("ending_name"), r.Get("ending_type") ?? "early", eventId);
        }
        return null;
    }

    /// <summary>
    /// 점수가 들어가는 유효 점수밴드 행을 찾는다.
    /// 유효 밴드 = score_min/score_max **값이 비어있지 않고 int 파싱 성공**인 행만.
    /// (행 11~23처럼 키는 있으나 값이 빈 문자열인 조기/누적/이벤트 엔딩 행은 후보에서 제외 →
    ///  Has 키존재 + GetInt 폴백으로 인한 와일드카드 오발 방지.)
    /// </summary>
    private static EndingResult FindScoreBand(int score)
    {
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.ending : null;
        if (t == null) return default;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (!TryGetBand(r, out int min, out int max)) continue;
            if (score >= min && score <= max)
                return new EndingResult(r.Get("ending_id"), r.Get("ending_name"), r.Get("ending_type") ?? "normal", "score");
        }
        return default;
    }

    /// <summary>
    /// 어떤 유효 밴드에도 안 드는 점수(범위초과/밴드갭)를 가장 가까운 경계 밴드로 클램프한다.
    /// 최고점 초과 → 최상위(max가 가장 큰) 밴드, 최저점 미만 → 최하위(min이 가장 작은) 밴드,
    /// 밴드 갭에 떨어지면 경계 거리가 가장 가까운 밴드로. 유효 밴드가 없으면 IsValid=false(코드 폴백).
    /// </summary>
    private static EndingResult ClampToNearestBand(int score)
    {
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.ending : null;
        if (t == null) return default;

        DataRow best = null;
        long bestDist = long.MaxValue;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (!TryGetBand(r, out int min, out int max)) continue;

            // 밴드까지의 거리(밴드 안이면 0이지만 그건 FindScoreBand가 이미 처리).
            long dist;
            if (score < min) dist = (long)min - score;
            else if (score > max) dist = (long)score - max;
            else dist = 0;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = r;
            }
        }

        if (best == null) return default;
        return new EndingResult(best.Get("ending_id"), best.Get("ending_name"), best.Get("ending_type") ?? "normal", "score");
    }

    /// <summary>
    /// 행에서 점수밴드(min/max)를 추출. score_min/score_max **둘 다 비어있지 않고 int 파싱 성공**일 때만 true.
    /// 빈 문자열/파싱 실패(조기·누적·이벤트 엔딩 행)는 점수밴드 후보가 아니므로 false.
    /// </summary>
    private static bool TryGetBand(DataRow r, out int min, out int max)
    {
        min = 0; max = 0;
        string sMin = r.Get("score_min");
        string sMax = r.Get("score_max");
        if (string.IsNullOrWhiteSpace(sMin) || string.IsNullOrWhiteSpace(sMax)) return false;
        return int.TryParse(sMin, out min) && int.TryParse(sMax, out max);
    }
}
