// Queries about the settlement as THIS machine has it: how many residents it can see, where they
// stand, how many animals are still in their pens and how many gates are open.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
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

        /// <summary>
        /// The loaded settlement that has residents. Not merely the first settlement: the astronaut colony in the chunk beside
        /// the nomads has no culture, so no society, and loads with the same 3x3.
        /// </summary>
        public static Settlement FindSettlement()
        {
            foreach (Settlement settlement in Object.FindObjectsByType<Settlement>(FindObjectsSortMode.None))
                if (settlement.HasResidents) return settlement;
            return null;
        }

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

        /// <summary>
        /// What every resident visibly does on THIS machine, on one line: the replicated state (activity, place, seat, cart) and what
        /// the machine derived from it (the tool in the hand, the hold style, whether the seat and the cart really are held, the loops
        /// playing). The clock minute leads, so the host's line and the client's can be put side by side and must agree resident by
        /// resident; a resident that differs is a body one machine derives differently.
        /// </summary>
        public static string TakeBodyLine(Settlement settlement)
        {
            SettlementSociety society = settlement.Society;
            var line = new StringBuilder(society.NowMinutes.ToString("0.00", CultureInfo.InvariantCulture));
            foreach (Resident resident in society.Residents)
            {
                var presence = resident.GetComponent<ResidentPresence>();
                if (presence == null) continue;

                var equipment = resident.GetComponent<EntityEquipmentController>();
                var actions = resident.GetComponentInChildren<CharacterActions>(true);
                var animator = resident.GetComponentInChildren<Animator>(true);
                Seat seat = Seat.Find(presence.SeatId);
                Pushable cart = Pushable.Find(presence.CartId);

                line.Append(" | ").Append(resident.index).Append(':').Append(presence.Activity)
                    .Append(" place=").Append(presence.Place)
                    .Append(" seat=").Append(presence.SeatId).Append(seat != null && seat.Occupant == resident.transform ? "+" : "-")
                    .Append(" cart=").Append(presence.CartId).Append(cart != null && cart.Holder == resident.transform ? "+" : "-")
                    .Append(" hidden=").Append(presence.Hidden ? 1 : 0)
                    .Append(" hand=").Append(equipment != null && equipment.EquippedItem != null ? equipment.EquippedItem.name : "-")
                    .Append(" hold=").Append(animator != null && animator.runtimeAnimatorController != null ? animator.GetInteger(HumanoidParams.HoldStyle) : -1);

                if (actions == null) continue;
                foreach (CharacterAction.Slot slot in new[] { CharacterAction.Slot.Full, CharacterAction.Slot.Upper })
                {
                    CharacterAction playing = actions.PlayingOn(slot);
                    if (playing != null && playing.Loops) line.Append(' ').Append(slot).Append('=').Append(playing.name.Replace(' ', '_'));
                }
            }
            return line.ToString();
        }

        private static bool StartsWithAny(string name, HashSet<string> prefixes)
        {
            foreach (string prefix in prefixes)
                if (name.StartsWith(prefix)) return true;
            return false;
        }
    }
}
