// The jumping rod: plant it and you bounce, high, for as long as you leave it out.
//
// It is deliberately NOT a vehicle. There is no craft to spawn, no seat, no mount camera and no
// second control scheme — the player keeps their own body, their own camera and their own WASD
// throughout, and all the rod does is throw them back up every time they come down. That is the
// whole difference between "a pogo stick" and "a machine you get into".
//
// This file is the item: what a press means, what equipping and unequipping do, and what survives
// in the hotbar slot. The bouncing itself is in JumpingRodItem.Bounce.cs.
//
// The authority split, which is the whole of its netcode:
//
//   * The bounce is the HOLDER'S OWN BODY, so it is owner-authoritative. The player's transform is
//     owner-authoritative already; a server-applied impulse would be overwritten within a tick and
//     silently (see the spacegame-multiplayer skill).
//   * The rod being OUT is cosmetic on every other machine, so it toggles in Present().
//   * The spring's squash is a pure function of the player's clearance, so every machine works it
//     out from a pose it already has and nothing about the bounce needs sending anywhere.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Locomotion;
using SpaceGame.Gear.JumpingRod;

namespace SpaceGame.Items
{
    public partial class JumpingRodItem : UsableItem
    {
        /// <summary>
        /// The effect is the holder's own body and nothing else, which is exactly the case
        /// <see cref="UseAuthority.Owner"/> exists for.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        [Header("Deployed rod")]
        [Tooltip("The rod as it appears once planted — model plus its spring rig. A plain visual " +
                 "with no NetworkObject: every machine instantiates its own copy from Present(), " +
                 "which is how an equipped visual is done here. Registering it as a network prefab " +
                 "would be wrong and would spawn a second one on the host.")]
        [SerializeField] private GameObject deployedPrefab;

        [Tooltip("Longest-axis size of the planted rod, metres. Tuned so the handlebar lands about " +
                 "where the hold pose puts the player's hands while the tip sits at their feet.")]
        [SerializeField, Min(0.2f)] private float deployedSize = 1.45f;

        [Tooltip("Trim on top of where the rod is put automatically. Its height is NOT set here: " +
                 "the tip is hung one contact band below the player's own soles, worked out from " +
                 "their collider, so the rod cannot drift from the height the bounce fires at. " +
                 "Z stands it off in front of them.")]
        [SerializeField] private Vector3 deployedOffset = new Vector3(0f, 0f, 0.22f);

        [Header("Bounce")]
        [SerializeField] private JumpingRodConfig hop = new JumpingRodConfig();

        [Tooltip("Layers the tip can push off.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far below the player's SOLES to look for ground. Only has to cover one " +
                 "physics step's worth of descent plus the contact band.")]
        [SerializeField, Min(0.3f)] private float probeDistance = 2f;

        [Tooltip("How far ABOVE the soles each probe starts, so ground they have already sunk " +
                 "into is still found. A ray started inside a mesh reports nothing.")]
        [SerializeField, Min(0.05f)] private float probeLift = 0.5f;

        // ── State ──────────────────────────────────────────────────────────────
        //
        // `planted` is flipped in Present(), which runs on EVERY machine and — on the owner — runs
        // BEFORE Use() (EquipmentController.OnUse presents first, so no item ever feels like it is
        // waiting for a reply). One flag therefore serves both halves and neither can drift from
        // the other. Nothing here depends on that ordering being right, though: FixedUpdate
        // re-asserts the movement flag from `planted` every step rather than toggling it at the
        // press.

        private bool planted;
        private GameObject deployed;
        private JumpingRodSpring spring;

        private PlayerMovement holderMovement;
        private Rigidbody holderBody;

        /// <summary>
        /// Ground sampling that rejects the holder's own colliders <i>and</i> anything under its own
        /// physics.
        ///
        /// The second half is why this is <c>WalkerGround</c> rather than a bare
        /// <c>Physics.Raycast</c>: a crate underfoot, a creature, another player bouncing past are
        /// none of them ground, and a probe that called them ground would have the rod pushing off
        /// their heads. See that class's own notes — a machine does not stand on its cargo.
        /// </summary>
        private WalkerGround ground;

        /// <summary>
        /// Where the holder's soles are, which is not where their transform is.
        ///
        /// <para>
        /// The player's pivot sits about a metre above their feet — see <see cref="BodyFeet"/>.
        /// Every height this item works in is a clearance under the soles: the contact band is
        /// 0.12 m and the squash band is 0.5 m, so a probe measured from the pivot reports a metre
        /// of air while the player is stood flat on the floor, and NOTHING the rod does can ever
        /// fire. That was this item's original bug, and it is silent in every direction — no
        /// exception, no warning, an artifact that simply does nothing.
        /// </para>
        /// </summary>
        private BodyFeet feet;

        /// <summary>Whether the rod is planted and bouncing.</summary>
        public bool IsPlanted => planted;

        // ── Equip / unequip ────────────────────────────────────────────────────

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            holderMovement = holder != null ? holder.GetComponent<PlayerMovement>() : null;
            holderBody = holder != null ? holder.GetComponent<Rigidbody>() : null;

            ground = holder != null
                ? new WalkerGround(holder.transform, groundMask, probeLift, probeDistance)
                : null;

            feet = holder != null ? new BodyFeet(holder.transform) : null;

            // A rod that was out when this slot was last held comes back out. RestoreItemState has
            // already run by now on a load, so this is also the path that puts a saved ride back.
            if (planted) Plant();
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            Stow();
        }

        private void OnDestroy() => Stow();

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Every machine. The rod going out and coming in is the whole of what a peer needs to see,
        /// so the flag lives here rather than in <see cref="Use"/>.
        ///
        /// Deliberately unconditional — there is no refusal. Planting in mid-air is a legitimate
        /// move (you simply land on it), and an item that sometimes declines a press is an item
        /// whose visual and whose effect can disagree, because <c>PlayUse</c> is not gated on
        /// <c>CanUse</c> and <c>TryUse</c> is.
        /// </summary>
        protected override void Present()
        {
            planted = !planted;

            if (planted) Plant();
            else Stow();
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // Every held object is a fresh Instantiate destroyed on unequip, so "the rod is out" has to
        // live in the hotbar slot or a player who scrolled past their own rod would come back to it
        // stowed while still bouncing.

        private const string PlantedKey = "planted";

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null || !planted) return;

            state.Set(PlantedKey, true);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            planted = state != null && state.GetBool(PlantedKey);

            // Not planted here: on a restore this runs before OnEquipped has found the holder, and
            // there is nothing to parent a rod to yet. OnEquipped reads the flag and does it.
            if (!planted) Stow();
        }
    }
}
