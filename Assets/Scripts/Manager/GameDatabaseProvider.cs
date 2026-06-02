using UnityEngine;

/// <summary>
/// GameDatabase SO 를 런타임에 1회 로드해 캐시한다(Resources/GameData/GameDatabase).
/// gameplay 매니저는 씬 바인딩 없이도 이 진입점으로 전 테이블에 접근한다.
/// data-tools 가 .asset 을 갱신하면 재임포트만으로 반영된다(규약 6장).
/// </summary>
public static class GameDatabaseProvider
{
    private const string ResourcePath = "GameData/GameDatabase";
    private static GameDatabase _cached;
    private static bool _tried;

    /// <summary>전역 데이터베이스(없으면 null). 처음 접근 시 Resources 에서 로드.</summary>
    public static GameDatabase Database
    {
        get
        {
            if (_cached == null && !_tried)
            {
                _tried = true;
                _cached = Resources.Load<GameDatabase>(ResourcePath);
                if (_cached == null)
                {
                    Debug.LogWarning(
                        $"[GameDatabaseProvider] Resources/{ResourcePath}.asset 을 찾지 못했습니다. " +
                        "캐릭터 점수/금액 테이블 조회가 비활성화됩니다(코드 폴백 사용).");
                }
            }
            return _cached;
        }
    }

    /// <summary>테스트/에디터에서 명시적으로 DB 를 주입(결정론·EditMode 검증용).</summary>
    public static void Override(GameDatabase db)
    {
        _cached = db;
        _tried = true;
    }

    /// <summary>캐시 초기화(테스트 격리용).</summary>
    public static void Reset()
    {
        _cached = null;
        _tried = false;
    }
}
