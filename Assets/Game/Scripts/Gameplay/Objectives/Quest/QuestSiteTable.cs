using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>Every <see cref="QuestSite"/> in the world scenes, baked at edit time. Written by the baker; not edited by hand.</summary>
    public class QuestSiteTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string siteId;
            public Vector3 position;
            public float radius;
        }

        [SerializeField] private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGet(string id, out Vector3 position, out float radius)
        {
            foreach (Entry entry in entries)
            {
                if (entry.siteId != id) continue;

                position = entry.position;
                radius = entry.radius;
                return true;
            }

            position = default;
            radius = 0f;
            return false;
        }

        /// <summary>Editor: replace the whole table.</summary>
        public void Set(List<Entry> baked) => entries = baked;
    }
}
