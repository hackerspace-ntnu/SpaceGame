// Fires a worn gauntlet the way NpcItemUseModule fires the hand item (D9): the same triggers, cadence,
// aim and facing (ItemUseModuleBase), aimed at the target through EntityBodyEquipment — and only for a
// gauntlet whose asset opts in (InventoryItem.npcUsable). Side effect: never claims movement.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class NpcGauntletUseModule : ItemUseModuleBase
    {
        [Tooltip("Which forearm's gauntlet this fires.")]
        [SerializeField] private BodySlot gauntletSlot = BodySlot.RightGauntlet;

        private WornGauntletUser user;

        // Lazy as well as in Awake, so an EditMode test and a module added at runtime both have one.
        // Null unless the gauntlet is worn and opted in: a bare forearm (or a looted gauntlet) must not
        // keep the NPC aiming and turning at a target it has nothing to fire at.
        protected override INpcItemUser User
        {
            get
            {
                user ??= Resolve();
                return user != null && user.IsReady ? user : null;
            }
        }

        public override string ModuleDescription =>
            "Fires the gauntlet worn on gauntletSlot (EntityBodyEquipment) at the target — only if its " +
            "InventoryItem has npcUsable set. Same triggers, ranges and cadence as NpcItemUseModule.";

        protected override void Awake()
        {
            base.Awake();
            user = Resolve();
            if (user == null)
                Debug.LogWarning($"{name}: NpcGauntletUseModule needs an EntityBodyEquipment to fire anything. It will do nothing.", this);
        }

        private WornGauntletUser Resolve() =>
            TryGetComponent(out EntityBodyEquipment body) ? new WornGauntletUser(body, gauntletSlot) : null;

        protected override bool PrepareUse() => User != null;

        protected override void OnValidate()
        {
            base.OnValidate();
            if (gauntletSlot == BodySlot.Torso) gauntletSlot = BodySlot.RightGauntlet;
        }
    }
}
