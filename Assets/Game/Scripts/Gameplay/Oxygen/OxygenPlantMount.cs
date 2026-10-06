using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Whether the lander's oxygen plant is in its mount — and the crash that tears it out.
    ///
    /// <para>
    /// At the first landing of a world (<see cref="ArrivalDirector.HullLanded"/>) the back door
    /// bursts open and the plant is thrown out: the mounted plant goes dark and untouchable, a
    /// loose copy (<see cref="Liftable"/>) is spawned on the ground behind the door, and a few scraps
    /// lie strewn along the line it flew, from the door to where it came to rest. Carried back to
    /// within <see cref="mountRadius"/>, the loose copy is despawned and the plant is in its mount again —
    /// the pattern a ship module follows, whose fitted mesh always lives in the ship — but cracked
    /// (<see cref="TorchRepairable"/>): it runs only once its seams are soldered shut.
    /// </para>
    /// <para>
    /// While it is out, cracked, or in but unpowered, the cabin has no air (<see cref="IAirSupply"/>)
    /// and the burnt-out transmitter's fire clock does not run; while it is out the docks refuse
    /// bottles and cells.
    /// </para>
    /// <para>
    /// Lives beside <see cref="OxygenGenerator"/> on the fixture and rides the hull's
    /// <c>NetworkObject</c>: server-written variables, read on spawn by late joiners.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OxygenGenerator))]
    public sealed class OxygenPlantMount : NetworkBehaviour, IAirSupply, ILiftDestination
    {
        [Tooltip("The loose plant thrown out in the crash: a registered network prefab with a Liftable.")]
        [SerializeField] private GameObject loosePlantPrefab;

        [Tooltip("The door the crash bursts open, and the plant is thrown out of.")]
        [SerializeField] private ArticulatedPartInteraction burstDoor;

        [Tooltip("How far behind the burst door the plant comes to rest, in metres (min, max).")]
        [SerializeField] private Vector2 ejectDistance = new(30f, 60f);

        [Tooltip("How far to either side of the door's line it may land, in metres.")]
        [SerializeField, Min(0f)] private float ejectSpread = 8f;

        [Tooltip("The plant's cracks, opened when it is carried back in and soldered shut with the torch. Optional: a " +
                 "mount without one takes its plant back whole.")]
        [SerializeField] private TorchRepairable damage;

        [Tooltip("How close, ignoring height, the carried plant must come to its mount to snap in.")]
        [SerializeField, Min(0.2f)] private float mountRadius = 1.6f;

        [Header("Crash debris")]
        [Tooltip("Small visual-only pieces (models, no colliders or saved state) strewn along the line " +
                 "the plant flew: the trail from the door to where it lies.")]
        [SerializeField] private GameObject[] debrisPieces;

        [SerializeField, Min(0)] private int debrisCount = 5;

        [Tooltip("Uniform scale of each piece (min, max).")]
        [SerializeField] private Vector2 debrisScale = new(0.3f, 0.55f);

        [Tooltip("How far to either side of the line a piece may lie, in metres.")]
        [SerializeField, Min(0f)] private float debrisSpread = 1.5f;

        [Tooltip("How far a piece is sunk into the sand, in metres, so none sits on it like a toy.")]
        [SerializeField, Min(0f)] private float debrisSink = 0.08f;

        private readonly NetworkVariable<bool> networkDetached = new();
        private readonly NetworkVariable<Vector3> networkEjectedFrom = new();
        private readonly NetworkVariable<Vector3> networkEjectedTo = new();

        private static readonly List<OxygenPlantMount> active = new();

        /// <summary>Every lander's plant mount. The objective asks this, not the scene.</summary>
        public static IReadOnlyList<OxygenPlantMount> Active => active;

        private OxygenGenerator generator;
        private readonly List<Renderer> hiddenRenderers = new();
        private readonly List<Collider> hiddenColliders = new();
        private bool detached;
        private Vector3 ejectedFrom;
        private Vector3 ejectedTo;
        private bool spawned;
        private readonly List<GameObject> debris = new();
        private bool shownDetached;

        // How far above and below a scrap's spot the sand is looked for, in metres.
        private const float DebrisProbeLift = 20f;

        private OxygenGenerator Generator => generator != null ? generator : generator = GetComponent<OxygenGenerator>();

        /// <summary>Thrown out of its mount and not yet carried back.</summary>
        public bool Detached => detached;

        /// <summary>In its mount but cracked: its seams want the soldering torch.</summary>
        public bool Damaged => !detached && damage != null && damage.NeedsRepair;

        /// <summary>The plant's cracks, if it has any to have.</summary>
        public TorchRepairable Damage => damage;

        /// <summary>In its mount, whole and powered: the cabin has air.</summary>
        public bool Running => !detached && !Damaged && Generator != null && Generator.Powered;

        public bool SuppliesAir => Running;

        /// <summary>Where the crash threw the plant from (the burst door) and to (where it landed).</summary>
        public Vector3 EjectedFrom => ejectedFrom;
        public Vector3 EjectedTo => ejectedTo;

        /// <summary>The crash has happened in this world: the plant is thrown out at most once.</summary>
        public bool HasBeenEjected => ejectedTo != Vector3.zero;

        /// <summary>The burst door's middle, for the objective to look at.</summary>
        public Vector3 DoorPoint => DoorBounds(out Bounds b) ? b.center : transform.position;

        // ── ILiftDestination ─────────────────────────────────────────────────

        public Vector3 Point => transform.position;
        public float Radius => mountRadius;

        public bool Accepts(Liftable load) => detached && load != null && load.LoadId == LoadId;

        /// <summary>SERVER: the carried plant snaps in, cracked from the crash.</summary>
        public void Receive(Liftable load)
        {
            if (!Network.Simulates(this) || !Accepts(load)) return;

            GameServices.World.Despawn(load.gameObject);
            Set(false, ejectedFrom, ejectedTo);
            if (damage != null) damage.Damage();
        }

        /// <summary>The load id a loose plant carries, read off the prefab so the two cannot disagree.</summary>
        private string LoadId =>
            loosePlantPrefab != null && loosePlantPrefab.TryGetComponent(out Liftable l) ? l.LoadId : null;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            active.Add(this);
            Liftable.Register(this);
            ArrivalDirector.HullLanded += OnHullLanded;
        }

        private void OnDisable()
        {
            active.Remove(this);
            Liftable.Unregister(this);
            ArrivalDirector.HullLanded -= OnHullLanded;
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkDetached.OnValueChanged += OnWireChanged;
            networkEjectedTo.OnValueChanged += OnWireVectorChanged;

            if (IsServer) Publish();
            else Adopt();
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkDetached.OnValueChanged -= OnWireChanged;
            networkEjectedTo.OnValueChanged -= OnWireVectorChanged;
        }

        private void OnWireChanged(bool previous, bool current) => Adopt();
        private void OnWireVectorChanged(Vector3 previous, Vector3 current) => Adopt();

        private void Adopt()
        {
            if (Network.Simulates(this)) return;

            detached = networkDetached.Value;
            ejectedFrom = networkEjectedFrom.Value;
            ejectedTo = networkEjectedTo.Value;
        }

        private void Update() => Present();

        // ── The crash (SERVER) ───────────────────────────────────────────────

        private void OnHullLanded(GameObject ship)
        {
            if (ship == null || transform.root != ship.transform) return;

            Eject();
        }

        /// <summary>
        /// SERVER: burst the door, throw the plant out. Once per plant: a plant already out, or one
        /// that has ever been out, is left alone.
        /// </summary>
        public bool Eject()
        {
            if (!Network.Simulates(this) || detached || HasBeenEjected || loosePlantPrefab == null) return false;

            if (!TryRestingPoint(out Vector3 from, out Vector3 to)) return false;

            Quaternion facing = Quaternion.LookRotation(Flat(to - from), Vector3.up);
            if (GameServices.World.Spawn(loosePlantPrefab, to, facing) == null) return false;

            if (burstDoor != null) burstDoor.SetOpenByAuthority(true);

            Set(true, from, to);
            return true;
        }

        /// <summary>
        /// Where the plant comes to rest: behind the burst door, along the hull's aft axis, at a
        /// distance and to a side drawn from the tunables, on the ground. False while that ground has
        /// not streamed in — never a guessed height.
        /// </summary>
        private bool TryRestingPoint(out Vector3 from, out Vector3 to)
        {
            Transform hull = transform.root;
            from = DoorPoint;
            to = default;

            Vector3 back = -Flat(hull.forward);
            Vector3 side = Vector3.Cross(Vector3.up, back);
            float distance = Random.Range(ejectDistance.x, Mathf.Max(ejectDistance.x, ejectDistance.y));
            Vector3 flat = from + back * distance + side * Random.Range(-ejectSpread, ejectSpread);

            if (!ShipGrounding.TryResolveGround(new Vector2(flat.x, flat.z), flat.y + 40f, out float y))
                return false;

            to = new Vector3(flat.x, y, flat.z);
            if (ShipGrounding.TryResolveGround(new Vector2(from.x, from.z), from.y + 2f, out float doorY))
                from.y = doorY;

            return true;
        }

        /// <summary>SERVER: the save system's way in.</summary>
        public void Restore(bool isDetached, Vector3 from, Vector3 to)
        {
            if (!Network.Simulates(this)) return;

            Set(isDetached, from, to);
        }

        private void Set(bool isDetached, Vector3 from, Vector3 to)
        {
            detached = isDetached;
            ejectedFrom = from;
            ejectedTo = to;
            Publish();
        }

        private void Publish()
        {
            if (!spawned || !IsServer) return;

            networkDetached.Value = detached;
            networkEjectedFrom.Value = ejectedFrom;
            networkEjectedTo.Value = ejectedTo;
        }

        // ── Every machine ────────────────────────────────────────────────────

        private void Present()
        {
            if (detached != shownDetached)
            {
                shownDetached = detached;
                ShowPlant(!detached);
            }

            if (HasBeenEjected && debris.Count == 0) StrewDebris();
        }

        /// <summary>
        /// The mounted plant is there or it is not: every renderer and collider under it, the docks'
        /// aim volumes included, so a missing plant cannot be pressed, and its light. Only what was
        /// on is switched off, and only that is switched back on: anything authored off stays off.
        /// </summary>
        private void ShowPlant(bool shown)
        {
            if (!shown)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }

                foreach (Collider c in GetComponentsInChildren<Collider>())
                    if (c.enabled) { c.enabled = false; hiddenColliders.Add(c); }

                foreach (Light l in GetComponentsInChildren<Light>()) l.enabled = false;
                return;
            }

            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (Collider c in hiddenColliders) if (c != null) c.enabled = true;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
        }

        /// <summary>
        /// A few scraps along the line the plant flew, laid on every machine from the two replicated
        /// end points. Drawn from a generator seeded by those points, so every machine — and every
        /// reload, since the points are saved — strews the same scraps in the same places, with
        /// nothing of their own to send or save. Visual only: a piece must never be something the
        /// carried plant snags on. World space: they stay where they fell if the ship is driven off.
        /// </summary>
        private void StrewDebris()
        {
            if (debrisPieces == null || debrisPieces.Length == 0) return;

            var rng = new System.Random(Seed(ejectedFrom, ejectedTo));
            Vector3 side = Vector3.Cross(Vector3.up, Flat(ejectedTo - ejectedFrom));
            Transform parent = transform.root.parent;

            for (int i = 0; i < debrisCount; i++)
            {
                GameObject piece = debrisPieces[rng.Next(debrisPieces.Length)];
                if (piece == null) continue;

                float along = (i + (float)rng.NextDouble()) / debrisCount;
                Vector3 p = Vector3.Lerp(ejectedFrom, ejectedTo, along) +
                            side * Mathf.Lerp(-debrisSpread, debrisSpread, (float)rng.NextDouble());
                if (!ShipGrounding.TryResolveCollisionGround(new Vector2(p.x, p.z), p.y + DebrisProbeLift,
                                                             DebrisProbeLift * 2f, transform.root.gameObject,
                                                             out float y))
                    continue;

                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                GameObject bit = Instantiate(piece, new Vector3(p.x, y - debrisSink, p.z), yaw, parent);
                bit.name = "CrashDebris";
                bit.transform.localScale *= Mathf.Lerp(debrisScale.x, debrisScale.y, (float)rng.NextDouble());
                debris.Add(bit);
            }
        }

        // Rounded to the centimetre: the points cross the wire and a save file as floats.
        private static int Seed(Vector3 from, Vector3 to)
        {
            unchecked
            {
                int h = 17;
                foreach (float f in new[] { from.x, from.z, to.x, to.z })
                    h = h * 31 + Mathf.RoundToInt(f * 100f);
                return h;
            }
        }

        private bool DoorBounds(out Bounds bounds)
        {
            bounds = default;
            if (burstDoor == null) return false;

            bool any = false;
            foreach (Collider c in burstDoor.GetComponentsInChildren<Collider>(true))
            {
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }

            return any;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        public override void OnDestroy()
        {
            foreach (GameObject bit in debris)
                if (bit != null) Destroy(bit);
            base.OnDestroy();
        }
    }
}
