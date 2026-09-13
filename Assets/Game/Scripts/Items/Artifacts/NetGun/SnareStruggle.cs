using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How long one net lasts, and what a captive fighting it is worth.
    ///
    /// <para>
    /// Lives here rather than on <see cref="SnareTether"/> for the reason
    /// <see cref="LassoStruggle"/> gives: the tether is added at runtime and never authored, and
    /// serialized fields on a component nobody can select in the Inspector are constants wearing a
    /// costume. These are the numbers a designer actually moves, so they are serialized on the gun
    /// prefab and handed to each tether as the net lands.
    /// </para>
    /// <para>
    /// It used to carry four more: a shuffle radius, a thrash frequency, a thrash share and a drag
    /// influence. Every one of them described a captive who is on their FEET — wandering to the end
    /// of a leash, throwing their weight about, dragging the net along behind it. A netted body now
    /// goes limp where it stands and the cord is nailed to its bones, so there is no wander to
    /// bound and nothing left for the net to be dragged by. They went with the behaviour they
    /// described rather than being left as tunables that move nothing.
    /// </para>
    /// <para>
    /// <see cref="HobbleSpeed"/> survived that cull and nearly did not, which is worth stating: it
    /// looks like one of the four, and it is not. It describes the captive the net could NOT put
    /// down — a mount with somebody aboard, a rig with no usable skeleton — and that captive really
    /// is still on its feet. See <see cref="SnareTether"/>, which is the only reader.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class SnareStruggle
    {
        [Tooltip("Seconds an ordinary single captive who does NOT fight is held before the net " +
                 "rots away.\n\n" +
                 "It means exactly that: one captive of SnareIntegrity.ReferenceLoad mass, lying " +
                 "still, is held for this long. Three of them share the same pool and get a third " +
                 "of it each, which is what stops a wide net being strictly better than a careful " +
                 "shot — and a captive who struggles gets out sooner still, by the multiplier " +
                 "below.")]
        [SerializeField, Min(0.01f)] private float holdSeconds = 30f;

        [Tooltip("Fraction of its normal speed a netted thing may move at, when the net could not " +
                 "put it down at all.\n\n" +
                 "The FALLBACK, not the usual case: a net now fells what it catches, and a felled " +
                 "body has no speed to cap. This applies to the bodies that refuse to go down — a " +
                 "mount with somebody aboard, a rig with no usable skeleton — which really are " +
                 "still on their feet. Raise it toward 1 and a net that fails to fell a mount " +
                 "barely inconveniences it; drop it toward 0 and a mount is pinned as firmly as if " +
                 "it had gone down, without the animation to say why.")]
        [SerializeField, Range(0.05f, 1f)] private float hobbleSpeed = 0.28f;

        [Tooltip("Struggle inputs per second past which nothing more is gained.\n\n" +
                 "The cap is the design, not a balance knob. Above it a struggle rewards input " +
                 "rate — which excludes anyone who cannot spam a key and rewards anyone who binds " +
                 "an autofire macro (GDC-L1-UX-0006).")]
        [SerializeField, Min(0.1f)] private float maxUsefulStruggleRate = 2.5f;

        [Tooltip("Seconds a struggle takes to fade once the captive stops fighting.")]
        [SerializeField, Min(0.05f)] private float struggleDecaySeconds = 1.2f;

        [Tooltip("How far a captive has to push a direction before it counts as one at all.\n\n" +
                 "Not a rest-drift filter: the Input System's own stick processor has already " +
                 "removed drift by the time a value reaches this. It is a 'you meant it' " +
                 "threshold — at 0.5 a gamepad pushed half way is steering rather than fighting. " +
                 "Lower it toward 0.2 if half-deflection struggling should count too.")]
        [SerializeField, Range(0.05f, 0.95f)] private float struggleMoveDeadzone = 0.5f;

        [Tooltip("How far round a captive must throw themselves for it to read as a struggle " +
                 "rather than a turn, in degrees.\n\n" +
                 "120 counts mashing A against D (a 180 degree reversal) and refuses strafing " +
                 "round a corner (90). Toward 90 and merely steering while netted drains the net; " +
                 "toward 180 and nothing but a dead-straight reversal counts, which on a keyboard " +
                 "means diagonals stop working.")]
        [SerializeField, Range(90f, 179f)] private float struggleReversalAngle = 120f;

        [Tooltip("Extra load a captive struggling flat out puts on the net, as a multiple of one " +
                 "ordinary captive.\n\n" +
                 "At 2 a fully struggling captive presents three captives' worth, so a 30 s net " +
                 "gives out in about 10 s. This sets how long an escape takes.")]
        [SerializeField, Min(0f)] private float struggleMultiplier = 2f;

        public float HoldSeconds => Mathf.Max(holdSeconds, 0.01f);

        /// <summary>
        /// Only ever read on the fallback path. See <see cref="SnareTether.Bind"/>.
        ///
        /// Not clamped here: the <see cref="RangeAttribute"/> holds it inside 0.05-1 in the
        /// Inspector, and the one reader records the authored speed before scaling it, so even a
        /// value that escaped that range is restored exactly on release rather than compounding.
        /// </summary>
        public float HobbleSpeed => hobbleSpeed;

        /// <summary>
        /// Handed straight to <see cref="SnareStruggleMeter"/>, floor and ceiling included.
        ///
        /// <para>
        /// Not re-clamped here, unlike <see cref="HoldSeconds"/>. The meter bounds this on BOTH
        /// sides, for a reason this class cannot see: at zero its cooldown is infinite and at an
        /// unbounded value the cooldown is zero, which removes the send throttle as well as the
        /// rate limit. A second, weaker copy of that guard here would be the one nobody revisits
        /// when the meter's own bounds change.
        /// </para>
        /// </summary>
        public float MaxUsefulStruggleRate => maxUsefulStruggleRate;

        /// <summary>Also clamped by the meter itself. See <see cref="MaxUsefulStruggleRate"/>.</summary>
        public float StruggleDecaySeconds => struggleDecaySeconds;

        /// <summary>
        /// A magnitude, not a squared one — the reader squares it rather than square-rooting the
        /// stick every frame.
        /// </summary>
        public float StruggleMoveDeadzone => struggleMoveDeadzone;

        /// <summary>
        /// The reversal angle as a dot product, which is the form the one reader can use.
        ///
        /// Authored in degrees and converted here rather than authored as a cosine: a designer
        /// tuning "how far round is a struggle" should be typing 120, not -0.5. The sign works out
        /// the way it reads — a wider angle is a MORE negative dot, so the test is `dot &lt; this`.
        /// </summary>
        public float StruggleReversalDot => Mathf.Cos(struggleReversalAngle * Mathf.Deg2Rad);

        /// <summary>
        /// Floored at zero here, because nothing downstream floors it. A negative multiplier would
        /// have a struggling captive drain the pool SLOWER than a still one — a net that lasts
        /// longer the harder it is fought, which is the mechanic inverted rather than mistuned.
        /// </summary>
        public float StruggleMultiplier => Mathf.Max(struggleMultiplier, 0f);
    }
}
