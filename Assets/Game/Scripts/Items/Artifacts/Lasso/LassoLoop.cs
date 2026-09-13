using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The honda — the loop on the end of the rope, and the whole reason a lasso reads as a lasso.
    ///
    /// <para>
    /// It has four states and the old item drew one. A spinning circle was pushed along the arc and
    /// then parked on whatever it hit, which meant the two most legible moments the item has never
    /// existed: the wind-up, where a player winding a rope over their head is visible across the
    /// map, and the cinch, where an open loop closes onto a neck. Everything else about a lasso is
    /// a line moving; those two are the lasso.
    /// </para>
    /// <para>
    /// <b>The drawn radius is the catch radius.</b> <see cref="Radius"/> is what the artifact tests
    /// the arc against, rather than a separate serialized number, so a wide charged loop genuinely
    /// has a wider mouth than a cold one and the player is never told they missed a thing that
    /// visibly went through the hoop.
    /// </para>
    /// <para>
    /// <b>It is a lariat, not a hoop, and it is tied to the rope.</b> The shape is pinched at the
    /// honda and bellied opposite it, and every drawing method returns the world position of that
    /// knot so the artifact can end the rope <i>on</i> it. Both halves of that are fixes for the
    /// same complaint: the loop used to be a perfect circle centred on the point the rope stopped
    /// at, so the cable died in the middle of the hole with nothing joining the two. A hoop and a
    /// line that happen to point at each other are not a lasso.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoLoop
    {
        [SerializeField] private int segments = 28;

        [Header("Radius, metres")]
        [Tooltip("Uncharged: the rope still coiled in the hand.")]
        [SerializeField] private float coilRadius = 0.22f;

        [Tooltip("Fully charged: the mouth of a rope wound properly over the head.")]
        [SerializeField] private float openRadius = 0.8f;

        [Tooltip("After the cinch — a collar, not a hoop.")]
        [SerializeField] private float collarRadius = 0.45f;

        [Header("Motion")]
        [SerializeField] private float spinSpeed = 620f;

        [Tooltip("Degrees from the travel axis while twirling. Near flat, because a loop spun " +
                 "overhead is seen from underneath and an edge-on circle is an invisible one.")]
        [SerializeField] private float twirlTilt = 74f;

        [Tooltip("Degrees from the travel axis in flight. Shallower, so the loop leads the rope " +
                 "open-face-first and the player can see the hole they are aiming.")]
        [SerializeField] private float flightTilt = 50f;

        [Tooltip("Metres the loop's centre orbits while twirling, at full charge. This is what " +
                 "makes the wind-up a sweep rather than a hoop spinning on the spot.")]
        [SerializeField] private float orbitRadius = 0.35f;

        [SerializeField] private float cinchDuration = 0.25f;

        [Header("Shape — a lariat, not a hoop")]
        [Tooltip("How sharply the two strands pinch together into the honda, where the rope ends " +
                 "and the loop begins.\n\n" +
                 "This is the whole difference between a rope loop and a circle. At 0 the strands " +
                 "meet the knot at a right angle and the thing is a hoop with a cable pointing at " +
                 "it; raising it draws them together into a throat, which is the shape the eye " +
                 "reads as tied. Higher is a longer, narrower throat.")]
        [SerializeField, Range(0f, 2.5f)] private float hondaPinch = 0.55f;

        [Tooltip("How much further the far side hangs than the honda side, as a fraction of the " +
                 "radius. A loop hanging off a knot carries its width away from the knot, which " +
                 "is what makes it a pear rather than a ring with a dent in it.")]
        [SerializeField, Range(0f, 0.6f)] private float belly = 0.12f;

        [Tooltip("Sag as a fraction of the radius, paid only once the loop has stopped turning.\n\n" +
                 "A spinning loop is held flat by its own speed and a resting one hangs, so this " +
                 "is scaled by how much of the spin is left — which means it costs nothing during " +
                 "the twirl and the throw, and shows up exactly where it belongs: on the collar " +
                 "sitting round an animal's neck.")]
        [SerializeField, Range(0f, 0.6f)] private float droop = 0.2f;

        [Header("Deformation")]
        [Tooltip("A rope loop is never the same shape twice. Two incommensurate terms, so the " +
                 "wobble drifts rather than pulsing in place.")]
        [SerializeField] private float wobbleAmount = 0.1f;

        [SerializeField] private float wobbleSpeed = 1.8f;

        [Tooltip("Metres. Keep this equal to LassoRope's width — the loop is not a separate object, " +
                 "it is the last few feet of the same rope tied back on itself, and a loop drawn " +
                 "thinner than the rope it hangs from reads as two different things.")]
        [SerializeField] private float width = 0.062f;

        [Tooltip("Metres of rope one repeat of the texture covers. Keep equal to LassoRope's — the " +
                 "loop is the last few feet of the same rope, and a braid at two different pitches " +
                 "is two different ropes.")]
        [SerializeField] private float metresPerTextureRepeat = 0.35f;

        [Tooltip("Keep this the SAME material asset as LassoRope's. The loop is not a separate " +
                 "object — it is the last few feet of the rope tied back on itself.")]
        [SerializeField] private Material material;

        private LineRenderer line;
        private float angle;
        private float radius;
        private float spin;
        private float cinchElapsed;
        private float cinchFrom;
        private bool cinching;

        // The throat profile's own maximum, cached against the pinch it was measured for.
        // See ThroatPeak.
        private float throatPeak = 1f;
        private float throatPeakFor = float.NaN;

        public bool IsBound => line != null;

        /// <summary>The radius the loop is drawn at right now, and therefore what it can catch.</summary>
        public float Radius => radius;

        public void Bind(LineRenderer renderer) => line = renderer;

        public void Show()
        {
            radius = coilRadius;
            spin = spinSpeed;
            cinching = false;
            cinchElapsed = 0f;

            if (line == null) return;

            line.positionCount = Mathf.Max(4, segments) + 1;

            // Flat curve as well as the multiplier: a tapering curve authored on the prefab's
            // LineRenderer is applied UNDERNEATH the multiplier, so setting the multiplier alone
            // leaves the loop thinning out to nothing around its far side.
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.widthMultiplier = width;
            line.numCornerVertices = 2;

            // The same treatment the rope gets, and for the same reason: the loop IS the rope, so a
            // braid that tiles on one and smears on the other reads as two different objects tied
            // together. See LassoRope.ApplyMaterialSettings.
            if (material != null) line.material = material;

            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(1f / Mathf.Max(metresPerTextureRepeat, 0.01f), 1f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;

            line.enabled = true;
        }

        public void Hide()
        {
            if (line == null) return;
            line.enabled = false;
        }

        /// <summary>
        /// Over the head, winding up. <paramref name="charge"/> runs 0 to 1 and is the only thing
        /// the player can see of how far this throw will go.
        /// </summary>
        /// <returns>The honda: where the rope's far end belongs this frame.</returns>
        public Vector3 Twirl(Vector3 centre, Vector3 up, float charge, float deltaTime)
        {
            spin = spinSpeed;
            angle += spin * deltaTime;
            radius = Mathf.Lerp(coilRadius, openRadius, Mathf.Clamp01(charge));

            Vector3 axis = up.sqrMagnitude < 1e-4f ? Vector3.up : up.normalized;

            // The loop's CENTRE travels too, so the rope sweeps a cone rather than the loop
            // rotating on the spot. A lasso wound overhead is not a spinning hoop pinned in the
            // air — the whole thing goes round, which is what makes the rope from the hand look
            // like it is driving the spin instead of just being attached to it.
            float phase = angle * Mathf.Deg2Rad;
            Vector3 sideways = Vector3.Cross(axis, Vector3.forward);
            if (sideways.sqrMagnitude < 1e-4f) sideways = Vector3.right;
            sideways.Normalize();

            Vector3 other = Vector3.Cross(axis, sideways);
            float orbit = orbitRadius * Mathf.Clamp01(charge);

            centre += (sideways * Mathf.Cos(phase) + other * Mathf.Sin(phase)) * orbit;

            return Draw(centre, axis, twirlTilt);
        }

        /// <summary>In the air, leading the rope. The radius holds whatever the twirl wound up to.</summary>
        /// <returns>The honda: where the rope's far end belongs this frame.</returns>
        public Vector3 Fly(Vector3 centre, Vector3 travelDirection, float charge, float deltaTime)
        {
            spin = spinSpeed;
            angle += spin * deltaTime;
            radius = Mathf.Lerp(coilRadius, openRadius, Mathf.Clamp01(charge));

            Vector3 axis = travelDirection.sqrMagnitude < 1e-4f ? Vector3.forward : travelDirection.normalized;
            return Draw(centre, axis, flightTilt);
        }

        /// <summary>The loop has closed on something. Start shutting it.</summary>
        public void BeginCinch()
        {
            cinching = true;
            cinchElapsed = 0f;
            cinchFrom = radius;
        }

        /// <summary>
        /// Riding the target. Finishes the cinch, then sits as a collar across the pull.
        ///
        /// The spin winds down rather than stopping, because a loop that stops dead reads as the
        /// animation being switched off. <paramref name="ropeDirection"/> orients the collar
        /// perpendicular to the rope, which is where a rope under tension actually sits.
        /// </summary>
        /// <returns>The honda: where the rope's far end belongs this frame.</returns>
        public Vector3 Ride(Vector3 centre, Vector3 ropeDirection, float deltaTime)
        {
            if (cinching)
            {
                cinchElapsed += deltaTime;
                float t = Mathf.Clamp01(cinchElapsed / Mathf.Max(cinchDuration, 0.0001f));
                radius = Mathf.Lerp(cinchFrom, collarRadius, Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f) cinching = false;
            }

            spin = Mathf.MoveTowards(spin, 0f, spinSpeed * deltaTime);
            angle += spin * deltaTime;

            Vector3 axis = ropeDirection.sqrMagnitude < 1e-4f ? Vector3.forward : ropeDirection.normalized;
            return Draw(centre, axis, 0f);
        }

        /// <summary>
        /// One closed loop, in a basis built from <paramref name="axis"/>: tilted, spun, pinched at
        /// its knot and deformed.
        ///
        /// <para>
        /// The knot sits at the profile's origin and therefore rides the spin with everything else,
        /// which is what a turning loop does — the honda goes round with the rope, and the spoke
        /// running back to the hand sweeps with it. Holding the knot still against the rope instead
        /// would need a direction the twirl cannot supply: there the hand is almost straight below
        /// the loop's own plane, so the projection is degenerate in exactly the state the loop is
        /// widest and most looked at.
        /// </para>
        /// </summary>
        /// <returns>The honda's world position.</returns>
        private Vector3 Draw(Vector3 centre, Vector3 axis, float tilt)
        {
            Quaternion basis = Quaternion.LookRotation(axis);
            Quaternion tilted = Quaternion.AngleAxis(tilt, basis * Vector3.right) * basis;
            Quaternion spun = Quaternion.AngleAxis(angle, axis) * tilted;

            Vector3 right = spun * Vector3.right;
            Vector3 up = spun * Vector3.up;

            float peak = ThroatPeak();

            // Centrifugal force holds a spinning loop flat, so the sag is only paid as the spin
            // winds down — which is precisely when the loop has become a collar hanging on an
            // animal rather than a hoop being thrown at one.
            float sag = radius * droop * (1f - Mathf.Clamp01(spin / Mathf.Max(spinSpeed, 1f)));

            Vector3 honda = centre + Offset(0f, right, up, peak, sag);

            if (line == null || !line.enabled) return honda;

            int count = Mathf.Max(4, segments);
            if (line.positionCount != count + 1) line.positionCount = count + 1;

            for (int i = 0; i <= count; i++)
                line.SetPosition(i, centre + Offset(i / (float)count * Mathf.PI * 2f, right, up, peak, sag));

            return honda;
        }

        /// <summary>
        /// One point on the loop, as an offset from its centre. <paramref name="phi"/> runs 0 to 2π
        /// from the honda, all the way round, and back to it.
        ///
        /// <para>
        /// The two sine terms of the wobble are deliberately incommensurate in both frequency and
        /// rate: one term alone is a loop pulsing between two fixed shapes, which the eye reads as
        /// a wobble on a timer rather than a rope that is never quite round.
        /// </para>
        /// </summary>
        private Vector3 Offset(float phi, Vector3 right, Vector3 up, float peak, float sag)
        {
            // 0 at the honda, 1 directly opposite it. Everything that makes this a rope loop rather
            // than a circle is a function of this one term. Clamped because sin(π) lands a hair
            // below zero in floating point at phi = 2π, and Pow of a negative base is NaN — which
            // would put the loop's closing vertex, and therefore the knot, nowhere.
            float away = Mathf.Pow(Mathf.Max(Mathf.Sin(phi * 0.5f), 0f), hondaPinch);

            float distort = Mathf.Sin(phi * 2f + angle * 0.03f * wobbleSpeed) * 0.6f
                          + Mathf.Sin(phi * 3f - angle * 0.05f * wobbleSpeed) * 0.4f;

            float r = radius * (1f + distort * wobbleAmount);

            // Cosine alone is a circle. The belly term pushes the half furthest from the knot out
            // past it, which is where a loop hanging off a honda actually carries its width.
            float axial = Mathf.Cos(phi) - belly * away;

            // Normalised by the profile's own peak, so tightening the throat narrows the throat and
            // not the whole mouth — the mouth is what the catch is judged by.
            float lateral = Mathf.Sin(phi) * away / peak;

            return (right * axial + up * lateral) * r + Vector3.down * (sag * away);
        }

        /// <summary>
        /// The widest the throat profile gets, for the pinch currently authored.
        ///
        /// <para>
        /// <c>sin φ · sin(φ/2)^pinch</c> has no useful closed-form maximum and the pinch is a
        /// serialized knob, so it is measured once and cached against the value it was measured
        /// for. Without the normalisation, raising the pinch to sharpen the knot would quietly
        /// shrink the whole loop — and <see cref="Radius"/> is the catch radius, so that would
        /// close the mouth while <see cref="LassoAim"/> went on drawing the old one.
        /// </para>
        /// </summary>
        private float ThroatPeak()
        {
            if (throatPeakFor == hondaPinch) return throatPeak;

            const int probes = 64;
            float found = 0f;

            for (int i = 1; i < probes; i++)
            {
                float phi = i / (float)probes * Mathf.PI;
                found = Mathf.Max(found, Mathf.Sin(phi) * Mathf.Pow(Mathf.Sin(phi * 0.5f), hondaPinch));
            }

            throatPeakFor = hondaPinch;
            throatPeak = Mathf.Max(found, 0.01f);
            return throatPeak;
        }
    }
}
