// How frozen one body looks, on one machine.
//
// It owns nothing about being frozen except the LOOK. Helplessness is FrozenStatus, read off
// StatusReceiver.Suppressed by AgentController and by BodyHold, and the ten seconds are
// FrozenStatus's own. This is the rime, the pose and the plinth.
//
// EVERY MACHINE HAS ITS OWN. The build-up is derived, not replicated: the sprayer's aim ray reaches
// the owner, the server and every peer on the ordinary hold stream, so every machine traces the same
// ray and reaches the same fraction within a frame of the others. That is the same argument
// SupplyReservoir makes for a tank and LaserStaffArtifact makes for a recharge, and it is why the
// telegraph needs no message of its own.
using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The ice on one body: how far the freeze has got, the statue it becomes at the end, and the
    /// pose it holds while it is one.
    ///
    /// <para>
    /// <b>Added at runtime, by whichever machine is watching the spray.</b> It is presentation and
    /// sends nothing, so unlike a <c>StatusReceiver</c> there is no trap in creating one per
    /// machine — a receiver made on the server alone is a body that freezes for the server and
    /// nobody else, because a status is a message on that body's relay; this subscribes to no
    /// relay and speaks on none.
    /// </para>
    /// <para>
    /// <b>Frozen is read every frame and never stored.</b> There is no subscription to
    /// <c>StatusChanged</c> here and no remembered flag, for the reason <c>StatusReactionModule</c>
    /// gives for deriving helplessness: a machine that missed the start of a condition — a late
    /// joiner, whose receiver is told the condition afresh on connect — still reaches the right
    /// answer on its next frame, and nothing has to be undone if it never hears the end.
    /// </para>
    /// <para>
    /// It removes itself once the last of the rime is gone, so a world full of animals that have
    /// been sprayed at costs nothing once they have thawed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrozenBody : MonoBehaviour
    {
        [Tooltip("What the ice is made of. Handed over by whatever froze this body — see " +
                 "FrostLook — because this component is created at runtime and Unity serializes " +
                 "nothing onto one of those.")]
        [SerializeField] private FrostLook look = new FrostLook();

        private readonly FrostShell shell = new FrostShell();

        private StatusReceiver receiver;
        private Animator animator;
        private GameObject plinth;

        /// <summary>The animator's own speed, taken back out of the way while the pose is held.</summary>
        private float restSpeed = 1f;
        private bool posed;

        /// <summary>How far the freeze has got on this body, 0 to 1.</summary>
        private float chill;

        /// <summary>
        /// When the cold currently on this body runs out, if nothing tops it up.
        ///
        /// A moment rather than a flag, because a sprayer resolves its plume fifteen times a second
        /// while this runs sixty: a flag cleared every frame would leave three frames in four
        /// looking like frames nobody was spraying, and the thaw would eat most of the build-up as
        /// fast as it arrived. Each sweep covers the span it actually represents.
        /// </summary>
        private float chilledUntil;

        /// <summary>How far the freeze has got, 0 to 1. One is the moment it becomes a statue.</summary>
        public float Progress => chill;

        /// <summary>
        /// Is this body actually frozen — as opposed to merely covered in rime on the way there?
        /// Asked of the receiver every time rather than cached, so a condition that started or
        /// ended on a machine this one never heard from is still answered correctly.
        /// </summary>
        public bool Frozen => Receiver != null && Receiver.Has(StatusKind.Frozen);

        /// <summary>
        /// The frost on <paramref name="body"/>, making it if it has none.
        ///
        /// <para>
        /// Keyed on the <c>StatusReceiver</c> rather than on whatever collider was hit, so a
        /// creature with a collider per limb wears one coat of ice rather than one per leg — and
        /// so it lands on the object the <c>Frozen</c> flag itself lands on, which is what
        /// <see cref="Frozen"/> then reads.
        /// </para>
        /// </summary>
        public static FrozenBody Ensure(StatusReceiver body, FrostLook look)
        {
            if (body == null) return null;

            FrozenBody frost = body.GetComponent<FrozenBody>();
            if (frost != null) return frost;

            frost = body.gameObject.AddComponent<FrozenBody>();

            // Assigned after the AddComponent, which is why nothing in OnEnable may read it: Unity
            // runs the new component's messages before this line does.
            if (look != null) frost.look = look;

            return frost;
        }

        /// <summary>
        /// Another <paramref name="seconds"/> of cold, out of the <paramref name="freezeSeconds"/>
        /// it takes to freeze solid. Called once per sweep by whatever is spraying, on every
        /// machine that can see the spray.
        ///
        /// <para>
        /// The two numbers are the sprayer's, not this body's: how long a freeze takes is a
        /// property of the gun, and a body frozen by two different sources should be answering to
        /// each of their clocks rather than to one it invented.
        /// </para>
        /// <para>
        /// A body that is already a statue takes none of it. Ten seconds is what
        /// <c>FrozenStatus</c> says a freeze is worth, and a second sprayer — or the same one held
        /// down — pushing that expiry out for as long as somebody keeps the trigger down is the
        /// difference between a control tool and a lock nobody escapes (GDC-L1-MP-0002).
        /// </para>
        /// </summary>
        /// <param name="rate">
        /// How much of the freezing rate this body takes, as a fraction — 1 on the crosshair and
        /// less out towards the rim of the plume. See <c>ConeSweep.Falloff</c>.
        /// </param>
        public void Chill(float seconds, float freezeSeconds, float rate)
        {
            if (Frozen || seconds <= 0f || rate <= 0f) return;

            // The SPAN and the AMOUNT are two different numbers and only one of them is scaled. A
            // body caught at the rim of the plume banks less per sweep, but it is being sprayed for
            // just as long — putting the scaled amount into the expiry too would let the thaw run
            // between sweeps on the very body that is standing in the vapour.
            chilledUntil = Time.time + seconds;
            chill = Mathf.Clamp01(chill + (freezeSeconds > 0f ? seconds * rate / freezeSeconds : 1f));

            Paint();
        }

        private void LateUpdate()
        {
            // After every Update, so a sweep that ran this frame has already been counted and the
            // thaw below is only ever reached by a body nothing is spraying.
            if (Frozen)
            {
                chill = 1f;
                Encase();
                return;
            }

            Release();

            if (Time.time >= chilledUntil && chill > 0f)
            {
                chill = Mathf.Max(0f, chill - look.ThawPerSecond * Time.deltaTime);
                Paint();
            }

            // Nothing left to draw and nothing holding it: the body is a body again.
            if (chill <= 0f)
            {
                shell.Shed();
                Destroy(this);
            }
        }

        private void OnDisable()
        {
            // A body torn down mid-freeze — killed, streamed out, put back in a pool — must not
            // leave a runtime material alive or an animator stopped for good.
            Release();
            shell.Shed();
        }

        /// <summary>Push the current fraction onto the ice, building it the first time.</summary>
        private void Paint()
        {
            if (chill <= 0f) return;

            if (!shell.Built) shell.Build(transform, look.StatueMaterial);

            shell.SetFreeze(chill);
        }

        /// <summary>
        /// Everything a finished statue is. Idempotent, because it is reached from
        /// <see cref="LateUpdate"/> every frame the body stays frozen.
        /// </summary>
        private void Encase()
        {
            Paint();
            HoldPose();
            RaisePlinth();
        }

        /// <summary>
        /// Stop the clip where it stands, so the statue holds the pose it was caught in.
        ///
        /// <para>
        /// The pose is the one thing <c>FrozenStatus</c> explicitly leaves to whatever draws the
        /// statue, and it is needed: <c>StatusReactionModule</c> starves a frozen creature's brain
        /// but disables nothing, so its animator goes on playing an idle — an ice statue breathing
        /// and shifting its weight. The speed is captured and given back rather than assumed to be
        /// one, and it is never written to disk: nothing saves an animator's speed, and the frame
        /// the condition ends this puts it back.
        /// </para>
        /// </summary>
        private void HoldPose()
        {
            if (posed) return;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            restSpeed = animator.speed;
            animator.speed = 0f;
            posed = true;
        }

        /// <summary>
        /// The plinth and shards a statue stands on, at the bottom of the ICE rather than at the
        /// body's transform — a player's pivot is about a metre above their soles. Yawed with the
        /// body so the shards read as having grown out from under it.
        /// </summary>
        private void RaisePlinth()
        {
            if (plinth != null || look.PlinthPrefab == null) return;

            Vector3 at = transform.position;
            if (shell.TryGetBounds(out Bounds bounds))
                at = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            plinth = Instantiate(look.PlinthPrefab, at,
                                 Quaternion.Euler(0f, transform.eulerAngles.y, 0f), transform);
        }

        /// <summary>
        /// Give back everything the statue took. Idempotent and cheap on a body that never froze,
        /// which is how <see cref="LateUpdate"/> reaches it on most frames.
        /// </summary>
        private void Release()
        {
            if (posed)
            {
                if (animator != null) animator.speed = restSpeed;
                posed = false;
            }

            if (plinth != null)
            {
                Destroy(plinth);
                plinth = null;
            }
        }

        /// <summary>
        /// Resolved on demand rather than in <c>Awake</c>, because this component is created with
        /// an <c>AddComponent</c> and Unity raises no Awake for one of those outside play mode.
        /// </summary>
        private StatusReceiver Receiver =>
            receiver != null ? receiver : receiver = GetComponent<StatusReceiver>();
    }
}
