using UnityEngine;

// ── 캐릭터 분기 점수표 (260602 분기표 편입) ──────────
//  payout 테이블과 (character_type, doc_state, defect_variant, branch_key)로 조인.
//  branch_key는 자유서술 분기를 정규화한 enum 문자열(BRANCH_CATALOG.md 참조).
//  점수는 엔딩용(규약 4장 점수≠돈 분리).

/// <summary>캐릭터별 분기 점수표. score_id, character_type(FK), visit_round, doc_state(normal/defect),
/// defect_variant, branch_key, score(int), score_range(범위/이벤트 텍스트), title(호칭), event_id(#11~#16), note.
/// 게임플레이: 판정 확정 시 (character_type, doc_state, defect_variant, branch_key)로 조회해 점수 가산.</summary>
[CreateAssetMenu(fileName = "CharacterScoreTable", menuName = "Passport/Data/Character Score Table")]
public class CharacterScoreTable : DataTableAsset
{
    /// <summary>조인 키로 행 조회. defect_variant/visit_round는 null이면 무시(부분 매칭).</summary>
    public DataRow Find(string characterType, string docState, string branchKey,
                        string defectVariant = null, string visitRound = null)
    {
        foreach (var r in rows)
        {
            if (r == null) continue;
            if (r.Get("character_type") != characterType) continue;
            if (r.Get("doc_state") != docState) continue;
            if (r.Get("branch_key") != branchKey) continue;
            if (defectVariant != null && r.Get("defect_variant") != defectVariant) continue;
            if (visitRound != null && r.Get("visit_round") != visitRound) continue;
            return r;
        }
        return null;
    }
}
