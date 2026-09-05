using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The one connection between a carried oxygen tank and the person breathing it: the reserved
    /// socket on the worn pack.
    ///
    /// <para>
    /// <b>A tank supplies you from here and from nowhere else.</b> Not from your hand, not from the
    /// mat, not from a hotbar slot — that is the whole rule, and it is why this class is a lookup
    /// rather than a search: it asks the pack what is standing in its socket, and anything else the
    /// player is carrying is cargo. The alternative, draining "the fullest tank you own", would make
    /// the socket decorative and the pack's plumbing a lie.
    /// </para>
    /// <para>
    /// <b>The live value is held here, not in the pack.</b> A placement's charge lives in
    /// <see cref="PackLayout"/>, and writing it raises <c>OnChanged</c>, which republishes the whole
    /// contents list to every machine (<c>BackpackNetwork</c>). Draining straight into it would do
    /// that sixty times a second. So the drain runs on <see cref="charge"/> and is written back only
    /// when the whole percent the player reads actually changes — a hundred writes over a full tank,
    /// one every eighteen seconds at the standard rate, instead of a hundred thousand.
    /// </para>
    /// <para>
    /// Not a MonoBehaviour. It is owned by <see cref="SuitOxygen"/> and takes the body it hangs off,
    /// so every rule in it can be exercised in an EditMode test with a pack and no player at all.
    /// </para>
    /// </summary>
    public sealed class OxygenSocket
    {
        /// <summary>
        /// The step at which the drain is written back to the pack, as a fraction. One whole
        /// percent, which is exactly the resolution <see cref="SupplyCharge.Describe"/> shows —
        /// writing back more often would publish changes no readout can display.
        /// </summary>
        private const float WriteBackStep = 0.01f;

        private readonly GameObject body;

        private BackpackController backpack;

        /// <summary>The key of the tank currently plugged in, or null when the socket is empty.</summary>
        private string key;

        /// <summary>How full that tank is. Authoritative between write-backs.</summary>
        private float charge;

        /// <summary>Its capacity in seconds, cached off the item's prefab.</summary>
        private float capacity;

        /// <summary>What was last written back to the pack, so a write only happens on a real step.</summary>
        private float published;

        public OxygenSocket(GameObject body) => this.body = body;

        /// <summary>Is a tank plugged in at all? An EMPTY tank still counts as plugged in.</summary>
        public bool Connected => key != null;

        /// <summary>How full the connected tank is, 0..1. Zero when nothing is connected.</summary>
        public float Charge => key != null ? charge : 0f;

        /// <summary>Seconds of air left in the connected tank. Zero when nothing is connected.</summary>
        public float Seconds => key != null ? charge * capacity : 0f;

        /// <summary>
        /// Re-read what is in the socket. Cheap, and called every authoritative tick: the player
        /// can swap tanks at any moment and nothing tells this class when they do.
        ///
        /// <para>
        /// A tank whose KEY is unchanged keeps this class's own <see cref="charge"/> rather than
        /// taking the pack's — the pack's copy is up to one percent stale by construction, so
        /// adopting it every tick would quantise the drain into visible one-percent jumps and lose
        /// the fraction of a percent drained since the last write-back.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            PackContainer pack = Pack();

            if (pack == null || !pack.TryFindSocketed(SupplyKind.Oxygen, out PackPlacement socketed))
            {
                Release();
                return;
            }

            if (socketed.ItemId == key) return;

            // A different tank — or the first one. Adopt its charge and its capacity wholesale.
            key = socketed.ItemId;
            charge = Mathf.Clamp01(socketed.Charge);
            published = charge;
            capacity = SupplyCharge.CapacityOf(pack.ItemFor(socketed.ItemId));
        }

        /// <summary>
        /// Take <paramref name="seconds"/> of air out of the connected tank, and say how much was
        /// actually there to take. Less than asked means the tank ran dry inside this tick, and the
        /// suit's own reserve covers the difference.
        /// </summary>
        public float Draw(float seconds)
        {
            if (key == null || seconds <= 0f || capacity <= 0f) return 0f;

            float available = charge * capacity;
            float taken = Mathf.Min(available, seconds);

            charge = Mathf.Clamp01((available - taken) / capacity);
            WriteBack();

            return taken;
        }

        /// <summary>
        /// Push the live value back into the pack, so it replicates, saves, and shows on the tank's
        /// own gauge. Only on a whole-percent step — or on empty, which must land exactly rather
        /// than being rounded to the step below it.
        /// </summary>
        private void WriteBack(bool force = false)
        {
            if (key == null) return;

            bool empty = charge <= 0f;
            if (!force && !empty && Mathf.Abs(published - charge) < WriteBackStep) return;

            PackContainer pack = Pack();
            if (pack == null) return;

            pack.SetCharge(key, charge);
            published = charge;
        }

        /// <summary>
        /// Let go of the tank, writing the last of the drain back first.
        ///
        /// <para>
        /// The write-back is the half that matters. Without it, every fraction of a percent drained
        /// since the last step is thrown away the instant the player pulls the tank out — which is
        /// small, and would also be the ONLY thing that happened if they pulled it out and put it
        /// straight back, making a tank that never empties.
        /// </para>
        /// </summary>
        private void Release()
        {
            if (key == null) return;

            WriteBack(force: true);

            key = null;
            charge = 0f;
            capacity = 0f;
            published = 0f;
        }

        /// <summary>
        /// Write the live value back whatever step it is on. Called before a save, so what the file
        /// records is the tank as it actually is rather than as it was up to a percent ago.
        /// </summary>
        public void Flush() => WriteBack(force: true);

        private PackContainer Pack()
        {
            if (body == null) return null;

            if (backpack == null) backpack = body.GetComponentInChildren<BackpackController>(true);

            return backpack != null ? backpack.Pack : null;
        }
    }
}
