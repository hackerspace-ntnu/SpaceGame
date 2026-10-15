// The body of a resident out with a band: a stand-in, spawned by the director from the resident's own prefab while
// the resident itself is Away at home (spec §4.1). A freshly spawned prefab has none of the scene overrides that made
// the resident who it is, so this carries them — the resident's key and name, its archetype and the kit its band gave
// it — and puts them on the body on every machine. Without it a client would see a nameless body of the prefab's
// default kind, carrying its trade's tools.
//
// Expedition mode: while it is a stand-in, Resident.Settlement is null, so the routine and the planner stand down and
// presence, hands, awareness and voice stay on. The presence shows Activity.Expedition, for which the hand rule draws
// the kit's weapon (ResidentHandsRule, Resident.HeldItem).
//
// The server stamps it before the network spawn (NpcSpawn.Create's beforeSpawn), before any Start, and writes the
// replicated copy when the body spawns, so the values travel with the spawn; every other machine reads them in
// OnNetworkSpawn and on change, late joiners included. Only a key, a name and four indices are sent: the kit, the
// archetype and the bag are looked up from them in the ExpeditionCatalog on each machine, so the bag is never
// replicated and every machine still packs the same one.
//
// On a resident at home it is inert (no key), except while the resident is WITH ITS BAND at home: mustering, walking
// out, walking back in (SettlementExpeditions). Then the server names the band's profile, goal and kit row without a key
// (CarryKit), every machine lends the kit's weapon to the resident's own bag beside its things, and the presence shows
// Activity.Expedition, so the hand rule draws that weapon as it does a stand-in's (Resident.HeldItem). PutKitAway takes
// the weapon back.
//
// Nothing here is saved: the band record is the stand-in's save, and NpcSpawn takes the body out of the world save. A
// lent weapon is not saved either (ILentSlots): a save taken mid-muster or mid-walk keeps the resident's own bag only.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>Who a stand-in is: the resident's key and name, and indices that every machine resolves the same way.</summary>
    public readonly struct StandInIdentity
    {
        /// <summary>An index that names nothing.</summary>
        public const short None = -1;

        /// <summary>A resident at home: no key, so no stand-in.</summary>
        public static readonly StandInIdentity Nobody = new StandInIdentity(string.Empty, string.Empty, None, None, None, None);

        public readonly string residentKey;
        public readonly string displayName;

        /// <summary>The band's profile, an index into the ExpeditionCatalog's profiles.</summary>
        public readonly short profile;

        /// <summary>The band's goal, an index into the profile's goals.</summary>
        public readonly short goal;

        /// <summary>The resident's archetype, an index into the profile's culture's archetypes.</summary>
        public readonly short archetype;

        /// <summary>The member's kit, a row of the goal's kits.</summary>
        public readonly short kit;

        public StandInIdentity(string residentKey, string displayName, short profile, short goal, short archetype, short kit)
        {
            this.residentKey = residentKey ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.profile = profile;
            this.goal = goal;
            this.archetype = archetype;
            this.kit = kit;
        }

        public bool IsStandIn => !string.IsNullOrEmpty(residentKey);
    }

    [DisallowMultipleComponent]
    public sealed class ExpeditionMember : NetworkBehaviour, ILentSlots
    {
        private readonly NetworkVariable<FixedString64Bytes> residentKey =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> displayName =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<short> profileIndex =
            new(StandInIdentity.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<short> goalIndex =
            new(StandInIdentity.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<short> archetypeIndex =
            new(StandInIdentity.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<short> kitIndex =
            new(StandInIdentity.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private StandInIdentity identity = StandInIdentity.Nobody;
        private Resident resident;
        private EntityInventoryComponent bag;
        // The kit weapon put in a resident's own bag while it is with its band at home, and the slot it went in.
        private InventoryItem lent;
        private int lentSlot = NoSlot;
        private bool started;

        private const int NoSlot = -1;

        /// <summary>Who this body stands in for on this machine; <see cref="StandInIdentity.Nobody"/> for a resident at home.</summary>
        public StandInIdentity Identity => identity;

        /// <summary>The body of a resident out with a band, not a resident at home.</summary>
        public bool IsStandIn => identity.IsStandIn;

        /// <summary>A resident at home with its band: mustering, walking out or walking back in, carrying its kit (<see cref="CarryKit"/>).</summary>
        public bool IsWithBand => !IsStandIn && identity.profile != StandInIdentity.None;

        /// <summary>Slot <paramref name="slot"/> of the bag holds the kit weapon lent for the muster and walks: the bag's saver keeps it empty.</summary>
        public bool IsLent(int slot)
        {
            if (lent == null || slot != lentSlot || bag == null) return false;

            InventorySlot held = bag.GetSlot(slot);
            return held != null && !held.IsEmpty && held.Item == lent;
        }

        /// <summary>The kit the band gave this member; null for a resident at home or a goal with no kit for its role.</summary>
        public ExpeditionKit Kit => KitAt(ExpeditionCatalog.Instance.profiles, identity.profile, identity.goal, identity.kit);

        /// <summary>What it carries in hand on the road; null without a kit.</summary>
        public InventoryItem KitWeapon
        {
            get
            {
                ExpeditionKit kit = Kit;
                return kit != null ? kit.weapon : null;
            }
        }

        private void Awake()
        {
            resident = GetComponent<Resident>();
            bag = GetComponent<EntityInventoryComponent>();
        }

        // After ResidentCarry.Start has packed the bag, which fills only an empty one: a weapon lent before it would leave
        // the resident without its own things (a late joiner arriving mid-muster).
        private void Start()
        {
            started = true;
            LendKitWeapon();
        }

        /// <summary>
        /// Server: the resident, at home, is with its band from the muster until the hand-off, or from the swap back until it
        /// is home: it carries member row <paramref name="kitIndex"/> of <paramref name="profile"/>'s goal
        /// <paramref name="goalId"/>, and shows the road. Never on a stand-in.
        /// </summary>
        public void CarryKit(ExpeditionProfile profile, string goalId, int kitIndex)
        {
            if (IsStandIn) return;

            identity = new StandInIdentity(string.Empty, string.Empty, (short)ExpeditionCatalog.Instance.IndexOf(profile),
                                           (short)GoalIndexOf(profile, goalId), StandInIdentity.None, (short)kitIndex);
            if (IsSpawned && IsServer) WriteReplicatedCopy();
            LendKitWeapon();
        }

        /// <summary>Server: the resident is no longer with its band at home (handed off, or home): its kit's weapon goes back.</summary>
        public void PutKitAway()
        {
            if (IsStandIn || !IsWithBand) return;

            identity = StandInIdentity.Nobody;
            if (IsSpawned && IsServer) WriteReplicatedCopy();
            LendKitWeapon();
        }

        /// <summary>
        /// Server, before the network spawn (NpcSpawn.Create's beforeSpawn): makes <paramref name="instance"/> the stand-in of
        /// the band's member <paramref name="memberIndex"/>. Names it, gives it the member's archetype and its kit, shows it on
        /// the road and puts it in the band's formation (<c>record.id</c> is the group's id), led by the band's leader.
        /// </summary>
        public static void Stamp(GameObject instance, ExpeditionRecord record, int memberIndex, ExpeditionProfile profile,
                                 string residentName)
        {
            if (!instance.TryGetComponent(out ExpeditionMember member))
            {
                Debug.LogError($"[Expedition] '{instance.name}' has no ExpeditionMember: band '{record.id}' gets a nameless, unarmed " +
                               "stand-in that keeps no formation. Run Tools/SpaceGame/Expeditions/Prepare Resident Prefabs.", instance);
                return;
            }

            MemberRecord stamped = record.members[memberIndex];
            member.identity = new StandInIdentity(stamped.residentKey, residentName, (short)ExpeditionCatalog.Instance.IndexOf(profile),
                                                  (short)GoalIndexOf(profile, record.goalId), (short)stamped.archetypeIndex,
                                                  (short)stamped.kitIndex);
            if (member.Kit == null)
                Debug.LogWarning($"[Expedition] Band '{record.id}': member '{stamped.residentKey}' has no kit (goal '{record.goalId}', " +
                                 $"row {stamped.kitIndex}), so its stand-in walks unarmed.", instance);

            member.Apply();
            member.Show();
            member.March(record.id, stamped.isLeader);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                WriteReplicatedCopy();
                return;
            }

            residentKey.OnValueChanged += OnTextChanged;
            displayName.OnValueChanged += OnTextChanged;
            profileIndex.OnValueChanged += OnIndexChanged;
            goalIndex.OnValueChanged += OnIndexChanged;
            archetypeIndex.OnValueChanged += OnIndexChanged;
            kitIndex.OnValueChanged += OnIndexChanged;

            // Read once as well as subscribing: a late joiner gets the current values with the spawn.
            ReadReplicatedCopy();
        }

        public override void OnNetworkDespawn()
        {
            residentKey.OnValueChanged -= OnTextChanged;
            displayName.OnValueChanged -= OnTextChanged;
            profileIndex.OnValueChanged -= OnIndexChanged;
            goalIndex.OnValueChanged -= OnIndexChanged;
            archetypeIndex.OnValueChanged -= OnIndexChanged;
            kitIndex.OnValueChanged -= OnIndexChanged;
        }

        private void OnTextChanged(FixedString64Bytes previous, FixedString64Bytes next) => ReadReplicatedCopy();

        private void OnIndexChanged(short previous, short next) => ReadReplicatedCopy();

        private void WriteReplicatedCopy()
        {
            residentKey.Value = new FixedString64Bytes(identity.residentKey);
            displayName.Value = new FixedString64Bytes(identity.displayName);
            profileIndex.Value = identity.profile;
            goalIndex.Value = identity.goal;
            archetypeIndex.Value = identity.archetype;
            kitIndex.Value = identity.kit;
        }

        private void ReadReplicatedCopy()
        {
            identity = new StandInIdentity(residentKey.Value.ToString(), displayName.Value.ToString(), profileIndex.Value,
                                           goalIndex.Value, archetypeIndex.Value, kitIndex.Value);
            if (IsStandIn) Apply();
            else LendKitWeapon();
        }

        // Every machine: the bag holds the kit's weapon while the resident is with its band at home, so the hand can draw it,
        // and gives back only what it was lent. A resident whose own bag already carries that item is lent nothing; one with
        // no room is lent nothing either, and its hands report the missing tool.
        private void LendKitWeapon()
        {
            if (!started || bag == null || IsStandIn) return;

            InventoryItem wanted = IsWithBand ? KitWeapon : null;
            if (wanted == lent) return;

            GiveBack();
            if (wanted == null || Carries(wanted)) return;
            if (bag.TryAddItem(wanted, out int slot)) (lent, lentSlot) = (wanted, slot);
        }

        private void GiveBack()
        {
            if (lent != null && lentSlot >= 0 && lentSlot < bag.Size)
            {
                InventorySlot slot = bag.GetSlot(lentSlot);
                if (slot != null && !slot.IsEmpty && slot.Item == lent) bag.TryRemoveItem(lentSlot);
            }
            (lent, lentSlot) = (null, NoSlot);
        }

        private bool Carries(InventoryItem item)
        {
            for (int i = 0; i < bag.Size; i++)
            {
                InventorySlot slot = bag.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) return true;
            }
            return false;
        }

        // Every machine: the resident's name and archetype, and the bag packed from the kit.
        private void Apply()
        {
            if (resident != null)
            {
                if (!string.IsNullOrEmpty(identity.displayName)) resident.displayName = identity.displayName;

                ResidentArchetype archetype = ArchetypeAt(At(ExpeditionCatalog.Instance.profiles, identity.profile), identity.archetype);
                if (archetype != null) resident.archetype = archetype;
            }

            Pack(Kit);
        }

        // The deciding machine: the presence shows the road, which every machine's hand rule turns into the kit's weapon.
        private void Show()
        {
            if (resident != null && resident.Presence != null) resident.Presence.Publish(Activity.Expedition, 0, false);
        }

        // Server: the band's formation, which the prefab ships switched off so a resident at home is in none.
        private void March(string bandId, bool leads)
        {
            if (!TryGetComponent(out FormationModule formation))
            {
                Debug.LogError($"[Expedition] '{name}' has no FormationModule, so it does not keep up with band '{bandId}'. " +
                               "Run Tools/SpaceGame/Expeditions/Prepare Resident Prefabs.", this);
                return;
            }

            formation.SetFormation(bandId, leads);
            formation.enabled = true;
        }

        // The kit is the whole bag. The weapon goes first, so it is the slot drawn and takes its belt anchor before anything
        // else (BeltSeat.Plan). Written slot by slot and only where it differs, so a repeat changes nothing; it is packed
        // before ResidentCarry.Start, which then leaves the non-empty bag alone.
        private void Pack(ExpeditionKit kit)
        {
            if (bag == null || kit == null) return;

            List<InventoryItem> items = Contents(kit);
            if (items.Count > bag.Size)
                Debug.LogWarning($"[Expedition] Kit '{kit.name}' holds {items.Count} items but '{name}' carries {bag.Size}; the last " +
                                 $"{items.Count - bag.Size} stay home.", this);

            for (int i = 0; i < bag.Size; i++)
            {
                InventoryItem item = i < items.Count ? items[i] : null;
                InventorySlot slot = bag.GetSlot(i);
                InventoryItem held = slot != null && !slot.IsEmpty ? slot.Item : null;
                if (held != item) bag.RestoreSlot(i, item);
            }
        }

        /// <summary>What a kit puts in the bag, in slot order: the weapon, the tool, then the belt items.</summary>
        public static List<InventoryItem> Contents(ExpeditionKit kit)
        {
            var items = new List<InventoryItem>();
            if (kit == null) return items;

            if (kit.weapon != null) items.Add(kit.weapon);
            if (kit.tool != null) items.Add(kit.tool);
            foreach (InventoryItem item in kit.beltItems)
                if (item != null) items.Add(item);
            return items;
        }

        /// <summary>The kit at <paramref name="kit"/> of goal <paramref name="goal"/> of profile <paramref name="profile"/>; null when any index names nothing.</summary>
        public static ExpeditionKit KitAt(IReadOnlyList<ExpeditionProfile> profiles, int profile, int goal, int kit)
        {
            ExpeditionProfile listed = At(profiles, profile);
            ExpeditionGoal drawn = listed != null ? At(listed.goals, goal) : null;
            return drawn != null && kit >= 0 && kit < drawn.kits.Length ? drawn.kits[kit].kit : null;
        }

        /// <summary>The archetype at <paramref name="index"/> of <paramref name="profile"/>'s culture; null when there is none.</summary>
        public static ResidentArchetype ArchetypeAt(ExpeditionProfile profile, int index) =>
            profile != null && profile.culture != null ? At(profile.culture.archetypes, index) : null;

        /// <summary>The index of the goal called <paramref name="goalId"/> among <paramref name="profile"/>'s goals; -1 when it has none.</summary>
        public static int GoalIndexOf(ExpeditionProfile profile, string goalId) =>
            profile != null ? Array.FindIndex(profile.goals, g => g != null && g.id == goalId) : StandInIdentity.None;

        private static T At<T>(IReadOnlyList<T> list, int index) where T : class =>
            list != null && index >= 0 && index < list.Count ? list[index] : null;
    }
}
