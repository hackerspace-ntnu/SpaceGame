// The ornithopter across machines: who flies it, who only watches, and the two messages that join
// them. Split off OrnithopterFlightMotor.cs for readability; it is one class.
//
// THE AUTHORITY MODEL, because getting it wrong is how this bug happened.
//
// The craft is spawned by the server — only the server may spawn — but it is handed straight to
// the PILOT, and it carries a ClientNetworkTransform. From that moment the pilot's machine owns
// the craft's position: what it publishes IS the craft's pose, and what the server thinks is
// overwritten within a tick. That is deliberate and it is what makes flying feel like flying,
// because the stick and the wings are on the same machine with no round trip between them.
//
// The launch used to be applied only where it was decided, on the server. Every consequence
// followed from that one line:
//
//   * NetAuthority had already switched the server's copy to follow the wire and made its body
//     kinematic, so the launch landed on a motor that was never going to run. PhysX said so, once
//     per call — "Setting linear velocity of a kinematic body is not supported".
//   * The pilot's machine, the only one that could have flown it, was never told there was
//     anything to fly. Nobody simulated the craft at all.
//   * And with nothing driving it, the pilot's copy sat at the pose it was instantiated with —
//     the prefab's, at the world origin — and owner authority published THAT as the truth, which
//     dragged the server's copy, the rider parented into the seat, and the chunk streamer to the
//     origin behind it. The pilot's report was "I was flying, but completely frozen, and
//     everything around was gone".
//
// So the launch travels outward to everyone (CraftLaunch) and the landing travels back to the
// server (CraftDown). In between, the machine that owns the craft is the only one running the
// flight model; everyone else derives the wings from the pose arriving over the wire, which is
// what IExternallyPosed means and why this motor must NOT be one of NetAuthority's suppressed
// drivers.
//
// KNOWN LIMIT: CraftLaunch is an event, so somebody who joins while a craft is already airborne
// never hears it and draws that craft with its wings shut until it lands. Everything that matters
// still works for them — the craft flies past on the replicated transform, and landing, damage and
// dismount are all decided elsewhere — so this is a cosmetic gap, and closing it needs the launch
// to become a NetworkVariable on a NetworkBehaviour added to the prefab.
using SpaceGame.Core;
using SpaceGame.Vehicles.Ornithopter;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents
{
    public partial class OrnithopterFlightMotor
    {
        // Speeds travel as centimetres per second: NetArg is one fixed struct with no float field,
        // which is the trade that makes adding a message a one-line change to NetMsg. A centimetre
        // of resolution on an airspeed is far below anything the flight model or the damage curve
        // can tell apart.
        private const float SpeedToWire = 100f;

        private bool externallyPosed;

        // Presentation sampling. The pose arrives over the wire, so how fast the craft is going is
        // something a watching machine has to measure rather than integrate.
        private Vector3 lastPresentedPosition;
        private bool hasPresentedSample;

        /// <summary>
        /// Whether some other machine is writing this craft's transform.
        ///
        /// Set by <see cref="Core.NetAuthority"/> on every machine that does not own the craft.
        /// The motor stays ENABLED there on purpose — it is both the thing that moves the craft
        /// and the thing that knows the wings are spread and beating, and a disabled one leaves a
        /// remote copy sailing past with its wings folded shut.
        /// </summary>
        public bool ExternallyPosed
        {
            get => externallyPosed;
            set
            {
                if (externallyPosed == value) return;

                externallyPosed = value;

                // Both directions resume from the craft's CURRENT position. A measured speed
                // carried across the handover is a speed from before the wire took over, and it
                // would flap the wings to a rhythm the craft is no longer flying.
                hasPresentedSample = false;
            }
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.CraftLaunch, OnLaunchOrder);
            this.NetOn(NetMsg.CraftDown, OnTouchdownReported);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.CraftLaunch, OnLaunchOrder);
            this.NetOff(NetMsg.CraftDown, OnTouchdownReported);
        }

        /// <summary>
        /// Put this craft into the air on every machine. Server-side (and the only machine there
        /// is, offline) — the wing pack calls this instead of <see cref="Launch"/>.
        ///
        /// <para>
        /// To everyone, not just to the owner, and not just here. The owner needs it because it is
        /// the machine that will actually fly the thing. The server needs it because the flight is
        /// part of what the craft IS — its save record carries it, and it is the server that has
        /// to know a craft is airborne when the pilot's report of the landing comes back. Everyone
        /// else needs it because folded wings and spread wings are the difference between a wreck
        /// and an aircraft.
        /// </para>
        /// <para>
        /// Offline this degrades to a local dispatch, which is the same call it always was.
        /// </para>
        /// </summary>
        public void NetworkLaunch(Vector3 headingForward, float initialSpeed)
        {
            Vector3 flat = headingForward;
            flat.y = 0f;
            Quaternion heading = flat.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : transform.rotation;

            this.NetToAll(NetMsg.CraftLaunch, new NetArg
            {
                R = heading,
                A = Mathf.RoundToInt(Mathf.Max(0f, initialSpeed) * SpeedToWire),
            });
        }

        /// <summary>
        /// A launch, on whichever machine this is. Idempotent: a craft already flying is left alone
        /// rather than snapped back to launch attitude, so a message that arrives twice — or after
        /// the local copy has already been launched by a save being restored — cannot re-level a
        /// craft mid-manoeuvre.
        /// </summary>
        private void OnLaunchOrder(in NetArg arg, ulong sender)
        {
            if (flying) return;

            Vector3 forward = arg.HasOrientation ? arg.R * Vector3.forward : transform.forward;
            Launch(forward, arg.A / SpeedToWire);
        }

        /// <summary>
        /// Tell the server how the flight ended. Sent only from the machine that was flying, and
        /// only when that is not already the server.
        ///
        /// The alternative — letting each machine decide for itself — was the state of things
        /// before ownership moved to the pilot, and it does not work in either direction: the
        /// server cannot see a landing it is not simulating, and a watching client that guessed at
        /// one would despawn a craft still in the air on the pilot's screen.
        /// </summary>
        private void PublishTouchdown(in OrnithopterTouchdown touchdown)
        {
            if (!Network.IsNetworked || Network.Server) return;

            this.NetToServer(NetMsg.CraftDown, new NetArg
            {
                P = touchdown.GroundPosition,
                A = Mathf.RoundToInt(touchdown.ClosingSpeed * SpeedToWire),
                B = touchdown.WasImpact ? 1 : 0,
            });
        }

        /// <summary>
        /// Server side: the pilot says they have arrived. Raise the landing here so the wing pack
        /// on THIS machine does the authoritative half — pricing the impact, standing the pilot on
        /// the ground and despawning the craft — exactly as it does for a host's own flight.
        /// </summary>
        private void OnTouchdownReported(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (!flying) return;
            if (!IsFromThePilot(sender)) return;

            // The contact point and the surface normal are not carried: nothing on this side reads
            // them, because where to put the pilot has already been resolved against the world by
            // the machine that was there. Ground position stands in for both rather than shipping
            // two vectors nobody consults.
            Vector3 ground = arg.P;
            var touchdown = new OrnithopterTouchdown(ground, Vector3.up, arg.A / SpeedToWire,
                                                     ground, arg.B == 1);

            EndFlight();
            Landed?.Invoke(touchdown);
        }

        /// <summary>
        /// Was this report sent by the machine actually flying the craft?
        ///
        /// Every client knows every craft's NetworkObjectId — that is how they draw it — so an
        /// unchecked handler would let anyone in the session land somebody else's aircraft, and
        /// choose where its pilot is put down while they were at it. Same rule, and the same
        /// reasoning, as <see cref="MountNetworkSync.IsDismountAllowed"/>.
        ///
        /// The server is waved through unconditionally: offline every send is attributed to the
        /// server id, and a host reporting its own flight arrives that way too.
        /// </summary>
        private bool IsFromThePilot(ulong sender)
        {
            if (!Network.IsNetworked) return true;
            if (sender == NetworkManager.ServerClientId) return true;

            NetworkObject craft = GetComponentInParent<NetworkObject>();
            return craft == null || !craft.IsSpawned || sender == craft.OwnerClientId;
        }

        /// <summary>
        /// What a machine that is only watching does with the flight state: derive it from the pose
        /// arriving over the wire instead of integrating one of its own.
        ///
        /// Deliberately not the flight model. Running the model here without the pilot's stick
        /// produces a second, divergent flight — a craft that banks when the real one is level and
        /// wings that beat out of time with a machine somebody else is flying. Everything the wing
        /// animator reads is available by measurement, so it is measured.
        /// </summary>
        private void Update()
        {
            if (!ExternallyPosed) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            if (!hasPresentedSample)
            {
                lastPresentedPosition = position;
                hasPresentedSample = true;
                return;
            }

            Vector3 velocity = (position - lastPresentedPosition) / dt;
            lastPresentedPosition = position;

            // A craft that is not moving at all is not deploying either — this is also the state of
            // a wreck sitting in the sand waiting to be despawned, and its wings should stay where
            // they are rather than opening on their own.
            if (!flying) return;

            state.Deployment = Mathf.MoveTowards(state.Deployment, 1f, dt / spreadDuration);
            state.WingSpread = state.Deployment;
            state.Airspeed = velocity.magnitude;

            // Attitude straight off the replicated rotation, undoing the sign conventions ApplyPose
            // writes it with, so the animator's twist and tail read the same on every machine.
            Vector3 euler = transform.rotation.eulerAngles;
            state.Pitch = -Mathf.DeltaAngle(0f, euler.x);
            state.Heading = euler.y;
            state.Roll = -Mathf.DeltaAngle(0f, euler.z);
            state.Gamma = state.Airspeed > 0.01f
                ? Mathf.Asin(Mathf.Clamp(velocity.y / state.Airspeed, -1f, 1f)) * Mathf.Rad2Deg
                : 0f;
            state.Stalled = state.AngleOfAttack > flight.StallAngle;

            // A wing holding altitude or climbing is beating; one giving altitude away is gliding.
            // Crude on purpose — it drives an animation, not a physics model — but it means a
            // watched craft flaps when it climbs, which is the whole of what the eye checks.
            state.FlapEffort = Mathf.Clamp01(0.15f + velocity.y * 0.25f);

            float flapHz = flight.FlapHzIdle + (flight.FlapHzMax - flight.FlapHzIdle) * state.FlapEffort;
            state.FlapPhase = Mathf.Repeat(state.FlapPhase + flapHz * dt, 1f);
        }
    }
}
