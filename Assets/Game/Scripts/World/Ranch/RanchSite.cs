using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Names one ranch for everything outside its chunk: the quest steps that talk about it and the wrangler who works it
    /// both point at this asset, and <see cref="Ranch.Of"/> finds the loaded ranch by it. Also where the ranch is, so a
    /// step can still point the way while the ranch's chunk is not loaded on this machine.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/World/Ranch Site")]
    public sealed class RanchSite : ScriptableObject
    {
        [Tooltip("What the quest's lines call the wrangler, e.g. in \"Keep up with {0}\".")]
        [SerializeField] private string wranglerName = "the wrangler";

        [Tooltip("The yard by the corral, world position. Recorded when the ranch is placed.")]
        [SerializeField] private Vector3 yard;

        [Tooltip("Where the wild herd grazes, world position. Recorded when the ranch is placed.")]
        [SerializeField] private Vector3 range;

        public string WranglerName => wranglerName;
        public Vector3 Yard => yard;
        public Vector3 Range => range;

#if UNITY_EDITOR
        /// <summary>Editor: where the placed ranch's yard and range are.</summary>
        public void Record(Vector3 yardPosition, Vector3 rangePosition)
        {
            yard = yardPosition;
            range = rangePosition;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
