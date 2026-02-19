using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [Header("References")]
    private Interactor playerInteractor;
    [SerializeField] private RawImage crosshairImage;

    [Header("Alpha")]
    [Range(0f, 1f)] public float idleAlpha = 0.25f;
    [Range(0f, 1f)] public float activeAlpha = 1f;
    public float fadeSpeed = 10f;

    private float _currentAlpha;

    void Update()
    {
        if(!playerInteractor) return;
        if (!crosshairImage) return;

        float targetAlpha = (playerInteractor && playerInteractor.IsHoveringInteractable)
            ? activeAlpha
            : idleAlpha;

        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        Color color = crosshairImage.color;
        color.a = _currentAlpha;
        crosshairImage.color = color;
    }
    
    public void Bind(Interactor interactor)
    {
        playerInteractor = interactor;
    }
}