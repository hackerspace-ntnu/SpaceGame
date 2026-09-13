// How a storm is drawn, and the only place this artifact talks to a shader.
//
// TWO NUMBERS, TWO MATERIALS, TWO OBJECTS. StormCloud.shader draws the body and StormVeil.shader
// draws the rain column; they are separate volumes with separate bounds, and separate objects so
// the transform can give them different scales. Both are told the same two values:
//
//     _Form   0..1  how much of the storm exists. It multiplies the alpha of BOTH halves, so one
//                   value gathers and disperses the whole storm rather than leaving rain hanging
//                   under nothing.
//     _Flash  0..1  one pulse per bolt, decayed here.
//
// AND ONE VECTOR THE CLOUD HALF ALONE READS. `_BoltPoint` is where the last bolt earthed, in
// WORLD space, so StormCloud.shader can light the volume from a point inside itself instead of
// brightening the whole disc at once. Painted through the same block as the two floats, and
// painted onto the veil as well: a property a shader does not declare is ignored, and one code
// path that paints everything is cheaper to keep true than two that each know their own half.
//
// THROUGH A PROPERTY BLOCK, NEVER THROUGH THE MATERIAL. Each material is shared by every storm in
// the world: renderer.material would clone it per object, and renderer.sharedMaterial would put one
// cloud's flash on every cloud on the map.
//
// IT DECIDES NOTHING. The form comes from the cloud's replicated clock and the flash from a
// replicated strike, so two machines watching one storm are drawing the same two numbers rather than
// each running their own weather.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Drives <c>_Form</c> and <c>_Flash</c> on a storm cloud's renderers. Presentation only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StormCloudLook : MonoBehaviour
    {
        [Tooltip("The cloud body and the rain veil. Both are painted, because both shaders read " +
                 "_Form and _Flash. Every renderer under this object is used when left empty, " +
                 "which is the right answer however the model is split.")]
        [SerializeField] private Renderer[] surfaces;

        [Tooltip("How long one bolt's flash takes to fade, in seconds. Short: it is a strike, not " +
                 "a light being switched on, and the bolt itself is drawn by the Lightning VFX.")]
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.35f;

        [Tooltip("How bright a bolt lights the cloud from inside, 0 to 1. The shader lerps the " +
                 "cloud's bands towards its flash colour by this much at the peak.")]
        [SerializeField, Range(0f, 1f)] private float flashPeak = 1f;

        private static readonly int FormId = Shader.PropertyToID("_Form");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int BoltPointId = Shader.PropertyToID("_BoltPoint");

        private MaterialPropertyBlock block;

        /// <summary>Seconds of flash still to burn off. Runs on every machine, off its own clock.</summary>
        private float flashLeft;

        private float drawnForm = -1f;
        private float drawnFlash = -1f;

        /// <summary>
        /// Resolve the renderers and put the storm at nothing.
        ///
        /// <para>
        /// In OnEnable rather than Awake, and painted rather than assumed: without the paint the
        /// cloud spends its first frame at whatever <c>_Form</c> the material was authored with,
        /// which is a fully formed storm appearing out of nothing before it has gathered.
        /// </para>
        /// </summary>
        private void OnEnable()
        {
            if (surfaces == null || surfaces.Length == 0)
                surfaces = GetComponentsInChildren<Renderer>(true);

            flashLeft = 0f;
            drawnForm = -1f;
            drawnFlash = -1f;

            SetForm(0f);
            Paint(FlashId, 0f, ref drawnFlash);
        }

        /// <summary>
        /// How much of the storm is there, 0 to 1. Told once a frame by the cloud, which derives it
        /// from the replicated clock every machine shares.
        /// </summary>
        public void SetForm(float form) => Paint(FormId, Mathf.Clamp01(form), ref drawnForm);

        /// <summary>
        /// A bolt has just left the cloud: light it from inside, from where it earthed.
        /// </summary>
        /// <param name="strikePoint">Where the bolt landed, in world space.</param>
        public void Flash(Vector3 strikePoint)
        {
            flashLeft = flashSeconds;
            PaintVector(BoltPointId, strikePoint);
        }

        private void Update()
        {
            if (flashLeft <= 0f) return;

            flashLeft = Mathf.Max(0f, flashLeft - Time.deltaTime);
            Paint(FlashId, flashPeak * (flashLeft / flashSeconds), ref drawnFlash);
        }

        /// <summary>
        /// Push one value into every surface.
        ///
        /// Skipped when the value has not moved: a property block write is not free, and most
        /// frames of a storm's life change neither of the two numbers.
        /// </summary>
        private void Paint(int propertyId, float value, ref float drawn)
        {
            if (surfaces == null || Mathf.Approximately(value, drawn)) return;

            drawn = value;
            block ??= new MaterialPropertyBlock();

            for (int i = 0; i < surfaces.Length; i++)
            {
                Renderer surface = surfaces[i];
                if (surface == null) continue;

                surface.GetPropertyBlock(block);
                block.SetFloat(propertyId, value);
                surface.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Push one vector into every surface. Unguarded, unlike <see cref="Paint"/>: it is
        /// written once per bolt rather than once per frame, so there is nothing to save.
        /// </summary>
        private void PaintVector(int propertyId, Vector4 value)
        {
            if (surfaces == null) return;

            block ??= new MaterialPropertyBlock();

            for (int i = 0; i < surfaces.Length; i++)
            {
                Renderer surface = surfaces[i];
                if (surface == null) continue;

                surface.GetPropertyBlock(block);
                block.SetVector(propertyId, value);
                surface.SetPropertyBlock(block);
            }
        }
    }
}
