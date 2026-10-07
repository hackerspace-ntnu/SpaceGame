using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Makes a character with stylized eyes blink, with lids the colour of its own skin.
    ///
    /// <para>
    /// The lids are drawn by the eye shader, <c>SpaceGame/Characters/StylizedEye</c>, over the
    /// eyeball itself; all this does is say where their edges are, through a property block per
    /// eye. A block rather than the material because one eye material is shared by every character
    /// wearing that style, and each character has its own skin. It costs those renderers the SRP
    /// batcher, which for a few pairs of eyes does not matter.
    /// </para>
    ///
    /// <para>
    /// Purely cosmetic, so it runs on every machine on its own clock: nothing is sent, nothing is
    /// saved, and two players watching the same drifter see it blink at different moments, which
    /// nobody can tell. A dead character's eyes are shut and stay shut until it is revived. Death
    /// reaches clients and loaded saves as a restore rather than a kill, and the eyes shut for that
    /// too -- a corpse is shut-eyed however it came to be one.
    /// </para>
    ///
    /// <para>
    /// Enabling and disabling the component is the switch. Disabled, the lids go back to rest
    /// rather than freezing wherever the last blink left them.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EyeBlink : MonoBehaviour
    {
        [Tooltip("The eye spheres. Each must wear a SpaceGame/Characters/StylizedEye material, or it " +
                 "will never blink.")]
        [SerializeField] private Renderer[] eyes = System.Array.Empty<Renderer>();

        [Header("Lids")]
        [Tooltip("The skin the lids are made of. The drifter builder samples it from the face around " +
                 "the eyes; 'Find Eyes And Match Skin' on this component's menu does the same.")]
        [SerializeField] private Color lidColour = new Color(0.83f, 0.73f, 0.55f);

        [Tooltip("The lids' sheen. Match the skin material's smoothness, or the lids read as a " +
                 "different surface from the face around them.")]
        [SerializeField, Range(0f, 1f)] private float lidSmoothness = 0.15f;

        [Tooltip("Where the upper lid rests while the eye is open, in degrees above the pupil, turning " +
                 "about the eye's side-to-side axis. 180 tucks it right behind the eyeball, so an open " +
                 "eye looks exactly as it did before it had lids; around 60 is heavy-lidded.")]
        [SerializeField, Range(0f, 180f)] private float upperLidRest = 180f;

        [Tooltip("Where the lower lid rests while the eye is open, in degrees below the pupil. -180 " +
                 "tucks it away.")]
        [SerializeField, Range(-180f, 0f)] private float lowerLidRest = -180f;

        [Tooltip("Where the lids meet when the eye is shut, in degrees from the pupil. Below 0 so the " +
                 "upper lid does most of the travel, as a real one does.")]
        [SerializeField, Range(-45f, 45f)] private float lidsMeet = -15f;

        [Tooltip("Width of the dark line along each lid's edge, in degrees of eyeball. It is what " +
                 "makes a shut eye read as shut rather than as a ball of skin. 0 for none.")]
        [SerializeField, Range(0f, 15f)] private float lashWidth = 4f;

        [Tooltip("How much darker than the lid its edge line is.")]
        [SerializeField, Range(0f, 1f)] private float lashDarkening = 0.55f;

        [SerializeField] private BlinkRhythm rhythm = new BlinkRhythm();

        private static readonly int LidColourId = Shader.PropertyToID("_LidColor");
        private static readonly int LidSmoothnessId = Shader.PropertyToID("_LidSmoothness");
        private static readonly int LidEdgesId = Shader.PropertyToID("_LidEdges");

        private MaterialPropertyBlock block;
        private System.Random random;
        private HealthComponent health;

        /// <summary>How shut the eyes were last drawn, or -1 before the first draw.</summary>
        private float shownClosure = -1f;

        private bool Dead => health != null && !health.Alive;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            random = new System.Random(GetInstanceID());
            health = GetComponent<HealthComponent>();

            // Loud, because the failure is silent: the shader ignores a property it does not declare,
            // so an eye on the wrong material simply never closes.
            foreach (var eye in eyes)
            {
                if (eye == null || eye.sharedMaterial == null || !eye.sharedMaterial.HasProperty(LidEdgesId))
                    Debug.LogError($"{name}: EyeBlink eye '{(eye != null ? eye.name : "(missing)")}' " +
                                   "does not wear a SpaceGame/Characters/StylizedEye material, so it " +
                                   "will never blink.", this);
            }
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.OnDeath += Shut;
                health.OnRevive += Reopen;
            }

            rhythm.Restart();
            Show(Dead ? 1f : 0f);
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDeath -= Shut;
                health.OnRevive -= Reopen;
            }

            Show(Dead ? 1f : 0f);
        }

        private void Update()
        {
            if (Dead) return;
            Show(rhythm.Advance(Time.deltaTime, random));
        }

        /// <summary>Lets the lid fields be tuned live in play mode. In edit mode nothing is drawn.</summary>
        private void OnValidate()
        {
            if (block != null && shownClosure >= 0f)
                Draw(shownClosure);
        }

        private void Shut() => Show(1f);

        private void Reopen()
        {
            rhythm.Restart();
            Show(0f);
        }

        /// <summary>Redraws only when the lids have moved, which is only during a blink.</summary>
        private void Show(float closure)
        {
            if (closure != shownClosure)
                Draw(closure);
        }

        private void Draw(float closure)
        {
            shownClosure = closure;

            var edges = new Vector4(
                Mathf.Lerp(upperLidRest, lidsMeet, closure) * Mathf.Deg2Rad,
                Mathf.Lerp(lowerLidRest, lidsMeet, closure) * Mathf.Deg2Rad,
                lashWidth * Mathf.Deg2Rad,
                lashDarkening);

            foreach (var eye in eyes)
            {
                if (eye == null) continue;

                eye.GetPropertyBlock(block);
                // Gamma, as the field holds it: MaterialPropertyBlock.SetColor linearises by itself in
                // a linear project (0.5 is stored as 0.214). Converting first does it twice, and every
                // lid comes out a darker, oversaturated cousin of the face.
                block.SetColor(LidColourId, lidColour);
                block.SetFloat(LidSmoothnessId, lidSmoothness);
                block.SetVector(LidEdgesId, edges);
                eye.SetPropertyBlock(block);
            }
        }
    }
}
