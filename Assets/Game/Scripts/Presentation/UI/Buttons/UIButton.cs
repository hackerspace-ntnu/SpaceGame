using FMODUnity;
using SpaceGame.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceGame.Agents;

namespace SpaceGame.Presentation
{
    [RequireComponent(typeof(Animator))]
    public class UIButton : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        [SerializeField] private Button button;
    
        [Header("Sound")]
        [SerializeField] private SfxId hoverId = SfxId.UiHover;
        [SerializeField] private EventReference hoverSound;
        [SerializeField] private SfxId pressId = SfxId.UiPress;
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
        
            // Went through AudioManager, which only exists on Bootstrap.unity — pressing Play
            // directly in MainMenu.unity left it null and the NRE aborted the handler before
            // SetState below, so the visible symptom was "buttons don't highlight". Sfx needs no
            // manager at all, which removes the hazard rather than null-guarding it.
            Sfx.Play2D(hoverId, hoverSound);

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
        
            Sfx.Play2D(pressId, pressSound);

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
}
