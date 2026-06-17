using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 모든 씬에 AudioListener(소리 수신기)가 하나는 있도록 보장하는 전역 안전망.
/// AudioListener 가 없는 씬에선 AudioSource 가 재생돼도 소리가 전혀 안 들린다
/// (예: ResultScene 의 Main Camera 에 AudioListener 가 빠져 있어 결과화면 음악이 안 들리던 문제).
///
/// 게임 시작 + 씬 로드 때마다 검사해서, 리스너가 하나도 없으면 그 씬의 카메라
/// (카메라도 없으면 새 오브젝트)에 AudioListener 를 붙인다. 이미 있으면 아무것도 안 한다(중복 경고 방지).
/// 수동 배치 불필요.
/// </summary>
public static class AudioListenerGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    private static void Ensure()
    {
        // 이미 AudioListener 가 있으면 그대로 둔다(중복 리스너 경고 방지).
        if (Object.FindFirstObjectByType<AudioListener>() != null) return;

        // 없으면: 씬의 카메라(우선)에, 카메라도 없으면 새 오브젝트에 하나 붙인다.
        Camera cam = Object.FindFirstObjectByType<Camera>();
        GameObject host = cam != null ? cam.gameObject : new GameObject("[AudioListener]");
        if (host.GetComponent<AudioListener>() == null) host.AddComponent<AudioListener>();
    }
}
