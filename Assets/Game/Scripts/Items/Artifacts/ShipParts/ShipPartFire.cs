using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Vehicles;

namespace SpaceGame.Items
{
    /// <summary>
    /// The fire a burnt-out module starts once the crew are down: on the lander, the transmitter
    /// left sparking in its cradle.
    ///
    /// <para>
    /// <b>Not a new fire system.</b> The flames are the body fire every burning thing in the game
    /// wears (<c>Resources/Effects/BodyFire</c>, sized here by strength the way
    /// <see cref="BurningVisual"/> sizes it by a body's width), and the harm is
    /// <see cref="Ignition.Light"/> — the announcement a ground-fire patch makes, billed by
    /// <c>BurningStatus</c> on the machine that simulates each body. What this owns is the one
    /// thing those do not: a fire with a strength that grows, that a spray knocks back, and that a
    /// save remembers.
    /// </para>
    /// <para>
    /// <b>Server decides, every machine draws.</b> The phase and strength are server-written
    /// NetworkVariables; the ignition clock is server-only state. A joiner reads both on spawn.
    /// The announcement runs on every machine from the replicated strength, the rule
    /// <c>GroundFire</c> follows, so a body is billed once however many machines announce it.
    /// </para>
    /// <para>
    /// On the ship ROOT, beside <see cref="ShipPartRack"/>, so <c>SaveablePolicy</c> can give it
    /// its saver like the rack's.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipPartRack))]
    public sealed class ShipPartFire : NetworkBehaviour
    {
        [Tooltip("The socket whose burnt-out unit burns. It must be authored broken on the rack.")]
        [SerializeField] private ShipPartSocket socket;

        [Tooltip("Where the flames stand and the harm is measured from: the unit's middle.")]
        [SerializeField] private Transform firePoint;

        [SerializeField] private ShipPartFireTuning tuning = new();

        [Header("Harm")]
        [Tooltip("How far from the fire a body catches, at the first flicker and at full strength, " +
                 "in metres. The fire never reaches further than this: it does not spread.")]
        [SerializeField, Min(0.1f)] private float reachSmall = 0.9f;
        [SerializeField, Min(0.1f)] private float reachFull = 2f;

        [Tooltip("Seconds between announcements. A body already burning refuses a refresh, so " +
                 "this only decides how soon someone stepping in catches.")]
        [SerializeField, Min(0.05f)] private float announceSeconds = 0.5f;

        [Tooltip("What the fire can catch. Triggers are always ignored.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Header("Look")]
        [Tooltip("The flames' width at the first flicker and at full strength, in metres.")]
        [SerializeField, Min(0.05f)] private float flameWidthSmall = 0.45f;
        [SerializeField, Min(0.05f)] private float flameWidthFull = 1.5f;

        [Tooltip("The unit's spark emitter. Burning, its rate is multiplied by sparkBoost.")]
        [SerializeField] private ParticleSystem sparks;

        [SerializeField, Min(1f)] private float sparkBoost = 4f;

        [Tooltip("Played while it burns. A stand-in until the bank has a fire loop.")]
        [SerializeField] private SfxId burnLoop = SfxId.WeaponBallLightningChargeLoop;

        private readonly NetworkVariable<byte> networkPhase = new();
        private readonly NetworkVariable<float> networkStrength = new();

        private static readonly List<ShipPartFire> active = new();

        /// <summary>Every fire that could be burning. The extinguisher asks this, not the scene.</summary>
        public static IReadOnlyList<ShipPartFire> Active => active;

        private ShipPartFireState state;
        private ShipPartRack rack;
        private bool spawned;
        private float announceTimer;
        private float sparkRate = -1f;
        private Transform flames;
        private FlameLayers flameLayers;
        private bool flamesMissing;
        private bool flamesLit;
        private readonly LoopingEmitter loop = new();
        private readonly Collider[] caught = new Collider[16];

        public ShipPartFireState State => state;
        public bool IsBurning => state.IsBurning;
        public float Strength => state.strength;
        public Vector3 Centre => firePoint != null ? firePoint.position : transform.position;

        /// <summary>How far the flames reach right now — also what a spray must reach to hit them.</summary>
        public float Reach => Mathf.Lerp(reachSmall, reachFull, state.strength);

        private ShipPartRack Rack => rack != null ? rack : rack = GetComponent<ShipPartRack>();

        private OxygenPlantMount plantMount;
        private bool plantMountResolved;

        /// <summary>
        /// The ignition clock may run: the crew are down, the burnt-out unit is in its socket, and
        /// the ship's oxygen plant is back in its mount and running. A hull with no plant mount
        /// arms at the landing alone. The unit is always seated until after its fire — it cannot be
        /// taken before — except in a save from before this feature that already had a working
        /// transmitter fitted, where there is nothing to burn.
        /// </summary>
        private bool Armed
        {
            get
            {
                if (!ArrivalDirector.CrewHasLanded) return false;
                if (Rack == null || socket == null || !Rack.IsBroken(Rack.IndexOf(socket))) return false;

                if (!plantMountResolved)
                {
                    plantMount = transform.root.GetComponentInChildren<OxygenPlantMount>(true);
                    plantMountResolved = true;
                }

                return plantMount == null || plantMount.Running;
            }
        }

        /// <summary>Is this collider part of the burning unit, its cradle or its socket?</summary>
        public bool Owns(Collider c) => c != null && socket != null && c.transform.IsChildOf(socket.transform);


        private void OnEnable() => active.Add(this);

        private void OnDisable()
        {
            active.Remove(this);
            loop.Stop(true);
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkPhase.OnValueChanged += OnNetworkPhaseChanged;
            networkStrength.OnValueChanged += OnNetworkStrengthChanged;

            if (IsServer) Publish();
            else Adopt(networkPhase.Value, networkStrength.Value);
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkPhase.OnValueChanged -= OnNetworkPhaseChanged;
            networkStrength.OnValueChanged -= OnNetworkStrengthChanged;
        }

        private void OnNetworkPhaseChanged(byte previous, byte current) =>
            Adopt(current, networkStrength.Value);

        private void OnNetworkStrengthChanged(float previous, float current) =>
            Adopt(networkPhase.Value, current);

        private void Adopt(byte phase, float strength)
        {
            if (Network.Simulates(this)) return;

            state.phase = (ShipPartFirePhase)phase;
            state.strength = strength;
        }

        private void Update()
        {
            if (Network.Simulates(this))
            {
                ShipPartFireState next = ShipPartFireRules.Step(state, Time.deltaTime, Armed, tuning);
                Set(next);
            }

            Present();
            Announce(Time.deltaTime);
        }

        /// <summary>
        /// Spray lands on the fire. Authority only — the extinguisher calls it from its
        /// authority-side sweep, and every other machine sees the result through the variables.
        /// </summary>
        public void Douse(float amount)
        {
            if (!Network.Simulates(this)) return;

            Set(ShipPartFireRules.Douse(state, amount));
        }

        /// <summary>The save system's way in: the whole state, wholesale.</summary>
        public void Restore(ShipPartFireState restored)
        {
            if (!Network.Simulates(this)) return;

            Set(restored);
        }

        private void Set(ShipPartFireState next)
        {
            bool changed = next.phase != state.phase || !Mathf.Approximately(next.strength, state.strength);
            state = next;

            if (changed) Publish();
        }

        private void Publish()
        {
            if (!spawned || !IsServer) return;

            networkPhase.Value = (byte)state.phase;
            networkStrength.Value = state.strength;
        }

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>
        /// Set fire to whoever stands in the flames, on every machine — see the class remarks. The
        /// ship itself is not a body here: a fire in its cabin must never set the hull alight.
        /// </summary>
        private void Announce(float deltaTime)
        {
            if (!state.IsBurning)
            {
                announceTimer = 0f;
                return;
            }

            announceTimer += deltaTime;
            if (announceTimer < announceSeconds) return;
            announceTimer = 0f;

            int count = Physics.OverlapSphereNonAlloc(Centre, Reach, caught, bodyMask,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (caught[i] == null || caught[i].transform.IsChildOf(transform.root)) continue;

                Ignition.Light(caught[i].gameObject, firePoint);
            }
        }

        private void Present()
        {
            bool burning = state.IsBurning;

            if (burning) EnsureFlames();
            PresentFlames(burning);

            PresentSparks(burning);

            if (burning) loop.Play(burnLoop, firePoint != null ? firePoint.gameObject : gameObject);
            else loop.Stop(true);
        }

        /// <summary>
        /// Light and snuff the flames on the edge, and size them by strength every frame.
        ///
        /// <para>
        /// <c>BodyFire</c>'s emitters do NOT play on awake: <see cref="BurningVisual"/> starts them
        /// through <see cref="FlameLayers"/>, and an instance that is only made active stays dark.
        /// That is exactly how the first version of this fire burned with no flames on any machine.
        /// Edge-triggered, because <c>Play</c> on a playing system restarts it.
        /// </para>
        /// <para>
        /// Held world-upright. The unit's socket is turned to face out of the cabin wall, and the
        /// flame layers simulate in local space, so a fire that took the socket's rotation would
        /// burn sideways into the room.
        /// </para>
        /// </summary>
        private void PresentFlames(bool burning)
        {
            if (flames == null) return;

            if (burning != flamesLit)
            {
                flamesLit = burning;
                flameLayers.SetEmitting(burning);
            }

            if (!burning) return;

            flames.rotation = Quaternion.identity;

            float width = Mathf.Lerp(flameWidthSmall, flameWidthFull, state.strength);
            Vector3 parentScale = flames.parent != null ? flames.parent.lossyScale : Vector3.one;
            flames.localScale = Vector3.one * (width / BurningVisual.PrefabWidth) /
                                Mathf.Max(0.0001f, parentScale.x);
        }

        private void PresentSparks(bool burning)
        {
            if (sparks == null) return;

            ParticleSystem.EmissionModule emission = sparks.emission;
            if (sparkRate < 0f) sparkRate = emission.rateOverTimeMultiplier;

            // The captured rate times a factor, never a factor alone: rateOverTimeMultiplier IS the
            // rate (see Flamethrower.md, Gotchas).
            emission.rateOverTimeMultiplier = burning ? sparkRate * sparkBoost : sparkRate;
        }

        private void EnsureFlames()
        {
            if (flames != null || flamesMissing) return;

            GameObject prefab = Resources.Load<GameObject>(BurningVisual.PrefabResource);
            if (prefab == null)
            {
                Debug.LogWarning($"[ShipPartFire] No fire prefab at Resources/{BurningVisual.PrefabResource}; " +
                                 $"'{name}' burns with no flames.", this);
                flamesMissing = true;
                return;
            }

            Transform parent = firePoint != null ? firePoint : transform;
            flames = Instantiate(prefab, parent.position, Quaternion.identity, parent).transform;
            flames.name = "Flames";
            flameLayers = new FlameLayers(flames.GetComponentInChildren<ParticleSystem>(true));
            flameLayers.SetRate(1f);
        }
    }
}
