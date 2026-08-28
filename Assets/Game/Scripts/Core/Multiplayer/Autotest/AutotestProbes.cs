// What THIS machine has: the questions both halves of the autotest ask of their own world.
//
// Everything here is found rather than asked of a component that would know, and picked by a rule
// both processes can apply independently, because a process that was never told about something
// has no component to ask — which is precisely the state under test.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    internal static class AutotestProbes
    {
        /// <summary>
        /// How many portal apertures stand on this machine.
        ///
        /// Counted by finding them rather than asked of a pair, because the question is what this
        /// MACHINE has: a client that never heard about a shooter's portals has no PortalPair to
        /// ask in the first place, which is exactly the state being tested for.
        /// </summary>
        public static int CountPortals() =>
            Object.FindObjectsByType<SpaceGame.Portals.Portal>(FindObjectsSortMode.None).Length;

        /// <summary>
        /// How many nets stand on this machine, and how many bodies they hold between them.
        ///
        /// Found rather than asked of a shooter's <c>SnareReceiver</c>, for the reason
        /// <see cref="CountPortals"/> gives: a machine that was never told about a net has no
        /// receiver entry to ask, which is precisely the state under test. A net that exists but
        /// holds nothing is the other half of the same failure, so both numbers come out together.
        /// </summary>
        public static int CountNets(out int captives)
        {
            SnareCatch[] nets = Object.FindObjectsByType<SnareCatch>(FindObjectsSortMode.None);

            captives = 0;
            foreach (SnareCatch net in nets) captives += net.Captives.Count;

            return nets.Length;
        }

        /// <summary>
        /// Whether a net is holding this creature.
        ///
        /// Asked of the children as well as the root because the two machines can bind different
        /// objects: the server captures whatever collider its <c>OverlapBox</c> returned, while a
        /// peer is told a NetworkObjectId and <c>NetArg.Resolve</c> only ever answers with the
        /// spawned ROOT. Both end up hobbling the same NavMeshAgent, and both count as bound.
        /// </summary>
        public static bool IsNetted(GameObject creature)
        {
            if (creature == null) return false;

            SnareTether tether = creature.GetComponentInChildren<SnareTether>();
            SnaredBody snared = creature.GetComponentInChildren<SnaredBody>();

            return (tether != null && tether.IsBound) || (snared != null && snared.IsBound);
        }

        /// <summary>
        /// A networked entity with health that is NOT a player — an AI, which is the case that
        /// never replicated before this work.
        ///
        /// Lowest NetworkObjectId wins, deliberately. Ids are assigned by the server and
        /// replicated, so this rule picks the same entity in both processes; dictionary order
        /// does not, and neither does name (persistentScene holds two "DuneRat").
        /// </summary>
        public static HealthComponent FindNetworkedVictim(out ulong id)
        {
            HealthComponent best = null;
            id = 0;

            foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                NetworkObject netObj = pair.Value;
                if (netObj == null || netObj.IsPlayerObject) continue;
                if (netObj.GetComponent<NetAuthority>() == null) continue;

                HealthComponent health = netObj.GetComponentInChildren<HealthComponent>();
                if (health == null || !health.Alive) continue;

                if (best == null || pair.Key < id)
                {
                    best = health;
                    id = pair.Key;
                }
            }

            return best;
        }

        /// <summary>
        /// A networked creature, chosen by the same lowest-id rule
        /// <see cref="FindNetworkedVictim"/> uses and for the same reason: both processes have
        /// to land on the same one without either being told which.
        ///
        /// An <c>AgentController</c> rather than merely something with health, because that is
        /// exactly what <c>SnareCatch.Capture</c> will accept — anything that is neither a
        /// player nor a creature is refused however wide the gun's layer mask is.
        /// </summary>
        public static AgentController FindNetworkedQuarry(out ulong id)
        {
            AgentController best = null;
            id = 0;

            foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                NetworkObject netObj = pair.Value;
                if (netObj == null || netObj.IsPlayerObject) continue;

                var agent = netObj.GetComponentInChildren<AgentController>();
                if (agent == null) continue;

                if (best == null || pair.Key < id)
                {
                    best = agent;
                    id = pair.Key;
                }
            }

            return best;
        }

        public static GameObject LocalPlayerObject()
        {
            NetworkObject player = NetworkManager.Singleton.LocalClient?.PlayerObject;
            return player != null ? player.gameObject : null;
        }

        /// <summary>The relay on the lowest-id spawned object — same answer in both processes.</summary>
        public static NetRelay LowestIdRelay()
        {
            NetRelay best = null;
            ulong bestId = 0;

            foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                NetRelay relay = pair.Value != null ? pair.Value.GetComponent<NetRelay>() : null;
                if (relay == null) continue;

                if (best == null || pair.Key < bestId)
                {
                    best = relay;
                    bestId = pair.Key;
                }
            }

            return best;
        }
    }
}
