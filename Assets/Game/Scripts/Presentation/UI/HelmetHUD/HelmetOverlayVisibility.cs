using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// H switches the helmet overlay off and on — the damage vignette and the AR nav markers that
    /// <see cref="HelmetHUDController"/> draws over the visor.
    ///
    /// Only the helmet. Health, crosshair, hotbar and the death screen are readouts you play by, so
    /// they stay where they are; this is the layer you turn off when you want to look at the world
    /// instead of at warnings about it.
    ///
    /// Lives on the PlayerHUD canvas root rather than on the helmet object it switches, for the
    /// obvious reason: a component cannot re-enable the GameObject it just deactivated. That is also
    /// why the switch is the helmet GameObject and not this one.
    /// </summary>
    public class HelmetOverlayVisibility : MonoBehaviour
    {
        [Tooltip("Action in the project-wide input asset that toggles the helmet overlay. Bound to H.")]
        [SerializeField] private string toggleActionName = "Hud";

        private InputAction toggleAction;
        private HelmetHUDController helmet;

        /// <summary>Whether the helmet overlay is currently drawn.</summary>
        public bool Shown => helmet != null && helmet.gameObject.activeSelf;

        private void Awake()
        {
            // includeInactive, so a helmet the player switched off last session — or one that starts
            // off — is still found rather than leaving H doing nothing.
            helmet = GetComponentInChildren<HelmetHUDController>(includeInactive: true);
            if (helmet == null)
                Debug.LogWarning("[HelmetOverlayVisibility] No HelmetHUDController under this canvas — nothing to toggle.", this);

            toggleAction = InputSystem.actions?.FindAction(toggleActionName);
            if (toggleAction == null)
                Debug.LogWarning($"[HelmetOverlayVisibility] Input action '{toggleActionName}' not found.", this);
        }

        private void Update()
        {
            // The UI action map stays live under every menu, so the press has to be qualified: H
            // belongs to the player, not to whatever panel is on top of them.
            if (toggleAction != null && toggleAction.WasPressedThisFrame() && GameplayMenuScope.AcceptsGameplayInput)
                SetShown(!Shown);
        }

        public void SetShown(bool shown)
        {
            if (helmet != null) helmet.gameObject.SetActive(shown);
        }
    }
}
