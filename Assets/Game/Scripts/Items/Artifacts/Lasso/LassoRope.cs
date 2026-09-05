using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The rope, and only the rope.
    ///
    /// <para>
    /// A Verlet chain rather than the analytic curve <see cref="GrappleRope"/> draws, and the
    /// difference is the point. The hook's cable only ever has to do two things — fly straight and
    /// hang taut — and two sine waves describe both. A lasso's rope has to be paid out behind a
    /// flying loop, snap tight, hang with whatever slack the player has reeled in, and then coil
    /// back into the hand on a miss. Four behaviours, and writing four analytic cases would be
    /// writing four lies. One chain of points with gravity and distance constraints gives all four
    /// as consequences, and gives the ones nobody thought to ask for — a rope dragged sideways
    /// lagging behind the hand, a rope going slack falling rather than shrinking — for nothing.
    /// </para>
    /// <para>
    /// <b>Rest length is not the gap.</b> Every method here takes the constraint length from the
    /// artifact rather than measuring the distance between the two ends, because the difference
    /// between the two IS the slack and the slack is the whole shape of the rope. The rope this
    /// replaces sagged by a constant scaled to the SPAN, so a rope pulled bar-tight across forty
    /// metres hung as limply as one with ten metres of slack in it.
    /// </para>
    /// <para>
    /// A plain serializable class rather than a MonoBehaviour, for the same reason
    /// <see cref="GrappleRope"/> is one: it tunes in the Inspector under its own foldout without
    /// adding a component to wire up or a GameObject to find.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoRope
    {
        [Tooltip("Nodes in the chain. Below about 20 a hanging rope reads as a folded ruler; " +
                 "above about 40 the constraint passes start costing more than they show.")]
        [SerializeField] private int points = 30;

        [Tooltip("Jakobsen passes per substep. This is what rope STIFFNESS actually is — one pass " +
                 "is elastic, and a rope that stretches under load stops reading as rope.")]
        [SerializeField, Range(1, 16)] private int iterations = 8;

        [Tooltip("Metres per second squared. Deliberately above real gravity: a game-scale rope " +
                 "has to settle inside the fraction of a second the player is looking at it.")]
        [SerializeField] private float gravity = 14f;

        [Tooltip("Velocity retained per substep. Below about 0.9 the rope is a wet towel; at 1 it " +
                 "never stops ringing.")]
        [SerializeField, Range(0.8f, 1f)] private float damping = 0.96f;

        [Tooltip("Fixed substep, seconds. Fixed rather than the frame's own delta so the rope " +
                 "settles to the same shape on every machine regardless of frame rate — two " +
                 "players watching the same rope is the case that makes this matter.")]
        [SerializeField] private float simulationStep = 1f / 90f;

        [Tooltip("Constraint strength, 0-1. Anything below 1 is a bungee.")]
        [SerializeField, Range(0.1f, 1f)] private float stiffness = 1f;

        [Tooltip("Resistance to bending, 0-1. Without this the rope folds into a zigzag — a chain " +
                 "of distance constraints cannot tell a smooth curve from a concertina, because " +
                 "both have every segment at exactly its rest length. Raise it if the rope still " +
                 "looks like stairs; lower it if the rope goes stiff and stops hanging.")]
        [SerializeField, Range(0f, 0.5f)] private float bendResistance = 0.3f;

        [Header("Line")]
        [Tooltip("Extra vertices at each joint. Zero mitres every bend into a hard corner, which " +
                 "is fine on a straight line and wrong on a hanging one.")]
        [SerializeField, Range(0, 6)] private int cornerVertices = 2;

        [SerializeField, Range(0, 6)] private int capVertices = 2;

        [Tooltip("Rope thickness in metres, the same from end to end.\n\n" +
                 "A rope is braided from a fixed number of strands, so it does not taper. The " +
                 "grapple's cable tapers because it is a cable being paid out of a gun, which is " +
                 "a different object — copying that here made the far end of the rope, which is " +
                 "the end the player is looking at, the thinnest part of it.\n\n" +
                 "Matches the loop's authored width, because the loop IS this rope. Keep the two " +
                 "equal.")]
        [SerializeField] private float width = 0.062f;

        [Tooltip("Metres of rope one repeat of the material's texture covers.\n\n" +
                 "A LineRenderer defaults to STRETCHING its texture once across the whole line, " +
                 "which on a rope that is 2 m long in the hand and 26 m long across a canyon means " +
                 "the braid is a different size every time you look at it — and at full stretch it " +
                 "is not a braid at all, it is one smear. Tiling makes the strands a fixed size in " +
                 "the world, which is what tells the eye how thick the rope is and how far away.")]
        [SerializeField] private float metresPerTextureRepeat = 0.35f;

        [Tooltip("What the rope is made of. Left empty the LineRenderer keeps whatever was " +
                 "authored on it, which is how this ended up drawn in Custom_Wood — a surface " +
                 "material off a prop, stretched once across up to 26 m of cable.\n\n" +
                 "Assigned rather than authored for the reason LeashRope assigns its own: the rope " +
                 "and the loop are the same rope and must not be able to drift onto two materials.")]
        [SerializeField] private Material material;

        [Header("Bite — the tension crack")]
        [Tooltip("Metres of shock sent down the rope at the instant the loop closes.")]
        [SerializeField] private float snapAmplitude = 0.4f;

        [Tooltip("Cycles of shock along the rope. Kept low on purpose: a wave with as many " +
                 "cycles as the rope has nodes is a zigzag, not a rope.")]
        [SerializeField] private float snapWaves = 3f;

        private LineRenderer line;
        private Vector3[] pos;
        private Vector3[] prev;
        private Vector3[] smoothed;
        private float accumulator;

        /// <summary>Whether there is a renderer to draw into at all.</summary>
        public bool IsBound => line != null;

        /// <summary>Hand over the renderer. Safe with null — the lasso simply draws no rope.</summary>
        public void Bind(LineRenderer renderer) => line = renderer;

        /// <summary>
        /// Start a rope between these two points.
        ///
        /// Every node is laid on the straight line between the ends rather than left where the
        /// last throw abandoned it, because a chain that starts as a tangle at the origin spends
        /// its first half second visibly falling into place.
        /// </summary>
        public void Show(Vector3 start, Vector3 end)
        {
            Allocate();

            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = Vector3.Lerp(start, end, i / (float)(pos.Length - 1));
                prev[i] = pos[i];
            }

            accumulator = 0f;

            if (line == null) return;

            line.positionCount = pos.Length;
            line.numCornerVertices = cornerVertices;
            line.numCapVertices = capVertices;

            // A flat curve, not just a multiplier: the LineRenderer on the prefab is shared with
            // whatever was authored on it, and a leftover tapering curve would still be applied
            // underneath a uniform multiplier.
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.widthMultiplier = width;

            ApplyMaterialSettings();

            line.enabled = true;
            Redraw();
        }

        /// <summary>
        /// How the rope takes its material, as opposed to which one it is given.
        ///
        /// <para>
        /// Set here rather than authored, because the prefab's LineRenderer is a shared asset that
        /// has been copied between effects and carries whatever the last one wanted. Both of these
        /// were wrong on it: the texture was stretched (see <see cref="metresPerTextureRepeat"/>)
        /// and the rope cast and received shadows.
        /// </para>
        /// <para>
        /// <b>A rope does not cast a shadow worth having.</b> A LineRenderer is a view-aligned
        /// ribbon — it faces the camera, so the shadow it casts is the shadow of a flat strip seen
        /// from the light's angle, which is to say a shadow of the wrong shape that changes as the
        /// player turns their head. Thirty nodes of it, per rope, per frame.
        /// </para>
        /// </summary>
        private void ApplyMaterialSettings()
        {
            if (material != null) line.material = material;

            // Tile, not Stretch: Tile repeats once per world unit, which textureScale then divides
            // into the pitch we actually want. Stretch — the default, and what the prefab had —
            // fits exactly one repeat to the whole line however long it is.
            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(1f / Mathf.Max(metresPerTextureRepeat, 0.01f), 1f);

            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
        }

        public void Hide()
        {
            if (line == null) return;
            line.enabled = false;
        }

        /// <summary>
        /// Advance the rope and draw it.
        ///
        /// <paramref name="ropeLength"/> is the CONSTRAINT length, not the gap between the ends —
        /// see the class summary. Pass the distance the head has travelled while the loop is in
        /// the air, and the artifact's own rope length once it has caught.
        /// </summary>
        public void Simulate(Vector3 start, Vector3 end, float ropeLength, float deltaTime)
        {
            if (pos == null) Show(start, end);

            // Clamped, so a hitch or a breakpoint cannot hand this a two-second delta and spiral
            // through a hundred substeps trying to catch up.
            accumulator = Mathf.Min(accumulator + deltaTime, 0.1f);

            float segment = Mathf.Max(ropeLength, 0.01f) / (pos.Length - 1);

            while (accumulator >= simulationStep)
            {
                Step(start, end, segment, simulationStep);
                accumulator -= simulationStep;
            }

            Redraw();
        }

        /// <summary>
        /// The moment the loop closes: a shock that runs down the rope.
        ///
        /// Written into <see cref="prev"/> rather than <see cref="pos"/>, and that is not a detail.
        /// In a Verlet chain the velocity of a node IS the gap between where it is and where it
        /// was, so moving <c>prev</c> gives it speed while moving <c>pos</c> teleports it. Writing
        /// the shock into <c>pos</c> would displace the rope's shape for exactly one frame and then
        /// have the constraints yank it back — a flicker, not a crack.
        /// </summary>
        public void Snap()
        {
            if (pos == null || pos.Length < 3) return;

            Vector3 axis = pos[pos.Length - 1] - pos[0];
            if (axis.sqrMagnitude < 1e-4f) return;

            Vector3 lateral = Vector3.Cross(axis.normalized, Vector3.up);
            lateral = lateral.sqrMagnitude < 1e-4f ? Vector3.right : lateral.normalized;

            for (int i = 1; i < pos.Length - 1; i++)
            {
                float t = i / (float)(pos.Length - 1);

                // Pinned at both ends, so the shock has to vanish at both.
                //
                // A few smooth cycles, NOT a per-node alternating sign. The alternating version
                // that was here reads as a crack in a diagram and as a flight of stairs on screen:
                // one node up, the next down, at the highest frequency the chain can represent.
                // Nothing about it can look like rope, because a rope cannot bend that sharply —
                // and the constraint solver preserves it, because a zigzag has every segment at
                // exactly its rest length.
                float envelope = Mathf.Sin(t * Mathf.PI);
                float wave = Mathf.Sin(t * Mathf.PI * 2f * snapWaves);

                prev[i] -= lateral * (snapAmplitude * envelope * wave);
            }
        }

        // ── The chain ──────────────────────────────────────────────────────────

        private void Allocate()
        {
            int count = Mathf.Max(3, points);
            if (pos != null && pos.Length == count) return;

            pos = new Vector3[count];
            prev = new Vector3[count];
        }

        private void Step(Vector3 start, Vector3 end, float segment, float step)
        {
            Vector3 fall = Vector3.down * (gravity * step * step);

            for (int i = 1; i < pos.Length - 1; i++)
            {
                Vector3 velocity = (pos[i] - prev[i]) * damping;
                prev[i] = pos[i];
                pos[i] += velocity + fall;
            }

            // Both ends are pinned, and prev is pinned with them: a node whose prev lags behind
            // carries velocity, so an end left with a stale prev would inject the hand's own
            // motion into the rope every single substep and the cable would never stop thrashing.
            pos[0] = start;
            prev[0] = start;
            pos[pos.Length - 1] = end;
            prev[pos.Length - 1] = end;

            // Alternating direction, because this is Gauss-Seidel: a pass carries tension from the
            // end it starts at all the way to the other, so running every pass the same way makes
            // the rope converge from the hand outward and leaves the far end lagging.
            for (int pass = 0; pass < iterations; pass++)
                Constrain(segment, forward: (pass & 1) == 0);

            Unkink();
            Straighten(start, end, segment * (pos.Length - 1));
        }

        /// <summary>
        /// Take the sharp folds out, and only the sharp folds.
        ///
        /// <para>
        /// This is bending stiffness, and a rope without it is the "stairs" problem. Distance
        /// constraints have nothing to say about the ANGLE at a node — a chain folded into a
        /// perfect concertina satisfies every one of them exactly, so the solver has no reason to
        /// undo it and every reason to keep it. Any slack at all then buckles into the sharpest
        /// fold the node count allows, which is one node up, one node down.
        /// </para>
        /// <para>
        /// Pulling each node toward the midpoint of its two neighbours is a Laplacian smooth, and
        /// what makes it the right tool here is how sharply it discriminates by frequency: at this
        /// strength a one-node zigzag loses about a quarter of its amplitude every substep and is
        /// gone within a tenth of a second, while the long sag of a hanging rope loses well under
        /// a percent and is continuously replaced by gravity. It removes the fold without
        /// straightening the rope.
        /// </para>
        /// <para>
        /// Through a scratch buffer rather than in place: smoothing a node with an already-smoothed
        /// neighbour drags the whole rope toward the end the loop started at, which shows up as a
        /// cable that leans toward the hand.
        /// </para>
        /// </summary>
        private void Unkink()
        {
            if (bendResistance <= 0f || pos.Length < 3) return;

            if (smoothed == null || smoothed.Length != pos.Length)
                smoothed = new Vector3[pos.Length];

            for (int i = 1; i < pos.Length - 1; i++)
            {
                Vector3 midpoint = (pos[i - 1] + pos[i + 1]) * 0.5f;
                smoothed[i] = Vector3.Lerp(pos[i], midpoint, bendResistance);
            }

            for (int i = 1; i < pos.Length - 1; i++)
                pos[i] = smoothed[i];
        }

        /// <summary>
        /// Pull the chain onto the straight line between its ends, in proportion to how taut it is.
        ///
        /// <para>
        /// Not a cheat, and not a substitute for the solver — a correction for something the solver
        /// cannot do inside a frame budget. Distance constraints are a Gauss-Seidel relaxation, and
        /// relaxations converge slowly precisely where the system is stiffest, which for a rope is
        /// the taut case. At eight passes a 30-node chain pinned at exactly its own length still
        /// hangs the better part of a metre below where it belongs, and no amount of tuning the
        /// gravity or the damping fixes that, because the residual is the solver's, not the model's.
        /// </para>
        /// <para>
        /// The taut case is also the one case whose answer is known in closed form: a rope at its
        /// full length between two points is the straight line between them. So it is applied
        /// directly, over the last tenth of the rope's extension — a band inside which a real rope
        /// is very nearly straight anyway, which is why blending across it is invisible. Below that
        /// band nothing here touches the chain and the sag is entirely the simulation's.
        /// </para>
        /// </summary>
        private void Straighten(Vector3 start, Vector3 end, float ropeLength)
        {
            float span = Vector3.Distance(start, end);
            if (ropeLength < 0.001f) return;

            float pull = Mathf.InverseLerp(0.9f, 1f, span / ropeLength);
            if (pull <= 0f) return;

            for (int i = 1; i < pos.Length - 1; i++)
            {
                Vector3 onChord = Vector3.Lerp(start, end, i / (float)(pos.Length - 1));
                pos[i] = Vector3.Lerp(pos[i], onChord, pull);
            }
        }

        /// <summary>
        /// One Jakobsen pass: pull every pair of neighbours back to the segment length.
        ///
        /// A segment with one pinned end hands the whole correction to the free node rather than
        /// splitting it, because splitting it with a node that cannot move loses half the
        /// correction — which shows up as a rope that hangs slightly long near the hand no matter
        /// how many iterations it is given.
        /// </summary>
        private void Constrain(float segment, bool forward)
        {
            int last = pos.Length - 1;

            for (int step = 0; step < last; step++)
            {
                int i = forward ? step : last - 1 - step;

                Vector3 delta = pos[i + 1] - pos[i];
                float length = delta.magnitude;
                if (length < 1e-5f) continue;

                float error = (length - segment) / length * stiffness;
                Vector3 correction = delta * error;

                bool aPinned = i == 0;
                bool bPinned = i + 1 == last;

                if (aPinned && bPinned) continue;

                if (aPinned) pos[i + 1] -= correction;
                else if (bPinned) pos[i] += correction;
                else
                {
                    pos[i] += correction * 0.5f;
                    pos[i + 1] -= correction * 0.5f;
                }
            }
        }

        private void Redraw()
        {
            if (line == null || !line.enabled || pos == null) return;

            if (line.positionCount != pos.Length) line.positionCount = pos.Length;
            line.SetPositions(pos);
        }
    }
}
