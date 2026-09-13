using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The two colours this game says yes and no in, and the unlit material that draws them.
    ///
    /// <para>
    /// Both placement systems answer the same question — "can this thing go here?" — and a player
    /// who has learnt the backpack's green should read the ship's green without being taught it
    /// again. Sharing the numbers is the only way that survives someone retuning one of them.
    /// </para>
    /// </summary>
    public static class PlacementTint
    {
        private const string ShaderName = "SpaceGame/PackDragTint";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BodyOnId = Shader.PropertyToID("_BodyOn");
        private static readonly int OutlineOnId = Shader.PropertyToID("_OutlineOn");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>"This placement is allowed, and this is where it would land."</summary>
        public static readonly Color Legal = new(0.38f, 0.92f, 0.45f, 0.5f);

        /// <summary>"This placement is refused." Also, on the hull, "something is missing here".</summary>
        public static readonly Color Refused = new(1f, 0.30f, 0.28f, 0.5f);

        /// <summary>
        /// A throwaway unlit tint material, body pass only, alpha-blended and depth-writing off.
        ///
        /// <para>
        /// Falls back to URP's own Unlit when the project shader is missing: the colour still
        /// reads, it just loses the draw-order control. The caller owns the material and must
        /// destroy it — it is <see cref="HideFlags.HideAndDontSave"/>, so nothing else will.
        /// </para>
        /// </summary>
        public static Material BuildMaterial(string name, Color colour, int renderQueue = 3000)
        {
            Shader shader = Shader.Find(ShaderName) ?? Shader.Find("Universal Render Pipeline/Unlit");

            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

            material.SetColor(ColorId, colour);
            material.SetFloat(BodyOnId, 1f);
            material.SetFloat(OutlineOnId, 0f);
            material.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.renderQueue = renderQueue;

            return material;
        }
    }
}
