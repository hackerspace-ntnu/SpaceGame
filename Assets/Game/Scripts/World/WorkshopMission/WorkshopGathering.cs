// The workshop's residents leaving their day to meet whoever opened the gate, and going back to it afterwards.
//
// Server only. A resident is held by a scripted override (the routine walks it there and holds it) and faces the
// player once it stands in its place; nothing new crosses the wire, because a resident's position and what it is
// doing already replicate. Their home is the upper levels, one NavMesh island no path joins to the main room's
// floor (docs/AI/systems/Workshop.md), so a resident that cannot walk there is moved, and moved back, while no player
// is looking -- or anyway once it has waited long enough.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Core;

namespace SpaceGame.World
{
    [Serializable]
    public sealed class WorkshopGathering
    {
        /// <summary>A scripted override lasts until it is let go; this is "for ever" in its seconds.</summary>
        private const float UntilLetGo = 1e7f;

        [Tooltip("Metres between the residents standing in a row behind the Shellback who raised the alarm.")]
        [SerializeField, Min(0.5f)] private float spacing = 1.6f;

        [Tooltip("How far behind the meeting point the residents' row stands, in metres.")]
        [SerializeField, Min(0f)] private float rowBack = 2.5f;

        [Tooltip("How close to its place in the row a resident must be to count as there, in metres.")]
        [SerializeField, Min(0.5f)] private float arriveDistance = 1.8f;

        [Tooltip("Seconds a resident keeps facing the player it is meeting, renewed while it stands there.")]
        [SerializeField, Min(0.5f)] private float focusSeconds = 3f;

        [Tooltip("How far away a player can be and still count as seeing a resident that is moved where no path leads, m.")]
        [SerializeField, Min(1f)] private float watchRadius = 60f;

        [Tooltip("Seconds a resident that cannot walk where it must go waits for nobody to look before it is moved anyway.")]
        [SerializeField, Min(0f)] private float movePatience = 20f;

        [Tooltip("How far from a target point a NavMesh point may be found to move a resident onto, in metres.")]
        [SerializeField, Min(0.5f)] private float moveSampleDistance = 3f;

        private readonly List<Resident> held = new();
        private readonly Dictionary<Resident, float> stuckSince = new();
        private readonly Dictionary<Resident, float> returningSince = new();

        public bool IsHolding => held.Count > 0;

        /// <summary>
        /// Server, every think: every resident of <paramref name="settlement"/> to its place in a row behind
        /// <paramref name="meeting"/>, facing the way it faces, and turned to <paramref name="greet"/> once there.
        /// </summary>
        public void Hold(Settlement settlement, Pose meeting, Transform greet)
        {
            SettlementSociety society = settlement != null ? settlement.Society : null;
            if (society == null) return;

            var present = new List<Resident>();
            foreach (Resident resident in society.Residents)
                if (resident != null && !resident.IsDead && !resident.IsAway) present.Add(resident);

            var rowCentre = new Pose(meeting.position - meeting.forward * rowBack, meeting.rotation);
            Pose[] row = ExpeditionRules.MusterRow(rowCentre, present.Count, spacing);
            for (int i = 0; i < present.Count; i++) HoldOne(present[i], row[i], greet);
        }

        private void HoldOne(Resident resident, Pose place, Transform greet)
        {
            if (!held.Contains(resident))
            {
                held.Add(resident);
                returningSince.Remove(resident);
                resident.ComeOnstage();
                if (resident.TryGetComponent(out ResidentRoutine routine)) routine.StandUp();
            }

            Vector3 at = resident.transform.position;
            if (!NavMeshReach.CanWalk(at, place.position) && MayMove(resident, at, place.position, stuckSince))
                MoveTo(resident, place);

            resident.SetOverride(OverrideKind.Scripted, place.position, UntilLetGo);

            bool there = (resident.transform.position - place.position).sqrMagnitude <= arriveDistance * arriveDistance;
            if (there && greet != null && resident.Focus != null) resident.Focus.FocusOn(greet, focusSeconds);
        }

        /// <summary>Server: everyone held goes back to their day; <see cref="Return"/> then moves those who cannot walk home.</summary>
        public void Dismiss()
        {
            foreach (Resident resident in held)
            {
                if (resident == null) continue;
                resident.SetOverride(OverrideKind.None, resident.transform.position, 0f);
                if (resident.Focus != null) resident.Focus.ClearFocus();
                returningSince[resident] = Time.time;
            }
            held.Clear();
            stuckSince.Clear();
        }

        /// <summary>Server, every think: residents dismissed onto the main room's floor are moved back to <paramref name="home"/>.</summary>
        public void Return(Vector3 home)
        {
            if (returningSince.Count == 0) return;

            var done = new List<Resident>();
            foreach (KeyValuePair<Resident, float> entry in returningSince)
            {
                Resident resident = entry.Key;
                if (resident == null)
                {
                    done.Add(resident);
                    continue;
                }

                Vector3 at = resident.transform.position;
                if (NavMeshReach.CanWalk(at, home))
                {
                    done.Add(resident);
                    continue;
                }

                if (!Unwatched(at, home) && Time.time - entry.Value < movePatience) continue;
                MoveTo(resident, new Pose(home, resident.transform.rotation));
                done.Add(resident);
            }
            foreach (Resident resident in done) returningSince.Remove(resident);
        }

        private bool MayMove(Resident resident, Vector3 from, Vector3 to, Dictionary<Resident, float> since)
        {
            if (!since.TryGetValue(resident, out float waiting)) since[resident] = waiting = Time.time;
            return Unwatched(from, to) || Time.time - waiting >= movePatience;
        }

        private bool Unwatched(Vector3 from, Vector3 to)
        {
            Vector3 chest = Vector3.up * ObserverCheck.ChestHeight;
            return !ObserverCheck.AnyPlayerSees(from + chest, watchRadius) && !ObserverCheck.AnyPlayerSees(to + chest, watchRadius);
        }

        private void MoveTo(Resident resident, Pose place)
        {
            if (!NavMesh.SamplePosition(place.position, out NavMeshHit hit, moveSampleDistance, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[WorkshopMission] no NavMesh within {moveSampleDistance} m of {place.position} to move " +
                                 $"'{resident.name}' onto.", resident);
                return;
            }

            if (resident.TryGetComponent(out ResidentRoutine routine)) routine.StandUp();
            NetworkedTeleport.Move(resident.gameObject, hit.position, place.rotation);
            stuckSince.Remove(resident);
        }
    }
}
