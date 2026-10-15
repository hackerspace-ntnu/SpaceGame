using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A place a quest step can name: the witch's hut, the hamlet gate. Steps refer to it by
    /// <see cref="SiteId"/>.
    ///
    /// <para>
    /// <b>Two ways to be found.</b> A loaded site registers itself and answers with where it stands
    /// now. A site whose chunk is not loaded — the hut, while the crew stand in the hamlet — answers
    /// from the <see cref="QuestSiteTable"/> baked out of the world scenes
    /// (<c>Tools ▸ SpaceGame ▸ Objectives ▸ Bake Quest Sites</c>), which is what lets a waypoint point
    /// at somewhere nobody has streamed in. <c>WorldSiteRegistry</c> cannot do that: it only knows
    /// loaded chunks.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestSite : MonoBehaviour
    {
        private static readonly Dictionary<string, QuestSite> Live = new();
        private static QuestSiteTable table;
        private static bool tableLoaded;

        /// <summary>Resources path of the baked table.</summary>
        public const string TablePath = "Objectives/QuestSites";

        [Tooltip("Named in quest step assets. Unique, readable, never renamed once a step names it.")]
        [SerializeField] private string siteId;

        [Tooltip("How close counts as being here, metres.")]
        [SerializeField, Min(1f)] private float radius = 15f;

        public string SiteId => siteId;
        public float Radius => radius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Live.Clear();
            table = null;
            tableLoaded = false;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(siteId))
            {
                Debug.LogError($"[Quest] {name} has a QuestSite with no id, so no quest step can name it.", this);
                return;
            }

            if (Live.TryGetValue(siteId, out QuestSite other) && other != null && other != this)
                Debug.LogError($"[Quest] Two places share the quest site id '{siteId}': {other.name} and {name}.", this);

            Live[siteId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(siteId) && Live.TryGetValue(siteId, out QuestSite registered) && registered == this)
                Live.Remove(siteId);
        }

        /// <summary>
        /// Where the site with this id is, loaded or not. False when no scene holds it — the site
        /// prefab has not been placed in the world yet, or the table has not been baked since.
        /// </summary>
        public static bool TryGet(string id, out Vector3 position, out float radius)
        {
            position = default;
            radius = 0f;
            if (string.IsNullOrEmpty(id)) return false;

            if (Live.TryGetValue(id, out QuestSite live) && live != null)
            {
                position = live.transform.position;
                radius = live.radius;
                return true;
            }

            if (!tableLoaded)
            {
                table = Resources.Load<QuestSiteTable>(TablePath);
                tableLoaded = true;
            }

            return table != null && table.TryGet(id, out position, out radius);
        }
    }
}
