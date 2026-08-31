using System.Collections.Generic;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Which seat each arriving player gets.
    ///
    /// <para>
    /// Integers rather than <c>ShipSeat</c> components, so the one part with a subtle requirement —
    /// stability — can be tested without building a GameObject hierarchy. The caller maps the
    /// returned indices back onto its own list.
    /// </para>
    ///
    /// <para>
    /// The rules are lifted from <c>VersusShipSpawner.Seats.cs</c>, which arrived at them first and
    /// for the same reasons. They are re-implemented here rather than shared because that class
    /// resolves seats against a live ship and claims them as a side effect, which is exactly what a
    /// pure ordering helper must not do.
    /// </para>
    /// </summary>
    public static class SeatOrdering
    {
        /// <summary>
        /// Indices into <paramref name="seatOrders"/>, lowest order first, ties keeping their
        /// original position.
        ///
        /// <para>
        /// Insertion sort, not <c>List.Sort</c>. The framework sort is unstable, so seats sharing an
        /// order — which is every seat on a ship nobody has bothered to number — would fill in an
        /// arbitrary sequence that changes between runs. Over a handful of seats the cost is
        /// nothing and the guarantee is the entire point.
        /// </para>
        /// </summary>
        public static int[] OrderedIndices(IReadOnlyList<int> seatOrders)
        {
            int count = seatOrders?.Count ?? 0;
            var indices = new int[count];

            for (int i = 0; i < count; i++) indices[i] = i;

            for (int i = 1; i < count; i++)
            {
                int index = indices[i];
                int order = seatOrders[index];
                int j = i - 1;

                while (j >= 0 && seatOrders[indices[j]] > order)
                {
                    indices[j + 1] = indices[j];
                    j--;
                }

                indices[j + 1] = index;
            }

            return indices;
        }

        /// <summary>
        /// Which of <paramref name="seatCount"/> seats the <paramref name="claim"/>-th arrival
        /// takes, or -1 when there are no seats to take.
        ///
        /// <para>
        /// Wraps rather than refusing. More players than seats is a fair thing to have, and two
        /// bodies briefly sharing a pose push apart on the next physics step — where a player
        /// handed no seat at all is left standing in the sky.
        /// </para>
        /// <para>
        /// The no-seat case returns -1 instead of dividing by zero, and callers are expected to
        /// treat it as the loud failure it is rather than clamping it to seat zero.
        /// </para>
        /// </summary>
        public static int SeatFor(int claim, int seatCount)
        {
            if (seatCount <= 0) return -1;

            return ((claim % seatCount) + seatCount) % seatCount;
        }
    }
}
