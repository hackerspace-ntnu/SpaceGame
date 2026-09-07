// Telling the holder why the draught will not take what it is pointed at.
//
// A use that silently does nothing is indistinguishable from an item that is broken, and the
// canister has four separate ways to say no that all look identical from behind it: too big,
// somebody is riding it, it is not something the world can put back, and its prefab is not
// registered. ContainmentFit already writes each of those as a player-facing sentence; this is the
// half that puts one on the screen (GDC-L1-UX-0003).
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay.Containment;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The one line of text the canister shows its holder.
    ///
    /// <para>
    /// <b>Owner-only, and the caller is what gates it.</b> The message goes to
    /// <see cref="PlayerHints"/>, which draws on the local visor — so a peer running this for
    /// somebody else's canister would put a stranger's refusal on this player's screen.
    /// <see cref="VacuumCanisterArtifact"/> asks <c>OwnerIsLocal</c> before it calls in.
    /// </para>
    /// <para>
    /// <b>Not an <c>ICrosshairReadout</c>.</b> That interface is resolved once, off the player's own
    /// body, the first time the visor finds an <c>Interactor</c> — a held item is instantiated into
    /// a hand long afterwards and is never in that array, so a readout written as one would be
    /// collected on exactly zero machines. The message channel is the honest surface for a held
    /// item.
    /// </para>
    /// <para>
    /// <b>The answer is memoised per target.</b> <c>ContainmentFit.TryFit</c> walks every collider
    /// on a body to measure it, and this is asked every frame the trigger is down — the same reason
    /// <c>ContainmentPull</c> remembers a target it has already refused.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VacuumCanisterReadout : MonoBehaviour
    {
        /// <summary>
        /// Which message this owns. Addressed by id so a late clear cannot take down somebody
        /// else's newer message, and so a second refusal replaces the first rather than stacking.
        /// </summary>
        private const string MessageId = "vacuum.canister";

        [Tooltip("Played once when the draught first settles on something it cannot take. Once, " +
                 "not per frame: a refusal repeated fifteen times a second is a buzz, not feedback.")]
        [SerializeField] private SfxId refusedSound = SfxId.InteractDenied;

        /// <summary>The body the standing answer was worked out for. Null means nothing is shown.</summary>
        private GameObject answered;

        /// <summary>Is a message up right now? Kept so <see cref="Clear"/> is cheap and idempotent.</summary>
        private bool showing;

        /// <summary>
        /// Say whatever needs saying about <paramref name="target"/>, or take the message down when
        /// there is nothing under the draught.
        ///
        /// <para>
        /// Called every frame the canister is drawing, and safe to call with the same target for as
        /// long as that lasts: the fit is only asked when the target actually changes.
        /// </para>
        /// </summary>
        public void Show(GameObject target, ContainmentSettings settings)
        {
            if (target == null)
            {
                Clear();
                return;
            }

            if (target == answered) return;

            answered = target;

            // A body that fits gets no words. The draught closing on it is the feedback, and a
            // line saying so would be one more thing competing for attention with the thing it is
            // describing.
            if (ContainmentFit.TryFit(target, settings, out string refusal))
            {
                Hide();
                return;
            }

            PlayerHints.Show(MessageId, refusal);
            showing = true;

            Sfx.Play(refusedSound, transform.position, GetInstanceID());
        }

        /// <summary>Nothing is under the draught any more. Forgets the standing answer too.</summary>
        public void Clear()
        {
            answered = null;
            Hide();
        }

        private void Hide()
        {
            if (!showing) return;

            showing = false;
            PlayerHints.Hide(MessageId);
        }

        /// <summary>
        /// A canister put away must not leave its refusal on the screen — and putting it away is
        /// the ordinary response to being told no.
        /// </summary>
        private void OnDisable() => Clear();
    }
}
