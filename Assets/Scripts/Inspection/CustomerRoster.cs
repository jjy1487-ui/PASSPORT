using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 손님 명단(로스터) 셔플 + 정상확률(valid_chance) 굴림 유틸.
///
/// 슬롯별 character_type 은 고정(유형 시퀀스=난이도 구성 보존)하되,
///   1) day_schedule.valid_chance 로 매 플레이 그 슬롯의 정상/결함(정상 승인/거절)을 새로 굴리고
///   2) 결정된 (유형, 정답) 풀에서 무작위 인물 패키지를 뽑아
/// 매 플레이 얼굴·이름·서류는 물론 정상/결함 여부까지 달라지게 한다.
///
/// 풀: Resources/GameData/day1~14.json 전체(각 CustomerData 는 서류/대화/스캔/정답/분기키를 통째로 보유 →
///     어느 슬롯에 꽂아도 판정·대조·정산이 일관).
/// 정상확률: GameDatabaseProvider.Database.daySchedule(엑셀 day_schedule)의 valid_chance.
///   - daySchedule 가 없거나 해당 (day,slot) 확률이 없으면 그 슬롯은 원래 정답 유지(=확률 미적용 폴백).
///   - 굴린 정답의 풀이 비어 있으면(희귀/단일 결과 유형) 원래 정답 풀로 폴백 → 항상 유효한 손님 보장.
/// 결정론: rng(System.Random) 주입 → 같은 시드면 같은 굴림/추첨(QA 재현).
/// </summary>
public static class CustomerRoster
{
    private const int FirstDay = 1;
    private const int LastDay = 14;
    private const string ResourcePrefix = "GameData/day";
    private const string CorrectApprove = "정상 승인";
    private const string CorrectReject  = "정상 거절";

    // "유형|정답" → 인물 패키지들(여러 날에서 모음).
    private static Dictionary<string, List<CustomerData>> _pool;
    // day*100+slot → 정상 확률(valid_chance, 0~1).
    private static Dictionary<int, float> _validChance;
    // 한 playthrough(1~14일) 동안 이미 등장시킨 인물(customerId). 날짜를 넘나들며 같은 인물이
    // 다시 뽑히지 않게 한다(전역 중복 방지). 새 게임 첫날에 BeginPlaythrough()로 비운다.
    private static readonly HashSet<int> _usedGlobal = new HashSet<int>();

    // 키에 서류 구성(비자/PCR 보유)을 포함 → 셔플이 day5-7 PCR·외국인 비자 요건을 보존(다른 날 패키지로 바뀌어도).
    private static string Key(string type, string correct, string docSig) => (type ?? "") + "|" + (correct ?? "") + "|" + docSig;
    private static int SlotKey(int day, int slot) => day * 100 + slot;

    /// <summary>서류 구성 시그니처: 비자(V)/PCR(P) 보유 여부. 같은 서류셋 패키지끼리만 교체되게 한다.</summary>
    private static string DocSig(CustomerData c)
    {
        bool visa = false, pcr = false;
        if (c != null && c.documents != null)
            foreach (var d in c.documents)
            {
                if (d == null || d.documentType == null) continue;
                if (d.documentType == "비자") visa = true;
                else if (d.documentType.Contains("PCR")) pcr = true;
            }
        return (visa ? "V" : "") + (pcr ? "P" : "");
    }

    /// <summary>day1~14.json 전체를 읽어 (유형|정답) 풀을 1회 구축한다(이미 있으면 무시).</summary>
    public static void EnsureLoaded()
    {
        if (_pool != null) return;
        _pool = new Dictionary<string, List<CustomerData>>();
        for (int d = FirstDay; d <= LastDay; d++)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePrefix + d);
            if (asset == null) continue;
            Day1Data day;
            try { day = JsonUtility.FromJson<Day1Data>(asset.text); }
            catch { continue; }
            if (day == null || day.customers == null) continue;
            foreach (var c in day.customers)
            {
                if (c == null) continue;
                string k = Key(c.characterType, c.correctResult, DocSig(c));
                if (!_pool.TryGetValue(k, out var list)) { list = new List<CustomerData>(); _pool[k] = list; }
                list.Add(c);
            }
        }
    }

    /// <summary>day_schedule(SO)에서 (day,slot)→valid_chance 맵을 1회 구축. 없으면 빈 맵(확률 미적용).</summary>
    private static void EnsureSchedule()
    {
        if (_validChance != null) return;
        _validChance = new Dictionary<int, float>();
        var t = GameDatabaseProvider.Database != null ? GameDatabaseProvider.Database.daySchedule : null;
        if (t == null || t.rows == null) return;
        foreach (var r in t.rows)
        {
            if (r == null) continue;
            if (!int.TryParse(r.Get("day"), out int day)) continue;
            if (!int.TryParse(r.Get("slot"), out int slot)) continue;
            if (float.TryParse(r.Get("valid_chance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float vc))
                _validChance[SlotKey(day, slot)] = Mathf.Clamp01(vc);
        }
    }

    /// <summary>새 playthrough(1일차) 시작 시 호출 — 전역 등장 기록을 비운다.
    /// 이후 14일 동안 같은 인물(customerId)이 두 번 등장하지 않게 한다(풀이 충분할 때).</summary>
    public static void BeginPlaythrough() => _usedGlobal.Clear();

    /// <summary>
    /// data.customers 각 슬롯을 재배정한다. 유형은 유지하고, valid_chance 로 정상/결함을 매 플레이 굴린 뒤
    /// 같은 (유형,정답) 풀에서 무작위 인물을 뽑는다. 전역 기록(_usedGlobal)으로 playthrough(1~14일)
    /// 전체에서 동일 인물(customerId)이 다시 등장하지 않게 한다(풀이 충분할 때 반복 0).
    /// </summary>
    /// <param name="data">교체 대상 하루치 데이터(in-place 수정).</param>
    /// <param name="rng">난수원(시드 주입 가능 → 재현성).</param>
    public static void Reassign(Day1Data data, System.Random rng)
    {
        if (data == null || data.customers == null || rng == null) return;
        EnsureLoaded();
        EnsureSchedule();

        for (int i = 0; i < data.customers.Length; i++)
        {
            CustomerData orig = data.customers[i];
            if (orig == null) continue;

            // 1) 정상확률 굴림 → 이 슬롯의 목표 정답(승인/거절) 결정. 확률 없으면 원래 정답 유지.
            string targetCorrect = orig.correctResult;
            if (_validChance.TryGetValue(SlotKey(data.day, orig.slot), out float vc))
                targetCorrect = (rng.NextDouble() < vc) ? CorrectApprove : CorrectReject;

            // 2) 미사용 인물을 (유형,목표정답,서류셋) → (유형,원래정답) → (유형,같은 서류셋의 어느 정답이든)
            //    순으로 찾는다. 모두 소진되면 원본 유지. _usedGlobal 로 14일 전체 인물 중복을 막는다.
            //    서류셋(비자/PCR)을 보존해 day5-7 PCR·외국인 비자가 셔플로 사라지지 않게 한다.
            string sig = DocSig(orig);
            CustomerData pick = PickFromPool(orig.characterType, targetCorrect, sig, _usedGlobal, rng)
                             ?? PickFromPool(orig.characterType, orig.correctResult, sig, _usedGlobal, rng)
                             ?? PickFromPool(orig.characterType, CorrectApprove, sig, _usedGlobal, rng)
                             ?? PickFromPool(orig.characterType, CorrectReject, sig, _usedGlobal, rng)
                             ?? orig;

            _usedGlobal.Add(pick.customerId);
            data.customers[i] = pick;
        }
    }

    // [QA/검수] 확률 변이 강제 모드. QaJumpOverlay 가 설정한다.
    //   Auto=valid_chance 확률 굴림(기본/정식 동작) · Normal=확률 손님 전원 정상 · Defect=전원 불량.
    //   릴리스에선 항상 Auto(설정 UI가 개발빌드 전용)라 정식 플레이엔 영향 없음.
    public enum VariantForce { Auto, Normal, Defect }
    public static VariantForce ForceMode = VariantForce.Auto;

    /// <summary>
    /// 확률 변형 굴림: altVariant 를 가진 손님(박철수 등)을 매 플레이 valid_chance 로 굴려,
    /// 굴림 결과(정상/불량)가 baked 와 다르면 그 손님 위에 altVariant(서류·대사·정답·검사)를 덮어쓴다.
    /// 신원(이름·얼굴·생년월일 등)은 유지 — 셔플(Reassign)과 독립적으로, 인물은 그대로 두고
    /// 서류 정상/불량만 매번 달라지게 한다. validChance 가 0/1 이거나 altVariant 가 없으면 건너뛴다.
    /// </summary>
    public static void RollVariants(Day1Data data, System.Random rng)
    {
        if (data == null || data.customers == null || rng == null) return;
        foreach (var c in data.customers)
        {
            if (c == null) continue;
            if (c.validChance <= 0f || c.validChance >= 1f) continue; // 고정/미설정 → 굴리지 않음
            // JsonUtility 는 없는 중첩객체를 null 이 아니라 '빈 인스턴스'로 역직렬화한다 →
            // correctResult 가 비면 실제 변형이 아니므로 건너뛴다(빈 altVariant 오버레이 방지).
            var a = c.altVariant;
            if (a == null || string.IsNullOrEmpty(a.correctResult)) continue;

            bool baseIsNormal = c.correctResult == CorrectApprove;
            // [QA] 강제 모드면 확률 대신 고정(정상=true/불량=false). Auto면 기존 valid_chance 굴림.
            bool rollNormal = ForceMode == VariantForce.Normal ? true
                            : ForceMode == VariantForce.Defect ? false
                            : rng.NextDouble() < c.validChance;
            if (rollNormal == baseIsNormal) continue; // 굴림이 baked 와 같음 → 그대로 둠

            // 반대 변형으로 오버레이(신원 필드는 보존)
            c.correctResult = a.correctResult;
            c.defectVariant = a.defectVariant;
            c.rejectAdvancedBranchKey = a.rejectAdvancedBranchKey;
            c.rejectGuidedCaseType = a.rejectGuidedCaseType;
            c.documents = a.documents;
            c.dialogueCases = a.dialogueCases;
            c.xray = a.xray;
            c.fingerprint = a.fingerprint;
            c.crossCheckLines = a.crossCheckLines; // 불량 변형의 대조 대사도 함께 오버레이(없으면 null → 폴백)
        }
    }

    /// <summary>(유형,정답) 풀에서 이 날 아직 안 쓴 인물 1명 무작위. 없으면 null.</summary>
    private static CustomerData PickFromPool(string type, string correct, string docSig, HashSet<int> usedIds, System.Random rng)
    {
        if (_pool == null) return null;
        if (!_pool.TryGetValue(Key(type, correct, docSig), out var cands) || cands.Count == 0) return null;
        var avail = new List<CustomerData>(cands.Count);
        foreach (var c in cands)
            if (c != null && !usedIds.Contains(c.customerId)) avail.Add(c);
        if (avail.Count == 0) return null;
        return avail[rng.Next(avail.Count)];
    }

    /// <summary>테스트/재빌드용 캐시 초기화.</summary>
    public static void ClearCache() { _pool = null; _validChance = null; _usedGlobal.Clear(); }
}
