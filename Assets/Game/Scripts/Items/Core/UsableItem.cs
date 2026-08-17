using System;
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>Which machine runs an item's effect.</summary>
    public enum UseAuthority
    {
        /// <summary>
        /// The server. For anything that changes the shared world — spawning a turret, dealing
        /// damage, consuming something another player could also have taken.
        /// </summary>
        Server,

        /// <summary>
        /// The player holding it. For tools whose whole effect is that player's own body moving:
        /// the grapple, the lasso, the leash, a potion that changes how you fall. Their body is
        /// already owner-authoritative, so the result replicates through the transform they own,
        /// and routing the swing through the server would put a round trip inside the feel of it.
        /// </summary>
        Owner,
    }

    /// <summary>
    /// Base for everything the player can hold and trigger.
    ///
    /// One use splits into two jobs that used to be one, because they belong on different machines:
    ///
    ///   • <see cref="Use"/> — what the use DOES. Spawning, damage, consuming a charge. Runs only
    ///     where this entity has authority, which is the server, or everywhere in single-player.
    ///   • <see cref="Present"/> — what the use LOOKS AND SOUNDS LIKE. Runs on every machine,
    ///     immediately on the owner's so the item never feels like it is waiting for a round trip.
    ///
    /// Splitting it here rather than per item is what makes every artifact — the ones that exist
    /// and the ones nobody has written yet — networked by default. An item that overrides neither
    /// still works; it simply does its work on the server only.
    /// </summary>
    public abstract class UsableItem : MonoBehaviour
    {
        [SerializeField] private int maxUses = -1; // -1 means unlimited uses

        // Deliberately None rather than a sensible-looking default: Weapon derives from this and
        // plays its own fire sound, so anything non-None here would double up on every shot. Items
        // that want a use sound opt in, per item.
        [Tooltip("Sound this item makes when used. Leave at None for items whose own logic makes the noise.")]
        [SerializeField] protected SfxId useSoundId = SfxId.None;
        [SerializeField] protected EventReference useSound;

        private int currentUses = 0;

        protected GameObject owner;

        /// <summary>
        /// What the owner reported about this use — chiefly where they were aiming.
        ///
        /// A remote machine cannot recompute that: it has neither the owner's camera nor their
        /// exact frame. Whatever <see cref="OnRequestUse"/> put in here is what every machine,
        /// including the server, works from.
        /// </summary>
        protected NetArg UseArg { get; private set; }

        public event Action<UsableItem> OnItemDepleted;

        /// <summary>Where <see cref="Use"/> runs. See <see cref="UseAuthority"/>.</summary>
        public virtual UseAuthority Authority => UseAuthority.Server;

        /// <summary>
        /// Owner-side, before the request leaves: add anything the other machines cannot work out
        /// for themselves. Aim points go in <c>P</c>, orientations in <c>R</c>.
        /// </summary>
        public virtual void OnRequestUse(ref NetArg arg) { }

        /// <summary>
        /// Authority-side: actually use the item. Called on the server, or on the only machine
        /// there is when offline. Never on a peer.
        /// </summary>
        public void TryUse(GameObject useOwner, NetArg arg = default)
        {
            owner = useOwner;
            UseArg = arg;

            if (!CanUse()) return;

            Use();
            currentUses++;

            // Check if we've reached max uses
            if (maxUses >= 0 && currentUses >= maxUses)
            {
                OnMaxUsesReached();
            }
        }

        /// <summary>
        /// Every machine: play the use. Sound always, plus whatever <see cref="Present"/> draws.
        ///
        /// Deliberately not gated on <see cref="CanUse"/>. The authority already decided the use
        /// happened; re-deciding here from a peer's copy of the charge count is how one machine
        /// ends up silently skipping an effect everyone else saw.
        /// </summary>
        public void PlayUse(GameObject useOwner, NetArg arg = default)
        {
            owner = useOwner;
            UseArg = arg;

            Sfx.Play(useSoundId, transform.position, useSound, GetInstanceID());

            Present();
        }

        protected virtual bool CanUse()
        {
            // Prevent use if max uses reached
            if (maxUses >= 0 && currentUses >= maxUses)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Called when the item reaches its maximum number of uses.
        /// Override in subclasses for custom behavior.
        /// </summary>
        protected virtual void OnMaxUsesReached()
        {
            OnItemDepleted?.Invoke(this);
        }

        /// <summary>
        /// Lifecycle hook fired by EquipmentController right after the item prefab is
        /// instantiated and parented to the player's hand. Use for "while held"
        /// effects (animation flags, audio loops, glow, etc). Subclasses overriding
        /// this should call base.OnEquipped() so the shared HoldAnimator wiring
        /// still fires.
        /// </summary>
        public virtual void OnEquipped(GameObject holder)
        {
            var hold = GetComponent<HoldAnimator>();
            if (hold != null) hold.SetHeld(holder, true);
        }

        /// <summary>
        /// Lifecycle hook fired by EquipmentController right before the item prefab
        /// is unparented/destroyed. Mirror of OnEquipped — clean up here.
        /// </summary>
        public virtual void OnUnequipped(GameObject holder)
        {
            var hold = GetComponent<HoldAnimator>();
            if (hold != null) hold.SetHeld(holder, false);
        }

        /// <summary>Authority-only effect. See the class summary.</summary>
        protected abstract void Use();

        /// <summary>
        /// Cosmetic half, on every machine. Empty by default: most items either affect only their
        /// own owner, or change the world through objects that replicate themselves. Override it
        /// for effects a peer would otherwise never see — a VFX burst, a beam, a muzzle flash.
        /// </summary>
        protected virtual void Present() { }
    }
}
