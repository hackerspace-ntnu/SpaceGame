// What a resident is visibly doing, as four small values the server writes and every machine shows.
//
// The routine decides on the server; this is how the decision reaches the screen. Nothing here is
// replicated as a picture — the loop a resident holds at a spot (the smith at the anvil, a sitter by
// the fire), the prop an outrider or an errand-runner carries, the body that vanished indoors are all DERIVED locally from
// the values, so a late joiner gets the current state with the spawn and no animation has to be sent.
// The held place is an index into the settlement's places, which every machine gathers in one order.
using System;
using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Presentation;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [DisallowMultipleComponent]
    public sealed class ResidentPresence : NetworkBehaviour
    {
        [Tooltip("Where a carried prop is parented. Empty: the right hand of a humanoid rig; no prop without one.")]
        [SerializeField] private Transform propSocket;

        private readonly NetworkVariable<byte> activity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> prop = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> hidden = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> place = new(NoPlace, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Holding at no place: walking, talking, sheltering.</summary>
        public const int NoPlace = -1;

        // The deciding machine's own record: offline there is no spawn to write the variables through.
        private Activity decidedActivity;
        private byte decidedProp;
        private bool decidedHidden;
        private int decidedPlace = NoPlace;

        // What this machine currently shows, so applying the same state twice does nothing.
        private Activity shownActivity;
        private byte shownProp;
        private bool shownHidden;
        private int shownPlace = NoPlace;

        private Resident resident;
        private EntityInventoryComponent inventory;
        private EntityEquipmentController equipment;
        private BodyLanguage body;
        private SeatedBodyFit seatFit;
        private float? seatSurface;
        private CharacterCue heldCue;
        private GameObject propInstance;
        private Collider hitCapsule;
        private bool capsuleWasEnabled;
        private readonly List<Renderer> hiddenRenderers = new();
        // The slot of the item an errand has put in the hand, and what was held before it.
        private int carriedSlot = NoPlace, slotBeforeCarrying = NoPlace;

        public Activity Activity => Mirrors ? (Activity)activity.Value : decidedActivity;
        public byte Prop => Mirrors ? prop.Value : decidedProp;
        public bool Hidden => Mirrors ? hidden.Value : decidedHidden;
        public int Place => Mirrors ? place.Value : decidedPlace;

        /// <summary>Raised on every machine when the shown activity changes.</summary>
        public event Action<Activity> Changed;

        private bool Mirrors => IsSpawned && !IsServer;

        private void Awake()
        {
            resident = GetComponent<Resident>();
            inventory = GetComponent<EntityInventoryComponent>();
            equipment = GetComponent<EntityEquipmentController>();
            body = GetComponentInChildren<BodyLanguage>();
            seatFit = new SeatedBodyFit(GetComponentInChildren<Animator>(), transform);
            // The body's own capsule, the one AgentRagdoll also owns — never a ragdoll bone's.
            hitCapsule = GetComponent<Collider>();
        }

        /// <summary>Server only. Writes the state every machine derives its presentation from.</summary>
        public void Publish(Activity newActivity, byte newProp, bool newHidden, int heldPlace = NoPlace)
        {
            if (!Network.Decides) return;

            decidedActivity = newActivity;
            decidedProp = newProp;
            decidedHidden = newHidden;
            decidedPlace = heldPlace;

            if (IsSpawned && IsServer)
            {
                activity.Value = (byte)newActivity;
                prop.Value = newProp;
                hidden.Value = newHidden;
                place.Value = heldPlace;
            }

            Apply();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                activity.OnValueChanged += OnStateChanged;
                prop.OnValueChanged += OnStateChanged;
                hidden.OnValueChanged += OnHiddenChanged;
                place.OnValueChanged += OnPlaceChanged;
            }

            // Read once as well as subscribing: a late joiner gets the current value with the spawn.
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            activity.OnValueChanged -= OnStateChanged;
            prop.OnValueChanged -= OnStateChanged;
            hidden.OnValueChanged -= OnHiddenChanged;
            place.OnValueChanged -= OnPlaceChanged;
        }

        private void OnStateChanged(byte previous, byte next) => Apply();

        private void OnHiddenChanged(bool previous, bool next) => Apply();

        private void OnPlaceChanged(int previous, int next) => Apply();

        // A loop is held by re-asserting it every frame, and BodyLanguage only writes where the animator is ours.
        private void Update()
        {
            if (heldCue != null && body != null && !shownHidden) body.Hold(heldCue);
            // The equipment draws its starting slot on its own Start, which can land after a late joiner's first Apply.
            if (carriedSlot != NoPlace && equipment != null && equipment.EquippedSlotIndex != carriedSlot) equipment.EquipSlot(carriedSlot);
        }

        // After the animator has posed the body: a sitter is lifted until its hips rest on the seat.
        private void LateUpdate() => seatFit.Update(shownHidden ? null : seatSurface, Time.deltaTime);

        private void Apply()
        {
            if (shownHidden != Hidden) ShowHidden(Hidden);
            if (shownProp != Prop) ShowProp(Prop);
            if (shownActivity == Activity && shownPlace == Place) return;

            bool changed = shownActivity != Activity;
            shownActivity = Activity;
            shownPlace = Place;
            if (heldCue != null && body != null) body.Release(heldCue);
            heldCue = CueFor(shownActivity, shownPlace);
            seatSurface = SeatSurfaceOf(shownPlace);
            if (changed) Changed?.Invoke(shownActivity);
        }

        // The spot's own loop where the resident holds at one, a camp sleeper's lying loop, an outrider's trip cue.
        private CharacterCue CueFor(Activity shown, int heldPlace)
        {
            if (shown == Activity.Climbing) return ResidentTuning.Instance.climbCue;
            if (shown == Activity.Stalking)
                return IsTripRow(shownProp) ? ResidentTuning.Instance.tripKinds[shownProp - 1].cue : null;

            SettlementSociety society = resident != null ? resident.Society : null;
            SettlementPlace at = heldPlace != NoPlace && society != null ? society.Place(heldPlace) : null;
            if (at == null) return null;
            if (at.Kind == PlaceKind.Camp) return shown == Activity.Sleep ? ResidentTuning.Instance.campSleepCue : null;
            return at.Use != null ? at.Use.holdCue : null;
        }

        // The surface a sitter at the held place sits on; null for a place nobody sits at.
        private float? SeatSurfaceOf(int heldPlace)
        {
            SettlementSociety society = resident != null ? resident.Society : null;
            SettlementPlace at = heldPlace != NoPlace && society != null ? society.Place(heldPlace) : null;
            return at != null && at.Seated ? at.SeatSurfaceY : null;
        }

        private void ShowProp(byte index)
        {
            shownProp = index;
            if (propInstance != null) Destroy(propInstance);
            propInstance = null;
            ShowCarried(ResidentTuning.Instance.CarryItemAt(index));

            Transform socket = PropSocket();
            GameObject carried = ResidentTuning.Instance.PropAt(index);
            if (socket == null || carried == null) return;

            // A plain local copy, like any equipped visual: a networked prop could not parent to a bone anyway.
            propInstance = Instantiate(carried, socket, false);
            BodyAttachment.Mark(propInstance);
            propInstance.SetActive(!shownHidden);
        }

        // What an errand carries goes through the equipment, which already knows every tool's grip and hold pose. The
        // item is put in the bag when it is missing (a save from before the chore wrote its own bag), and what was
        // held before is held again once it is put down.
        private void ShowCarried(InventoryItem item)
        {
            if (equipment == null || inventory == null) return;

            if (item == null)
            {
                if (carriedSlot == NoPlace) return;

                carriedSlot = NoPlace;
                if (slotBeforeCarrying != NoPlace) equipment.EquipSlot(slotBeforeCarrying);
                else equipment.Unequip();
                return;
            }

            int slot = SlotOf(item);
            if (slot == NoPlace && inventory.TryAddItem(item)) slot = SlotOf(item);
            if (slot == NoPlace)
            {
                Debug.LogWarning($"[Residents] {name}: no room in the bag to carry {item.name} for its errand.", this);
                return;
            }

            if (carriedSlot == NoPlace) slotBeforeCarrying = equipment.EquippedSlotIndex;
            carriedSlot = slot;
            equipment.EquipSlot(slot);
        }

        private int SlotOf(InventoryItem item)
        {
            for (int i = 0; i < inventory.Size; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (!slot.IsEmpty && slot.Item == item) return i;
            }
            return NoPlace;
        }

        private void ShowHidden(bool hide)
        {
            shownHidden = hide;

            if (hide)
            {
                hiddenRenderers.Clear();
                foreach (Renderer part in GetComponentsInChildren<Renderer>())
                {
                    if (!part.enabled) continue;
                    part.enabled = false;
                    hiddenRenderers.Add(part);
                }

                capsuleWasEnabled = hitCapsule != null && hitCapsule.enabled;
                if (hitCapsule != null) hitCapsule.enabled = false;
            }
            else
            {
                foreach (Renderer part in hiddenRenderers)
                    if (part != null) part.enabled = true;
                hiddenRenderers.Clear();

                // Only what was switched off here: AgentRagdoll owns the same capsule while the body is limp.
                if (hitCapsule != null && capsuleWasEnabled) hitCapsule.enabled = true;
            }

            if (heldCue != null && body != null && hide) body.Release(heldCue);
            if (propInstance != null) propInstance.SetActive(!hide);
        }

        // Prop indices are 1-based: the tuning's trip rows, then its carry props; 0 carries nothing. Only a trip row has a cue.
        private static bool IsTripRow(byte index)
        {
            var rows = ResidentTuning.Instance.tripKinds;
            return index > 0 && rows != null && index <= rows.Length;
        }

        private Transform PropSocket()
        {
            if (propSocket != null) return propSocket;

            Animator animator = GetComponentInChildren<Animator>();
            return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }
    }
}
