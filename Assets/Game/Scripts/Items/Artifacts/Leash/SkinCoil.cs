// Rope laid tight on a body's skin: the measuring half of the hogtie wrap and the lasso's collar.
//
// A cord pulled tight round anything lies on the convex hull of what it crosses, clothes and all,
// and moves with the bone under it. Everything here is that sentence, made cheap enough to run on
// the frame a tie lands or a loop closes.
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>How a cord is laid on the skin. Built from whatever owns the rope's look.</summary>
    public readonly struct SkinFit
    {
        /// <summary>The cord's thickness, metres. Its centre lies half this off the skin.</summary>
        public readonly float Width;

        /// <summary>
        /// How far one part of a cross-section may stand out past the rest, as a multiple of its middle
        /// radius, before the cord is let through it rather than pulled round it: a cape edge, a strap.
        /// </summary>
        public readonly float LooseLimit;

        /// <summary>
        /// How far out something lying against the body (an arm along the trunk, a hood against a neck)
        /// may reach, as a multiple of the body's own reach that way, and still be taken in.
        /// </summary>
        public readonly float LimbHug;

        public SkinFit(float width, float looseLimit, float limbHug)
        {
            Width = width;
            LooseLimit = looseLimit;
            LimbHug = limbHug;
        }
    }

    /// <summary>
    /// Rope laid tight on a body: a coil of turns, or one closed ring, in the space of the bone it
    /// rides on. Laid round the body's own cross-section, so it follows that bone through a struggle
    /// or a drag without being measured again. Shared by the hogtie wrap and the lasso's collar.
    /// </summary>
    internal sealed class SkinCoil
    {
        /// <summary>
        /// Points per turn. Sixteen keeps a straight chord within about 2% of the radius it cuts
        /// across, which on a torso is under the rope's own thickness.
        /// </summary>
        public const int PointsPerTurn = 16;

        /// <summary>Angular slices a turn's cross-section is read in, to judge how far out its body reaches.</summary>
        private const int Slices = 24;

        private readonly Transform carrier;
        private readonly Vector3[] local;

        private SkinCoil(Transform carrier, Vector3[] local, bool measured)
        {
            this.carrier = carrier;
            this.local = local;
            Measured = measured;
        }

        /// <summary>Did any of it find a body to lie on? False is the fallback circle.</summary>
        public bool Measured { get; }

        /// <summary>Where it lies now, world space, appended to <paramref name="into"/>.</summary>
        public void AddTo(List<Vector3> into)
        {
            if (carrier == null) return;
            foreach (Vector3 point in local) into.Add(carrier.TransformPoint(point));
        }

        /// <summary>
        /// <paramref name="turns"/> turns round <paramref name="centre"/>, spread over
        /// <paramref name="span"/> metres of <paramref name="axis"/>, each laid on the convex hull
        /// of the surface it crosses — what a cord pulled tight round it actually touches —
        /// half the rope's width out. A circle of <paramref name="fallbackRadius"/> where there is
        /// nothing to measure. Where <paramref name="alongside"/>'s surface lies against
        /// <paramref name="part"/>'s, within <see cref="SkinFit.LimbHug"/> of it, it is taken in too.
        /// </summary>
        public static SkinCoil Round(BodySurface surface, System.Func<Transform, bool> part, System.Func<Transform, bool> alongside,
                                     Transform carrier, Vector3 centre, Vector3 axis, int turns, float span,
                                     float fallbackRadius, SkinFit fit) =>
            Lay(surface, part, alongside, carrier, centre, axis, turns, span, span, fallbackRadius, fit);

        /// <summary>
        /// One closed ring round <paramref name="centre"/>, flat across <paramref name="axis"/>, laid on
        /// the body's cross-section measured over <paramref name="band"/> metres of it: a noose
        /// pulled shut. The first point is also the last.
        /// </summary>
        public static SkinCoil Ring(BodySurface surface, System.Func<Transform, bool> part, System.Func<Transform, bool> alongside,
                                    Transform carrier, Vector3 centre, Vector3 axis, float band, float fallbackRadius,
                                    SkinFit fit) =>
            Lay(surface, part, alongside, carrier, centre, axis, 1, band, 0f, fallbackRadius, fit);

        /// <param name="measured">Metres of the axis the cross-sections are read over, split between the turns.</param>
        /// <param name="laid">Metres of the axis the turns advance over: the span for a coil, nothing for a ring.</param>
        private static SkinCoil Lay(BodySurface surface, System.Func<Transform, bool> part, System.Func<Transform, bool> alongside,
                                    Transform carrier, Vector3 centre, Vector3 axis, int turns, float measured, float laid,
                                    float fallbackRadius, SkinFit fit)
        {
            Vector3 along = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
            Vector3 across = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 round = Vector3.Cross(along, across);

            SkinSection[] sections = Sections(surface, part, alongside, centre, along, across, round, turns, measured, fit);
            float half = fit.Width * 0.5f;

            int count = turns * PointsPerTurn + 1;
            var local = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float angle = t * turns * Mathf.PI * 2f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // Between the cross-sections of the turns either side, so the rope never steps
                // where one turn's slice of the body ends and the next one's begins.
                float at = Mathf.Clamp(t * turns - 0.5f, 0f, turns - 1);
                int below = Mathf.FloorToInt(at);
                int above = Mathf.Min(below + 1, turns - 1);
                Vector2 onSkin = Vector2.Lerp(SkinSection.OnSkin(sections[below], direction, half, fallbackRadius),
                                              SkinSection.OnSkin(sections[above], direction, half, fallbackRadius), at - below);

                Vector3 world = centre + along * ((t - 0.5f) * laid) + across * onSkin.x + round * onSkin.y;
                local[i] = carrier.InverseTransformPoint(world);
            }

            return new SkinCoil(carrier, local, System.Array.Exists(sections, section => section != null));
        }

        /// <summary>
        /// The body's cross-section under each turn: the convex hull of <paramref name="part"/>'s surface in that
        /// turn's band of the axis, less any point standing out past <see cref="SkinFit.LooseLimit"/> times the
        /// middle of the angular slices' outermost reaches — and then with <paramref name="alongside"/>'s points that lie
        /// within <see cref="SkinFit.LimbHug"/> of that outline taken in too.
        /// </summary>
        private static SkinSection[] Sections(BodySurface surface, System.Func<Transform, bool> part,
                                          System.Func<Transform, bool> alongside, Vector3 centre, Vector3 along,
                                          Vector3 across, Vector3 round, int turns, float span, SkinFit fit)
        {
            var crossed = new List<Vector2>[turns];
            var lying = new List<Vector2>[turns];
            var reach = new float[turns, Slices];
            float band = span / turns;

            for (int i = 0; i < surface.Count; i++)
            {
                Transform owner = surface.OwnerOf(i);
                if (owner == null) continue;

                bool own = part(owner);
                if (!own && (alongside == null || !alongside(owner))) continue;

                Vector3 offset = surface.PointOf(i) - centre;
                int ring = Mathf.FloorToInt((Vector3.Dot(offset, along) + span * 0.5f) / band);
                if (ring < 0 || ring >= turns) continue;

                var flat = new Vector2(Vector3.Dot(offset, across), Vector3.Dot(offset, round));
                if (!own)
                {
                    (lying[ring] ??= new List<Vector2>()).Add(flat);
                    continue;
                }

                int slice = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(flat.y, flat.x) + Mathf.PI) / (2f * Mathf.PI) * Slices),
                                        0, Slices - 1);
                reach[ring, slice] = Mathf.Max(reach[ring, slice], flat.magnitude);
                (crossed[ring] ??= new List<Vector2>()).Add(flat);
            }

            var sections = new SkinSection[turns];
            var radii = new List<float>(Slices);

            for (int ring = 0; ring < turns; ring++)
            {
                if (crossed[ring] == null) continue;

                // The slices' outermost reaches say how far out this turn's body goes; a point well past
                // their middle one is a flap the cord is let through rather than pulled round. The hull is
                // then of every point left, not one per slice: points on a slice's edge would otherwise
                // drop a corner and the cord would cut across it.
                radii.Clear();
                for (int slice = 0; slice < Slices; slice++)
                    if (reach[ring, slice] > 0f) radii.Add(reach[ring, slice]);
                radii.Sort();
                float limit = radii[radii.Count / 2] * fit.LooseLimit;

                crossed[ring].RemoveAll(flat => flat.sqrMagnitude > limit * limit);
                SkinSection own = SkinSection.Hull(crossed[ring]);
                if (own == null || lying[ring] == null)
                {
                    sections[ring] = own;
                    continue;
                }

                // An arm or a leg is taken in only where it lies against this outline, not where it hangs clear of it.
                foreach (Vector2 flat in lying[ring])
                {
                    Vector2 outward = flat - own.Middle;
                    float distance = outward.magnitude;
                    if (distance <= own.Reach(outward / Mathf.Max(distance, 1e-6f)) * fit.LimbHug) crossed[ring].Add(flat);
                }

                sections[ring] = SkinSection.Hull(crossed[ring]);
            }

            // A turn whose band caught nothing borrows its neighbour's: the body did not stop there,
            // the sample was just thin.
            for (int pass = 0; pass < turns; pass++)
                for (int ring = 0; ring < turns; ring++)
                {
                    if (sections[ring] != null) continue;
                    sections[ring] = ring > 0 && sections[ring - 1] != null ? sections[ring - 1]
                                   : ring + 1 < turns ? sections[ring + 1] : null;
                }

            return sections;
        }
    }

    /// <summary>A convex cross-section of the body across a coil's axis, in that coil's own 2D frame.</summary>
    internal sealed class SkinSection
    {
        private readonly Vector2[] hull;
        private readonly Vector2 middle;

        private SkinSection(Vector2[] hull)
        {
            this.hull = hull;
            foreach (Vector2 corner in hull) middle += corner;
            middle /= hull.Length;
        }

        /// <summary>
        /// The convex hull of <paramref name="outline"/> (Andrew's monotone chain), or null when
        /// fewer than three points span an area.
        /// </summary>
        public static SkinSection Hull(List<Vector2> outline)
        {
            if (outline.Count < 3) return null;

            outline.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var chain = new Vector2[outline.Count * 2];
            int k = 0;

            for (int i = 0; i < outline.Count; i++)
            {
                while (k >= 2 && Cross(chain[k - 2], chain[k - 1], outline[i]) <= 0f) k--;
                chain[k++] = outline[i];
            }

            for (int i = outline.Count - 2, lower = k + 1; i >= 0; i--)
            {
                while (k >= lower && Cross(chain[k - 2], chain[k - 1], outline[i]) <= 0f) k--;
                chain[k++] = outline[i];
            }

            if (k - 1 < 3) return null;

            var hull = new Vector2[k - 1];
            System.Array.Copy(chain, hull, k - 1);
            return new SkinSection(hull);
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// Where a cord <paramref name="half"/> its width thick lies on <paramref name="section"/> in
        /// <paramref name="direction"/> from its middle. A circle of <paramref name="fallbackRadius"/>
        /// about the axis when there is no section at all — a body that draws nothing there.
        /// </summary>
        public static Vector2 OnSkin(SkinSection section, Vector2 direction, float half, float fallbackRadius)
        {
            float reach = section != null ? section.Reach(direction) : float.PositiveInfinity;
            return float.IsPositiveInfinity(reach) ? direction * fallbackRadius : section.middle + direction * (reach + half);
        }

        /// <summary>The section's middle: the mean of its corners, always inside it.</summary>
        public Vector2 Middle => middle;

        /// <summary>How far the outline is from <see cref="Middle"/> along unit <paramref name="direction"/>; infinite if the ray finds no edge.</summary>
        public float Reach(Vector2 direction)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 a = hull[i] - middle;
                Vector2 edge = hull[(i + 1) % hull.Length] - hull[i];

                // middle + direction·t = hull[i] + edge·u, for 0 ≤ u ≤ 1 and t > 0.
                float denominator = direction.x * edge.y - direction.y * edge.x;
                if (Mathf.Abs(denominator) < 1e-9f) continue;

                float t = (a.x * edge.y - a.y * edge.x) / denominator;
                float u = (a.x * direction.y - a.y * direction.x) / denominator;
                if (t > 0f && u >= -1e-4f && u <= 1f + 1e-4f) nearest = Mathf.Min(nearest, t);
            }

            return nearest;
        }
    }

    /// <summary>
    /// A sample of the body's drawn surface that can be re-posed on demand: each sampled vertex
    /// keeps its bind position and its bone weights, and <see cref="Pose"/> skins just those —
    /// what <c>SkinnedMeshRenderer.BakeMesh</c> does for every vertex of every mesh, which on a
    /// Nomad is 340 000 of them and took a fifth of a second per tie.
    ///
    /// <para>
    /// <b>What counts as the body.</b> Every enabled skinned mesh under it — clothes, cape and
    /// boots are part of the outline a cord goes round — plus, for a robot built of rigid plates,
    /// every plate hanging from a bone that no skin is weighted to. Anything merely attached
    /// (<see cref="BodyAttachment"/>) is left out, and so is a rigid prop held by a skinned hand.
    /// </para>
    /// </summary>
    internal sealed class BodySurface
    {
        private struct Influence
        {
            public Transform Bone;
            public Matrix4x4 Bind;
        }

        private struct Vertex
        {
            public Vector3 Position;
            public int Bone0, Bone1, Bone2, Bone3;
            public float Weight0, Weight1, Weight2, Weight3;
            public Transform Owner;
        }

        private readonly List<Influence> influences = new List<Influence>();
        private readonly List<Vertex> sampled = new List<Vertex>();
        private readonly List<Vector3> posed = new List<Vector3>();
        private Matrix4x4[] skinning = System.Array.Empty<Matrix4x4>();

        private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        private readonly List<bool[]> dominant = new List<bool[]>();
        private readonly List<MeshFilter> plates = new List<MeshFilter>();
        private readonly HashSet<Transform> bones;
        private readonly HashSet<Transform> carrying = new HashSet<Transform>();

        /// <summary>
        /// The bones the body is built on: those weighted to its skin and those its plates hang
        /// from, or every bone it was given when it draws nothing. Never an IK target or a pole.
        /// </summary>
        public Transform[] Skeleton { get; }

        public int Count => sampled.Count;
        public Vector3 PointOf(int i) => posed[i];
        public Transform OwnerOf(int i) => sampled[i].Owner;

        public BodySurface(Transform root, Transform[] given)
        {
            bones = new HashSet<Transform>();
            foreach (Transform bone in given)
                if (bone != null) bones.Add(bone);

            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || skin.sharedMesh == null || BodyAttachment.Covers(skin.transform, root)) continue;
                if (!Readable(skin.sharedMesh)) continue;

                skins.Add(skin);

                // A bone carries the body if it is the heaviest weight on any vertex. Marked per bone
                // index and looked up once each: a Nomad has a million weights and sixty-five bones.
                Transform[] skinBones = skin.bones;
                var dominates = new bool[skinBones.Length];
                foreach (int bone in WeightingOf(skin.sharedMesh).Dominant)
                    if (bone >= 0 && bone < dominates.Length) dominates[bone] = true;

                for (int b = 0; b < skinBones.Length; b++)
                    if (dominates[b] && skinBones[b] != null && bones.Contains(skinBones[b])) carrying.Add(skinBones[b]);
                dominant.Add(dominates);
            }

            // A plate is part of the body only where no skin is: on a skinned hand, the same
            // shape is a staff being carried.
            foreach (MeshFilter plate in root.GetComponentsInChildren<MeshFilter>())
            {
                Transform hangs = plate.transform.parent;
                MeshRenderer drawn = plate.GetComponent<MeshRenderer>();
                if (hangs == null || hangs == root || drawn == null || !drawn.enabled || plate.sharedMesh == null) continue;
                if (BodyAttachment.Covers(plate.transform, root) || carrying.Contains(hangs) || !Readable(plate.sharedMesh)) continue;
                plates.Add(plate);
            }

            foreach (MeshFilter plate in plates)
            {
                bones.Add(plate.transform.parent);
                carrying.Add(plate.transform.parent);
            }

            Skeleton = new List<Transform>(carrying.Count > 0 ? carrying : bones).ToArray();
        }

        /// <summary>Was <paramref name="bone"/> handed in as one of the body's bones, or does a plate of it hang there?</summary>
        public bool IsBone(Transform bone) => bone != null && bones.Contains(bone);

        /// <summary>
        /// Pick the vertices to measure: those owned by some <paramref name="parts"/> entry, at most
        /// <see cref="MaxPointsPerBone"/> per owning bone, spread evenly through each. Per bone, not
        /// per coil: the middle of a Dunehorn is "everything but its legs", and a budget for that
        /// spent most of itself on the head and the tail, leaving a dozen points round a barrel
        /// 2 m across — a hull whose chords ran the rope deep inside it.
        /// </summary>
        public void Sample(System.Func<Transform, bool>[] parts)
        {
            var slots = new Dictionary<Transform, int>();
            var counts = new List<int>();
            var seen = new List<int>();

            int SlotOf(Transform owner)
            {
                if (slots.TryGetValue(owner, out int slot)) return slot;
                slots.Add(owner, counts.Count);
                counts.Add(0);
                seen.Add(0);
                return counts.Count - 1;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < skins.Count; k++)
                {
                    SkinnedMeshRenderer skin = skins[k];
                    UnityEngine.Mesh mesh = skin.sharedMesh;
                    Transform[] skinBones = skin.bones;

                    // Per bone, not per vertex: a Nomad is 340 000 vertices and a handful of bones.
                    var ownerOf = new Transform[skinBones.Length];
                    var slotOf = new int[skinBones.Length];
                    bool measured = false;
                    for (int b = 0; b < skinBones.Length; b++)
                    {
                        ownerOf[b] = skinBones[b] != null ? OwnerAmong(skinBones[b], carrying) : null;
                        slotOf[b] = dominant[k][b] && PartOf(ownerOf[b], parts) >= 0 ? SlotOf(ownerOf[b]) : -1;
                        measured |= slotOf[b] >= 0;
                    }

                    // A boot or a helmet no coil goes round is never read at all — judged by the bones that
                // actually carry its vertices, since most meshes list the whole skeleton.
                    if (!measured) continue;

                    int first = influences.Count;
                    if (pass == 1)
                    {
                        Matrix4x4[] binds = mesh.bindposes;
                        for (int b = 0; b < skinBones.Length; b++)
                            influences.Add(new Influence { Bone = skinBones[b], Bind = b < binds.Length ? binds[b] : Matrix4x4.identity });
                        mesh.GetVertices(Vertices);
                    }

                    Weighting weighting = WeightingOf(mesh);
                    NativeArray<BoneWeight1> weights = pass == 1 ? mesh.GetAllBoneWeights() : default;

                    for (int v = 0; v < weighting.Dominant.Length; v++)
                    {
                        int dominant = weighting.Dominant[v];
                        if (dominant < 0 || dominant >= skinBones.Length) continue;

                        int slot = slotOf[dominant];
                        if (slot < 0) continue;

                        if (pass == 0)
                        {
                            counts[slot]++;
                            continue;
                        }

                        if (seen[slot]++ % Stride(counts[slot]) != 0) continue;

                        var vertex = new Vertex { Position = Vertices[v], Owner = ownerOf[dominant], Bone0 = -1, Bone1 = -1, Bone2 = -1, Bone3 = -1 };
                        int start = weighting.First[v], n = weighting.First[v + 1] - start;
                        for (int w = 0; w < n && w < 4; w++)
                        {
                            int index = first + weights[start + w].boneIndex;
                            float weight = weights[start + w].weight;
                            switch (w)
                            {
                                case 0: vertex.Bone0 = index; vertex.Weight0 = weight; break;
                                case 1: vertex.Bone1 = index; vertex.Weight1 = weight; break;
                                case 2: vertex.Bone2 = index; vertex.Weight2 = weight; break;
                                default: vertex.Bone3 = index; vertex.Weight3 = weight; break;
                            }
                        }

                        sampled.Add(vertex);
                    }
                }

                foreach (MeshFilter plate in plates)
                {
                    Transform owner = OwnerAmong(plate.transform, carrying);
                    if (PartOf(owner, parts) < 0) continue;

                    int slot = SlotOf(owner);
                    plate.sharedMesh.GetVertices(Vertices);
                    if (pass == 0)
                    {
                        counts[slot] += Vertices.Count;
                        continue;
                    }

                    int index = influences.Count;
                    influences.Add(new Influence { Bone = plate.transform, Bind = Matrix4x4.identity });
                    foreach (Vector3 position in Vertices)
                    {
                        if (seen[slot]++ % Stride(counts[slot]) != 0) continue;
                        sampled.Add(new Vertex { Position = position, Owner = owner, Bone0 = index, Weight0 = 1f,
                                                 Bone1 = -1, Bone2 = -1, Bone3 = -1 });
                    }
                }
            }
        }

        /// <summary>
        /// Each vertex's heaviest bone, and where its weights start, per mesh: read once a session. Walking
        /// a Nomad's million bone weights through the native arrays twice a measure cost a sixth of a second;
        /// the plain arrays cost a couple of milliseconds. Keyed by the shared mesh asset, so every Nomad shares one.
        /// </summary>
        private sealed class Weighting
        {
            public int[] Dominant;
            public int[] First;
        }

        private static readonly Dictionary<UnityEngine.Mesh, Weighting> Weightings = new Dictionary<UnityEngine.Mesh, Weighting>();

        private static Weighting WeightingOf(UnityEngine.Mesh mesh)
        {
            if (Weightings.TryGetValue(mesh, out Weighting known)) return known;

            NativeArray<byte> perVertex = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            var weighting = new Weighting { Dominant = new int[perVertex.Length], First = new int[perVertex.Length + 1] };

            // Unity keeps a vertex's weights heaviest first.
            for (int v = 0, at = 0; v < perVertex.Length; v++)
            {
                weighting.First[v] = at;
                weighting.Dominant[v] = perVertex[v] > 0 ? weights[at].boneIndex : -1;
                at += perVertex[v];
                weighting.First[v + 1] = at;
            }

            Weightings.Add(mesh, weighting);
            return weighting;
        }

        /// <summary>Reused across measures: a Nomad's suit alone is 90 000 vertices, and growing a fresh list for it cost more than reading it.</summary>
        private static readonly List<Vector3> Vertices = new List<Vector3>();

        private static int Stride(int count) => Mathf.Max(1, Mathf.CeilToInt(count / (float)MaxPointsPerBone));

        private static int PartOf(Transform owner, System.Func<Transform, bool>[] parts)
        {
            if (owner == null) return -1;
            for (int p = 0; p < parts.Length; p++)
                if (parts[p](owner)) return p;
            return -1;
        }

        /// <summary>Where every sampled vertex is now, skinned exactly as the renderer skins it (blend shapes aside).</summary>
        public void Pose()
        {
            if (skinning.Length != influences.Count) skinning = new Matrix4x4[influences.Count];
            for (int i = 0; i < influences.Count; i++)
                skinning[i] = influences[i].Bone != null ? influences[i].Bone.localToWorldMatrix * influences[i].Bind : Matrix4x4.zero;

            posed.Clear();
            foreach (Vertex v in sampled)
            {
                Vector3 point = skinning[v.Bone0].MultiplyPoint3x4(v.Position) * v.Weight0;
                if (v.Bone1 >= 0) point += skinning[v.Bone1].MultiplyPoint3x4(v.Position) * v.Weight1;
                if (v.Bone2 >= 0) point += skinning[v.Bone2].MultiplyPoint3x4(v.Position) * v.Weight2;
                if (v.Bone3 >= 0) point += skinning[v.Bone3].MultiplyPoint3x4(v.Position) * v.Weight3;

                // Only the four heaviest weights are kept, so they are renormalised to a whole.
                float total = v.Weight0 + v.Weight1 + v.Weight2 + v.Weight3;
                posed.Add(total > 1e-6f ? point / total : point);
            }
        }

        /// <summary>
        /// A mesh the rope may read. A player build refuses to read one imported without Read/Write, so the editor is
        /// made to refuse it too — a rope that measures in the editor and floats in a build would be found by players.
        /// </summary>
        private static bool Readable(UnityEngine.Mesh mesh)
        {
            if (mesh.isReadable) return true;
            if (Unreadable.Add(mesh))
                Debug.LogWarning($"[SkinCoil] '{mesh.name}' is not Read/Write enabled, so a rope cannot be laid on it; " +
                                 "ropes go round the rest of the body. Enable Read/Write on its import.", mesh);
            return false;
        }

        private static readonly HashSet<UnityEngine.Mesh> Unreadable = new HashSet<UnityEngine.Mesh>();

        /// <summary>The closest of <paramref name="node"/> and its ancestors that is one of <paramref name="among"/>.</summary>
        private static Transform OwnerAmong(Transform node, HashSet<Transform> among)
        {
            for (Transform up = node; up != null; up = up.parent)
                if (among.Contains(up)) return up;

            return null;
        }

        /// <summary>
        /// Vertices measured per bone. A forearm's or a spine bone's share of the skin is a few hundred
        /// to a few thousand; this keeps enough of each for every slice of a turn's outline to find a
        /// point, and a Nomad's 340 000 vertices down to a few thousand skinned per measure.
        /// </summary>
        private const int MaxPointsPerBone = 400;
    }
}
