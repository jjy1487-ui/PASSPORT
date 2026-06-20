using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 심사(게임플레이) 씬에서 배경으로 '사람들 웅성거리는 소리'를 반복 재생하는 전역 싱글톤.
/// 게임 시작 시 자동 생성(DontDestroyOnLoad)되어 모든 씬에서 동작 — 수동 배치 불필요.
/// 클립은 Resources/Audio/Ambience 에서 로드.
/// Day1~14Scene(및 ImmigrationScene)에서만 재생하고, 그 외 씬(타이틀/메뉴/브리핑/결과/상점)에선 정지한다.
/// (그 씬들엔 BgmManager 가 Main Theme 를 트므로 음악과 겹치지 않는다 — 심사 중엔 음악 없이 앰비언스만.)
/// </summary>
public sealed class AmbienceManager : MonoBehaviour
{
    private static AmbienceManager _instance;

    private AudioSource _src;
    private AudioClip _clip;
    private const float Volume = 0.35f; // 배경이라 낮게

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[AmbienceManager]");
        _instance = go.AddComponent<AmbienceManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;       // 앰비언스는 반복
        _src.spatialBlend = 0f; // 2D
        _src.volume = Volume;
        _clip = Resources.Load<AudioClip>("Audio/Ambience");
        if (_clip == null) Debug.LogWarning("[AmbienceManager] Resources/Audio/Ambience 클립을 찾지 못했습니다.");
        _src.clip = _clip;

        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (_instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>엔딩 컷씬 등에서 군중 앰비언스를 즉시 멈춘다(다음 씬 로드 시 규칙대로 재평가됨).</summary>
    public static void Suspend()
    {
        if (_instance != null && _instance._src != null && _instance._src.isPlaying)
            _instance._src.Stop();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyForScene(scene.name);

    /// <summary>심사(게임플레이) 씬이면 (이미 재생 중이 아닐 때만) 재생, 아니면 정지.</summary>
    private void ApplyForScene(string sceneName)
    {
        if (_src == null || _clip == null) return;
        bool gameplay = sceneName.StartsWith("Day") || sceneName == "ImmigrationScene";
        if (gameplay)
        {
            if (!_src.isPlaying) _src.Play();
        }
        else
        {
            if (_src.isPlaying) _src.Stop();
        }
    }
}
