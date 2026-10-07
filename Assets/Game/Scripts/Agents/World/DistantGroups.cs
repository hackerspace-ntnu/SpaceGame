// Where every group seen from afar stands, on every machine (SettlementLods.md, "The distant city").
//
// NpcWorldSim decides on the server alone, and its GameObject carries no NetworkObject, so the groups a
// template marks showFromAfar are published here, on the session's one NetworkObject (NetworkGameManager
// in persistentScene): a NetworkList the server rewrites a couple of times a second -- only entries that
// changed go out -- and NGO hands a late joiner whole with the spawn. The host reads the same list a
// client does, so DistantGroupSilhouette has one code path. Nothing is saved here: the group's record
// (position, rosterSeed) already is.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public sealed class DistantGroups : NetworkBehaviour
    {
        [Tooltip("Seconds between the server's writes. A folded group steps once per NpcWorldSim tick (1 s), so " +
                 "twice a second never leaves the drawn city more than a tick behind.")]
        [Min(0.05f)]
        [SerializeField] private float publishInterval = 0.5f;

        // Built with the behaviour: NGO registers NetworkVariables when it initialises them.
        private NetworkList<DistantGroupState> states = new();
        private readonly List<DistantGroupState> desired = new();
        private float publishTimer;

        /// <summary>Groups published; 0 until this object has spawned.</summary>
        public int Count => IsSpawned ? states.Count : 0;

        public DistantGroupState this[int index] => states[index];

        /// <summary>Every group in <paramref name="groups"/> whose template is showFromAfar and that is not wiped out, into <paramref name="into"/> (cleared first).</summary>
        public static void Collect(IReadOnlyList<NpcGroup> groups, Func<string, NpcGroupTemplate> templateFor, List<DistantGroupState> into)
        {
            into.Clear();
            foreach (NpcGroup group in groups)
            {
                if (group == null || group.WipedOut) continue;

                NpcGroupTemplate template = templateFor(group.TemplateId);
                if (template == null || !template.showFromAfar) continue;

                into.Add(DistantGroupState.Of(group));
            }
        }

        private void Update()
        {
            if (!IsSpawned || !Network.Simulates(this)) return;

            publishTimer -= Time.deltaTime;
            if (publishTimer > 0f) return;
            publishTimer = publishInterval;

            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return;

            Collect(sim.Groups, sim.FindTemplate, desired);
            Publish();
        }

        /// <summary>Writes only the entries that differ, so an idle city sends nothing.</summary>
        private void Publish()
        {
            for (int i = 0; i < desired.Count; i++)
            {
                if (i >= states.Count) states.Add(desired[i]);
                else if (!states[i].Equals(desired[i])) states[i] = desired[i];
            }
            while (states.Count > desired.Count) states.RemoveAt(states.Count - 1);
        }
    }
}
