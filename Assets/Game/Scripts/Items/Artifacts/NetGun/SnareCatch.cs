using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// One net, alive in the world: its lattice, its drape, its mesh, its captives and its pool.
    ///
    /// <para>
    /// A GameObject created at the muzzle on every machine and destroyed when the net rots. It
    /// carries no NetworkObject on purpose — the flight is drawn from a shared seed and the capture
    /// is announced, so there is nothing here for the network to replicate. Adding one would also
    /// break <c>NetworkPrefabRegistrationTests</c>, which asserts that every prefab in the project
    /// carrying a NetworkObject is registered.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // created in code, never by hand
    public sealed class SnareCatch : MonoBehaviour
    {
        /// <summary>Seconds the net takes to slacken and vanish once it has given out.</summary>
        private const float RotSeconds = 1.1f;

        /// <summary>Slack on the failsafe, so the authority always wins the race in the normal case.</summary>
        private const float LifeMargin = 2f;

        /// <summary>
        /// Metres of clearance around the net's CENTRE that count as having hit something.
        ///
        /// The net is six metres across, so a cast at its true width would stop it three metres
        /// short of every wall. Tracing the centre instead means the net stops where its middle
        /// arrives, which is also exactly where a capture wants it: over the thing. A creature the
        /// centre misses by more than this is not stopped, but is very often still caught, because
        /// the landing query runs against the net's whole draped footprint rather than this.
        /// </summary>
        private const float ImpactRadius = 0.75f;

        /// <summary>How many colliders one impact cast will consider. Reused, so it never allocates.</summary>
        private const int MaxImpactHits = 8;

        /// <summary>
        /// How much of the flight speed the net keeps when it arrives, 0-1.
        ///
        /// Not 1: the net has just hit something, and one that carried its full muzzle speed
        /// through the impact would sail on past it. Not 0 either — that is the old behaviour,
        /// where the carry stopped dead and the net dropped straight down like a cut curtain
        /// instead of folding forward over what it landed on.
        /// </summary>
        private const float ImpactCarryShare = 0.3f;

        /// <summary>
        /// Share of the carry over which the net turns from edge-on to face-down, 0-1.
        ///
        /// <para>
        /// See <see cref="FaceAlongFlight"/> for why the turn has to be driven at all. This is how
        /// fast: at 1 the net is only flat on the frame the carry ends, which is far too late to
        /// have opened onto anything.
        /// </para>
        /// <para>
        /// 0.4, measured rather than guessed. Replaying this class's own flight loop against the
        /// shipped tuning (15 x 15 lattice, 6 m net, muzzle 1.45 m, level shot, flat ground) gives a
        /// landed footprint of, along the flight axis: <b>1.2 m</b> with no driven turn at all,
        /// 2.2 m at a share of 1, 3.2 m at 0.6, <b>4.5 m at 0.4</b>, and 4.1 m at 0.25 — the last
        /// losing width as well, because the net is still bundle-sized when it starts to turn. The
        /// unfurl finishes at 0.28 s (<c>SnareLattice.unfurlSeconds</c>) and this finishes at
        /// 0.34 s, so the net is open just before it is flat, which is the order that reads.
        /// </para>
        /// </summary>
        private const float FaceDownShare = 0.4f;

        /// <summary>
        /// Seconds the net takes to close around what it hit.
        ///
        /// <para>
        /// A window rather than a settle test, because there is nothing to test: the cinch is a
        /// constraint the solver relaxes, so it converges asymptotically and never "finishes". The
        /// window is what makes the solver stop at all, which is the whole saving the wrap buys.
        /// </para>
        /// </summary>
        private const float CinchSeconds = 0.7f;

        /// <summary>
        /// The longest the cinch will wait for a captive to be recorded before binding anyway.
        ///
        /// <para>
        /// <b>This is latency tolerance. <see cref="CinchSeconds"/> is feel. They must not be
        /// collapsed into one number.</b> The bind needs a captive to nail the cord to, and on a
        /// PEER the capture arrives as <c>NetMsg.Snared</c> over the wire — after the authority's
        /// own landing pass has recorded it, which <c>SnareReceiver.SettleSeconds</c> gives 0.8 s
        /// to do. Binding on the feel clock alone means a peer whose message is late finds no
        /// captive, falls back to <see cref="SnarePhase.Fallen"/>, and draws the net lying on the
        /// sand while every other machine has it wrapped round a body.
        /// </para>
        /// <para>
        /// So the ordinary bind waits for the captive and the clock together, and this bounds the
        /// wait: a capture that never arrives at all still stops the solver rather than leaving it
        /// running for the net's whole thirty seconds. Three seconds covers the authority's own
        /// 0.8 s window plus a round trip far longer than any playable session.
        /// </para>
        /// </summary>
        private const float MaxCinchSeconds = 3f;

        /// <summary>
        /// Seconds a net that came down on nothing goes on solving before it freezes.
        ///
        /// <para>
        /// <b>Not <c>SnareReceiver.SettleSeconds</c>, which is a different clock entirely</b> — that
        /// one bounds how long the receiver keeps asking what a net caught. This one is how long the
        /// cord is given to lie down.
        /// </para>
        /// <para>
        /// It cannot be zero. <see cref="ImpactRadius"/> is 0.75, so ground contact is reported with
        /// the net's CENTRE three quarters of a metre up, and a drape only lifts nodes that are
        /// already below the ground — so a net frozen on the contact frame keeps the shape of a
        /// sheet arriving edge-on, held up in the middle by nothing. It is also what carries the
        /// wall case: a net that meets a cliff face slides down it and lands, instead of freezing
        /// plastered against it in mid-air.
        /// </para>
        /// <para>
        /// 2.75 s, taken from this project's own two settle beds. <c>ASettledNetGoesQuiet</c>
        /// drops a net four metres onto a BARE floor — <c>LandedLattice</c> passes
        /// <c>Array.Empty&lt;Capsule&gt;()</c>, so there is no captive under it — and reaches its
        /// strict bar (no node moving 0.01 m in a substep) after 400 substeps that include a ~0.76 s
        /// fall, so about 3.6 s of contact. <c>ALandedNetSettlesInsteadOfSkatingOnForever</c> is the
        /// gentler one and the one that matches this phase: a net fired from 1.6 m onto a floor,
        /// treated as having stopped moving after 240 substeps — 2.67 s.
        /// </para>
        /// <para>
        /// So the number sits just above the gentler bed rather than below it. That is deliberate:
        /// 2.67 s is the only directly comparable measurement in the project, a fired net arrives
        /// carrying <see cref="ImpactCarryShare"/> of its flight speed where the drop-test bed
        /// arrives with none, and undercutting the one bed that lands a net the way this phase does
        /// would be buying 0.17 s of solver time with the exact defect the window exists to fix. The
        /// saving is unaffected either way: 2.75 s against a thirty-second life is 91% of it.
        /// </para>
        /// </summary>
        private const float SettlingSeconds = 2.75f;

        /// <summary>
        /// Metres from the victim's centre line the cord is drawn to.
        ///
        /// Not the victim's own radius, and deliberately smaller than one: the strands are
        /// inextensible, so the cord that cannot reach this ring is what becomes the folds. A
        /// radius sized to the body would gather nothing.
        /// </summary>
        private const float CinchRadius = 0.45f;

        /// <summary>
        /// Metres of clearance under the net's lowest node that count as having reached the floor.
        ///
        /// Mirrors <c>SnareLattice</c>'s own contact band. Not zero: the drape clamps a sinking
        /// node to exactly the ground height, so a node resting there is one float epsilon from
        /// reading as airborne.
        /// </summary>
        private const float GroundContact = 0.05f;

        /// <summary>
        /// How far above the sample point the ground probe starts, and how far it reaches.
        ///
        /// It has to begin well clear of the net: the probe is cast from among the cord, and one
        /// starting at the sample point would be inside whatever the net is lying on.
        /// </summary>
        private const float ProbeRise = 30f;
        private const float ProbeReach = 120f;

        private SnareLattice lattice;
        private SnareDrape drape;
        private SnareMesh meshBuilder;
        private SnareBinding binding;
        private SnareStruggle struggle;
        private SnareIntegrity integrity;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private readonly List<GameObject> captives = new List<GameObject>();
        private readonly List<SnareDrape.Capsule> proxies = new List<SnareDrape.Capsule>();

        /// <summary>
        /// How hard each captive is fighting, index-aligned with <see cref="captives"/>.
        ///
        /// <para>
        /// A second meter, and the duplication is the design. The captive's own
        /// <c>SnaredBody</c> meter runs on THEIR machine and decides what they may send; this one
        /// runs on the authority and decides what the net gives out under. Sharing one would mean
        /// putting a level on the wire, which is handing the client the escape.
        /// </para>
        /// <para>
        /// Only the authority ever pushes or reads these — see <see cref="Struggled"/> and
        /// <see cref="Advance"/> — but the list is kept aligned on every machine regardless, so
        /// "these two lists are the same length" is a claim that needs no per-machine reasoning.
        /// It is mutated in exactly the three places <see cref="captives"/> is.
        /// </para>
        /// <para>
        /// A parallel list rather than a dictionary keyed by the captive, because a destroyed
        /// GameObject is a key with Unity's own null semantics under it, and this way the entry
        /// simply leaves at the index the body left from.
        /// </para>
        /// </summary>
        private readonly List<SnareStruggleMeter> struggles = new List<SnareStruggleMeter>();

        private Vector3 flightOrigin;
        private Vector3 flightAim;
        private float flightElapsed;
        private Vector3 carriedTo;

        /// <summary>Which way the lattice is currently square to, so each step's turn is a delta.</summary>
        private Vector3 flightFacing;

        private SnarePhase phase = SnarePhase.Flight;

        /// <summary>
        /// Which phase <see cref="Tear"/> was called from, so a rotting net goes on presenting the
        /// way it was. A net that stopped drawing the moment it gave out reads as a pop, not as a
        /// net coming off — and one torn off a bound captive would hang welded to the pose the
        /// creature had when it was freed, while the creature itself runs out from under it.
        /// </summary>
        private SnarePhase tornFrom = SnarePhase.Flight;

        private float landedElapsed;
        private float cinchElapsed;
        private float settleElapsed;

        /// <summary>
        /// Where the cord is this frame once the solver has stopped. Sized from the lattice at
        /// bind time and reused, because this is written every frame for the rest of the net's life.
        /// </summary>
        private Vector3[] boundNodes;

        /// <summary>
        /// The box the bound nodes occupy, recomputed alongside them.
        ///
        /// Kept rather than derived on demand: <see cref="Footprint"/>, the ribbon's view direction
        /// and the renderer's own position all want it, and the lattice cannot answer for a net
        /// whose nodes it no longer owns.
        /// </summary>
        private Bounds boundBounds;

        /// <summary>
        /// Whose shot this was, for the two things that need to know.
        ///
        /// The impact cast ignores them: the net starts life INSIDE the player who fired it, and a
        /// cast that stops on its own owner lands every shot at the shooter's feet. And a captive
        /// reporting a struggle addresses it here — see <see cref="Shooter"/>.
        /// </summary>
        private GameObject shooter;

        private readonly RaycastHit[] impactHits = new RaycastHit[MaxImpactHits];

        private float cordWidth;
        private float groundHeight;

        /// <summary>How far into the rot this net is. Only meaningful in <see cref="SnarePhase.Tearing"/>.</summary>
        private float rotElapsed;

        private float lifeElapsed;
        private float maxLifeSeconds;
        private bool authoritative;

        /// <summary>Which net this is, so the two messages can name it. Assigned by the artifact.</summary>
        public int NetId { get; private set; }

        /// <summary>Seconds the net has left, for the HUD and for the artifact's own bookkeeping.</summary>
        public float HoldFraction => integrity?.Fraction ?? 0f;

        /// <summary>Everything currently held. The artifact reads this to broadcast.</summary>
        public IReadOnlyList<GameObject> Captives => captives;

        /// <summary>
        /// The player whose gun put this net in the world, or null offline-in-a-test.
        ///
        /// Read by <c>SnaredBody</c> so a captive can address its struggle report: the listener is
        /// <c>SnareReceiver</c>, which lives on the shooter, so the shooter is whose relay the
        /// message has to ride. See <c>NetMsg.SnareStruggled</c>.
        /// </summary>
        public GameObject Shooter => shooter;

        /// <summary>
        /// The net has finished flying and is where it is going to be.
        ///
        /// <para>
        /// Derived from <see cref="Phase"/> rather than kept beside it, so that
        /// <see cref="SnareReceiver"/> — which reads this to decide when to ask what a net came
        /// down on — cannot disagree with the net about whether it has landed.
        /// </para>
        /// <para>
        /// A torn net answers FALSE whatever it was doing when it gave out. It has released
        /// everything and has nothing to answer about, and <see cref="SnareReceiver"/> reads this to
        /// decide whether to run its capture query — so a net torn in mid-air that still claimed to
        /// have landed would have an <c>OverlapBox</c> thrown around a FLYING net's footprint,
        /// felling whatever was inside it and broadcasting <c>NetMsg.Snared</c> about a net that is
        /// a second from being destroyed.
        /// </para>
        /// <para>
        /// It is meeting something, or settling on the ground after the carry ran out. NOT the
        /// clock: it used to be the clock alone, and that is what made a landed net behave so
        /// strangely: a shot that reached the sand in a third of a second went on being dragged
        /// along the closed-form arc for the rest of the flight while the drape flattened it
        /// against the floor every frame, which is a net rolling and skidding on the spot rather
        /// than landing.
        /// </para>
        /// </summary>
        public bool HasLanded => phase != SnarePhase.Flight && phase != SnarePhase.Tearing;

        /// <summary>Where this net is in its life. The one piece of state the rest is derived from.</summary>
        public SnarePhase Phase => phase;

        /// <summary>
        /// Has the solver stopped for good?
        ///
        /// <para>
        /// The saving the wrap exists for, stated as a question the outside can ask: a draped net
        /// ran ninety substeps a second for its full thirty-second life, three at a time per gun,
        /// and a net that reached <see cref="SnarePhase.Bound"/> or <see cref="SnarePhase.Fallen"/>
        /// without freezing goes on paying that with nothing to show for it — silently, because a
        /// net that is still solving looks exactly like one that is not.
        /// </para>
        /// </summary>
        public bool Frozen => lattice != null && lattice.Frozen;

        /// <summary>
        /// This net has given out. Everything it held has been released, and it is on its way out
        /// of the world — still drawn, and still following whatever it was following, for
        /// <see cref="RotSeconds"/>. Read by the HUD, and by anything that needs to know a net it
        /// still holds a reference to is no longer holding anything.
        /// </summary>
        public bool IsTearing => phase == SnarePhase.Tearing;

        /// <summary>
        /// Seconds since touchdown, or 0 while still in the air. Read by <see cref="SnareReceiver"/>
        /// to know how long it may keep asking what this net has come down on.
        /// </summary>
        public float SecondsSinceLanding => HasLanded ? landedElapsed : 0f;

        /// <summary>
        /// The box the net actually occupies right now.
        ///
        /// Taken from the nodes rather than from an assumed square around the transform, because
        /// after the drape the net's footprint is whatever the ground and the captive under it made
        /// of it. Anything sizing a capture query off the authored half-width instead is asking a
        /// question about a shape the player cannot see.
        ///
        /// <para>
        /// Once the net is <see cref="SnarePhase.Bound"/> the lattice is frozen and its nodes are
        /// where they were at the freeze, so the answer comes from the bound nodes instead. Nothing
        /// in the game would notice today — <see cref="SnareReceiver"/> stops asking 0.8 s after
        /// touchdown and the bind lands at 0.7 s, so it only ever sees a hundred-millisecond-old
        /// box — but a footprint that quietly stopped tracking a captive who is being dragged is
        /// the kind of stale answer that is only wrong later, in whatever asks next.
        /// </para>
        /// </summary>
        public Bounds Footprint =>
            RidingBones ? boundBounds
            : lattice != null ? lattice.WorldBounds()
            : new Bounds(transform.position, Vector3.zero);

        /// <summary>
        /// Build a net at the muzzle.
        ///
        /// <paramref name="authority"/> is true only on the machine entitled to decide when the net
        /// tears. Everywhere else this runs the visual and applies what it is told.
        /// </summary>
        public void Begin(int netId, Vector3 origin, Vector3 aim, float halfWidth, float cord,
                          SnareLattice source, SnareStruggle struggleSettings, bool authority,
                          GameObject firedBy = null)
        {
            NetId = netId;
            cordWidth = cord;
            authoritative = authority;
            struggle = struggleSettings;
            shooter = firedBy;

            lattice = source;

            // The net's OPEN size. It is laid out bundle-sized and grows to this over the unfurl —
            // see SnareLattice's driven rest length. Passing the muzzle-sized figure here instead
            // would leave the net permanently the size of the thing in the canister.
            lattice.Deploy(origin, aim, halfWidth);
            lattice.Bloom();

            flightOrigin = origin;
            flightAim = aim;
            flightElapsed = 0f;
            carriedTo = origin;
            flightFacing = aim.sqrMagnitude < 1e-4f ? Vector3.forward : aim.normalized;

            phase = SnarePhase.Flight;
            tornFrom = SnarePhase.Flight;
            landedElapsed = 0f;
            cinchElapsed = 0f;
            settleElapsed = 0f;
            rotElapsed = 0f;
            boundNodes = null;

            drape = new SnareDrape();
            meshBuilder = new SnareMesh();
            binding = new SnareBinding();

            integrity = new SnareIntegrity();
            integrity.Reset(struggle.HoldSeconds);

            // The longest this net could possibly last, plus a margin. An EMPTY net is the slow
            // case: it drains at nothing but the idle rot, so it survives HoldSeconds divided by
            // that share. See the failsafe in Update for why the number is needed at all.
            lifeElapsed = 0f;
            maxLifeSeconds = NetGunFlight.MaxFlightSeconds
                           + struggle.HoldSeconds / SnareIntegrity.IdleRotShare
                           + RotSeconds + LifeMargin;

            // The renderer sits on the net itself, and Redraw is what makes that legal.
            //
            // Lattice nodes are WORLD space — the drape clamps them against world ground heights
            // and pushes them out of world-space capsules — while Unity draws a vertex buffer
            // THROUGH the renderer's transform. Feeding the raw nodes to a renderer on this object,
            // which moves to the muzzle and then follows the flight, drew the net at twice its
            // distance from the origin: fire at a player standing 500 m out and the net appeared
            // 500 m past them. It simulated, drifted and caught correctly the whole time, so
            // nothing errored and nothing looked wrong except that there was no net.
            //
            // So the mesh is built about this transform instead — see Redraw. Moving the RENDERER
            // to a pinned root object would fix the same defect, at the price of a second object
            // per net to keep alive, orphan if this one is disabled, and cull separately, and of
            // writing a 0.028 m cord in absolute coordinates four kilometres across this world.
            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

            groundHeight = SampleGround(origin);
        }

        /// <summary>Hand the renderer its material. Separated so the prefab owns the look.</summary>
        public void SetMaterial(Material material)
        {
            if (meshRenderer != null) meshRenderer.sharedMaterial = material;
        }

        /// <summary>
        /// Take hold of one body.
        ///
        /// <para>
        /// <b>Every machine runs this; only one of them DECIDES.</b> The deciding machine reaches
        /// it from <c>SnareReceiver</c>'s own landing pass and then announces the capture, and every
        /// peer reaches it from <c>SnareReceiver.OnSnared</c> on hearing that announcement. What is
        /// server-side is the choice of who is caught, not the applying of it — the hold has to be
        /// present on all of them or a captive is limp on one screen and upright on another.
        /// </para>
        /// <para>
        /// Returns false when something else already has it, or when the body refused to be put
        /// down at all, so the caller does not announce a capture that did not happen.
        /// </para>
        /// <para>
        /// Refuses anything that is neither a player nor a creature, and that guard is not
        /// decorative. The capture query runs against a layer mask a designer can set to everything,
        /// and a terrain collider answers it like any other: without this the net would put a
        /// <see cref="SnareTether"/> on the landscape, hobble it, and then hold the landscape as a
        /// captive draining the pool.
        /// </para>
        /// </summary>
        public bool Capture(GameObject body)
        {
            if (body == null || captives.Contains(body)) return false;

            bool isPlayer = body.CompareTag("Player");
            if (!isPlayer && body.GetComponentInParent<AgentController>() == null) return false;

            bool took = isPlayer
                ? SnaredBody.Ensure(body).Bind(transform, struggle)
                : SnareTether.Ensure(body).Bind(transform, struggle);

            if (!took) return false;

            captives.Add(body);
            struggles.Add(new SnareStruggleMeter(struggle.MaxUsefulStruggleRate,
                                                 struggle.StruggleDecaySeconds));

            // A net that came down on a body its own flight cast never saw. Two ways in, and
            // neither is rare: the impact sweep lives in the CARRY, so a shot at the end of its
            // range falls the last ten metres with nothing casting at all and can drop straight
            // through a creature; and the capture query runs against the whole draped footprint,
            // which is much wider than the sweep's 0.75 m centre trace, so a net that landed
            // beside an animal still catches it. Left as it is, the creature is felled and hobbled
            // while the net lies flat on the sand next to it — the same on every machine, so not a
            // desync, just a capture nothing on screen accounts for.
            //
            // Promoting back into the cinch reuses the branch an ordinary catch takes rather than
            // adding a second landing path, and it is also what lets the bind wait for a capture
            // that crossed the wire late. Only from Settling: a net already Cinching is closing on
            // this body or another, and one that is Bound has spent its binding.
            if (phase == SnarePhase.Settling) CloseAround(body);

            return true;
        }

        /// <summary>
        /// One captive fought the net, once. The authority's side of <c>NetMsg.SnareStruggled</c>.
        ///
        /// <para>
        /// Offered rather than added. The meter here carries the same cooldown the captive's own
        /// does, so a client sending a hundred a second is discarded here exactly as it would be
        /// there — which is what makes it safe for the wire to carry no magnitude at all.
        /// </para>
        /// </summary>
        /// <returns>
        /// True when this net is the one holding <paramref name="captive"/>, so a caller sweeping
        /// its live nets can stop at the first that answers. False on a machine that does not
        /// decide, which has no meter worth pushing: it waits to be told the net tore.
        /// </returns>
        public bool Struggled(GameObject captive)
        {
            if (!authoritative || captive == null) return false;

            int index = captives.IndexOf(captive);
            if (index < 0) return false;

            struggles[index].Push();
            return true;
        }

        public void ReleaseAll()
        {
            foreach (GameObject captive in captives)
            {
                if (captive == null) continue;

                if (captive.TryGetComponent(out SnaredBody snared)) snared.Release(transform);
                if (captive.TryGetComponent(out SnareTether tether)) tether.Release(transform);
            }

            captives.Clear();
            struggles.Clear();
        }

        /// <summary>
        /// Let the net rot. Called on every machine — by the artifact on hearing the message, and
        /// directly on the authority that decided it.
        /// </summary>
        public void Tear()
        {
            if (phase == SnarePhase.Tearing) return;

            ReleaseAll();
            tornFrom = phase;
            phase = SnarePhase.Tearing;
            rotElapsed = 0f;
        }

        private void Update() => Advance(Time.deltaTime);

        /// <summary>
        /// One frame of this net's life: whatever its phase does, then drain, then rot.
        ///
        /// <para>
        /// The rot is checked FIRST and returns, so a torn net stops paying the life clock, the
        /// meters and the drain it has already run out of — all three of which live below that
        /// return, which is the whole reason it is placed there. It does NOT stop presenting; see
        /// <see cref="PresentRot"/>. Everything below is what every live phase shares;
        /// <see cref="RunPhase"/> holds what each one does differently.
        /// </para>
        /// <para>
        /// Public because the EditMode tests compile into Assembly-CSharp-Editor, which cannot see
        /// internals of Assembly-CSharp — the same seam <see cref="SnareLattice.Step"/> exposes for
        /// the same reason. A net is the one piece of this item that only means anything assembled,
        /// so there has to be a way to run a whole one without a play session: the defect this seam
        /// was added for lived entirely in how the parts fit together, and every one of them passed
        /// its own test while the net was invisible.
        /// </para>
        /// </summary>
        public void Advance(float delta)
        {
            if (lattice == null) return;

            if (phase == SnarePhase.Tearing)
            {
                rotElapsed += delta;

                // The life clock, the meters and the drain are what a torn net must stop paying,
                // and they all live below. The DRAWING is not: a net that stopped presenting the
                // moment it gave out hangs in whatever pose it had and then pops.
                PresentRot(delta);

                if (rotElapsed >= RotSeconds) Destroy(gameObject);
                return;
            }

            RunPhase(delta);

            lifeElapsed += delta;

            // The failsafe, and it runs on every machine rather than only the authority.
            //
            // A peer's net never drains — it waits to be told it has torn — so if that message
            // never arrives the net holds its captives forever. It does not arrive when the shooter
            // despawns with nets live: the announcement goes out on the SHOOTER's relay, and a
            // player being destroyed has no relay left to send from. Nothing can be sent at that
            // moment, by anyone, so the only honest answer is for each net to know its own worst
            // case and stop by itself. See SnareReceiver.OnDisable, which handles the local half.
            if (lifeElapsed >= maxLifeSeconds) Tear();

            if (!authoritative) return;

            // Advanced before the drain, and only here: this is the one machine that both counts
            // the struggle and spends the pool, so a peer never runs a meter it would then have
            // nothing to do with.
            foreach (SnareStruggleMeter meter in struggles) meter.Advance(delta);

            integrity.Drain(StrugglingMass(), delta);
            if (integrity.IsSpent) Tear();
        }

        /// <summary>
        /// One frame of whichever phase the net is in. Everything that is NOT per-phase — the
        /// failsafe, the meters, the drain — stays in <see cref="Advance"/>, so each phase here
        /// says only what makes it different from the others.
        ///
        /// <para>
        /// <see cref="SnarePhase.Tearing"/> is absent on purpose: it is handled before this is
        /// called, because it is the one phase that must not go on paying for a life clock it has
        /// already run out of.
        /// </para>
        /// </summary>
        private void RunPhase(float delta)
        {
            switch (phase)
            {
                case SnarePhase.Flight:
                    CarryAlongFlight(delta);

                    // The carry may have landed the net. Both landings resolve the contact frame
                    // themselves — the cinch by starting, the ground landing by draping — so
                    // running this frame's solve on top would be a second solve of one substep.
                    if (phase != SnarePhase.Flight) break;

                    RefreshProxies();
                    lattice.Simulate(delta);

                    // Before the drape, which clamps against exactly this height.
                    if (CarrySpent) TrackFallingNet();

                    drape.Resolve(lattice, proxies, groundHeight);

                    // After the drape, because the drape is what decides which nodes are touching.
                    lattice.GripGround(groundHeight);

                    // The flight clock is a RANGE limit, not a landing — see
                    // NetGunFlight.MaxFlightSeconds. A net whose carry runs out in open air, which
                    // is every shot aimed above the horizontal, still has to fall: freezing it at
                    // the moment the arc stops leaves it hanging where nothing is holding it.
                    if (CarrySpent && ReachedTheGround())
                    {
                        // LandOnGround redraws the contact frame itself, so there is nothing left.
                        LandOnGround();
                        break;
                    }

                    Redraw();
                    break;

                case SnarePhase.Cinching:
                    landedElapsed += delta;
                    cinchElapsed += delta;

                    RefreshProxies();
                    lattice.Simulate(delta);

                    // No GripGround here, unlike the flight. The cinch is a purse-seine: its whole
                    // job is to draw the hem inward ACROSS the sand, and the grip exists to stop a
                    // node moving sideways along it.
                    drape.Resolve(lattice, proxies, groundHeight);

                    Redraw();

                    // The window has to close. A net left solving is the cost the wrap exists to
                    // remove, and there is nothing in a relaxed constraint that ever says "done".
                    if (ReadyToBind) Bind();
                    break;

                case SnarePhase.Bound:
                    landedElapsed += delta;
                    PruneCaptives();

                    ResolveBound();
                    break;

                case SnarePhase.Settling:
                    landedElapsed += delta;
                    settleElapsed += delta;

                    RefreshProxies();
                    lattice.Simulate(delta);

                    // The same reason as the fall above, for a smaller distance: a net sliding down
                    // a cliff face or off the shoulder of a dune ends up over ground that is not
                    // the ground it first touched.
                    TrackFallingNet();

                    drape.Resolve(lattice, proxies, groundHeight);

                    // After the drape, because the drape is what decides which nodes are touching.
                    // This is what stops a net that arrived with speed skating along the surface,
                    // and it is why the settle terminates rather than drifting forever.
                    lattice.GripGround(groundHeight);

                    Redraw();

                    if (settleElapsed >= SettlingSeconds) Settle();
                    break;

                case SnarePhase.Fallen:
                    landedElapsed += delta;
                    PruneCaptives();

                    // No Redraw. The lattice is frozen, so every node is exactly where the last
                    // draw put it — rebuilding the same mesh sixty times a second for the rest of
                    // the net's half-minute is the work this phase exists to stop paying.
                    break;
            }
        }

        /// <summary>
        /// May the cinch close now?
        ///
        /// <para>
        /// Two conditions, and keeping them apart is the point — see <see cref="MaxCinchSeconds"/>.
        /// The ordinary bind waits for the feel clock AND for a captive to exist to be bound to,
        /// because on a peer the capture arrives over the wire and can be later than the clock. The
        /// backstop is what stops a capture that never comes at all from leaving the solver running
        /// for the net's whole life.
        /// </para>
        /// <para>
        /// A cinch that keeps going while it waits is visually free: the ring is already closed at
        /// <see cref="CinchSeconds"/> and the solver is only holding it there.
        /// </para>
        /// </summary>
        private bool ReadyToBind =>
            cinchElapsed >= MaxCinchSeconds
            || (cinchElapsed >= CinchSeconds && captives.Count > 0);

        /// <summary>
        /// What a net still does while it rots away.
        ///
        /// <para>
        /// Presentation only. A torn net has already given everything back — <see cref="Tear"/>
        /// runs <see cref="ReleaseAll"/> — so there is nothing here that could catch, hold or bill
        /// anybody, and the things it must STOP paying (the life clock, the struggle meters, the
        /// integrity drain) all live in <see cref="Advance"/> below the call that brings it here.
        /// </para>
        /// <para>
        /// It presents the way it was presenting, which is why <see cref="tornFrom"/> is kept. A
        /// bound net goes on riding its captive's bones, so the cord comes off the body with them
        /// rather than hanging in the pose the creature had at the instant it was freed; a net torn
        /// in the air or mid-close goes on solving, so it falls rather than hovering; a net torn
        /// where it lay is frozen and its last draw already stands.
        /// </para>
        /// </summary>
        private void PresentRot(float delta)
        {
            switch (tornFrom)
            {
                case SnarePhase.Bound:
                    ResolveBound();
                    break;

                case SnarePhase.Fallen:
                    break;

                default:
                    // Flight, Cinching and Settling alike: the solver was live and stopping it now
                    // would freeze the net in mid-air for the length of the rot. RefreshProxies
                    // finds an empty captive list — ReleaseAll cleared it — so the drape has only
                    // the ground left to work against, which is the right answer for cord that is
                    // no longer holding anything.
                    RefreshProxies();
                    lattice.Simulate(delta);
                    drape.Resolve(lattice, proxies, groundHeight);
                    lattice.GripGround(groundHeight);
                    Redraw();
                    break;
            }
        }

        /// <summary>
        /// Keep the ground sample and the renderer under a net that is coming down on its own.
        ///
        /// <para>
        /// The carry resampled the ground and moved the transform on every step; once it is spent
        /// neither happens, and what follows is not a short drop. A 25-degree shot's carry ends
        /// about ten metres up still carrying most of its forward speed, so the net comes down
        /// eleven or twelve metres beyond the last place the ground was measured. Over a dune that
        /// is metres of error in the SINGLE plane the whole drape clamps against: too high and the
        /// net freezes against a floor that is not there, too low and it sinks into the sand.
        /// </para>
        /// <para>
        /// The transform follows for the float-precision reason <see cref="ResolveBound"/> gives —
        /// otherwise the mesh is built about an origin ten metres from the net for the length of
        /// the fall and the whole settle after it.
        /// </para>
        /// </summary>
        private void TrackFallingNet()
        {
            Vector3 centre = lattice.Centre();
            transform.position = centre;

            // Only on a real hit — see TrySampleGround. Measured from the net's OWN centre, a miss
            // answering "the height you asked from" is a floor that falls with the net.
            if (TrySampleGround(centre, out float height)) groundHeight = height;
        }

        /// <summary>Has the arc run out of momentum? The net still falls; it is only the carry that ends.</summary>
        private bool CarrySpent => flightElapsed >= NetGunFlight.MaxFlightSeconds;

        /// <summary>
        /// Has the falling net met the floor under it?
        ///
        /// The LOWEST node rather than the centre, because the hem is what arrives first — a net
        /// that waited for its middle to reach the ground would have driven its whole skirt through
        /// the sand by then.
        /// </summary>
        private bool ReachedTheGround() =>
            lattice.WorldBounds().min.y <= groundHeight + GroundContact;

        /// <summary>
        /// The net came down on something it cannot hold. Let it lie down.
        ///
        /// <para>
        /// This does NOT freeze. A net frozen on the frame it made contact keeps the shape of a
        /// sheet arriving: <see cref="ImpactRadius"/> reports the hit with the net's centre three
        /// quarters of a metre up, and a drape only lifts nodes that are already under the ground,
        /// so the middle stays exactly where the arc left it. <see cref="SnarePhase.Settling"/> is
        /// the window in which it flattens, and <see cref="Settle"/> is what ends it.
        /// </para>
        /// <para>
        /// The contact frame is resolved here rather than left to the next one, because
        /// <see cref="RunPhase"/> leaves the Flight branch the instant the phase changes: without
        /// this the net is drawn for one substep in the pose it had before it touched anything.
        /// </para>
        /// <para>
        /// It takes no ground height. Both callers have just measured one into
        /// <c>groundHeight</c> — the contact path from the touchdown point, the fall from
        /// <see cref="TrackFallingNet"/> — and a parameter they could only ever fill from that same
        /// field is a second way to say one thing.
        /// </para>
        /// </summary>
        private void LandOnGround()
        {
            if (phase != SnarePhase.Flight) return;

            phase = SnarePhase.Settling;
            landedElapsed = 0f;
            settleElapsed = 0f;

            RefreshProxies();
            drape.Resolve(lattice, proxies, groundHeight);
            lattice.GripGround(groundHeight);
            Redraw();
        }

        /// <summary>
        /// The net has stopped moving. Stop solving it, for good.
        ///
        /// No draw and no transform move: <see cref="TrackFallingNet"/> and <c>Redraw</c> both ran
        /// this same frame in the Settling branch, and <see cref="SnareLattice.Freeze"/> moves
        /// nothing — it pins <c>prev</c> to <c>pos</c>. So the last Settling draw is already the
        /// frozen shape, and <see cref="SnarePhase.Fallen"/> makes no more.
        /// </summary>
        private void Settle()
        {
            if (phase != SnarePhase.Settling) return;

            lattice.Freeze();
            phase = SnarePhase.Fallen;
        }

        /// <summary>
        /// The net touched a body. Start closing around it.
        ///
        /// <para>
        /// <b>The axis is sampled here, once.</b> The body is about to topple — that is the point
        /// of hitting it — and a cinch frame that tumbled with it would sweep the ring, and every
        /// node the ring holds, through the ground.
        /// </para>
        /// <para>
        /// Straight up, and NOT <c>body.transform.up</c>. Three reasons, and the first alone
        /// settles it: the collider a cast meets is very often a ragdoll limb whose local up is
        /// whichever way that bone was modelled, so the ring would be pitched at an angle nothing
        /// chose. The second is that the victim may already be part-way down by the time the net
        /// arrives, and a ring tipped over with them closes
        /// around a diagonal line through the sand. The third is what the cinch IS: a purse-seine
        /// draw-string gathering a hem that is spread out horizontally underneath a body. Along
        /// any other axis it gathers cord into a tube around the body's length, which is the drawn
        /// capsule <see cref="SnareCinch"/> exists to refuse.
        /// </para>
        /// </summary>
        private void LandOnBody(GameObject body)
        {
            if (phase != SnarePhase.Flight) return;

            landedElapsed = 0f;
            CloseAround(body);
        }

        /// <summary>
        /// Start the ring closing on one body, from wherever the net currently is.
        ///
        /// Shared by the flight's own contact and by a capture that arrives after the net has
        /// already come down — see <see cref="Capture"/>. It does not touch
        /// <see cref="landedElapsed"/>: that clock belongs to the landing, and a net promoted out
        /// of <see cref="SnarePhase.Settling"/> landed some time ago.
        /// </summary>
        private void CloseAround(GameObject body)
        {
            phase = SnarePhase.Cinching;
            cinchElapsed = 0f;

            lattice.BeginCinch(new SnareCinch.Axis(body.transform.position, Vector3.up),
                               CinchRadius, CinchSeconds);
        }

        /// <summary>
        /// The cinch window is over: stop solving, and nail the cord to the captive's bones.
        ///
        /// <para>
        /// The first captive, of however many the net swept up, because there is one binding and it
        /// can only ride one skeleton. A net across two bodies follows the one it is most likely to
        /// have closed on — the capture pass records them in the order the overlap query returned
        /// them, so this is arbitrary rather than chosen, and the alternative is cord that tears in
        /// half between two ragdolls walking apart.
        /// </para>
        /// <para>
        /// Falls back to <see cref="SnarePhase.Fallen"/> when there is nothing to bind to, and that
        /// is not defensive tidiness. A rig can measure a real skeleton and keep NONE of it — the
        /// weight floor and the bone cap can trim every candidate away, and <c>RagdollRig.Build</c>
        /// then returns having logged nothing at all. A net that bound to that would hang in the
        /// air exactly where the creature used to be, with no error anywhere to say why.
        /// </para>
        /// <para>
        /// The fallback goes straight to <see cref="SnarePhase.Fallen"/> and skips
        /// <see cref="SnarePhase.Settling"/>, which a ground landing does not. That is not an
        /// oversight: a net reaching here has already been solving in contact with the body for at
        /// least <see cref="CinchSeconds"/>, so it is not the case the settle window exists for —
        /// a net frozen on the frame it first touched something.
        /// </para>
        /// </summary>
        private void Bind()
        {
            GameObject captive = captives.Count > 0 ? captives[0] : null;
            RagdollRig rig = captive != null ? captive.GetComponentInParent<RagdollRig>() : null;

            if (rig != null && rig.HasSkeleton)
            {
                binding.Capture(lattice.Positions, rig.BoneTransforms());
                boundNodes = new Vector3[lattice.Positions.Length];
            }

            if (!binding.IsBound)
            {
                boundNodes = null;

                // Nothing to nail the cord to, so the net has to come DOWN — and the freeze is
                // therefore the last thing that may happen, not the first. Freezing before this is
                // known leaves the net exactly where the cinch left it: gathered in the air around
                // a body at chest height, held up by nothing, over a creature that walks out of it
                // hobbled. The settle window is the phase that resolves precisely that shape, so
                // the fallback goes back through it.
                lattice.EndCinch();
                if (TrySampleGround(lattice.Centre(), out float height)) groundHeight = height;

                phase = SnarePhase.Settling;
                settleElapsed = 0f;
                return;
            }

            lattice.Freeze();
            phase = SnarePhase.Bound;

            // Resolved HERE and not left to the next frame's Advance. Footprint answers from the
            // bound box the moment this phase is entered, and SnareReceiver is still asking it
            // what this net came down on for another tenth of a second — its settle window is 0.8 s
            // against this bind at 0.7 s. An unpopulated box is a zero-sized one at the WORLD
            // ORIGIN, so a receiver that asked before the net's own Update had run would size its
            // capture query around (0,0,0) and net whatever happens to be standing there.
            ResolveBound();
        }

        /// <summary>
        /// Where the cord is this frame, for a net riding a captive's bones.
        ///
        /// The transform goes with it — the nodes ride a body that can be dragged, by a leash or by
        /// its own ragdoll sliding down a dune, while the mesh is built about this transform for the
        /// float precision <c>SnareMesh.Build</c> documents. Left at the touchdown point, a captive
        /// hauled a few hundred metres is drawn from vertices that far out, and a 0.028 m cord
        /// written at that distance is a handful of mantissa bits from the width it was authored at.
        /// The renderer's culling bounds come from the same mesh, so they grow to match rather than
        /// tracking the net.
        /// </summary>
        private void ResolveBound()
        {
            binding.Resolve(boundNodes);
            boundBounds = BoxAround(boundNodes);
            transform.position = boundBounds.center;

            Redraw();
        }

        /// <summary>Is this net's cord coming from a captive's bones rather than from the solver?</summary>
        private bool RidingBones => boundNodes != null;

        /// <summary>The box a set of nodes occupies. The lattice's own version, for nodes it no longer owns.</summary>
        private static Bounds BoxAround(Vector3[] nodes)
        {
            if (nodes == null || nodes.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            var box = new Bounds(nodes[0], Vector3.zero);
            for (int i = 1; i < nodes.Length; i++) box.Encapsulate(nodes[i]);
            return box;
        }

        /// <summary>
        /// Move the whole net along its arc while it is in the air.
        ///
        /// <para>
        /// By the DIFFERENCE between two samples of the closed-form flight, applied to every node
        /// at once, so the net travels without the solver noticing it moved — see
        /// <see cref="SnareLattice.Translate"/>. The unfurl and the bloom go on underneath, which
        /// is what makes the net open as it flies rather than after it lands.
        /// </para>
        /// <para>
        /// Nothing about this is sent. Origin, aim and seed all arrived with the press and
        /// <see cref="NetGunFlight"/> is pure, so every machine carries its own net along the
        /// identical path and arrives at the same place.
        /// </para>
        /// <para>
        /// <b>Do not replace this with a muzzle velocity.</b> Handing the lattice an initial speed
        /// and letting its own integrator fly the net is the obvious simplification, it is shorter,
        /// and it is wrong: the lattice takes whole FIXED substeps out of real frame deltas, so two
        /// machines running at different frame rates take different numbers of them and their nets
        /// land in different places. The catch would then be decided against a net the other player
        /// can see is somewhere else. Carrying the whole lattice along a closed-form arc is what
        /// makes the flight identical everywhere, and it is the entire reason
        /// <see cref="NetGunFlight"/> is a pure function rather than a physics step.
        /// </para>
        /// </summary>
        private void CarryAlongFlight(float delta)
        {
            // The carry is spent but the net has not landed: it is falling under the solver's own
            // gravity now, and RunPhase is watching for it to reach the ground. Sampling the arc
            // again would only hand back the same clamped position.
            if (CarrySpent) return;

            flightElapsed = Mathf.Min(flightElapsed + delta, NetGunFlight.MaxFlightSeconds);

            Vector3 next = NetGunFlight.PositionAt(flightOrigin, flightAim, NetId, flightElapsed);

            // What the net MEETS ends the flight. The clock only ends the CARRY.
            bool struck = TryFindImpact(carriedTo, next, out Vector3 touchdown, out Collider met);
            if (struck) next = touchdown;

            lattice.Translate(next - carriedTo);
            carriedTo = next;
            FaceAlongFlight();

            transform.position = next;
            groundHeight = SampleGround(next);

            // Handed over on both endings, because both of them stop the carry: without it the net
            // stops dead in the air and drops straight down like a cut curtain instead of folding
            // forward over what is in front of it.
            if (!struck && !CarrySpent) return;

            lattice.Impart(NetGunFlight.VelocityAt(flightAim, NetId, flightElapsed) * ImpactCarryShare);

            if (!struck) return;

            GameObject victim = CatchableBody(met);

            if (victim != null) LandOnBody(victim);
            else LandOnGround();
        }

        /// <summary>
        /// Turn the net from the pane it left the barrel as into the sheet it has to land as.
        ///
        /// <para>
        /// <see cref="SnareLattice.Deploy"/> lays the sheet out perpendicular to the aim, which is
        /// right at the muzzle — the net leaves edge-on, the way a thrown cast net does — and wrong
        /// by the time it arrives, because a net that meets the ground edge-on does not lie down on
        /// it. It buckles into a strip, and that strip is also the <see cref="Footprint"/>
        /// <c>SnareReceiver</c> sizes its capture query from.
        /// </para>
        /// <para>
        /// <b>The turn is driven, not inherited from the arc.</b> This used to follow the
        /// closed-form velocity alone and claim in this comment that doing so tipped the net over on
        /// the way down. It does not, and has not since the flight was retuned: at
        /// <see cref="NetGunFlight.MuzzleSpeed"/> 32 m/s under <see cref="NetGunFlight.Gravity"/>
        /// 7 m/s² the velocity turns by <c>atan(7 × 0.85 / 32)</c> — about ten and a half degrees
        /// over the whole carry — so the net arrived within 10° of vertical. Measured off the
        /// shipped tuning, a level shot landed 4.4 m wide by <b>1.2 m deep</b> where the net is
        /// 6 m across: a hank of rope, not a net, holding a capture box 1.2 m deep.
        /// </para>
        /// <para>
        /// A DELTA from the last facing rather than an absolute orientation, because the lattice
        /// has a shape of its own by now — bloomed, unfurled, fluttering — and an absolute set
        /// would throw that away every frame.
        /// </para>
        /// <para>
        /// Swung about the horizontal hinge across the flight rather than by interpolating toward
        /// <c>Vector3.down</c>, so a shot fired straight up — an ordinary shot, not an edge case,
        /// for the reason <see cref="SnareLattice.Deploy"/> gives — still has a well-defined axis
        /// to turn about instead of passing through a degenerate midpoint.
        /// </para>
        /// </summary>
        private void FaceAlongFlight()
        {
            Vector3 travel = NetGunFlight.VelocityAt(flightAim, NetId, flightElapsed);
            if (travel.sqrMagnitude < 1e-4f) return;

            travel.Normalize();

            float turned = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(flightElapsed / (NetGunFlight.MaxFlightSeconds * FaceDownShare)));

            Vector3 flat = new Vector3(travel.x, 0f, travel.z);
            Vector3 hinge = flat.sqrMagnitude > 1e-6f
                ? Vector3.Cross(Vector3.up, flat).normalized
                : Vector3.right;

            Vector3 heading =
                Quaternion.AngleAxis(Vector3.Angle(travel, Vector3.down) * turned, hinge) * travel;

            lattice.RotateAbout(carriedTo, Quaternion.FromToRotation(flightFacing, heading));
            flightFacing = heading;
        }

        /// <summary>
        /// Whatever this step of the arc runs into, if anything.
        ///
        /// <para>
        /// Swept rather than sampled: the net covers half a metre in a frame at muzzle speed, and a
        /// test that only asked whether the endpoint had ended up inside something would step
        /// straight through any wall thinner than that.
        /// </para>
        /// <para>
        /// The shooter is skipped, and that is not a nicety. The net is born at the muzzle, which
        /// is inside the player holding it, so an unfiltered cast reports a hit at zero distance on
        /// the very first step and every shot in the game lands at the shooter's own feet.
        /// </para>
        /// </summary>
        private bool TryFindImpact(Vector3 from, Vector3 to, out Vector3 point, out Collider hit)
        {
            point = to;
            hit = null;

            Vector3 step = to - from;
            float distance = step.magnitude;
            if (distance < 1e-4f) return false;

            Vector3 direction = step / distance;
            int found = Physics.SphereCastNonAlloc(from, ImpactRadius, direction, impactHits,
                                                   distance, ~0, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;

            for (int i = 0; i < found; i++)
            {
                Collider met = impactHits[i].collider;
                if (met == null) continue;
                if (shooter != null && met.transform.IsChildOf(shooter.transform)) continue;
                if (impactHits[i].distance >= nearest) continue;

                nearest = impactHits[i].distance;
                hit = met;
            }

            if (hit == null) return false;

            point = from + direction * nearest;
            return true;
        }

        /// <summary>
        /// The body worth closing around, resolved up the hierarchy from whatever the cast met —
        /// or null when the net hit the world.
        ///
        /// <para>
        /// Walked UP rather than read off the collider, because the collider is very often a
        /// ragdoll limb. A bone carries neither the Player tag nor an <see cref="AgentController"/>,
        /// so a net asking the bone would decide it had hit scenery and freeze flat across a body
        /// it is standing on — which looks exactly like a net that landed correctly, right up until
        /// nothing is ever held.
        /// </para>
        /// <para>
        /// Deliberately NOT <c>attachedRigidbody</c>, which is how <see cref="SnareReceiver"/>
        /// resolves the same question. On a ragdoll every bone has its own Rigidbody, so that route
        /// answers with the limb; here the whole point is to reach the thing the limb belongs to.
        /// </para>
        /// </summary>
        private static GameObject CatchableBody(Collider met)
        {
            if (met == null) return null;

            for (Transform step = met.transform; step != null; step = step.parent)
            {
                if (step.CompareTag("Player")) return step.gameObject;
                if (step.TryGetComponent(out AgentController _)) return step.gameObject;
            }

            return null;
        }

        /// <summary>
        /// Drop captives the world has destroyed under this net.
        ///
        /// <para>
        /// Split out of <see cref="RefreshProxies"/> because the two halves have different
        /// lifetimes. The PROXIES are only ever read by the drape, which stops running the moment
        /// the net freezes; the pruning has to go on regardless, or a net that binds and then
        /// outlives its captive by half a minute keeps naming a destroyed GameObject in
        /// <see cref="Captives"/> and in <see cref="Struggled"/>'s lookup.
        /// </para>
        /// <para>
        /// Backwards, and both lists at the same index, which is what keeps
        /// <see cref="struggles"/> aligned with <see cref="captives"/>.
        /// </para>
        /// </summary>
        private void PruneCaptives()
        {
            for (int i = captives.Count - 1; i >= 0; i--)
            {
                if (captives[i] != null) continue;

                // The meter leaves with the body, so the two lists stay index-aligned.
                captives.RemoveAt(i);
                struggles.RemoveAt(i);
            }
        }

        private void RefreshProxies()
        {
            PruneCaptives();

            proxies.Clear();
            for (int i = 0; i < captives.Count; i++) proxies.Add(SnareDrape.ProxyFor(captives[i]));
        }

        /// <summary>
        /// What the net is holding, weighed the way <see cref="SnareIntegrity.Drain"/> reads it.
        ///
        /// <para>
        /// A creature needs no meter, and that is not an omission. <see cref="SnareTether.Mass"/>
        /// already scales with what the animal weighs, so an ant and a six-legged habitat present
        /// wildly different loads without either pressing a key — the weight IS the struggle. A
        /// player weighs the same whatever they do, so theirs is the branch the multiplier belongs
        /// on: a captive fighting flat out presents <c>1 + StruggleMultiplier</c> captives' worth,
        /// which is what turns mashing into getting out.
        /// </para>
        /// <para>
        /// Nothing about <see cref="SnareIntegrity"/> changes for this. It already takes the
        /// greater of the idle rot and <c>load / ReferenceLoad</c>; a struggling captive simply
        /// arrives as more mass.
        /// </para>
        /// </summary>
        private float StrugglingMass()
        {
            float total = 0f;

            for (int i = 0; i < captives.Count; i++)
            {
                GameObject captive = captives[i];
                if (captive == null) continue;

                if (captive.TryGetComponent(out SnareTether tether))
                {
                    total += tether.Mass;
                    continue;
                }

                total += SnareIntegrity.ReferenceLoad
                       * (1f + struggle.StruggleMultiplier * struggles[i].Level);
            }

            return total;
        }

        private void Redraw()
        {
            // A bound net's cord comes from the bones, not from the solver, and everything
            // downstream — the winding, the view direction, the origin correction — is the same
            // for both. SnareMesh takes the nodes rather than the lattice so there is still only
            // one ribbon winding in the project.
            Vector3[] nodes = RidingBones ? boundNodes : lattice.Positions;
            Vector3 centre = RidingBones ? boundBounds.center : lattice.Centre();

            Camera view = Camera.main;

            // Camera MINUS centre. The ribbon's front face comes out along this vector, so getting
            // it backwards winds every quad in the net the wrong way and — under ordinary culling —
            // the net renders as nothing at all: no error, no warning, a shot that fires and
            // produces an invisible catch. SnareMesh names the parameter `toViewer` for that
            // reason and pins the direction with a test.
            Vector3 toViewer = view != null ? view.transform.position - centre : Vector3.back;

            // Built about this transform, because the nodes are world space and Unity draws the
            // vertex buffer THROUGH the transform. Redraw runs last in each phase for that reason:
            // the carry and the bind both move the transform, and a mesh built about where it used
            // to be is a net drawn a frame's travel behind itself.
            meshFilter.sharedMesh = meshBuilder.Build(nodes, lattice.Resolution, toViewer,
                                                      cordWidth, transform.position);
        }

        /// <summary>
        /// One raycast for the whole net rather than one per node.
        ///
        /// Two hundred and twenty-five raycasts a substep is not a budget that exists, and over six
        /// metres of this game's terrain a single height plus the captive capsules is within a
        /// hand's width of the truth everywhere the difference would show.
        /// </summary>
        private static float SampleGround(Vector3 around) =>
            TrySampleGround(around, out float height) ? height : around.y;

        /// <summary>
        /// The same probe, saying whether it found anything.
        ///
        /// <para>
        /// <see cref="SampleGround"/>'s answer for a miss is the sample point's own height, which is
        /// a harmless placeholder for a net being carried along an arc — it is measured once per
        /// step from a position the flight decides, so nothing feeds back. It is NOT harmless for a
        /// net measuring from its own centre: the drape would clamp against a floor that follows the
        /// net down, and the net would never land at all. Anything sampling from where the net
        /// already is has to keep its last real answer instead.
        /// </para>
        /// </summary>
        private static bool TrySampleGround(Vector3 around, out float height)
        {
            bool found = Physics.Raycast(around + Vector3.up * ProbeRise, Vector3.down,
                                         out RaycastHit hit, ProbeReach,
                                         ~0, QueryTriggerInteraction.Ignore);

            height = found ? hit.point.y : around.y;
            return found;
        }

        /// <summary>
        /// A chunk unloading under a live net must not leave its captives hobbled forever, so this
        /// releases on the way out rather than trusting the rot timer to get there first.
        /// </summary>
        private void OnDisable() => ReleaseAll();

        private void OnDestroy() => meshBuilder?.Dispose();
    }
}
