using System;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Base for items whose whole effect is a timed change to the holder's own body — the
    /// anti-gravity potion, and every speed boost, slow and damage-over-time written after it.
    ///
    /// <para>
    /// These split across machines differently from every other artifact, and getting the split
    /// wrong is invisible in single-player because single-player runs as a host, where the two
    /// machines are the same machine. The split is:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// <b>Consuming it</b> — the charge count, and the inventory slot it is removed from — belongs
    /// to the server. That is <see cref="UseAuthority.Server"/>'s own definition: "consuming
    /// something another player could also have taken". It runs once per session rather than once
    /// per machine, and the removal replicates through <see cref="PlayerInventoryNetwork"/>'s
    /// hotbar, which is server-authoritative already.
    /// </description></item>
    /// <item><description>
    /// <b>The physical effect</b> belongs to the machine that OWNS the body, and to no other. The
    /// player's NetworkTransform is <c>AuthorityMode: Owner</c>, so a Rigidbody the server pushes
    /// is overwritten by that owner's next state update, within a tick and silently — the same
    /// failure <see cref="NetworkedTeleport"/> exists for, and the same one that made a rope's pull
    /// on a player have to be handed back to them as <see cref="NetMsg.RopeTug"/>. This is the
    /// third of that set.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// So the effect is applied from <see cref="Present"/> — the half of a use that runs on EVERY
    /// machine — filtered on ownership. On the owner that is immediate, with no round trip inside
    /// the feel of it. On the server and on the other two players it is skipped, which is not
    /// merely wasteful: a replica the local machine does not own is made kinematic by
    /// <c>NetworkRigidbody</c> (AutoUpdateKinematicState), so applying it there would silently
    /// bank a <c>useGravity = false</c> on a body that cannot move, ready to take effect the moment
    /// something hands that body back its physics.
    /// </para>
    /// </summary>
    public abstract class EffectItem : UsableItem
    {
        /// <summary>
        /// The server, for the consumption. See the class summary for why that is not the same
        /// question as which machine runs the effect.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>
        /// Nothing, and sealed so it stays nothing.
        ///
        /// <para>
        /// The authority-only half of a use is exactly the wrong place for an effect on the
        /// holder's body, and it is the obvious-looking place — this is where every EffectItem
        /// subclass put its <see cref="RegisterEffect"/> call, and a potion drunk by a client
        /// therefore floated the server's kinematic copy of them and nothing else. Sealing it
        /// means the next person to write an EffectItem cannot make that mistake by following the
        /// shape of <see cref="UsableItem"/>: the only hook they are offered is
        /// <see cref="ApplyEffect"/>, which runs where the body is.
        /// </para>
        /// <para>
        /// Consuming the item still happens, on the server, in <see cref="UsableItem.TryUse"/> —
        /// it counts the use against <c>maxUses</c> and fires the depletion that takes the item out
        /// of the hotbar. That needs no code here.
        /// </para>
        /// </summary>
        protected sealed override void Use() { }

        /// <summary>
        /// Every machine: play the cosmetics, and on the one machine that owns the body, apply the
        /// effect.
        /// </summary>
        protected override void Present()
        {
            PresentEffect();

            // The one line this whole class exists for. Everyone runs Present; only the owner of
            // the body being changed may change it.
            if (!OwnsAffectedBody()) return;

            ApplyEffect();
        }

        /// <summary>
        /// Is the body this effect targets ours to move?
        ///
        /// <para>
        /// Asked of the HOLDER rather than of this component. An equipped item is parented into the
        /// holder's hand but carries its own NetworkObject on several prefabs, and
        /// <see cref="Network.Owns"/> walks up to the nearest one — asked of the item it would
        /// answer about an unspawned NetworkObject, which is always "yes, this is yours", on all
        /// four machines at once.
        /// </para>
        /// </summary>
        private bool OwnsAffectedBody() =>
            owner != null && Network.Owns(owner.transform);

        /// <summary>
        /// Cosmetics for the use — a flash, a puff, a drinking sound. Runs on every machine,
        /// including the ones that skip the effect itself, because a peer still has to see that
        /// somebody drank something.
        /// </summary>
        protected virtual void PresentEffect() { }

        /// <summary>
        /// Owner-side: register what this item does to the holder's body, normally by calling
        /// <see cref="RegisterEffect"/>. Never call it directly — <see cref="Present"/> decides
        /// whether this machine is allowed to.
        /// </summary>
        protected abstract void ApplyEffect();

        /// <summary>
        /// Hand a timed effect to the holder's <see cref="EffectManager"/>.
        ///
        /// <para>
        /// Keyed on the item's own type, so a second potion of the same kind REPLACES the first
        /// rather than stacking beside it. That is not tidiness: an effect that turns gravity off
        /// on apply and back on when it expires cannot overlap with itself. Two anti-gravity
        /// potions drunk four seconds apart used to leave the first one's expiry switching gravity
        /// back on in the middle of the second one's float — and the per-item field that was
        /// supposed to prevent it could not, because the second potion is a freshly instantiated
        /// prefab that has never heard of the first.
        /// </para>
        /// </summary>
        protected void RegisterEffect(float duration, Action<Rigidbody> onApply,
            Action<Rigidbody> onTick, Action<Rigidbody> onStop) =>
            RegisterEffect(new Effect(duration)
            {
                Key = GetType(),
                applyEffect = onApply,
                onTick = onTick,
                stopEffect = onStop,
            });

        /// <summary>
        /// Hand a pre-built effect to the holder's <see cref="EffectManager"/>.
        ///
        /// <para>
        /// The overload an item uses when its effect is built by a STATIC factory — which is what
        /// <see cref="EffectCatalog"/> needs in order to rebuild it after a load, since the item
        /// instance that produced it is long destroyed by then. One path either way: the delegate
        /// overload above funnels into this, so a use and a restore go through the same door.
        /// </para>
        /// <para>
        /// <see cref="Effect.Key"/> is filled in from this item's type when the factory left it
        /// empty, because keying on the item is what makes a second potion replace the first rather
        /// than stack with it.
        /// </para>
        /// </summary>
        protected void RegisterEffect(Effect effect)
        {
            if (effect == null) return;

            effect.Key ??= GetType();

            if (owner == null)
            {
                Debug.LogWarning($"[Effect] '{name}' was used with no holder, so there is no body " +
                                 "to affect.", this);
                return;
            }

            var effectManager = owner.GetComponent<EffectManager>();
            if (effectManager == null)
            {
                Debug.LogWarning($"[Effect] '{owner.name}' has no EffectManager, so '{name}' does " +
                                 "nothing at all. Add one beside the Rigidbody.", this);
                return;
            }

            effectManager.AddEffect(effect);
        }
    }
}
