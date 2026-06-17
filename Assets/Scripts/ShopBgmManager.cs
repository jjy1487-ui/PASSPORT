using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 상점 배경음악을 결과(정산)화면과 상점 씬에서 '연속'으로 재생하는 전역 싱글톤.
/// 게임 시작 시 자동 생성(DontDestroyOnLoad) — 수동 배치 불필요. 클립은 Resources/Audio/ShopBgm.
///
/// 흐름: 결과화면 진입 → (일차 마치는 소리 → 정산 소리) → ResultSceneManager 가 Play() 호출 → 상점 BGM 시작
///       → [상점] 버튼 → 상점 씬에서도 끊김 없이 계속 → [돌아가기]로 결과화면 복귀해도 계속.
///       결과·상점 외 씬(브리핑/심사 등)으로 가면 정지한다.
/// 결과화면 진입 시 '자동'으로 틀지 않는다(인트로 소리 뒤에 시작해야 하므로) — ResultSceneManager 가 시점을 제어.
/// 상점 씬에 직접 들어오면(혹은 BGM 이 꺼져 있으면) 바로 재생한다.
/// </summary>
public sealed class ShopBgmManager : MonoBehaviour
{
    public static ShopBgmManager Instance { get; private set; }

    private AudioSource _src;
    private AudioClip _clip;
    private const float Volume = 0.6f;
    private static readonly string[] PlayScenes = { "ResultScene", "ShopScene" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[ShopBgmManager]");
        go.AddComponent<ShopBgmManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;
        _src.spatialBlend = 0f; // 2D
        _src.volume = Volume;
        _clip = Resources.Load<AudioClip>("Audio/ShopBgm");
        if (_clip == null) Debug.LogWarning("[ShopBgmManager] Resources/Audio/ShopBgm 클립을 찾지 못했습니다.");
        _src.clip = _clip;

        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyForScene(scene.name);

    private void ApplyForScene(string sceneName)
    {
        if (_src == null) return;
        bool playScene = System.Array.IndexOf(PlayScenes, sceneName) >= 0;
        if (!playScene)
        {
            if (_src.isPlaying) _src.Stop(); // 결과/상점 밖으로 나가면 정지(브리핑 등은 Main Theme)
            return;
        }
        // 상점 씬: 바로 재생(이미 재생 중이면 유지 = 연속).
        // 결과화면: 자동 재생 안 함 — ResultSceneManager 가 인트로 소리 뒤 Play() 호출. (상점 왕복 복귀 시엔 이미 재생 중이라 그대로 유지)
        if (sceneName == "ShopScene") Play();
    }

    /// <summary>상점 배경음악을 시작한다. 이미 재생 중이면 그대로 둔다(재시작 없음 = 연속).</summary>
    public void Play()
    {
        if (_src == null || _clip == null) return;
        if (!_src.isPlaying) _src.Play();
    }
}
