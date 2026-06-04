using UnityEngine;

/// <summary>
/// Resources/GameData/day{N}.json 을 읽어 <see cref="Day1Data"/>로 역직렬화한다.
/// (Day1Data 는 "하루치" 데이터 DTO. 이름은 씬/직렬화 호환 위해 유지.)
/// MonoBehaviour가 아닌 일반 클래스. 실패 시 null 반환(호출부에서 가드).
/// </summary>
public sealed class GameDataLoader
{
    // Resources.Load 경로(확장자 제외). day{N}.json -> "GameData/day{N}"
    private const string ResourcePrefix = "GameData/day";

    /// <summary>지정한 일차(1~14) 데이터를 로드한다. 실패하면 null.</summary>
    public Day1Data Load(int day)
    {
        string path = $"{ResourcePrefix}{day}";
        TextAsset asset = Resources.Load<TextAsset>(path);
        if (asset == null)
        {
            // 파일이 없으면 그날은 미구현으로 처리(상위에서 종료/대기 판단).
            Debug.LogError($"[GameDataLoader] Resources/{path}.json 을 찾을 수 없습니다. (day {day} 미구현)");
            return null;
        }

        try
        {
            Day1Data data = JsonUtility.FromJson<Day1Data>(asset.text);
            if (data == null || data.customers == null || data.customers.Length == 0)
            {
                Debug.LogError($"[GameDataLoader] day{day}.json 파싱 결과가 비어 있습니다.");
                return null;
            }
            return data;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameDataLoader] day{day}.json 파싱 실패: {e.Message}");
            return null;
        }
    }

    /// <summary>하위호환: 인자 없는 호출은 1일차 로드로 위임.</summary>
    public Day1Data Load() => Load(1);
}
