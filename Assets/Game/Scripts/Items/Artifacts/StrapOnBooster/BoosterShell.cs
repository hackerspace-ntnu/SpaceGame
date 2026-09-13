// What a booster LOOKS like, kept away from what it does.
//
// Two jobs live here and nowhere else. The first is the pair of markers the model carries: the
// mounting face and the muzzle. Everything about where the booster sits and which way it pushes is
// measured between those two points, so a re-export that moves them moves the item with them and
// nothing in the code holds a number that has to be kept in step.
//
// The exhaust is the jetpack's, deliberately: the same JetFlame cone and the same JetSmoke puffs
// the pack burns. Two different stylized fires on two rockets in the same desert read as two
// different games, and this one has no throttle to justify a look of its own — see Ignite.
//
// The second is the readout. This booster has ONE charge, not a tank, so there is no fraction to
// draw and no SupplyGauge on it — a fill bar would be showing a number that does not exist. What it
// has instead is an arming lamp, and the lamp is GEOMETRY: the armed model carries a lit one, the
// spent model carries it out and cracked. Swapping the two models is the readout (GDC-L1-SYS-0006).
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The booster's body: its two model states, its exhaust, and the markers everything else is
    /// measured from. On the clamped booster prefab, beside <see cref="BoosterMount"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoosterShell : MonoBehaviour
    {
        [Header("Markers")]
        [Tooltip("Marker_Mount — the mounting face, and the model's origin plane. This is the point " +
                 "that is laid ON the surface the booster clamps to.")]
        [SerializeField] private Transform mountFace;

        [Tooltip("Marker_Muzzle — where the exhaust leaves, on the bore axis.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Marker_Clamp — the nose throat, on the SAME bore axis as the muzzle. The thrust " +
                 "runs from here to the muzzle. Leave it unset only on a model that has no nose " +
                 "marker; the fallback is the mount face, which is measurably wrong (see below).")]
        [SerializeField] private Transform noseClamp;

        [Header("Models")]
        [Tooltip("The armed booster: intact, with its arming lamp lit. Shown from the moment the " +
                 "clamp lands until the burn is over.")]
        [SerializeField] private GameObject armedModel;

        [Tooltip("The spent booster: the husk, with the arming lamp out and cracked. Shown when the " +
                 "burn ends. This swap IS the readout — there is no gauge on a one-charge item.")]
        [SerializeField] private GameObject spentModel;

        [Header("Jaw")]
        [Tooltip("Mesh_DeviceClamp_JawMoving on the ARMED model — the half of the clamp that swings. " +
                 "Snapped shut the instant the booster lands on something, which is the only thing " +
                 "on the model that says it has taken hold. Optional; leave empty for a fixed jaw.\n\n" +
                 "The spent model's jaw is authored already shut: it never opens again.")]
        [SerializeField] private Transform jaw;

        [Tooltip("The hinge, in the jaw's own space. A model fact, so it is measured by eye on the " +
                 "prefab rather than guessed here — the FBX bakes each node's own transform, so " +
                 "there is no axis this code could assume and be right about.")]
        [SerializeField] private Vector3 jawHingeAxis = Vector3.right;

        [Tooltip("How far the jaw swings to close, degrees. Negative swings the other way.")]
        [SerializeField] private float jawClosedDegrees = 35f;

        [Header("Exhaust")]
        [Tooltip("The plume: the JetFlameCone mesh wearing SpaceGame/Effects/JetFlame, the same " +
                 "flame the jetpack burns. That shader reads its own OBJECT SPACE as the plume's " +
                 "frame — base at y = 0 radius 1, tip at y = 1 radius 0 — so the cone's transform " +
                 "is what carries the real size and the +Y axis MUST point the way the exhaust " +
                 "leaves. A billboard cannot wear this shader.\n\n" +
                 "Author it disabled: it is switched on for the length of the burn and off again " +
                 "at burnout.")]
        [SerializeField] private Renderer plume;

        [Tooltip("Smoke, played for the length of the burn on every machine. Author it in WORLD " +
                 "simulation space so a puff hangs where it was made while the booster flies out " +
                 "from under it; a local-space trail follows the booster instead and reads as a " +
                 "stuck decal. Cosmetic, so these are plain children of this prefab and never " +
                 "network objects of their own.")]
        [SerializeField] private ParticleSystem[] exhaust;

        /// <summary>
        /// True when both markers are present. Read before anything is spawned, so a prefab that
        /// lost a marker in a re-import says so once and loudly rather than clamping boosters that
        /// point in an arbitrary direction.
        /// </summary>
        public bool IsWired => mountFace != null && muzzle != null;

        /// <summary>
        /// The booster's axis in its own local space, pointing the way the exhaust leaves.
        ///
        /// <para>
        /// Measured, not assumed. An `_exportlib` FBX bakes each node's transform onto the node
        /// itself and is already axis-converted, so "the booster points along +Z" is a guess that
        /// survives exactly until the next export. The two markers are the model's own statement of
        /// which way it faces.
        /// </para>
        /// <para>
        /// Readable straight off the prefab ASSET — it is composed from serialized child transforms
        /// and touches nothing that needs an <c>Awake</c>, which is what lets the item seat a
        /// booster before instantiating one.
        /// </para>
        /// </summary>
        public Vector3 LocalExhaustAxis
        {
            get
            {
                if (!IsWired) return Vector3.zero;

                // Along the BORE, not from the mounting face. Marker_Clamp and Marker_Muzzle sit at
                // the same height on the bell's own axis, so the line between them is the direction
                // a rocket actually pushes. The mount face is 88 mm below that axis, so measuring
                // from it tilts the thrust 20.5 degrees off the bore -- a booster that looks right
                // on the crate and shoves it sideways. Measured on the built prefab: mount->muzzle
                // is (0, 0.3507, -0.9365), clamp->muzzle is (0, 0, -1).
                Transform from = noseClamp != null ? noseClamp : mountFace;

                Vector3 axis = transform.InverseTransformPoint(muzzle.position)
                             - transform.InverseTransformPoint(from.position);

                return axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// The mounting face in the booster's own local space. The clamp lays this point on the
        /// surface, so the booster's origin sits back from the hit by however far the model puts
        /// its face from its origin.
        /// </summary>
        public Vector3 LocalMountPoint =>
            IsWired ? transform.InverseTransformPoint(mountFace.position) : Vector3.zero;

        private Quaternion jawRest = Quaternion.identity;

        private MaterialPropertyBlock block;

        private static readonly int ThrottleId = Shader.PropertyToID("_Throttle");

        private void Awake()
        {
            // Captured, never assumed. `_exportlib` bakes no transforms, so an imported node carries
            // whatever rotation the .blend gave it and a bare localRotation assignment would flatten
            // the jaw to identity the first time the clamp shut.
            if (jaw != null) jawRest = jaw.localRotation;
        }

        /// <summary>
        /// Snap the jaw shut. Every machine, at the moment the clamp lands: it is the one thing on
        /// the model that says the booster has taken hold rather than merely arrived.
        /// </summary>
        public void CloseJaw()
        {
            if (jaw == null) return;

            jaw.localRotation = jawRest * Quaternion.AngleAxis(jawClosedDegrees, jawHingeAxis);
        }

        /// <summary>
        /// Show neither model.
        ///
        /// <para>
        /// The state a freshly spawned booster is in on a machine that has not been told what it is
        /// clamped to yet. Without it a peer draws a booster hanging at the prefab pose for the tick
        /// between the spawn and the clamp arriving.
        /// </para>
        /// </summary>
        public void Hide()
        {
            if (armedModel != null) armedModel.SetActive(false);
            if (spentModel != null) spentModel.SetActive(false);
            if (plume != null) plume.enabled = false;
        }

        /// <summary>Armed or spent — one lamp, two pieces of geometry. See the note at the top.</summary>
        public void SetSpent(bool spent)
        {
            if (armedModel != null) armedModel.SetActive(!spent);
            if (spentModel != null) spentModel.SetActive(spent);
        }

        /// <summary>
        /// Light the exhaust. Every machine, because everybody watches this thing burn.
        ///
        /// <para>
        /// The plume is lit at full throttle and stays there: a booster has no throttle to speak
        /// of — it is one committed shove — so the only two values it ever has are burning and
        /// spent, and the flicker in the shader is what keeps a constant number from reading as a
        /// still image. Written through a <see cref="MaterialPropertyBlock"/> so the JetFlame
        /// material stays shared with the jetpack's own four cones.
        /// </para>
        /// </summary>
        public void Ignite()
        {
            if (plume != null)
            {
                plume.enabled = true;

                block ??= new MaterialPropertyBlock();
                plume.GetPropertyBlock(block);
                block.SetFloat(ThrottleId, 1f);
                plume.SetPropertyBlock(block);
            }

            if (exhaust == null) return;

            foreach (ParticleSystem stream in exhaust)
                if (stream != null) stream.Play(withChildren: true);
        }

        /// <summary>
        /// Cut it.
        ///
        /// <para>
        /// Stops the emitters and leaves the particles already in the air to finish, the way the
        /// dragon rocket's trail does. They are not detached: the booster does not vanish at burnout
        /// — the husk stays for a few seconds — so there is nothing for them to be orphaned from.
        /// </para>
        /// </summary>
        public void Cut()
        {
            // The plume is geometry and not a particle, so it has nothing left in the air to
            // finish: it goes out on the frame the burn ends.
            if (plume != null) plume.enabled = false;

            if (exhaust == null) return;

            foreach (ParticleSystem stream in exhaust)
                if (stream != null) stream.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
