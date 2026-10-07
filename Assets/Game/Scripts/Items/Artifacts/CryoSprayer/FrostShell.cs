// The ice a body wears while it is freezing, and the whole of the statue once it has.
//
// A body is NOT swapped for a statue prefab here. The design's version of that swap has to
// snapshot a skinned pose, has to despawn and respawn a networked body, and has to answer what
// happens when the body it is replacing is a rider strapped into a seat — three ways to leave a
// body in a bad state, for a look this reaches without touching the body at all: a second set of
// renderers sharing the original's mesh and the original's BONES, shaded as ice, drawn over it.
// The pose is right because it is the same pose, on a rig that is still the same rig.
//
// Not a MonoBehaviour: it is owned by FrozenBody, which is the one component on the body, and a
// second component would be a second lifetime to get wrong.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// A coincident copy of one body's meshes, shaded with the frozen-statue material, whose
    /// <c>_Freeze</c> property is the rime creeping over it.
    ///
    /// <para>
    /// <b>Coincident, and that is why the render queue is forced.</b> The shell's vertices are the
    /// body's vertices — the same mesh, the same bones, the same matrix — so the two surfaces are
    /// at exactly the same depth rather than fighting over it. With <c>ZTest LEqual</c> whichever
    /// draws SECOND is the one that shows, and opaque draw order is by render queue first, so the
    /// ice is pushed to the last opaque queue. Left at the material's own queue the body would win
    /// on some frames and the statue on others, which reads as flicker and has nothing in the
    /// console. The last opaque queue is still opaque: it is inside the opaque pass the depth
    /// texture is copied after, so the ink pass still outlines the statue.
    /// </para>
    /// <para>
    /// <b>One material instance for the whole body</b>, so the freeze is one <c>SetFloat</c> rather
    /// than one per renderer, and so there is exactly one thing to destroy when the ice goes. A
    /// runtime material is a leak if nobody destroys it, which is what <see cref="Shed"/> is for.
    /// </para>
    /// </summary>
    public sealed class FrostShell
    {
        /// <summary>
        /// How covered in ice a surface is, 0 to 1. The one property the frozen-statue shader
        /// takes, shared with <see cref="CryoSprayerNozzle"/> so the rime on the gun's own barrel
        /// and the rime on what it is pointed at are driven by the same name.
        /// </summary>
        public static readonly int FreezeProperty = Shader.PropertyToID("_Freeze");

        private const string ShellName = "Frost Shell";

        private readonly List<Renderer> shells = new List<Renderer>();

        private Material ice;
        private bool built;

        /// <summary>Has the shell been made? Building is once per freeze, not once per frame.</summary>
        public bool Built => built;

        /// <summary>
        /// Copy every mesh under <paramref name="body"/> and shade the copies as ice. Does nothing
        /// on a second call, and nothing at all without a material to make the ice out of — a
        /// sprayer whose prefab was never given one still freezes bodies, invisibly, rather than
        /// throwing on the first creature somebody points it at.
        /// </summary>
        public void Build(Transform body, Material statueMaterial)
        {
            if (built || body == null || statueMaterial == null) return;

            built = true;

            ice = new Material(statueMaterial) { name = statueMaterial.name + " (frost)" };
            ice.SetFloat(FreezeProperty, 0f);
            ice.renderQueue = (int)RenderQueue.GeometryLast;

            // Enabled renderers only. A model child somebody switched off is one the player cannot
            // see, and an ice copy of it would be ice hanging in the air.
            foreach (Renderer source in body.GetComponentsInChildren<Renderer>(false))
            {
                Renderer shell = Clone(source);
                if (shell == null) continue;

                shell.sharedMaterials = Slots(source);

                // The body underneath still casts the same silhouette into the shadow map, so a
                // second caster of the same shape would only fight it.
                shell.shadowCastingMode = ShadowCastingMode.Off;
                shell.receiveShadows = source.receiveShadows;

                shells.Add(shell);
            }
        }

        /// <summary>How much of the body the ice has reached, 0 to 1. Cheap; call it every frame.</summary>
        public void SetFreeze(float freeze01)
        {
            if (ice == null) return;

            ice.SetFloat(FreezeProperty, Mathf.Clamp01(freeze01));
        }

        /// <summary>
        /// What the ice occupies in the world, for anything that needs to stand something under it.
        ///
        /// Taken off the SHELL rather than off the body's transform: a player's pivot sits about a
        /// metre above their soles, so a plinth placed at the transform would float at their waist.
        /// </summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            for (int i = 0; i < shells.Count; i++)
            {
                Renderer shell = shells[i];
                if (shell == null) continue;

                if (any) bounds.Encapsulate(shell.bounds);
                else { bounds = shell.bounds; any = true; }
            }

            return any;
        }

        /// <summary>
        /// Take the ice off and give the material back. Safe on a shell that was never built, and
        /// reached that way constantly — a body that thaws, a body that is destroyed mid-freeze,
        /// and a body whose chunk streamed out all end here.
        /// </summary>
        public void Shed()
        {
            for (int i = 0; i < shells.Count; i++)
                if (shells[i] != null) Object.Destroy(shells[i].gameObject);

            shells.Clear();

            if (ice != null)
            {
                Object.Destroy(ice);
                ice = null;
            }

            built = false;
        }

        /// <summary>
        /// One ice material per slot the source draws, so a two-submesh body freezes both halves.
        /// The slot COUNT is the source's, because that is what the mesh is actually drawn with;
        /// a shell with fewer slots would leave the last submesh bare and one with more would draw
        /// the last submesh twice.
        /// </summary>
        private Material[] Slots(Renderer source)
        {
            var slots = new Material[Mathf.Max(1, source.sharedMaterials.Length)];

            for (int i = 0; i < slots.Length; i++) slots[i] = ice;

            return slots;
        }

        /// <summary>
        /// An ice twin of one renderer, or null for anything that should not be frozen.
        ///
        /// <para>
        /// Particles, trails and lines are skipped because a frozen copy of a dust puff is a frozen
        /// dust puff. Anything under a <c>Canvas</c> is skipped because a world-space canvas draws
        /// through a <c>MeshRenderer</c> like everything else — a nameplate and a damage number
        /// both hang off one — and an ice-shaded copy of a nameplate is a pane of blue floating
        /// over the creature's head.
        /// </para>
        /// </summary>
        private static Renderer Clone(Renderer source)
        {
            if (source.GetComponentInParent<Canvas>() != null) return null;

            if (source is SkinnedMeshRenderer skinned) return CloneSkinned(skinned);
            if (source is MeshRenderer) return CloneStatic(source);

            return null;
        }

        /// <summary>
        /// The skinned case, which is the one that matters: the copy is given the SAME bone array
        /// and the same root bone, so it is deformed by the same matrices as the body and sits
        /// exactly on it, pose for pose. Copying only the mesh and letting the clone's own
        /// transform carry it is what produces a T-posed statue beside a walking animal.
        /// </summary>
        private static SkinnedMeshRenderer CloneSkinned(SkinnedMeshRenderer source)
        {
            if (source.sharedMesh == null) return null;

            var copy = Host(source).AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = source.sharedMesh;
            copy.bones = source.bones;
            copy.rootBone = source.rootBone;
            copy.localBounds = source.localBounds;
            copy.quality = source.quality;
            copy.updateWhenOffscreen = source.updateWhenOffscreen;

            return copy;
        }

        private static MeshRenderer CloneStatic(Renderer source)
        {
            if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return null;

            GameObject host = Host(source);
            host.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            return host.AddComponent<MeshRenderer>();
        }

        /// <summary>
        /// A child of the renderer it copies, at identity — so it inherits the same matrix, moves
        /// with it, and goes away with it if the body is destroyed before the ice is shed.
        /// </summary>
        private static GameObject Host(Renderer source)
        {
            var host = new GameObject(ShellName);
            host.layer = source.gameObject.layer;
            host.transform.SetParent(source.transform, false);

            return host;
        }
    }
}
