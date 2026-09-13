using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while rummaging in a deployed pack.
    ///
    /// <para>
    /// The pose is authored, not orbited: on the rig's centre axis, 2.46 m out on the side of it
    /// AWAY from the player, 1.9 m up over the flat mat and 2.2 m up over a pack that is still
    /// shut — all in the ORIGINAL frame, so 2.58, 1.995 and 2.31 m at the current
    /// <see cref="PackScale.Factor"/> — pitched 45&#176; down, at FOV 40, looking back down the
    /// player&#8594;pack line. The distance was 1.9 m until the 2026-08-25 board deepening pushed
    /// the leading edge from 0.94 m to 1.50 m out from the rig root and left the near cells
    /// 0.40 m from the lens, cropped out of the bottom of the frame; the lens moved back by
    /// exactly the growth — 0.56 m — so the near edge sits the 0.96 m from the lens it always
    /// did. FOV 40 is narrower than gameplay FOV on purpose: a narrow lens flattens perspective,
    /// which is what makes comparing two items' true sizes honest and makes a placement easy to
    /// judge.
    /// </para>
    /// <para>
    /// <b>The height and the pitch are one number, not two.</b> The frame's bottom edge sits
    /// <c>Pitch + Fov/2</c> below horizontal and the board's leading edge sits
    /// <c>atan(HeightUp / (DistanceOut - 1.50))</c> below it, so raising the lens on a fixed pitch
    /// slides the rig DOWN the screen and crops the front row of cells off the bottom — at the old
    /// 1.5 m / 38&#176; there was 0.6&#176; in it. <see cref="PitchForHeight"/> is the pitch that
    /// keeps the optical axis on the same spot of the mat whatever the height is, and
    /// <see cref="NearEdgeMargin"/> is the headroom that leaves; <c>PackFocusShotTests</c> pins
    /// both, so a future lift comes back as a red test rather than as a cropped mat.
    /// </para>
    /// <para>
    /// <b>Every distance rides <see cref="PackScale.Factor"/>, and the framing never moves with
    /// it.</b> They are offsets from the rig's own origin, so multiplying them by exactly what the
    /// rig, the faces and the gear are multiplied by makes every shot a similarity transform of
    /// the one before it: the same solid angle, the same pitch, the same field of view, every item
    /// filling the same fraction of the frame. That held when the factor went 1 -> 1.5 on
    /// 2026-09-01 (leaving the lens put would have had it a third of the way inside a 3.12 m mat)
    /// and it holds the same way at 1.5 -> 1.05. <b>So shrinking the rig does not make the pack
    /// smaller on screen in focus mode</b> — the camera comes in with it and the mat fills the
    /// frame exactly as it did. What changes is the rig in the WORLD: how big it is on the
    /// player's back and standing on the sand. Wanting the mat to read smaller in focus mode is a
    /// change to <see cref="PackFocusCamera"/>'s own numbers, not to the factor.
    /// </para>
    /// </summary>
    public sealed class PackFocusCamera : FocusCamera
    {
        // ── The authored pose ────────────────────────────────────────────────
        //
        // The shot is ON the player→pack axis, facing the pack square — from the FAR side of the
        // rig, looking back down that axis. The mat unfolds away from the player's feet, so a lens
        // on the player's own side of the pack frames the closed harness back and none of the
        // items; crossing over is what puts the mat, and everything laid out on it, toward the
        // camera.
        //
        // Distances are offsets from the RIG's origin, so they take PackScale.Factor with the rig.
        // The angles do not, and must not: a similarity transform leaves every angle alone, which
        // is exactly why the enlarged pack frames identically from here.
        /// <summary>Metres past the rig's origin the lens sits, in the ORIGINAL frame.</summary>
        private const float Standoff = 2.46f;

        private static readonly float DistanceOut = PackScale.Apply(Standoff);

        /// <summary>
        /// Metres above the rig's origin with the board lying flat, in the ORIGINAL frame.
        /// The mat is what the shot is for, so this is the height the pitch is derived from.
        /// </summary>
        public const float OpenHeight = 1.9f;

        /// <summary>
        /// Metres above the rig's origin with the flap standing up, in the ORIGINAL frame.
        ///
        /// <para>
        /// Higher, and at the same pitch: a shut pack is a taller, shorter object and the lift is
        /// what keeps all of it — the standing board included — comfortably inside the frame. It
        /// costs nothing to be up here because the crop this file's header describes is the flat
        /// board's leading edge, and with the flap up there is no leading edge in front of the
        /// lens at all.
        /// </para>
        /// </summary>
        public const float ClosedHeight = 2.2f;

        /// <summary>
        /// How far past the rig's origin, toward the lens, the optical axis meets the ground.
        /// Derived once from the shot that shipped before the 2026-09-06 lift — 1.5 m up at
        /// 38&#176; from 2.46 m out lands 0.54 m from the root, on the mat rather than on the
        /// pack's own body — and then held fixed, which is what makes a higher lens read as the
        /// same shot from higher up instead of as a different one.
        /// </summary>
        private const float AimFromRoot = 0.54f;

        /// <summary>
        /// How far the flat board's leading edge reaches from the rig's origin toward the lens,
        /// in the ORIGINAL frame. Measured on the rig after the 2026-08-25 deepening. This is the
        /// thing the bottom of the frame has to clear.
        /// </summary>
        private const float BoardReach = 1.50f;

        private const float FieldOfView = 40f;

        /// <summary>
        /// The pitch that puts the optical axis on the same spot of the mat from any height.
        ///
        /// <para>
        /// Pure, static and public so <c>PackFocusShotTests</c> can ask it the same question the
        /// camera does. Both arguments are in the original frame; the answer is an angle, so it is
        /// the same at every <see cref="PackScale.Factor"/>.
        /// </para>
        /// </summary>
        public static float PitchForHeight(float height) =>
            Mathf.Atan2(height, Standoff - AimFromRoot) * Mathf.Rad2Deg;

        /// <summary>
        /// The one pitch the shot is taken at, derived from the OPEN height. Held constant through
        /// the flap's whole swing on purpose: the shut pack is framed by lifting the lens alone,
        /// and steepening the shot as the board came up would be a second motion nobody asked for
        /// on top of the one the player is making.
        /// </summary>
        public static readonly float Pitch = PitchForHeight(OpenHeight);

        /// <summary>
        /// Degrees between the bottom edge of the frame and the flat board's leading edge, for any
        /// height and pitch. Positive is headroom; negative means the front row of cells is
        /// cropped off the bottom of the screen.
        ///
        /// <para>
        /// Both arguments are explicit rather than derived, because the two questions worth asking
        /// of this are about shots that are NOT the one shipped: what the old 1.5 m / 38&#176; had
        /// left in it, and what raising the lens on that same 38&#176; would have cost. Ask it
        /// about the shipped shot with <see cref="OpenHeight"/> and <see cref="Pitch"/>.
        /// </para>
        /// <para>
        /// Only the OPEN shot has to answer it. At <see cref="ClosedHeight"/> the flap is up, so
        /// there is no flat board in front of the lens to crop against — which is exactly why the
        /// shut pack can be framed from higher up without the pitch moving with it.
        /// </para>
        /// </summary>
        public static float NearEdgeMargin(float height, float pitch) =>
            pitch + FieldOfView * 0.5f
            - Mathf.Atan2(height, Standoff - BoardReach) * Mathf.Rad2Deg;

        // ── The arrival ──────────────────────────────────────────────────────
        //
        // The pack's own arc is 0.9 s (BackpackController.deploySeconds). Starting 0.15 s in and
        // taking 0.9 s means the camera settles a breath after the rig does. It deliberately does
        // NOT wait for the unfold to finish: this is an interaction performed hundreds of times a
        // session, and 1.4 s of nothing at the front of it is the difference between a pocket and
        // a cutscene.
        private const float Delay = 0.15f;
        private const float Seconds = 0.9f;

        private BackpackObject pack;

        // Where the LENS looks: down the player→pack line, from beyond the pack back toward the
        // player. The reverse of what the caller hands over, reversed once on the way in.
        private Vector3 lensForward = Vector3.forward;

        /// <summary>
        /// Puts a focus camera on <paramref name="pack"/>.
        /// </summary>
        /// <param name="pack">The deployed rig. Tracked live rather than sampled once, because it
        /// is still mid-arc when this is called and lands under the camera as it arrives — and
        /// because the shot rides how far up its arc the front flap is standing.</param>
        /// <param name="viewDirection">The player→rig line, flattened. The camera sits DistanceOut
        /// PAST the rig along it and looks back down it, so the mat faces the lens square-on and
        /// the player stands beyond the pack. Frozen at spawn so the shot cannot swing while the
        /// player's body drifts.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static PackFocusCamera Spawn(BackpackObject pack, Vector3 viewDirection, Camera playerCamera)
        {
            if (pack == null) return null;

            var go = new GameObject("PackFocusCamera");
            var focus = go.AddComponent<PackFocusCamera>();

            focus.pack = pack;

            // Reversed here, once: the caller measures player→pack, the lens looks pack→player.
            var flat = new Vector3(viewDirection.x, 0f, viewDirection.z);
            focus.lensForward = flat.sqrMagnitude > 1e-6f ? -flat.normalized : Vector3.forward;

            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => pack != null;

        /// <summary>
        /// Where the shot sits between its two heights, 0 over the flat mat and 1 over a pack that
        /// is still shut.
        ///
        /// <para>
        /// Read off the flap's own eased progress rather than off <c>IsRacked</c>, so the lens
        /// rises and falls WITH the board instead of jumping when the state flips — including
        /// under the drag, where the board is being pulled through its arc by hand. It is 0.3 m
        /// over a 0.45 s swing, which is a move small enough to read as the camera keeping the
        /// pack framed rather than as the camera doing something of its own (GDC-L1-FEEL-0006).
        /// </para>
        /// </summary>
        private float Shut => pack.RackProgress;

        /// <summary>Live rather than sampled once: the rig is still travelling along its deploy
        /// arc when the camera spawns, so tracking it means the shot converges on the landing pose
        /// instead of framing the patch of sand the pack was over when the key was pressed.</summary>
        protected override Vector3 LensPosition() =>
            pack.transform.position - lensForward * DistanceOut
            + Vector3.up * PackScale.Apply(Mathf.Lerp(OpenHeight, ClosedHeight, Shut));

        /// <summary>Frozen with <see cref="lensForward"/> at spawn, so unlike the position this does not move.</summary>
        protected override float LensYaw() => Quaternion.LookRotation(lensForward, Vector3.up).eulerAngles.y;

        /// <summary>
        /// Derived from the OPEN height at every point in the swing, so the pitch is a constant and
        /// the shut pack is framed by lifting the lens alone. Tying it to the live height instead
        /// would steepen the shot as the board came up, which is a second motion nobody asked for
        /// on top of the one the player is making.
        /// </summary>
        protected override float PitchDown => Pitch;

        protected override float Fov => FieldOfView;
        protected override float FlyInDelay => Delay;
        protected override float FlyInSeconds => Seconds;
        protected override float FocusDistance() => Vector3.Distance(transform.position, pack.transform.position);
    }
}
