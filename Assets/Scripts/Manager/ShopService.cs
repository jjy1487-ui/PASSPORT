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
    public const string FxCounselBook   = "COUNSEL_BOOK";     // 심리상담집(1회성, 손님에게 사용 → 결함 유무 힌트)

    /// <summary>1회성 소비형 effect_type(나머지는 영구 활성 플래그).</summary>
    private static readonly HashSet<string> ConsumableEffects = new HashSet<string>
    {
        FxTimeBonus, FxMistakeForgive, FxXrayScan, FxCounselBook,
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

    // ── 구매 성공음(정산 타이핑) ──────────────────────────────
    // 구매가 성공할 때만 재생하는 2D 효과음. 실패(잠금/돈부족/중복)에는 울리지 않는다.
    // 같은 클립을 PlayOneShot 으로 매번 재생(JudgmentPanel 의 도장음과 동일 패턴).
    private AudioSource _sfx;
    private AudioClip _purchaseSound;
    private const float PurchaseVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ShopSave.LoadInto(this);

        // 구매 성공음용 2D AudioSource(없으면 추가) + 정산 타이핑 클립 로드.
        _sfx = GetComponent<AudioSource>();
        if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0f; // 2D
        _purchaseSound = Resources.Load<AudioClip>("Audio/Settlement");
        if (_purchaseSound == null) Debug.LogWarning("[ShopService] Resources/Audio/Settlement 클립을 찾지 못했습니다.");

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

    /// <summary>1회성 소비형 effect_type 인지(상점 UI 가 "보유 N개" 표시 여부 판단).</summary>
    public bool IsConsumable(string effectType)
        => !string.IsNullOrEmpty(effectType) && ConsumableEffects.Contains(effectType);

    /// <summary>이미 보유(영구 활성)했는가 — 영구 효과 중복 구매 방지 UI 표시용.</summary>
    public bool IsOwned(string effectType)
        => !ConsumableEffects.Contains(effectType) && IsEffectActive(effectType);

    /// <summary>가방(인벤토리)에 표시할 보유 아이템 1개(표시 전용 DTO).</summary>
    public readonly struct OwnedItem
    {
        /// <summary>shop 테이블 item_name(표시 이름).</summary>
        public readonly string Name;
        /// <summary>shop 테이블 icon 키(Resources/Shop/&lt;icon&gt; 로드용).</summary>
        public readonly string Icon;
        /// <summary>effect_type(사용 가능 판정용 — 예: COUNSEL_BOOK).</summary>
        public readonly string EffectType;
        /// <summary>소비품 보유 수량(영구 효과는 0).</summary>
        public readonly int Count;
        /// <summary>true=소비품(수량 표시), false=영구 효과.</summary>
        public readonly bool Consumable;
        public OwnedItem(string name, string icon, string effectType, int count, bool consumable)
        { Name = name; Icon = icon; EffectType = effectType; Count = count; Consumable = consumable; }
    }

    /// <summary>구매(보유)한 상점 아이템 목록(가방 표시용). 소비품은 수량&gt;0, 영구 효과는 활성된 것만.
    /// shop 테이블 순서(shop_item_id)대로 반환한다. UI(가방)는 이 결과를 표시에만 쓴다(규약 5장).</summary>
    public List<OwnedItem> GetOwnedItems()
    {
        var result = new List<OwnedItem>();
        if (Shop == null) return result;
        foreach (DataRow r in Shop.rows)
        {
            if (r == null) continue;
            string fx = r.Get("effect_type");
            string nm = r.Get("item_name");
            string ic = r.Get("icon");
            if (string.IsNullOrEmpty(fx) || string.IsNullOrEmpty(nm)) continue;
            if (ConsumableEffects.Contains(fx))
            {
                int c = GetConsumableCount(fx);
                if (c > 0) result.Add(new OwnedItem(nm, ic, fx, c, true));
            }
            else if (IsEffectActive(fx))
            {
                result.Add(new OwnedItem(nm, ic, fx, 0, false));
            }
        }
        return result;
    }

    // ── 가방 칸 배치(자유 배치) ────────────────────────────────
    /// <summary>가방 칸 수(고정 6칸).</summary>
    public const int SlotCount = 6;
    /// <summary>칸 배치: 인덱스=칸, 값=effect_type(빈 칸=null). 플레이어가 드래그로 자유 배치한다(세이브 대상).</summary>
    private readonly string[] _slotLayout = new string[SlotCount];

    /// <summary>이 effect_type 을 현재 보유 중인가(소비품=수량&gt;0, 영구=활성).</summary>
    private bool IsItemOwned(string fx)
        => !string.IsNullOrEmpty(fx) && (ConsumableEffects.Contains(fx) ? GetConsumableCount(fx) > 0 : IsEffectActive(fx));

    private int FirstEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++) if (string.IsNullOrEmpty(_slotLayout[i])) return i;
        return -1;
    }

    /// <summary>칸 배치를 보유 상태와 맞춘다: 미보유가 된 칸은 비우고, 칸 없는 보유 아이템은 첫 빈 칸에 넣는다(상점 순서).</summary>
    private void SyncSlotLayout()
    {
        for (int i = 0; i < SlotCount; i++)
            if (!string.IsNullOrEmpty(_slotLayout[i]) && !IsItemOwned(_slotLayout[i])) _slotLayout[i] = null;

        if (Shop == null) return;
        foreach (DataRow r in Shop.rows)
        {
            if (r == null) continue;
            string fx = r.Get("effect_type");
            if (string.IsNullOrEmpty(fx) || !IsItemOwned(fx)) continue;
            if (Array.IndexOf(_slotLayout, fx) >= 0) continue; // 이미 어느 칸에 있음
            int e = FirstEmptySlot();
            if (e >= 0) _slotLayout[e] = fx;
        }
    }

    /// <summary>칸별 보유 아이템(자유 배치 반영). 길이 SlotCount, 빈 칸은 EffectType=null. UI 표시 전용(규약 5장).</summary>
    public OwnedItem[] GetSlotItems()
    {
        SyncSlotLayout();
        var arr = new OwnedItem[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            string fx = _slotLayout[i];
            if (string.IsNullOrEmpty(fx) || Shop == null) continue;
            DataRow r = Shop.FindByEffect(fx);
            if (r == null) continue;
            bool cons = ConsumableEffects.Contains(fx);
            arr[i] = new OwnedItem(r.Get("item_name"), r.Get("icon"), fx, cons ? GetConsumableCount(fx) : 0, cons);
        }
        return arr;
    }

    /// <summary>아이템을 다른 칸으로 옮긴다(자유 배치). 대상 칸에 다른 아이템이 있으면 자리 교환. 저장+통지.</summary>
    public void MoveItemToSlot(string effectType, int targetSlot)
    {
        if (string.IsNullOrEmpty(effectType) || targetSlot < 0 || targetSlot >= SlotCount) return;
        SyncSlotLayout();
        int from = Array.IndexOf(_slotLayout, effectType);
        if (from < 0 || from == targetSlot) return;
        _slotLayout[from] = _slotLayout[targetSlot]; // 대상이 비었으면 null → 교환
        _slotLayout[targetSlot] = effectType;
        ShopSave.SaveFrom(this);
        OnEffectsChanged?.Invoke();
    }

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
        // 1개만 구매 가능: 영구 효과는 이미 보유 시, 소비품도 1개 보유 시 구매 차단(중복/비축 방지).
        //  소비품은 사용해 0개가 되면 다시 살 수 있다.
        if (ConsumableEffects.Contains(effectType))
        {
            if (GetConsumableCount(effectType) >= 1) return false;
        }
        else if (IsEffectActive(effectType))
        {
            return false;
        }

        int price = row.GetInt("price", 0);
        var econ = Economy;
        if (econ == null) return false;
        if (!econ.TrySpend(price)) return false; // 돈 부족

        GrantEffect(effectType, row.GetInt("effect_value", 0));
        ShopSave.SaveFrom(this);
        // 구매 성공 시에만 정산 타이핑 효과음 재생(여기 도달 = 돈 차감·효과 부여 확정).
        if (_purchaseSound != null && _sfx != null) _sfx.PlayOneShot(_purchaseSound, PurchaseVolume);
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

        // 자유 배치: 새로 보유하게 된 아이템을 첫 빈 칸에 둔다(이미 칸에 있으면 유지).
        if (Array.IndexOf(_slotLayout, effectType) < 0)
        {
            int e = FirstEmptySlot();
            if (e >= 0) _slotLayout[e] = effectType;
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
    /// <summary>칸 배치 직렬화(ShopSave 전용). 길이 SlotCount, 빈 칸은 null.</summary>
    internal string[] SlotLayoutRaw => _slotLayout;

    internal void RestoreState(IEnumerable<string> active, IDictionary<string, int> consumables)
    {
        _activeEffects.Clear();
        if (active != null) foreach (var e in active) if (!string.IsNullOrEmpty(e)) _activeEffects.Add(e);
        _consumables.Clear();
        if (consumables != null) foreach (var kv in consumables) _consumables[kv.Key] = kv.Value;
        OnEffectsChanged?.Invoke();
    }

    /// <summary>칸 배치 복원(ShopSave 전용). 빈 문자열/누락은 빈 칸(null)으로.</summary>
    internal void RestoreSlots(IList<string> slots)
    {
        for (int i = 0; i < SlotCount; i++)
            _slotLayout[i] = (slots != null && i < slots.Count && !string.IsNullOrEmpty(slots[i])) ? slots[i] : null;
    }

    /// <summary>새 게임 시작 시 초기화.</summary>
    public void ResetAll()
    {
        _activeEffects.Clear();
        _consumables.Clear();
        for (int i = 0; i < SlotCount; i++) _slotLayout[i] = null;
        ShopSave.Clear();
        OnEffectsChanged?.Invoke();
    }
}
