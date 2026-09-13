using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// Materials on <c>SpaceGame/PackDragTint</c>: the one shader that draws a flat tinted body
    /// and/or an inflated outline round a mesh. The pack's hover rim and refusal flash, and the body
    /// screen's ghosts and previews, are all built here so they are one visual language.
    ///
    /// <para>
    /// The shader's two passes carry explicit <c>LightMode</c> tags — URP silently skips a
    /// multi-pass shader whose passes have none, which is why the pack's whole overlay once
    /// rendered nothing. Do not add a pass.
    /// </para>
    /// </summary>
    public static class TintMaterials
    {
        public const string ShaderName = "SpaceGame/PackDragTint";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int BodyOnId = Shader.PropertyToID("_BodyOn");
        private static readonly int OutlineOnId = Shader.PropertyToID("_OutlineOn");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>
        /// Outline only, depth-tested normally, drawn on a shell that traces the real item so the
        /// ITEM lights up — no floating UI box. <paramref name="width"/> is a placeholder: every
        /// shell build sets a real one from the item's own size.
        /// </summary>
        public static Material Rim(string name, Color colour, float width)
        {
            Material material = New(name);
            material.SetFloat(BodyOnId, 0f);
            material.SetFloat(OutlineOnId, 1f);
            material.SetColor(OutlineColorId, colour);
            material.SetFloat(OutlineWidthId, width);
            Blend(material, queue: 2001);
            return material;
        }

        /// <summary>
        /// A see-through body with an outline: what a ghost is made of. Alpha-blended, no depth
        /// write, in the transparent queue so the world behind it still shows.
        /// </summary>
        public static Material Translucent(string name, Color body, Color outline, float width)
        {
            Material material = New(name);
            material.SetFloat(BodyOnId, 1f);
            material.SetFloat(OutlineOnId, 1f);
            material.SetColor(ColorId, body);
            material.SetColor(OutlineColorId, outline);
            material.SetFloat(OutlineWidthId, width);
            Blend(material, queue: 3000);
            return material;
        }

        public static void SetBody(Material material, Color body) => material.SetColor(ColorId, body);

        public static void SetOutline(Material material, Color outline) => material.SetColor(OutlineColorId, outline);

        public static void SetOutlineWidth(Material material, float width) => material.SetFloat(OutlineWidthId, width);

        private static Material New(string name)
        {
            Shader shader = Shader.Find(ShaderName);

            // Same fallback shape HelmetDangerVignette uses, so a missing project shader keeps
            // the session alive rather than null-reffing it. It is a keep-running fallback, not a
            // visual one: URP/Unlit knows nothing of the outline pass, so a rim renders as plain
            // colour instead of a rim.
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            return new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        private static void Blend(Material material, int queue)
        {
            material.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.renderQueue = queue;
        }
    }
}
