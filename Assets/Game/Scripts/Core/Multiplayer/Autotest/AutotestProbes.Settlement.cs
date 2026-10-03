// Queries about the settlement as THIS machine has it: how many residents it can see, where they
// stand, how many animals are still in their pens and how many gates are open.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Core
{
    internal static partial class AutotestProbes
    {
        /// <summary>How close to its measured stand point a resident holding a place has to be to count as standing on it.</summary>
        private const float OnStandPointMetres = 0.15f;

        /// <summary>What one machine counts in a settlement. Compared across the two machines by the caller.</summary>
        internal readonly struct SettlementCensus
        {
            public readonly int residents, hidden, holdingAPlace, onStandPoint, climbing, stock, stockInPens, gates, gatesOpen;

            public SettlementCensus(int residents, int hidden, int holdingAPlace, int onStandPoint, int climbing,
                                    int stock, int stockInPens, int gates, int gatesOpen)
            {
                this.residents = residents;
                this.hidden = hidden;
                this.holdingAPlace = holdingAPlace;
                this.onStandPoint = onStandPoint;
                this.climbing = climbing;
                this.stock = stock;
                this.stockInPens = stockInPens;
                this.gates = gates;
                this.gatesOpen = gatesOpen;
            }
        }

        public static Settlement FindSettlement() => Object.FindFirstObjectByType<Settlement>();

        public static SettlementCensus TakeSettlementCensus(Settlement settlement)
        {
            int hidden = 0, holding = 0, onStand = 0, climbing = 0;
            var residents = settlement.Society.Residents;
            foreach (Resident resident in residents)
            {
                var presence = resident.GetComponent<ResidentPresence>();
                if (presence == null) continue;

                if (presence.Hidden) hidden++;
                if (presence.Activity == Activity.Climbing) climbing++;
                if (presence.Hidden || presence.Place < 0) continue;

                // A machine does not gather trip points, so a resident held at one has no place here to measure against.
                SettlementPlace place = settlement.Society.Place(presence.Place);
                if (place == null) continue;

                holding++;
                Vector3 offset = resident.transform.position - place.Position;
                offset.y = 0f;
                if (offset.magnitude <= OnStandPointMetres) onStand++;
            }

            var pens = settlement.GetComponentsInChildren<SettlementPen>();
            var stockNames = new HashSet<string>();
            int gates = 0, open = 0;
            foreach (SettlementPen pen in pens)
            {
                if (pen.Gate != null)
                {
                    gates++;
                    if (pen.Gate.IsOpen) open++;
                }

                if (pen.StockPrefab != null) stockNames.Add(pen.StockPrefab.name);
            }

            int stock = 0, inPens = 0;
            foreach (AgentController animal in Object.FindObjectsByType<AgentController>(FindObjectsSortMode.None))
            {
                if (!StartsWithAny(animal.name, stockNames)) continue;

                stock++;
                foreach (SettlementPen pen in pens)
                    if (pen.Contains(animal.transform.position))
                    {
                        inPens++;
                        break;
                    }
            }

            return new SettlementCensus(residents.Count, hidden, holding, onStand, climbing, stock, inPens, gates, open);
        }

        private static bool StartsWithAny(string name, HashSet<string> prefixes)
        {
            foreach (string prefix in prefixes)
                if (name.StartsWith(prefix)) return true;
            return false;
        }
    }
}
