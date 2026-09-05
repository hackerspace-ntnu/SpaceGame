using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Wears what the body slots say, and fires it.
    ///
    /// <para>
    /// The worn counterpart of <see cref="EquipmentController"/>: it derives the three worn
    /// instances from replicated state on every machine, never from a message, so the host, the
    /// wearer and every peer see the same bracer on the same arm. Gauntlets are strapped to the
    /// forearm bones by <see cref="ForearmSeat"/> — their own bone rather than the hand's socket,
    /// so a bracer and a bazooka coexist on one arm — and a torso item sits on the spine where
    /// its <see cref="WornFit"/> says — or on the chest, for chest gear — by <see cref="WornSeat"/>.
    /// </para>
    /// <para>
    /// Each slot has its own <see cref="UseChannel"/>: Q and E press and release the gauntlets,
    /// a double tap of Jump presses and releases the torso item. The four use messages arrive on the
    /// player's relay for both controllers; this one takes the share whose slot code names a
    /// body slot.
    /// </para>
    /// </summary>
    public class BodyEquipmentController : MonoBehaviour
    {
        [Header("Torso sockets")]
        [Tooltip("Which bone a Back item hangs from on a humanoid rig.")]
        [SerializeField] private HumanBodyBones backBone = HumanBodyBones.Spine;

        [Tooltip("Substring hints for a non-humanoid rig (case-insensitive).")]
        [SerializeField] private string[] backBoneNameHints = { "Spine", "Chest", "Torso" };

        [Tooltip("Manual fallback when neither lookup finds a bone.")]
        [SerializeField] private Transform backSocketOverride;

        [Tooltip("Which bone a Chest item sits on. Higher up the spine than the back bone, so a " +
                 "chest device rides the sternum rather than the small of the back.")]
        [SerializeField] private HumanBodyBones chestBone = HumanBodyBones.Chest;

        [Tooltip("Substring hints for a non-humanoid rig (case-insensitive).")]
        [SerializeField] private string[] chestBoneNameHints = { "Chest", "Spine2", "UpperChest" };

        [Header("Arm raise")]
        [Tooltip("Seconds a gauntlet arm stays up after a tap, or after a held item lets go. A tap " +
                 "is over in a frame, and an arm that came up and dropped in the same breath would " +
                 "read as a twitch rather than a shot.")]
        [SerializeField, Min(0f)] private float raiseLingerSeconds = 0.6f;

        [Header("Wear feedback")]
        [Tooltip("How long after this body wakes up a slot filling is still read as the body being " +
                 "told what it holds rather than as a player putting something on. Long enough to " +
                 "cover a save restore arriving from the server; far shorter than it takes anyone " +
                 "to open the body screen and move a piece of gear.")]
        [SerializeField, Min(0f)] private float wearSettleSeconds = 2f;

        private sealed class Worn
        {
            public BodySlot Slot;
            public EquipItemSocket Socket;    // the hand's grip frame, for its thumb side; null for the torso
            public Transform Bone;            // the spine (or chest) for a torso item, the forearm for a gauntlet
            public GameObject Instance;
            public InventoryItem Item;
            public UseChannel Channel;

            /// <summary>Gauntlets only: whether the arm this sits on is up.</summary>
            public ArmRaiseLatch Raise;
        }

        private readonly Worn[] worn = new Worn[GearRef.BodySlotCount];

        private static readonly string[] LeftForearmHints = { "LeftForeArm", "ForeArm_L", "L_ForeArm", "forearm.L" };
        private static readonly string[] RightForearmHints = { "RightForeArm", "ForeArm_R", "R_ForeArm", "forearm.R" };

        private PlayerController player;
        private IBodyEquipment body;
        private EquipmentController hands;
        private PlayerAimRig aimRig;
        private bool listening;

        /// <summary>
        /// The chest bone, resolved in <c>Start</c> beside the spine. It is not a <c>Worn</c> entry
        /// of its own because it is not a slot of its own: it is the torso slot's second PLACE, and
        /// which of the two an item takes is a fact about the item.
        /// </summary>
        private Transform chest;

        /// <summary>
        /// True once <c>Start</c> has worn whatever the slots already held. A slot change after that
        /// may be a player putting something on; the initial adopt — a starting loadout, a late
        /// joiner's copy of a body already wearing three things — never is, and celebrating it would
        /// make every spawn clank up to three times and throw both arms up.
        /// </summary>
        private bool adopted;

        /// <summary>
        /// The moment a slot change stops being explained by this body having just arrived.
        ///
        /// <para>
        /// <see cref="adopted"/> on its own does not cover a load, because the save restore lands
        /// AFTER <c>Start</c> on every machine. The server only learns which profile a body plays
        /// when that owner's claim RPC arrives (<c>PlayerSaveSync</c>), so
        /// <c>BodyEquipmentSaveable.RestoreState</c> — and the slot writes it makes — happen a frame
        /// or a round trip later than the adopt pass. Without this, reloading a world clanks once
        /// for every slot whose saved gear differs from the prefab's starting loadout.
        /// </para>
        /// <para>
        /// A window rather than a flag, because a PEER has nothing else to go on: the restore runs
        /// on the server alone, and on every other machine it arrives as an ordinary replicated slot
        /// change with no local trace of the load that caused it. The window costs nothing real —
        /// the body screen cannot be opened and driven inside it, and no other path fills a body
        /// slot without a player asking — so the only thing it can swallow is one clank for a peer
        /// who wears something in the first seconds of being visible.
        /// </para>
        /// </summary>
        private float settledAt = float.PositiveInfinity;

        /// <summary>
        /// Whether a slot filling right now is somebody acting, rather than this body being told
        /// what it already wears.
        /// </summary>
        private bool Deliberate => adopted && Time.time >= settledAt;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            body = GetComponent<IBodyEquipment>();
            hands = GetComponent<EquipmentController>();
            aimRig = GetComponent<PlayerAimRig>();

            for (int i = 0; i < worn.Length; i++)
            {
                var slot = (BodySlot)i;
                var entry = new Worn { Slot = slot };
                entry.Channel = new UseChannel(this, GearArea.Body, () => GearRef.Body(slot), () => UsableOf(entry));
                worn[i] = entry;

                if (slot == BodySlot.Torso) continue;

                // The arm comes up wherever the use is SHOWN — the wearer's press and every peer's
                // copy of it alike — which is what makes the gesture visible to other players
                // without a message of its own.
                entry.Raise = new ArmRaiseLatch(raiseLingerSeconds);
                entry.Channel.Presented += () =>
                {
                    UsableItem usable = UsableOf(entry);
                    entry.Raise.Press(Time.time, usable != null && usable.IsContinuous);
                };
                entry.Channel.HoldPresented += active => entry.Raise.Hold(active, Time.time);
            }
        }

        private void Start()
        {
            // A player prefab without worn slots — the base prefab in a test scene — simply has
            // nothing to wear. Not an error.
            if (body == null)
            {
                enabled = false;
                return;
            }

            var animator = GetComponentInChildren<Animator>(true);
            Transform back = BoneResolver.Resolve(animator, transform, backBone, backBoneNameHints);
            if (back == null) back = backSocketOverride;
            if (back == null)
                Debug.LogError("BodyEquipmentController: could not resolve a back bone. Assign backSocketOverride or add hints.", this);

            // Not an error when it comes back null: chest gear is the newer half of the torso slot
            // and a rig without a chest bone still wears everything on the spine — WornSeat.BoneFor
            // makes that the fallback rather than dropping the item. Reported only when a chest
            // item is actually asked for, in Wear, where there is something concrete to name.
            chest = BoneResolver.Resolve(animator, transform, chestBone, chestBoneNameHints);

            // After EquipmentController.Awake, which is what Start guarantees: the hand sockets'
            // frames come from the same derivation the held item uses, so one set of ItemGrip
            // offsets seats a bracer whether it is held or worn.
            worn[(int)BodySlot.LeftGauntlet].Socket = hands != null ? hands.NewSocket(ItemGrip.Hand.Left) : null;
            worn[(int)BodySlot.RightGauntlet].Socket = hands != null ? hands.NewSocket(ItemGrip.Hand.Right) : null;
            worn[(int)BodySlot.LeftGauntlet].Bone = BoneResolver.Resolve(animator, transform, HumanBodyBones.LeftLowerArm, LeftForearmHints);
            worn[(int)BodySlot.RightGauntlet].Bone = BoneResolver.Resolve(animator, transform, HumanBodyBones.RightLowerArm, RightForearmHints);
            worn[(int)BodySlot.Torso].Bone = back;

            body.OnBodySlotChanged += OnSlotChanged;
            listening = true;

            // Wear whatever is already there — a late joiner's copy of another player arrives
            // with the slots filled and no change event coming.
            for (int i = 0; i < worn.Length; i++)
                OnSlotChanged((BodySlot)i, body.GetSlot((BodySlot)i));

            // After the adopt pass, before anything a player can drive: everything above this line
            // is the body being told what it already wears, and none of it is worth a sound.
            adopted = true;
            settledAt = Time.time + wearSettleSeconds;

            player.Input.OnGauntletPressed += OnGauntletPressed;
            player.Input.OnGauntletReleased += OnGauntletReleased;
            player.Input.OnBodyActivatePressed += OnBodyActivate;
        }

        private void OnDestroy()
        {
            if (listening && body != null) body.OnBodySlotChanged -= OnSlotChanged;

            if (player != null && player.Input != null)
            {
                player.Input.OnGauntletPressed -= OnGauntletPressed;
                player.Input.OnGauntletReleased -= OnGauntletReleased;
                player.Input.OnBodyActivatePressed -= OnBodyActivate;
            }
        }

        // ── Wearing ────────────────────────────────────────────────────────────

        private void OnSlotChanged(BodySlot slot, InventorySlot contents)
        {
            Worn entry = worn[(int)slot];
            InventoryItem item = contents == null || contents.IsEmpty ? null : contents.Item;

            // Same item, still worn: a state-only change. Nothing to rebuild.
            if (item == entry.Item && (item == null || entry.Instance != null)) return;

            Strip(entry);
            if (item != null) Wear(entry, item, contents);
        }

        private void Wear(Worn entry, InventoryItem item, InventorySlot contents)
        {
            if (item.itemPrefab == null)
            {
                Debug.LogError($"BodyEquipmentController: '{item.itemName}' has no prefab.", this);
                return;
            }

            if (!BodySlotRules.Accepts(entry.Slot, item.equipKind))
            {
                // The server refuses this on every path that writes a slot; reaching here means a
                // save or a starting list disagreed with the asset. Worn anyway would fire the wrong
                // artifact off the wrong key.
                Debug.LogWarning($"BodyEquipmentController: '{item.itemName}' is a {item.equipKind} and cannot be worn in the {entry.Slot} slot.", this);
                return;
            }

            if (entry.Slot == BodySlot.Torso)
                entry.Instance = WearOnTorso(entry, item.itemPrefab, item.equipKind);
            else if (item.itemPrefab.GetComponent<GauntletFit>() is GauntletFit fit)
                entry.Instance = WearOnForearm(entry, item.itemPrefab, fit);
            else
            {
                // Every gauntlet is built on the shared base and strapped to the forearm, so the
                // fit component is not optional — it is how a prefab says which frame it is in.
                // A gauntlet seated in the HAND socket instead used to be the fallback here; it
                // aligned the item with the fingers and sized it per item, and it is what "does
                // not fit as a gauntlet" looked like.
                Debug.LogError($"BodyEquipmentController: '{item.itemName}' is a gauntlet with no GauntletFit. Add one — its model must be built on components/props/gauntlet_base.blend.", this);
                return;
            }

            if (entry.Instance == null)
            {
                Debug.LogWarning($"BodyEquipmentController: this rig has nowhere to wear '{item.itemName}' ({entry.Slot}).", this);
                return;
            }

            entry.Item = item;

            // Here rather than at the end of the method: this is the point at which the thing is
            // genuinely worn, and the early return below would otherwise leave a worn item that
            // happens to have no UsableItem landing in silence.
            if (Deliberate) Celebrate(entry);

            var usable = entry.Instance.GetComponent<UsableItem>();
            if (usable == null) return;

            // BEFORE OnEquipped: it is the switch that keeps a worn item from posing the arm.
            usable.Worn = true;
            usable.OnItemDepleted += OnWornDepleted;
            usable.OnEquipped(gameObject);

            // AFTER OnEquipped, for the reason EquipmentController gives: items reset themselves there.
            if (usable is IItemStateCarrier carrier)
                carrier.RestoreItemState(contents?.State);

        }

        /// <summary>
        /// Something was just put on. The equip sound at the item, and — for a gauntlet — the same
        /// arm raise a Q or E press gives, through the latch that already times it.
        ///
        /// <para>
        /// Runs on every machine, because what drives it is the replicated slot change and not a
        /// message of its own: a peer sees the flex the wearer sees, for nothing on the wire. This
        /// is the same trick the firing raise uses in <c>Awake</c>.
        /// </para>
        /// <para>
        /// The sound is placed at the instance rather than at the player. It is a one-shot short
        /// enough not to need to follow anything; what the instance's transform buys is a position
        /// on the limb the gear landed on — audibly left or right to somebody standing next to the
        /// wearer — and a rate-limiting key of its own, so two slots filling in the same breath
        /// cannot silence one another on <see cref="Sfx"/>'s per-source cooldown.
        /// </para>
        /// </summary>
        private void Celebrate(Worn entry)
        {
            Sfx.Play(SfxId.WeaponEquip, entry.Instance.transform);

            // Null for the back slot, which has no arm to raise. Not continuous: a tap, so the
            // latch's linger is what holds the arm up and then drops it.
            entry.Raise?.Press(Time.time, continuous: false);
        }

        /// <summary>
        /// Wear a torso item: make the instance, strip what a worn copy must not carry, and hand it
        /// to <see cref="WornSeat"/> — where the pose itself lives, so the body screen's ghost of
        /// this item is seated by the same arithmetic. The instance is this controller's from here
        /// on; <see cref="Strip"/> destroys it.
        ///
        /// <para>
        /// <paramref name="kind"/> is what decides between the two places the one torso slot has:
        /// back gear on the spine, chest gear on the chest. It is read from the item rather than
        /// from the slot, which is the whole reason a single slot can hold either.
        /// </para>
        /// </summary>
        private GameObject WearOnTorso(Worn entry, GameObject prefab, EquipKind kind)
        {
            Transform bone = WornSeat.BoneFor(kind, entry.Bone, chest);
            if (bone == null) return null;

            GameObject instance = Instantiate(prefab, bone);
            EquipItemSocket.Sanitize(instance);

            // torsoForm, not Worn: the gear screen is WHERE gear is put on, so an item can perfectly
            // well arrive while the screen is open, and one seated in the world's form then would
            // be the only thing on that screen wearing the wrong shape until it closed.
            WornSeat.Apply(instance, bone, instance.GetComponent<WornFit>(), TorsoMount(kind), torsoForm);

            return instance;
        }

        /// <summary>
        /// Which of its models torso gear is wearing. The world's, except while the gear screen is
        /// open — see <see cref="WornVisual.Form"/>.
        ///
        /// <para>
        /// Held here rather than asked of the screen because this controller owns the instances and
        /// outlives any session over them: a screen that is torn down without an Exit would
        /// otherwise leave its gear spread, and a screen that opens after an item is worn would
        /// have to find that item to fix it. One field, set from both ends of the session.
        /// </para>
        /// </summary>
        private WornVisual.Form torsoForm = WornVisual.Form.Worn;

        /// <summary>
        /// Re-seat whatever torso gear is worn into <paramref name="form"/>, and remember it for
        /// anything worn later. Called by <c>BodyFocusSession</c> at both ends of the gear screen.
        ///
        /// <para>
        /// A re-seat rather than a bare model swap, because the two models have different spans and
        /// <see cref="WornSeat.Apply"/> is what turns a span into a scale. Swapping the child alone
        /// would leave the gear screen's 5.51 m wings wearing the scale that was computed for the
        /// 1.97 m stowed bundle, which is the same wing shrunk to a third — a change that looks
        /// deliberate and is not.
        /// </para>
        /// <para>
        /// Idempotent, and safe with nothing worn. It must be, because Exit runs on teardown paths
        /// that may never have reached Enter.
        /// </para>
        /// </summary>
        public void SetTorsoForm(WornVisual.Form form)
        {
            torsoForm = form;

            Worn entry = worn[(int)BodySlot.Torso];
            if (entry?.Instance == null || entry.Item == null) return;

            // The item's own kind, exactly as WearOnTorso reads it: the one torso slot holds back
            // gear or chest gear, and which one decides both the bone and whether there is a rail.
            EquipKind kind = entry.Item.equipKind;

            Transform bone = WornSeat.BoneFor(kind, entry.Bone, chest);
            if (bone == null) return;

            WornSeat.Apply(entry.Instance, bone, entry.Instance.GetComponent<WornFit>(),
                           TorsoMount(kind), form);
        }

        /// <summary>
        /// The fixture a torso item of this kind clips to: the worn pack's lash rail for back gear,
        /// and nothing for chest gear, which has no fixture and sits at its authored fit.
        ///
        /// <para>
        /// Null is ordinary — a player whose pack is deployed on the sand has no rail on their back
        /// — and <see cref="WornSeat.Apply"/> falls back to the authored offset for it. The gear
        /// screen resolves the same mount through the same seam, so what it lights up is where the
        /// gear goes.
        /// </para>
        /// </summary>
        public Transform TorsoMount(EquipKind kind)
        {
            if (kind != EquipKind.Back) return null;

            var pack = GetComponent<BackpackController>();
            return pack != null ? pack.GearMount : null;
        }

        /// <summary>
        /// Wear a gauntlet: make the instance, strip what a worn copy must not carry, and hand it
        /// to <see cref="ForearmSeat"/> — where the strapping itself lives (the arm axis, the
        /// dorsal side, the mirrored left arm), so the body screen's ghost of this gauntlet is
        /// seated by the same arithmetic. The instance is this controller's from here on;
        /// <see cref="Strip"/> destroys it.
        ///
        /// <para>
        /// The hand socket is passed for its grip frame alone — the seat reads the thumb side off
        /// it to work out which way the back of the arm faces. Nothing is ever parented to it.
        /// </para>
        /// </summary>
        private GameObject WearOnForearm(Worn entry, GameObject prefab, GauntletFit fit)
        {
            if (entry.Bone == null || entry.Socket == null) return null;

            GameObject instance = Instantiate(prefab, entry.Bone);
            EquipItemSocket.Sanitize(instance);

            ForearmSeat.Apply(instance, entry.Bone, entry.Socket.Socket, entry.Socket.GripRotation,
                              entry.Slot == BodySlot.LeftGauntlet, fit);

            return instance;
        }

        private void Strip(Worn entry)
        {
            if (entry.Instance == null)
            {
                entry.Item = null;
                return;
            }

            // The slot keeps what this instance became — unless the slot has already moved on to
            // another item, in which case the state describes nothing that is there.
            WriteBack(entry);

            // While UsableOf still answers with the thing that is burning.
            entry.Channel.EndHold(send: true);

            var usable = entry.Instance.GetComponent<UsableItem>();
            if (usable != null)
            {
                usable.OnUnequipped(gameObject);
                usable.OnItemDepleted -= OnWornDepleted;
            }

            // Destroyed here rather than handed back to a socket: a gauntlet is parented to the
            // forearm by WearOnForearm and a back item to the spine, so no socket owns either, and
            // asking one to unequip would leave the thing strapped on.
            Destroy(entry.Instance);

            entry.Instance = null;
            entry.Item = null;
            entry.Raise?.Clear();
        }

        private void OnWornDepleted(UsableItem item)
        {
            // Every worn artifact today is unlimited or refills. A consumable gauntlet would need a
            // server-side removal the body does not have yet; say so rather than fire on empty.
            Debug.LogWarning($"BodyEquipmentController: worn item '{item.name}' ran out of uses and stays worn — worn items must be unlimited or refilling.", this);
        }

        // ── State ──────────────────────────────────────────────────────────────

        private void WriteBack(Worn entry)
        {
            if (entry.Instance == null || body == null) return;
            if (entry.Instance.GetComponent<UsableItem>() is not IItemStateCarrier carrier) return;

            InventorySlot slot = body.GetSlot(entry.Slot);
            if (slot == null || slot.IsEmpty || slot.Item != entry.Item) return;

            var state = new ItemState();
            carrier.CaptureItemState(state);
            slot.State = state.IsEmpty ? null : state;
        }

        /// <summary>Copy every worn instance's live state back into its slot — before a save.</summary>
        public void WriteBackWornState()
        {
            foreach (Worn entry in worn) WriteBack(entry);
        }

        /// <summary>Hand every worn instance the state its slot now holds — the second pass of a load.</summary>
        public void ReapplyWornState()
        {
            if (body == null) return;

            foreach (Worn entry in worn)
            {
                if (entry.Instance == null) continue;
                if (entry.Instance.GetComponent<UsableItem>() is not IItemStateCarrier carrier) continue;

                carrier.RestoreItemState(body.GetSlot(entry.Slot)?.State);
            }
        }

        /// <summary>The worn instances that exist, for the saver's deferred pass.</summary>
        public IEnumerable<UsableItem> WornItems
        {
            get
            {
                foreach (Worn entry in worn)
                {
                    UsableItem usable = UsableOf(entry);
                    if (usable != null) yield return usable;
                }
            }
        }

        private static UsableItem UsableOf(Worn entry) =>
            entry.Instance != null ? entry.Instance.GetComponent<UsableItem>() : null;

        /// <summary>The instance worn in a slot, or null. For the autotest and the pose audit.</summary>
        public UsableItem WornIn(BodySlot slot) => UsableOf(worn[(int)slot]);

        // ── Where things are worn ──────────────────────────────────────────────
        //
        // Read-only seams for the body screen, which draws a ghost of each candidate item on the
        // body it would be worn on. It asks here rather than resolving the bones again: a second
        // resolution would be a second answer the day a rig or a hint list changes, and a ghost
        // that promises one place while the gear lands in another is the whole failure this
        // screen has to avoid. Every one of these is null until Start has run.

        /// <summary>The bone a Back item hangs from, once <c>Start</c> has resolved it. The body screen seats its ghosts on it.</summary>
        public Transform BackBone => worn[(int)BodySlot.Torso].Bone;

        /// <summary>The bone a Chest item sits on, or null on a rig that has none — in which case <see cref="WornSeat.BoneFor"/> falls back to the spine.</summary>
        public Transform ChestBone => chest;

        /// <summary>The forearm bone a gauntlet is strapped to, or null for the torso slot.</summary>
        public Transform ForearmBone(BodySlot slot) =>
            slot == BodySlot.Torso ? null : worn[(int)slot].Bone;

        /// <summary>The hand socket a gauntlet's seating reads its thumb side from, or null for the torso slot.</summary>
        public EquipItemSocket HandSocket(BodySlot slot) =>
            slot == BodySlot.Torso ? null : worn[(int)slot].Socket;

        /// <summary>The instance worn in a slot, or null. The body screen hides and outlines it; it never moves it.</summary>
        public GameObject WornInstance(BodySlot slot) => worn[(int)slot].Instance;

        /// <summary>
        /// Fire a worn slot as though its key had been pressed and released.
        ///
        /// The seam <c>MultiplayerAutotest</c> fires through — the same one
        /// <see cref="EquipmentController.UseHeldItem"/> is for the hand: the input events are C#
        /// events only their declaring class can raise, so everything from the press onwards is the
        /// real path and only the key binding itself is left untested.
        /// </summary>
        public void UseWorn(BodySlot slot)
        {
            UseChannel channel = worn[(int)slot].Channel;
            channel.Press();
            channel.Release();
        }

        // ── Firing ─────────────────────────────────────────────────────────────

        private Worn Gauntlet(ItemGrip.Hand hand) =>
            worn[(int)(hand == ItemGrip.Hand.Left ? BodySlot.LeftGauntlet : BodySlot.RightGauntlet)];

        // Gauntlets fire mounted or not — a scanner read from the saddle, a leash thrown from the
        // air. Q and E are nominally the mount's Turn axis, but no shipped mount binds it
        // (SteerModule.turnActionName is empty on every prefab; the ornithopter yaws off Move.x),
        // so there is nothing for the press to fight. If a mount ever does bind Turn, this is the
        // seam that has to arbitrate.
        private void OnGauntletPressed(ItemGrip.Hand hand) => Gauntlet(hand).Channel.Press();

        private void OnGauntletReleased(ItemGrip.Hand hand) => Gauntlet(hand).Channel.Release();

        private void OnBodyActivate()
        {
            // The back item is the wing pack, and the double tap of Space that deploys it is the
            // ornithopter's flap once airborne; a craft already under the pilot must not spawn another.
            if (body.IsMounted) return;

            UseChannel back = worn[(int)BodySlot.Torso].Channel;
            back.Press();
            back.Release();
        }

        private void Update()
        {
            float now = Time.time;
            foreach (Worn entry in worn) entry.Channel.Tick(now);

            if (aimRig == null) return;

            aimRig.RaiseArm(ItemGrip.Hand.Left, worn[(int)BodySlot.LeftGauntlet].Raise.Raised(now));
            aimRig.RaiseArm(ItemGrip.Hand.Right, worn[(int)BodySlot.RightGauntlet].Raise.Raised(now));
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.UseItem, OnUseRequested);
            this.NetOn(NetMsg.ItemUsed, OnItemUsedElsewhere);
            this.NetOn(NetMsg.UseItemHold, OnHoldRequested);
            this.NetOn(NetMsg.ItemUseHeld, OnItemHeldElsewhere);
        }

        private void OnDisable()
        {
            // Locally only, as EquipmentController does: this runs during death and teardown.
            foreach (Worn entry in worn) entry.Channel?.EndHold(send: false);

            this.NetOff(NetMsg.UseItem, OnUseRequested);
            this.NetOff(NetMsg.ItemUsed, OnItemUsedElsewhere);
            this.NetOff(NetMsg.UseItemHold, OnHoldRequested);
            this.NetOff(NetMsg.ItemUseHeld, OnItemHeldElsewhere);
        }

        /// <summary>The channel a use message names, or null when it is the hand's or nobody's.</summary>
        private UseChannel ChannelFor(int code)
        {
            GearRef slot = UseSlotCode.Decode(code);
            if (!slot.IsBody || slot.Index >= worn.Length) return null;
            return worn[slot.Index].Channel;
        }

        private void OnUseRequested(in NetArg arg, ulong sender) => ChannelFor(arg.A)?.OnUseRequested(arg, sender);
        private void OnItemUsedElsewhere(in NetArg arg, ulong sender) => ChannelFor(arg.A)?.OnUsedElsewhere(arg, sender);
        private void OnHoldRequested(in NetArg arg, ulong sender) => ChannelFor(arg.A)?.OnHoldRequested(arg, sender);
        private void OnItemHeldElsewhere(in NetArg arg, ulong sender) => ChannelFor(arg.A)?.OnHeldElsewhere(arg, sender);
    }
}
