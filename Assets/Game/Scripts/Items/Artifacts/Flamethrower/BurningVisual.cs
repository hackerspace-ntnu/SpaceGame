using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// The flames on a body that is on fire — a torched crate, a burning creature, a player who
    /// walked through a patch.
    ///
    /// <para>
    /// <b>It listens rather than being told.</b> Nothing calls this to light a body up: it
    /// subscribes to its own <see cref="StatusReceiver.StatusChanged"/> and draws whatever the
    /// status says, so a fire lit by the cone, by a patch of <see cref="GroundFire"/>, or by
    /// anything added later looks the same and needs no new call site (GDC-L1-ARCH-0003 — the
    /// sender announces, it does not call). It follows that rain putting the fire out through
    /// <c>StatusReceiver.Clear</c> puts these flames out too, for free.
    /// </para>
    /// <para>
    /// <b>It runs on every machine and decides nothing.</b> The status is already replicated, so
    /// each machine draws its own copy off the same expiry — see <see cref="Ignition"/> for why the
    /// receiver has to exist everywhere for that to be true at all.
    /// </para>
    /// <para>
    /// <b>The flames are fitted to the body, not authored per prefab.</b> This is attached at
    /// runtime to whatever the fire touched, and a crate, an ostrich and a player are wildly
    /// different sizes; the prefab is authored at one metre and scaled to the body's rendered
    /// bounds. Anything else would need every burnable prefab in the game to carry a hand-placed
    /// fire, which is the opposite of "everything burns".
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BurningVisual : MonoBehaviour
    {
        /// <summary>
        /// Where the fire prefab lives. Loaded through <c>Resources</c> because this component is
        /// added to bodies at runtime and so has no Inspector anybody could wire — the same reason
        /// <c>MountModule</c> loads its default camera that way.
        /// </summary>
        private const string PrefabResource = "Effects/BodyFire";

        /// <summary>
        /// How much wider than the body the flames are drawn. Slightly proud of it, so the fire
        /// reads as being ON the body rather than as a column standing inside it.
        /// </summary>
        private const float Overshoot = 1.1f;

        /// <summary>
        /// The prefab's flame shell diameter at a scale of one, in metres — see
        /// the shipped body-fire prefab. The fitted scale is the body's width
        /// divided by this, so a creature ends up wearing a fire its own width instead of a ball
        /// sized off its diagonal, which on anything tall was far wider than the thing burning.
        /// </summary>
        private const float PrefabWidth = 0.8f;

        /// <summary>
        /// A body smaller than this is still drawn at this size, in metres. Fire on a pebble that
        /// is faithfully pebble-sized is fire nobody can see.
        /// </summary>
        private const float MinimumSize = 0.4f;

        /// <summary>
        /// And nothing is drawn bigger than this, in metres. Without it a fire lit on something
        /// enormous — a ship hull, a walker — fills the screen with one flame.
        /// </summary>
        private const float MaximumSize = 2f;

        private static GameObject prefab;
        private static bool prefabResolved;

        private StatusReceiver receiver;
        private GameObject fire;
        private FlameLayers layers;

        private void Awake() => receiver = StatusReceiver.Of(gameObject);

        private void OnEnable()
        {
            if (receiver == null) receiver = StatusReceiver.Of(gameObject);
            if (receiver == null) return;

            receiver.StatusChanged += OnStatusChanged;

            // A body can already be alight when this is attached: Ignition adds the receiver and
            // this component in the same breath, and the status is applied immediately after. The
            // subscription alone would then miss the one event that mattered.
            if (receiver.Has(StatusKind.Burning)) Light();
        }

        private void OnDisable()
        {
            if (receiver != null) receiver.StatusChanged -= OnStatusChanged;

            Douse();
        }

        private void OnStatusChanged(StatusKind kind, bool running)
        {
            if (kind != StatusKind.Burning) return;

            if (running) Light();
            else Snuff();
        }

        /// <summary>Put fire on the body, unless it is already there.</summary>
        private void Light()
        {
            if (fire != null)
            {
                // Already burning and refreshed. Emission is restored rather than the object being
                // rebuilt, so a jet held on one creature does not restart its flames fifteen times
                // a second.
                layers?.SetEmitting(true);
                layers?.SetRate(1f);
                return;
            }

            GameObject source = Prefab();
            if (source == null) return;

            fire = Instantiate(source, transform);

            Fit(fire.transform);

            layers = new FlameLayers(fire.GetComponentInChildren<ParticleSystem>(true));
            layers.SetRate(1f);
            layers.SetEmitting(true);
        }

        /// <summary>
        /// Stop feeding the flames and let what is lit burn out where it is. The object goes when
        /// the last particle does, so a fire does not vanish on the frame the status expires.
        /// </summary>
        private void Snuff()
        {
            if (fire == null) return;

            layers?.SetEmitting(false);

            // The prefab's own longest lifetime plus a margin. A coroutine or a per-frame IsAlive
            // poll would both cost more than a body on fire is worth, and the flames are additive:
            // a stray frame either way is invisible.
            Destroy(fire, 3f);

            fire = null;
            layers = null;
        }

        /// <summary>Everything gone now. A teardown, not a fire going out.</summary>
        private void Douse()
        {
            if (fire != null) Destroy(fire);

            fire = null;
            layers = null;
        }

        /// <summary>
        /// Size the flames to the body and sit them at its middle.
        ///
        /// <para>
        /// Measured off RENDERERS rather than off the transform, because a body's origin is not its
        /// middle — a player's pivot sits about a metre above their soles, and fire centred on the
        /// transform would burn at their waist or under their feet depending on the rig.
        /// </para>
        /// <para>
        /// Sized off the body's WIDTH, not its diagonal. A creature is far taller than it is wide,
        /// and a fire scaled to the diagonal is a fireball a body and a half across that hides
        /// whatever is burning inside it — the flames belong on the body.
        /// </para>
        /// </summary>
        private void Fit(Transform flames)
        {
            if (!TryMeasure(out Bounds bounds))
            {
                flames.localPosition = Vector3.zero;
                flames.localScale = Vector3.one;
                return;
            }

            flames.position = bounds.center;

            float width = Mathf.Max(bounds.size.x, bounds.size.z);

            float size = Mathf.Clamp(width * Overshoot / PrefabWidth,
                                     MinimumSize, MaximumSize);

            // World scale, so a body whose own transform is scaled — and plenty are — does not
            // multiply the fire by that again.
            Vector3 parentScale = transform.lossyScale;
            flames.localScale = new Vector3(
                size / Mathf.Max(0.0001f, parentScale.x),
                size / Mathf.Max(0.0001f, parentScale.y),
                size / Mathf.Max(0.0001f, parentScale.z));
        }

        /// <summary>
        /// The body's rendered extent, or its colliders' if it draws nothing at all. False when it
        /// has neither, which is a trigger volume rather than a thing that can be seen to burn.
        /// </summary>
        private bool TryMeasure(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                // A particle renderer is very often the fire itself, on a body already alight, and
                // measuring against it grows the flames every time they are refreshed.
                if (renderer is ParticleSystemRenderer) continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (any) return true;

            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger) continue;

                if (!any) { bounds = collider.bounds; any = true; }
                else bounds.Encapsulate(collider.bounds);
            }

            return any;
        }

        /// <summary>
        /// The fire prefab, resolved once per session. A miss is reported once and then tolerated:
        /// a missing effect must not stop a body burning, and the warning is what says the builder
        /// has not been run rather than leaving a silent absence to be puzzled over.
        /// </summary>
        private static GameObject Prefab()
        {
            if (prefabResolved) return prefab;

            prefabResolved = true;
            prefab = Resources.Load<GameObject>(PrefabResource);

            if (prefab == null)
            {
                Debug.LogWarning($"[BurningVisual] No fire prefab at Resources/{PrefabResource}. " +
                                 "Bodies will burn without showing flames. Run " +
                                 "Tools > SpaceGame > Items > Build Flamethrower Fire.");
            }

            return prefab;
        }

        // Statics outlive play mode with Enter Play Mode Options on, and a prefab reference cached
        // from a previous session is a destroyed object that resolves to null for ever after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            prefab = null;
            prefabResolved = false;
        }
    }
}
