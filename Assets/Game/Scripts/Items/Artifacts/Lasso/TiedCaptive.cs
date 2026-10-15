using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Interact (right mouse) on somebody tied up: heave them onto your shoulder — or put them down again.
    ///
    /// <para>
    /// An interaction rather than an item reading the button itself, so the crosshair names it
    /// ("Tied up — RMB: carry") and so it is chosen over the captive's own dialogue, which refuses
    /// while they are tied (see <c>Interactor.OfferingAlong</c>). Put on the body by
    /// <see cref="BoundBody"/> on every machine; offered only while the body is under
    /// <see cref="StatusKind.Bound"/>.
    /// </para>
    /// <para>
    /// <c>Interact</c> runs only on the presser's machine and hands the press to <see cref="CaptiveHaul"/>. A presser
    /// leading the captive on their own lasso lets go of the lead first (<see cref="LassoArtifact.ToggleLead"/>, on the
    /// rope's own channel, which the server therefore hears before the request to carry).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class TiedCaptive : MonoBehaviour, IInteractable, IInteractionReadout, IInteractionMoment
    {
        public bool CanInteract() => StatusReceiver.HasStatus(gameObject, StatusKind.Bound);

        public void Interact(Interactor interactor)
        {
            if (interactor == null || !CanInteract() || !TryGetComponent(out CaptiveHaul haul)) return;

            GameObject who = NetChannel.RootOf(interactor);
            LassoArtifact lasso = interactor.GetComponentInChildren<LassoArtifact>();
            bool leading = lasso != null && lasso.Leads(gameObject);
            if (leading && haul.MayTakeHold(who, givingUpLead: true)) lasso.ToggleLead(gameObject);

            haul.Toggle(who, leading);
        }

        /// <summary>The press moves nobody's body here: the hold's own take-hold clip does.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public string Label => "Tied up";

        public string Prompt
        {
            get
            {
                if (!TryGetComponent(out CaptiveHaul haul)) return null;
                if (haul.Hauler != null) return haul.HauledByLocalPlayer ? "RMB / Esc: put down" : "Somebody is carrying them";

                Transform local = GameplayMenuScope.LocalPlayerTransform;
                if (local != null && !haul.FitsOnShoulderOf(local.gameObject)) return "Too big to carry";
                if (CuttableRopes.AnyBinds(gameObject))
                {
                    LassoArtifact lasso = local != null ? local.GetComponentInChildren<LassoArtifact>() : null;
                    return lasso != null && lasso.Leads(gameObject) ? "RMB: carry over your shoulder" : "Roped";
                }

                return "RMB: carry over your shoulder";
            }
        }

        /// <summary>How far they are through working free — left still. Moving them sets it back.</summary>
        public float? Value01 => Escape > 0f ? Escape : (float?)null;

        public string ValueText => Escape > 0f ? "Working free" : null;

        private float Escape => TryGetComponent(out BoundBody bound) ? bound.EscapeProgress : 0f;
    }
}
