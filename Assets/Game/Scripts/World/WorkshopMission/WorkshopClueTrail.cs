// The raiders' trail from the workshop's gate to their camp: a few things they dropped on the way (bits of the Heart
// Gear's housing), each found with the item scanner and examined to learn which way they went.
//
// Every machine builds the same trail on its own: the clues stand at points worked out from the gate and the camp and
// sampled on the world NavMesh, which every machine holds alike, so nothing but "how many have been examined" crosses
// the wire or goes in the save. The clues are local scenery (no NetworkObject); examining one asks the server through
// the mission, which checks the asker is standing at it.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    [Serializable]
    public sealed class WorkshopClueTrail
    {
        [Tooltip("What a clue looks like: a model only, no networking (a small gear from the gear kit).")]
        [SerializeField] private GameObject clueModel;

        [Tooltip("How big the clue model is drawn.")]
        [SerializeField, Min(0.01f)] private float clueScale = 0.6f;

        [Tooltip("What examining each clue tells the player, in trail order. One clue per line.")]
        [SerializeField, TextArea(2, 4)] private string[] findings =
        {
            "A cog from the Heart Gear's housing, ground into the sand. Drag marks lead off into the dunes.",
            "Boot prints - three of them, one dragging a foot - and a dropped bolt. They're not even hiding their tracks.",
            "A broken cart wheel. They're carrying the gear by hand now; the prints sink deep here.",
            "A rag soaked in gear grease, flung aside. Still sticky: they passed not long ago.",
            "A water flask, half full, and the smell of smoke on the wind. Their camp is just ahead.",
        };

        [Tooltip("How far along the way from the gate to the camp each clue lies, 0 to 1, in trail order.")]
        [SerializeField] private float[] alongTrail = { 0.06f, 0.25f, 0.45f, 0.65f, 0.85f };

        [Tooltip("How far to either side of the straight way each clue may lie, in metres.")]
        [SerializeField, Min(0f)] private float wander = 25f;

        [Tooltip("How far from a clue the visor's search area may be centred, in metres: the scanner finds the rest.")]
        [SerializeField, Min(0f)] private float searchRadius = 15f;

        [Tooltip("How close a player must stand to a clue to examine it, in metres. Checked again on the server.")]
        [SerializeField, Min(0.5f)] private float examineRange = 4f;

        [Tooltip("How far from its point the NavMesh may be found to lay a clue on, in metres.")]
        [SerializeField, Min(0.5f)] private float sampleDistance = 6f;

        // The golden angle: successive clues swing to either side, and their search areas lie round them, without any
        // two lining up.
        private const float GoldenAngle = 137.508f;
        // The visor's search area is centred this share of searchRadius off the clue.
        private const float SearchOffset = 0.6f;

        private readonly List<WorkshopClue> shown = new();

        public int Count => Mathf.Min(findings.Length, alongTrail.Length);
        public float ExamineRange => examineRange;

        /// <summary>Where clue <paramref name="index"/> lies on the way from <paramref name="start"/> to <paramref name="camp"/>, the same on every machine.</summary>
        public Vector3 Position(int index, Vector3 start, Vector3 camp)
        {
            Vector3 along = camp - start;
            Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
            // Straight out of the gate (the workshop's mountain stands to either side of it), wandering more the further out.
            float outward = Count > 1 ? index / (float)(Count - 1) : 0f;
            float sway = Mathf.Sin((index + 1) * GoldenAngle * Mathf.Deg2Rad) * wander * outward;
            Vector3 point = start + along * alongTrail[index] + side * sway;
            return NavMesh.SamplePosition(point, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas) ? hit.position : point;
        }

        /// <summary>The middle of the area the visor marks for clue <paramref name="index"/>: near it, not on it.</summary>
        public Vector3 SearchCentre(int index, Vector3 start, Vector3 camp)
        {
            Vector3 clue = Position(index, start, camp);
            float bearing = (index + 1) * GoldenAngle;
            return clue + Quaternion.AngleAxis(bearing, Vector3.up) * Vector3.forward * (searchRadius * SearchOffset);
        }

        public string Finding(int index) => index >= 0 && index < findings.Length ? findings[index] : string.Empty;

        /// <summary>Every machine: lay the trail, if it is not laid, with the clues already examined marked as such.</summary>
        public void Show(WorkshopMission mission, Vector3 start, Vector3 camp, int examined)
        {
            if (shown.Count == 0)
            {
                for (int i = 0; i < Count; i++)
                {
                    var clue = new GameObject($"WorkshopClue_{i}").AddComponent<WorkshopClue>();
                    clue.transform.position = Position(i, start, camp);
                    clue.transform.rotation = Quaternion.Euler(90f, i * GoldenAngle, 0f);   // lying flat in the sand
                    clue.Bind(mission, i, clueModel, clueScale);
                    shown.Add(clue);
                }
            }

            for (int i = 0; i < shown.Count; i++)
                if (shown[i] != null) shown[i].Examined = i < examined;
        }

        /// <summary>Every machine: take the trail away (the story moved on, or the workshop streamed out).</summary>
        public void Hide()
        {
            foreach (WorkshopClue clue in shown)
                if (clue != null) UnityEngine.Object.Destroy(clue.gameObject);
            shown.Clear();
        }
    }
}
