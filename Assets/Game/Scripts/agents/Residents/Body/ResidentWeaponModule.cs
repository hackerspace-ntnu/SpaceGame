// A resident fires what it holds at whoever it is fighting — when what it holds is a weapon an NPC can aim
// (InventoryItem.npcUsable: a guard's shock harpoon). A resident whose item is a tool (a hammer, a spear) is never
// armed, reaches no further and fights with its fists as before; so the module sits on every resident and does
// nothing on most of them.
//
// It never touches the hand. ResidentHands owns that on every machine, from replicated state, because NPC equipment
// is not replicated: a hand drawn on the server alone would leave every client holding something else, and a peer
// drops a shot whose slot is not the one in its hand. So this says only that the resident is ARMED (in a fight with
// its weapon); the routine publishes it, every machine's hand draws the weapon, and only once it is drawn here does
// a press go out.
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [DisallowMultipleComponent]
    public sealed class ResidentWeaponModule : ItemUseModuleBase
    {
        // First values for a guard's shock harpoon: inside its 22 m wire, and a press only once a full shot (out, 1.5 s
        // in the body, reeled home) is over, so no press is refused for being early. Below the shortest shot the fists take over (CloseCombatModule).
        private const float FirstShortestShot = 4f;
        private const float FirstLongestShot = 20f;
        private const float FirstCooldown = 7.5f;

        private Resident resident;
        private EntityEquipmentController hand;
        private AgentTargeting targeting;
        private bool hadWeapon;

        // Lazy: AgentTargeting reads MaxRange (which asks for a User) from its own Awake, which may run before this one.
        // Unity null, not interface null: a destroyed controller must read as nothing to use.
        protected override INpcItemUser User => HoldsWeapon ? hand : null;

        /// <summary>Server: in a fight, with a weapon it can fire. Published, so every machine draws it.</summary>
        public bool Armed
        {
            get
            {
                if (!targeting) targeting = GetComponent<AgentTargeting>();
                return HoldsWeapon && targeting && targeting.HasTarget;
            }
        }

        private bool HoldsWeapon
        {
            get
            {
                if (!resident) resident = GetComponent<Resident>();
                if (!hand) hand = GetComponent<EntityEquipmentController>();
                InventoryItem item = resident ? resident.HeldItem : null;
                return hand && item != null && item.npcUsable;
            }
        }

        public override string ModuleDescription =>
            "Server. Fires the resident's own item (Resident.HeldItem) at its fight target, when that item is " +
            "an NPC-usable weapon (InventoryItem.npcUsable): a guard's shock harpoon. Anything else, and the " +
            "module does nothing and widens nothing.\n\n" +
            "• Never draws: it reports Armed, ResidentPresence replicates it, ResidentHands draws on every machine\n" +
            "• Fires only once the hand holds the weapon\n" +
            "• minRange / maxRange / cooldown — fit them to the weapon (the harpoon: 22 m wire, ~7 s from press to home)";

        private void Reset()
        {
            SetPriorityDefault(ModulePriority.RangedAttack);
            (minRange, maxRange, cooldown) = (FirstShortestShot, FirstLongestShot, FirstCooldown);
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // A band's kit weapon replaces the archetype's item while the resident is with it, so the reach can change.
            bool holds = HoldsWeapon;
            if (holds != hadWeapon)
            {
                hadWeapon = holds;
                RaiseReachChanged();
            }

            return base.Tick(in context, deltaTime);
        }

        protected override bool PrepareUse() => HoldsWeapon && hand.HasItem && hand.EquippedItem == resident.HeldItem;
    }
}
