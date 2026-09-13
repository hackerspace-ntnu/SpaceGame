using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// What one kind of coat actually is: how long it lasts, how much grip it leaves, how it is
    /// drawn, and whether it is anything you can stand on.
    ///
    /// <para>
    /// Behaviour lives here rather than on <see cref="SurfaceCoatField"/> for the reason
    /// <c>StatusBehaviour</c> keeps it off <c>StatusReceiver</c>: three kinds with a switch apiece
    /// is how the field becomes the class that knows about films, ice and rain at once. Here the
    /// field only knows that a kind has a clock, a footprint and a look. Adding a fourth coat is a
    /// new enum value, a new class and one field — and nothing in the field changes.
    /// </para>
    /// <para>
    /// A behaviour is a plain serializable object held by a concrete field on the field component,
    /// not a component and not a <c>SerializeReference</c>. That gives every number below an
    /// Inspector row without a component per kind and without a type name in a prefab, which a
    /// rename would then break.
    /// </para>
    /// <para>
    /// <b>A behaviour is asked; it never pushes.</b> Nothing in here writes a velocity or a
    /// transform. The grip it reports is read by whichever machine owns the mover, on the frame it
    /// is read (GDC-L1-MP-0004, GDC-L1-FEEL-0002) — a server that wrote a player's velocity from
    /// here would be overwritten within a tick with nothing in the console.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class SurfaceCoatBehaviour
    {
        /// <summary>
        /// The least grip any coat may leave. See the <c>grip</c> tooltip: this is the difference
        /// between a hazard and a sentence, and it is enforced here rather than trusted to whoever
        /// drags the slider.
        /// </summary>
        public const float MinimumGrip = 0.01f;

        [Tooltip("How long a patch of this kind lasts from the moment it is sprayed or refreshed, " +
                 "in seconds. Zero or less is permanent — only something breaking it ends it. " +
                 "Whatever sprays the coat may name its own duration, but this is what the coat " +
                 "itself says it is worth: a spray can does not get to decide how long a film " +
                 "lasts.")]
        [SerializeField] private float seconds;

        [Tooltip("Footprint of one dab, in metres, when the sprayer does not name one.")]
        [SerializeField] private float radius;

        [Tooltip("Grip left to anything standing on this coat, as a share of normal ground. 1 is " +
                 "ordinary ground and the floor is almost nothing.\n\n" +
                 "It is clamped above zero and that is a design decision, not a safety net: at " +
                 "exactly 0 a body can do nothing at all about where it is going, which is a " +
                 "situation with no way out of it rather than a hazard, and a coat the player " +
                 "cannot act inside is a sentence rather than counterplay (GDC-L1-BAL-0004). " +
                 "Zero is also what would let a legged machine latch: its travel would be frozen " +
                 "at whatever it was, including nothing, and nothing could un-stick it.")]
        [SerializeField, Range(MinimumGrip, 1f)] private float grip;

        [Tooltip("How far above and below the sprayed point this coat still counts as being " +
                 "underfoot, in metres. It is a tolerance, not a thickness: the sprayed point is a " +
                 "raycast hit on a surface, and the ground point a mover reports is its own " +
                 "probe's hit on the same surface — two measurements of one piece of ground that " +
                 "agree to within a few centimetres, not exactly.")]
        [SerializeField] private float verticalReach = 0.35f;

        [Tooltip("How long the film takes to fade out at the end of its life, in seconds. A patch " +
                 "that vanished on one frame would read as a bug rather than as an expiry; the " +
                 "GRIP is not faded with it, because grip that trails off is a rule the player " +
                 "cannot see (GDC-L1-SYS-0006).")]
        [SerializeField] private float fadeSeconds = 2f;

        [Tooltip("Material the film is drawn with. Left empty, one is built from the shader named " +
                 "by this kind — which is what every runtime-built surface in this project does, " +
                 "so a coat works without a prefab reference to wire.\n\n" +
                 "Each kind wants its OWN material, and not only for looks: a player about to " +
                 "step on a patch has to be able to tell ice they can cross from a film they " +
                 "cannot stand on. Left on the fallback, all three build the same shader with the " +
                 "same defaults and read alike.")]
        [SerializeField] private Material film;

        /// <summary>
        /// Subclasses pass their authored defaults up, because a serialized field's default is a
        /// field initialiser and the fields live here rather than in each of them.
        /// </summary>
        protected SurfaceCoatBehaviour(float defaultSeconds, float defaultRadius, float defaultGrip)
        {
            seconds = defaultSeconds;
            radius = defaultRadius;
            grip = defaultGrip;
        }

        /// <summary>Which coat this is. Must be unique across the behaviours on one field.</summary>
        public abstract SurfaceCoatKind Kind { get; }

        /// <summary>How long a spray lasts when the sprayer does not name a duration. 0 = forever.</summary>
        public float DefaultSeconds => Mathf.Max(0f, seconds);

        /// <summary>Footprint of one dab when the sprayer does not name one, in metres.</summary>
        public float DefaultRadius => Mathf.Max(0.01f, radius);

        /// <summary>Grip left to a body standing on this, as a share of normal.</summary>
        public float Grip => Mathf.Clamp(grip, MinimumGrip, 1f);

        /// <summary>How far off the sprayed plane this coat still counts as underfoot, in metres.</summary>
        public float VerticalReach => Mathf.Max(0.01f, verticalReach);

        /// <summary>Seconds of fade at the end of a patch's life.</summary>
        public float FadeSeconds => Mathf.Max(0f, fadeSeconds);

        /// <summary>
        /// Is this coat worth a line in the save file?
        ///
        /// False for everything that expires inside half a minute — a quicksave taken over a slick
        /// pool that loaded it back would hand the player a hazard they had already walked past.
        /// </summary>
        public virtual bool Saved => false;

        /// <summary>
        /// Is this coat geometry as well as grip? True gives the patch a collider, which is what
        /// makes a frozen pool something you can walk across.
        /// </summary>
        public virtual bool Standable => false;

        /// <summary>
        /// The shader a film of this kind is drawn with when no material is wired.
        ///
        /// Resolved by name rather than by reference for the reason every other runtime-built
        /// surface in this project resolves one that way: the coat is created in code, on a
        /// GameObject nobody authored, so there is no Inspector slot to fill in unless somebody
        /// wants to.
        /// </summary>
        protected virtual string FilmShader => "SpaceGame/Artifacts/SlickSheen";

        /// <summary>
        /// The material a patch of this kind draws with, built once on first use.
        ///
        /// Null when neither the wired material nor the shader can be found, which the patch reads
        /// as "draw nothing" — a coat that cannot be seen is a defect, but a coat that cannot be
        /// seen and also throws every frame is a worse one.
        /// </summary>
        public Material Film
        {
            get
            {
                if (film != null) return film;

                Shader shader = Shader.Find(FilmShader);
                if (shader == null) return null;

                return film = new Material(shader) { name = $"Coat {Kind}" };
            }
        }

        /// <summary>
        /// May a coat of this kind be laid at <paramref name="point"/>?
        ///
        /// <para>
        /// Answered on the machine that decides, before anything is announced, so a refusal costs
        /// nothing on the wire. <paramref name="colliderLayer"/> is the physics layer a collider
        /// built for this patch should sit on — the layer of whatever it was laid over, so a slab
        /// of ice is found by exactly the ground probes that already found the surface it froze.
        /// </para>
        /// </summary>
        public virtual bool CanCoat(Vector3 point, SurfaceCoatField field, out int colliderLayer)
        {
            colliderLayer = 0;
            return true;
        }

        /// <summary>
        /// Give <paramref name="patch"/> whatever this kind needs beyond a film — which for two of
        /// the three kinds is nothing at all.
        /// </summary>
        public virtual void Build(SurfaceCoatPatch patch, int colliderLayer) { }
    }
}
