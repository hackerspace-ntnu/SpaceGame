using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Paints one lamp, gauge or status light on a renderer — or on a single submesh of one —
    /// without touching the material asset it shares.
    ///
    /// <para>
    /// Always a <see cref="MaterialPropertyBlock"/>. Every model in this project comes out of the
    /// shared Blender palette, so a status lamp's material is the SAME asset on the repair station,
    /// on both power cells, on the oxygen bottle and on the display copy of that bottle standing in
    /// a dock. Writing the material would change all of them together, and in the editor it writes
    /// the change to the <c>.mat</c> on disk.
    /// </para>
    /// <para>
    /// Three properties, written together, because which one a shader honours depends on the
    /// shader: <c>_BaseColor</c> is URP Lit, <c>_Color</c> is the name Simple Lit and the legacy
    /// shaders answer to, and <c>_EmissionColor</c> is the one that makes a lamp read as a SOURCE
    /// rather than as a pale surface. Setting a property a shader does not declare is free.
    /// </para>
    /// <para>
    /// The block is static and reused. Every call re-reads the renderer's own block into it first,
    /// so there is no state to leak between call sites — and a lamp animated per frame (a bottle
    /// filling) must not allocate one a frame.
    /// </para>
    /// </summary>
    public static class EmissiveLamp
    {
        /// <summary>
        /// How much brighter than its own colour a lit lamp emits. Over 1 on purpose: at 1 a lamp
        /// is exactly as bright as the wall beside it and reads as paint.
        /// </summary>
        public const float EmissionGain = 2f;

        /// <summary>Pass as the material index to paint every submesh of the renderer.</summary>
        public const int WholeRenderer = -1;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static MaterialPropertyBlock shared;

        /// <summary>
        /// Paint <paramref name="materialIndex"/> of <paramref name="renderer"/>, or the whole
        /// renderer at <see cref="WholeRenderer"/>.
        ///
        /// <para>
        /// The index matters because these models are one mesh per PART, not one per material: the
        /// generator's amber lamp is submesh 3 of its control head and the bottle's gauge is
        /// submesh 0 of a five-material mesh, so painting the whole renderer would enamel the
        /// valve caps and the shell along with the lamp.
        /// </para>
        /// <para>
        /// An out-of-range index is left to Unity, which logs it. Range is not re-checked here
        /// because that costs a <c>sharedMaterials</c> array allocation on a path that runs every
        /// frame; the builders that wire an index verify it once, off the model, instead.
        /// </para>
        /// </summary>
        public static void Paint(Renderer renderer, int materialIndex, Color colour)
        {
            if (renderer == null) return;

            shared ??= new MaterialPropertyBlock();

            if (materialIndex < 0) renderer.GetPropertyBlock(shared);
            else renderer.GetPropertyBlock(shared, materialIndex);

            shared.SetColor(BaseColorId, colour);
            shared.SetColor(ColorId, colour);
            shared.SetColor(EmissionColorId, colour * EmissionGain);

            if (materialIndex < 0) renderer.SetPropertyBlock(shared);
            else renderer.SetPropertyBlock(shared, materialIndex);
        }

        /// <summary>Paint every submesh. The plain case: a lamp that is a mesh of its own.</summary>
        public static void Paint(Renderer renderer, Color colour) =>
            Paint(renderer, WholeRenderer, colour);
    }
}
