// The order a group's shuffled members take their formation slots in, dealt like a deck of cards.
//
// Followers take slots in spawn order, so a template listed by kind marches by kind: the Strider city
// came out as its houses, then its crawlers, its crabs and its barges (the user, 2026-10-04 and
// 2026-10-05: "like drawing from a card deck"). A member whose spec is shuffled is a card; the group's
// roster seed shuffles the deck and each slot takes the next card drawn that keeps its rules there. The
// seed is the group's, saved in its record, so a refold or a reload deals the same column and a new
// world deals its own. Pure, so it is tested without a scene; the server alone calls it (Resolve).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>A member's place in a shuffled column, and the rules it keeps there (NpcGroupMemberSpec.column).</summary>
    [Serializable]
    public struct ColumnCard
    {
        [Tooltip("Takes a seeded place among the template's other shuffled members instead of its listed " +
                 "one. The group's roster seed deals the order, so a refold or a reload keeps it.")]
        public bool shuffled;

        [Tooltip("May not ride beside a member of the same kind in a row, nor nose to tail behind one in " +
                 "a lane. Empty: rides anywhere.")]
        public string kind;

        [Tooltip("Rides in one of the first this-many follower slots (0: any), e.g. on the ground a " +
                 "stop levels.")]
        [Min(0)]
        public int withinFirstSlots;

        [Tooltip("Must clear every other member at their slots, footprint to footprint: for a hull " +
                 "longer than a row is deep.")]
        public bool keepsClear;

        [Tooltip("The member seen from above, in its own space: x right, y forward. Written by the " +
                 "template's authoring tool from the prefab's renderers.")]
        public Rect footprint;

        [Tooltip("How far off its slot the member may park (its FormationModule's slotTolerance).")]
        [Min(0f)]
        public float slotTolerance;
    }

    public static class ColumnDeal
    {
        /// <summary>
        /// Cards one deal may lay (each one tried in a slot counts) before it is given up and the deck
        /// dealt afresh: a card early on can leave the last slots nothing that fits, and taking cards back
        /// one by one would try every order of the slots between.
        /// </summary>
        private const int LaidPerDeal = 400;

        /// <summary>Fresh deals before giving up: a deck whose rules leave no column fails here instead of hanging the spawn.</summary>
        private const int MaxDeals = 200;

        // Keeps the column's shuffle apart from the roster deals made from the same seed.
        private const int DealSalt = 0x0C01;

        // The leader rides at the origin, ahead of follower slot 0.
        private const int LeaderSlot = -1;

        /// <summary>
        /// Deals <paramref name="cards"/> into <paramref name="slots"/> (follower slot indices, ascending):
        /// <paramref name="order"/>[i] is the card that takes slots[i]. Slot by slot, the next card is
        /// drawn from what is left of the shuffled deck; a card that breaks a rule there goes back and the
        /// next is drawn, a slot nothing fits takes back the card before it, and a deal that lays too many
        /// starts again from a fresh shuffle. The same seed deals the same order. False, with an error
        /// logged, when no order keeps the rules.
        /// </summary>
        public static bool TryDeal(ColumnCard leader, IReadOnlyList<ColumnCard> cards, IReadOnlyList<int> slots,
                                   FormationShape shape, int seed, out int[] order)
        {
            int count = cards.Count;
            int dealSeed = (int)RosterDraw.Hash(seed, DealSalt);
            int shuffles = 0, laid = 0;
            var dealt = new int[count];
            var used = new bool[count];

            // Two cards alike fit or break alike: a slot tries one of each, never the second.
            var alike = new int[count];
            for (int i = 0; i < count; i++)
            {
                alike[i] = i;
                for (int j = 0; j < i; j++)
                {
                    if (!cards[j].Equals(cards[i])) continue;
                    alike[i] = alike[j];
                    break;
                }
            }

            // The cards bound to their first slots, tightest first (RestFits).
            var bound = new List<int>();
            for (int i = 0; i < count; i++)
                if (cards[i].withinFirstSlots > 0) bound.Add(i);
            bound.Sort((a, b) => cards[a].withinFirstSlots.CompareTo(cards[b].withinFirstSlots));

            for (int deal = 0; deal < MaxDeals; deal++)
            {
                laid = 0;
                if (DealFrom(0))
                {
                    order = dealt;
                    return true;
                }
            }

            Debug.LogError($"[ColumnDeal] No column of {count} members keeps every member's rules (its first " +
                           $"slots, no kind beside or behind its own, clear of its neighbours) in {MaxDeals} deals. " +
                           "Widen the formation or loosen the rules.");
            order = null;
            return false;

            bool DealFrom(int position)
            {
                if (position == count) return true;

                var deck = new List<int>(count - position);
                for (int i = 0; i < count; i++)
                    if (!used[i]) deck.Add(i);
                for (int i = deck.Count - 1; i > 0; i--)
                {
                    int j = RosterDraw.IndexFor(dealSeed, shuffles++, i + 1);
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                }

                var tried = new List<int>(deck.Count);
                foreach (int card in deck)
                {
                    if (tried.Contains(alike[card])) continue;
                    tried.Add(alike[card]);
                    if (++laid > LaidPerDeal) return false;
                    if (!Fits(card, position)) continue;

                    used[card] = true;
                    dealt[position] = card;
                    if (RestFits(position + 1) && DealFrom(position + 1)) return true;
                    used[card] = false;
                }

                return false;
            }

            // Whether card keeps its rules in slots[position], beside the leader and the cards dealt before it.
            bool Fits(int card, int position)
            {
                ColumnCard c = cards[card];
                int slot = slots[position];
                if (c.withinFirstSlots > 0 && slot >= c.withinFirstSlots) return false;
                if (Touch(leader, LeaderSlot, c, slot, shape)) return false;

                for (int q = 0; q < position; q++)
                {
                    ColumnCard placed = cards[dealt[q]];
                    if (Neighbours(placed, slots[q], c, slot, shape) || Touch(placed, slots[q], c, slot, shape)) return false;
                }

                return true;
            }

            // Whether the bound cards left can still ride in their first slots: the k-th tightest needs a
            // free slot before its limit, and the free slots are slots[next...], in order.
            bool RestFits(int next)
            {
                int k = 0;
                foreach (int card in bound)
                {
                    if (used[card]) continue;
                    if (slots[next + k] >= cards[card].withinFirstSlots) return false;
                    k++;
                }

                return true;
            }
        }

        /// <summary>
        /// The first rule <paramref name="column"/> breaks, as a description, or null. column[i] rides
        /// in follower slot slots[i]; slot s is row s / Lanes, lane s % Lanes (FormationMath.SlotOffset).
        /// </summary>
        public static string FirstBroken(ColumnCard leader, IReadOnlyList<ColumnCard> column, IReadOnlyList<int> slots,
                                         FormationShape shape)
        {
            for (int i = 0; i < column.Count; i++)
            {
                if (column[i].withinFirstSlots > 0 && slots[i] >= column[i].withinFirstSlots)
                    return $"a member that rides in the first {column[i].withinFirstSlots} slots is in slot {slots[i]}";
                if (Touch(leader, LeaderSlot, column[i], slots[i], shape))
                    return $"the leader and slot {slots[i]} overlap";

                for (int j = 0; j < i; j++)
                {
                    if (Neighbours(column[j], slots[j], column[i], slots[i], shape))
                        return $"{column[i].kind} in slots {slots[j]} and {slots[i]}, beside or behind its own kind";
                    if (Touch(column[j], slots[j], column[i], slots[i], shape))
                        return $"slots {slots[j]} and {slots[i]} overlap";
                }
            }

            return null;
        }

        /// <summary>Two of one kind side by side in a row, or nose to tail in a lane.</summary>
        private static bool Neighbours(ColumnCard a, int slotA, ColumnCard b, int slotB, FormationShape shape)
        {
            if (string.IsNullOrEmpty(a.kind) || a.kind != b.kind) return false;

            int lanes = Mathf.Max(1, shape.Lanes);
            int first = Mathf.Min(slotA, slotB), second = Mathf.Max(slotA, slotB);
            bool besideInRow = second == first + 1 && second % lanes > 0;
            bool behindInLane = second == first + lanes;
            return besideInRow || behindInLane;
        }

        /// <summary>
        /// Whether two members, one of which keeps clear, can touch. They clear each other when they are
        /// apart side to side or nose to tail by their jitter and drift at their worst, or by both parked
        /// as far off their slots as their slotTolerance lets them.
        /// </summary>
        private static bool Touch(ColumnCard a, int slotA, ColumnCard b, int slotB, FormationShape shape)
        {
            if (!a.keepsClear && !b.keepsClear) return false;

            Rect p = AreaAt(a, slotA, shape), q = AreaAt(b, slotB, shape);
            float side = Mathf.Max(p.xMin - q.xMax, q.xMin - p.xMax);
            float length = Mathf.Max(p.yMin - q.yMax, q.yMin - p.yMax);
            float parked = a.slotTolerance + b.slotTolerance;
            float sideSlop = 2f * (shape.LateralJitter + shape.DriftAmplitude);
            float lengthSlop = 2f * (shape.LongitudinalJitter + shape.DriftAmplitude);
            return side < Mathf.Max(sideSlop, parked) && length < Mathf.Max(lengthSlop, parked);
        }

        /// <summary>A member's footprint at its slot, seen from above: x right of the leader, y ahead of it.</summary>
        private static Rect AreaAt(ColumnCard card, int slot, FormationShape shape)
        {
            // SlotOffset's y is metres behind; the column's is metres ahead.
            Vector2 at = slot == LeaderSlot ? Vector2.zero : FormationMath.SlotOffset(slot, shape) * new Vector2(1f, -1f);
            return new Rect(card.footprint.position + at, card.footprint.size);
        }
    }
}
