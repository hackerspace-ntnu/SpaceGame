using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Animator))]
public class UIButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private Button button;
    
    [Header("Sound")]
    [SerializeField] private EventReference hoverSound;
    [SerializeField] private EventReference pressSound;

    [SerializeField] private Animator animator;
    
    private static readonly int State = Animator.StringToHash("State");

    private enum ButtonState
    {
        Normal = 0,
        Highlighted = 1,
        Pressed = 2,
        Disabled = 3
    }
    
    private bool IsDisabled => button != null && !button.interactable;
    
    private void SetState(ButtonState state)
    {
        animator.SetInteger(State, (int)state);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        AudioManager.Instance.PlayEvent(hoverSound);
        
        SetState(ButtonState.Highlighted);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        SetState(ButtonState.Normal);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        AudioManager.Instance.PlayEvent(pressSound);
        
        SetState(ButtonState.Pressed);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        SetState(ButtonState.Highlighted);
    }
    
    /// <summary>
    /// Use this method to change button interactability.
    /// Automatically triggers the correct animation.
    /// </summary>
    public void SetInteractable(bool value)
    {
        button.interactable = value;
        SetState(value ? ButtonState.Normal : ButtonState.Disabled);
    }
    
}
