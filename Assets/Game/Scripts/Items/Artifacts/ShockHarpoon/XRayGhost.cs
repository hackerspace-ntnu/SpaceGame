using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// The glass outline an electrocuted body turns into: a ghost copy of every renderer of the
    /// body, drawn in the X-ray shell, shown while the body's own renderers are switched off.
    ///
    /// <para>
    /// <b>The body's materials are never touched.</b> A ghost is a second renderer bound to the
    /// same mesh and the same bones, so it skins and falls with the ragdoll for free; the body's
    /// own renderer is hidden with <see cref="Renderer.forceRenderingOff"/>, which no other system
    /// in the game writes (the singularity's veil uses <c>enabled</c>, first person's head hide uses
    /// <c>shadowCastingMode</c>), so ending the shock is putting one flag back.
    /// </para>
    /// <para>
    /// A body with a <see cref="LODGroup"/> is ghosted by its nearest level only and has every
    /// level hidden: the group drives its renderers' visibility itself, and a ghost per level would
    /// draw all of them at once, additively, three times as bright.
    /// </para>
    /// </summary>
    public sealed class XRayGhost
    {
        private readonly List<Renderer> hidden = new List<Renderer>();
        private readonly List<Renderer> ghosts = new List<Renderer>();
        private readonly List<(SkinnedMeshRenderer body, SkinnedMeshRenderer ghost)> faces =
            new List<(SkinnedMeshRenderer, SkinnedMeshRenderer)>();

        public XRayGhost(Transform body, Material shell)
        {
            var nearest = new HashSet<Renderer>();
            var levelled = new HashSet<Renderer>();
            foreach (LODGroup group in body.GetComponentsInChildren<LODGroup>())
            {
                LOD[] lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                foreach (Renderer r in lods[i].renderers)
                {
                    if (r == null) continue;
                    levelled.Add(r);
                    if (i == 0) nearest.Add(r);
                }
            }

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (BodyAttachment.Covers(r.transform, body)) continue;

                hidden.Add(r);
                if (levelled.Contains(r) && !nearest.Contains(r)) continue;

                Renderer ghost = Ghost(r, shell);
                if (ghost != null) ghosts.Add(ghost);
            }

            SetShowing(false);
        }

        /// <summary>X-ray on (the body hidden, its ghost drawn) or off (the body as it is).</summary>
        public void SetShowing(bool xray)
        {
            foreach (Renderer r in hidden)
                if (r != null) r.forceRenderingOff = xray;
            foreach (Renderer g in ghosts)
                if (g != null) g.enabled = xray;
        }

        /// <summary>Keep a ghost's face in step with the body's: a mouth that moves, eyes that blink.</summary>
        public void Tick()
        {
            foreach ((SkinnedMeshRenderer body, SkinnedMeshRenderer ghost) in faces)
            {
                if (body == null || ghost == null) continue;
                int count = body.sharedMesh != null ? body.sharedMesh.blendShapeCount : 0;
                for (int i = 0; i < count; i++) ghost.SetBlendShapeWeight(i, body.GetBlendShapeWeight(i));
            }
        }

        public void Dispose()
        {
            foreach (Renderer r in hidden)
                if (r != null) r.forceRenderingOff = false;
            foreach (Renderer g in ghosts)
                if (g != null) RuntimeObjects.Discard(g.gameObject);
            hidden.Clear();
            ghosts.Clear();
            faces.Clear();
        }

        private Renderer Ghost(Renderer source, Material shell)
        {
            var go = new GameObject("XRay");
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            // Attached to the body, not part of it: the ragdoll and every later X-ray must skip it.
            go.AddComponent<BodyAttachment>();

            Renderer ghost;
            int subMeshes;
            if (source is SkinnedMeshRenderer skin)
            {
                if (skin.sharedMesh == null) { RuntimeObjects.Discard(go); return null; }

                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh;
                copy.bones = skin.bones;
                copy.rootBone = skin.rootBone;
                copy.localBounds = skin.localBounds;
                copy.updateWhenOffscreen = skin.updateWhenOffscreen;
                if (skin.sharedMesh.blendShapeCount > 0) faces.Add((skin, copy));
                subMeshes = skin.sharedMesh.subMeshCount;
                ghost = copy;
            }
            else
            {
                MeshFilter filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) { RuntimeObjects.Discard(go); return null; }

                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                ghost = go.AddComponent<MeshRenderer>();
                subMeshes = filter.sharedMesh.subMeshCount;
            }

            var materials = new Material[Mathf.Max(1, subMeshes)];
            for (int i = 0; i < materials.Length; i++) materials[i] = shell;
            ghost.sharedMaterials = materials;
            ghost.shadowCastingMode = ShadowCastingMode.Off;
            ghost.receiveShadows = false;
            return ghost;
        }
    }
}
