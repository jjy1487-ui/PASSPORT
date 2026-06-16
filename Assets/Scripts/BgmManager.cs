using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬이 바뀌어도 끊기지 않고 이어지는 배경음악(BGM) 싱글톤.
/// DontDestroyOnLoad 로 살아남아, 지정한 '음악 씬'(타이틀/메인메뉴/브리핑/결과) 사이를 오갈 때
/// 같은 AudioSource 가 계속 재생된다(재시작 없음 = 연속). 그 외 씬(심사 DayN 등)에선 정지.
///
/// 각 음악 씬에 이 프리팹을 하나씩 두면, 먼저 로드된 인스턴스만 살아남고
/// 나머지는 Awake 에서 스스로 파괴한다(중복/재시작 방지). 어느 음악 씬에서 시작하든 동작.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class BgmManager : MonoBehaviour
{
    public static BgmManager Instance { get; private set; }

    [Header("BGM")]
    [SerializeField] private AudioClip _clip;
    [Range(0f, 1f)]
    [SerializeField] private float _volume = 0.6f;

    [Tooltip("이 BGM 이 재생될 씬 이름들. 그 외 씬에선 정지한다.")]
    [SerializeField] private string[] _playScenes =
    {
        "TitleScene", "MainMenuScene", "BriefingScene", "ResultScene",
    };

    private AudioSource _src;

    private void Awake()
    {
        // 이미 BGM 매니저가 살아 있으면(다른 음악 씬에서 넘어옴) 이 인스턴스는 파괴 →
        // 기존 재생이 끊기거나 처음부터 다시 시작되지 않는다(연속 재생 보장).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _src = GetComponent<AudioSource>();
        _src.clip = _clip;
        _src.loop = true;
        _src.playOnAwake = false;   // 재생 타이밍은 ApplyForScene 이 제어
        _src.spatialBlend = 0f;     // 2D
        _src.volume = _volume;

        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyForScene(scene.name);

    /// <summary>현재 씬이 음악 씬이면 (이미 재생 중이 아닐 때만) 재생, 아니면 정지.
    /// '이미 재생 중이면 그대로 둔다'가 핵심 — 음악 씬끼리 이동 시 끊김 없이 이어진다.</summary>
    private void ApplyForScene(string sceneName)
    {
        if (_src == null) return;
        bool shouldPlay = System.Array.IndexOf(_playScenes, sceneName) >= 0;
        if (shouldPlay)
        {
            if (_src.clip != null && !_src.isPlaying) _src.Play();
        }
        else
        {
            if (_src.isPlaying) _src.Stop();
        }
    }
}
