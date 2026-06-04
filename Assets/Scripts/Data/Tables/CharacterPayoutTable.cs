using UnityEngine;

/// <summary>캐릭터별 분기 금액표. payout_id, character_type(FK), visit_round, doc_state, defect_variant,
/// branch_key, base_tier(int), bounty(포상금 int), payout(지급액 int), formula(산식 텍스트),
/// item_drop(아이템), early_ending(#11/#12 등 조기엔딩 트리거), appears_round1(bool), note.
/// 게임플레이: 점수표와 동일 키로 조회해 돈 가산 + 아이템/조기엔딩 트리거.</summary>
[CreateAssetMenu(fileName = "CharacterPayoutTable", menuName = "Passport/Data/Character Payout Table")]
public class CharacterPayoutTable : DataTableAsset
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
