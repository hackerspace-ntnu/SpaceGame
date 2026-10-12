using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A cartoon skeleton drawn inside a body: a bone between every pair of the rig's own joints,
    /// a skull with eye sockets on the head, and a rib cage on the chest. It follows the joints
    /// every frame, so it falls and twitches with a ragdoll.
    ///
    /// <para>
    /// Built from whatever skeleton the body's skinned meshes are bound to, so it fits a Raxy, the
    /// astronaut, a robot or a crab without anything authored per creature. What a rig cannot tell
    /// us — which way its face points — is measured once by <see cref="BodyFrame"/> while the body
    /// is still standing.
    /// </para>
    /// <para>
    /// The drawn parts live under one object at the scene root, not under the body, so nothing that
    /// walks the body's hierarchy (the ragdoll, the singularity's veil, a scanner) ever finds them.
    /// </para>
    /// </summary>
    public sealed class BodySkeleton
    {
        /// <summary>A rig with more joints than this is drawn by its first ones only.</summary>
        private const int MaxBones = 90;

        // Unit bone geometry: along +Y from 0 to 1, one unit across. See BoneMesh.
        private const float ShaftRadius = 0.2f;
        private const float KnobRadius = 0.26f;
        private const float KnobSpread = 0.2f;
        private const float KnobInset = 0.14f;

        private struct Segment
        {
            public Transform From, To, Drawn;
        }

        private struct Pinned
        {
            public Transform Bone, Drawn;
            public Vector3 Offset;
            public Quaternion Rotation;
        }

        private static Mesh boneMesh;
        private static Mesh sphereMesh;

        private readonly GameObject root;
        private readonly List<Segment> segments = new List<Segment>();
        private readonly List<Pinned> pinned = new List<Pinned>();
        private readonly List<Transform> joints = new List<Transform>();
        private readonly float thickness;

        public BodySkeleton(Transform body, BodyFrame frame, ShockLook look)
        {
            root = new GameObject(body.name + " (x-ray skeleton)");
            thickness = frame.Height * look.boneThickness;
            float minLength = frame.Height * look.minBoneLength;

            HashSet<Transform> bones = BodyFrame.BonesOf(body);
            foreach (Transform bone in bones)
            {
                if (segments.Count >= MaxBones) break;

                Transform parent = bone.parent;
                if (parent == null || !bones.Contains(parent) || IsRigRoot(parent)) continue;
                if (Vector3.Distance(parent.position, bone.position) < minLength) continue;

                segments.Add(new Segment { From = parent, To = bone, Drawn = Part("Bone", BoneMesh(), look.bone) });
                if (!joints.Contains(parent)) joints.Add(parent);
                if (!joints.Contains(bone)) joints.Add(bone);
            }

            if (frame.Head != null) AddSkull(frame, look);
            if (frame.Chest != null) AddRibs(frame, look);

            Tick();
        }

        /// <summary>The joints the skeleton is drawn between, for arcs to jump along.</summary>
        public IReadOnlyList<Transform> Joints => joints;

        /// <summary>
        /// Two points the current can jump between: along one bone, or — when
        /// <paramref name="across"/> — between any two joints of the body.
        /// </summary>
        public bool TryPickArc(bool across, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (segments.Count == 0) return false;

            if (across && joints.Count > 1)
            {
                Transform a = joints[Random.Range(0, joints.Count)];
                Transform b = joints[Random.Range(0, joints.Count)];
                if (a != null && b != null && a != b)
                {
                    from = a.position;
                    to = b.position;
                    return true;
                }
            }

            Segment s = segments[Random.Range(0, segments.Count)];
            if (s.From == null || s.To == null) return false;
            from = s.From.position;
            to = s.To.position;
            return true;
        }

        public void SetVisible(bool visible) => root.SetActive(visible);

        /// <summary>Lay every drawn part along the joints as they are this frame.</summary>
        public void Tick()
        {
            foreach (Segment s in segments)
            {
                if (s.From == null || s.To == null) continue;

                Vector3 span = s.To.position - s.From.position;
                float length = span.magnitude;
                if (length < 1e-4f) continue;

                s.Drawn.SetPositionAndRotation(s.From.position, Quaternion.FromToRotation(Vector3.up, span / length));
                s.Drawn.localScale = new Vector3(thickness, length, thickness);
            }

            foreach (Pinned p in pinned)
            {
                if (p.Bone == null) continue;
                p.Drawn.SetPositionAndRotation(p.Bone.TransformPoint(p.Offset), p.Bone.rotation * p.Rotation);
            }
        }

        public void Dispose()
        {
            RuntimeObjects.Discard(root);
        }

        // ── Skull and ribs ──────────────────────────────────────────────────────

        private void AddSkull(BodyFrame frame, ShockLook look)
        {
            float size = frame.Height * look.skullSize;
            Transform head = frame.Head;
            Vector3 up = frame.HeadUp, front = frame.HeadFront;

            // The head joint sits at the base of the skull; the skull is the ball above it, its
            // local Y up the head and Z out of the face.
            Quaternion faced = Quaternion.LookRotation(front, up);
            Vector3 centre = up * (size * 0.45f);
            Pin(head, "Skull", SphereMesh(), look.bone, centre, faced, new Vector3(size * 0.9f, size, size));

            // Jaw: a flatter ball low and forward.
            Pin(head, "Jaw", SphereMesh(), look.bone, centre - up * (size * 0.38f) + front * (size * 0.12f),
                faced, new Vector3(size * 0.6f, size * 0.3f, size * 0.55f));

            Vector3 side = Vector3.Cross(up, front).normalized;
            float socket = size * 0.26f;
            foreach (float s in new[] { -1f, 1f })
                Pin(head, "Socket", SphereMesh(), look.socket,
                    centre + front * (size * 0.36f) + side * (s * size * 0.2f) + up * (size * 0.05f),
                    faced, Vector3.one * socket);
        }

        private void AddRibs(BodyFrame frame, ShockLook look)
        {
            Transform chest = frame.Chest;
            Vector3 up = frame.ChestUp, front = frame.ChestFront;
            Vector3 side = Vector3.Cross(up, front).normalized;
            float width = frame.Height * look.ribWidth;
            float rib = thickness * 0.7f;

            // Three bars across the front of the chest, narrowing toward the waist; a sternum down
            // the middle holds them together.
            for (int i = 0; i < 3; i++)
            {
                float w = width * (1f - i * 0.15f);
                Vector3 centre = front * (width * 0.2f) + up * (width * (0.3f - i * 0.28f));
                Pin(chest, "Rib", BoneMesh(), look.bone, centre - side * (w * 0.5f),
                    Quaternion.FromToRotation(Vector3.up, side), new Vector3(rib, w, rib));
            }

            Pin(chest, "Sternum", BoneMesh(), look.bone, front * (width * 0.24f) + up * (width * 0.38f),
                Quaternion.FromToRotation(Vector3.up, -up), new Vector3(rib, width * 0.6f, rib));
        }

        /// <summary>
        /// A part riding a joint at a fixed offset. <paramref name="offset"/> and the axes the
        /// rotation is built from are in the joint's own space, measured by <see cref="BodyFrame"/>.
        /// </summary>
        private void Pin(Transform bone, string name, Mesh mesh, Material material, Vector3 offset,
                         Quaternion rotation, Vector3 scale)
        {
            Transform drawn = Part(name, mesh, material);
            drawn.localScale = scale;
            pinned.Add(new Pinned { Bone = bone, Drawn = drawn, Offset = offset, Rotation = rotation });
        }

        private Transform Part(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        /// <summary>A bone the rig hangs from rather than a bone of the body: the root at the feet.</summary>
        private static bool IsRigRoot(Transform bone)
        {
            string n = bone.name.ToLowerInvariant();
            return n.Contains("root") || n.Contains("armature");
        }

        // ── Meshes, built once ──────────────────────────────────────────────────

        /// <summary>
        /// A cartoon bone: a shaft with a pair of knobs at each end, from y = 0 to y = 1 and one
        /// unit across. Made from the built-in primitives, which exist in every build.
        /// </summary>
        private static Mesh BoneMesh()
        {
            if (boneMesh != null) return boneMesh;

            Mesh cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            Mesh sphere = SphereMesh();
            float shaftLength = 1f - 2f * KnobInset;

            var parts = new List<CombineInstance>
            {
                new CombineInstance
                {
                    mesh = cylinder,
                    // The built-in cylinder is 2 tall and 1 across, centred.
                    transform = Matrix4x4.TRS(new Vector3(0f, 0.5f, 0f), Quaternion.identity,
                                              new Vector3(ShaftRadius * 2f, shaftLength * 0.5f, ShaftRadius * 2f)),
                },
            };
            foreach (float y in new[] { KnobInset, 1f - KnobInset })
            foreach (float x in new[] { -KnobSpread, KnobSpread })
                parts.Add(new CombineInstance
                {
                    mesh = sphere,
                    transform = Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, Vector3.one * (KnobRadius * 2f)),
                });

            boneMesh = new Mesh { name = "XRayBone" };
            boneMesh.CombineMeshes(parts.ToArray(), mergeSubMeshes: true, useMatrices: true);
            return boneMesh;
        }

        private static Mesh SphereMesh() => sphereMesh != null ? sphereMesh : sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            // At once, not at the end of the frame: the primitive carries a collider.
            Object.DestroyImmediate(primitive);
            return mesh;
        }
    }
}
