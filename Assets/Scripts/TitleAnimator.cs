using UnityEngine;

public class TitleAnimator : MonoBehaviour
{
    [SerializeField] RectTransform logoRect;
    [SerializeField] CanvasGroup pressAnyKeyGroup;

    [SerializeField] float bobAmplitude = 15f;
    [SerializeField] float bobSpeed = 1f;
    [SerializeField] float pulseSpeed = 1.5f;
    [SerializeField] float pulseMin = 0.3f;

    Vector2 logoOrigin;

    void Start()
    {
        if (logoRect != null)
            logoOrigin = logoRect.anchoredPosition;
    }

    void Update()
    {
        if (logoRect != null)
        {
            float yOffset = Mathf.Sin(Time.time * bobSpeed * Mathf.PI) * bobAmplitude;
            logoRect.anchoredPosition = logoOrigin + new Vector2(0f, yOffset);
        }

        if (pressAnyKeyGroup != null)
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed * Mathf.PI) + 1f) * 0.5f;
            pressAnyKeyGroup.alpha = Mathf.Lerp(pulseMin, 1f, t);
        }
    }
}
