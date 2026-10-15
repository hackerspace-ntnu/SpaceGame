using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How an electrocuted body looks: the materials, the arc and every number of the effect.
    ///
    /// <para>
    /// An asset rather than fields on <see cref="ShockedVisual"/> because that component is added to
    /// bodies at runtime and so has no Inspector anybody could wire — the reason
    /// <c>BurningVisual</c> loads its fire through <c>Resources</c>. One asset, loaded from
    /// <see cref="ResourcePath"/>, is the one place to tune it. Built by ShockHarpoonBuilder.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Effects/Shock Look", fileName = "ShockLook")]
    public sealed class ShockLook : ScriptableObject
    {
        /// <summary>Where <see cref="Load"/> finds the asset, under a Resources folder.</summary>
        public const string ResourcePath = "Effects/ShockLook";

        [Header("X-ray")]
        [Tooltip("Drawn on a ghost of every body renderer while the X-ray is showing (SpaceGame/XRayShell).")]
        public Material xrayShell;
        [Tooltip("The skeleton's bones and skull.")]
        public Material bone;
        [Tooltip("The skull's eye sockets.")]
        public Material socket;

        [Tooltip("Seconds the X-ray shows each time it flashes on, random between x and y.")]
        public Vector2 xraySeconds = new Vector2(0.22f, 0.3f);
        [Tooltip("Seconds the body shows normally between X-ray flashes, random between x and y. " +
                 "With xraySeconds this keeps the strobe under three flashes a second, the usual " +
                 "photosensitivity limit: the game has no reduce-flashing setting, so the effect " +
                 "must be safe as shipped.")]
        public Vector2 normalSeconds = new Vector2(0.12f, 0.16f);

        [Header("Skeleton, as fractions of the body's height")]
        [Tooltip("Thickness of a limb bone.")]
        public float boneThickness = 0.022f;
        [Tooltip("Bones shorter than this are not drawn: fingers, toes, twist helpers.")]
        public float minBoneLength = 0.04f;
        [Tooltip("Diameter of the skull.")]
        public float skullSize = 0.1f;
        [Tooltip("Width of the rib cage.")]
        public float ribWidth = 0.085f;

        [Header("Current")]
        [Tooltip("One short arc. Spawned locally on every machine, never networked.")]
        public LightningBoltEffect arc;
        [Tooltip("Seconds between arcs jumping along the body's bones.")]
        public float arcInterval = 0.05f;
        [Tooltip("Chance that an arc jumps across the body (hand to foot, head to hip) instead of along one bone.")]
        [Range(0f, 1f)] public float longArcChance = 0.3f;

        [Header("Light")]
        public Color lightColor = new Color(0.45f, 0.75f, 1f);
        [Tooltip("Peak intensity. The light wavers with noise rather than strobing.")]
        public float lightIntensity = 6f;
        [Tooltip("Range, as a multiple of the body's height.")]
        public float lightRange = 2.5f;
        [Tooltip("How fast the light wavers, in cycles per second.")]
        public float lightWaver = 9f;

        private static ShockLook loaded;
        private static bool resolved;

        /// <summary>The shipped look, or null (with one error) if the asset is missing.</summary>
        public static ShockLook Load()
        {
            if (resolved) return loaded;
            resolved = true;

            loaded = Resources.Load<ShockLook>(ResourcePath);
            if (loaded == null)
                Debug.LogError($"[ShockLook] No asset at Resources/{ResourcePath}. Run Tools/SpaceGame/Items/Build Shock Harpoon.");
            return loaded;
        }
    }
}
