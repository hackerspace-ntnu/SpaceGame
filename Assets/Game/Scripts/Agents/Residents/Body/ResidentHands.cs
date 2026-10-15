// The one owner of what is in a resident's hand.
//
// ResidentPresence feeds it the two things that decide it — what the resident is doing and what an errand
// has put in its arms — and it resolves them to a bag slot (ResidentHandsRule): the tool for work, the
// carried item for an errand, nothing for everything else. Nothing is sent. Every machine gets the same
// activity and prop from the presence's NetworkVariables and so draws the same hand; the bag it draws
// from is the one every machine builds from the archetype (ResidentCarry), or a stand-in's from its kit
// (ExpeditionMember).
//
// The resolved slot is re-asserted every frame, so it wins over a slot a save restored and over the
// equipment's own starting draw, which can land after a late joiner's first state. The belt needs no
// message either: BeltCarrier hangs whatever bag slot is not in the hand, so putting the tool away is
// emptying the hand. A tool the belt cannot carry stays in the hand (see BeltCarrier.CanStow).
//
// Not a component: it needs no prefab wiring, and its lifetime is ResidentPresence's.
using System.Collections.Generic;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class ResidentHands
    {
        private const int NoSlot = -1;

        private readonly Resident resident;
        private readonly EntityInventoryComponent inventory;
        private readonly EntityEquipmentController equipment;
        private readonly BeltCarrier belt;
        private readonly Object context;

        private Activity activity = Activity.None;
        private bool atPlace;
        private InventoryItem carried;
        private CharacterCue station;
        private bool pushing;
        private int wantedSlot = NoSlot;
        private bool dirty = true;
        private bool failed;
        private bool toolMissing;

        // Reported once per kind of problem, not per resident per frame: a settlement is seventy people.
        private static readonly HashSet<string> Reported = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReported() => Reported.Clear();

        public ResidentHands(Resident resident, EntityInventoryComponent inventory, EntityEquipmentController equipment,
                             BeltCarrier belt, Object context)
        {
            this.resident = resident;
            this.inventory = inventory;
            this.equipment = equipment;
            this.belt = belt;
            this.context = context;
        }

        /// <summary>
        /// The hand holds what the activity wants. False only when a tool the work needs could not be drawn: the activity
        /// must then not be shown, a swing with an empty hand is a mime.
        /// </summary>
        public bool Ready => equipment == null || (!toolMissing && equipment.EquippedSlotIndex == wantedSlot);

        public void Bind()
        {
            if (inventory != null) inventory.OnSlotChanged += OnSlotChanged;
        }

        public void Unbind()
        {
            if (inventory != null) inventory.OnSlotChanged -= OnSlotChanged;
        }

        /// <summary>What the resident is doing now, and whether it is standing at the place it does it.</summary>
        public void Doing(Activity now, bool standingAtPlace)
        {
            if (activity == now && atPlace == standingAtPlace) return;

            activity = now;
            atPlace = standingAtPlace;
            dirty = true;
        }

        /// <summary>
        /// The loop held at the place the resident works: its clips say which tool they are made for (<see cref="CharacterCue.Tools"/>)
        /// or that they are done with empty hands (<see cref="CharacterCue.BareHands"/>), and the hand follows. Null when it holds none.
        /// </summary>
        public void Station(CharacterCue cue)
        {
            if (station == cue) return;

            station = cue;
            dirty = true;
        }

        /// <summary>Whether both hands are on a cart's handles, which leaves nothing for them to hold.</summary>
        public void Pushing(bool now)
        {
            if (pushing == now) return;

            pushing = now;
            dirty = true;
        }

        /// <summary>What an errand has put in its arms; null when it carries nothing.</summary>
        public void Carrying(InventoryItem item)
        {
            if (carried == item) return;

            carried = item;
            dirty = true;
        }

        /// <summary>Every frame, after the state is applied and before a loop is held. True when it changed what the hand holds.</summary>
        public bool Update()
        {
            if (equipment == null || inventory == null) return false;

            if (dirty)
            {
                dirty = false;
                failed = false;
                wantedSlot = Resolve();
            }

            if (failed || equipment.EquippedSlotIndex == wantedSlot) return false;

            if (wantedSlot == NoSlot) equipment.Unequip();
            else equipment.EquipSlot(wantedSlot);
            if (equipment.EquippedSlotIndex == wantedSlot) return true;

            failed = true;
            Report($"cannot draw:{resident.name}:{activity}",
                   $"[Residents] {resident.name} cannot draw its tool for {activity}; the activity is skipped rather than mimed.");
            return false;
        }

        // The bag fills after the first state on a client, and a looted or restored slot changes it: resolve again.
        private void OnSlotChanged(int index, InventorySlot slot) => dirty = true;

        private int Resolve()
        {
            InventoryItem tool = ToolForStation(resident.HeldItem);
            int toolSlot = SlotOf(tool);
            int carriedSlot = EnsureSlot(carried);
            toolMissing = false;
            string refusal = belt == null ? "the resident has no BeltCarrier" : null;
            bool stowable = toolSlot != NoSlot && belt != null && belt.CanStow(toolSlot, out refusal);

            HandContents contents = ResidentHandsRule.Wanted(activity, atPlace, carriedSlot != NoSlot, stowable, pushing);
            // Work whose clips are done with empty hands (wiping, rummaging) or move both arms like a gesture (explaining at a stall):
            // the tool goes on the belt, if it can.
            if (contents == HandContents.Tool && stowable && ResidentHandsRule.EmptyHandsFor(station) && ResidentHandsRule.NeedsTool(activity, atPlace))
                contents = HandContents.Empty;

            if (contents == HandContents.Tool && toolSlot != NoSlot && !ResidentHandsRule.NeedsTool(activity, atPlace) && refusal != null)
                ReportNotStowable(tool, refusal);

            if (contents == HandContents.Tool && ResidentHandsRule.NeedsTool(activity, atPlace) && tool != null && toolSlot == NoSlot)
            {
                toolMissing = true;
                Report($"no tool:{resident.name}:{tool.name}",
                       $"[Residents] {resident.name} has no {tool.name} in its bag; the work that needs it is skipped rather than mimed.");
            }

            return contents switch
            {
                HandContents.Carried => carriedSlot,
                HandContents.Tool => toolSlot,
                _ => NoSlot,
            };
        }

        // The tool the held loop's clips are made for, when the resident carries one: its usual tool if that is among them, else the
        // first of them in the bag (a cook's cleaver for the chopping board). Otherwise the usual tool.
        private InventoryItem ToolForStation(InventoryItem usual)
        {
            if (station == null || station.Tools.Count == 0) return usual;

            foreach (InventoryItem made in station.Tools)
                if (made == usual) return usual;

            foreach (InventoryItem made in station.Tools)
                if (SlotOf(made) != NoSlot) return made;
            return usual;
        }

        private void ReportNotStowable(InventoryItem tool, string reason) =>
            Report($"cannot stow:{tool.name}:{reason}",
                   $"[Residents] {tool.name} stays in the hand of an idle resident: {reason}.");

        private void Report(string key, string message)
        {
            if (Reported.Add(key)) Debug.LogWarning(message, context);
        }

        // The slot holding the carried item, putting it in the bag when it is missing (a save from before the chore wrote its own bag).
        private int EnsureSlot(InventoryItem item)
        {
            if (item == null) return NoSlot;

            int slot = SlotOf(item);
            if (slot == NoSlot && inventory.TryAddItem(item)) slot = SlotOf(item);
            if (slot == NoSlot)
                Report($"no room:{resident.name}:{item.name}",
                       $"[Residents] {resident.name}: no room in the bag to carry {item.name} for its errand.");
            return slot;
        }

        private int SlotOf(InventoryItem item)
        {
            if (item == null) return NoSlot;

            for (int i = 0; i < inventory.Size; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (!slot.IsEmpty && slot.Item == item) return i;
            }
            return NoSlot;
        }
    }
}
