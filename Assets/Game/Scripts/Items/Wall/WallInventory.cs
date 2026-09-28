using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A gear wall: a <see cref="PackContainer"/> bolted to something, with no fold, no deploy and
    /// no owner.
    ///
    /// <para>
    /// Everything about holding gear — the layout, the faces, the display copies, the transfers to
    /// and from a hotbar — is <see cref="PackContainer"/>'s and is shared verbatim with the
    /// backpack. What is left here is the two answers a wall gives differently from a rig:
    /// every face is always reachable (nothing folds over anything), and requests go out on the
    /// wall's OWN entity rather than through a wearer. The pack has to borrow its player's channel
    /// because it has no <c>NetworkObject</c>; a wall is part of a ship that has one.
    /// </para>
    /// <para>
    /// <b>It is not an <see cref="IInteractable"/>.</b> Pointing at the wall does not offer one
    /// verb — it offers a different verb per cell, and which one depends on what is in the
    /// player's hand and what is already on the wall under the crosshair. That question is asked
    /// every frame by <see cref="WallAimController"/> on the looking player, which is also the only
    /// place the answer can be drawn. An <c>Interact</c> here would be a second, blinder path to
    /// the same two requests.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallInventory : PackContainer
    {
        /// <summary>
        /// Which wall this is on its entity — the number every message carries so the others drop
        /// it. Resolved on first use rather than in Awake, so a wall added at runtime, or built by
        /// an EditMode fixture where Awake never runs, still numbers itself.
        ///
        /// <para>
        /// A ship has one wall today. The number costs nothing and its absence is the bug three
        /// other systems here found separately: without it, one press acts on every wall on the
        /// entity.
        /// </para>
        /// </summary>
        public int WallIndex => wallIndex ??= NetChannel.IndexOf(this);

        private int? wallIndex;

        /// <summary>
        /// One of each of these per crew member, laid on once when the ship arrives.
        ///
        /// <para>
        /// Separate from <c>PackContainer</c>'s own starting lists because those are a fixed
        /// manifest — the same gear whoever turns up — and stores are not. A single set of
        /// consumables split between four players is a party-size difficulty curve nobody
        /// authored, and four sets for one player is a sink with nothing to spend against
        /// (<c>GDC-L1-SYS-0008</c>). Per head keeps the flow per player constant at any crew size.
        /// </para>
        /// <para>
        /// On the wall rather than on <c>PackContainer</c>: a backpack has no crew. Wired by
        /// the shipped oxygen gear prefabs, which own what enters the game and where.
        /// </para>
        /// </summary>
        [Header("Crew stores")]
        [Tooltip("One of each per crew member aboard on arrival. Fixed stores belong in the " +
                 "starting item lists above; these scale with the party.")]
        [SerializeField] private List<InventoryItem> perCrewItems = new();

        /// <summary>
        /// Whether <see cref="StockForCrew"/> has already run. A wall is stocked exactly once, at
        /// the arrival that put the ship on the ground, and never again — a second pass would
        /// double the stores every time the director asked.
        /// </summary>
        private bool crewStocked;

        private void Awake() => BeginContents();

        // ── Crew stores ──────────────────────────────────────────────────────

        /// <summary>
        /// Lay on <paramref name="crew"/> of each item in <see cref="perCrewItems"/>, once.
        /// Answers how many placements were made.
        ///
        /// <para>
        /// <b>Server side only, and new worlds only.</b> The contents are server-authoritative and
        /// reach every other machine through <see cref="WallInventoryNetwork"/>, so a client that
        /// stocked itself would show stores nobody else has until the next wire update took them
        /// away again. And the one caller is the arrival — which does not run in a world loaded
        /// from a save, where the wall's real contents come back through
        /// <c>WallInventorySaveable</c> instead.
        /// </para>
        /// <para>
        /// <c>Network.Simulates</c> rather than <c>Network.Server</c>, the rule the saver next door
        /// records: an editor-launched session has no <c>NetworkManager</c> at all and
        /// <c>Network.Server</c> is false there.
        /// </para>
        /// </summary>
        public int StockForCrew(int crew)
        {
            if (!Network.Simulates(this)) return 0;

            // Marked stocked whatever the number: a hull told "nobody is aboard" has had its one
            // chance, and leaving the flag down would let a later call fill it after all.
            if (crewStocked) return 0;
            crewStocked = true;

            if (crew <= 0 || perCrewItems == null) return 0;

            int stowed = 0;

            foreach (InventoryItem item in perCrewItems)
            {
                if (item == null) continue;

                for (int i = 0; i < crew; i++)
                {
                    // StowAuthored, not TryStow: this is a manifest being read onto the wall
                    // rather than a player choosing a face, the same standing the authored lists
                    // and a restored save have. Every face of a wall is reachable anyway, so the
                    // two differ only in what they say.
                    if (StowAuthored(item))
                    {
                        stowed++;
                        continue;
                    }

                    // Loud, because the failure is invisible in the game: a wall that quietly held
                    // three tanks for a crew of four is a wall nobody can tell is short.
                    Debug.LogWarning($"[Wall] No room on '{name}' for {item.itemName} " +
                                     $"{i + 1} of {crew}. The crew are a set of stores short.",
                                     this);
                    break;
                }
            }

            return stowed;
        }

        private void OnDestroy() => EndContents();

        private void OnEnable()
        {
            this.NetOn(NetMsg.WallTake, OnTakeRequested);
            this.NetOn(NetMsg.WallStow, OnStowRequested);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.WallTake, OnTakeRequested);
            this.NetOff(NetMsg.WallStow, OnStowRequested);
        }

        // ── Asking ───────────────────────────────────────────────────────────

        /// <summary>
        /// Somebody wants whatever is at a point on the wall.
        ///
        /// <para>
        /// The request goes to the server and nothing happens locally, which is the rule the pack
        /// follows and for the same reason: a wall is a container two people can reach into at
        /// once, and only one machine can be allowed to decide which of them got the last charge
        /// cell. Doing the transfer optimistically here would hand it to both of them and then take
        /// it back from one.
        /// </para>
        /// <para>
        /// Positional, not an index into the wall's list, for the reason the pack's take documents:
        /// the list is rebuilt wholesale on every change, so a client's index N and the server's
        /// index N are the same item only until somebody else touches the wall.
        /// </para>
        /// </summary>
        public override void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor)
        {
            if (interactor == null) return;

            // The taker's BODY, not the camera rig their Interactor sits on. Resolved the way the
            // messaging layer resolves it, so the id we mint and the object the server resolves are
            // the same thing.
            GameObject taker = NetChannel.RootOf(interactor);
            if (taker == null) return;

            var arg = new NetArg
            {
                A = WallIndex,
                B = (int)surface,
                P = new Vector3(uv.x, 0f, uv.y),
            };

            this.NetToServer(NetMsg.WallTake, arg.With(taker));
        }

        /// <summary>
        /// The mirror: somebody wants one of their hotbar slots put on the wall, at that exact spot
        /// and turn.
        ///
        /// <para>
        /// The hotbar slot travels as an INDEX where the position is positional, and that
        /// difference is deliberate: a hotbar slot is a numbered box, and it is not a thing anybody
        /// else is rearranging underneath them.
        /// </para>
        /// </summary>
        public override void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv,
                                         float yaw, Interactor interactor)
        {
            if (interactor == null) return;

            // Guarded here rather than left to silently corrupt the surface byte: a slot index that
            // does not fit in a byte would bleed into it.
            if (slotIndex < 0 || slotIndex > byte.MaxValue) return;

            GameObject stower = NetChannel.RootOf(interactor);
            if (stower == null) return;

            var arg = new NetArg
            {
                A = WallIndex,
                B = EncodeStowTarget(slotIndex, surfaceId),
                P = new Vector3(uv.x, 0f, uv.y),
                R = Quaternion.Euler(0f, Mathf.Repeat(yaw, 360f), 0f),
            };

            this.NetToServer(NetMsg.WallStow, arg.With(stower));
        }

        // ── Answering (server only) ──────────────────────────────────────────

        /// <summary>Hand over whatever is under that point, if it is still there.</summary>
        private void OnTakeRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (arg.A != WallIndex) return;
            if (!TryDecodeSurface(arg.B, out PackSurfaceId surface)) return;

            IPlayerInventory hotbar = HotbarOf(arg);
            if (hotbar == null) return;

            // Idempotent by construction: the space is empty the second time, so nothing is found
            // under the point and TryTakeToHotbar answers false rather than conjuring a duplicate.
            // That is exactly the race two players grabbing the same item produce, and this is the
            // machine that settles it.
            TryTakeToHotbar(surface, new Vector2(arg.P.x, arg.P.z), hotbar);
        }

        /// <summary>Put it on the wall, if it is still in that slot and the spot is still free.</summary>
        private void OnStowRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (arg.A != WallIndex) return;

            DecodeStowTarget(arg.B, out int slotIndex, out int surfaceValue);
            if (!TryDecodeSurface(surfaceValue, out PackSurfaceId surface)) return;

            IPlayerInventory hotbar = HotbarOf(arg);
            if (hotbar == null) return;

            // A refused spot is a refusal, not a first-fit: the player only ever sends this for
            // cells they watched turn green, so putting the item anywhere else is a lie about what
            // they asked for. Idempotent for free — the slot is empty the second time round.
            TryStowFromHotbar(hotbar, slotIndex, surface,
                              new Vector2(arg.P.x, arg.P.z), arg.R.eulerAngles.y);
        }

        /// <summary>
        /// The sender's hotbar, off the body named in the message.
        ///
        /// <c>GetComponentInChildren</c> rather than <c>GetComponent</c>, so a body that keeps its
        /// hotbar on a child still answers — and on the body rather than on the Interactor, which
        /// on this project's player lives on the camera rig where a plain lookup finds no inventory
        /// at all. That was what made the pack's version of this silently do nothing.
        /// </summary>
        private static IPlayerInventory HotbarOf(in NetArg arg)
        {
            GameObject body = arg.Resolve();
            return body != null ? body.GetComponentInChildren<IPlayerInventory>(true) : null;
        }

        // ── The wire's two small encodings ───────────────────────────────────

        /// <summary>
        /// Slot in the low byte, surface in the next one up. One int because <see cref="NetArg"/>
        /// has two and the wall index has claimed the other; the pack's stow packs the same pair
        /// the same way.
        /// </summary>
        public static int EncodeStowTarget(int slotIndex, PackSurfaceId surface) =>
            (slotIndex & 0xFF) | ((int)surface << 8);

        public static void DecodeStowTarget(int packed, out int slotIndex, out int surface)
        {
            slotIndex = packed & 0xFF;
            surface = (packed >> 8) & 0xFF;
        }

        /// <summary>
        /// A surface id off the wire, refused unless it is one this build knows.
        ///
        /// A cast straight to the enum would accept anything — a byte from a newer build naming a
        /// face that does not exist here — and then fail somewhere further in, where the reason is
        /// no longer visible.
        /// </summary>
        private static bool TryDecodeSurface(int value, out PackSurfaceId surface)
        {
            surface = (PackSurfaceId)value;
            return System.Enum.IsDefined(typeof(PackSurfaceId), surface);
        }
    }
}
