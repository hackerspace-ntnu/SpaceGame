using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything focus mode draws that is not the pack itself: the hover rim, the dragged copy
    /// and its tint, the ghost left behind at the origin, the projected footprint, and the item's
    /// name by the cursor.
    ///
    /// <para>
    /// Split out of <c>PackDragController</c> so that file can be about <em>what the player is
    /// doing</em> and this one about <em>what that looks like</em>. They are genuinely separable:
    /// nothing here reads input, decides a placement or talks to the network, and nothing there
    /// touches a material.
    /// </para>
    /// <para>
    /// Every material is an instance created here and destroyed in <see cref="Dispose"/>. Unity
    /// does not collect materials with the objects that used them, and a focus session is opened
    /// hundreds of times.
    /// </para>
    /// </summary>
    public sealed class PackDragVisuals
    {
        private const string ShaderName = "SpaceGame/PackDragTint";

        /// <summary>Metres the dragged copy floats above the surface it is over.</summary>
        private const float DragLift = 0.06f;

        /// <summary>Metres the footprint quad sits above the surface, clear of z-fighting.</summary>
        private const float QuadLift = 0.004f;

        // Spec 5.2: a flat desaturated grey around 0.55 value with a thin brighter outline, so the
        // silhouette reads against the pack's canvas and against sand. Red on conflict.
        private static readonly Color DragBody = new(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color DragOutline = new(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color ConflictBody = new(0.62f, 0.16f, 0.14f, 1f);
        private static readonly Color ConflictOutline = new(1f, 0.42f, 0.36f, 1f);

        private static readonly Color HoverRim = new(1f, 0.92f, 0.6f, 1f);
        private static readonly Color GhostRim = new(0.6f, 0.62f, 0.66f, 0.5f);

        private static readonly Color QuadClear = new(0.45f, 0.85f, 1f, 0.28f);
        private static readonly Color QuadBlocked = new(1f, 0.35f, 0.3f, 0.3f);

        /// <summary>
        /// Outline width as a fraction of the item's own longest side, and the metres it is
        /// allowed to land between.
        ///
        /// <para>
        /// The width is now world metres (see <c>PackDragTint.shader</c>), so it has to be chosen
        /// per item or it is either a hairline on a 1.35 m staff or a 5% border round a 0.16 m
        /// leash. A fraction with a floor and a ceiling gives a line that reads the same on both
        /// and can never swamp the silhouette it is drawn around, which is the failure this
        /// replaces: the old object-space width came out at 1.2 m on the item scanner.
        /// </para>
        /// </summary>
        private const float OutlineFraction = 0.020f;
        private const float MinOutlineWidth = 0.0015f;
        private const float MaxOutlineWidth = 0.010f;

        /// Relative weights, keeping the ghost the widest and the hover rim the finest — the
        /// proportions the three roles were originally authored with.
        private const float HoverWeight = 1f;
        private const float DragWeight = 1.2f;
        private const float GhostWeight = 1.4f;

        /// <summary>Marks the shell objects this class adds, so it never re-shells its own work.</summary>
        private const string ShellName = "PackOutlineShell";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int BodyOnId = Shader.PropertyToID("_BodyOn");
        private static readonly int OutlineOnId = Shader.PropertyToID("_OutlineOn");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private readonly Material dragMaterial;
        private readonly Material hoverMaterial;
        private readonly Material ghostMaterial;
        private readonly Material quadMaterial;

        private GameObject proxy;
        private PackSurface proxySurface;
        private Vector2 proxyUv;
        private float proxyYaw;

        private GameObject quad;

        private Canvas labelCanvas;
        private TextMeshProUGUI label;

        // The shell objects standing in for the hover rim and the ghost. They are parented to the
        // renderers they trace, so a display copy destroyed under us — a layout change from
        // another player does exactly that — takes its shells with it and leaves nothing dangling.
        private readonly List<GameObject> rimShell = new();
        private readonly List<GameObject> ghostShell = new();
        private GameObject rimmed;
        private GameObject ghosted;

        public PackDragVisuals()
        {
            Shader shader = Shader.Find(ShaderName);

            // Same fallback shape HelmetDangerVignette uses. Without the custom shader the tint
            // still reads — it just loses the outline and the draw-on-top, which are polish on
            // something that still communicates.
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            dragMaterial = Build(shader, "PackDrag");
            hoverMaterial = Build(shader, "PackHover");
            ghostMaterial = Build(shader, "PackGhost");
            quadMaterial = Build(shader, "PackFootprint");

            // The dragged copy: body and outline, depth test off, queued after everything. This is
            // spec 4.3's "visible at all times" — an item halfway across the rig must not vanish
            // behind the back panel it is passing.
            dragMaterial.SetFloat(BodyOnId, 1f);
            dragMaterial.SetFloat(OutlineOnId, 1f);
            dragMaterial.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
            dragMaterial.renderQueue = 4000;
            SetDragTint(conflict: false);

            // Hover and ghost: the outline pass only, depth-tested normally, drawn on a shell that
            // traces the real item so the ITEM lights up. Spec 5.2 is explicit that there is no
            // floating UI box. The widths here are placeholders — every Apply sets a real one from
            // the item's own size.
            ConfigureRim(hoverMaterial, HoverRim, MinOutlineWidth);
            ConfigureRim(ghostMaterial, GhostRim, MinOutlineWidth);

            // The footprint: body only, transparent, no depth write.
            quadMaterial.SetFloat(BodyOnId, 1f);
            quadMaterial.SetFloat(OutlineOnId, 0f);
            quadMaterial.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            quadMaterial.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            quadMaterial.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            quadMaterial.SetFloat(ZWriteId, 0f);
            quadMaterial.renderQueue = 3000;
        }

        private static Material Build(Shader shader, string name) =>
            new(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

        private static void ConfigureRim(Material material, Color colour, float width)
        {
            material.SetFloat(BodyOnId, 0f);
            material.SetFloat(OutlineOnId, 1f);
            material.SetColor(OutlineColorId, colour);
            material.SetFloat(OutlineWidthId, width);
            material.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.renderQueue = 2001;
        }

        // ── Hover ────────────────────────────────────────────────────────────

        /// <summary>Rim-light one placed item and un-rim whatever was lit before. Null clears.</summary>
        public void SetHovered(GameObject visual)
        {
            // The null case is never skipped: a display copy destroyed under us leaves `rimmed`
            // equal to null by Unity's own comparison, and skipping would strand the shell list.
            if (visual != null && rimmed == visual) return;

            rimmed = visual;
            BuildShell(rimmed, hoverMaterial, HoverWeight, rimShell);
        }

        /// <summary>The outline left standing where a dragged item came from.</summary>
        public void SetGhost(GameObject visual)
        {
            if (visual != null && ghosted == visual) return;

            ghosted = visual;
            BuildShell(ghosted, ghostMaterial, GhostWeight, ghostShell);
        }

        // ── The dragged copy ─────────────────────────────────────────────────

        /// <summary>
        /// Put a copy of <paramref name="itemPrefab"/> in the player's hand, at true size.
        ///
        /// A separate copy rather than the placed one, because the placed one belongs to
        /// <c>BackpackObject</c> and is destroyed and rebuilt wholesale on every layout change —
        /// including the one this drag is about to cause.
        /// </summary>
        public void BeginDrag(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            EndDrag();
            Rebuild(itemPrefab, surface, uv, yaw);
        }

        /// <summary>
        /// Follow the cursor.
        ///
        /// <para>
        /// Within one face this is a translation, because a surface is planar and rigid: the world
        /// delta between two uvs is exact, and re-seating from scratch every frame would mean an
        /// Instantiate and a DestroyImmediate per frame. Crossing to another face, or turning,
        /// changes the seating, so those rebuild — both are things a player does a handful of times
        /// per drag.
        /// </para>
        /// </summary>
        public void MoveDrag(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            if (proxy == null || surface == null) return;

            if (surface != proxySurface || !Mathf.Approximately(yaw, proxyYaw))
            {
                Rebuild(itemPrefab, surface, uv, yaw);
                return;
            }

            proxy.transform.position += surface.ToWorld(uv, 0f) - surface.ToWorld(proxyUv, 0f);
            proxyUv = uv;
        }

        /// <summary>Grey, or red where the drop would be refused.</summary>
        public void SetDragTint(bool conflict)
        {
            dragMaterial.SetColor(ColorId, conflict ? ConflictBody : DragBody);
            dragMaterial.SetColor(OutlineColorId, conflict ? ConflictOutline : DragOutline);
        }

        public void EndDrag()
        {
            if (proxy != null) Object.Destroy(proxy);

            proxy = null;
            proxySurface = null;
            SetGhost(null);
            HideFootprint();
        }

        private void Rebuild(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            if (proxy != null) Object.Destroy(proxy);

            proxy = BackpackItemVisual.Build(itemPrefab, surface, uv, yaw);
            proxySurface = surface;
            proxyUv = uv;
            proxyYaw = yaw;

            if (proxy == null) return;

            // The copy Build hands back carries one BoxCollider for the cursor ray. On the thing
            // the cursor is currently holding that collider is in the way of everything behind it,
            // including the surface the player is trying to drop it on.
            foreach (Collider collider in proxy.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);

            proxy.transform.position += surface.transform.up * DragLift;

            // The proxy is ours alone, so its own materials can be replaced outright rather than
            // shelled — one material per submesh means every submesh gets both passes, which is
            // exactly what a shell buys elsewhere.
            dragMaterial.SetFloat(OutlineWidthId, OutlineWidthFor(proxy, DragWeight));
            Paint(proxy, dragMaterial);
        }

        // ── The footprint ────────────────────────────────────────────────────

        /// <summary>
        /// The exact rectangle the item would occupy, laid on the target face.
        ///
        /// This is what makes free placement feel measured rather than approximate — the player is
        /// not guessing whether a 1.35 m staff clears the edge, they can see the corner.
        /// </summary>
        public void ShowFootprint(PackSurface surface, Vector2 uv, Vector2 footprint, float yaw, bool conflict)
        {
            if (surface == null || footprint.x <= 0f || footprint.y <= 0f)
            {
                HideFootprint();
                return;
            }

            EnsureQuad();

            quad.SetActive(true);
            quad.transform.SetPositionAndRotation(surface.ToWorld(uv, QuadLift), surface.WorldRotation(yaw));

            // Left unparented and scaled in world metres, so the surface's own scale — the rig's
            // FBX arrives on the centimetre convention — never multiplies into the rectangle.
            quad.transform.localScale = new Vector3(footprint.x, 1f, footprint.y);

            quadMaterial.SetColor(ColorId, conflict ? QuadBlocked : QuadClear);
        }

        public void HideFootprint()
        {
            if (quad != null) quad.SetActive(false);
        }

        private void EnsureQuad()
        {
            if (quad != null) return;

            quad = new GameObject("PackFootprintQuad") { hideFlags = HideFlags.HideAndDontSave };

            var filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = UnitPlane();

            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = quadMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            int layer = BackpackItemVisual.ItemLayer;
            if (layer >= 0) quad.layer = layer;
        }

        /// <summary>A 1 x 1 m plane in XZ with its normal up, so a localScale IS the footprint.</summary>
        private static Mesh UnitPlane()
        {
            var mesh = new Mesh { name = "PackFootprintPlane", hideFlags = HideFlags.HideAndDontSave };

            mesh.SetVertices(new List<Vector3>
            {
                new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        // ── The name by the cursor ───────────────────────────────────────────

        /// <summary>
        /// The hovered item's name, small and low contrast, beside the cursor. Empty hides it.
        ///
        /// Deliberately not a panel. A pack whose whole point is that you can SEE what is in it
        /// does not need a tooltip window over the thing you are looking at.
        /// </summary>
        public void ShowName(string text, Vector2 screenPosition)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (labelCanvas != null) labelCanvas.gameObject.SetActive(false);
                return;
            }

            EnsureLabel();

            labelCanvas.gameObject.SetActive(true);
            label.text = text;
            label.rectTransform.position = screenPosition + new Vector2(18f, -18f);
        }

        private void EnsureLabel()
        {
            if (labelCanvas != null) return;

            var go = new GameObject("PackHoverLabel") { hideFlags = HideFlags.HideAndDontSave };

            labelCanvas = go.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Over the HUD, which is still on screen: focus mode keeps the hotbar because items
            // are dragged onto it.
            labelCanvas.sortingOrder = 500;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);

            label = textGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 15f;
            label.color = new Color(0.92f, 0.92f, 0.9f, 0.62f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.sizeDelta = new Vector2(320f, 26f);
        }

        // ── Outline shells ───────────────────────────────────────────────────

        /// <summary>
        /// Trace <paramref name="visual"/> with a set of throwaway renderers carrying only
        /// <paramref name="outline"/>, replacing whatever was traced before.
        ///
        /// <para>
        /// This used to <em>append</em> the outline material to the item's own renderers, which is
        /// half a technique: Unity draws a renderer's Nth material against submesh N and against
        /// the LAST submesh once it runs out, so an appended material outlines one submesh and no
        /// others. On this roster that is not an edge case — the item scanner's case has 10
        /// submeshes, the portal gun 12, the leash spool and the weather-station emitter 8 each —
        /// so the rim traced one arbitrary fragment of the prop and the player saw an outline that
        /// did not match the item.
        /// </para>
        /// <para>
        /// A shell has no such limit: it is a renderer of its own, so it gets one outline material
        /// per submesh and covers the whole silhouette. It is also safer than borrowing. Each part
        /// is parented to the renderer it traces, inheriting that renderer's exact object-to-world
        /// matrix and dying with it, so a display copy destroyed mid-hover — which
        /// <c>BackpackObject</c> does on every layout change — cannot leave this class holding a
        /// material array it can no longer put back.
        /// </para>
        /// </summary>
        private static void BuildShell(GameObject visual, Material outline, float weight,
                                       List<GameObject> parts)
        {
            ClearShell(parts);
            if (visual == null) return;

            outline.SetFloat(OutlineWidthId, OutlineWidthFor(visual, weight));

            foreach (Renderer source in visual.GetComponentsInChildren<Renderer>(true))
            {
                // Never shell our own shells: a hover and a ghost can land on the same visual.
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
                    // to be skinned too and share them. Nothing on a display copy animates — Strip
                    // takes the Animator off — but the bind pose still has to be evaluated.
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
                shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shell.receiveShadows = false;

                parts.Add(part);
            }
        }

        private static void ClearShell(List<GameObject> parts)
        {
            foreach (GameObject part in parts)
                if (part != null) Object.Destroy(part);

            parts.Clear();
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// How thick a line to draw round this particular item, in world metres.
        ///
        /// <para>
        /// A fraction of the item's own longest side, floored and capped. The shader inflates in
        /// world space now, so one constant cannot serve a 0.16 m leash and a 1.35 m staff at
        /// once — and the bug this replaces was exactly a constant that meant something different
        /// on every prop.
        /// </para>
        /// </summary>
        private static float OutlineWidthFor(GameObject visual, float weight)
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

        private static void Paint(GameObject visual, Material material)
        {
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                // One slot per SUBMESH, not per existing material. A prop whose renderer ships
                // fewer materials than submeshes would otherwise keep drawing its own material on
                // the submeshes past the end of the array, and the drag tint would come out
                // patchy on exactly the multi-submesh props this roster is full of.
                Mesh mesh = MeshOf(renderer);
                int slots = Mathf.Max(mesh != null ? mesh.subMeshCount : 0,
                                      renderer.sharedMaterials.Length);

                var all = new Material[Mathf.Max(1, slots)];
                for (int i = 0; i < all.Length; i++) all[i] = material;

                renderer.sharedMaterials = all;
            }
        }

        /// <summary>Destroys everything this owns: both outline shells, the quad, the label and
        /// the four materials.</summary>
        public void Dispose()
        {
            SetHovered(null);
            SetGhost(null);
            EndDrag();

            // SetHovered(null) and SetGhost(null) above already cleared these unless nothing was
            // lit, in which case the early-out skipped them. Cheap and idempotent either way.
            ClearShell(rimShell);
            ClearShell(ghostShell);

            if (quad != null) Object.Destroy(quad);
            quad = null;

            if (labelCanvas != null) Object.Destroy(labelCanvas.gameObject);
            labelCanvas = null;
            label = null;

            Object.Destroy(dragMaterial);
            Object.Destroy(hoverMaterial);
            Object.Destroy(ghostMaterial);
            Object.Destroy(quadMaterial);
        }
    }
}
