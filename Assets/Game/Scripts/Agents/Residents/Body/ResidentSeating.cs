// A resident's body on a Seat: the deciding machine's half of sitting.
//
// A spot whose use is a sit does not make a resident sit by itself. Once the resident has walked to the spot's stand
// point, this claims the nearest free Seat there, takes the resident's feet (the NavMesh agent and its motors, through the
// same NpcSeating a vessel's passengers use) and puts the body on the seat, facing the way the seat faces. A spot with no
// free Seat makes nobody sit: the resident stays standing and the gap is reported once per spot.
//
// It lets go when the plan moves the resident on, when something overrides the plan, when the resident goes indoors,
// and when the seat is carried away from where it was claimed; a resident that dies or is torn down is let go where it is. The body goes back to the spot's stand point, on
// the NavMesh, and the agent is switched back on there.
//
// Server only. Every other machine learns the claim from the seat id ResidentPresence publishes, and sees the body move
// through the resident's own transform sync. A claim is never saved: after a load the plan puts the resident at its spot
// again and it sits again.
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class ResidentSeating
    {
        private readonly GameObject body;
        private readonly NpcSeating carried = new();
        private Seat seat;
        private int place = ResidentPresence.NoPlace;
        private Vector3 standPoint;
        private bool thrownOff;

        public ResidentSeating(GameObject body) => this.body = body;

        public bool IsSeated => seat != null;

        /// <summary>The place the resident sits at; <see cref="ResidentPresence.NoPlace"/> when it does not.</summary>
        public int Place => IsSeated ? place : ResidentPresence.NoPlace;

        /// <summary>The id of the seat the resident sits on; 0 when it does not.</summary>
        public int SeatId => seat != null ? seat.Id : 0;

        /// <summary>
        /// Brings the body in line with the plan. <paramref name="wanted"/> is the place the plan has the resident at (or
        /// <see cref="ResidentPresence.NoPlace"/>), <paramref name="arrived"/> whether the walk to it is over.
        /// </summary>
        public void Sync(SettlementSociety society, int wanted, bool arrived)
        {
            if (!Network.Decides) return;

            if (IsSeated && (thrownOff || wanted != place)) StandUp();
            if (IsSeated || !arrived || wanted == ResidentPresence.NoPlace) return;

            SettlementPlace spot = society.Place(wanted);
            if (spot == null || !spot.Seated || !spot.Usable) return;

            Sit(society, wanted, spot);
        }

        /// <summary>Stands the resident up at its spot's stand point, to go indoors or carry on.</summary>
        public void Release()
        {
            if (IsSeated) StandUp();
        }

        /// <summary>Lets go without moving the body: it died where it sat, or is being torn down and nothing may be placed.</summary>
        public void Abandon()
        {
            if (!IsSeated) return;

            Seat from = seat;
            seat = null;
            thrownOff = false;
            place = ResidentPresence.NoPlace;
            from.Vacated -= OnVacated;
            from.Release(body.transform);
            carried.Abandon();
        }

        private void Sit(SettlementSociety society, int wanted, SettlementPlace spot)
        {
            Vector3 spotPoint = society.SpotAt(wanted).Position;
            float reach = ResidentTuning.Instance.seatReach;
            Seat found = Seat.NearestFree(spotPoint, reach);
            if (found == null)
            {
                // Every seat there taken is the ordinary case of a busy hearth: the latecomer stands. No seat at all is a fault.
                if (!Seat.AnyWithin(spotPoint, reach) && society.ReportOnce(wanted))
                    Debug.LogWarning($"[Residents] {society.Name}: place {wanted} is a sit, but there is no Seat within {reach:0.#} m of it, " +
                                     "so nobody sits there. Run Tools > SpaceGame > Residents > Place Seats At Sit Spots.", body);
                return;
            }
            if (!found.TryClaim(body.transform)) return;

            seat = found;
            place = wanted;
            standPoint = spot.Position;
            seat.Vacated += OnVacated;
            carried.Suppress(body);
            NetworkedTeleport.Move(body, seat.FeetPosition, seat.Facing);
        }

        // Back onto the NavMesh at the spot's stand point, and the feet back on.
        private void StandUp()
        {
            Seat from = seat;
            seat = null;
            thrownOff = false;
            place = ResidentPresence.NoPlace;
            from.Vacated -= OnVacated;
            from.Release(body.transform);

            NetworkedTeleport.Move(body, standPoint, body.transform.rotation);
            carried.Restore(body, ResidentTuning.Instance.standSnapRadius);
        }

        // The seat was carried away (or switched off, as a chunk unloads): the sitter is thrown off. It stands up at the next
        // Sync, not here — this is raised from the seat's own teardown too, when nothing may be teleported — and the evaluation
        // after that finds it a seat again.
        private void OnVacated(Seat vacated, Transform who)
        {
            if (body != null && who == body.transform && vacated == seat) thrownOff = true;
        }
    }
}
