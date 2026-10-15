using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// A sprite-hamlet keeper held off the ground: scooped up into a player's arms out of the witch's
    /// poison, or — at the end of the fight — leaping up onto her hat.
    ///
    /// <para>
    /// <b>Why carrying exists.</b> The keepers cannot dodge, and the witch's spiral sweeps the whole
    /// clearing. A player can carry all three, and a carried keeper's colliders are off, so a ball that
    /// would have reached it reaches the carrier instead. But full arms cannot fire the Oathbranch
    /// (<see cref="ArmsLoad"/>), and only a frozen witch can be hit — so every spin is a choice
    /// between protecting the keepers and setting up the next herb (GDC-L1-DESIGN-0002).
    /// </para>
    /// <para>
    /// <b>Server decides, everyone draws.</b> The lift is a request (<c>NetMsg.KeeperLift</c>) judged on
    /// the server against the sender's own body; the carrier is replicated. The server holds the body
    /// up through the motor's own carry (<see cref="NavMeshAgentMotor.RequestTow"/>, the rope's
    /// channel), so the keeper is lifted, carried and dropped with the motor's fall and landing, and
    /// reaches every machine inside its replicated transform. Because that transform arrives a round
    /// trip late, <c>KeeperPose</c> draws a carried keeper at each machine's OWN view of the carrier's
    /// arms — on the carrier's screen it never trails behind them.
    /// </para>
    /// <para>
    /// <b>Not saved.</b> A reload puts every keeper back on its feet wherever its transform was saved;
    /// nothing about being held is worth restoring.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NavMeshAgentMotor))]
    public class KeeperCarry : NetworkBehaviour
    {
        [Tooltip("Its name, as the crosshair and the failure screen say it.")]
        [SerializeField] private string keeperName = "keeper";

        [Header("Lifting")]
        [Tooltip("While the quest is inside this window the keeper can be picked up — the witch fight.")]
        [SerializeField] private QuestStepWindow liftableDuring;

        [Tooltip("How close the server must believe the lifter is, metres. A little over the interaction " +
                 "reach, so a lagging client is not refused for being where it was a moment ago.")]
        [SerializeField, Min(0.5f)] private float liftReach = 4f;

        [Tooltip("The trigger the crosshair finds it by. Off whenever it cannot be lifted, so outside " +
                 "the fight the keeper's own dialog answers instead.")]
        [SerializeField] private Collider liftHandle;

        [Header("In the arms")]
        [Tooltip("Where each held keeper's FEET ride, relative to the carrier's feet and heading: the " +
                 "first hugged in front, the next two on either arm. Low and out in front, so in first " +
                 "person a keeper's hat sits in the bottom of the view rather than filling it.")]
        [SerializeField] private Vector3[] armSlots =
        {
            new(0f, 0.3f, 0.75f), new(-0.5f, 0.25f, 0.5f), new(0.5f, 0.25f, 0.5f),
        };

        [Tooltip("Where a set-down keeper is let go of, relative to the carrier. It drops from there.")]
        [SerializeField] private Vector3 setDownOffset = new(0f, 0.5f, 1.1f);

        [Tooltip("Fastest the server moves a keeper towards the arms, m/s. It scurries to the player and " +
                 "hops up rather than teleporting into place. Faster than a sprint, so a running carrier " +
                 "is never left behind on the server.")]
        [SerializeField, Min(0.5f)] private float catchUpSpeed = 14f;

        [Header("Leaping")]
        [Tooltip("Height of a leap's arc per metre of distance — enough that the first step is a jump.")]
        [SerializeField, Min(0f)] private float leapArcPerMetre = 0.3f;

        [Tooltip("Lowest a leap's arc may be, metres.")]
        [SerializeField, Min(0f)] private float minLeapArc = 1.5f;

        private const ulong Nobody = 0;

        private static readonly List<KeeperCarry> All = new();

        private readonly NetworkVariable<ulong> carrierId = new(
            Nobody, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> slot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NavMeshAgentMotor motor;
        private PoisonTally tally;
        private Collider[] bodyColliders = Array.Empty<Collider>();

        private GameObject armsHeldBy;
        private bool awaitingLanding;

        // ---- a leap, SERVER only: the keepers going up the witch's hat.
        private Transform perchOn;
        private Vector3 perchOffset;
        private Vector3 leapFrom;
        private float leapHeight;
        private float leapSeconds;
        private float leapElapsed;

        /// <summary>SERVER: a set-down keeper has landed, where it will now stand.</summary>
        public event Action<Vector3> SetDownAt;

        public string KeeperName => keeperName;

        /// <summary>Every machine: somebody has this keeper in their arms.</summary>
        public bool IsCarried => carrierId.Value != Nobody;

        /// <summary>SERVER: leaping onto, or clinging to, something.</summary>
        public bool IsPerched => perchOn != null;

        /// <summary>Every machine: the body of whoever holds it, or null.</summary>
        public Transform Carrier => Resolve(carrierId.Value);

        /// <summary>Every machine: the quest is at the point where keepers can be picked up.</summary>
        public bool InLiftableStretch => liftableDuring.Contains(liftableDuring.FindRun());

        /// <summary>Can this keeper be scooped up right now? Every machine.</summary>
        public bool CanBeLifted => InLiftableStretch && !IsCarried && (tally == null || !tally.Fallen);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();

        private void Awake()
        {
            motor = GetComponent<NavMeshAgentMotor>();
            tally = GetComponent<PoisonTally>();

            var colliders = new List<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c != liftHandle && !c.isTrigger) colliders.Add(c);
            bodyColliders = colliders.ToArray();
        }

        private void OnEnable()
        {
            All.Add(this);
            this.NetOn(NetMsg.KeeperLift, OnLiftRequested);
        }

        private void OnDisable()
        {
            All.Remove(this);
            this.NetOff(NetMsg.KeeperLift, OnLiftRequested);
            SyncArms(null);
        }

        /// <summary>Where the arms of <paramref name="carrier"/> hold this keeper, on whichever machine asks.</summary>
        public Vector3 ArmPoint(Transform carrier)
        {
            Vector3 local = armSlots.Length > 0 ? armSlots[Mathf.Clamp(slot.Value, 0, armSlots.Length - 1)] : Vector3.up;
            return carrier.position + Quaternion.Euler(0f, carrier.eulerAngles.y, 0f) * local;
        }

        // ─────────── Requests ───────────

        /// <summary>OWNER of <paramref name="player"/>: ask to pick this keeper up.</summary>
        public void RequestLift(GameObject player) =>
            this.NetToServer(NetMsg.KeeperLift, new NetArg { A = 1 }.With(player));

        /// <summary>OWNER of <paramref name="player"/>: ask to set down every keeper they hold.</summary>
        private void RequestSetDown(GameObject player) =>
            this.NetToServer(NetMsg.KeeperLift, new NetArg { A = 0 }.With(player));

        private void OnLiftRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Decides) return;

            GameObject player = arg.Resolve();
            if (player == null || !Network.MayActFor(player, sender)) return;

            if (arg.A == 0)
            {
                ulong id = IdOf(player);
                foreach (KeeperCarry keeper in All)
                    if (keeper.carrierId.Value == id) keeper.SetDown();
                return;
            }

            if (!CanBeLifted || IsPerched) return;
            if ((player.transform.position - transform.position).sqrMagnitude > liftReach * liftReach) return;

            int free = FreeSlot(IdOf(player));
            if (free < 0) return;

            slot.Value = free;
            carrierId.Value = IdOf(player);
        }

        /// <summary>The first arm slot nobody else this player carries is in, or -1 when the arms are full.</summary>
        private int FreeSlot(ulong player)
        {
            for (int candidate = 0; candidate < armSlots.Length; candidate++)
            {
                bool taken = false;
                foreach (KeeperCarry keeper in All)
                    if (keeper != this && keeper.carrierId.Value == player && keeper.slot.Value == candidate)
                        taken = true;
                if (!taken) return candidate;
            }

            return -1;
        }

        // ─────────── Server ───────────

        /// <summary>SERVER: let go of it in front of whoever was holding it.</summary>
        public void SetDown()
        {
            if (!IsCarried) return;

            Transform carrier = Carrier;
            if (carrier != null)
                motor.RequestTow(carrier.position + Quaternion.Euler(0f, carrier.eulerAngles.y, 0f) * setDownOffset);

            carrierId.Value = Nobody;
            awaitingLanding = true;
        }

        /// <summary>SERVER: put every carried keeper down — the fight is resetting.</summary>
        public static void SetDownEverywhere()
        {
            foreach (KeeperCarry keeper in All) keeper.SetDown();
        }

        /// <summary>
        /// SERVER: leap up onto <paramref name="on"/> and cling there at <paramref name="offset"/>
        /// (in its space) until <see cref="Unperch"/>.
        /// </summary>
        public void Perch(Transform on, Vector3 offset, float seconds)
        {
            SetDown();
            awaitingLanding = false;

            perchOn = on;
            perchOffset = offset;
            leapFrom = transform.position;
            leapSeconds = Mathf.Max(0.05f, seconds);
            leapElapsed = 0f;
            leapHeight = Mathf.Max(minLeapArc, Vector3.Distance(leapFrom, PerchPoint()) * leapArcPerMetre);
        }

        /// <summary>SERVER: let go of whatever it clung to; it drops to the ground.</summary>
        public void Unperch() => perchOn = null;

        private Vector3 PerchPoint() => perchOn.TransformPoint(perchOffset);

        private void FixedUpdate()
        {
            if (!IsServer) return;

            if (perchOn != null)
            {
                leapElapsed += Time.fixedDeltaTime;
                float s = Mathf.Clamp01(leapElapsed / leapSeconds);
                Vector3 along = Vector3.Lerp(leapFrom, PerchPoint(), s) + Vector3.up * (4f * leapHeight * s * (1f - s));
                motor.RequestTow(along);
                return;
            }

            if (!IsCarried) return;

            // A carrier who has gone, or gone down, lets go.
            Transform carrier = Carrier;
            if (carrier == null || carrier.GetComponent<HealthComponent>() is { Alive: false })
            {
                SetDown();
                return;
            }

            // One bounded step towards the arms. From across the grass that step is mostly level, so
            // the motor drags the keeper along its mesh — it scurries over; once it is under the arms
            // the step is mostly up, which is what the motor takes as a lift (AgentCarry.IsLift), and
            // it hops off the ground.
            Vector3 toArms = ArmPoint(carrier) - transform.position;
            motor.RequestTow(transform.position + Vector3.ClampMagnitude(toArms, catchUpSpeed * Time.fixedDeltaTime));
        }

        // ─────────── Every machine ───────────

        private void Update()
        {
            bool held = IsCarried;
            foreach (Collider c in bodyColliders)
                if (c != null && c.enabled == held) c.enabled = !held;

            if (liftHandle != null && liftHandle.enabled != CanBeLifted) liftHandle.enabled = CanBeLifted;

            SyncArms(held ? Carrier : null);

            if (IsServer && awaitingLanding && !motor.IsCarried)
            {
                awaitingLanding = false;
                SetDownAt?.Invoke(transform.position);
            }
        }

        /// <summary>Tell the carrier's equipment its arms are full, and give them back when not.</summary>
        private void SyncArms(Transform carrier)
        {
            GameObject body = carrier != null ? carrier.gameObject : null;
            if (body == armsHeldBy) return;

            if (armsHeldBy != null) ArmsLoad.Release(armsHeldBy, this);
            armsHeldBy = body;
            if (body != null) ArmsLoad.Take(body, this, () => RequestSetDown(body));
        }

        private static ulong IdOf(GameObject go)
        {
            NetworkObject identity = go != null ? go.GetComponent<NetworkObject>() : null;
            return identity != null && identity.IsSpawned ? identity.NetworkObjectId : Nobody;
        }

        private static Transform Resolve(ulong id)
        {
            if (id == Nobody) return null;

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || network.SpawnManager == null) return null;

            return network.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject found) ? found.transform : null;
        }
    }
}
