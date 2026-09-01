using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The net, and only the net.
    ///
    /// <para>
    /// A Verlet particle grid — the same position-based model <see cref="LassoRope"/> uses, with the
    /// chain generalised to a lattice. The reasoning for choosing it over the two obvious
    /// alternatives is worth keeping here, because both look better on paper. Unity's
    /// <c>Cloth</c> collides only against sphere and capsule colliders registered on the component
    /// by hand and against terrain not at all, which rules it out for a thing whose entire job is
    /// to land on the ground and drape over an animal. A lattice of rigidbodies and configurable
    /// joints does collide with everything, and a 15x15 net is 225 bodies and some 800 joints that
    /// PhysX relaxes badly enough to stretch and buzz visibly.
    /// </para>
    /// <para>
    /// <b>Positions are the state; velocity is implied.</b> A node's velocity IS the gap between
    /// <see cref="pos"/> and <see cref="prev"/>, which is why every impulse in this class is written
    /// into <c>prev</c> and never into <c>pos</c>. Writing an impulse into <c>pos</c> displaces the
    /// shape for one frame and lets the constraints snap it back — a flicker, not a push. Same trap
    /// <see cref="LassoRope.Snap"/> documents.
    /// </para>
    /// <para>
    /// <b>The rest length is driven, not fixed.</b> A lattice with a fixed rest length cannot change
    /// size — its strands are inextensible by construction, which is the whole point of them — so a
    /// net cannot be opened by an impulse. Both ways of trying were measured, and each fails in its
    /// own direction. Laying the net out COMPRESSED, with nodes closer together than their rest
    /// length, stores energy that the bilateral strand constraint releases in a single substep: the
    /// net explodes, reaching three times the span of an unbloomed one. Laying it out at FULL size
    /// and shoving the hem outward hands the solver a target it cannot satisfy, so the impulse goes
    /// into the only deformation that preserves strand length — shear. The mesh racks over toward
    /// its shear cap and the net gets SMALLER, while never leaving its own plane.
    /// </para>
    /// <para>
    /// Growing the rest length avoids both, because it never presents an unsatisfiable target:
    /// every strand sits exactly at rest at every instant, there is no stored energy to release,
    /// and the net simply has more cord available each substep. That is what
    /// <see cref="bundleFraction"/> and <see cref="unfurlSeconds"/> are for, and it is why they
    /// exist at all.
    /// </para>
    /// <para>
    /// A plain serializable class rather than a MonoBehaviour, so it tunes in the Inspector under
    /// its own foldout with no component to wire up.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class SnareLattice
    {
        [Tooltip("Nodes per side. 15 across 6 m is a 0.43 m mesh — coarse enough to be cheap, fine " +
                 "enough that a draped node spacing reads as cord rather than as a tent frame.")]
        [SerializeField] private int nodesPerSide = 15;

        [Tooltip("Jakobsen passes per substep. This is what INEXTENSIBILITY actually is — one pass " +
                 "is a spring, and a net that stretches under load is a trampoline.\n\n" +
                 "Note this budget is SHARED: every constraint family added to the loop — strands, " +
                 "then shear, then anything after — spends the same passes. Adding one visibly " +
                 "costs the others convergence, so if strands start creeping toward their " +
                 "tolerance the answer is more passes here, never a looser tolerance in the test.")]
        [SerializeField, Range(1, 16)] private int iterations = 8;

        [Tooltip("Metres per second squared. Above real gravity for the reason LassoRope's is: a " +
                 "game-scale net has to settle inside the fraction of a second the player watches.")]
        [SerializeField] private float gravity = 14f;

        [Tooltip("Velocity retained per substep.")]
        [SerializeField, Range(0.8f, 1f)] private float damping = 0.97f;

        [Tooltip("Fixed substep, seconds. Fixed rather than the frame's delta so two players " +
                 "watching one net see the same shape regardless of frame rate.\n\n" +
                 "Floored, because an authored 0 leaves the catch-up loop in Simulate subtracting " +
                 "nothing from its own backlog — which hangs the editor outright.")]
        [SerializeField, Min(0.001f)] private float simulationStep = 1f / 90f;

        [Tooltip("How much heavier a rim node is than a mesh node. Real cast nets carry most of " +
                 "their mass in the hem, and that mass is what blooms the net open in flight and " +
                 "carries the skirt PAST and UNDER a target on impact. At 1 the net is a bedsheet.\n\n" +
                 "Floored at 1, because anything below it is a hem LIGHTER than the mesh — the " +
                 "exact inverse of what this field is for.")]
        [SerializeField, Min(1f)] private float rimMassMultiplier = 6f;

        [Tooltip("How far a square cell may rack over into a rhombus before the mesh locks, as a " +
                 "multiple of the cell's rest diagonal.\n\n" +
                 "This is the number that decides whether the thing reads as a NET. Four-neighbour " +
                 "strands say nothing about the angle at a corner, so a lattice racked flat into a " +
                 "line satisfies all of them exactly and the solver never undoes it — that is a " +
                 "trellis. Holding the diagonal AT its rest length instead is the opposite mistake: " +
                 "a rigid sheet that tents over a captive rather than wrapping it.\n\n" +
                 "1.30 lets a cell close from 90 degrees to roughly 46 before it stops, which is " +
                 "about where a knotted mesh binds. Lower is stiffer canvas.\n\n" +
                 "Capped below the geometric limit on purpose. A square cell cannot exceed a " +
                 "diagonal of twice its side however hard it is racked, so a limit near 1.41 is " +
                 "not a loose net — it is no constraint at all, and it silently disarms the test " +
                 "that proves the mesh locks.")]
        [SerializeField, Range(1.02f, 1.30f)] private float shearLimit = 1.30f;

        [Tooltip("How hard the diagonals pull back once a cell reaches the limit above, 0-1.\n\n" +
                 "At 1 the limit is a WALL: the diagonal pass yanks the cell back the whole way in " +
                 "one go, the strand pass then has to undo the stretch that caused, and the two " +
                 "spend the iteration budget fighting each other. Neither converges, so every " +
                 "substep ends holding a different residual and the net buzzes. A soft limit " +
                 "converges on the strands instead of arguing with them, which is the whole " +
                 "difference between cloth and a rattling frame.")]
        [SerializeField, Range(0.02f, 1f)] private float shearStiffness = 0.3f;

        [Tooltip("Resistance to bending, 0-1. A net is FLOPPY: this wants to be low, just enough " +
                 "to erase the one-node zigzag that distance constraints cannot see (a concertina " +
                 "has every segment at exactly its rest length, so the strands have no reason to " +
                 "undo it). LassoRope needs 0.3 because a rope holds a curve; anything near that " +
                 "here is a bedsheet.\n\n" +
                 "This is a CONSTRAINT the solver relaxes, not a smoothing pass run afterwards — " +
                 "see ConstrainBend for why that distinction is the difference between a net that " +
                 "settles and one that shivers.\n\n" +
                 "There is a CLIFF just above this range. Measured against a net dropped onto a " +
                 "shoulder, 0.016 drapes over it and holds the centre 2.2 m up; 0.030 slides " +
                 "straight off onto the floor and 0.050 never folds at all. The Range stops short " +
                 "of it on purpose — past there the net is a board, and a board is not a net.")]
        [SerializeField, Range(0f, 0.025f)] private float bendStiffness = 0.016f;

        [Tooltip("How hard the mesh resists moving broadside-on through air, 0-1. This is what " +
                 "makes the net flutter as it falls and what slows the bloom without a tuned curve.")]
        [SerializeField, Range(0f, 1f)] private float faceDrag = 0.25f;

        [Tooltip("Metres per second the hem is thrown outward when the net leaves the canister.\n\n" +
                 "Floored at zero: a negative bloom draws the hem INWARD, which balls the net up at " +
                 "the muzzle and reads as the gun having failed to fire.")]
        [SerializeField, Min(0f)] private float bloomSpeed = 7f;

        [Tooltip("The net in the canister, as a fraction of its open size. The lattice is laid out " +
                 "at this scale WITH a rest length to match, so it starts correct and tiny rather " +
                 "than correct and huge — a 6 m net at the muzzle is wider than the distance the " +
                 "shot covers in its first fifth of a second, and would engulf the shooter.")]
        [SerializeField, Range(0.02f, 0.3f)] private float bundleFraction = 0.08f;

        [Tooltip("Seconds from canister to open. The rest length is what travels — see the class " +
                 "summary for why the net cannot simply be pushed open instead.")]
        [SerializeField, Min(0.01f)] private float unfurlSeconds = 0.28f;

        [Tooltip("How much of a node's sideways speed the ground takes off it per substep, 0-1.\n\n" +
                 "Without this the drape is a height clamp and nothing else: it stops nodes going " +
                 "THROUGH the sand and says nothing about sliding along it, so a net that lands " +
                 "with any speed left keeps skating and rolling on the spot for the rest of its " +
                 "life. Cord on sand does not slide, so this is high.")]
        [SerializeField, Range(0f, 1f)] private float groundGrip = 0.45f;

        [Tooltip("The same, for cord lying against a captive rather than against the ground.\n\n" +
                 "This is what decides whether a net STAYS on the animal it landed on. With no " +
                 "friction against a body the net is on a frictionless dome: the weighted hem " +
                 "pulls, the cord slides over the shoulders, and the whole thing ends up in a ring " +
                 "on the floor around a creature standing perfectly free in the middle of it.")]
        [SerializeField, Range(0f, 1f)] private float bodyGrip = 0.6f;

        [Tooltip("How much harder gravity pulls on the hem than on the mesh.\n\n" +
                 "The lead line, and it is a FORCE, not the inertia rimMassMultiplier gives. Those " +
                 "two are different things and only one of them holds a net down: gravity is an " +
                 "acceleration, so a hem that is merely heavier falls at exactly the rate the mesh " +
                 "does and its weight shows up only when the two fight over a strand. A real cast " +
                 "net has lead on the hem and cord everywhere else, and what that buys is a skirt " +
                 "that drives itself down and under while the light mesh drifts — which is the " +
                 "whole of how a net ends up beneath an animal instead of tented over it.")]
        [SerializeField, Min(1f)] private float hemWeight = 3f;

        /// <summary>Seconds of backlog the solver will try to catch up on before dropping the rest.</summary>
        private const float MaxCatchUpSeconds = 0.1f;

        /// <summary>
        /// Metres above the floor a node still counts as touching it, for <see cref="GripGround"/>.
        ///
        /// Not zero. The drape clamps a sinking node to exactly the floor height, and a node
        /// resting there is one float epsilon from being judged airborne on the next substep, so a
        /// zero band makes the grip flicker on and off and the net buzzes instead of settling.
        /// </summary>
        private const float ContactBand = 0.05f;

        private Vector3[] pos;
        private Vector3[] prev;
        private float[] inverseMass;

        /// <summary>Which nodes are the hem. Stored rather than derived, so Integrate stays a loop.</summary>
        private bool[] onRim;
        private int side;
        private float restSpacing;
        private float openSpacing;
        private float bundleSpacing;
        private float unfurlClock;
        private float accumulator;

        /// <summary>The authored stiffnesses divided across this substep's passes. See PerPass.</summary>
        private float shearPerPass;
        private float bendPerPass;

        /// <summary>
        /// Nodes per side. Reports the authored count before <see cref="Deploy"/> and the live one
        /// after, so it is never zero — a consumer looping over it on an undeployed lattice would
        /// otherwise iterate nothing at all and draw an invisible net with nothing logged.
        /// </summary>
        public int Resolution => side > 0 ? side : Mathf.Max(3, nodesPerSide);

        /// <summary>Rest length of one strand segment, metres.</summary>
        public float RestSpacing => restSpacing;

        /// <summary>Cell diagonals may reach this multiple of their rest length, and no further.</summary>
        public float ShearLimit => shearLimit;

        /// <summary>Backing array, for the mesh builder. Read-only by contract.</summary>
        public Vector3[] Positions => pos;

        /// <summary>How much of a node's sideways speed a captive's body takes off it per substep.</summary>
        public float BodyGrip => bodyGrip;

        public Vector3 NodeAt(int row, int col) => pos[Index(row, col)];

        /// <summary>1/mass for a node. Rim nodes answer lower — see <see cref="rimMassMultiplier"/>.</summary>
        public float InverseMassAt(int row, int col) => inverseMass[Index(row, col)];

        /// <summary>Move one node outright. A test seam — nothing in the game calls this.</summary>
        public void SetNodeForTest(int row, int col, Vector3 world)
        {
            int i = Index(row, col);
            pos[i] = world;
            prev[i] = world;
        }

        /// <summary>
        /// Rack the lattice sideways, as a shove would. A test seam for the shear-lock test, which
        /// otherwise has no way to reach a deformation the solver has to answer.
        /// </summary>
        public void RackForTest(float shearMetres)
        {
            for (int row = 0; row < side; row++)
            {
                float t = row / (float)(side - 1) - 0.5f;
                Vector3 shove = Vector3.right * (t * shearMetres);

                for (int col = 0; col < side; col++)
                {
                    int i = Index(row, col);
                    pos[i] += shove;
                    prev[i] += shove;
                }
            }
        }

        /// <summary>
        /// Throw the hem outward. Called once, at the muzzle.
        ///
        /// <para>
        /// This does NOT open the net — <see cref="AdvanceUnfurl"/> does, by growing the rest
        /// length, and the class summary explains why nothing else can. What this adds is outward
        /// MOMENTUM, so the net leads with its hem and overshoots its open size slightly before
        /// settling, rather than inflating evenly like a balloon. Without it the unfurl is correct
        /// and lifeless.
        /// </para>
        /// <para>
        /// Written into <see cref="prev"/> rather than <see cref="pos"/>, because in a Verlet
        /// lattice velocity is the gap between the two: moving <c>prev</c> gives a node speed,
        /// while moving <c>pos</c> teleports it and lets the next constraint pass undo it.
        /// </para>
        /// </summary>
        public void Bloom()
        {
            if (pos == null) return;

            Vector3 centre = Centre();

            for (int row = 0; row < side; row++)
            for (int col = 0; col < side; col++)
            {
                int i = Index(row, col);
                if (!onRim[i]) continue;

                Vector3 outward = pos[i] - centre;
                if (outward.sqrMagnitude < 1e-6f) continue;

                prev[i] -= outward.normalized * (bloomSpeed * simulationStep);
            }
        }

        /// <summary>Mean node position. The net has no transform of its own; this is its handle.</summary>
        public Vector3 Centre()
        {
            if (pos == null || pos.Length == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < pos.Length; i++) sum += pos[i];
            return sum / pos.Length;
        }

        /// <summary>Widest distance between two opposite rim nodes. A test seam for the bloom.</summary>
        public float SpanForTest() =>
            Vector3.Distance(NodeAt(0, 0), NodeAt(side - 1, side - 1));

        private int Index(int row, int col) => row * side + col;

        /// <summary>
        /// Override the authored tunables. For tests, which have no Inspector.
        ///
        /// Named for the seam it is rather than for what it does, because it overwrites values a
        /// designer put on the gun prefab. Anything in the game that wants a differently sized net
        /// should carry its own authored lattice instead of reaching in and rewriting this one.
        /// </summary>
        public void ConfigureForTest(int nodesPerSide, float rimMassMultiplier, float shearLimit,
                                    float shearStiffness, float bendStiffness)
        {
            this.nodesPerSide = nodesPerSide;
            this.rimMassMultiplier = rimMassMultiplier;
            this.shearLimit = shearLimit;
            this.shearStiffness = shearStiffness;
            this.bendStiffness = bendStiffness;
        }

        /// <summary>
        /// Override how hard a captive's body holds the cord lying on it. For tests, which have to
        /// be able to turn one contribution off to show it is the one doing the work.
        /// </summary>
        public void ConfigureGripForTest(float bodyGrip) => this.bodyGrip = bodyGrip;

        /// <summary>Override the lead line's pull. For tests and for tuning sweeps.</summary>
        public void ConfigureHemForTest(float hemWeight) => this.hemWeight = hemWeight;

        /// <summary>
        /// A fresh lattice with the same tunables and none of the state.
        ///
        /// The instance serialized on the gun prefab is a TEMPLATE, not a net. Two nets in the air
        /// sharing one instance share one set of node arrays, so the second shot overwrites the
        /// first net's geometry mid-flight and the first net snaps to the second's muzzle.
        /// </summary>
        public SnareLattice Clone() => new SnareLattice
        {
            nodesPerSide = nodesPerSide,
            iterations = iterations,
            gravity = gravity,
            damping = damping,
            simulationStep = simulationStep,
            rimMassMultiplier = rimMassMultiplier,
            shearLimit = shearLimit,
            shearStiffness = shearStiffness,
            bendStiffness = bendStiffness,
            faceDrag = faceDrag,
            bloomSpeed = bloomSpeed,
            bundleFraction = bundleFraction,
            unfurlSeconds = unfurlSeconds,
            groundGrip = groundGrip,
            bodyGrip = bodyGrip,
            hemWeight = hemWeight,
        };

        /// <summary>
        /// Carry the whole net, without disturbing it.
        ///
        /// <para>
        /// Applied to <see cref="pos"/> AND <see cref="prev"/> by the same amount, which is what
        /// makes this a carry rather than a shove: velocity here is the gap between the two, so
        /// moving only <c>pos</c> would read as the entire net being flung, and the constraint
        /// passes would spend the next substep tearing it apart.
        /// </para>
        /// <para>
        /// This is how the net travels while it is in the air. The flight itself is closed-form and
        /// lives in <c>NetGunFlight</c>, so every machine carries its net along the identical arc
        /// while the lattice goes on unfurling and blooming underneath.
        /// </para>
        /// </summary>
        public void Translate(Vector3 delta)
        {
            if (pos == null || delta == Vector3.zero) return;

            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] += delta;
                prev[i] += delta;
            }
        }

        /// <summary>
        /// Turn the whole net about a point, without disturbing it.
        ///
        /// <para>
        /// The rotational twin of <see cref="Translate"/>, and applied to <see cref="pos"/> AND
        /// <see cref="prev"/> for the same reason: velocity here is the gap between the two, so
        /// turning only <c>pos</c> would read as the whole net being spun and the constraint passes
        /// would spend the next substep tearing it apart.
        /// </para>
        /// <para>
        /// What this is for: a net is <see cref="Deploy"/>ed square to the aim and then carried
        /// along an arc that bends away from that aim, so without this it sails the whole way as
        /// an upright pane facing the direction it was FIRED rather than the direction it is
        /// GOING. <c>SnareCatch</c> keeps it square to the closed-form velocity instead, so it
        /// tips over as the arc falls and meets the ground face-first.
        /// </para>
        /// </summary>
        public void RotateAbout(Vector3 pivot, Quaternion delta)
        {
            if (pos == null) return;

            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = pivot + delta * (pos[i] - pivot);
                prev[i] = pivot + delta * (prev[i] - pivot);
            }
        }

        /// <summary>
        /// Give the whole net a velocity.
        ///
        /// <para>
        /// Written into <see cref="prev"/> and never into <see cref="pos"/> — see the class
        /// summary. Added to what each node already has rather than replacing it, so the bloom and
        /// the unfurl are not erased by it.
        /// </para>
        /// <para>
        /// Called once, at touchdown. While the net is in the air it is CARRIED, which by
        /// construction gives it no velocity at all: <see cref="Translate"/> moves <c>pos</c> and
        /// <c>prev</c> together. So a net whose carry simply stopped would hang in the air with
        /// nothing but its own gravity and drop vertically, like a curtain cut from its rail.
        /// Handing it the speed the carry had been supplying is what makes it collapse INTO what
        /// it hit and fold over it.
        /// </para>
        /// </summary>
        public void Impart(Vector3 velocity)
        {
            if (pos == null) return;

            for (int i = 0; i < pos.Length; i++) prev[i] -= velocity * simulationStep;
        }

        /// <summary>
        /// Put one node on a surface it was about to pass through, without bouncing it off.
        ///
        /// <para>
        /// The bounce is the whole reason this exists. Moving <see cref="pos"/> to the surface and
        /// leaving <see cref="prev"/> where it was does not park the node there — velocity here IS
        /// the gap between the two, so shoving <c>pos</c> outward by the penetration depth hands
        /// the node that depth as outward SPEED, every substep it is in contact. A net dropped on
        /// an animal took that as an impulse off every node at once and jumped clear of it, which
        /// is a net bouncing off the thing it was supposed to catch.
        /// </para>
        /// <para>
        /// So the correction is applied to both, and the velocity is then edited directly: the part
        /// driving the node INTO the surface is dropped outright, and the part sliding it ALONG the
        /// surface is scaled by <paramref name="friction"/>. That second half is what keeps a net
        /// on a shoulder — without it the cord is on a frictionless dome and the weighted hem
        /// simply drags the whole sheet off onto the floor.
        /// </para>
        /// </summary>
        public void PlaceOnSurface(int index, Vector3 surface, Vector3 normal, float friction)
        {
            if (pos == null) return;

            Vector3 velocity = pos[index] - prev[index];

            float into = Vector3.Dot(velocity, normal);
            Vector3 alongSurface = velocity - normal * into;

            // Only the inward half goes. An outward-moving node in contact is one the solver is
            // already pulling clear, and cancelling that would fight it.
            if (into > 0f) alongSurface += normal * into;

            pos[index] = surface;
            prev[index] = surface - alongSurface * (1f - Mathf.Clamp01(friction));
        }

        /// <summary>
        /// Let the ground hold onto the cord lying on it.
        ///
        /// <para>
        /// <see cref="SnareDrape"/> is a position solver: it stops nodes passing through the floor
        /// and says nothing whatever about sliding along it. That is the whole of why a landed net
        /// used to skid and roll on the spot — every node it clamped kept its sideways speed and
        /// simply travelled along the surface instead of through it, forever, since the only thing
        /// that ever removed speed was the general <see cref="damping"/>.
        /// </para>
        /// <para>
        /// Only the horizontal component is touched. Damping the vertical one as well would fight
        /// the clamp itself, which is the correction the drape has just made, and re-damping a
        /// solver's own corrections is the trap <see cref="Step"/> documents for face drag.
        /// </para>
        /// </summary>
        public void GripGround(float groundHeight)
        {
            if (pos == null) return;

            float slide = 1f - Mathf.Clamp01(groundGrip);

            for (int i = 0; i < pos.Length; i++)
            {
                if (pos[i].y > groundHeight + ContactBand) continue;

                Vector3 velocity = pos[i] - prev[i];
                velocity.x *= slide;
                velocity.z *= slide;
                prev[i] = pos[i] - velocity;
            }
        }

        /// <summary>The axis-aligned box the net currently occupies. Empty before <see cref="Deploy"/>.</summary>
        public Bounds WorldBounds()
        {
            if (pos == null || pos.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            var bounds = new Bounds(pos[0], Vector3.zero);
            for (int i = 1; i < pos.Length; i++) bounds.Encapsulate(pos[i]);
            return bounds;
        }

        /// <summary>
        /// Lay the net out flat, facing <paramref name="forward"/>, centred on <paramref name="centre"/>.
        ///
        /// <para>
        /// <paramref name="halfWidth"/> is metres from centre to edge, so the net is twice this
        /// across. It is a SQUARE, so the corners reach further again — a half-width of 3 gives a
        /// 6 m net whose corners are 4.24 m out. Anything sizing a trigger volume off this number
        /// has to say which of the two it means.
        /// </para>
        /// <para>
        /// Every node placed on the plane rather than left where the last shot abandoned it, for the
        /// reason <see cref="LassoRope.Show"/> gives: a lattice that starts as a tangle at the origin
        /// spends its first half second visibly falling into place.
        /// </para>
        /// </summary>
        public void Deploy(Vector3 centre, Vector3 forward, float halfWidth)
        {
            side = Mathf.Max(3, nodesPerSide);
            int count = side * side;

            if (pos == null || pos.Length != count)
            {
                pos = new Vector3[count];
                prev = new Vector3[count];
                inverseMass = new float[count];
                onRim = new bool[count];
            }

            float span = halfWidth * 2f;

            // Born in the canister and grown from there. Laying the nodes out across the bundle
            // span with a rest length to match means every strand starts exactly at rest — no
            // stored energy, nothing to release. See the class summary.
            openSpacing = span / (side - 1);
            bundleSpacing = openSpacing * bundleFraction;
            restSpacing = bundleSpacing;
            unfurlClock = 0f;

            span *= bundleFraction;

            Vector3 axis = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;

            // An explicit reference, because LookRotation's implicit Vector3.up is undefined when
            // the aim IS vertical — and a net fired straight up or straight down is an ordinary
            // shot, not an edge case. Left implicit, the basis collapses and the net deploys in a
            // fixed world plane no matter where the player was aiming.
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;

            Quaternion basis = Quaternion.LookRotation(axis, reference);
            Vector3 right = basis * Vector3.right;
            Vector3 up = basis * Vector3.up;

            for (int row = 0; row < side; row++)
            for (int col = 0; col < side; col++)
            {
                float u = col / (float)(side - 1) - 0.5f;
                float v = row / (float)(side - 1) - 0.5f;

                int i = Index(row, col);
                pos[i] = centre + right * (u * span) + up * (v * span);
                prev[i] = pos[i];

                // The hem is every node on the outer ring. Mass is expressed as its inverse
                // because that is the form the constraint solver wants, and because a pinned node
                // is then simply one with an inverse mass of zero.
                onRim[i] = row == 0 || col == 0 || row == side - 1 || col == side - 1;
                inverseMass[i] = onRim[i] ? 1f / Mathf.Max(rimMassMultiplier, 0.0001f) : 1f;
            }

            accumulator = 0f;
        }

        /// <summary>Advance by a frame's worth of time, in fixed substeps.</summary>
        public void Simulate(float deltaTime)
        {
            // Quiet, unlike Step's throw: this runs every frame, and an undeployed lattice is an
            // ordinary state here — a holstered gun — rather than a caller mistake.
            if (pos == null) return;

            // Clamped, so a hitch or a breakpoint cannot hand this a two-second delta and spiral
            // through a hundred substeps trying to catch up.
            //
            // The cost, which the determinism tooltip above does not pay: a machine that hitches
            // past this loses simulated time permanently and its net will not re-sync, because
            // nothing re-seeds the lattice after deploy. That is acceptable only because the net is
            // presentation — what was CAUGHT is the server's answer and travels as a message — so
            // the worst case is two players seeing slightly different folds in the same net.
            accumulator = Mathf.Min(accumulator + deltaTime, MaxCatchUpSeconds);

            while (accumulator >= simulationStep)
            {
                Step(simulationStep);
                accumulator -= simulationStep;
            }
        }

        /// <summary>
        /// One substep. Public because the EditMode tests compile into Assembly-CSharp-Editor,
        /// which cannot see internals of Assembly-CSharp — the same seam
        /// <see cref="LassoedBody.Step"/> exposes for the same reason.
        ///
        /// <para>
        /// <b>The order of these stages is load-bearing.</b> Position-based solvers do not commute,
        /// and getting it wrong looks like a tuning problem rather than an ordering one.
        /// </para>
        /// <para>
        /// <b>Drag runs before the constraint passes, not after.</b> Drag reads velocity as the gap
        /// between <c>pos</c> and <c>prev</c>. After the passes have run that gap is no longer
        /// velocity — it is velocity plus every correction the solver just made. Damping it there
        /// does not model air resistance; it silently damps the solver, so the harder the net is
        /// pulled the slower it converges, and a taut net creeps instead of holding.
        /// </para>
        /// <para>
        /// <b>Nothing runs after the passes.</b> This is what a cloth solver is: every rule the
        /// cord obeys is a constraint the solve relaxes, so the substep ENDS satisfying all of them
        /// together. Bending used to be a Laplacian smoothing pass applied after the loop instead,
        /// and that is a different thing entirely — a filter that moves nodes off their rest
        /// lengths by construction, leaving every substep ending off-constraint for the next one to
        /// yank back. At ninety substeps a second that is not a small residual, it is a permanent
        /// vibration, and it is what the net's shivering and jumping actually was. Bending is now
        /// <see cref="ConstrainBend"/>, inside the loop, sharing the same budget as everything else.
        /// </para>
        /// </summary>
        public void Step(float step)
        {
            if (pos == null)
                throw new System.InvalidOperationException(
                    "SnareLattice.Step before Deploy. Deploy lays out the nodes; there is nothing " +
                    "to advance until it has run.");

            AdvanceUnfurl(step);
            Integrate(step);
            ApplyFaceDrag();

            shearPerPass = PerPass(shearStiffness);
            bendPerPass = PerPass(bendStiffness);

            // Alternating direction, because this is Gauss-Seidel: a pass carries tension from the
            // corner it starts at across the whole lattice, so running every pass the same way
            // leaves the far corner lagging.
            //
            // Shear is relaxed inside this loop rather than after it, because the two constraints
            // genuinely fight: pulling a stretched strand back in racks its cell further over, and
            // capping the diagonal stretches the strands again. Interleaving lets them converge on
            // each other; running the diagonals once at the end just leaves the strands holding
            // the whole residual.
            for (int pass = 0; pass < iterations; pass++)
            {
                ConstrainStrands(forward: (pass & 1) == 0);
                ConstrainShear();
                ConstrainBend();
            }
        }

        /// <summary>
        /// One pass's share of an authored stiffness.
        ///
        /// <para>
        /// A soft constraint relaxed <see cref="iterations"/> times is not soft: each pass takes
        /// its fraction of what the last one left, so the total is
        /// <c>1 - (1 - k)^iterations</c> and the authored number means nothing on its own. At eight
        /// passes an authored 0.06 arrives as 0.39, and that is not a subtle difference — it was
        /// the whole of why the first cloth build turned the net into a board that slid off the
        /// shoulder it landed on instead of folding over it.
        /// </para>
        /// <para>
        /// Inverting it here is the standard position-based fix, and what it buys is that the
        /// authored value means the same thing at any iteration count: raising
        /// <see cref="iterations"/> for convergence no longer silently stiffens the net as a side
        /// effect. Strands are exempt because they are not soft — inextensibility is applied whole,
        /// every pass.
        /// </para>
        /// </summary>
        private float PerPass(float stiffness)
        {
            if (stiffness <= 0f) return 0f;
            if (stiffness >= 1f) return 1f;

            return 1f - Mathf.Pow(1f - stiffness, 1f / Mathf.Max(iterations, 1));
        }

        /// <summary>
        /// Let out more cord.
        ///
        /// <para>
        /// The rest length travels from bundle to open across <see cref="unfurlSeconds"/>, and the
        /// constraints simply follow it. SmoothStep rather than a straight ramp because the ends
        /// are what read: a linear unfurl starts and stops with a visible corner, while eased ends
        /// look like cord paying out and then running out.
        /// </para>
        /// <para>
        /// This runs before <see cref="Integrate"/> so that every constraint in the substep sees
        /// one rest length — including <see cref="ConstrainShear"/>, whose cap is derived from it.
        /// </para>
        /// </summary>
        private void AdvanceUnfurl(float step)
        {
            if (restSpacing >= openSpacing) return;

            unfurlClock += step;

            float t = Mathf.Clamp01(unfurlClock / unfurlSeconds);
            restSpacing = Mathf.Lerp(bundleSpacing, openSpacing, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>
        /// One Verlet step, with the hem pulled down harder than the mesh.
        ///
        /// <para>
        /// Gravity is an acceleration, so it is deliberately NOT divided by mass — a heavy rim node
        /// falls at exactly the rate a light mesh node does, and <see cref="rimMassMultiplier"/>
        /// shows up only where the two fight over a constraint. That is correct, and on its own it
        /// is also why a net used to tent over an animal rather than closing under it: nothing was
        /// driving the skirt DOWN relative to the rest of the sheet.
        /// </para>
        /// <para>
        /// <see cref="hemWeight"/> is the lead line, and it is a separate quantity from the inertia
        /// for exactly that reason. Weights sewn into a hem are not the same claim as a hem that is
        /// harder to move; they are a hem being pulled on. Modelling it as extra gravity on the rim
        /// is what makes the sides fall first, hang below the mesh, and gather under whatever the
        /// net came down on.
        /// </para>
        /// </summary>
        private void Integrate(float step)
        {
            Vector3 fall = Vector3.down * (gravity * step * step);
            Vector3 hemFall = fall * Mathf.Max(hemWeight, 1f);

            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 velocity = (pos[i] - prev[i]) * damping;
                prev[i] = pos[i];
                pos[i] += velocity + (onRim[i] ? hemFall : fall);
            }
        }

        /// <summary>
        /// Bending: how hard the cord resists being folded back on itself.
        ///
        /// <para>
        /// The third thing a cloth solver needs, after inextensible strands and a shear rule.
        /// Four-neighbour distance constraints are completely blind to it — a concertina has every
        /// segment at exactly its rest length, so nothing in <see cref="ConstrainStrands"/> has any
        /// reason to undo one, and a net without this crumples into hard creases with no scale to
        /// them.
        /// </para>
        /// <para>
        /// Run over every straight triple along a row or a column, pulling the middle node back
        /// toward the line between its neighbours. That is still frequency-selective the way the
        /// Laplacian this replaces was — a one-node zigzag is a large violation and a draped net's
        /// long sag is a fraction of a percent, so the fold goes and the sag stays — but it is a
        /// CONSTRAINT rather than a filter, so it is relaxed alongside the strands and the shear
        /// instead of undoing their work after the fact. See <see cref="Step"/>.
        /// </para>
        /// </summary>
        private void ConstrainBend()
        {
            if (bendPerPass <= 0f) return;

            for (int row = 0; row < side; row++)
            for (int col = 0; col < side - 2; col++)
                Straighten(Index(row, col), Index(row, col + 1), Index(row, col + 2));

            for (int col = 0; col < side; col++)
            for (int row = 0; row < side - 2; row++)
                Straighten(Index(row, col), Index(row + 1, col), Index(row + 2, col));
        }

        /// <summary>
        /// Move one bent triple toward straight, sharing the move across all three nodes.
        ///
        /// <para>
        /// All three, and weighted by inverse mass, because a correction applied only to the middle
        /// node is a force with nothing on the other end of it: over a whole lattice those add up
        /// and the net drifts under its own bending. The weights are the standard position-based
        /// ones for the constraint "b sits on the line ac" — the middle node has twice the gradient
        /// of either end, so it takes four times the share.
        /// </para>
        /// </summary>
        private void Straighten(int a, int b, int c)
        {
            Vector3 bend = (pos[a] + pos[c]) * 0.5f - pos[b];
            if (bend.sqrMagnitude < 1e-10f) return;

            float weightA = inverseMass[a];
            float weightB = inverseMass[b];
            float weightC = inverseMass[c];

            float share = weightB + (weightA + weightC) * 0.25f;
            if (share <= 0f) return;

            Vector3 correction = bend * (bendPerPass / share);

            pos[b] += correction * weightB;
            pos[a] -= correction * (weightA * 0.5f);
            pos[c] -= correction * (weightC * 0.5f);
        }

        /// <summary>
        /// Air resistance, but only across the face.
        ///
        /// <para>
        /// A net edge-on through the air barely slows; broadside-on it is a parachute. Damping
        /// alone cannot express that difference, and without it the bloom expands forever and the
        /// fall is a dead drop with no flutter in it.
        /// </para>
        /// <para>
        /// Interior nodes only, because the face normal is taken from the four neighbours and a rim
        /// node does not have them. That leaves the hem undragged, which is the right answer rather
        /// than a limitation: the hem is the heavy part, and it is supposed to carry.
        /// </para>
        /// </summary>
        private void ApplyFaceDrag()
        {
            if (faceDrag <= 0f) return;

            for (int row = 1; row < side - 1; row++)
            for (int col = 1; col < side - 1; col++)
            {
                int i = Index(row, col);

                Vector3 across = pos[Index(row, col + 1)] - pos[Index(row, col - 1)];
                Vector3 along = pos[Index(row + 1, col)] - pos[Index(row - 1, col)];

                Vector3 normal = Vector3.Cross(across, along);
                if (normal.sqrMagnitude < 1e-8f) continue;
                normal.Normalize();

                Vector3 velocity = pos[i] - prev[i];
                float broadside = Vector3.Dot(velocity, normal);

                // Into prev, so this is a force on the node rather than a displacement of it.
                prev[i] += normal * (broadside * faceDrag);
            }
        }

        /// <summary>
        /// One Jakobsen pass over the four-neighbour strands.
        ///
        /// <para>
        /// The correction is split by INVERSE MASS rather than 50/50. That single ratio is what
        /// makes a weighted hem behave like one: a rim node yields a sixth as far as the mesh node
        /// it is pulling, so the hem drags the mesh rather than the mesh restraining the hem.
        /// </para>
        /// <para>
        /// Rows are always relaxed before columns, and the <c>forward</c> alternation the caller
        /// does cannot address that: it flips which END of a line is favoured, not which DIRECTION
        /// is solved first. So a systematic anisotropy survives — the columns are relaxed against
        /// rows that have already moved this pass, and carry marginally more of the leftover error.
        /// At eight iterations it is far below anything visible, and it is written down here so the
        /// alternation is not mistaken for a complete fix.
        /// </para>
        /// </summary>
        private void ConstrainStrands(bool forward)
        {
            for (int row = 0; row < side; row++)
            for (int step = 0; step < side - 1; step++)
            {
                int col = forward ? step : side - 2 - step;
                Resolve(Index(row, col), Index(row, col + 1), restSpacing);
            }

            for (int col = 0; col < side; col++)
            for (int step = 0; step < side - 1; step++)
            {
                int row = forward ? step : side - 2 - step;
                Resolve(Index(row, col), Index(row + 1, col), restSpacing);
            }
        }

        /// <summary>
        /// The diagonals, as MAXIMUMS.
        ///
        /// <para>
        /// Both diagonals of every cell, and each one only ever pulled IN. Shearing a square into a
        /// rhombus lengthens one diagonal and shortens the other, so capping the long one caps the
        /// rack angle while leaving the cell completely free to close — which is exactly the
        /// asymmetry that separates a net from a sheet. The whole behaviour is the one-sided test
        /// in <see cref="ResolveMaximum"/>; making it two-sided is a rigid sheet, and removing it
        /// is a trellis.
        /// </para>
        /// <para>
        /// Relaxed at <see cref="shearStiffness"/> rather than snapped back the whole way. The two
        /// families genuinely fight — pulling a stretched strand in racks its cell further over,
        /// and yanking a diagonal back stretches the strands again — and at full stiffness neither
        /// ever wins, so every substep ends holding a different residual and the net shivers. Soft,
        /// they converge on each other across the iteration budget instead.
        /// </para>
        /// </summary>
        private void ConstrainShear()
        {
            float limit = restSpacing * Mathf.Sqrt(2f) * shearLimit;

            for (int row = 0; row < side - 1; row++)
            for (int col = 0; col < side - 1; col++)
            {
                ResolveMaximum(Index(row, col), Index(row + 1, col + 1), limit);
                ResolveMaximum(Index(row, col + 1), Index(row + 1, col), limit);
            }
        }

        /// <summary>
        /// Pull two nodes back to <paramref name="rest"/>, sharing the move by inverse mass.
        ///
        /// Bilateral: a pair that has drifted too CLOSE is pushed apart again, which is what makes
        /// a strand a strand rather than a piece of slack. <see cref="ResolveMaximum"/> is the
        /// one-sided counterpart.
        /// </summary>
        private void Resolve(int a, int b, float rest)
        {
            Vector3 delta = pos[b] - pos[a];
            float length = delta.magnitude;
            if (length < 1e-5f) return;

            // Full stiffness, and only here. Inextensibility is the one property of cord that is
            // not a matter of degree: a strand that gives is a rubber band, and the drape over a
            // captive becomes a sag that never settles.
            ShareCorrection(a, b, delta, length, rest, stiffness: 1f);
        }

        /// <summary>
        /// Pull two nodes together if they are further apart than <paramref name="maximum"/>. Never pushes.
        ///
        /// The early return IS the feature. Take it out and this becomes <see cref="Resolve"/> with
        /// a longer rest length: every cell is then held OPEN at the shear limit, and the net turns
        /// into a rigid sheet that tents over a captive instead of wrapping it.
        /// </summary>
        private void ResolveMaximum(int a, int b, float maximum)
        {
            Vector3 delta = pos[b] - pos[a];
            float length = delta.magnitude;
            if (length <= maximum || length < 1e-5f) return;

            ShareCorrection(a, b, delta, length, maximum, shearPerPass);
        }

        /// <summary>
        /// Move two nodes until the gap between them is <paramref name="target"/>, splitting the
        /// move by inverse mass so the lighter one travels further.
        ///
        /// <para>
        /// The half <see cref="Resolve"/> and <see cref="ResolveMaximum"/> genuinely share. What
        /// they do not share is the guard above the call — and that guard is the entire difference
        /// between an equality constraint and an inequality one, so it stays written out in each of
        /// them rather than hidden behind a flag argument here. A caller reading
        /// <c>ResolveMaximum(a, b, limit)</c> can see which one it got; a caller reading
        /// <c>Resolve(a, b, limit, true)</c> cannot.
        /// </para>
        /// <para>
        /// Takes <paramref name="delta"/> and <paramref name="length"/> already measured, because
        /// both callers need them for their own guard and this runs a few hundred thousand times a
        /// second — a second <c>magnitude</c> per constraint is a square root for nothing. Callers
        /// must have ruled out a zero <paramref name="length"/>.
        /// </para>
        /// </summary>
        private void ShareCorrection(int a, int b, Vector3 delta, float length, float target,
                                    float stiffness)
        {
            float weightSum = inverseMass[a] + inverseMass[b];
            if (weightSum <= 0f) return;

            Vector3 correction = delta * (stiffness * (length - target) / length);

            pos[a] += correction * (inverseMass[a] / weightSum);
            pos[b] -= correction * (inverseMass[b] / weightSum);
        }
    }
}
