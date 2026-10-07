using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where a display's glass is: its centre, its outward normal, its up, and its size in the
    /// plane. Measured off the plate MESH itself rather than read from the plate's transform.
    ///
    /// <para>
    /// The transform cannot be trusted for this. A plate exported from Blender arrives in Unity
    /// under the FBX importer's -90°X / x100 bake, and a hand-edited one may have been rotated
    /// and moved by its author since — the standing terminal's screen leans back 24° because its
    /// author leaned the whole cabinet. The triangles are the one thing that is always right:
    /// the glass is the plate's largest face, and it faces away from the housing.
    /// </para>
    /// </summary>
    public readonly struct ScreenPlane
    {
        public readonly Vector3 Centre;

        /// <summary>Unit normal pointing out of the glass, toward the viewer.</summary>
        public readonly Vector3 Normal;

        /// <summary>Unit "up" in the plane — the normal tipped upright, so text reads the right way.</summary>
        public readonly Vector3 Up;

        /// <summary>Unit "right" in the plane, completing a right-handed frame with Up and Normal.</summary>
        public readonly Vector3 Right;

        /// <summary>Extent of the glass along <see cref="Right"/>, metres.</summary>
        public readonly float Width;

        /// <summary>Extent of the glass along <see cref="Up"/>, metres.</summary>
        public readonly float Height;

        public ScreenPlane(Vector3 centre, Vector3 normal, Vector3 up, Vector3 right, float width, float height)
        {
            Centre = centre;
            Normal = normal;
            Up = up;
            Right = right;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Measures a plate from its world-space vertices and triangles.
        ///
        /// <para>
        /// The normal is the largest triangle's, flipped if it points toward
        /// <paramref name="housingCentre"/>: a thin plate has two large faces and only one of
        /// them is glass. Up is world up projected into the plane, which is what "the right way
        /// up" means for a screen that leans back; for a plate lying flat it falls back to the
        /// viewer's forward, so a table-top display still has a defined up.
        /// </para>
        /// </summary>
        public static ScreenPlane Measure(Vector3[] vertices, int[] triangles, Vector3 housingCentre)
        {
            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
                return new ScreenPlane(housingCentre, Vector3.forward, Vector3.up, Vector3.right, 0f, 0f);

            Vector3 centre = Vector3.zero;
            foreach (Vector3 v in vertices) centre += v;
            centre /= vertices.Length;

            Vector3 normal = Vector3.forward;
            float largest = -1f;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude;
                if (area <= largest) continue;
                largest = area;
                normal = cross / area;
            }

            if (Vector3.Dot(normal, centre - housingCentre) < 0f) normal = -normal;

            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            up.Normalize();
            Vector3 right = Vector3.Cross(up, normal).normalized;

            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                Vector3 d = v - centre;
                float r = Vector3.Dot(d, right);
                float u = Vector3.Dot(d, up);
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }

            // The centre of the glass proper is the front face's centre, not the plate's
            // volumetric one: half the plate's thickness forward along the normal.
            float minN = float.MaxValue, maxN = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                float n = Vector3.Dot(v - centre, normal);
                if (n < minN) minN = n;
                if (n > maxN) maxN = n;
            }
            Vector3 face = centre + normal * maxN;

            return new ScreenPlane(face, normal, up, right, maxR - minR, maxU - minU);
        }

        /// <summary>The frame's rotation: forward along the normal (out of the glass), up along Up.</summary>
        public Quaternion Rotation => Quaternion.LookRotation(Normal, Up);
    }
}
