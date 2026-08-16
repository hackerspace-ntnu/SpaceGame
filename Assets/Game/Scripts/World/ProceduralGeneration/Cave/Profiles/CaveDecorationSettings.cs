using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Decoration sub-config carried by a <see cref="CaveProfile"/>. Owns the rule list plus
    /// global scatter parameters (sampling density cap, normal threshold). Profiles can share the
    /// same rule list via the prebuilt-rule factory helpers in <see cref="PrebuiltDecorationRules"/>.
    /// </summary>
    [System.Serializable]
    public class CaveDecorationSettings
    {
        [Tooltip("Master toggle. Off = no decoration pass runs.")]
        public bool enabled = true;

        [Tooltip("|inward-normal.y| threshold separating floor/ceiling from walls. 0.7 ≈ 45°.")]
        [Range(0.3f, 0.95f)] public float floorCeilingThreshold = 0.7f;

        [Tooltip("Maximum total instances summed across all rules. Safety valve for runaway scatter on huge caves. 0 = no cap.")]
        public int globalInstanceCap = 4000;

        [Tooltip("Decoration rules. Each is evaluated independently; instances from different rules can overlap unless their minSpacing values cover one another.")]
        public DecorationRule[] rules = new DecorationRule[0];
    }
}
