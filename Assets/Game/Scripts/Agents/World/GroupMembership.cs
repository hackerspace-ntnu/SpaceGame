// Which world-sim group an NPC was spawned for.
//
// Stamped by NpcWorldSim BEFORE the member's network spawn (NpcSpawn.Create's beforeSpawn), so its
// NpcRandomLoadout roll in OnNetworkSpawn is already seeded by the group. Server-side bookkeeping
// only: clients never have one, and nothing on a client asks.
//
// Four readers: the loadout roll (seed), the war-party director (fighters spawned and dead,
// who dealt a killing blow), the goodwill ledger (the self-defence exemption) and the world sim
// (NpcGroup.Fighters: a rider who dismounted is still the group's to count and to despawn).
//
// It also watches a member that can be ridden (a MountModule): the moment a player mounts it, the
// member is the player's, not the group's, and NpcWorldSim.ReleaseToPlayer takes it out of the group
// (Leave clears this record). Only here because this is the one component every member carries on
// the server and nowhere else -- the decision itself is the world sim's.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class GroupMembership : MonoBehaviour
    {
        /// <summary>Added to the mount's index for the rider it seats, so the two never share a roll.</summary>
        public const int RiderIndexOffset = 1000;

        /// <summary>
        /// Added per seat beyond the saddle (seat 0), so a double's gunners roll apart from its driver
        /// and from each other. Must exceed every group's member count (its plan index range): seat s
        /// of member m is m + RiderIndexOffset + s × stride, which lands on another member m''s saddle
        /// rider (m' + RiderIndexOffset) only if m' = m + s × stride. NpcWorldSim logs an error for a
        /// plan larger than this.
        /// </summary>
        public const int GunnerIndexStride = 100;

        public NpcGroup Group { get; private set; }
        public int MemberIndex { get; private set; }
        public FactionDefinition Tribe { get; private set; }

        /// <summary>False for a mount: a mount carries, the rider fights (AgentSystem.md).</summary>
        public bool IsFighter { get; private set; }

        private HealthComponent health;
        private MountModule mount;

        public static GroupMembership Stamp(GameObject member, NpcGroup group, int memberIndex,
                                            FactionDefinition tribe)
        {
            if (member == null || group == null) return null;

            if (!member.TryGetComponent(out GroupMembership membership))
                membership = member.AddComponent<GroupMembership>();

            membership.Group = group;
            membership.MemberIndex = memberIndex;
            membership.Tribe = tribe;

            // A mount is carried into the fight by its rider's standing, not its own; a machine with
            // no health (a walking city's houses and workers) can never fall, so neither is a fighter.
            membership.IsFighter = !member.TryGetComponent(out NpcPassenger _) &&
                                   member.TryGetComponent(out HealthComponent _);

            if (membership.IsFighter) membership.Enlist();
            membership.WatchForPlayerRider();
            return membership;
        }

        /// <summary>
        /// Called for an NPC a mount seats, before that NPC spawns: by <see cref="NpcPassenger"/> for
        /// the driver in the saddle (seat 0), and by MountedGunners for each gunner (seat 1 and up).
        /// </summary>
        public static void StampRider(GameObject mount, GameObject rider, int seat = 0)
        {
            if (mount == null || !mount.TryGetComponent(out GroupMembership membership) || membership.Group == null)
                return;

            Stamp(rider, membership.Group,
                  membership.MemberIndex + RiderIndexOffset + seat * GunnerIndexStride, membership.Tribe);
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

        /// <summary>
        /// No longer any group's: a player took this member for themselves. Its death is nobody's
        /// loss now, and a second player mounting it later has nothing to take it from.
        /// </summary>
        public void Leave()
        {
            if (health != null) health.OnDeath -= OnDied;
            health = null;
            if (mount != null) mount.Mounted -= OnPlayerMounted;
            mount = null;
            Group = null;
            IsFighter = false;
        }

        private void WatchForPlayerRider()
        {
            if (mount != null || !TryGetComponent(out mount)) return;
            mount.Mounted += OnPlayerMounted;
        }

        private void OnPlayerMounted(PlayerMovement _)
        {
            // The server's seating is the decision (MountNetworkSync.SeatOnServer); a peer replaying
            // it has no membership to act on anyway.
            if (!Network.Decides || Group == null) return;

            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim != null) sim.ReleaseToPlayer(gameObject);
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
            if (mount != null) mount.Mounted -= OnPlayerMounted;
        }
    }
}
