using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The ordered steps a crew works through. One chain per world; the crew share their place in it.
    ///
    /// <para>
    /// Steps are saved by <see cref="ObjectiveStep.Id"/>, never by position, so steps may be
    /// inserted, removed and reordered here without an existing save landing on the wrong one.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Chain")]
    public class ObjectiveChain : ScriptableObject
    {
        [SerializeField] private ObjectiveStep[] steps = Array.Empty<ObjectiveStep>();

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
