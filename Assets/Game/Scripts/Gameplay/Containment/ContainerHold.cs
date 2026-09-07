// The container's side of containment: what it is holding, and how that survives everything.
//
// Put this on the item prefab of anything that can have a living thing inside it. It is the piece
// that makes a FULL container a different item from an empty one — the record rides the item
// instance the way an oxygen bottle's charge does, through the hand, the hotbar slot, the save
// file and the ground.
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// One container and its captive.
    ///
    /// <para>
    /// <b>The encoded string is the truth, not the decoded record.</b> A record this build cannot
    /// parse is still the only copy of a creature that exists anywhere, and decoding eagerly would
    /// turn "I do not understand this" into "there is nothing here". Holding the text and decoding
    /// on demand means a captive survives a build that does not understand it.
    /// </para>
    /// <para>
    /// <b>A bottled PLAYER is deliberately not part of that state.</b> Players are held for a few
    /// seconds and never recorded (<see cref="BottledPlayer"/>), so nothing about one is written to
    /// the slot bag or the save file. A quit-time autosave that captured a bottled player would
    /// reload a world in which somebody cannot move, with nothing in the log to say why — the same
    /// trade <c>Hogtie</c> makes for the same reason.
    /// </para>
    /// <para>
    /// <b>This is not an <c>IItemStateCarrier</c>.</b> <c>EquipmentController.WriteBackHeldItemState</c>
    /// asks the item's <c>UsableItem</c> and nothing else, so a second carrier component beside it
    /// would never be called. The container's own artifact forwards to
    /// <see cref="CaptureInto"/> / <see cref="RestoreFrom"/> from its <c>CaptureItemState</c> /
    /// <c>RestoreItemState</c> overrides instead.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ContainerHold : MonoBehaviour, ISaveable
    {
        /// <summary>
        /// The saver key for a container lying in the world. Written into save files — never rename.
        /// </summary>
        public const string Key = "containment";

        [Tooltip("What this container can take, how long it takes, and how hard a captive may " +
                 "fight it. A bigger canister is a prefab variant carrying a bigger rated volume.")]
        [SerializeField] private ContainmentSettings settings = new();

        /// <summary>The captive, encoded. Null or empty means empty. See the class summary.</summary>
        private string stored;

        /// <summary>The player inside right now, if any. Never persisted — see the class summary.</summary>
        private BottledPlayer bottled;

        /// <summary>
        /// The player carrying this container, or null while it lies in the world.
        ///
        /// Needed for more than convenience: an equipped item's own NetworkObject is never spawned,
        /// so it has neither a relay nor an authority of its own, and both questions have to be
        /// asked of the holder instead.
        /// </summary>
        private GameObject holder;

        /// <summary>
        /// Set on a machine that was TOLD the container is full, and never on the machine that
        /// filled it.
        ///
        /// A peer has no record — the record is server state, and <c>ItemState</c> does not
        /// replicate — but it still has to draw a full canister differently from an empty one, so
        /// the two announcements carry that one bit. The known limit: a peer that re-equips the
        /// item gets a fresh instance and loses the bit, because no wire format carries it.
        /// </summary>
        private bool toldFull;

        /// <summary>What this container can take. Never null.</summary>
        public ContainmentSettings Settings => settings ??= new ContainmentSettings();

        /// <summary>Is there anything in here?</summary>
        public bool IsFull => !string.IsNullOrEmpty(stored) || bottled != null || toldFull;

        /// <summary>Is a player in here right now? They come out on their own — see <see cref="BottledPlayer"/>.</summary>
        public bool HoldsPlayer => bottled != null;

        /// <summary>The player carrying this, or null while it lies in the world.</summary>
        public GameObject Holder => holder;

        /// <summary>
        /// Is this the machine that decides what this container does?
        ///
        /// <para>
        /// Asked of the HOLDER while it is held. An equipped item is instantiated into a hand and
        /// never spawned, so its own NetworkObject is dormant and <c>Network.Simulates</c> would
        /// answer yes on every machine in the session — every peer would then despawn its own copy
        /// of the captive. Lying in the world it is a spawned object in its own right, and then it
        /// is the one that can tell.
        /// </para>
        /// </summary>
        public bool Decides =>
            holder != null ? Network.Simulates(holder.transform) : Network.Simulates(this);

        /// <summary>
        /// The captive, decoded, or null. Answers null — loudly — for a record this build cannot
        /// read; the record itself is kept regardless.
        /// </summary>
        public CaptiveRecord Peek() =>
            CaptiveRecord.TryDecode(stored, out CaptiveRecord record) ? record : null;

        // ── Being carried ──────────────────────────────────────────────────────

        /// <summary>
        /// Say who is holding this. Call from the artifact's <c>OnEquipped</c>.
        ///
        /// <para>
        /// This is what puts a <see cref="ContainmentPull"/> on the captor, and the receiver has to
        /// be there before the first message rather than at the first pull: a player who picked up
        /// a full container and uncorks it never pulls anything, and the uncork announcement has to
        /// land somewhere. It goes on the captor's relay because a held item has none of its own.
        /// </para>
        /// </summary>
        public void Bind(GameObject carrier)
        {
            if (carrier == null) return;

            holder = carrier;
            ContainmentPull.Ensure(carrier)?.Attach(this);
        }

        /// <summary>Let go. Call from the artifact's <c>OnUnequipped</c>.</summary>
        public void Unbind()
        {
            if (holder != null) ContainmentPull.Find(holder)?.Detach(this);
            holder = null;
        }

        // ── Filling and emptying ───────────────────────────────────────────────

        /// <summary>
        /// Put <paramref name="body"/> in. Authority only, and the end of a contest rather than
        /// the start of one — <see cref="ContainmentPull"/> decides when the fight is over.
        /// </summary>
        /// <returns>False when nothing changed, in which case the caller must keep pulling or stop.</returns>
        public bool TryFill(GameObject body)
        {
            if (!Decides || IsFull || body == null) return false;
            if (!ContainmentFit.TryFit(body, Settings, out _)) return false;

            // A player is held, not recorded: the authority takes the hold first, because a hold
            // that is refused (a corpse, a body already in a seat) must not be announced to
            // anybody. Every peer then takes its own hold off the announcement below, exactly as a
            // net's captives do.
            if (ContainmentFit.IsPlayer(body))
            {
                BottledPlayer player = BottledPlayer.Ensure(body);
                if (player == null || !player.Bottle(this, Settings, authority: true)) return false;

                Adopt(player);
                Announce(body);
                return true;
            }

            // BEFORE the despawn, so every machine can play the fold-in on a body it can still see.
            // A creature cannot be pre-checked into certainty the way a player's hold can — the one
            // remaining refusal below is a rider that will not leave a saddle on a body being torn
            // down — so a peer can in principle play a fold-in for a capture that then does not
            // happen. That is a cosmetic hiccup; announcing after the despawn would be a fold-in
            // played on a body nobody can see, which is not.
            Announce(body);

            CaptiveRecord record = Captivity.Capture(body);
            if (record == null) return false;

            stored = CaptiveRecord.Encode(record);
            return true;
        }

        /// <summary>
        /// Let the captive out at <paramref name="point"/>. Authority only.
        ///
        /// <para>
        /// <b>Clearing and spawning are one step.</b> The record is taken out of this container
        /// before the body is built, so a second uncork on the same frame — a doubled input, a
        /// message that arrived twice — finds an empty bottle rather than making a second creature.
        /// It is put back only if the rebuild failed, which is the one case where the captive still
        /// exists and has nowhere else to be.
        /// </para>
        /// </summary>
        /// <returns>The released body, or null when nothing came out.</returns>
        public GameObject TryUncork(Vector3 point, Quaternion rotation)
        {
            if (!Decides || string.IsNullOrEmpty(stored)) return null;
            if (!CaptiveRecord.TryDecode(stored, out CaptiveRecord record)) return null;

            string held = stored;
            stored = null;

            GameObject body = Captivity.Release(record, point, rotation);
            if (body == null)
            {
                // Rebuild refused and said so. The captive is still the only copy there is.
                stored = held;
                return null;
            }

            AnnounceRelease(point);
            return body;
        }

        /// <summary>
        /// A machine elsewhere says this container is full, or empty again. Presentation only —
        /// a peer never holds the record.
        ///
        /// <para>
        /// Ignored on the deciding machine, which holds the record itself and needs telling
        /// nothing. Both announcements go to <c>All</c> and re-enter inline on the host, so acting
        /// on one here would leave a container that had just been emptied still reading as full,
        /// for the rest of its life.
        /// </para>
        /// </summary>
        public void MarkToldFull(bool full)
        {
            if (Decides) return;

            toldFull = full;
        }

        // ── Per-instance state, for the hand and the hotbar slot ───────────────

        /// <summary>
        /// Write the captive into the slot's bag. Call from the artifact's
        /// <c>CaptureItemState</c>, after <c>base</c>.
        /// </summary>
        public void CaptureInto(ItemState state)
        {
            if (state == null || string.IsNullOrEmpty(stored)) return;

            state.Set(Captivity.StateKey, stored);
        }

        /// <summary>
        /// Take the captive back out of the slot's bag. Call from the artifact's
        /// <c>RestoreItemState</c>, after <c>base</c>.
        ///
        /// A null bag means "at its defaults", which for a container is empty — written out rather
        /// than assumed, because the same instance can be handed a bag and then handed none.
        /// </summary>
        public void RestoreFrom(ItemState state)
        {
            stored = state?.GetString(Captivity.StateKey);
        }

        // ── Persistence, for a container lying in the world ────────────────────

        /// <summary>
        /// The payload. A public-field struct rather than the bare string, because
        /// <c>StateBag.Set</c> drops a key whose value is not an object.
        /// </summary>
        public struct State
        {
            public string captive;
        }

        public string SaveKey => Key;

        public object CaptureState() =>
            string.IsNullOrEmpty(stored) ? null : new State { captive = stored };

        public void RestoreState(JObject state)
        {
            stored = state?[nameof(State.captive)]?.Value<string>();
        }

        // ── Internals ──────────────────────────────────────────────────────────

        /// <summary>
        /// Tell every machine the fight is over, on the CAPTIVE's relay.
        ///
        /// <para>
        /// The captive's relay and not this container's, because the container has none: an
        /// equipped item is never spawned. <c>Target</c> is the container, exactly as
        /// <see cref="NetMsg.Contained"/> documents — and for a held one <c>NetArg.IdOf</c> walks
        /// up to the nearest spawned NetworkObject, which is the holder. So a peer resolves the
        /// message to the player whose hand it is in, and finds the container instance there.
        /// </para>
        /// </summary>
        private void Announce(GameObject body) =>
            NetMessaging.NetSendTo(body, NetMsg.Contained, new NetArg().With(gameObject), NetTo.All);

        /// <summary>
        /// Tell every machine a captive is out, on the CAPTOR's relay.
        ///
        /// The captive does not exist yet on a peer — it arrives as an ordinary spawn a moment
        /// later — so there is no captive channel to use, and the container's is the holder's.
        ///
        /// A container with no holder is one lying in the world, which nothing uncorks: it has to
        /// be picked up first. Silence is the honest answer there rather than a warning about a
        /// path no caller takes.
        /// </summary>
        private void AnnounceRelease(Vector3 point)
        {
            if (holder == null) return;

            NetArg arg = new NetArg().With(gameObject);
            arg.P = point;

            NetMessaging.NetSendTo(holder, NetMsg.Released, arg, NetTo.All);
        }

        /// <summary>
        /// Take charge of a bottled player, and let go of them when they get out.
        ///
        /// <para>
        /// The subscription is what keeps this container's own idea of "full" honest, and it is
        /// deliberately one-way: the player's clock runs on their OWN object, so a container
        /// destroyed while somebody is inside it — a hotbar slot switched, a chunk unloaded — still
        /// lets them out. A container is a five-second event to a player, not a place they can be
        /// left.
        /// </para>
        /// </summary>
        private void Adopt(BottledPlayer player)
        {
            bottled = player;
            bottled.Freed += OnBottledPlayerFreed;
        }

        private void OnBottledPlayerFreed(BottledPlayer player)
        {
            if (bottled != player) return;

            bottled.Freed -= OnBottledPlayerFreed;
            bottled = null;
        }

        private void OnDisable()
        {
            // Not a release: the player's own clock is what frees them, and it outlives this. This
            // only stops a destroyed container from being called back.
            if (bottled != null) bottled.Freed -= OnBottledPlayerFreed;
            bottled = null;

            Unbind();
        }
    }
}
