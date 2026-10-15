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

        // Lazy as well as in Awake, so an EditMode test, a module added at runtime and an AgentTargeting
        // that wakes first all find one.
        private WornGauntletUser Resolved => user ??= Resolve();

        // Null unless the gauntlet is worn and opted in: a bare forearm (or a looted gauntlet) must not
        // keep the NPC aiming and turning at a target it has nothing to fire at, nor widen its reach.
        protected override INpcItemUser User => Resolved is { IsReady: true } ready ? ready : null;

        public override string ModuleDescription =>
            "Fires the gauntlet worn on gauntletSlot (EntityBodyEquipment) at the target — only if its " +
            "InventoryItem has npcUsable set. Same triggers, ranges and cadence as NpcItemUseModule.";

        protected override void Awake()
        {
            base.Awake();
            if (Resolved == null)
                Debug.LogWarning($"{name}: NpcGauntletUseModule needs an EntityBodyEquipment to fire anything. It will do nothing.", this);
        }

        private WornGauntletUser Resolve()
        {
            if (!TryGetComponent(out EntityBodyEquipment body)) return null;
            var resolved = new WornGauntletUser(body, gauntletSlot);
            resolved.ReadyChanged += RaiseReachChanged;
            return resolved;
        }

        private void OnDestroy() => user?.Dispose();

        protected override bool PrepareUse() => User != null;

        protected override void OnValidate()
        {
            base.OnValidate();
            if (gauntletSlot == BodySlot.Torso) gauntletSlot = BodySlot.RightGauntlet;
        }
    }
}
