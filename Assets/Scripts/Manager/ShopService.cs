using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 백엔드(데이터/경제). shop 테이블을 소비해 "구매 가능 품목 조회 → 구매(돈 차감) →
/// 활성 효과 보유"를 담당한다. UI 패널은 ui 가 만들고, 이 서비스의 공개 API 만 호출한다(규약 5장).
///
/// 효과 모델:
///  - 영구 활성(MAGNIFY/UV_LIGHT/AUTO_HIGHLIGHT/DB_UPDATE): 한 번 사면 effect_type 플래그가 켜진다.
///  - 1회성 소비(XRAY_SCAN/MISTAKE_FORGIVE/TIME_BONUS): 보유 카운트로 들고 있다가 사용 시 소비.
/// 결정론: 외부 입력(구매 호출)만으로 상태가 결정된다(랜덤 없음).
/// </summary>
public sealed class ShopService : MonoBehaviour
{
    public static ShopService Instance { get; private set; }

    // ── effect_type 상수(shop 시트 값과 1:1) ──────────────────
    public const string FxMagnify       = "MAGNIFY";          // 확대경(영구, UI 의존)
    public const string FxUvLight       = "UV_LIGHT";         // 자외선램프(영구, UI 의존)
    public const string FxTimeBonus     = "TIME_BONUS";       // 추가검사시간(1회성, +30초)
    public const string FxMistakeForgive = "MISTAKE_FORGIVE"; // 커피: 다음 오판 1회 무효(1회성)
    public const string FxAutoHighlight = "AUTO_HIGHLIGHT";   // 규정집 하이라이터(영구, UI 의존)
    public const string FxXrayScan      = "XRAY_SCAN";        // X-ray 정밀 스캔권(1회성, UI 의존)
    public const string FxDbUpdate      = "DB_UPDATE";        // 지문 DB 갱신(영구, UI 의존)

    /// <summary>1회성 소비형 effect_type(나머지는 영구 활성 플래그).</summary>
    private static readonly HashSet<string> ConsumableEffects = new HashSet<string>
    {
        FxTimeBonus, FxMistakeForgive, FxXrayScan,
    };

    // ── 상태(세이브 대상) ─────────────────────────────────────
    /// <summary>구매로 켜진 영구 활성 효과(effect_type 집합).</summary>
    public IReadOnlyCollection<string> ActiveEffects => _activeEffects;
    private readonly HashSet<string> _activeEffects = new HashSet<string>();

    /// <summary>1회성 소비형 보유 수량(effect_type → 남은 횟수).</summary>
    private readonly Dictionary<string, int> _consumables = new Dictionary<string, int>();

    // ── 이벤트(UI 구독) ───────────────────────────────────────
    /// <summary>구매 성공(effect_type). UI 가 구매 피드백·잠금 해제 갱신에 사용.</summary>
    public event Action<string> OnItemPurchased;
    /// <summary>활성 효과/소비 카운트가 바뀌면. UI 가 인디케이터 갱신에 사용.</summary>
    public event Action OnEffectsChanged;

    private ScoreEconomyManager Economy => ScoreEconomyManager.Instance;
    private GameDatabase Db => GameDatabaseProvider.Database;
    private ShopTable Shop => Db != null ? Db.shop : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ShopSave.LoadInto(this);

        // 매니저에 "오판 1회 무효" 소비 훅을 등록(오판 정산 직전 호출).
        if (Economy != null) Economy.MistakeForgiveHook = TryConsumeMistakeForgive;
    }

    private void OnEnable()
    {
        // Awake 보다 늦게 ScoreEconomyManager 가 생성되는 씬 순서 대비(멱등 재등록).
        if (Economy != null && Economy.MistakeForgiveHook == null)
            Economy.MistakeForgiveHook = TryConsumeMistakeForgive;
    }

    // ── 조회 API(UI 가 호출) ──────────────────────────────────

    /// <summary>해당 일차+회차에 구매 가능한 품목 행 목록.
    /// unlock_day &lt;= day 이고 unlock_run &lt;= 현재 회차(GameProgressSave.CurrentPlaythrough)인 행만.
    /// unlock_run 컬럼/값이 없으면 1(=1회차부터)로 간주 → 기존 데이터 호환.</summary>
    public List<DataRow> GetAvailableItems(int day)
    {
        if (Shop == null) return new List<DataRow>();
        int run = GameProgressSave.CurrentPlaythrough;
        List<DataRow> list = Shop.AvailableOn(day);
        list.RemoveAll(r => r == null || r.GetInt("unlock_run", 1) > run);
        return list;
    }

    /// <summary>effect_type 영구 활성 여부(MAGNIFY 등 UI/스캐너 게이팅용).</summary>
    public bool IsEffectActive(string effectType)
        => !string.IsNullOrEmpty(effectType) && _activeEffects.Contains(effectType);

    /// <summary>1회성 소비형 보유 수량(없으면 0).</summary>
    public int GetConsumableCount(string effectType)
        => _consumables.TryGetValue(effectType, out int n) ? n : 0;

    /// <summary>이미 보유(영구 활성)했는가 — 영구 효과 중복 구매 방지 UI 표시용.</summary>
    public bool IsOwned(string effectType)
        => !ConsumableEffects.Contains(effectType) && IsEffectActive(effectType);

    // ── 구매 API(UI 가 호출) ──────────────────────────────────

    /// <summary>
    /// shop_item_id 로 구매한다(현재 일차 검증 포함). 성공 시 돈을 차감하고 효과를 부여한다.
    /// 실패(잠금/돈부족/중복) 시 false. 결정론적(외부 입력만으로 결과 확정).
    /// </summary>
    /// <param name="shopItemId">shop 테이블 shop_item_id.</param>
    /// <param name="currentDay">현재 일차(unlock_day 검증).</param>
    public bool TryBuy(string shopItemId, int currentDay)
    {
        if (Shop == null) return false;
        DataRow row = Shop.FindById(shopItemId);
        if (row == null) { Debug.LogWarning($"[ShopService] shop_item_id={shopItemId} 행 없음"); return false; }

        int unlockDay = row.GetInt("unlock_day", int.MaxValue);
        if (currentDay < unlockDay) return false; // 아직 잠김(일차)
        if (GameProgressSave.CurrentPlaythrough < row.GetInt("unlock_run", 1)) return false; // 아직 잠김(회차)

        string effectType = row.Get("effect_type");
        // 영구 효과 중복 구매 차단(이미 켜져 있으면 돈 낭비 방지).
        if (!ConsumableEffects.Contains(effectType) && IsEffectActive(effectType)) return false;

        int price = row.GetInt("price", 0);
        var econ = Economy;
        if (econ == null) return false;
        if (!econ.TrySpend(price)) return false; // 돈 부족

        GrantEffect(effectType, row.GetInt("effect_value", 0));
        ShopSave.SaveFrom(this);
        OnItemPurchased?.Invoke(effectType);
        OnEffectsChanged?.Invoke();
        return true;
    }

    /// <summary>구매한 effect_type 을 활성 효과/소비 수량에 반영한다.</summary>
    private void GrantEffect(string effectType, int effectValue)
    {
        if (string.IsNullOrEmpty(effectType)) return;
        if (ConsumableEffects.Contains(effectType))
        {
            // effect_value 가 있으면 그만큼(예: 사용 횟수), 없으면 1회 추가.
            int add = effectValue > 0 && effectType != FxTimeBonus ? effectValue : 1;
            _consumables.TryGetValue(effectType, out int cur);
            _consumables[effectType] = cur + add;
        }
        else
        {
            _activeEffects.Add(effectType);
        }
    }

    // ── 소비 API(게임플레이/UI 가 호출) ───────────────────────

    /// <summary>
    /// 1회성 소비형 효과를 1회 사용한다(XRAY_SCAN/TIME_BONUS 등). 보유 없으면 false.
    /// TIME_BONUS 의 "당일 +30초" 적용은 타이머 UI/시스템이 이 true 를 받아 처리한다.
    /// </summary>
    public bool TryConsume(string effectType)
    {
        if (!_consumables.TryGetValue(effectType, out int n) || n <= 0) return false;
        _consumables[effectType] = n - 1;
        ShopSave.SaveFrom(this);
        OnEffectsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 다음 오판 1회 무효(커피) 소비 시도. ScoreEconomyManager 가 오판 정산 직전 호출한다.
    /// 보유분이 있으면 1 소비 후 true(→ 오판 점수/벌금 미적용). 없으면 false.
    /// </summary>
    public bool TryConsumeMistakeForgive() => TryConsume(FxMistakeForgive);

    // ── 세이브 직렬화 헬퍼(ShopSave 전용) ─────────────────────

    internal IReadOnlyCollection<string> ActiveEffectsRaw => _activeEffects;
    internal IReadOnlyDictionary<string, int> ConsumablesRaw => _consumables;

    internal void RestoreState(IEnumerable<string> active, IDictionary<string, int> consumables)
    {
        _activeEffects.Clear();
        if (active != null) foreach (var e in active) if (!string.IsNullOrEmpty(e)) _activeEffects.Add(e);
        _consumables.Clear();
        if (consumables != null) foreach (var kv in consumables) _consumables[kv.Key] = kv.Value;
        OnEffectsChanged?.Invoke();
    }

    /// <summary>새 게임 시작 시 초기화.</summary>
    public void ResetAll()
    {
        _activeEffects.Clear();
        _consumables.Clear();
        ShopSave.Clear();
        OnEffectsChanged?.Invoke();
    }
}
