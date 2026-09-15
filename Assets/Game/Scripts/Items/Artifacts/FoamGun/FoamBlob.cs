// One lump of foam standing in the world.
//
// It is a SPAWNED NETWORK OBJECT rather than a Present() visual, and that is the whole reason it
// costs a prefab registration: unlike a projectile, everybody has to collide with the same lump.
// A ramp one player builds is a ramp every player climbs.
//
// EVERY MACHINE RUNS THE SAME CLOCK OFF THE SAME TWO NUMBERS. The server stamps when the blob was
// laid and how long it has, both on the shared server clock, and each machine derives its growth,
// its dissolve and its silhouette from them. A late joiner receives the pair with the spawn and
// picks the blob up half grown and half dissolved, exactly where everyone else has it — which is
// what a locally started clock could not do, because it would re-grow a fifty-second-old ramp from
// nothing the moment somebody walked in the door.
//
// IT IS NOT SAVED, and that is load-bearing rather than an omission. Every blob dies inside a
// minute, so a save taken mid-spray loads a world with no foam in it. The prefab therefore must NOT
// carry a non-kinematic Rigidbody, a HealthComponent, a PickupableItem, a NavMeshAgent or a
// SceneTracked: SaveablePolicy.EnsureSpawned reads exactly those, and any one of them would give
// this object a SaveableEntity with no stamped prefab id — captured faithfully into every save file
// and dropped with a warning on every load.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A dab of foam: swells into a sphere you can stand on, welds with its neighbours through
    /// <see cref="FoamField"/>, and dissolves when its clock runs out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoamBlob : NetworkBehaviour
    {
        [Header("Shape")]
        [Tooltip("Radius of a fully grown blob, in metres. The mesh is a UNIT sphere, so this is " +
                 "the transform scale and nothing else — the collider grows with it because it is " +
                 "the same number, which is the only reason the shape you see and the shape you " +
                 "stand on cannot drift apart.")]
        [SerializeField, Min(0.01f)] private float radius = 1.3f;

        [Tooltip("How much a blob may fall short of its full radius, as a share of it. DOWNWARD " +
                 "ONLY, and that is not a style choice: the gun measures its catch sweep off the " +
                 "FULL radius, so a blob allowed to grow past it would encase bodies the sweep " +
                 "never looked at. Without this a mass reads as a row of identical balls no " +
                 "matter how well the surface welds.")]
        [SerializeField, Range(0f, 0.5f)] private float radiusVariance = 0.36f;

        [Tooltip("How far this blob may sit off the shading ladder its lighting alone would put " +
                 "it on, pushed into the material as _ShadeBias. This is the colour variation: " +
                 "the four bands are one column of the palette, so a nudged blob lands on a " +
                 "different one of those four entries and cannot walk off the column the way a " +
                 "tint would.")]
        [SerializeField, Range(0f, 0.5f)] private float shadeVariance = 0.22f;

        [Tooltip("How far a lump may be squashed on its two shorter axes, as a share of its " +
                 "radius. This is what stops the mass reading as a pile of balls: every lump is a " +
                 "differently proportioned, differently tumbled ellipsoid, and the shader bends " +
                 "the silhouette further on top of it. The LONGEST axis always stays at the full " +
                 "radius — see AxisScale for why that cap is load-bearing rather than tidy.")]
        [SerializeField, Range(0f, 0.5f)] private float shapeSquash = 0.38f;

        [Tooltip("Seconds the blob takes to swell to full size AND to go hard after it lands. It " +
                 "grows rather than appearing, so foam sprayed at your own feet pushes you out " +
                 "gently instead of launching you — and until the clock runs out the lump is wet: " +
                 "it is not solid, nothing stands on it, and it reads a shade darker. Spraying a " +
                 "ramp is therefore something you do BEFORE you need to climb it.")]
        [SerializeField, Min(0f)] private float growSeconds = 3f;

        [Tooltip("How far down the shading ladder a lump sits while it is still wet, on top of its " +
                 "own shade bias. This is the only tell that foam is not yet solid, so it is not " +
                 "decoration: a player who cannot see which part of a mound has set will step onto " +
                 "one that has not.")]
        [SerializeField, Range(0f, 0.5f)] private float wetShade = 0.2f;

        [Tooltip("How big a blob is before its foam has arrived, as a share of its full radius. " +
                 "Not zero: a SphereCollider at zero scale is degenerate, and a blob that spent a " +
                 "frame as one would be a lump nothing could stand on. Kept small, because the " +
                 "blob sits at this size for the whole of the spray's flight time — see Begin.")]
        [SerializeField, Range(0.01f, 0.5f)] private float birthRadiusShare = 0.06f;

        [Header("Expiry")]
        [Tooltip("Seconds of dissolve at the end of the blob's life. The shader eats it away from " +
                 "its own bubbles outward over this, so it pops apart instead of blinking out.")]
        [SerializeField, Min(0.05f)] private float dissolveSeconds = 0.8f;

        [Header("Bodies")]
        [Tooltip("What the blob looks for when it lands, to avoid shoving whatever it landed on.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Tooltip("The sphere. Found in children when unset; it is what the dissolve is painted on.")]
        [SerializeField] private Renderer surface;

        [Tooltip("The lump's own collider, so a body already inside it at birth can be excused " +
                 "from colliding with it. Found on this object when unset.")]
        [SerializeField] private Collider shell;

        /// <summary>Scratch for the birth-time body sweep. Nothing is held between calls.</summary>
        private static readonly Collider[] Touching = new Collider[16];

        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int ShadeBiasId = Shader.PropertyToID("_ShadeBias");

        /// <summary>
        /// When this blob was laid, on the shared server clock, and how long it has. Replicated
        /// rather than local, for the late-joiner reason in the file header. Server-write because
        /// the expiry is the server's — see the design doc.
        /// </summary>
        private readonly NetworkVariable<double> bornAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> lifetime = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock dissolveBlock;
        private float drawnDissolve = -1f;
        private float drawnBias = float.NaN;

        /// <summary>Has this lump been given its one permanent pose? See <see cref="Tumble"/>.</summary>
        private bool tumbled;

        /// <summary>
        /// Who sprayed this, on the machine that decided to. Deliberately not replicated: the only
        /// question it answers is which of a player's blobs to retire when they exceed their live
        /// budget, and that is the server's question alone.
        /// </summary>
        public GameObject Sprayer { get; private set; }

        /// <summary>The sphere's centre in world space — the dab's placement point.</summary>
        public Vector3 Centre => transform.position;

        /// <summary>The lump's longest half-extent right now, in metres.</summary>
        public float Radius { get; private set; }

        /// <summary>
        /// The radius the analytic weld field should union this lump as.
        ///
        /// <para>
        /// The MEAN of its three half-extents, not the longest. FoamSurface welds neighbours by
        /// unioning spheres of the radius it is given, and a squashed lump is not that sphere —
        /// handing it the longest extent would inflate the field past the geometry on two axes out
        /// of three and grow a fillet welded onto empty air. The mean is the sphere that best fits
        /// the lump, and the residual mismatch is exactly the price of a silhouette that is not a
        /// ball (see the Gotchas in FoamGun.md).
        /// </para>
        /// </summary>
        public float FieldRadius
        {
            get
            {
                Vector3 axes = AxisScale;
                return Radius * (axes.x + axes.y + axes.z) / 3f;
            }
        }

        /// <summary>
        /// How big one of these ends up. Read off the prefab by the gun, so the sweep that decides
        /// who a dab encases and the lump that dab becomes are the same number.
        /// </summary>
        public float FullRadius => radius;

        /// <summary>
        /// The longest half-extent this lump ends up at — its own draw of
        /// <see cref="FullRadius"/>, not the prefab's figure.
        ///
        /// <para>
        /// This is the reach a spray arc has to be tested against rather than
        /// <see cref="Radius"/>, which is a twentieth of it for the whole of the lump's flight
        /// and under half of it for the first second and a half of the swell. See
        /// <see cref="TryHitCommitted"/>.
        /// </para>
        /// </summary>
        public float CommittedRadius => radius * Variance;

        /// <summary>
        /// Where a straight chord first meets the volume this lump has COMMITTED to fill, as
        /// opposed to the volume its collider occupies right now.
        ///
        /// <para>
        /// The two are the same only at the end of the swell. A dab is spawned at
        /// <see cref="birthRadiusShare"/> of its size and stays there until its foam has flown,
        /// then takes <see cref="growSeconds"/> to reach full — so at the design's fifteen dabs a
        /// second, the first twenty of a held spray are all laid while every lump before them is
        /// still a pebble. Traced against the colliders, every one of those passes through the
        /// mass and lands on the ground: the gun spreads a carpet where the player was building a
        /// pile. Tested against the committed volume it stacks from the first dab, which is what
        /// the foam is for (<c>GDC-L1-FEEL-0007</c> — the sensation is the target, and the
        /// collider's honest current size is the physical accuracy that fights it).
        /// </para>
        /// <para>
        /// The test is against the ELLIPSOID, not a sphere of <see cref="CommittedRadius"/>. A
        /// lump is squashed on two axes by up to 38 %, so the sphere would stand up to a third of
        /// a metre proud of the geometry on those axes and leave a dab hanging in the air beside
        /// the mass it was meant to land on.
        /// </para>
        /// <para>
        /// A chord that STARTS inside the volume reports nothing, which is what
        /// <c>Physics.Raycast</c> does with a collider it starts inside. Without it a player who
        /// has buried their own muzzle in foam could not spray back out of it.
        /// </para>
        /// </summary>
        /// <param name="distance">How far along the chord from <paramref name="from"/>, in metres.</param>
        public bool TryHitCommitted(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal,
                                    out float distance)
        {
            point = default;
            normal = Vector3.up;
            distance = 0f;

            Vector3 extents = AxisScale * CommittedRadius;
            if (extents.x <= 1e-4f || extents.y <= 1e-4f || extents.z <= 1e-4f) return false;

            // Into the lump's own frame and then onto the unit sphere, where a ray/ellipsoid
            // intersection is an ordinary quadratic. The pose is Tumble rather than
            // transform.rotation because a lump that has not been stamped yet has not been turned
            // to it — and the arc has to agree with the shape the lump is going to hold.
            Quaternion inverse = Quaternion.Inverse(Tumble);
            Vector3 origin = inverse * (from - Centre);
            Vector3 leg = inverse * (to - from);

            Vector3 o = new Vector3(origin.x / extents.x, origin.y / extents.y, origin.z / extents.z);
            Vector3 d = new Vector3(leg.x / extents.x, leg.y / extents.y, leg.z / extents.z);

            float a = Vector3.Dot(d, d);
            if (a < 1e-12f) return false;

            float c = Vector3.Dot(o, o) - 1f;
            if (c <= 0f) return false;

            float b = 2f * Vector3.Dot(o, d);
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;

            // The near root. The chord is parameterised 0..1 over its own length, so anything
            // outside that is the ellipsoid sitting off the end of this chord rather than on it.
            float t = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            if (t < 0f || t > 1f) return false;

            // The outward normal is the ellipsoid's gradient, which is the unit-sphere point
            // divided by the extents again — the same squash applied a second time, not undone.
            Vector3 surfacePoint = o + d * t;
            Vector3 gradient = new Vector3(surfacePoint.x / extents.x, surfacePoint.y / extents.y,
                                           surfacePoint.z / extents.z);
            if (gradient.sqrMagnitude < 1e-12f) return false;

            point = from + (to - from) * t;
            normal = (Tumble * gradient).normalized;
            distance = Vector3.Distance(from, to) * t;
            return true;
        }

        /// <summary>
        /// Push <paramref name="point"/> out to <paramref name="clearance"/> outside the volume
        /// this lump has COMMITTED to fill, and say whether it was inside at all.
        ///
        /// <para>
        /// This is the other half of <see cref="FoamSettle"/>: falling is what carries a dab down,
        /// and being ejected from the lumps it lands on is what carries it OUT. Both are needed —
        /// gravity alone drops a dab through the mound, and the push alone piles it into the same
        /// leaning column the arc already built.
        /// </para>
        /// <para>
        /// It is the ellipsoid, in the lump's own frame, for the reason <see cref="TryHitCommitted"/>
        /// gives: a sphere of <see cref="CommittedRadius"/> stands a third of a metre proud of a
        /// squashed lump on two axes, and a settle relaxing against that would leave a visible gap
        /// around every flattened lump in the mass.
        /// </para>
        /// <para>
        /// The push is along the ellipsoid's own outward direction rather than along the shortest
        /// way out, which is the standard cheap approximation and is the RIGHT one here: the exact
        /// nearest point on an ellipsoid is an iterative solve, and this runs a few hundred times a
        /// second inside another iteration.
        /// </para>
        /// </summary>
        public bool PushOutOfCommitted(Vector3 point, float clearance, out Vector3 pushed)
        {
            pushed = point;

            Vector3 extents = AxisScale * CommittedRadius + Vector3.one * Mathf.Max(0f, clearance);
            if (extents.x <= 1e-4f || extents.y <= 1e-4f || extents.z <= 1e-4f) return false;

            Quaternion inverse = Quaternion.Inverse(Tumble);
            Vector3 local = inverse * (point - Centre);

            Vector3 unit = new Vector3(local.x / extents.x, local.y / extents.y, local.z / extents.z);

            float length = unit.magnitude;
            if (length >= 1f) return false;

            // Dead centre, which a cluster of dabs sharing one landing point can genuinely hit.
            // Up is the only direction that cannot bury the lump in whatever it landed on; the
            // fall on the next step is what turns that into a slide rather than a stack.
            if (length < 1e-4f)
            {
                pushed = Centre + Tumble * new Vector3(0f, extents.y, 0f);
                return true;
            }

            unit /= length;
            pushed = Centre + Tumble * new Vector3(unit.x * extents.x, unit.y * extents.y,
                                                   unit.z * extents.z);
            return true;
        }

        /// <summary>Seconds since this blob was laid, on whatever clock both machines share.</summary>
        private float Age => Mathf.Max(0f, (float)(Now - bornAt.Value));

        /// <summary>
        /// How far this lump has SET, 0 the instant its foam lands and 1 once it is hard.
        ///
        /// <para>
        /// It shares <see cref="growSeconds"/> with the swell on purpose rather than carrying a
        /// clock of its own: the lump is wet exactly as long as it is still moving, which is what
        /// makes "wait for it to go hard" a thing a player can read off the shape rather than a
        /// hidden timer (<c>GDC-L1-SYS-0006</c>).
        /// </para>
        /// </summary>
        private float Hardness => growSeconds <= 0f ? 1f : Mathf.Clamp01(Age / growSeconds);

        /// <summary>
        /// The clock every machine measures this blob against. The server's, when there is one:
        /// two machines drawing the same lump have to agree about how old it is, and their own
        /// <c>Time.time</c> values started whenever each of them did.
        /// </summary>
        private static double Now =>
            Network.IsNetworked && NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.timeAsDouble;

        /// <summary>
        /// Start this blob's life. Server-side, immediately after the spawn.
        /// </summary>
        /// <param name="sprayer">Whose budget this counts against, and who is blamed for a hold.</param>
        /// <param name="seconds">
        /// How long it stands: the design's sixty on the ground, ten on a body. A setback rather
        /// than a sentence for whoever is inside it, while a ramp is still there when they climb
        /// back down (GDC-L1-BAL-0004).
        /// </param>
        /// <param name="travelSeconds">
        /// How long the sprayed foam takes to FLY from the muzzle to this landing point.
        ///
        /// <para>
        /// The lump is spawned the instant the trigger tick is authorised, but the foam the player
        /// can see is still crossing the room — so without this a mass swells up at the far wall
        /// before any spray has reached it, and the jet reads as decoration painted over an effect
        /// that had already happened. Stamping the clock in the FUTURE holds the blob at its birth
        /// size until the foam arrives, because <see cref="Age"/> clamps at zero. It then grows
        /// from there, which is what makes the mass look like it was deposited by the jet.
        /// </para>
        /// <para>
        /// The delay costs no extra replication: <see cref="bornAt"/> already crosses the wire, so
        /// a late joiner picks up a blob still in flight exactly where everyone else has it.
        /// </para>
        /// </param>
        public void Begin(GameObject sprayer, float seconds, float travelSeconds = 0f)
        {
            Sprayer = sprayer;

            bornAt.Value = Now + Mathf.Max(0f, travelSeconds);
            lifetime.Value = Mathf.Max(dissolveSeconds, seconds);

            // The pose is already right, but the size is not: without this the blob spends its
            // first frame at the prefab's authored scale, which is a fully grown sphere appearing
            // inside whatever it just landed on.
            ApplyShape();
        }

        private void OnEnable()
        {
            if (surface == null) surface = GetComponentInChildren<Renderer>(true);
            if (shell == null) shell = GetComponent<Collider>();

            // Wet foam is not solid, so the shell starts OFF and is switched on by ApplyShape when
            // the lump goes hard. The prefab ships it enabled because that is what it is for the
            // rest of the lump's life; a blob is only ever wet in the first seconds of it.
            if (shell != null) shell.enabled = false;

            ApplyShape();

            FoamField.Register(this);
        }

        private void OnDisable() => FoamField.Unregister(this);

        /// <summary>
        /// Take this blob out of the world now, because whoever sprayed it has laid too many.
        /// Server-side, like the expiry it stands in for.
        ///
        /// <para>
        /// It leaves the field BEFORE the despawn rather than waiting for its own OnDisable.
        /// <c>Object.Destroy</c> is deferred to the end of the frame, so a caller retiring several
        /// lumps in a row would otherwise keep finding the ones it had already retired — and, being
        /// a while loop over a count that never falls, would never come back.
        /// </para>
        /// </summary>
        public void Retire()
        {
            FoamField.Unregister(this);
            GameServices.World.Despawn(gameObject);
        }

        private void Update()
        {
            ApplyShape();

            // Expiry is the server's, on the blob itself, so one machine decides and the despawn
            // reaches the rest as an ordinary spawn message. Every peer has already drawn the
            // dissolve to the end by the time it arrives.
            if (!Network.Simulates(this)) return;
            if (lifetime.Value <= 0f || Age < lifetime.Value) return;

            GameServices.World.Despawn(gameObject);
        }

        /// <summary>
        /// Size and dissolve, both derived from the age rather than stored, so a machine that
        /// joined halfway through picks the blob up where everyone else has it.
        /// </summary>
        private void ApplyShape()
        {
            float total = lifetime.Value;

            // Nobody has stamped this blob yet: it was instantiated a moment ago and either the
            // server has not reached Begin or the client has not unpacked the spawn payload. Held
            // at its birth size rather than derived from an unstamped clock, which would read as
            // an infinitely old blob — a fully grown sphere, fully dissolved, for a frame.
            if (total <= 0f)
            {
                Radius = radius * birthRadiusShare;
                transform.localScale = Vector3.one * Radius;
                PaintDissolve(0f);
                return;
            }


            float age = Age;
            float full = radius * Variance;

            float grown = growSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, age / growSeconds);
            Radius = Mathf.Lerp(full * birthRadiusShare, full, grown);

            // Non-uniform, and tumbled. Radius stays the LONGEST half-extent, so everything that
            // reasons about this lump from the outside — the catch sweep, the budget's sort, the
            // weld field — keeps working off one honest number.
            transform.localScale = AxisScale * Radius;

            // The pose is fixed for the blob's life, so it is written once the stamp that seeds it
            // has arrived rather than every frame.
            if (!tumbled && lifetime.Value > 0f)
            {
                transform.rotation = Tumble;
                tumbled = true;
            }

            ApplyHardness();

            PaintDissolve(Mathf.Clamp01((age - (total - dissolveSeconds)) / dissolveSeconds));
        }

        /// <summary>
        /// Solid or not, off the same clock as the swell.
        ///
        /// <para>
        /// The shell is switched rather than made a trigger, which is the opposite of what
        /// <see cref="ExcuseBodiesAlreadyInside"/> does and for the opposite reason: that excuses
        /// ONE body from a lump everybody else still collides with, whereas wet foam is not solid
        /// to anyone. Dropping out of every raycast and overlap while wet is correct here — nothing
        /// should be able to stand on, shoot or seat itself against foam that has not set. The one
        /// thing that must still see a wet lump is the spray arc, and it never used the collider:
        /// it goes through <see cref="TryHitCommitted"/>, so a mound keeps building on itself while
        /// every part of it is still soft.
        /// </para>
        /// <para>
        /// The bodies standing in it are excused at the moment it hardens rather than at birth,
        /// because that is now the moment a solid sphere appears around a capsule — the launch the
        /// design flagged. A player who walked in while it was wet is caught by the same sweep as
        /// one who was there when it landed.
        /// </para>
        /// </summary>
        private void ApplyHardness()
        {
            if (shell == null) return;

            bool hard = Hardness >= 1f;
            if (shell.enabled == hard) return;

            shell.enabled = hard;

            // Physics.IgnoreCollision is cleared when a collider is disabled and re-enabled, so
            // this is asked after the switch rather than kept from an earlier call.
            if (hard) ExcuseBodiesAlreadyInside();
        }





        /// <summary>
        /// One deterministic 0..1 draw for this blob, <paramref name="salt"/> picking which.
        ///
        /// <para>
        /// Seeded from <see cref="bornAt"/> and the object id rather than from the spawn position
        /// or from <c>Random</c>, because every machine has to derive the SAME lump. The position
        /// is not trustworthy when a client's copy first enables — it holds the prefab's pose until
        /// the spawn payload is unpacked, and a blob that changed shape a frame later would pop.
        /// The stamp arrives with that payload, and until it does <see cref="ApplyShape"/> is on
        /// its unstamped branch anyway.
        /// </para>
        /// <para>
        /// The id is asked only of a SPAWNED object: <c>NetworkBehaviour.NetworkObjectId</c>
        /// reaches through a NetworkObject that logs an error of its own when there is not one yet.
        /// It is the tie-break for the several dabs that share one server tick at this dab rate.
        /// </para>
        /// </summary>
        private float Draw(int salt)
        {
            ulong id = IsSpawned ? NetworkObjectId : 0UL;

            long bits = System.BitConverter.DoubleToInt64Bits(bornAt.Value)
                        ^ (long)(id * 2654435761UL)
                        ^ ((long)salt * 40503L);

            return ((bits ^ (bits >> 17)) & 0xFFFF) / 65535f;
        }

        /// <summary>How far short of its full radius this blob falls, as a share of it.</summary>
        private float Variance => radiusVariance <= 0f ? 1f : 1f - radiusVariance * Draw(11);

        /// <summary>
        /// How far this blob sits off the shading ladder its lighting alone would give it: its own
        /// permanent draw, plus however wet it still is.
        ///
        /// <para>
        /// The wet term is QUANTIZED to eighths, and that is a cost decision rather than a look
        /// one. <see cref="PaintDissolve"/> skips the write when nothing has moved, and a term
        /// sliding continuously for three seconds would defeat that for every lump laid in the last
        /// three seconds — which at this dab rate is most of them. Eight steps is finer than the
        /// four bands the scalar lands on anyway.
        /// </para>
        /// </summary>
        private float ShadeBias
        {
            get
            {
                float own = shadeVariance <= 0f ? 0f : (Draw(5) - 0.5f) * 2f * shadeVariance;
                if (wetShade <= 0f) return own;

                return own - wetShade * (1f - Mathf.Round(Hardness * 8f) / 8f);
            }
        }

        /// <summary>
        /// The lump's proportions: how far it is squashed per axis, LONGEST AXIS EXACTLY 1.
        ///
        /// <para>
        /// That cap is not cosmetic. The gun sweeps <c>FullRadius + catchMargin</c> for bodies to
        /// encase, and <see cref="FullRadius"/> is read off the prefab and cannot know a per-blob
        /// shape — so a lump allowed to stretch past its radius on any axis would reach further
        /// than the sweep that decided who was inside it. Squashing only keeps every existing
        /// guarantee true. It costs volume, which is bought back on the radius instead.
        /// </para>
        /// <para>
        /// WHICH axis stays long is drawn too. A hundred ellipsoids all flattened the same way
        /// reads as a rendering artefact rather than as foam.
        /// </para>
        /// </summary>
        private Vector3 AxisScale
        {
            get
            {
                if (shapeSquash <= 0f) return Vector3.one;

                float a = 1f - shapeSquash * Draw(23);
                float b = 1f - shapeSquash * Draw(37);
                float pick = Draw(53);

                if (pick < 0.334f) return new Vector3(1f, a, b);
                return pick < 0.667f ? new Vector3(a, 1f, b) : new Vector3(a, b, 1f);
            }
        }

        /// <summary>
        /// The pose a squashed lump is laid at. Random, because the surface normal a dab landed on
        /// is of no use to a shape that is not a sphere — and because lumps all leaning one way is
        /// the most obvious tell there is.
        /// </summary>
        private Quaternion Tumble =>
            Quaternion.Euler(Draw(71) * 360f, Draw(89) * 360f, Draw(97) * 360f);

        /// <summary>
        /// Push the blob's own place in its life into the material.
        ///
        /// Through a property block, so the one shared foam material is untouched and twenty-four
        /// blobs do not become twenty-four material instances. Skipped when the value has not
        /// moved, because a property block write is not free and most frames of a blob's life do
        /// not change it.
        /// </summary>
        private void PaintDissolve(float dissolve)
        {
            if (surface == null) return;

            // The bias is part of the test, not just part of the write. It is derived from a
            // REPLICATED stamp, so on a client it is one value before the spawn payload arrives
            // and another after — and a guard on the dissolve alone would latch the first, leaving
            // every blob on a peer biased identically while the host's were varied.
            float bias = ShadeBias;
            if (Mathf.Approximately(dissolve, drawnDissolve) &&
                Mathf.Approximately(bias, drawnBias)) return;

            drawnDissolve = dissolve;
            drawnBias = bias;

            dissolveBlock ??= new MaterialPropertyBlock();
            surface.GetPropertyBlock(dissolveBlock);
            dissolveBlock.SetFloat(DissolveId, dissolve);

            // Written in the same block as the dissolve rather than once at birth, because a
            // property block is replaced wholesale by SetPropertyBlock: a bias set on its own
            // earlier would be dropped by the first dissolve write and the mass would go flat
            // exactly when it started expiring.
            dissolveBlock.SetFloat(ShadeBiasId, bias);

            surface.SetPropertyBlock(dissolveBlock);
        }

        /// <summary>
        /// Stop this blob colliding with anything that was already standing where it landed.
        ///
        /// <para>
        /// <c>Physics.IgnoreCollision</c> rather than a trigger or a disabled collider: the lump
        /// still has to be solid to everybody else, and a collider that is switched off drops out
        /// of every raycast, spherecast and overlap in the game as well as out of contacts.
        /// </para>
        /// </summary>
        private void ExcuseBodiesAlreadyInside()
        {
            if (shell == null) return;

            int count = Physics.OverlapSphereNonAlloc(Centre, radius, Touching, bodyMask,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider other = Touching[i];
                if (other == null || other == shell || other.transform.IsChildOf(transform)) continue;

                // Bodies only. A dune is not going to be launched by a blob, and excusing the
                // ground from collision would drop the lump straight through it.
                if (other.GetComponentInParent<HealthComponent>() == null) continue;

                Physics.IgnoreCollision(shell, other, true);
            }
        }

        /// <summary>
        /// Encase <paramref name="body"/>: hand it to <see cref="StatusKind.Foamed"/> and let that
        /// system answer "held in place", how long for, and what breaks it early.
        ///
        /// <para>
        /// Server-side, called by the gun at the moment the dab lands. Here rather than in the gun
        /// so that "what a blob does to a body" reads in one place with what a blob is.
        /// </para>
        /// </summary>
        public static bool Encase(GameObject body, GameObject sprayer)
        {
            StatusReceiver receiver = StatusReceiver.Ensure(body);
            if (receiver == null) return false;

            // No duration: the condition's own authored ten seconds is the number, not the gun's.
            // A sprayer does not get to decide how long being stuck lasts.
            receiver.Apply(StatusKind.Foamed, source: sprayer != null ? sprayer.transform : null);
            return true;
        }
    }
}
