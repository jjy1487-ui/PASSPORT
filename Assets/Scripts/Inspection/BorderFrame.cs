using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 교차 대조 하이라이트용 9-slice 테두리 스프라이트(Resources/UI/border_frame)를 1회 로드해 캐시한다.
/// 채움 없이 외곽선만 그리는 공통 헬퍼. 인스펙터에 _borderSprite 가 연결되지 않은 뷰가 폴백으로 사용한다.
/// 표시 전용 — 판정/대조 로직 없음.
/// </summary>
public static class BorderFrame
{
    public const string ResourcePath = "UI/border_frame";

    private static Sprite _sprite;
    private static bool _loaded;

    /// <summary>9-slice 테두리 스프라이트(없으면 경고 후 null).</summary>
    public static Sprite Sprite
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _sprite = Resources.Load<Sprite>(ResourcePath);
                if (_sprite == null)
                    Debug.LogWarning($"[BorderFrame] Resources/{ResourcePath} 스프라이트를 찾지 못했습니다.");
            }
            return _sprite;
        }
    }

    /// <summary>Image 를 "채움 없는 9-slice 테두리"로 설정하고 테두리 색(불투명)을 적용한다.</summary>
    public static void ApplyBorder(Image img, Color color)
    {
        if (img == null) return;
        Sprite s = Sprite;
        if (s != null)
        {
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.pixelsPerUnitMultiplier = 1f;
        }
        Color c = color; c.a = 1f;
        img.color = c;
    }
}
