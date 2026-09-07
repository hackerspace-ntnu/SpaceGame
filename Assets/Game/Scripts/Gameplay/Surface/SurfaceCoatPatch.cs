using UnityEngine;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// One coat, in the world: a point, a radius, a kind and a clock.
    ///
    /// <para>
    /// <b>A coat is a patch, not a flag on a collider.</b> Spraying a dune does not tag the
    /// terrain — nothing is written to the surface at all, so a coat works over terrain, a rock, a
    /// deck plate and a chunk that has not finished streaming in, and two patches can overlap
    /// without either of them having to know.
    /// </para>
    /// <para>
    /// <b>It is deliberately not a NetworkObject.</b> Position, radius, kind and expiry is the
    /// whole of it, which fits in one message; after that every machine runs the same clock over
    /// the same numbers and nothing else is ever sent. See <see cref="SurfaceCoatField"/>.
    /// </para>
    /// <para>
    /// The film is drawn by a DECAL: a box the shader reprojects onto whatever solid is behind each
    /// of its pixels, so the coat lies correctly on a dune, a boulder and a ramp without anything
    /// here knowing the shape of the ground. That is why the renderer is a plain cube and why its
    /// height is a tolerance rather than a thickness.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // created in code, never by hand
    public sealed class SurfaceCoatPatch : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private MaterialPropertyBlock block;
        private MeshRenderer film;
        private Transform slab;
        private float slabThickness;
        private float appliedFade = -1f;

        /// <summary>
        /// The server-minted id this patch is known by everywhere. What a
        /// <c>NetMsg.CoatBroken</c> refers to, and — because the server mints them in order — the
        /// tie-break for which of two overlapping patches is the newer, on every machine alike.
        /// </summary>
        public int Id { get; private set; }

        /// <summary>What kind of coat this is, and everything that follows from that.</summary>
        public SurfaceCoatBehaviour Coat { get; private set; }

        /// <summary>Shorthand for <c>Coat.Kind</c>.</summary>
        public SurfaceCoatKind Kind => Coat != null ? Coat.Kind : SurfaceCoatKind.Slick;

        /// <summary>Where it was sprayed, in world space.</summary>
        public Vector3 Center => transform.position;

        /// <summary>Its footprint on the ground, in metres.</summary>
        public float Radius { get; private set; }

        /// <summary>Seconds until it wears off. Meaningless while <see cref="IsPermanent"/>.</summary>
        public float SecondsLeft { get; private set; }

        /// <summary>True for a coat only a break can end — ice.</summary>
        public bool IsPermanent { get; private set; }

        /// <summary>Has this patch's clock run out?</summary>
        public bool Expired => !IsPermanent && SecondsLeft <= 0f;

        /// <summary>
        /// The chunk this patch belongs to, and whether it has one.
        ///
        /// <para>
        /// <b>Server-side only</b>, and set from the patch's CENTRE rather than from the ground its
        /// radius covers. That is the whole answer to a coat sprayed on the seam between two
        /// chunks: a circle straddling a boundary overlaps two chunks but has exactly one centre,
        /// so exactly one chunk owns it, so it is dropped once and saved once — the same rule every
        /// other placed thing in this world follows.
        /// </para>
        /// <para>
        /// False outside the streaming grid: an interior, the arena, a coat sprayed off the map.
        /// Nothing owns those, and no chunk unload takes them away.
        /// </para>
        /// </summary>
        public Vector2Int OwningChunk { get; private set; }

        /// <summary>See <see cref="OwningChunk"/>.</summary>
        public bool HasOwningChunk { get; private set; }

        /// <summary>
        /// The physics layer this patch's geometry was built on, kept so a restatement to a late
        /// joiner can say it again. Meaningless for a kind that carries no collider.
        /// </summary>
        public int ColliderLayer { get; private set; }

        /// <summary>
        /// Build a patch. <paramref name="seconds"/> of 0 or less is permanent;
        /// <paramref name="colliderLayer"/> is only consulted by a kind that carries one.
        /// </summary>
        public static SurfaceCoatPatch Create(SurfaceCoatBehaviour coat, int id, Vector3 centre,
                                              float radius, float seconds, Material filmMaterial,
                                              int colliderLayer, Transform parent)
        {
            if (coat == null) return null;

            var go = new GameObject($"Coat {coat.Kind} #{id}");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            var patch = go.AddComponent<SurfaceCoatPatch>();
            patch.Id = id;
            patch.Coat = coat;
            patch.ColliderLayer = colliderLayer;
            patch.Refresh(radius, seconds);

            if (filmMaterial != null) patch.BuildFilm(filmMaterial);
            coat.Build(patch, colliderLayer);

            return patch;
        }

        /// <summary>
        /// Give this patch a new footprint and a new clock — a second dab landing on top of the
        /// first, or a storm still raining on the ground it already wet.
        ///
        /// <para>
        /// The footprint only ever GROWS. A held spray wanders, and a refresh that took the newest
        /// dab's radius would shrink a patch the player has just spent three seconds widening.
        /// </para>
        /// </summary>
        public void Refresh(float radius, float seconds)
        {
            Radius = Mathf.Max(Radius, Mathf.Max(0.01f, radius));
            IsPermanent = seconds <= 0f;
            SecondsLeft = IsPermanent ? 0f : Mathf.Max(SecondsLeft, seconds);

            if (film != null) SizeFilm();

            // The footing has to grow with the film. A sheet of ice that a second spray widened
            // while its collider stayed the size of the first dab is ice a player can see and walk
            // straight through — the failure this whole system exists to avoid, in reverse.
            if (slab != null) SizeSlab();
        }

        /// <summary>Server-side: say which chunk owns this patch. See <see cref="OwningChunk"/>.</summary>
        public void SetOwningChunk(Vector2Int coord, bool owned)
        {
            OwningChunk = coord;
            HasOwningChunk = owned;
        }

        /// <summary>Run the clock down and fade the film out at the end of it.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsPermanent) SecondsLeft -= deltaTime;

            ApplyFade();
        }

        /// <summary>
        /// Is <paramref name="point"/> underfoot of this patch?
        ///
        /// A circle on the ground with a vertical tolerance, not a sphere: the coat lies on a
        /// surface, and what is being asked is whether the mover is standing on that surface — not
        /// how far away the sprayed point happens to be in three dimensions.
        /// </summary>
        public bool Covers(Vector3 point)
        {
            if (Coat == null) return false;

            Vector3 delta = point - Center;
            if (Mathf.Abs(delta.y) > Coat.VerticalReach) return false;

            delta.y = 0f;
            return delta.sqrMagnitude <= Radius * Radius;
        }

        /// <summary>
        /// Make this patch something that can be stood on — the ice sheet's collider.
        ///
        /// <para>
        /// A convex mesh collider on the cylinder's own mesh, not the capsule Unity's cylinder
        /// primitive ships with: a capsule's caps are hemispheres of the cylinder's radius, so a
        /// sheet a metre and a half across would bulge three quarters of a metre above the water it
        /// froze and read as a dome. It is also a disc rather than a box, so the ice a player can
        /// stand on is the ice they can see — a square slab under a round film puts invisible
        /// footing forty per cent past the edge at the corners.
        /// </para>
        /// <para>
        /// The slab's TOP sits on the sprayed point. Ice that stood proud of the pool it froze
        /// would be a step up onto the water.
        /// </para>
        /// </summary>
        public void AddSlab(float thickness, int layer)
        {
            if (slab != null) return;

            slabThickness = Mathf.Max(0.01f, thickness);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Ice";
            go.layer = layer;
            go.transform.SetParent(transform, false);

            Mesh mesh = go.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

            RemovePart(go.GetComponent<Collider>());

            // The film draws the ice; the slab is footing and nothing else. Left in, it would draw
            // an untextured white cylinder through the decal.
            RemovePart(go.GetComponent<MeshRenderer>());

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            slab = go.transform;
            SizeSlab();
        }

        private void BuildFilm(Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Film";
            box.transform.SetParent(transform, false);

            // The decal is drawn from inside, so the box must not be solid to anything. A collider
            // here would also be the one thing in a slick pool a player could trip over.
            RemovePart(box.GetComponent<Collider>());

            film = box.GetComponent<MeshRenderer>();
            film.sharedMaterial = material;
            film.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            film.receiveShadows = false;

            SizeFilm();
        }

        /// <summary>
        /// The decal volume: the footprint across, and the coat's own tolerance deep, so the film
        /// finds the ground whether the sprayed point landed a little above or a little below it.
        /// </summary>
        private void SizeFilm()
        {
            float diameter = Radius * 2f;
            film.transform.localScale = new Vector3(diameter, Coat.VerticalReach * 2f, diameter);
        }

        /// <summary>
        /// Unity's cylinder primitive is radius 0.5 and height 2, so a half-thickness on Y and a
        /// diameter on X and Z give a disc of <see cref="Radius"/> that is <c>slabThickness</c>
        /// deep — hanging below the sprayed plane, with its top face on it.
        /// </summary>
        private void SizeSlab()
        {
            float diameter = Radius * 2f;
            slab.localPosition = new Vector3(0f, -slabThickness * 0.5f, 0f);
            slab.localScale = new Vector3(diameter, slabThickness * 0.5f, diameter);
        }

        private void ApplyFade()
        {
            if (film == null) return;

            float fadeSeconds = Coat.FadeSeconds;
            float fade = IsPermanent || fadeSeconds <= 0f || SecondsLeft >= fadeSeconds
                ? 0f
                : Mathf.Clamp01(1f - SecondsLeft / fadeSeconds);

            // Written only when it moves. A world with fifty patches in it otherwise pushes fifty
            // property blocks a frame to say nothing changed.
            if (Mathf.Approximately(fade, appliedFade)) return;
            appliedFade = fade;

            block ??= new MaterialPropertyBlock();
            film.GetPropertyBlock(block);
            block.SetFloat(FadeId, fade);
            film.SetPropertyBlock(block);
        }

        /// <summary>
        /// Take a part off a primitive this class built, for good.
        ///
        /// <para>
        /// Switched off first, because <c>Destroy</c> is deferred to the end of the frame and the
        /// part is live until then — which is one physics step of a box collider a player can trip
        /// over inside a slick puddle, and one frame of an untextured white cylinder standing in
        /// the water.
        /// </para>
        /// <para>
        /// <c>DestroyImmediate</c> outside play mode, because <c>Destroy</c> is refused there and
        /// an EditMode test builds coats. The same shape <c>PackHose</c> uses for the same reason.
        /// </para>
        /// </summary>
        private static void RemovePart(Component part)
        {
            if (part == null) return;

            if (part is Collider collider) collider.enabled = false;
            else if (part is Renderer renderer) renderer.enabled = false;

            if (Application.isPlaying) Destroy(part);
            else DestroyImmediate(part);
        }
    }
}
