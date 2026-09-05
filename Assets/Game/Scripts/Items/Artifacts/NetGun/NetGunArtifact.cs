using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The net gun.
    ///
    /// <para>
    /// The shot rides <c>UseItem</c> / <c>ItemUsed</c> like every other artifact, carrying the
    /// muzzle in <c>NetArg.P</c>, the aim in <c>R</c> and a rolled seed in <c>B</c>. Every machine
    /// then draws the identical flight from those three, so nothing needs to be said about the net
    /// while it is in the air.
    /// </para>
    /// <para>
    /// There is no authority-side half at all — <see cref="ToolItem"/>'s empty <c>Use</c> stands.
    /// The shot is drawn by <see cref="Present"/> on every machine, and what the net CAUGHT is
    /// decided by <see cref="SnareReceiver"/> when it lands, which is well after this instance may
    /// have stopped existing.
    /// </para>
    /// <para>
    /// <b>This owns the gun, not the nets.</b> Everything about a net that has already left the
    /// barrel — the registry, the capture messages, the landing pass — belongs to
    /// <see cref="SnareReceiver"/> on the shooter, because this instance is destroyed the moment
    /// the player switches hotbar slot. What is left here is what a HELD gun is: where it is
    /// pointing, how many charges it has, and how they come back.
    /// </para>
    /// </summary>
    public class NetGunArtifact : ToolItem
    {
        [Header("Net")]
        [Tooltip("Metres from the net's centre to its edge, so the net is twice this across. " +
                 "Not a radius: the net is SQUARE, and its corners reach out a further 41%.")]
        [SerializeField] private float netHalfWidth = 3f;

        [Tooltip("Cord thickness in metres.")]
        [SerializeField] private float cordWidth = 0.028f;

        [SerializeField] private Material netMaterial;
        [SerializeField] private SnareLattice lattice = new SnareLattice();
        [SerializeField] private SnareStruggle struggle = new SnareStruggle();

        [Header("Gun")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Layers the net may catch. Excluding a layer here is the only way to make a thing " +
                 "un-nettable.")]
        [SerializeField] private LayerMask catchableLayers = ~0;

        [Tooltip("Seconds before one spent charge comes back.\n\n" +
                 "The clock only runs while the gun is HELD — see TickRecharge.")]
        [SerializeField] private float rechargeSeconds = 12f;

        [Tooltip("The bundled net in the canister. Hidden while the gun is empty, which is the " +
                 "whole reason the canister mouth is modelled open.")]
        [SerializeField] private GameObject loadedBundle;

        /// <summary>
        /// Metres added above and below the net's own footprint when deciding what it caught.
        ///
        /// The net is a sheet with no thickness, and the thing under it may be a crouching player
        /// or a six-legged habitat whose origin sits well below the mesh lying over its back. This
        /// is the slack that stops the answer depending on exactly which triangle the drape settled
        /// against.
        /// </summary>
        private const float CaptureHeight = 2.5f;

        /// <summary>State key for the part-elapsed recharge. Written into save files — never rename.</summary>
        private const string RechargeKey = "netgun.recharge";

        private float rechargeElapsed;

        public int ChargesRemaining => Mathf.Max(0, ChargesLeft);

        // ── Test seams ─────────────────────────────────────────────────────────
        //
        // Public because the EditMode tests compile into Assembly-CSharp-Editor, which cannot see
        // internals of Assembly-CSharp — the same seam LassoedBody.Step exposes for the same reason.

        /// <summary>Fire until empty, without a world to fire into.</summary>
        public void SpendAllChargesForTest()
        {
            while (ChargesLeft > 0) TryUse(gameObject);
        }

        /// <summary>Run the recharge clock forward.</summary>
        public void AdvanceRechargeForTest(float seconds) => TickRecharge(seconds);

        /// <summary>
        /// Owner-side: where the net leaves from, and where it is going.
        ///
        /// <para>
        /// Those are two different transforms, and conflating them was the bug. The net comes out
        /// of the BARREL — that is what <c>P</c> is for, and drawing it from anywhere else puts the
        /// bundle in mid-air beside the gun. But it flies along the CAMERA, because the camera is
        /// what the player is aiming with.
        /// </para>
        /// <para>
        /// Sending <c>muzzle.rotation</c> instead sent the gun's own pose, and a held gun is
        /// oriented by <see cref="ItemGrip"/> and the hold pose: it points along the fingers, which
        /// sits a little right of and below the crosshair and barely pitches at all when the player
        /// looks up. So the net went right and down of where it was aimed, and a shot straight up
        /// went out flat and fell in the sand. The gun models the arm; the arm is not the aim.
        /// Reading the player's intent rather than the literal bone is GDC-L1-FEEL-0003.
        /// </para>
        /// <para>
        /// The bore and the eye are a few centimetres apart, so the net flies PARALLEL to the
        /// crosshair rather than through it. That is the ordinary arrangement for a barrel offset
        /// from a camera and it is not worth converging: this net drops on the way out, so nothing
        /// makes the crosshair predict the landing point exactly, and pretending otherwise by
        /// firing from the eye would put the bundle inside the player's head.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            Transform bore = muzzle != null ? muzzle : transform;

            // The bore fires PARALLEL to the aim, from its own position — see above for why this
            // one deliberately does not converge. Parallel to the aim RAY, though, not to the eye's
            // own forward: mounted, the eye is pitched with the seat and points at the floor.
            bool aimed = aimProvider != null && aimProvider.AimTransform != null;

            arg.P = bore.position;
            arg.R = aimed
                ? Quaternion.LookRotation(aimProvider.GetAimRay().direction)
                : bore.rotation;

            // One seed, rolled by the owner, so the scatter on the flight is the same everywhere.
            // The same trick the Gravel Blaster uses to make one shot's spread agree across
            // machines without putting the spread itself on the wire.
            arg.B = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// Which net a shot produced. The SEED, not a counter.
        ///
        /// A counter has to be advanced by either <see cref="Use"/> or <see cref="Present"/>, and
        /// which of those runs first differs by machine — on a host the present happens before the
        /// server's use. So a counter names a different net on the shooter's machine than on
        /// everyone else's, and the capture messages bind captives to the wrong one. The seed is
        /// already identical everywhere and already unique per shot.
        /// </summary>
        private static int ShotNetId(NetArg arg) => arg.B;

        /// <summary>
        /// Every machine, the server included: build the net and hand it to the shooter's receiver.
        ///
        /// Nothing about the flight is sent. Origin, aim and seed all arrived with the press, and
        /// <see cref="NetGunFlight"/> is closed-form, so every machine reaches the same place at the
        /// same moment on its own.
        /// </summary>
        protected override void Present()
        {
            int netId = ShotNetId(UseArg);

            var go = new GameObject($"SnareNet {netId}");
            go.transform.position = UseArg.P;

            var net = go.AddComponent<SnareCatch>();

            // Clone, never the serialized instance: that one is the template a designer tunes, and
            // two nets sharing it share one set of node arrays.
            // Asked of the SHOOTER and not of this gun. Network.Simulates answers from the nearest
            // spawned NetworkObject, and an equipped item never spawns — ask it here and every peer
            // in the session says yes and starts draining its own copy of the net's pool. The
            // shooter's own object is the one that can tell. Offline it answers yes, which is
            // right: with no session there is nobody else to defer to.
            bool decides = Network.Simulates(owner != null ? owner.transform : transform);

            net.Begin(netId, UseArg.P, UseArg.R * Vector3.forward, netHalfWidth, cordWidth,
                      lattice.Clone(), struggle, authority: decides, firedBy: owner);
            net.SetMaterial(netMaterial);

            // Handed straight over. Holding a reference here as well would be a second copy of the
            // registry on an object that is about to be destroyed, which is the bug this split
            // exists to remove.
            SnareReceiver.Ensure(owner)?.Track(netId, net, catchableLayers, CaptureHeight);

            RefreshBundle();
        }

        /// <summary>Show or hide the bundle in the canister, so loaded and spent read at a glance.</summary>
        private void RefreshBundle()
        {
            if (loadedBundle != null) loadedBundle.SetActive(ChargesRemaining > 0);
        }

        private void Update() => TickRecharge(Time.deltaTime);

        /// <summary>
        /// Give a charge back on a timer.
        ///
        /// <para>
        /// The clock only runs while the gun is short of a charge, so a full gun does not
        /// accumulate a stockpile of instant reloads to spend the moment it fires.
        /// </para>
        /// <para>
        /// <b>And only while the gun is HELD.</b> This runs in <see cref="Update"/> on the item
        /// instance, and that instance is destroyed on unequip and rebuilt on equip — so a gun in a
        /// hotbar slot the player is not holding does not recharge. The part-elapsed value survives
        /// in <c>ItemState</c>, so switching away and back resumes rather than restarts. That is
        /// deliberate rather than emergent: the recharge is the gun cycling a fresh net into the
        /// canister, which is something the player is doing with it in hand. Making it wall-clock
        /// instead would need a world-time reference that survives save and load — <c>Time.time</c>
        /// resets, so a stored timestamp would hand back a full gun on every load.
        /// </para>
        /// </summary>
        private void TickRecharge(float delta)
        {
            if (ChargesLeft < 0 || ChargesLeft >= MaxCharges) { rechargeElapsed = 0f; return; }

            rechargeElapsed += delta;
            if (rechargeElapsed < rechargeSeconds) return;

            rechargeElapsed -= rechargeSeconds;
            RefundUse();
            RefreshBundle();
        }

        /// <summary>
        /// Stay silent when the last charge goes.
        ///
        /// The base raises <c>OnItemDepleted</c>, and <c>EquipmentController.ItemDepleted</c>
        /// answers that by removing the item from the inventory — which for a gun that refills would
        /// mean firing three shots deletes it out of the player's hand.
        /// </summary>
        protected override void OnMaxUsesReached() { }

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state != null && rechargeElapsed > 0f) state.Set(RechargeKey, rechargeElapsed);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);
            rechargeElapsed = state == null ? 0f : state.GetFloat(RechargeKey, 0f);
            RefreshBundle();
        }
    }
}
