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
    public abstract class UsableItem : MonoBehaviour, IItemStateCarrier
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
        /// Does this item keep acting for as long as the button is held?
        ///
        /// False for everything that existed before the laser staff, and that is the point: a
        /// press-and-forget item is untouched by the held-use path, which never runs for it. An
        /// item that answers true additionally gets <see cref="OnRequestHold"/>,
        /// <see cref="Hold"/> and <see cref="PresentHold"/> called on the same three machines, at
        /// the send rate EquipmentController streams at, until the button comes up.
        ///
        /// A continuous item still gets the ordinary press first. That is deliberate rather than
        /// incidental: the press is what plays the ignition sound and what counts against
        /// <c>maxUses</c>, so a held item is a normal item that also happens to keep going.
        /// </summary>
        public virtual bool IsContinuous => false;

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

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // Every held object in this game is a fresh Instantiate of the item prefab, destroyed on
        // unequip — see ItemState. So anything an item has BECOME lives in the hotbar slot rather
        // than on the instance, and these two methods are how it gets there and back.
        //
        // The charge count is here because it belongs to every item: a limited-use artifact whose
        // charges refilled every time the player scrolled past it was not a save bug, it was a
        // gameplay bug that a save merely made visible.

        /// <summary>State key for the charge count. Written into save files — never rename.</summary>
        private const string UsesKey = "uses";

        /// <summary>
        /// Write what this instance would otherwise lose. Subclasses override and call base.
        /// </summary>
        public virtual void CaptureItemState(ItemState state)
        {
            if (state == null) return;

            // An unlimited item has no count worth storing, and storing a zero for every artifact in
            // the game would put a bag on every slot that has nothing in it.
            if (maxUses >= 0 && currentUses > 0) state.Set(UsesKey, currentUses);
        }

        /// <summary>
        /// Apply a captured bag. Runs after <see cref="OnEquipped"/>, so it is free to overwrite
        /// whatever equipping set up.
        /// </summary>
        public virtual void RestoreItemState(ItemState state)
        {
            // A null bag is "this item is at its defaults", which for a fresh instance is already
            // true — but the reset is written out rather than assumed, because the same instance can
            // be handed a bag and then handed none.
            currentUses = state == null ? 0 : state.GetInt(UsesKey, 0);
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

        // ── Held use ───────────────────────────────────────────────────────────
        //
        // The same request/authority/present split as a press, because a hold has the same three
        // machines with the same three jobs. `active` is false on the final tick, which is the
        // release — every override must treat that as "stop", including the case where it never
        // saw the ticks that came before it.

        /// <summary>
        /// Owner-side, before each hold tick leaves: describe the aim, which is knowable only here.
        /// </summary>
        public virtual void OnRequestHold(ref NetArg arg, bool active) { }

        /// <summary>Authority-side: keep doing the thing. See <see cref="TryUse"/>.</summary>
        public void TryHold(GameObject useOwner, NetArg arg, bool active)
        {
            owner = useOwner;
            UseArg = arg;
            Hold(arg, active);
        }

        /// <summary>Every machine: keep showing the thing. See <see cref="PlayUse"/>.</summary>
        public void PlayHold(GameObject useOwner, NetArg arg, bool active)
        {
            owner = useOwner;
            UseArg = arg;
            PresentHold(arg, active);
        }

        /// <summary>Authority-only, once per hold tick. Empty unless the item is continuous.</summary>
        protected virtual void Hold(NetArg arg, bool active) { }

        /// <summary>Cosmetic half of a hold tick, on every machine.</summary>
        protected virtual void PresentHold(NetArg arg, bool active) { }

        /// <summary>
        /// Lifecycle hook fired by EquipmentController right after the item prefab is
        /// instantiated and parented to the player's hand. Use for "while held"
        /// effects (animation flags, audio loops, glow, etc). Subclasses overriding
        /// this should call base.OnEquipped() so the shared HoldAnimator wiring
        /// still fires.
        /// </summary>
        public virtual void OnEquipped(GameObject holder)
        {
            // Set here and not only in TryUse/PlayUse, because OnRequestUse runs BEFORE either of
            // those on the very first use of a freshly equipped item. Anything that reads the
            // holder to describe a use — an aim ray, a muzzle, a velocity — would otherwise find
            // null exactly once per equip.
            owner = holder;

            // Give the item a hold pose whether or not anyone remembered to author one.
            //
            // This used to read the component and do nothing when it was absent, which made the
            // pose opt-in and silently so — an artifact without a HoldAnimator does not fail or
            // warn, it just stands in the idle tree holding a gun. Four of eleven equippable
            // artifacts had the component; the other seven were the bug.
            //
            // An authored component is left exactly as it is, because it carries per-prefab
            // tuning. This only fills the gap.
            var hold = GetComponent<HoldAnimator>();
            if (hold == null && UsesHoldPose) hold = gameObject.AddComponent<HoldAnimator>();
            if (hold != null) hold.SetHeld(holder, true);
        }

        /// <summary>
        /// Whether holding this item should pose the holder's body.
        ///
        /// <para>
        /// True for anything gripped. Override to false for something worn rather than held — a
        /// pack, a suit module — where posing the arms as though gripping it is wrong.
        /// </para>
        /// </summary>
        protected virtual bool UsesHoldPose => true;

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
