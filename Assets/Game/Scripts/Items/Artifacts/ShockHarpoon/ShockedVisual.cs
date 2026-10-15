using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A body being electrocuted: it flashes to a glowing X-ray with its skeleton showing, current
    /// jumps along its bones and across it, and a blue light wavers over it — for exactly as long
    /// as <see cref="StatusKind.Shocked"/> runs.
    ///
    /// <para>
    /// <b>It listens rather than being told</b>, like <c>BurningVisual</c>: it follows its own
    /// <see cref="StatusReceiver.StatusChanged"/>, so a shock from the harpoon or from anything
    /// added later looks the same and needs no new call site (GDC-L1-ARCH-0003). It runs on every
    /// machine and decides nothing — the status is already replicated.
    /// </para>
    /// <para>
    /// <b>The strobe is held under three flashes a second</b> (<see cref="ShockLook.xraySeconds"/>,
    /// <see cref="ShockLook.normalSeconds"/>), the usual photosensitivity limit; the game has no
    /// reduce-flashing setting, so the effect has to be safe as shipped (GDC-L1-FEEL-0006). The
    /// light wavers with noise rather than strobing for the same reason; the arcs are thin and
    /// small on screen and carry the crackle instead (GDC-L1-FEEL-0004).
    /// </para>
    /// <para>
    /// Added at runtime by whatever shocks the body (<see cref="EnsureOn"/>), beside its receiver,
    /// before the shock lands: <see cref="BodyFrame"/> measures the body while it still stands.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShockedVisual : MonoBehaviour
    {
        private StatusReceiver receiver;
        private BodyFrame frame;
        private ShockLook look;

        private XRayGhost ghost;
        private BodySkeleton skeleton;
        private Light glow;

        private bool xray;
        private float switchAt;
        private float nextArc;
        private float noiseSeed;

        /// <summary>Give <paramref name="receiver"/>'s body the look, unless it has it already.</summary>
        public static void EnsureOn(StatusReceiver receiver)
        {
            if (receiver != null && receiver.GetComponent<ShockedVisual>() == null)
                receiver.gameObject.AddComponent<ShockedVisual>();
        }

        private void OnEnable()
        {
            receiver = StatusReceiver.Of(gameObject);
            if (receiver == null) return;

            frame ??= BodyFrame.Measure(transform);
            receiver.StatusChanged += OnStatusChanged;

            // Attached to a body already shocked (a late join, a reactivated chunk): draw it now
            // rather than waiting for a change that has already happened.
            if (receiver.Has(StatusKind.Shocked)) Begin();
        }

        private void OnDisable()
        {
            if (receiver != null) receiver.StatusChanged -= OnStatusChanged;
            End();
        }

        private void OnStatusChanged(StatusKind kind, bool running)
        {
            if (kind != StatusKind.Shocked) return;
            if (running) Begin();
            else End();
        }

        private void Begin()
        {
            if (skeleton != null) return;

            look = ShockLook.Load();
            if (look == null) return;

            ghost = new XRayGhost(transform, look.xrayShell);
            skeleton = new BodySkeleton(transform, frame, look);

            var light = new GameObject(name + " (shock light)");
            glow = light.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = look.lightColor;
            glow.range = frame.Height * look.lightRange;
            glow.shadows = LightShadows.None;

            noiseSeed = Random.value * 100f;
            nextArc = Time.time;
            // Straight into the X-ray: the first frame of the shock is the one that has to read.
            Show(true);
        }

        private void LateUpdate()
        {
            if (skeleton == null) return;

            if (Time.time >= switchAt) Show(!xray);

            skeleton.Tick();
            ghost.Tick();
            TickArcs();
            TickLight();
        }

        private void Show(bool on)
        {
            xray = on;
            ghost.SetShowing(on);
            skeleton.SetVisible(on);

            Vector2 hold = on ? look.xraySeconds : look.normalSeconds;
            switchAt = Time.time + Random.Range(hold.x, hold.y);
        }

        private void TickArcs()
        {
            if (look.arc == null || Time.time < nextArc) return;
            nextArc = Time.time + look.arcInterval;

            if (skeleton.TryPickArc(Random.value < look.longArcChance, out Vector3 from, out Vector3 to))
                Instantiate(look.arc).Strike(from, to);
        }

        private void TickLight()
        {
            Transform centre = frame.Chest != null ? frame.Chest : transform;
            glow.transform.position = centre.position;

            float waver = Mathf.PerlinNoise(noiseSeed, Time.time * look.lightWaver);
            glow.intensity = look.lightIntensity * (0.45f + 0.55f * waver) * (xray ? 1f : 0.6f);
        }

        private void End()
        {
            ghost?.Dispose();
            skeleton?.Dispose();
            if (glow != null) RuntimeObjects.Discard(glow.gameObject);
            ghost = null;
            skeleton = null;
            glow = null;
        }
    }
}
