using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// Traces a visual with throwaway renderers carrying one outline material, so the whole
    /// silhouette lights up.
    ///
    /// <para>
    /// This used to <em>append</em> the outline material to the item's own renderers, which is
    /// half a technique: Unity draws a renderer's Nth material against submesh N and against the
    /// LAST submesh once it runs out, so an appended material outlines one submesh and no others.
    /// On this roster that is not an edge case — the item scanner's case has 10 submeshes, the
    /// portal gun 12, the leash spool and the weather-station emitter 8 each — so the rim traced
    /// one arbitrary fragment of the prop and the player saw an outline that did not match the
    /// item.
    /// </para>
    /// <para>
    /// A shell has no such limit: it is a renderer of its own, so it gets one outline material per
    /// submesh and covers the whole silhouette. It is also safer than borrowing. Each part is
    /// parented to the renderer it traces, inheriting that renderer's exact object-to-world matrix
    /// and dying with it, so a display copy destroyed mid-hover — which <c>BackpackObject</c> does
    /// on every layout change — cannot leave a caller holding a material array it can no longer
    /// put back.
    /// </para>
    /// </summary>
    public static class OutlineShell
    {
        /// <summary>Marks the shell objects, so a shell is never built round a shell.</summary>
        public const string ShellName = "PackOutlineShell";

        /// <summary>
        /// Outline width as a fraction of the traced visual's own longest side, and the metres it
        /// is allowed to land between.
        ///
        /// <para>
        /// The width is world metres (see <c>PackDragTint.shader</c>), so it has to be chosen per
        /// visual or it is either a hairline on a 1.35 m staff or a 5% border round a 0.16 m
        /// leash. A fraction with a floor and a ceiling gives a line that reads the same on both
        /// and can never swamp the silhouette it is drawn around, which is the failure this
        /// replaces: the old object-space width came out at 1.2 m on the item scanner.
        /// </para>
        /// </summary>
        private const float OutlineFraction = 0.020f;
        public static readonly float MinOutlineWidth = PackScale.Apply(0.0015f);
        public static readonly float MaxOutlineWidth = PackScale.Apply(0.010f);

        /// <summary>
        /// Trace <paramref name="visual"/> with a set of throwaway renderers carrying only
        /// <paramref name="outline"/>, replacing whatever <paramref name="parts"/> traced before.
        /// <paramref name="weight"/> scales the width the visual's own size earns it, so two rims
        /// on one object can read as the thicker and the finer without either being hand-typed.
        /// </summary>
        public static void Build(GameObject visual, Material outline, float weight, List<GameObject> parts)
        {
            Clear(parts);
            if (visual == null) return;

            TintMaterials.SetOutlineWidth(outline, WidthFor(visual, weight));

            foreach (Renderer source in visual.GetComponentsInChildren<Renderer>(true))
            {
                // Never shell our own shells: Unity's Destroy is deferred, so the parts cleared a
                // moment ago are still hanging on these renderers for the rest of the frame.
                if (source == null || source.gameObject.name == ShellName) continue;

                Mesh mesh = MeshOf(source);
                if (mesh == null || mesh.subMeshCount <= 0) continue;

                var part = new GameObject(ShellName) { hideFlags = HideFlags.HideAndDontSave };
                part.transform.SetParent(source.transform, false);
                part.layer = source.gameObject.layer;

                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = outline;

                Renderer shell;

                if (source is SkinnedMeshRenderer skinned)
                {
                    // A skinned mesh's vertices mean nothing without its bones, so the shell has
                    // to be skinned too and share them. Sharing rather than copying is also what
                    // makes the shell follow whatever pose the source is in, animated or not.
                    var copy = part.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = mesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.localBounds = skinned.localBounds;
                    shell = copy;
                }
                else
                {
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    shell = part.AddComponent<MeshRenderer>();
                }

                shell.sharedMaterials = materials;
                shell.shadowCastingMode = ShadowCastingMode.Off;
                shell.receiveShadows = false;

                parts.Add(part);
            }
        }

        /// <summary>Destroy every part <see cref="Build"/> put in <paramref name="parts"/> and
        /// empty the list.</summary>
        public static void Clear(List<GameObject> parts)
        {
            foreach (GameObject part in parts)
                if (part != null) Object.Destroy(part);

            parts.Clear();
        }

        /// <summary>
        /// How thick a line to draw round this particular visual, in world metres.
        ///
        /// <para>
        /// A fraction of the visual's own longest side, floored and capped. The shader inflates in
        /// world space now, so one constant cannot serve a 0.16 m leash and a 1.35 m staff at
        /// once — and the bug this replaces was exactly a constant that meant something different
        /// on every prop.
        /// </para>
        /// </summary>
        public static float WidthFor(GameObject visual, float weight)
        {
            float span = 0f;
            bool any = false;
            Bounds bounds = default;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.gameObject.name == ShellName) continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (any)
            {
                Vector3 size = bounds.size;
                span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }

            return Mathf.Clamp(span * OutlineFraction * weight, MinOutlineWidth, MaxOutlineWidth);
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
