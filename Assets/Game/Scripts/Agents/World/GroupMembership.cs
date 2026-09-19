// Which world-sim group an NPC was spawned for.
//
// Stamped by NpcWorldSim BEFORE the member's network spawn (NpcSpawn.Create's beforeSpawn), so its
// NpcRandomLoadout roll in OnNetworkSpawn is already seeded by the group. Server-side bookkeeping
// only: clients never have one, and nothing on a client asks.
//
// Four readers: the loadout roll (seed), the war-party director (fighters spawned and dead,
// who dealt a killing blow), the goodwill ledger (the self-defence exemption) and the world sim
// (NpcGroup.Fighters: a rider who dismounted is still the group's to count and to despawn).
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class GroupMembership : MonoBehaviour
    {
        /// <summary>Added to the mount's index for the rider it seats, so the two never share a roll.</summary>
        public const int RiderIndexOffset = 1000;

        public NpcGroup Group { get; private set; }
        public int MemberIndex { get; private set; }
        public FactionDefinition Tribe { get; private set; }

        /// <summary>False for a mount: a mount carries, the rider fights (AgentSystem.md).</summary>
        public bool IsFighter { get; private set; }

        private HealthComponent health;

        public static GroupMembership Stamp(GameObject member, NpcGroup group, int memberIndex,
                                            FactionDefinition tribe)
        {
            if (member == null || group == null) return null;

            if (!member.TryGetComponent(out GroupMembership membership))
                membership = member.AddComponent<GroupMembership>();

            membership.Group = group;
            membership.MemberIndex = memberIndex;
            membership.Tribe = tribe;
            membership.IsFighter = !member.TryGetComponent(out NpcPassenger _);

            if (membership.IsFighter) membership.Enlist();
            return membership;
        }

        /// <summary>Called by <see cref="NpcPassenger"/> for the rider it seats, before that rider spawns.</summary>
        public static void StampRider(GameObject mount, GameObject rider)
        {
            if (mount == null || !mount.TryGetComponent(out GroupMembership membership) || membership.Group == null)
                return;

            Stamp(rider, membership.Group, membership.MemberIndex + RiderIndexOffset, membership.Tribe);
        }

        /// <summary>The group id of whoever <paramref name="source"/> belongs to, or null.</summary>
        public static string GroupIdOf(Transform source)
        {
            if (source == null) return null;

            GroupMembership membership = source.GetComponentInParent<GroupMembership>();
            return membership != null && membership.Group != null ? membership.Group.Id : null;
        }

        /// <summary>The one who fights for this member: the seated rider on a mount, else the member.</summary>
        public static GameObject FighterOf(GameObject member) =>
            member != null && member.TryGetComponent(out NpcPassenger passenger) && passenger.HasRider
                ? passenger.Rider
                : member;

        /// <summary>How many of <paramref name="fighters"/> still exist and are not dead. No health counts as standing.</summary>
        public static int CountStanding(IReadOnlyList<GameObject> fighters)
        {
            int standing = 0;

            foreach (GameObject fighter in fighters)
                if (fighter != null && (!fighter.TryGetComponent(out HealthComponent health) || health.Alive))
                    standing++;

            return standing;
        }

        private void Enlist()
        {
            // The mount keeps its own faction; only a fighter takes the tribe. Null table keeps the
            // prefab's relationships (EntityFaction.SetFaction).
            if (Tribe != null) EntityFaction.Ensure(gameObject, Tribe, null);

            if (!Group.Fighters.Contains(gameObject))
            {
                Group.Fighters.Add(gameObject);
                Group.FightersSpawned++;
            }

            if (health != null) health.OnDeath -= OnDied;
            health = GetComponent<HealthComponent>();
            if (health != null) health.OnDeath += OnDied;
        }

        private void OnDied()
        {
            // A restore replaying a death is not a death in this fight.
            if (health != null && health.IsRestoring) return;
            if (Group != null) Group.FightersDead++;
        }

        private void OnDestroy()
        {
            if (health != null) health.OnDeath -= OnDied;
        }
    }
}
