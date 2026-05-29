using UnityEngine;

/// <summary>
/// Resources/GameData/day1.json 을 읽어 <see cref="Day1Data"/>로 역직렬화한다.
/// MonoBehaviour가 아닌 일반 클래스. 실패 시 null 반환(호출부에서 가드).
/// </summary>
public sealed class GameDataLoader
{
    // Resources.Load 경로(확장자 제외). day1.json -> "GameData/day1"
    private const string ResourcePath = "GameData/day1";

    /// <summary>1일차 데이터를 로드한다. 실패하면 null.</summary>
    public Day1Data Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogError($"[GameDataLoader] Resources/{ResourcePath}.json 을 찾을 수 없습니다.");
            return null;
        }

        try
        {
            Day1Data data = JsonUtility.FromJson<Day1Data>(asset.text);
            if (data == null || data.customers == null || data.customers.Length == 0)
            {
                Debug.LogError("[GameDataLoader] day1.json 파싱 결과가 비어 있습니다.");
                return null;
            }
            return data;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameDataLoader] day1.json 파싱 실패: {e.Message}");
            return null;
        }
    }
}
