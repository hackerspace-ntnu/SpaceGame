using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// H cycles how much of the helmet visor is drawn: Full → Vitals only → Off → Full.
    ///
    /// <para>
    /// This used to be a plain on/off switch, and its comment said that health, crosshair and
    /// hotbar deliberately stayed OUT of the toggled layer because they are "readouts you play by".
    /// They are on the visor now, so a two-state toggle would let the player hide their own health
    /// bar. The middle state is what preserves the original intent: the annotations — the AR
    /// markers and the world commentary — go away, and everything you play by stays.
    /// </para>
    /// <para>
    /// Lives on the PlayerHUD canvas root rather than on the layer it switches, for the obvious
    /// reason: a component cannot re-enable a GameObject it has just deactivated. That is also why
    /// what gets switched are the visor's two sublayers, never this object.
    /// </para>
    /// </summary>
    public class HelmetOverlayVisibility : MonoBehaviour
    {
        [Tooltip("Action in the project-wide input asset that cycles the visor. Bound to H.")]
        [SerializeField] private string toggleActionName = "Hud";

        private InputAction toggleAction;
        private HelmetHUDController helmet;

        /// <summary>The detail level after this one. Wraps back to Full.</summary>
        public static int NextDetail(int detail) =>
            detail >= GameSettings.VisorDetailOff ? GameSettings.VisorDetailFull : detail + 1;

        /// <summary>Whether the things you play by are drawn at this detail level.</summary>
        public static bool ShowsVitals(int detail) => detail != GameSettings.VisorDetailOff;

        /// <summary>Whether the things that describe the world are drawn at this detail level.</summary>
        public static bool ShowsAnnotations(int detail) => detail == GameSettings.VisorDetailFull;

        /// <summary>The detail level currently applied.</summary>
        public int Detail => GameSettings.VisorDetail;

        private void Awake()
        {
            // includeInactive, so a visor the player switched off last session — or one that starts
            // off — is still found rather than leaving H doing nothing.
            helmet = GetComponentInChildren<HelmetHUDController>(includeInactive: true);
            if (helmet == null)
            {
                Debug.LogWarning("[HelmetOverlayVisibility] No HelmetHUDController under this canvas " +
                                 "— nothing to toggle.", this);
            }

            toggleAction = InputSystem.actions?.FindAction(toggleActionName);
            if (toggleAction == null)
                Debug.LogWarning($"[HelmetOverlayVisibility] Input action '{toggleActionName}' not found.", this);
        }

        // Applied on enable rather than only on press, so the level chosen last session is in
        // force from the first frame the HUD comes back.
        private void OnEnable() => Apply(GameSettings.VisorDetail);

        private void Update()
        {
            // The UI action map stays live under every menu, so the press has to be qualified: H
            // belongs to the player, not to whatever panel is on top of them.
            if (toggleAction != null && toggleAction.WasPressedThisFrame() && GameplayMenuScope.AcceptsGameplayInput)
                SetDetail(NextDetail(GameSettings.VisorDetail));
        }

        /// <summary>Stores the choice and applies it. The chosen level survives a quit.</summary>
        public void SetDetail(int detail)
        {
            GameSettings.VisorDetail = detail;
            Apply(detail);
        }

        private void Apply(int detail)
        {
            // The message channel is pushed to rather than switched, because it does not live
            // under the visor: it is a DontDestroyOnLoad overlay, so that the arrival can still
            // announce things at the moments the player's whole HUD is switched off. It reads as
            // part of the visor and hides with it, but H cannot reach it by deactivating a parent.
            //
            // The banner is deliberately NOT silenced at Vitals level: a warning is the one thing
            // on the visor allowed to interrupt, and "I turned the markers off" is not consent to
            // stop being told the suit is failing.
            VisorMessageStack.SetShown(ShowsAnnotations(detail));
            VisorWarningBanner.SetShown(ShowsVitals(detail));

            if (helmet == null) return;

            // The root stays active at every level: it owns the sublayers, and a controller that
            // deactivated itself could not switch them back on again.
            helmet.gameObject.SetActive(true);

            if (helmet.Vitals != null) helmet.Vitals.gameObject.SetActive(ShowsVitals(detail));
            if (helmet.Annotations != null) helmet.Annotations.gameObject.SetActive(ShowsAnnotations(detail));
        }
    }
}
