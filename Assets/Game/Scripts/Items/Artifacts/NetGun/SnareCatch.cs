using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;

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

        private SnareLattice lattice;
        private SnareDrape drape;
        private SnareMesh meshBuilder;
        private SnareStruggle struggle;
        private SnareIntegrity integrity;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private readonly List<GameObject> captives = new List<GameObject>();
        private readonly List<SnareDrape.Capsule> proxies = new List<SnareDrape.Capsule>();

        private Vector3 flightOrigin;
        private Vector3 flightAim;
        private float flightElapsed;
        private Vector3 carriedTo;

        /// <summary>Which way the lattice is currently square to, so each step's turn is a delta.</summary>
        private Vector3 flightFacing;

        private bool landed;
        private float landedElapsed;

        /// <summary>
        /// Whose shot this was. Held only so the impact cast can ignore them: the net starts life
        /// INSIDE the player who fired it, and a cast that stops on its own owner lands every shot
        /// at the shooter's feet.
        /// </summary>
        private GameObject shooter;

        private readonly RaycastHit[] impactHits = new RaycastHit[MaxImpactHits];

        private float cordWidth;
        private float groundHeight;
        private float rotElapsed = -1f;
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
        /// The net has finished flying and is where it is going to be.
        ///
        /// Set by meeting something, or by running out of flight — not by a clock alone. It used to
        /// be the clock alone, and that is what made a landed net behave so strangely: a shot that
        /// reached the sand in a third of a second went on being dragged along the closed-form arc
        /// for the rest of the flight while the drape flattened it against the floor every frame,
        /// which is a net rolling and skidding on the spot rather than landing.
        /// </summary>
        public bool HasLanded => landed;

        /// <summary>
        /// Seconds since touchdown, or 0 while still in the air. Read by <see cref="SnareReceiver"/>
        /// to know how long it may keep asking what this net has come down on.
        /// </summary>
        public float SecondsSinceLanding => landed ? landedElapsed : 0f;

        /// <summary>
        /// The box the net actually occupies right now.
        ///
        /// Taken from the nodes rather than from an assumed square around the transform, because
        /// after the drape the net's footprint is whatever the ground and the captive under it made
        /// of it. Anything sizing a capture query off the authored half-width instead is asking a
        /// question about a shape the player cannot see.
        /// </summary>
        public Bounds Footprint => lattice != null
            ? lattice.WorldBounds()
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
            landed = false;
            landedElapsed = 0f;

            drape = new SnareDrape();
            meshBuilder = new SnareMesh();

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
        /// Take hold of one body. Server-side only — the peers are told, they do not decide.
        ///
        /// <para>
        /// Returns false when something else already has it, so the caller does not announce a
        /// capture that did not happen.
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
        }

        /// <summary>
        /// Let the net rot. Called on every machine — by the artifact on hearing the message, and
        /// directly on the authority that decided it.
        /// </summary>
        public void Tear()
        {
            if (rotElapsed >= 0f) return;

            ReleaseAll();
            rotElapsed = 0f;
        }

        private void Update() => Advance(Time.deltaTime);

        /// <summary>
        /// One frame of this net's life: fly, drape, redraw, drain, rot.
        ///
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

            CarryAlongFlight(delta);
            RefreshProxies();
            DragTowardCaptives(delta);

            lattice.Simulate(delta);
            drape.Resolve(lattice, proxies, groundHeight);

            // After the drape, because the drape is what decides which nodes are touching at all.
            lattice.GripGround(groundHeight);

            Redraw();

            if (rotElapsed >= 0f)
            {
                rotElapsed += delta;
                if (rotElapsed >= RotSeconds) Destroy(gameObject);
                return;
            }

            lifeElapsed += delta;

            // The failsafe, and it runs on every machine rather than only the authority.
            //
            // A peer's net never drains — it waits to be told it has torn — so if that message
            // never arrives the net holds its captives forever. It does not arrive when the shooter
            // despawns with nets live: the announcement goes out on the SHOOTER's relay, and a
            // player being destroyed has no relay left to send from. Nothing can be sent at that
            // moment, by anyone, so the only honest answer is for each net to know its own worst
            // case and stop by itself. See SnareReceiver.OnDisable, which handles the local half.
            if (rotElapsed < 0f && lifeElapsed >= maxLifeSeconds) Tear();

            if (!authoritative) return;

            integrity.Drain(StrugglingMass(), delta);
            if (integrity.IsSpent) Tear();
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
            if (landed) { landedElapsed += delta; return; }

            flightElapsed = Mathf.Min(flightElapsed + delta, NetGunFlight.MaxFlightSeconds);

            Vector3 next = NetGunFlight.PositionAt(flightOrigin, flightAim, NetId, flightElapsed);

            // What the net MEETS ends the flight. The clock is only the backstop behind that.
            bool struck = TryFindImpact(carriedTo, next, out Vector3 touchdown);
            if (struck) next = touchdown;

            lattice.Translate(next - carriedTo);
            carriedTo = next;
            FaceAlongFlight();

            transform.position = next;
            groundHeight = SampleGround(next);

            if (struck || flightElapsed >= NetGunFlight.MaxFlightSeconds) Land();
        }

        /// <summary>
        /// Keep the net square to where it is going rather than to where it was fired.
        ///
        /// <para>
        /// <see cref="SnareLattice.Deploy"/> lays the sheet out perpendicular to the aim, which is
        /// right at the muzzle and wrong everywhere after it: the arc bends downward and the aim
        /// does not, so a net that never turns sails the whole way as an upright pane and then
        /// meets the ground edge-on. Turning it by the closed-form velocity each step is what makes
        /// it tip over on the way down and land face-first, the way a cast net does.
        /// </para>
        /// <para>
        /// A DELTA from the last facing rather than an absolute orientation, because the lattice
        /// has a shape of its own by now — bloomed, unfurled, fluttering — and an absolute set
        /// would throw that away every frame.
        /// </para>
        /// </summary>
        private void FaceAlongFlight()
        {
            Vector3 heading = NetGunFlight.VelocityAt(flightAim, NetId, flightElapsed);
            if (heading.sqrMagnitude < 1e-4f) return;

            heading.Normalize();
            lattice.RotateAbout(carriedTo, Quaternion.FromToRotation(flightFacing, heading));
            flightFacing = heading;
        }

        /// <summary>Touchdown: stop carrying the net, and hand it what the carry was providing.</summary>
        private void Land()
        {
            if (landed) return;

            landed = true;
            landedElapsed = 0f;

            lattice.Impart(NetGunFlight.VelocityAt(flightAim, NetId, flightElapsed) * ImpactCarryShare);
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
        private bool TryFindImpact(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = to;

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
            }

            if (nearest == float.MaxValue) return false;

            point = from + direction * nearest;
            return true;
        }

        private void RefreshProxies()
        {
            proxies.Clear();

            for (int i = captives.Count - 1; i >= 0; i--)
            {
                if (captives[i] == null) { captives.RemoveAt(i); continue; }
                proxies.Add(SnareDrape.ProxyFor(captives[i]));
            }
        }

        /// <summary>
        /// Let the captives pull the net about.
        ///
        /// Without this a captive that shuffles walks out from under a net pinned where it landed,
        /// which is the single most obvious way for the whole illusion to fail. The net follows the
        /// mean of what it is holding, slowly, so it drags rather than snapping.
        /// </summary>
        private void DragTowardCaptives(float delta)
        {
            if (captives.Count == 0) return;

            Vector3 mean = Vector3.zero;
            foreach (GameObject captive in captives) mean += captive.transform.position;
            mean /= captives.Count;

            transform.position = Vector3.MoveTowards(
                transform.position, mean, struggle.DragInfluence * delta);

            groundHeight = SampleGround(transform.position);
        }

        private float StrugglingMass()
        {
            float total = 0f;

            foreach (GameObject captive in captives)
            {
                if (captive == null) continue;

                if (captive.TryGetComponent(out SnareTether tether)) total += tether.Mass;
                else total += SnareIntegrity.ReferenceLoad;
            }

            return total;
        }

        private void Redraw()
        {
            Camera view = Camera.main;

            // Camera MINUS centre. The ribbon's front face comes out along this vector, so getting
            // it backwards winds every quad in the net the wrong way and — under ordinary culling —
            // the net renders as nothing at all: no error, no warning, a shot that fires and
            // produces an invisible catch. SnareMesh names the parameter `toViewer` for that
            // reason and pins the direction with a test.
            Vector3 toViewer = view != null
                ? view.transform.position - lattice.Centre()
                : Vector3.back;

            // Built about this transform, because the nodes are world space and Unity draws the
            // vertex buffer THROUGH the transform. Redraw runs last in the frame for that reason:
            // both CarryAlongFlight and DragTowardCaptives move the transform, and a mesh built
            // about where it used to be is a net drawn a frame's travel behind itself.
            meshFilter.sharedMesh = meshBuilder.Build(lattice, toViewer, cordWidth, transform.position);
        }

        /// <summary>
        /// One raycast for the whole net rather than one per node.
        ///
        /// Two hundred and twenty-five raycasts a substep is not a budget that exists, and over six
        /// metres of this game's terrain a single height plus the captive capsules is within a
        /// hand's width of the truth everywhere the difference would show.
        /// </summary>
        private static float SampleGround(Vector3 around)
        {
            return Physics.Raycast(around + Vector3.up * 30f, Vector3.down,
                                   out RaycastHit hit, 120f,
                                   ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : around.y;
        }

        /// <summary>
        /// A chunk unloading under a live net must not leave its captives hobbled forever, so this
        /// releases on the way out rather than trusting the rot timer to get there first.
        /// </summary>
        private void OnDisable() => ReleaseAll();

        private void OnDestroy() => meshBuilder?.Dispose();
    }
}
