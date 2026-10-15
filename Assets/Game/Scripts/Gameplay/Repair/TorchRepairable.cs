using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A machine with cracked seams that a soldering torch closes: hold the flame on the glowing seam and it cools to a dark weld
    /// bead, then the next one starts to glow. Today: the lander's oxygen plant, cracked when the crash threw it out.
    ///
    /// <para>
    /// <b>The seams close in order</b>, each after <see cref="secondsPerSeam"/> of flame landing within
    /// <see cref="seamRadius"/> of it. Only the seam being worked on is lit bright, so the next thing to do is the brightest
    /// thing on the machine (GDC-L1-SYS-0006); the ones still to come glow dull, the closed ones not at all.
    /// </para>
    /// <para>
    /// <b>Server decides, every machine presents.</b> The torch lands its flame on every machine (the aim ray travels in the hold
    /// stream) but only the authority calls <see cref="Solder"/>. Progress is one server-written <see cref="NetworkVariable{T}"/>
    /// of seconds, read on spawn by a late joiner; every glow is derived from it. A nested component: it rides the
    /// <c>NetworkObject</c> of whatever it is on (the hull, for the plant). Saved by <c>TorchRepairableSaveable</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TorchRepairable : NetworkBehaviour
    {
        [Tooltip("The cracks to solder, in the order they are worked. Each is a point on the machine's surface.")]
        [SerializeField] private Transform[] seams = System.Array.Empty<Transform>();

        [Tooltip("The glowing crack drawn on each seam, one renderer per seam in the same order. Painted, never switched off.")]
        [SerializeField] private Renderer[] seamGlows = System.Array.Empty<Renderer>();

        [Tooltip("Seconds of flame each seam takes to close.")]
        [SerializeField, Min(0.1f)] private float secondsPerSeam = 2f;

        [Tooltip("How close to a seam the flame must land to count, in metres.")]
        [SerializeField, Min(0.05f)] private float seamRadius = 0.45f;

        [Header("Glow")]
        [SerializeField] private Color workingGlow = new(1f, 0.45f, 0.08f);
        [SerializeField] private Color waitingGlow = new(0.45f, 0.12f, 0.02f);
        [SerializeField] private Color closedGlow = new(0.04f, 0.035f, 0.03f);

        [Tooltip("How fast the seam being worked pulses, per second.")]
        [SerializeField, Min(0f)] private float pulseRate = 1.6f;

        [Tooltip("A light at the seam being worked, on while the machine is damaged. Optional.")]
        [SerializeField] private Light seamLight;

        private readonly NetworkVariable<bool> networkDamaged = new();
        private readonly NetworkVariable<float> networkSoldered = new();

        private static readonly List<TorchRepairable> active = new();

        /// <summary>Every repairable machine. The torch asks this, not the scene.</summary>
        public static IReadOnlyList<TorchRepairable> Active => active;

        private bool damaged;
        private float soldered;
        private bool spawned;
        private bool painted;

        /// <summary>Cracked and not yet soldered shut. The last seam closing clears the damage.</summary>
        public bool NeedsRepair => damaged;

        /// <summary>Seconds of flame all the seams take together.</summary>
        public float TotalSeconds => secondsPerSeam * Mathf.Max(1, seams.Length);

        /// <summary>Flame already put in, in seconds — what the save records.</summary>
        public float SolderedSeconds => soldered;

        /// <summary>0 cracked, 1 whole.</summary>
        public float Progress01 => damaged ? Mathf.Clamp01(soldered / TotalSeconds) : 1f;

        /// <summary>The seam being worked now; -1 when there is none.</summary>
        public int WorkingSeam => NeedsRepair ? Mathf.Min(seams.Length - 1, Mathf.FloorToInt(soldered / secondsPerSeam)) : -1;

        public int SeamCount => seams.Length;

        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkDamaged.OnValueChanged += OnWireChanged;
            networkSoldered.OnValueChanged += OnWireChanged;

            if (IsServer) Publish();
            else Adopt();
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkDamaged.OnValueChanged -= OnWireChanged;
            networkSoldered.OnValueChanged -= OnWireChanged;
        }

        private void OnWireChanged<T>(T previous, T current) => Adopt();

        private void Adopt()
        {
            if (Network.Simulates(this)) return;

            damaged = networkDamaged.Value;
            soldered = networkSoldered.Value;
        }

        /// <summary>
        /// Is <paramref name="point"/> — where a flame landed — on the seam being worked? Every machine asks, to draw the sparks
        /// where they belong; only the authority goes on to <see cref="Solder"/>.
        /// </summary>
        public bool IsOnWorkingSeam(Vector3 point)
        {
            int seam = WorkingSeam;
            return seam >= 0 && seams[seam] != null && (seams[seam].position - point).sqrMagnitude <= seamRadius * seamRadius;
        }

        /// <summary>The working seam's position, for a flame aimed near it to snap its sparks onto.</summary>
        public bool TryGetWorkingSeam(out Vector3 position)
        {
            int seam = WorkingSeam;
            position = seam >= 0 && seams[seam] != null ? seams[seam].position : default;
            return seam >= 0 && seams[seam] != null;
        }

        /// <summary>SERVER: <paramref name="seconds"/> more flame on the working seam.</summary>
        public void Solder(float seconds)
        {
            if (!Network.Simulates(this) || !NeedsRepair || seconds <= 0f) return;

            float next = soldered + seconds;
            if (next >= TotalSeconds) Set(false, 0f);
            else Set(true, next);
        }

        /// <summary>SERVER: cracked again from scratch — the crash, or the plant torn out and brought back.</summary>
        public void Damage()
        {
            if (Network.Simulates(this)) Set(true, 0f);
        }

        /// <summary>SERVER: the save system's way in.</summary>
        public void Restore(bool isDamaged, float solderedSeconds)
        {
            if (!Network.Simulates(this)) return;

            bool open = isDamaged && solderedSeconds < TotalSeconds;
            Set(open, open ? Mathf.Max(0f, solderedSeconds) : 0f);
        }

        private void Set(bool isDamaged, float seconds)
        {
            damaged = isDamaged;
            soldered = seconds;
            Publish();
        }

        private void Publish()
        {
            if (!spawned || !IsServer) return;

            networkDamaged.Value = damaged;
            networkSoldered.Value = soldered;
        }

        // ── Every machine ────────────────────────────────────────────────────

        private void Update() => Paint();

        /// <summary>
        /// Every seam painted from the progress: the one being worked pulses bright, the ones to come glow dull, the closed ones
        /// are dark beads. An undamaged machine paints once and then writes nothing.
        /// </summary>
        private void Paint()
        {
            if (!NeedsRepair && painted) return;

            int working = WorkingSeam;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * pulseRate * 2f * Mathf.PI);

            for (int i = 0; i < seamGlows.Length; i++)
            {
                Color colour = working < 0 || i < working ? closedGlow
                    : i == working ? workingGlow * pulse
                    : waitingGlow;
                EmissiveLamp.Paint(seamGlows[i], colour);
            }

            if (seamLight != null)
            {
                seamLight.enabled = working >= 0;
                if (working >= 0 && seams[working] != null) seamLight.transform.position = seams[working].position;
            }

            painted = !NeedsRepair;
        }
    }
}
