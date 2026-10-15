using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The ordered steps a crew works through. The crew share their place in it. A world has one main
    /// chain, under way from the landing, and any number of side chains, each started when somebody
    /// accepts it (see <see cref="ObjectiveDirector"/>).
    ///
    /// <para>
    /// Steps are saved by <see cref="ObjectiveStep.Id"/>, never by position, so steps may be
    /// inserted, removed and reordered here without an existing save landing on the wrong one. The
    /// chain itself is saved by <see cref="Id"/> for the same reason.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Chain")]
    public class ObjectiveChain : ScriptableObject
    {
        [Tooltip("Written into save files as the name of this chain. NEVER rename it once a save " +
                 "exists — a record naming a chain the world no longer has is dropped.")]
        [SerializeField] private string id;

        [Tooltip("Who speaks this chain's briefings. Shown in bold before every line.")]
        [SerializeField] private string speaker;

        [SerializeField] private ObjectiveStep[] steps = Array.Empty<ObjectiveStep>();

        public string Id => id;
        public string Speaker => speaker;
        public IReadOnlyList<ObjectiveStep> Steps => steps;

        /// <summary>Position of the step with this id, or -1.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;

            for (int i = 0; i < steps.Length; i++)
                if (steps[i] != null && steps[i].Id == id) return i;

            return -1;
        }
    }
}
