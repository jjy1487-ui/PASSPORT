using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마우스 왼쪽 클릭 시 '클릭 대상'에 맞는 효과음을 재생하는 전역 싱글톤.
/// 게임 시작 시 자동 생성(DontDestroyOnLoad)되어 모든 씬에서 동작한다 — 수동 배치 불필요.
/// 대상별 분기:
///  - 도장 버튼(ApproveStamp/RejectStamp)·NoClickSound 마커 → 무음(자기 효과음만 냄)
///  - 서류 카드(DraggableDocument/PassportDocument) → 종이 소리(Resources/Audio/Paper1~2 랜덤)
///  - 그 외(버튼·빈 곳·배경) → 클릭음(Resources/Audio/MouseClick)
/// 판정/진행 로직과 무관(소리만).
/// </summary>
public sealed class ClickSound : MonoBehaviour
{
    private static ClickSound _instance;

    private float _clickVolume = 0.5f;
    private float _paperVolume = 0.8f;

    private AudioSource _src;
    private AudioClip _clickClip;
    private AudioClip[] _paperClips;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[ClickSound]");
        _instance = go.AddComponent<ClickSound>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f; // 2D
        _clickClip = Resources.Load<AudioClip>("Audio/MouseClick");
        _paperClips = new[]
        {
            Resources.Load<AudioClip>("Audio/Paper1"),
            Resources.Load<AudioClip>("Audio/Paper2"),
        };
        if (_clickClip == null) Debug.LogWarning("[ClickSound] Resources/Audio/MouseClick 클립을 찾지 못했습니다.");
    }

    private static readonly List<RaycastResult> _hits = new List<RaycastResult>();

    private void Update()
    {
        if (_src == null) return;
        Mouse m = Mouse.current;
        if (m == null || !m.leftButton.wasPressedThisFrame) return;
        PlayForClick(m.position.ReadValue());
    }

    /// <summary>클릭 지점의 '맨 위' UI 종류에 맞는 효과음을 재생한다.</summary>
    private void PlayForClick(Vector2 screenPos)
    {
        GameObject top = TopmostUnder(screenPos);
        if (top == null) { Play(_clickClip, _clickVolume); return; } // 빈 곳 → 클릭음

        // 도장 버튼/NoClickSound → 무음(자기 효과음만)
        if (top.GetComponentInParent<NoClickSound>() != null) return;
        Button btn = top.GetComponentInParent<Button>();
        if (btn != null && btn.name.Contains("Stamp")) return;

        // 서류 카드 → 종이 소리(클릭음 대신)
        if (top.GetComponentInParent<DraggableDocument>() != null
            || top.GetComponentInParent<PassportDocument>() != null)
        {
            PlayRandom(_paperClips, _paperVolume);
            return;
        }

        // 그 외(버튼·배경 등) → 클릭음
        Play(_clickClip, _clickVolume);
    }

    /// <summary>화면 좌표 아래 '맨 위' UI 오브젝트(없으면 null = 빈 곳).</summary>
    private static GameObject TopmostUnder(Vector2 screenPos)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return null;
        var data = new PointerEventData(es) { position = screenPos };
        _hits.Clear();
        es.RaycastAll(data, _hits);
        return _hits.Count > 0 ? _hits[0].gameObject : null;
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip != null) _src.PlayOneShot(clip, volume);
    }

    private void PlayRandom(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip != null) _src.PlayOneShot(clip, volume);
    }
}
