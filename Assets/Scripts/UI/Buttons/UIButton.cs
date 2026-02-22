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

    [Header("Animator Triggers")]
    [SerializeField] private string normalTrigger = "Normal";
    [SerializeField] private string hoverTrigger = "Highlighted";
    [SerializeField] private string pressTrigger = "Pressed";
    [SerializeField] private string disabledTrigger = "Disabled";

    [SerializeField] private Animator animator;
    
    private bool IsDisabled => button != null && !button.interactable;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        AudioManager.Instance.PlayEvent(hoverSound);
        
        animator.SetTrigger(hoverTrigger);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        animator.SetTrigger(normalTrigger);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        AudioManager.Instance.PlayEvent(pressSound);
        
        animator.SetTrigger(pressTrigger);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsDisabled) return;
        
        animator.SetTrigger(normalTrigger);
    }
    
    /// <summary>
    /// Use this method to change button interactability.
    /// </summary>
    public void SetInteractable(bool value)
    {
        button.interactable = value;
        animator.SetTrigger(value ? normalTrigger : disabledTrigger);
    }
    
}