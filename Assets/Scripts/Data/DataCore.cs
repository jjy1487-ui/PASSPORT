using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
//  DataCore — 「여권 주세요」 데이터 핵심 타입 (data-tools 소유)
//  규약 SHARED-CONVENTIONS 3.7 / 6장: 변동 필드는 고정 멤버로 박지 않고
//  키-값(snake_case 키 그대로) + meta(한글 표시명/자료형)로 다룬다.
//  엑셀 컬럼이 추가/삭제돼도 임포터만 고치면 되고 런타임 로직은 무수정.
// ─────────────────────────────────────────────────────────────

/// <summary>판정 결과(안정 enum). 변동이 큰 document_type/character_type은 문자열 데이터로 둔다.</summary>
public enum Verdict
{
    Approve, // 통과
    Reject   // 거절
}

/// <summary>컬럼 1개의 표시 메타. 임포트 시 data-tools가 채운다(DocumentInstance.meta 근거).</summary>
[Serializable]
public class FieldMeta
{
    [Tooltip("엑셀 row3 영문 컬럼명(키). 예: expiry_date")]
    public string key;

    [Tooltip("엑셀 row4 한글 표시명. 예: 만료일")]
    public string label;

    [Tooltip("엑셀 row2 자료형. 예: date / varchar(30) / int")]
    public string type;
}

/// <summary>
/// 키-값 데이터 한 행. 직렬화 가능한 병렬 리스트(keys/values)로 저장하고
/// 런타임 조회는 Dictionary로 한다. snake_case 키를 그대로 보존(매핑은 임포터 한 곳).
/// </summary>
[Serializable]
public class DataRow
{
    [SerializeField] private List<string> keys = new List<string>();
    [SerializeField] private List<string> values = new List<string>();

    [NonSerialized] private Dictionary<string, string> _cache;

    public IReadOnlyList<string> Keys => keys;
    public IReadOnlyList<string> Values => values;

    public void Set(string key, string value)
    {
        int i = keys.IndexOf(key);
        if (i >= 0) { values[i] = value; }
        else { keys.Add(key); values.Add(value); }
        _cache = null;
    }

    /// <summary>키로 값 조회. 없으면 null.</summary>
    public string Get(string key)
    {
        EnsureCache();
        return _cache.TryGetValue(key, out var v) ? v : null;
    }

    public bool Has(string key)
    {
        EnsureCache();
        return _cache.ContainsKey(key);
    }

    public int GetInt(string key, int fallback = 0)
        => int.TryParse(Get(key), out var v) ? v : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => float.TryParse(Get(key), out var v) ? v : fallback;

    /// <summary>키-값 사전 복제(변조 사본 생성용). 진실을 건드리지 않게 새 Dictionary 반환.</summary>
    public Dictionary<string, string> ToDictionary()
    {
        var d = new Dictionary<string, string>(keys.Count);
        for (int i = 0; i < keys.Count; i++) d[keys[i]] = i < values.Count ? values[i] : null;
        return d;
    }

    private void EnsureCache()
    {
        if (_cache != null) return;
        _cache = new Dictionary<string, string>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
            _cache[keys[i]] = i < values.Count ? values[i] : null;
    }
}

/// <summary>
/// 모든 테이블 SO의 공통 베이스. 컬럼 메타(한글/자료형) + 데이터 행을 키-값으로 보존.
/// 임포터가 19시트 전부 이 형태로 채운다(변경 흡수 단일 지점).
/// </summary>
public abstract class DataTableAsset : ScriptableObject
{
    [Header("테이블 식별")]
    [Tooltip("엑셀 시트명. 예: customer")]
    public string sheetName;

    [Header("컬럼 메타 (한글 표시명/자료형)")]
    public List<FieldMeta> columns = new List<FieldMeta>();

    [Header("데이터 행 (snake_case 키-값)")]
    public List<DataRow> rows = new List<DataRow>();

    /// <summary>키(컬럼)에 대한 한글 표시명. 없으면 키 그대로.</summary>
    public string Label(string key)
    {
        foreach (var c in columns)
            if (c != null && c.key == key) return string.IsNullOrEmpty(c.label) ? key : c.label;
        return key;
    }

    /// <summary>특정 컬럼 값이 일치하는 첫 행 반환. 없으면 null.</summary>
    public DataRow FindBy(string key, string value)
    {
        foreach (var r in rows)
            if (r != null && r.Get(key) == value) return r;
        return null;
    }
}
