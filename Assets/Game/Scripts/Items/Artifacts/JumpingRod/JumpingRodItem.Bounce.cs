// The bouncing half of the jumping rod: the ground probe, the hop, and the rod the player can see
// under them. JumpingRodItem.cs is the item itself — what a press means and what survives a slot
// change.
//
// Split here because the line is also the authority line. Everything in this file that MOVES is
// owner-only; everything that is DRAWN runs on every machine. Keeping the two in one method is how
// an item like this ends up applying an impulse on a peer once per physics step for the rest of
// the session.
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gear.JumpingRod;

namespace SpaceGame.Items
{
    public partial class JumpingRodItem
    {
        private float lastBounceTime = float.NegativeInfinity;

        /// <summary>Clearance under the holder's feet as of the last probe, metres.</summary>
        public float HeightAboveGround { get; private set; }

        /// <summary>
        /// Fastest descent seen since the last bounce, as a positive speed.
        ///
        /// <para>
        /// The touchdown step cannot be asked how hard the arrival was. At 50 Hz a player falling
        /// at the rod's own cruise speed covers 0.22 m per step, which is wider than the whole
        /// contact band — so the probe usually first sees them on the step AFTER the collision has
        /// already taken their velocity away, and reading the arrival off that frame hands back
        /// the cruise hop for every landing however far it fell. Remembering the fall instead is
        /// what makes <see cref="JumpingRodHopModel.TakeoffSpeed"/>'s whole range reachable:
        /// bounce off a cliff and you go higher, which is the promise of the thing.
        /// </para>
        /// </summary>
        private float arrivalSpeed;

        // ── The bounce ─────────────────────────────────────────────────────────

        /// <summary>
        /// The owner, once the press has been presented. Its one job is the first hop.
        ///
        /// Without it, planting the rod does nothing until the player happens to sink into the
        /// contact band — which, standing still, they already are, so the next bounce is a physics
        /// step away and it looks like it worked; planting mid-fall or on a slope does not. Kicking
        /// off here makes the press always do something on the frame it was pressed.
        /// GDC-L1-FEEL-0002.
        /// </summary>
        protected override void Use()
        {
            if (!planted || holderBody == null) return;

            Probe();
            if (HeightAboveGround > hop.ContactHeight) return;   // in the air; nothing to push off

            Bounce(0f);
        }

        private void FixedUpdate()
        {
            // Only the holder's own machine moves the holder's own body. On a peer this player is a
            // replicated copy whose PlayerMovement is switched off, and writing velocity into it
            // would be a write netcode discards once per physics step for the rest of the session.
            if (!OwnerIsLocal() || holderBody == null) return;

            // Re-asserted every step rather than toggled at the press, so the flag cannot be left
            // set by a teardown path that did not run — and fall damage that stayed suppressed
            // would be a bug the player only finds out about from a cliff.
            if (holderMovement != null) holderMovement.SetBouncing(planted);

            if (!planted) return;

            Probe();

            Vector3 v = holderBody.linearVelocity;

            // Before either gate, so a fall that happens during the lockout is still remembered.
            arrivalSpeed = Mathf.Max(arrivalSpeed, -v.y);

            if (Time.time - lastBounceTime < hop.RebounceLockout) return;
            if (!JumpingRodHopModel.HasTouchedDown(HeightAboveGround, v.y, hop.ContactHeight)) return;

            Bounce(arrivalSpeed);
        }

        /// <summary>
        /// Throw the holder up.
        ///
        /// Writing <c>linearVelocity.y</c> outright is safe, and is why no new impulse API was
        /// added to PlayerMovement: its FixedUpdate writes only x and z, so the vertical axis is
        /// already free for whatever wants it. It is SET rather than added: how hard the arrival
        /// was is priced once, by <see cref="JumpingRodHopModel.TakeoffSpeed"/>, and adding the
        /// take-off on top of whatever downward velocity happens to be left on the body at the
        /// moment of the write would make the first bounce of a fall mysteriously short.
        /// </summary>
        private void Bounce(float arrival)
        {
            float takeoff = JumpingRodHopModel.TakeoffSpeed(arrival, hop);

            Vector3 v = holderBody.linearVelocity;
            holderBody.linearVelocity = new Vector3(v.x, takeoff, v.z);

            lastBounceTime = Time.time;
            arrivalSpeed = 0f;

            Sfx.Play(SfxId.PlayerJump, holderBody.position, GetInstanceID());
        }

        /// <summary>
        /// How much air is under the holder's SOLES — never under their transform, which on this
        /// player sits a metre higher and would report a metre of clearance while they stand on
        /// the floor. See <see cref="feet"/>.
        /// </summary>
        private void Probe()
        {
            if (ground == null || feet == null || owner == null)
            {
                HeightAboveGround = probeDistance;
                return;
            }

            Vector3 pivot = owner.transform.position;
            float drop = feet.RootAboveFeet;

            HeightAboveGround = ground.Below(new Vector3(pivot.x, pivot.y - drop, pivot.z),
                                             out RaycastHit hit)
                ? JumpingRodHopModel.Clearance(pivot.y, drop, hit.point.y)
                // Nothing underneath: a hole, an unstreamed chunk, a hop out over a canyon.
                // Answering "very high" keeps the player falling rather than bouncing off a ray
                // that found nothing.
                : probeDistance;
        }

        // ── The rod the player can see ─────────────────────────────────────────

        /// <summary>
        /// Put the rod under the player, on whichever machine is asking.
        ///
        /// A plain <c>Instantiate</c> parented to the holder, never a networked spawn: an equipped
        /// visual belongs to the machine drawing it, and one that replicated would arrive late,
        /// leave late and need a prefab registration it has no business having.
        /// </summary>
        private void Plant()
        {
            if (deployed != null || deployedPrefab == null || owner == null) return;

            // A rod that has just come out has not seen a fall: without this, stowing it mid-drop
            // and planting it again later spends that old descent on the first bounce.
            arrivalSpeed = 0f;

            Bounds bounds = ItemBounds.Measure(deployedPrefab, null);
            float scale = ScaleFor(bounds, deployedSize);

            // Hung by its TIP, one contact band under the holder's own soles, so the rod is on
            // the ground at the moment the bounce fires and not a moment before. Both halves of
            // that come from the body rather than from authored numbers: the drop to the soles is
            // this player's, and the drop to the tip is this model's.
            float height = JumpingRodHopModel.TipOffset(
                feet != null ? feet.RootAboveFeet : 0f, hop.ContactHeight, bounds.min.y * scale);

            deployed = Instantiate(deployedPrefab, owner.transform);
            deployed.transform.localPosition = deployedOffset + Vector3.up * height;
            deployed.transform.localRotation = Quaternion.identity;
            deployed.transform.localScale = Vector3.one * scale;

            spring = deployed.GetComponentInChildren<JumpingRodSpring>(true);

            // One rod, not two: the copy in the hand goes away while the real one is out.
            SetHeldVisible(false);
        }

        private void Stow()
        {
            if (deployed != null) Destroy(deployed);
            deployed = null;
            spring = null;

            if (holderMovement != null) holderMovement.SetBouncing(false);

            SetHeldVisible(true);
        }

        /// <summary>
        /// Drive the coil, on every machine including the ones only watching.
        ///
        /// LateUpdate rather than FixedUpdate so the squash is computed from the pose about to be
        /// drawn — on a peer that pose is interpolated between network ticks, and reading it a
        /// physics step early makes the rod visibly lag the player it hangs under.
        /// </summary>
        private void LateUpdate()
        {
            if (!planted || spring == null) return;

            // Peers never reach FixedUpdate's probe, which is gated on ownership, so the clearance
            // is measured here for them. The owner already has a fresher figure from this step's
            // physics and re-measuring would only cost a second raycast.
            if (!OwnerIsLocal()) Probe();

            spring.SetCompression(
                JumpingRodHopModel.Compression(HeightAboveGround, hop.CompressHeight));
        }

        /// <summary>
        /// Uniform scale that brings something <paramref name="bounds"/> big to
        /// <paramref name="size"/> metres on its longest axis. Measured off the prefab rather than
        /// hard-coded, so re-proportioning the model re-derives the fit instead of leaving a number
        /// here that used to be right.
        /// </summary>
        private static float ScaleFor(Bounds bounds, float size)
        {
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

            return longest > 1e-4f ? size / longest : 1f;
        }

        /// <summary>
        /// Hide the carried rod in the player's hand while the real one is planted. The planted rod
        /// is parented to the PLAYER rather than to this item, so this sweep never reaches it.
        /// </summary>
        private void SetHeldVisible(bool visible)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = visible;
        }
    }
}
