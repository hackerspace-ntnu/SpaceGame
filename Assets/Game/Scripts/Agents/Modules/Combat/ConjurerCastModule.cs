// Stand still, hold the staff up for four seconds while the turbine spins and the
// charge climbs into the sky, then bring lightning down out of the clouds onto the
// target.
//
// ---- the bolt falls out of the SKY -------------------------------------------
//
// It used to be fired from between the two palms, down a line, from a ring in the
// creature's chest. Both the ring and the pose that served it are gone: the
// conjurer carries a staff with a wind turbine on it now, and what the animation
// sells is summoning rather than aiming.
//
// The difference is not cosmetic, and it is the whole reason the rest of this file
// looks the way it does. A bolt fired along a line can be BLOCKED -- put something
// solid between the two of you and LightningStrike.Beam stops at it. A bolt that
// falls cannot. There is no cover, no angle and no wall; the only counterplay
// available is to not be standing there when it lands.
//
// So this attack has to be readable or it is not an attack, it is a tax. Two
// things carry that, and neither is optional:
//
//   The WIND-UP is four seconds long and extremely loud -- a staff hoisted
//   overhead, a turbine spinning up, arcs reaching further into the sky the closer
//   it gets. ConjurerStaffCharge draws it.
//
//   The AIM is shown on the ground, at the real blast radius, and it TRACKS the
//   target until a second before impact. StrikeTelegraph draws it. Standing still
//   is punished; a late move beats it.
//
// The line-fired version is still here, behind `skyStrike`. It is off by default
// and it is kept because it is a different fight, not because it is dead code:
// flip it and the same wind-up delivers a dodgeable, blockable line attack from
// the tip of the staff instead.
//
// ---- the one decision that matters -------------------------------------------
//
// Whether the bolt lands where the target IS when it resolves, where it WAS when
// the cast began, or somewhere between. That single choice is most of how the
// creature feels to fight, so it is a field (CastAim) rather than something buried
// in the code:
//
//   TracksThenCommits  the default. The mark follows you for three of the four
//                      seconds, then LOCKS and the last second is yours. Standing
//                      still gets you hit; walking out at the end does not.
//                      This is the only one of the three that makes a falling,
//                      unblockable bolt into a fight rather than a dice roll.
//   TracksTarget       lands on you wherever you are. The wind-up is a WARNING and
//                      the blast radius is the only escape.
//   WhereItCommitted   the aim freezes when the cast starts. The wind-up is a
//                      DODGE -- the bolt lands where you were standing and you
//                      simply leave.
//
// Either way the aim, the WARNING and the ANIMATION agree: whatever
// CurrentAimPoint returns is what the bolt hits, what the telegraph marks, and
// what TryGetFacing turns the body toward.
//
// ---- shape -------------------------------------------------------------------
//
//   idle        target inside CastRange and off cooldown  -> settle
//   settling    holds station, and holds the walk at its cadence, until the body has
//               come to rest, the stride has played out to the end of its cycle, and
//               the walk has blended down into the standing pose -> begin
//   casting     holds position for CastSeconds, body tracking the target, the
//               ground mark following it until CastSeconds - AimLockSeconds
//   commit      strike, then CooldownSeconds from the cast's START before it can
//               begin again -- long enough that the clip finishes its recoil first
//
// Claims movement only while settling and casting. Out of range it returns null and
// passes, so ChaseModule (priority 20, below this one's 22) closes the gap on its
// own. That is the same division AgentRangedCombatModule uses and it is why this
// module does no walking.
//
// ---- why there is a SETTLE phase at all --------------------------------------
//
// Because a NavMeshAgent does not stop when you tell it to. Idle drops the path and
// sets isStopped, and the agent then coasts down at its own `acceleration` -- from
// this creature's 9 m/s that is a couple of seconds and the better part of ten
// metres of travel. Fire the cast trigger on the same frame and the conjurer plants
// its feet in the attack pose and SKATES that distance, which is the one thing that
// reads as a bug rather than as weight.
//
// So the decision to cast and the START of the cast are two different moments. The
// settle phase sits between them: it claims the frame (so Chase cannot re-issue a
// destination and start it moving again), holds an Idle intent so the agent brakes,
// and holds the WALK while it does.
//
// ---- and why stopping the BODY is only half of it ----------------------------
//
// The legs and the body run down on different clocks, and left to themselves they
// disagree twice over.
//
// First the CADENCE. The walk's blend tree maps forward speed onto playback rate, so
// a conjurer coasting down from 9 m/s plays its walk slower and slower over the four
// or five metres it takes to stop -- the legs winding down while the body slides on,
// which reads as the animation having frozen with a foot in the air rather than as
// the creature braking. AgentAnimatorDriver.HoldStride is the answer: for the length
// of the stop the cadence stops following the body down and stays where it was, so
// the walk carries on at walking pace while the body runs out underneath it.
//
// Then the CYCLE. The body reaches rest a good quarter of a second before the
// animator does, wherever in the stride it happens to be. Drop the cadence there and
// Walk starts its 0.35 s crossfade into the standing pose from mid-swing; fire the
// Cast trigger into that crossfade and it does not queue behind it, it INTERRUPTS it
// -- and an interrupted transition in Unity does not carry on from where it was, it
// snapshots the pose it had reached and blends that frozen picture into the
// destination. Whatever the walk had in the air stays in the air for the whole
// four-second wind-up.
//
// So the hold does not let go the moment the body stops either. It takes one more
// step: it releases on the next FOOTFALL -- the driver is given the frames anim.py
// plants a foot on -- and only then does the walk blend down into the standing pose.
// The stop lands on a step instead of on whatever frame the brake happened to end
// on, and it costs at worst half a cycle of striding on the spot.
//
// LegsHaveSettled is what waits that out here -- it wants the animator STANDING in
// the settled state rather than on its way to it, so both feet are down and gathered
// under the body before the attack asks for them.
//
// The wait is bounded (SettleTimeout, and MaxStrideHoldSeconds inside the driver).
// An agent wedged against geometry, shoved by a vehicle or knocked off the NavMesh
// must not stand there declining to attack forever, so the timeout casts anyway -- a
// rare skate beats a creature that never fights back. It has to cover the run-down,
// a whole walk cycle behind it and the crossfade on the end, which is why it is
// several seconds rather than the one the brake alone needs.
//
// ---- what runs where ---------------------------------------------------------
//
// AgentController only ticks modules on the machine that SIMULATES the agent, and
// NetAuthority switches that controller off everywhere else. So everything below --
// deciding to cast, timing it, aiming it, applying the damage -- happens on exactly
// one machine, which is what stops a bolt billing each victim once per player
// watching.
//
// The consequence is that a watching machine never runs a line of Tick, so it would
// show nothing: no wind-up animation, no charge on the staff, no warning on the
// ground, no bolt. Motion is the exception and arrives for free -- the
// NetworkTransform carries the body and AgentAnimatorDriver measures that transform
// on any frame nobody drove it, so the walk cycle is already correct on every peer.
// Discrete effects are not free and have to be told.
//
// Hence the split every method below is arranged around:
//
//   DECIDE     server only, in Tick. Begin() and Commit(): timing, aim, damage,
//              and a broadcast.
//   PRESENT    every machine, in Update. PresentCast() and PresentStrike(): the
//              trigger, the charge, the ground mark, the bolt. Reached locally on
//              the server and from a message everywhere else, so both paths run
//              identical code.
//
// The presentation half keeps its OWN clock (`presentElapsed`) rather than reading
// the authoritative one, because on a peer the authoritative one never advances.
// That clock is also how the telegraph knows when to lock: every machine started
// the wind-up on the same frame and every machine has the same authored duration,
// so each reaches the lock on its own without a message for it. Spending a packet
// per frame to keep a cosmetic ring in perfect step would be the wrong trade -- the
// damage point is authoritative and arrives with ConjurerStruck.
//
// Damage is deliberately NOT in the present half. It is shared world state and
// exactly one machine may decide it.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    /// Where a charged bolt lands. See ConjurerCastModule's header.
    public enum CastAim
    {
        /// Follows the target for the whole wind-up and lands on it. Move out of the
        /// blast radius or take it; you cannot outrun the aim.
        TracksTarget = 0,

        /// Frozen when the cast begins. Lands where the target was standing four
        /// seconds ago, so walking away is the counterplay.
        WhereItCommitted = 1,

        /// Follows the target, then locks AimLockSeconds before impact. Punishes
        /// standing still, rewards a late move. The default, and the only one of the
        /// three that gives a falling bolt real counterplay.
        TracksThenCommits = 2,
    }

    public class ConjurerCastModule : BehaviourModuleBase, IFacingModule
    {
        [Header("Engagement")]
        [Tooltip("Maximum distance at which a cast will START. Beyond this the module passes " +
                 "and ChaseModule closes the gap.")]
        [SerializeField] private float castRange = 25f;

        [Tooltip("Line of sight required to BEGIN a cast. Once begun the cast always " +
                 "finishes -- breaking line of sight mid-wind-up does not cancel it.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Whether the bolt lands where the target is when it resolves, where the " +
                 "target stood when the cast began, or where it stood a second before " +
                 "impact. See the file header - this is the difference between a wind-up " +
                 "you must move out of, one you can walk away from, and one that reads " +
                 "as a real duel.")]
        [SerializeField] private CastAim aim = CastAim.TracksThenCommits;

        [Header("Settle")]
        [Tooltip("How slowly the body must be moving before the staff goes up, in m/s. " +
                 "This is what stops the conjurer skating into its own attack: the cast " +
                 "is decided on one frame and STARTED on a later one, once the " +
                 "NavMeshAgent has finished coasting down. Raise it and the wind-up " +
                 "begins while the creature is still drifting; drop it to zero and it " +
                 "waits for the timeout instead, because a NavMeshAgent's velocity does " +
                 "not reach exactly zero.")]
        [SerializeField] private float settleSpeed = 0.2f;

        [Tooltip("Animator state the body must be STANDING in before the staff goes up - " +
                 "not blending toward, standing in. Leave empty to skip the check and " +
                 "cast as soon as the body has stopped.\n\n" +
                 "This is what keeps a leg from freezing in mid-air. Firing the Cast " +
                 "trigger while Walk is still crossfading into Idle interrupts that " +
                 "crossfade, and an interrupted transition in Unity does not carry on - " +
                 "it SNAPSHOTS the pose it was in and blends the frozen picture into the " +
                 "attack. Whatever the walk cycle happened to have in the air stays in " +
                 "the air. Waiting for the crossfade to finish means the legs have " +
                 "already come down and gathered under the body before the attack asks " +
                 "for them.")]
        [SerializeField] private string settledAnimState = "Idle";

        [Tooltip("Longest the conjurer will wait to come to rest before casting anyway. " +
                 "Has to cover the whole settle, which is the run-down (about a second at " +
                 "the prefab's NavMeshAgent acceleration), then the REST of the walk cycle " +
                 "the body stopped in the middle of - up to about 1.7 s, because the " +
                 "settle holds the stride at its cadence rather than letting it decay " +
                 "as the body slows - and then the walk's crossfade into " +
                 "SettledAnimState.\n\n" +
                 "A creature wedged against a rock, riding a moving platform or shoved by " +
                 "a vehicle may never get under SettleSpeed, and one that answers that by " +
                 "never attacking is a worse bug than the skate this phase exists to " +
                 "remove. Set to zero to cast immediately, i.e. to restore the old " +
                 "behaviour.")]
        [SerializeField] private float settleTimeout = 5f;

        [Tooltip("Once a cast has been DECIDED, how much further the target may drift " +
                 "before the conjurer gives up on it and goes back to chasing. A " +
                 "multiplier on CastRange, and it exists only for the settle phase: " +
                 "without it a target hovering on the range boundary flips this module " +
                 "and Chase every frame and the creature stutters instead of stopping.")]
        [SerializeField] [Range(1f, 2f)] private float settleRangeExitFactor = 1.2f;

        [Header("Timing")]
        [Tooltip("Wind-up before the bolt lands. This is the clip's FIRE FRAME, not its " +
                 "length: _Source~/anim.py authors 135 frames at 30 fps and strikes on " +
                 "frame 120, so this is 4.0 s against a 4.5 s clip. The last half second " +
                 "is the recoil and the return to neutral. Setting this to the clip length " +
                 "lands the bolt after the staff has already come down.")]
        [SerializeField] private float castSeconds = 4f;

        [Tooltip("TracksThenCommits ONLY. How long before impact the aim freezes, which is " +
                 "exactly how long the player has to get out. Raise it and the attack " +
                 "becomes a dodge; drop it to zero and it becomes unavoidable.")]
        [SerializeField] private float aimLockSeconds = 1f;

        [Tooltip("From the START of a cast, not the end. The clip needs 4.5 s of that just " +
                 "to play, so at the defaults it is 4 s to the strike, half a second of " +
                 "recoil, and 2 s standing before it can begin again.")]
        [SerializeField] private float cooldownSeconds = 6.5f;

        [Header("Animation")]
        [Tooltip("Trigger fired on the Animator when a cast begins. Leave empty to disable.")]
        [SerializeField] private string castAnimTrigger = "Cast";
        [SerializeField] private Animator animator;

        [Tooltip("Drives the locomotion parameters, and the thing the settle phase asks to " +
                 "hold the walk cadence through the stop. Found on this object or a child " +
                 "when left empty; leave it empty unless the rig is somewhere unusual.")]
        [SerializeField] private AgentAnimatorDriver animatorDriver;

        [Header("Strike")]
        [SerializeField] private GameObject lightningVFXPrefab;

        [Tooltip("Drop the bolt out of the sky onto the target. ON by default: this is the " +
                 "creature's attack. Turn it off and the same wind-up fires a blockable " +
                 "line from the tip of the staff instead - a different and more forgiving " +
                 "fight, kept for that reason. See the file header.")]
        [SerializeField] private bool skyStrike = true;

        [Tooltip("LINE FIRE ONLY. Thickness of the fired bolt's sweep, in metres. The drawn " +
                 "ribbon has width, so a zero-thickness ray would slip through gaps the " +
                 "picture clearly does not. Ignored when skyStrike is on.")]
        [SerializeField] private float beamRadius = 0.6f;

        [Tooltip("SKY STRIKE ONLY. How far above the ground point the bolt is drawn from, " +
                 "so its graph has sky to fall through. Damage is always billed at the " +
                 "ground point.")]
        [SerializeField] private float drawHeight = 100f;

        [Tooltip("Seconds before the spawned bolt is destroyed. The lightning prefab never " +
                 "cleans itself up, and this creature casts every few seconds forever - " +
                 "without this the scene fills with spent bolts. Must outlast the graph or " +
                 "the bolt is cut off mid-strike.")]
        [SerializeField] private float vfxLifetime = 5f;

        [SerializeField] private int damage = 10;
        [SerializeField] private float damageRadius = 3.5f;
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Charge effect")]
        [Tooltip("Spawned on the staff's emitter when the cast begins and destroyed when " +
                 "the bolt lands. Optional.")]
        [SerializeField] private GameObject chargeVFXPrefab;

        [Tooltip("Bone the charge effect parents to. staff.py puts StaffTip at the emitter " +
                 "above the turbine, so the effect rides the staff through the whole raise " +
                 "for free.")]
        [SerializeField] private string chargeSocketBone = "StaffTip";

        [Header("Warning")]
        [Tooltip("The mark on the ground showing where the bolt will land. Spawned when " +
                 "the cast begins and destroyed when it lands.\n\n" +
                 "Optional in code and not in practice: a falling bolt cannot be blocked " +
                 "or dodged by angle, so this mark is the whole of the player's " +
                 "counterplay. Leave it empty and the attack is unavoidable damage on a " +
                 "timer.")]
        [SerializeField] private GameObject telegraphPrefab;

        [Tooltip("Bone the fired line leaves from. LINE FIRE ONLY - a sky strike is drawn " +
                 "from above the impact point and never touches this. Resolved by name " +
                 "because a serialized Transform cannot point into a prefab's own rig.")]
        [SerializeField] private string muzzleBone = "StaffTip";

        private AgentTargeting targeting;

        /// Only ever asked one question: whether this creature is still alive. See
        /// DrainCastTrigger, which is the one thing here that outlives the AgentController.
        private HealthComponent health;
        private Transform chargeSocket;
        private Transform muzzle;
        private GameObject liveCharge;
        private StrikeTelegraph liveTelegraph;

        // ---- the authoritative half. Only the simulating machine writes these.
        private bool casting;
        private float castElapsed;
        private float cooldownRemaining;
        private bool aimLocked;

        /// The gap between deciding to cast and starting one, while the body coasts to a
        /// stop. Purely local and purely about locomotion -- nothing has been committed,
        /// broadcast or aimed yet, so a settle that is abandoned costs nothing and is not
        /// worth a message to the peers.
        private bool settling;
        private float settleElapsed;

        /// Whether THIS settle has already asked for its stride hold.
        ///
        /// The ask has to be once, and the reason is the whole point of the hold. It
        /// releases itself at the footfall, and a settle is still running when it does --
        /// LegsHaveSettled is waiting out the crossfade the release just started. Ask again
        /// on the next frame and the driver latches a fresh hold, drives the cadence back up
        /// to walking pace, and the walk plays on at full speed through the entire blend
        /// down into the standing pose. The stop it was supposed to land is undone one frame
        /// after it lands.
        private bool strideHoldAsked;

        /// A Cast trigger that has been decided but not yet handed to the Animator, because
        /// the Animator was mid-crossfade when it was decided. Presentation state, so it
        /// lives on every machine: the peers are exactly where the ungated path runs.
        private bool castTriggerPending;

        /// settledAnimState, hashed once. Compared against the layer's shortNameHash rather
        /// than run through IsName every frame -- IsName hashes the string on each call and
        /// this is asked once a frame for the length of every settle.
        private int settledStateHash;

        // ---- the presentation half. Every machine writes these, including the server.
        private bool presenting;
        private float presentElapsed;

        /// Where the target stood when the cast committed.
        ///
        /// Under WhereItCommitted this IS the aim. Under the two tracking modes it is the
        /// fallback, and it has to exist: a target that dies, despawns or is streamed out
        /// during the wind-up leaves a null Transform, and a cast with nowhere to land is
        /// a NullReferenceException on the frame it resolves. Freezing it up front means
        /// the bolt always has somewhere to go. Under TracksThenCommits it is also where
        /// the lock writes the final answer.
        private Vector3 committedPoint;

        /// How long into the wind-up the aim freezes. Never past the strike itself.
        private float LockAt =>
            aim == CastAim.TracksThenCommits
                ? Mathf.Clamp(castSeconds - aimLockSeconds, 0f, castSeconds)
                : castSeconds;

        /// Where the bolt is aimed RIGHT NOW -- what it will hit, what the ground mark
        /// sits on, and what the body turns to face. Those must be the same point or the
        /// warning lies about where the strike is going.
        private Vector3 CurrentAimPoint()
        {
            if (aim == CastAim.WhereItCommitted || aimLocked) return committedPoint;

            Transform target = targeting != null ? targeting.Target : null;
            return target != null ? target.position : committedPoint;
        }

        /// Which of the three phases this module is in. Read-only.
        public string Phase => casting ? "casting" : settling ? "settling" : "idle";

        public override string ModuleDescription =>
            "Comes to a full stop, then holds the staff up for castSeconds and calls a " +
            "lightning strike down on the target - tracking it, striking where it " +
            "committed, or tracking and then locking a second out, per Aim. Holds station " +
            "while settling and casting; passes otherwise so Chase can close.";

        // Facing outranks Chase's so the conjurer keeps its eye on its target through the
        // whole wind-up rather than turning to look where it last walked.
        public int FacingPriority => ModulePriority.RangedAttack;

        private void Reset()
        {
            SetPriorityDefault(ModulePriority.RangedAttack);
        }

        private void Awake()
        {
            targeting = GetComponent<AgentTargeting>();
            health = GetComponentInParent<HealthComponent>();
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            if (!animatorDriver) animatorDriver = GetComponentInChildren<AgentAnimatorDriver>(true);
            settledStateHash = string.IsNullOrEmpty(settledAnimState)
                ? 0
                : Animator.StringToHash(settledAnimState);
            ResolveBones();
        }

        // Registered on every machine, including the one that simulates -- a broadcast sent
        // from inside a handler re-enters Dispatch inline on the host, so the server also
        // receives its own message. Both present methods are idempotent for that reason.
        private void OnEnable()
        {
            this.NetOn(NetMsg.ConjurerCast, OnCastElsewhere);
            this.NetOn(NetMsg.ConjurerStruck, OnStruckElsewhere);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.ConjurerCast, OnCastElsewhere);
            this.NetOff(NetMsg.ConjurerStruck, OnStruckElsewhere);

            casting = false;
            settling = false;
            presenting = false;
            castTriggerPending = false;
            strideHoldAsked = false;
            animatorDriver?.ReleaseStride();
            ClearCharge();
            ClearTelegraph();
        }

        /// One pass over the rig for both bones: the staff emitter the charge hangs on and
        /// the muzzle the line-fire path leaves from. They are the same bone by default,
        /// and looked up separately anyway so that changing one does not silently move the
        /// other.
        ///
        /// Neither is fatal. A missing charge socket means the effect spawns on the root; a
        /// missing muzzle means a fired line starts at the creature's feet. Both are worth
        /// a line, because a renamed bone is otherwise silent and the symptom -- lightning
        /// coming out of the creature's middle -- reads as a bug in the animation rather
        /// than as a lookup that missed.
        private void ResolveBones()
        {
            if (animator == null) return;

            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (chargeSocket == null && t.name == chargeSocketBone) chargeSocket = t;
                if (muzzle == null && t.name == muzzleBone) muzzle = t;
            }

            if (chargeSocket == null && !string.IsNullOrEmpty(chargeSocketBone))
                Debug.LogWarning(
                    $"{name}: ConjurerCastModule found no bone '{chargeSocketBone}' under " +
                    "the Animator; the charge effect will spawn on the agent root instead. " +
                    "_Source~/staff.py is what creates it.", this);

            if (muzzle == null && !skyStrike && !string.IsNullOrEmpty(muzzleBone))
                Debug.LogWarning(
                    $"{name}: ConjurerCastModule found no bone '{muzzleBone}' under the " +
                    "Animator; a fired line will leave from the agent root.", this);
        }

        /// Where a fired line leaves. LINE FIRE ONLY.
        ///
        /// Read off the live bone every time rather than cached as a point, because the
        /// staff is still moving when it is asked for -- and read the same way on every
        /// machine, which is what lets the strike message carry only the impact point.
        /// Each peer is playing the same clip, so each peer's staff is in the same place.
        private Vector3 Muzzle()
        {
            if (muzzle != null) return muzzle.position;
            if (chargeSocket != null) return chargeSocket.position;
            return transform.position;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Belt and braces. NetAuthority already disables AgentController on a watching
            // machine so this should never be reached there, but a module that decides to
            // cast on a peer would fire a second, unsynchronised bolt -- cheap to rule out.
            if (!Network.Simulates(this)) return null;

            if (cooldownRemaining > 0f) cooldownRemaining -= deltaTime;

            if (casting)
                return TickCast(deltaTime);

            if (settling)
                return TickSettle(in context, deltaTime);

            if (!CanBegin(in context)) return null;

            // Decided, not started. The staff does not go up until the feet have stopped --
            // see the header. Idle is returned rather than null so that this frame is
            // CLAIMED: hand it down to Chase and Chase immediately sets a destination
            // again, and the body never gets the chance to brake.
            settling = true;
            settleElapsed = 0f;
            strideHoldAsked = false;
            return TickSettle(in context, deltaTime);
        }

        /// Brake, let the walk finish, then cast.
        ///
        /// TWO gates, and the second one is the whole reason this method is not just a
        /// speed check. The body stopping and the LEGS stopping are different events: the
        /// motor is at rest a good quarter of a second before the animator has finished
        /// crossfading Walk into its standing pose, and a Cast trigger fired inside that
        /// window interrupts the crossfade rather than following it -- Unity snapshots the
        /// half-blended pose and drags the frozen picture into the attack, so a foot caught
        /// in mid-swing simply stays there for the whole four-second wind-up.
        ///
        /// Velocity is read out of the context rather than measured off the transform
        /// because it is the same number AgentAnimatorDriver feeds the walk blend, so the
        /// two halves cannot disagree about what "stopped" means.
        private MoveIntent? TickSettle(in AgentContext context, float deltaTime)
        {
            // The target left while we were stopping. Nothing was committed, so drop it and
            // pass; Chase takes the frame back and closes the gap again. The exit factor is
            // deliberately looser than CastRange -- a target sitting exactly on the boundary
            // would otherwise flip this module and Chase every frame.
            if (!CanContinueSettle(in context))
            {
                settling = false;
                strideHoldAsked = false;
                animatorDriver?.ReleaseStride();
                return null;
            }

            // Once per settle. See strideHoldAsked -- asking every frame re-latches the hold
            // the frame after it lets go, and the walk runs on at full cadence through the
            // blend it was supposed to hand over to.
            if (!strideHoldAsked)
            {
                animatorDriver?.HoldStride();
                strideHoldAsked = true;
            }

            settleElapsed += deltaTime;

            // The timeout answers both gates at once, and it has to: an agent that never
            // gets under settleSpeed never reaches the second gate either, so a ceiling on
            // only the first one would not be a ceiling at all.
            bool waitedLongEnough = settleElapsed >= settleTimeout;

            if (!waitedLongEnough)
            {
                bool atRest = context.Velocity.sqrMagnitude <= settleSpeed * settleSpeed;
                if (!atRest || !LegsHaveSettled())
                    return MoveIntent.Idle();
            }

            settling = false;
            Begin(in context);
            return MoveIntent.Idle();
        }

        /// Whether the animator is STANDING in settledAnimState rather than on its way
        /// there.
        ///
        /// IsInTransition is the half that matters. Being in the state is not enough --
        /// Unity reports the destination state as current from the first frame of a
        /// crossfade, so a check for the state alone passes while Walk is still visibly
        /// mixed in, and firing the trigger then is exactly the interruption this is here
        /// to avoid.
        ///
        /// Layer 0: this creature's controller is a single Base Layer, and the locomotion
        /// states and the attack all live on it. True when there is no animator or no state
        /// configured, so the gate cannot be what stops an otherwise-working creature from
        /// ever attacking.
        private bool LegsHaveSettled()
        {
            if (animator == null || settledStateHash == 0) return true;
            if (animator.runtimeAnimatorController == null) return true;

            if (animator.IsInTransition(0)) return false;

            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == settledStateHash;
        }

        /// Whether a settle that has already started is still worth finishing.
        ///
        /// Deliberately WEAKER than CanBegin, on both of the conditions it drops:
        ///
        /// Line of sight is not re-checked. It is a raycast, it flickers -- a target
        /// stepping behind a thin post, a ray clipping the creature's own geometry for a
        /// frame -- and an abandoned settle does not merely pause, it hands the frame back
        /// to Chase, which sets a destination and puts the body back up to speed. Flicker
        /// that a few times and the creature never comes to rest at all, the settle runs
        /// out its timeout, and the cast starts mid-stride: the original bug, arriving
        /// intermittently instead of every time. This matches what the cast itself already
        /// does -- requireLineOfSight gates BEGINNING, and once begun, cover is an escape
        /// rather than a cancel.
        ///
        /// Range is re-checked but loosened by settleRangeExitFactor, for the same reason
        /// in slower motion: a target sitting exactly on the boundary would otherwise flip
        /// this module and Chase every frame.
        private bool CanContinueSettle(in AgentContext context)
        {
            if (targeting == null || !targeting.HasTarget) return false;

            return targeting.DistanceToTarget <= castRange * settleRangeExitFactor;
        }

        /// The presentation clock, on EVERY machine.
        ///
        /// Separate from Tick because Tick does not run on a peer -- NetAuthority disables
        /// the controller there -- and the ground mark still has to know when to lock. Both
        /// halves advance on the server; only this one advances anywhere else.
        private void Update()
        {
            DrainCastTrigger();

            if (!presenting) return;

            presentElapsed += Time.deltaTime;

            if (liveTelegraph != null && presentElapsed >= LockAt)
                liveTelegraph.Freeze();
        }

        /// Hand a decided cast to the Animator, but never in the middle of a crossfade.
        ///
        /// This is the last line of defence against the stuck leg, and it is here rather
        /// than in the settle phase because the settle phase is not the only way in. Three
        /// callers reach PresentCast, and only one of them has waited for anything:
        ///
        ///   Begin, after a completed settle       the legs are already down
        ///   Begin, after settleTimeout expired    no gate at all -- that is what the
        ///                                         timeout IS, an override
        ///   OnCastElsewhere, on a peer            no gate possible: AgentController is
        ///                                         switched off there, so no settle ever
        ///                                         runs and the replicated body is still
        ///                                         sliding when the message lands
        ///
        /// A trigger arriving mid-crossfade does not queue behind it, it interrupts it, and
        /// an interrupted transition in Unity snapshots the pose it had reached and blends
        /// that frozen picture onward. Whatever the walk had in the air stays there for the
        /// whole wind-up.
        ///
        /// Waiting cannot deadlock: a transition always ends within its own duration -- the
        /// longest here is the walk's 0.35 s stop blend -- so the worst this costs is a
        /// third of a second of lead on a four-second cast, and only on the two paths that
        /// were already the broken ones.
        private void DrainCastTrigger()
        {
            if (!castTriggerPending) return;

            // Dead creatures do not cast, and this is the only place that has to say so.
            //
            // Everything else about a cast stops on its own when the creature dies:
            // HealthReactionModule switches off the AgentController, and Tick -- which is
            // what decides to cast -- goes with it. This method does not. It runs from
            // Update, on every machine, precisely so that a PEER whose AgentController was
            // never on can still present a cast the server told it about.
            //
            // So the reachable case is a trigger already pending at the moment of death: a
            // cast decided on the last frame the creature was alive, or a network message
            // that lands as it dies. Without this the trigger fires a fraction of a second
            // later, the controller's Attack hangs off Any State, and the corpse gets up
            // mid-collapse and casts. Dropping it rather than holding it, because there is
            // no state this creature can come back to in which the cast would still be
            // wanted -- Death has no way out.
            if (health != null && !health.Alive)
            {
                castTriggerPending = false;
                return;
            }

            if (animator == null || string.IsNullOrEmpty(castAnimTrigger) ||
                animator.runtimeAnimatorController == null)
            {
                castTriggerPending = false;
                return;
            }

            // Layer 0: single Base Layer, and both the locomotion states and the attack
            // live on it.
            if (animator.IsInTransition(0)) return;

            animator.SetTrigger(castAnimTrigger);
            castTriggerPending = false;
        }

        private bool CanBegin(in AgentContext context)
        {
            if (cooldownRemaining > 0f) return false;
            if (targeting == null || !targeting.HasTarget) return false;
            if (requireLineOfSight && !targeting.CanSeeTarget) return false;

            return targeting.DistanceToTarget <= castRange;
        }

        private void Begin(in AgentContext context)
        {
            // Ordinarily already released -- the walk finished its cycle and blended out,
            // which is what LegsHaveSettled was waiting for. Not so on the timeout path,
            // which begins whether the legs got there or not, and a cast that starts under a
            // held cadence would spend its wind-up being told the creature is still walking.
            animatorDriver?.ReleaseStride();
            strideHoldAsked = false;

            casting = true;
            castElapsed = 0f;
            cooldownRemaining = cooldownSeconds;

            // Target.position rather than LastKnownPosition: CanBegin has just confirmed
            // this thing is visible right now, and LastKnownPosition can lag it by a frame.
            // LastKnownPosition is the fallback for the case where it is not.
            Transform target = targeting.Target;
            committedPoint = targeting.CanSeeTarget && target != null
                ? target.position
                : targeting.LastKnownPosition;

            // WhereItCommitted is locked from the outset, by definition. The other two
            // start open; TracksThenCommits closes at LockAt.
            aimLocked = aim == CastAim.WhereItCommitted;

            PresentCast(committedPoint, aimLocked ? null : target);

            // Everyone else starts their wind-up now, on the same frame the server did.
            // Sent rather than timed locally because a peer cannot know when the server
            // decided, and a peer that joins mid-cast must not start one of its own.
            //
            // The VICTIM travels in the arg's subject, not the caster.
            //
            // That is a change from what this message used to carry, and it is free:
            // NetMessaging routes by the SENDING component's own relay -- Send() calls
            // NetRelay.Find(self) -- so the subject field was never doing any routing work
            // and pointing it at the conjurer only restated who was sending. Pointed at the
            // target instead, Resolve() answers with the right GameObject on every machine
            // and offline both, which is what lets each peer's ground mark follow the
            // victim itself rather than being told where it is every frame.
            this.NetToAll(NetMsg.ConjurerCast,
                          new NetArg { P = committedPoint }
                              .With(target != null ? target.gameObject : null));
        }

        /// The wind-up, on every machine. Idempotent: the host reaches it once directly and
        /// once more when NetToAll hands its own broadcast back.
        ///
        /// <paramref name="track"/> is the victim to follow, or null for an aim that is
        /// already committed.
        private void PresentCast(Vector3 at, Transform track)
        {
            if (presenting) return;

            presenting = true;
            presentElapsed = 0f;

            // Latched rather than set, and drained in Update once the animator can take
            // it. See DrainCastTrigger -- this method is reached by paths that have NOT
            // waited for the legs, and setting the trigger from one of those is what
            // leaves a foot hanging in the air.
            castTriggerPending = true;
            DrainCastTrigger();

            if (chargeVFXPrefab != null && liveCharge == null)
            {
                Transform on = chargeSocket != null ? chargeSocket : transform;
                liveCharge = Instantiate(chargeVFXPrefab, on.position, on.rotation, on);
            }

            if (telegraphPrefab != null && liveTelegraph == null)
            {
                // Unparented, deliberately. The mark belongs to the GROUND, not to the
                // caster: parenting it to the creature would drag the warning around
                // whenever the body turned to face its target.
                GameObject go = Instantiate(telegraphPrefab, at, Quaternion.identity);
                liveTelegraph = go.GetComponent<StrikeTelegraph>();

                if (liveTelegraph != null)
                    liveTelegraph.Begin(at, track, castSeconds, damageRadius);
                else
                    Debug.LogWarning(
                        $"{name}: the telegraph prefab has no StrikeTelegraph on it; the " +
                        "warning will sit wherever it spawned and never track or lock.",
                        this);
            }
        }

        /// The strike, on every machine.
        ///
        /// The prefab is deliberately NOT a network prefab. It is pure cosmetic, so every
        /// machine draws its own from this one message -- spawning it through the server
        /// would cost a replicated object per cast to show something nobody interacts with.
        ///
        /// <paramref name="strike"/> is where the bolt ENDS in both modes, and the server
        /// resolved it. Only the far end travels: a sky strike's near end is derived from
        /// it, and a fired line's near end is the staff, which every machine can already
        /// see because every machine is playing the same clip.
        private void PresentStrike(Vector3 strike)
        {
            // Read the muzzle BEFORE the charge is cleared. Same frame either way, but the
            // ordering is the kind of thing that quietly starts mattering if the charge
            // effect ever ends up owning the emitter.
            Vector3 from = skyStrike ? strike + Vector3.up * drawHeight : Muzzle();

            ClearCharge();
            ClearTelegraph();
            presenting = false;

            LightningStrike.Present(lightningVFXPrefab, from, strike, vfxLifetime);
        }

        private void ClearCharge()
        {
            if (liveCharge == null) return;
            Destroy(liveCharge);
            liveCharge = null;
        }

        private void ClearTelegraph()
        {
            if (liveTelegraph == null) return;
            Destroy(liveTelegraph.gameObject);
            liveTelegraph = null;
        }

        private void OnCastElsewhere(in NetArg arg, ulong sender)
        {
            GameObject victim = arg.Resolve();
            PresentCast(arg.P, victim != null ? victim.transform : null);
        }

        private void OnStruckElsewhere(in NetArg arg, ulong sender) => PresentStrike(arg.P);

        private MoveIntent? TickCast(float deltaTime)
        {
            castElapsed += deltaTime;

            // The lock. Read the tracking answer one last time and keep it -- from here the
            // target can run and the bolt will not follow, which is the second the player
            // is being given. The telegraph freezes itself off the presentation clock, so
            // the picture and the decision land on the same frame without a message.
            if (!aimLocked && aim == CastAim.TracksThenCommits && castElapsed >= LockAt)
            {
                committedPoint = CurrentAimPoint();
                aimLocked = true;
            }

            if (castElapsed < castSeconds)
                return MoveIntent.Idle();

            Commit();
            return null;
        }

        private void Commit()
        {
            casting = false;

            // Resolved once, here, and used for all three: the picture every machine draws
            // and the damage this one applies have to agree about where the bolt landed.
            // Re-reading the target between them would draw it in one place and hurt people
            // in another, and shipping the point on the wire is what keeps the peers honest.
            Vector3 aimed = CurrentAimPoint();

            // Server only, by virtue of Tick's guard. Damage is shared world state and
            // exactly one machine may decide it -- applying it beside the visual on every
            // peer would kill a player once per player watching.
            //
            // The two modes differ in WHERE the bolt stops, which is why the damage runs
            // before the picture here rather than after. A fired line is swept and stops at
            // the first solid thing, so the point that gets billed is an OUTPUT of the
            // physics query; a falling bolt always reaches the point it was aimed at. Both
            // then draw to, and broadcast, that same resolved point.
            Vector3 strike;

            if (skyStrike)
            {
                strike = aimed;
                LightningStrike.Damage(strike, damage, damageRadius, damageMask,
                                       gameObject, damagesAttacker: false);
            }
            else
            {
                LightningStrike.Beam(Muzzle(), aimed, beamRadius, damage, damageRadius,
                                     damageMask, gameObject, out strike);
            }

            PresentStrike(strike);

            this.NetToAll(NetMsg.ConjurerStruck,
                          new NetArg { P = strike }.With(gameObject));
        }

        /// Face the spot about to be struck.
        ///
        /// The SAME point the bolt will land on, deliberately, whichever aim mode is set,
        /// so the creature is always looking at what it is about to hit. Under the tracking
        /// modes the body follows the target through the wind-up; under WhereItCommitted,
        /// and after a TracksThenCommits lock, it holds the spot it committed to and lets
        /// the target run out of it -- which is itself information, because a conjurer that
        /// has stopped turning has stopped aiming.
        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            if (casting)
            {
                facePosition = CurrentAimPoint();
                return true;
            }

            // Squaring up while it brakes. CurrentAimPoint is no good here -- committedPoint
            // is still whatever the LAST cast aimed at and this one has not committed yet --
            // so the live target is read directly. Claiming facing this early is what turns
            // the settle into "it saw you and stopped" rather than "it stopped, then noticed
            // it was pointing the wrong way": at 45 deg/s the body needs the braking distance
            // to come round, and it has nothing else to do with it.
            if (settling)
            {
                Transform target = targeting != null ? targeting.Target : null;
                if (target != null)
                {
                    facePosition = target.position;
                    return true;
                }
            }

            facePosition = default;
            return false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            castRange = Mathf.Max(0f, castRange);
            settleSpeed = Mathf.Max(0f, settleSpeed);
            // Rehashed here as well as in Awake so that retyping the state name on a live
            // creature takes effect without a domain reload.
            settledStateHash = string.IsNullOrEmpty(settledAnimState)
                ? 0
                : Animator.StringToHash(settledAnimState);
            settleTimeout = Mathf.Max(0f, settleTimeout);
            castSeconds = Mathf.Max(0f, castSeconds);
            aimLockSeconds = Mathf.Clamp(aimLockSeconds, 0f, castSeconds);
            cooldownSeconds = Mathf.Max(castSeconds, cooldownSeconds);
            damage = Mathf.Max(0, damage);
            damageRadius = Mathf.Max(0f, damageRadius);
            beamRadius = Mathf.Max(0.01f, beamRadius);
            drawHeight = Mathf.Max(0f, drawHeight);
            vfxLifetime = Mathf.Max(0f, vfxLifetime);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, castRange);

            if (!casting) return;

            Vector3 strike = CurrentAimPoint();

            // The blast, and it is drawn in the colour of whether it can still move: this
            // is the one thing worth seeing while a cast runs, because after the lock the
            // ring on the ground is a promise rather than a guess.
            Gizmos.color = aimLocked ? Color.white : Color.yellow;
            Gizmos.DrawWireSphere(strike, damageRadius);

            if (skyStrike)
                Gizmos.DrawLine(strike + Vector3.up * drawHeight, strike);
            else
                Gizmos.DrawLine(Muzzle(), strike);
        }
    }
}
