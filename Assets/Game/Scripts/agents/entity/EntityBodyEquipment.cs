// An NPC's worn gear: the same three BodySlots and BodySlotRules as the player's body, decided on the
// server and replicated as item ids, so every machine — late joiners included — seats the same gear
// itself. Torso gear is worn on the spine without a rig (WornSeat.ApplyWithoutRig: the wing pack
// folded, D3); gauntlets are strapped to the forearms by the same ForearmSeat the player's use.
//
// Why not BodyEquipmentController: that is the player's — owner RPCs, an input map, a backpack's lash
// rail, a gear screen. This shares the SEATING half only (WornSeat/ForearmSeat/WornAnchor/WornBones),
// never the network half.
//
// Persistence: EntityBodyEquipmentSaveable ("npcWorn"). A restore wins over the prefab's starting gear,
// including a restore that says "nothing" — a looted pack must not grow back on reload.
// Loot: EntityLootTable takes what it drops off the body slot by slot on death (Remove) — every worn item but
// a wing pack, which only a death aloft or a war-party flier sheds.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class EntityBodyEquipment : NetworkBehaviour, INpcAim
    {
        private const int Torso = (int)BodySlot.Torso;

        [Tooltip("Worn from the start, by body slot: Torso, LeftGauntlet, RightGauntlet. An entry of the " +
                 "wrong kind for its slot is skipped with a warning. A save that says otherwise wins.")]
        [SerializeField] private InventoryItem[] startingWorn = new InventoryItem[GearRef.BodySlotCount];

        private readonly InventoryItem[] worn = new InventoryItem[GearRef.BodySlotCount];
        private readonly GameObject[] instances = new GameObject[GearRef.BodySlotCount];

        // Server-written, everyone-read: one item id per slot, empty for nothing. Three fields rather
        // than a list because NGO discovers NetworkVariables as fields; NpcRandomLoadout's pattern.
        private readonly NetworkVariable<FixedString64Bytes> torsoId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<FixedString64Bytes> leftId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<FixedString64Bytes> rightId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);

        private Transform spine;
        private readonly Transform[] forearms = new Transform[2];
        private readonly EquipItemSocket[] hands = new EquipItemSocket[2];
        private bool rigResolved;

        // Set once anything (a restore, a TryWear) has written a slot: the prefab's starting gear is
        // only for a body nothing has spoken for.
        private bool written;
        private bool torsoShown = true;
        private bool hasAimPoint;
        private Vector3 aimPoint;

        public event Action<BodySlot> WornChanged;

        public bool HasAimPoint => hasAimPoint;
        public Vector3 AimPoint => aimPoint;
        public bool TorsoShown => torsoShown;

        public InventoryItem ItemIn(BodySlot slot) => worn[(int)slot];
        public GameObject InstanceIn(BodySlot slot) => instances[(int)slot];
        public UsableItem UsableIn(BodySlot slot) =>
            instances[(int)slot] != null ? instances[(int)slot].GetComponent<UsableItem>() : null;

        /// <summary>A worn gauntlet this NPC may fire: opted in on its asset (D9) and actually usable.</summary>
        public bool IsNpcUsable(BodySlot slot) =>
            slot != BodySlot.Torso && worn[(int)slot] != null && worn[(int)slot].npcUsable && UsableIn(slot) != null;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Offline there is no spawn; the starting gear still has to go on. The stowed check runs on every
        // machine: a body already under a carrier when it wakes never sees a parent change.
        private void Start()
        {
            if (!Network.IsNetworked) WearStarting();
            RefreshStowed();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                WearStarting();
                for (int i = 0; i < worn.Length; i++) Publish(i);
                return;
            }

            torsoId.OnValueChanged += OnTorsoChanged;
            leftId.OnValueChanged += OnLeftChanged;
            rightId.OnValueChanged += OnRightChanged;
            // A late joiner gets the values with the spawn and never sees a change event for them.
            Mirror(Torso, torsoId.Value);
            Mirror((int)BodySlot.LeftGauntlet, leftId.Value);
            Mirror((int)BodySlot.RightGauntlet, rightId.Value);
            // The netcode parenting can arrive before the spawn, with no parent change left to cue it.
            RefreshStowed();
        }

        public override void OnNetworkDespawn()
        {
            torsoId.OnValueChanged -= OnTorsoChanged;
            leftId.OnValueChanged -= OnLeftChanged;
            rightId.OnValueChanged -= OnRightChanged;
        }

        private void OnEnable() => this.NetOn(NetMsg.ItemUsed, OnItemUsedElsewhere);
        private void OnDisable() => this.NetOff(NetMsg.ItemUsed, OnItemUsedElsewhere);

        // A rider is netcode-parented under its carrier on every machine; the parent change is the cue.
        private void OnTransformParentChanged() => RefreshStowed();

        private void WearStarting()
        {
            if (written || startingWorn == null) return;

            for (int i = 0; i < startingWorn.Length && i < worn.Length; i++)
            {
                InventoryItem item = startingWorn[i];
                if (item == null) continue;
                if (!BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[EntityBody] Starting item '{item.itemName}' is a {item.equipKind} and does not fit the {(BodySlot)i} slot — skipped.", this);
                    continue;
                }
                Write(i, item);
            }
        }

        // ── Server writes ──────────────────────────────────────────────────────

        public bool TryWear(InventoryItem item)
        {
            if (item == null) return false;
            for (int i = 0; i < worn.Length; i++)
                if (worn[i] == null && BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                    return TryWear(item, (BodySlot)i);
            return false;
        }

        public bool TryWear(InventoryItem item, BodySlot slot)
        {
            if (item == null || !Network.Simulates(this)) return false;
            if (worn[(int)slot] != null || !BodySlotRules.Accepts(slot, item.equipKind)) return false;

            Write((int)slot, item);
            return true;
        }

        public InventoryItem Remove(BodySlot slot)
        {
            if (!Network.Simulates(this)) return null;
            InventoryItem item = worn[(int)slot];
            if (item != null) Write((int)slot, null);
            return item;
        }

        /// <summary>A save's view of what is worn, positional by slot. Server only; wins over starting gear.</summary>
        public void RestoreWorn(IReadOnlyList<InventoryItem> items)
        {
            if (!Network.Simulates(this))
            {
                Debug.LogWarning("[Save] EntityBody RestoreWorn ignored on a client — worn gear is server state.", this);
                return;
            }

            for (int i = 0; i < worn.Length; i++)
            {
                InventoryItem item = items != null && i < items.Count ? items[i] : null;
                if (item != null && !BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[Save] '{item.itemName}' does not fit the {(BodySlot)i} slot it was saved in — left empty.", this);
                    item = null;
                }
                Write(i, item);
            }
        }

        private void Write(int index, InventoryItem item)
        {
            written = true;
            worn[index] = item;
            Publish(index);
            Rebuild(index);
            WornChanged?.Invoke((BodySlot)index);
        }

        private void Publish(int index)
        {
            if (!IsSpawned || !IsServer) return;
            InventoryItem item = worn[index];
            var id = new FixedString64Bytes(item != null && !string.IsNullOrEmpty(item.ID) ? item.ID : string.Empty);
            IdOf(index).Value = id;
        }

        private NetworkVariable<FixedString64Bytes> IdOf(int index) => index switch
        {
            Torso => torsoId,
            (int)BodySlot.LeftGauntlet => leftId,
            _ => rightId,
        };

        // ── Every machine: mirror and seat ─────────────────────────────────────

        private void OnTorsoChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror(Torso, next);
        private void OnLeftChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror((int)BodySlot.LeftGauntlet, next);
        private void OnRightChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror((int)BodySlot.RightGauntlet, next);

        private void Mirror(int index, FixedString64Bytes value)
        {
            string id = value.ToString();
            InventoryItem item = string.IsNullOrEmpty(id) ? null : Registry<InventoryItem>.Get(id);
            if (!string.IsNullOrEmpty(id) && item == null)
                Debug.LogWarning($"[EntityBody] '{name}' wears '{id}' on the server but this machine has no such item registered.", this);
            if (worn[index] == item && (item == null || instances[index] != null)) return;

            worn[index] = item;
            Rebuild(index);
            WornChanged?.Invoke((BodySlot)index);
        }

        private void Rebuild(int index)
        {
            Strip(index);
            InventoryItem item = worn[index];
            if (item == null) return;
            if (item.itemPrefab == null)
            {
                Debug.LogError($"[EntityBody] '{item.itemName}' has no prefab.", this);
                return;
            }

            ResolveRig();
            GameObject instance = index == Torso ? WearOnSpine(item.itemPrefab) : WearOnForearm(item.itemPrefab, index);
            if (instance == null)
            {
                Debug.LogWarning($"[EntityBody] '{name}' has nowhere to wear '{item.itemName}' ({(BodySlot)index}).", this);
                return;
            }

            instances[index] = instance;
            if (index == Torso) ApplyTorsoShown();

            if (instance.TryGetComponent(out UsableItem usable))
            {
                usable.Worn = true;
                usable.WornOn = index == (int)BodySlot.LeftGauntlet ? ItemGrip.Hand.Left : ItemGrip.Hand.Right;
                usable.OnEquipped(gameObject);
            }
        }

        private GameObject WearOnSpine(GameObject prefab)
        {
            if (spine == null) return null;
            GameObject instance = Instantiate(prefab, spine);
            EquipItemSocket.Sanitize(instance);
            WornSeat.ApplyWithoutRig(instance, spine, instance.GetComponent<WornFit>());
            WornAnchor.Pin(instance, spine);
            return instance;
        }

        private GameObject WearOnForearm(GameObject prefab, int index)
        {
            int side = index == (int)BodySlot.LeftGauntlet ? 0 : 1;
            Transform forearm = forearms[side];
            EquipItemSocket hand = hands[side];
            var fit = prefab.GetComponent<GauntletFit>();
            if (forearm == null || hand == null || fit == null) return null;

            GameObject instance = Instantiate(prefab, forearm);
            EquipItemSocket.Sanitize(instance);
            ForearmSeat.Apply(instance, forearm, hand.Socket, hand.GripRotation, side == 0, instance.GetComponent<GauntletFit>());
            WornAnchor.Pin(instance, forearm);
            return instance;
        }

        private void Strip(int index)
        {
            GameObject instance = instances[index];
            instances[index] = null;
            if (instance == null) return;

            if (instance.TryGetComponent(out UsableItem usable)) usable.OnUnequipped(gameObject);
            // DestroyImmediate outside play mode: an EditMode test and an editor build both strip gear,
            // and Destroy there is an error.
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }

        private void ResolveRig()
        {
            if (rigResolved) return;
            rigResolved = true;

            Animator animator = GetComponentInChildren<Animator>(true);
            spine = BoneResolver.Resolve(animator, transform, HumanBodyBones.Spine, WornBones.BackHints);
            forearms[0] = BoneResolver.Resolve(animator, transform, HumanBodyBones.LeftLowerArm, WornBones.LeftForearmHints);
            forearms[1] = BoneResolver.Resolve(animator, transform, HumanBodyBones.RightLowerArm, WornBones.RightForearmHints);
            hands[0] = Socket(animator, BoneResolver.Resolve(animator, transform, HumanBodyBones.LeftHand, WornBones.LeftHandHints), right: false);
            hands[1] = Socket(animator, BoneResolver.Resolve(animator, transform, HumanBodyBones.RightHand, WornBones.RightHandHints), right: true);
        }

        // The hand's grip frame is read for its thumb side only (ForearmSeat); nothing is parented to it.
        private static EquipItemSocket Socket(Animator animator, Transform hand, bool right) =>
            hand != null ? new EquipItemSocket(hand, HandGripFrame.Derive(animator, hand, right)) : null;

        // ── Stowed while riding ────────────────────────────────────────────────

        /// <summary>Hide or show the worn torso item. Presentation; every machine.</summary>
        public void SetTorsoShown(bool shown)
        {
            torsoShown = shown;
            ApplyTorsoShown();
        }

        /// <summary>Re-read whether a carrier above this body stows its torso gear (IStowsTorsoGear).</summary>
        public void RefreshStowed() =>
            SetTorsoShown(transform.parent == null || transform.parent.GetComponentInParent<IStowsTorsoGear>() == null);

        private void ApplyTorsoShown()
        {
            GameObject instance = instances[Torso];
            if (instance == null) return;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = torsoShown;
        }

        // ── Aim (INpcAim) ──────────────────────────────────────────────────────

        public void AimAt(Vector3 point)
        {
            aimPoint = point;
            hasAimPoint = true;
        }

        public void ClearAim() => hasAimPoint = false;

        public Vector3 FireOrigin(BodySlot slot) =>
            instances[(int)slot] != null ? instances[(int)slot].transform.position : transform.position;

        /// <summary>Fire an opted-in worn item at <paramref name="aimPoint"/>. Server only; peers see it through ItemUsed.</summary>
        public bool TryUseWornAt(BodySlot slot, Vector3 aimPoint)
        {
            // Public, so it checks the opt-in itself rather than trusting every caller to.
            if (!IsNpcUsable(slot) || !Network.Simulates(this)) return false;

            AimAt(aimPoint);
            // A body code, never a hand-slot number: EntityEquipmentController ignores it.
            int code = UseSlotCode.Encode(GearRef.Body(slot));
            NpcItemFire.Fire(this, UsableIn(slot), NpcItemFire.AimedArg(code, FireOrigin(slot), aimPoint, transform.rotation));
            return true;
        }

        // ── Peers: presentation of a worn use ──────────────────────────────────

        private void OnItemUsedElsewhere(in NetArg arg, ulong sender)
        {
            if (Network.Simulates(this)) return;
            GearRef slot = UseSlotCode.Decode(arg.A);
            if (!slot.IsBody) return;   // the hand's, EntityEquipmentController's
            UsableIn(slot.Slot)?.PlayUse(gameObject, arg);
        }

        public override void OnDestroy()
        {
            for (int i = 0; i < instances.Length; i++) Strip(i);
            // Disposes the NetworkVariables and deregisters the behaviour (BodyEquipmentNetwork.OnDestroy).
            base.OnDestroy();
        }
    }
}
