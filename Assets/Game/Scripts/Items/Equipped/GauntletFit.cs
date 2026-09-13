using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How a forearm gauntlet is strapped on. Present on a gauntlet prefab whose model is built on
    /// the shared gauntlet base — origin at the wrist joint, the arm running down its own -Z, the
    /// back of the arm along +Y — and read by <c>BodyEquipmentController</c>, which then seats it
    /// on the FOREARM bone rather than in the hand's grip frame.
    ///
    /// <para>
    /// The hand frame is the wrong seat for a gauntlet twice over: it aligns the item with the
    /// fingers rather than the forearm, and its size ladder is per item, so four cuffs from one
    /// component once came out 7-10 cm off the arm's axis at four scales. Here the model is
    /// aligned to the elbow-to-wrist line and worn at the scale it was authored at. Since
    /// 2026-09-02 every gauntlet is built on <c>components/props/gauntlet_base.blend</c>, which is
    /// modelled at TRUE suit scale against the skinned forearm (0.40 m long, 0.17-0.22 m from the
    /// bone), so the scales below are 1 and the whole family shares them.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GauntletFit : MonoBehaviour
    {
        /// <summary>What the family wears when a prefab carries the component at its defaults.</summary>
        public const float DefaultCuffScale = 1f;
        public const float DefaultLengthScale = 1f;
        public const float DefaultWristGap = 0.02f;

        [Tooltip("Scale of the model ACROSS the forearm. The gauntlet base is authored at the suit's " +
                 "true size, so 1. Keep every gauntlet on the same number: the base is one shared " +
                 "component, and a gauntlet that needs another scale is a gauntlet built off it.")]
        [SerializeField, Min(0.1f)] private float cuffScale = DefaultCuffScale;

        [Tooltip("Scale of the model ALONG the forearm. Separate from the width so a model that was " +
                 "authored long can be shortened without squashing its device across; the base " +
                 "needs neither, so 1.")]
        [SerializeField, Min(0.1f)] private float lengthScale = DefaultLengthScale;

        [Tooltip("Metres from the wrist joint toward the elbow at which the model's origin — the " +
                 "wrist joint in the model's frame — sits. A little gap keeps the collar off the glove.")]
        [SerializeField] private float wristGap = DefaultWristGap;

        [Tooltip("Roll about the forearm, in degrees, on top of 'device on the back of the arm'. " +
                 "For a model whose dorsal face is not exactly its +Y.")]
        [SerializeField] private float rollDegrees;

        public float CuffScale => cuffScale;
        public float LengthScale => lengthScale;
        public float WristGap => wristGap;
        public float RollDegrees => rollDegrees;
    }
}
