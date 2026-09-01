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

        private void Awake() => BeginContents();

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
