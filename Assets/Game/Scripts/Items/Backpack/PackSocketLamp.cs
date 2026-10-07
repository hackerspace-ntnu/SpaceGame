using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The two status lamps on the rig's valve block: one lit whenever the socket holds a supply
    /// with something left in it, the other lit the rest of the time.
    ///
    /// <para>
    /// <b>Derived, never stored.</b> Everything it needs is already in <see cref="PackLayout"/> —
    /// what is in the socket, and how full — and the layout already replicates
    /// (<c>BackpackNetwork</c> sends a charge byte per placement) and already saves
    /// (<c>PackSaveCodec</c>). So this holds no state of its own, sends nothing, and saves nothing:
    /// every machine computes the same answer from the contents it was already given, for its own
    /// pack and for every pack it can see. A lamp that replicated its own bool would be a second
    /// copy of a fact, and the first thing to disagree with the tank it describes.
    /// </para>
    /// <para>
    /// <b>Event-driven, off the same signal the hose uses.</b> <see cref="PackLayout.OnChanged"/>
    /// fires when a tank goes in or comes out, and — the half that matters here — when
    /// <c>PackLayout.SetCharge</c> writes a drain back. <see cref="Gameplay.OxygenSocket"/> writes
    /// back on every whole-percent step and forces a write **exactly on empty**, so the moment the
    /// last of the air goes is the moment this flips, rather than a percent either side of it.
    /// </para>
    /// <para>
    /// <b>Empty is not the same as absent, and both light the same lamp.</b> The socket reports a
    /// dead tank as connected — that is right for <see cref="Gameplay.OxygenSocket"/>, which has to
    /// keep hold of it — but a lamp saying "plumbed in" over a tank with no air in it would answer
    /// the question the player is not asking. What the lamp claims is that the rig is *supplying*,
    /// which needs both halves.
    /// </para>
    /// <para>
    /// This is a redundant channel, not the readout: the visor gauge and the tank's own emissive
    /// gauge are what the wearer reads (<c>SupplyCharge.Describe</c>). These lamps face backwards,
    /// so they are for the people around you and for a pack lying open on the sand.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PackSocketLamp : MonoBehaviour
    {
        [Tooltip("The container whose socket these lamps report on.")]
        [SerializeField] private PackContainer container;

        [Tooltip("Which socket. Asked by kind rather than by face, so a battery bay would need " +
                 "only a second component and no change here.")]
        [SerializeField] private SupplyKind kind = SupplyKind.Oxygen;

        [Tooltip("Lit while the socket holds a supply of that kind with charge left in it.")]
        [SerializeField] private Renderer suppliedLamp;

        [Tooltip("Lit the rest of the time: nothing in the socket, or what is in it is empty.")]
        [SerializeField] private Renderer starvedLamp;

        private PackLayout watched;

        private void OnEnable()
        {
            Watch();
            Refresh();
        }

        private void OnDisable()
        {
            if (watched != null) watched.OnChanged -= Refresh;
            watched = null;
        }

        /// <summary>
        /// Re-subscribe if the container swapped its layout out from under us — the same guard
        /// <see cref="PackHose"/> makes, and for the same reason: a restore replaces contents
        /// wholesale and a handler bound to the old layout would go quiet without ever erroring.
        /// </summary>
        private void Watch()
        {
            if (container == null) return;

            PackLayout layout = container.Layout;
            if (layout == watched) return;

            if (watched != null) watched.OnChanged -= Refresh;
            watched = layout;
            if (watched != null) watched.OnChanged += Refresh;
        }

        /// <summary>Show whichever lamp the socket's contents call for right now.</summary>
        public void Refresh()
        {
            Watch();

            bool supplied = Measure();

            // `enabled`, not SetActive: the lamps are children of the model prefab instance, and
            // deactivating one would take any component a later pass hangs off it down with it.
            if (suppliedLamp != null) suppliedLamp.enabled = supplied;
            if (starvedLamp != null) starvedLamp.enabled = !supplied;
        }

        private bool Measure()
        {
            if (container == null) return false;
            if (!container.TryFindSocketed(kind, out PackPlacement socketed)) return false;

            return ChargeOf(socketed) > 0f;
        }

        /// <summary>
        /// How full the socketed supply is. A placement can still carry <see cref="SupplyCharge.None"/>
        /// — <c>AdoptPlacements</c> replays a record's own charge without defaulting it — and None
        /// means "has never been through a container that knows about charges", which every other
        /// path in the game reads as the item's AUTHORED starting charge. Reading it as zero here
        /// would light the empty lamp over a full tank the player has never touched.
        /// </summary>
        private float ChargeOf(PackPlacement placement) =>
            placement.Charge >= 0f
                ? placement.Charge
                : SupplyCharge.StartingChargeOf(container.ItemFor(placement.ItemId));
    }
}
