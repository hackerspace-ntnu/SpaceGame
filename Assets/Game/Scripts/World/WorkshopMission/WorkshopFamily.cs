// The Shellbacks shut inside the workshop, for a workshop that has no residents of its own (the bare Workshop.prefab
// placed in a chunk, as in Chunk_4_3). Server only. They appear in the main hall the moment the gate opens, out of
// sight of whoever opened it, stand in a row behind the meeting point, and once the mission is given each goes to its
// own spot round the hall. Saved by nobody: the mission rebuilds them from its phase, like the Shellback at the gate.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Core;

namespace SpaceGame.World
{
    [Serializable]
    public sealed class WorkshopFamily
    {
        [Tooltip("Who was shut inside: one Shellback per entry, each a WorkshopFamilyMember.")]
        [SerializeField] private GameObject[] members = Array.Empty<GameObject>();

        [Tooltip("Metres between them in the row behind the meeting point.")]
        [SerializeField, Min(0.5f)] private float spacing = 1.8f;

        [Tooltip("How far behind the meeting point the row stands, in metres.")]
        [SerializeField, Min(0f)] private float rowBack = 2.5f;

        [Tooltip("How far from the meeting point their own spots round the hall are, in metres. The pit stairs start at 14.6.")]
        [SerializeField, Min(1f)] private float spotRadius = 9f;

        [Tooltip("Bearing of the first spot round the hall from the way in, degrees; the rest spread to the last.")]
        [SerializeField, Range(0f, 360f)] private float firstSpotBearing = 70f;

        [Tooltip("Bearing of the last spot round the hall from the way in, degrees.")]
        [SerializeField, Range(0f, 360f)] private float lastSpotBearing = 290f;

        [Tooltip("How far from a spot the NavMesh may be found to put one of them on, in metres.")]
        [SerializeField, Min(0.5f)] private float sampleDistance = 4f;

        private readonly List<WorkshopFamilyMember> present = new();

        public bool IsPresent => present.Count > 0;

        /// <summary>Server: the family appears at its places for this part of the story, if it is not here already.</summary>
        public void Appear(Pose meeting, bool inRow, UnityEngine.Object context)
        {
            if (present.Count > 0) return;

            Pose[] places = inRow ? Row(meeting) : Spots(meeting);
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                if (!NavMesh.SamplePosition(places[i].position, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas)) continue;

                GameObject spawned = NpcSpawn.Create(members[i], hit.position, places[i].rotation, context);
                if (spawned == null) continue;
                if (spawned.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);
                if (spawned.TryGetComponent(out WorkshopFamilyMember member)) present.Add(member);
                else Debug.LogError($"[WorkshopMission] '{members[i].name}' has no WorkshopFamilyMember.", members[i]);
            }
        }

        /// <summary>Server, every think: each to its place in the row, or its own spot round the hall, turned to <paramref name="greet"/>.</summary>
        public void Hold(Pose meeting, bool inRow, Transform greet)
        {
            Pose[] places = inRow ? Row(meeting) : Spots(meeting);
            for (int i = 0; i < present.Count && i < places.Length; i++)
                if (present[i] != null) present[i].Stand(places[i], greet);
        }

        /// <summary>Server: they go with the workshop when its chunk streams out.</summary>
        public void Leave()
        {
            foreach (WorkshopFamilyMember member in present)
                if (member != null) GameServices.World.Despawn(member.gameObject);
            present.Clear();
        }

        private Pose[] Row(Pose meeting) =>
            ExpeditionRules.MusterRow(new Pose(meeting.position - meeting.forward * rowBack, meeting.rotation), members.Length, spacing);

        /// <summary>Spread on a circle round the meeting point, away from the way in, each facing the middle.</summary>
        private Pose[] Spots(Pose meeting)
        {
            var spots = new Pose[members.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                float t = spots.Length == 1 ? 0.5f : i / (float)(spots.Length - 1);
                float bearing = Mathf.Lerp(firstSpotBearing, lastSpotBearing, t);
                Vector3 outward = Quaternion.AngleAxis(bearing, Vector3.up) * meeting.forward;
                Vector3 at = meeting.position + outward * spotRadius;
                spots[i] = new Pose(at, Quaternion.LookRotation(-outward, Vector3.up));
            }
            return spots;
        }
    }
}
