using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The miniature of the lander the terminal draws: which of its renderers are modules that can
    /// be fitted, and which are the hull they bolt to.
    ///
    /// <para>
    /// Baked by <c>ShipSchematicBuilder</c> from the same FBX <c>PlayerShipBuilder</c> uses, so the
    /// drawing on the glass cannot drift from the ship standing around it. Nothing here is logic —
    /// it is the index <see cref="ShipSchematicStage"/> reads so that painting a module is an array
    /// lookup rather than a name search every frame.
    /// </para>
    /// <para>
    /// A module is addressed by the NAME of the mesh it was cut from — <c>Part_NuclearMotor_A</c> —
    /// and never by its position in this array. The ship's own socket order is decided by
    /// <c>ShipPartRack</c> on a prefab built by a different builder from a different pass over the
    /// same model; two arrays that happen to agree today because they were sorted the same way is
    /// not a thing to bet a replicated bitmask on.
    /// </para>
    /// <para>
    /// Every measurement here is in the model root's OWN space, not the world's. The miniature is
    /// parented to a terminal aboard a ship that turns; framing it in world space would swing the
    /// drawing round the glass every time the hull changed heading.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipSchematicModel : MonoBehaviour
    {
        [Serializable]
        public struct Part
        {
            [Tooltip("The mesh's name in the ship model — the same name the ship's own ShipPartSocket carries.")]
            public string socketName;

            public ShipPartKind kind;

            [Tooltip("The module's faces. Measured for its box, and drawn dark so the wireframe over it reads.")]
            public Renderer partRenderer;

            [Tooltip("The module's feature edges, as a line mesh. Painted with the faces, never apart from them.")]
            public Renderer wireRenderer;
        }

        [SerializeField] private Part[] parts = Array.Empty<Part>();

        [Tooltip("Everything that is not a module: the hull the modules are missing FROM.")]
        [SerializeField] private Renderer[] hull = Array.Empty<Renderer>();

        [Tooltip("The hull's feature edges. One combined line mesh, painted with the hull's faces.")]
        [SerializeField] private Renderer[] hullWire = Array.Empty<Renderer>();

        private Bounds[] partBounds;
        private Bounds wholeBounds;
        private bool measured;

        public IReadOnlyList<Part> Parts => parts;
        public IReadOnlyList<Renderer> Hull => hull;
        public IReadOnlyList<Renderer> HullWire => hullWire;

        /// <summary>The box round the whole miniature, model space.</summary>
        public Bounds Bounds
        {
            get { Measure(); return wholeBounds; }
        }

        /// <summary>The box round one module, model space — what the lens frames and the cursor hits.</summary>
        public Bounds PartBounds(int index)
        {
            Measure();
            return index >= 0 && index < partBounds.Length ? partBounds[index] : new Bounds();
        }

        public IEnumerable<Renderer> All()
        {
            foreach (Renderer r in Faces()) yield return r;
            foreach (Renderer r in Wires()) yield return r;
        }

        /// <summary>The triangle renderers: dark, depth-writing, and what hides the lines behind them.</summary>
        public IEnumerable<Renderer> Faces()
        {
            foreach (Part part in parts)
                if (part.partRenderer != null) yield return part.partRenderer;

            foreach (Renderer r in hull)
                if (r != null) yield return r;
        }

        /// <summary>The line renderers: the drawing itself.</summary>
        public IEnumerable<Renderer> Wires()
        {
            foreach (Part part in parts)
                if (part.wireRenderer != null) yield return part.wireRenderer;

            foreach (Renderer r in hullWire)
                if (r != null) yield return r;
        }

        /// <summary>
        /// Measured from the MESHES rather than from <c>Renderer.bounds</c>, once. A renderer's
        /// bounds are a world-axis-aligned box that changes shape as the ship it is parented to
        /// turns; the mesh's own box, carried into model space, is the same box forever — and a
        /// module hidden because it is not fitted has no renderer bounds worth reading at all.
        /// </summary>
        private void Measure()
        {
            if (measured) return;
            measured = true;

            partBounds = new Bounds[parts.Length];
            bool any = false;

            for (int i = 0; i < parts.Length; i++)
            {
                partBounds[i] = InModelSpace(parts[i].partRenderer);
                if (parts[i].partRenderer == null) continue;

                if (!any) { wholeBounds = partBounds[i]; any = true; }
                else wholeBounds.Encapsulate(partBounds[i]);
            }

            foreach (Renderer r in hull)
            {
                if (r == null) continue;
                Bounds box = InModelSpace(r);
                if (!any) { wholeBounds = box; any = true; }
                else wholeBounds.Encapsulate(box);
            }

            if (!any) wholeBounds = new Bounds(Vector3.zero, Vector3.one);
        }

        private Bounds InModelSpace(Renderer r)
        {
            var filter = r != null ? r.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return new Bounds();

            Bounds local = mesh.bounds;
            var box = new Bounds();
            bool any = false;

            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 point = transform.InverseTransformPoint(r.transform.TransformPoint(offset));
                if (!any) { box = new Bounds(point, Vector3.zero); any = true; }
                else box.Encapsulate(point);
            }

            return box;
        }
    }
}
