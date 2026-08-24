// The shape of an aperture.
//
// A portal used to be an ellipse and nothing else: one Vector2, and every question about the
// opening — is this point in the hole, can this creature get through it, how big a box does the
// sweep need — answered from it. A sprayed portal is not an ellipse, so the questions moved here
// and the answers now come in two flavours.
//
// WITH NO DABS THIS IS STILL THAT ELLIPSE, exactly. That is not a courtesy: an aperture placed in
// a scene by hand, and every aperture in every save file written before spraying existed, is an
// ellipse and has to keep behaving like one down to the last decimal. The dab path is additive,
// and PortalLifecycleTests and PortalTraversalTests passing unchanged is what proves it.
//
// WITH DABS the shape is the smooth union of up to 24 circles in the portal's local plane — the
// paint, literally. The shader evaluates the same field with the same constants folded in the same
// order (see PortalStencil.hlsl), and that is the whole reason this is a distance field rather
// than, say, a polygon: there is exactly one definition of where the hole is, and the picture and
// the physics both read it. A lobe you can see is a lobe you can walk through.
//
// Pure C#, no MonoBehaviour and no Unity lifecycle, so the maths can be tested without a scene.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Portals
{
    /// <summary>One blob of paint, in the portal's local plane. Metres.</summary>
    public struct PortalDab
    {
        public Vector2 Centre;
        public float Radius;

        public PortalDab(Vector2 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }
    }

    public sealed class PortalStencil
    {
        /// <summary>
        /// How many dabs a shape may hold.
        ///
        /// A full reservoir buys about twenty-two, so this is one tank plus slack — and it is also
        /// the length of the array the shader declares, which is the real constraint. Past it a new
        /// dab is merged into whichever one is nearest rather than dropped: a player topping a
        /// portal up after the tank refills should see the paint land, not watch it vanish.
        /// </summary>
        public const int MaxDabs = 24;

        /// <summary>
        /// The smooth-union radius, in metres.
        ///
        /// Zero here would make a stroke read as a string of overlapping coins. This is what welds
        /// them into one run of liquid. It MUST match PORTAL_SMOOTH in PortalStencil.hlsl — the
        /// shader and the physics disagreeing about where the edge is, is the one failure this
        /// whole class is shaped to prevent.
        /// </summary>
        public const float Smoothing = 0.35f;

        /// <summary>Most dabs one hold tick may be interpolated into. A flick is not free area.</summary>
        public const int MaxStrokeSteps = 8;

        /// <summary>
        /// How close a new blob has to be to an existing one to POOL into it rather than sit beside
        /// it, as a fraction of the blob radius.
        ///
        /// This is what makes holding the stream still widen the hole. Without it a second blob
        /// landing on the first is a second identical circle in the same place — the shape does not
        /// change at all, the player sees paint pouring onto a hole that refuses to grow, and the
        /// dab cap is eaten by blobs that contributed nothing.
        /// </summary>
        private const float PoolDistance = 0.45f;

        /// <summary>
        /// How far a pooling blob may grow, as a multiple of the radius it was sprayed at.
        ///
        /// Unbounded pooling would let somebody stand still and open a hole the size of the wall,
        /// which is the failure the reservoir is supposed to be the only guard against — but paint
        /// really does stop spreading, and a cap here is what makes SWEEPING rather than waiting
        /// the way to get a big portal.
        /// </summary>
        private const float MaxPoolFactor = 2.6f;

        /// <summary>How far apart consecutive dabs are laid, as a fraction of their radius.</summary>
        private const float StrokeSpacing = 0.6f;

        /// <summary>Resolution the inscribed radius is sampled at. 16x16 is 256 probes.</summary>
        private const int InscribedSamples = 16;

        private readonly List<PortalDab> dabs = new List<PortalDab>(MaxDabs);

        private Vector2 ellipse = new Vector2(3.45f, 6.15f);
        private float strokeRadius = 0.62f;
        private Rect bounds;
        private float inscribed;
        private Vector2 centroid;
        private bool derived;

        /// <summary>True while this shape is the ellipse inscribed in <see cref="EllipseSize"/>.</summary>
        public bool IsEllipse => dabs.Count == 0;

        public int Count => dabs.Count;

        public IReadOnlyList<PortalDab> Dabs => dabs;

        /// <summary>The ellipse's width and height. Meaningless once there are dabs.</summary>
        public Vector2 EllipseSize => ellipse;

        /// <summary>The shape's axis-aligned box in the portal's local plane.</summary>
        public Rect Bounds
        {
            get { Derive(); return bounds; }
        }

        /// <summary>
        /// The radius of the largest circle that fits inside the shape.
        ///
        /// What decides whether something can get through. A long thin snake of paint is a big
        /// shape with a small hole, and measuring its BOUNDS instead would let a walker through a
        /// gap the width of a hand.
        /// </summary>
        public float InscribedRadius
        {
            get { Derive(); return inscribed; }
        }

        /// <summary>The middle of the paint — where the surface's vortex is centred.</summary>
        public Vector2 Centroid
        {
            get { Derive(); return centroid; }
        }

        /// <summary>
        /// The metric the surface's rim, throat and swirl are drawn against, in metres.
        ///
        /// This exists because of a bug that made the whole aperture appear to rescale as it was
        /// sprayed. The shader used to normalise against <see cref="InscribedRadius"/>, which grows
        /// with the paint — so every blob added re-sized the rim band, deepened the throat and
        /// stretched the vortex over paint that had not changed. The picture breathed.
        ///
        /// The answer is a scale that does NOT follow the growth: the radius the stroke is being
        /// sprayed at, fixed at its first blob. New paint then extends the opening and leaves the
        /// look of the old paint exactly where it was. An unsprayed ellipse has no stroke, and uses
        /// its inscribed radius, which is constant for it anyway.
        /// </summary>
        public float ReferenceScale =>
            dabs.Count == 0 ? InscribedRadius : Mathf.Max(strokeRadius, 1e-3f);

        /// <summary>The ellipse's semi-axes, for the shader's own copy of the field.</summary>
        public Vector2 SemiAxes => ellipse * 0.5f;

        /// <summary>Make this the ellipse inscribed in <paramref name="size"/>, discarding any dabs.</summary>
        public void SetEllipse(Vector2 size)
        {
            ellipse = new Vector2(Mathf.Max(size.x, 1e-3f), Mathf.Max(size.y, 1e-3f));
            dabs.Clear();
            derived = false;
        }

        /// <summary>Throw the paint away, ready for a fresh stroke.</summary>
        public void Clear()
        {
            dabs.Clear();
            ellipse = Vector2.zero;
            derived = false;
        }

        /// <summary>
        /// Add one blob of paint, pooling it into whatever is already there.
        ///
        /// The opening grows the way paint actually covers a wall: land on bare surface and there
        /// is a new blob, land on paint you have already laid and that blob WIDENS. Growth is by
        /// AREA — two blobs' worth of paint in one place makes a circle of twice the area, not
        /// twice the radius — which is both what a fluid does and what stops a player who parks the
        /// stream on one spot from opening a hole the size of the room.
        ///
        /// The pooled blob's centre drifts towards the new paint as well, so a stream held on one
        /// place and then eased sideways draws the opening after it instead of leaving a bulge.
        ///
        /// Past <see cref="MaxDabs"/> the nearest blob absorbs everything, which keeps the outline
        /// moving where the player painted even once the array is full.
        /// </summary>
        public void AddDab(Vector2 centre, float radius)
        {
            radius = Mathf.Max(radius, 1e-3f);
            derived = false;

            // The radius the stroke is being sprayed at, remembered from its first blob. Read by
            // ReferenceScale — see there for why it must not follow the pooling.
            if (dabs.Count == 0) strokeRadius = radius;

            int nearest = dabs.Count > 0 ? Nearest(centre) : -1;
            bool full = dabs.Count >= MaxDabs;

            if (nearest >= 0)
            {
                PortalDab existing = dabs[nearest];
                float gap = Vector2.Distance(existing.Centre, centre);

                if (full || gap <= existing.Radius * PoolDistance)
                {
                    // Area-additive: r' = sqrt(r² + added²), capped. Reaching the cap is not a
                    // failure — it is the point at which paint stops spreading and sweeping is the
                    // only way left to make the hole bigger.
                    float pooled = Mathf.Sqrt(existing.Radius * existing.Radius + radius * radius);
                    float limit = strokeRadius * MaxPoolFactor;

                    // Once it is full the array cannot grow, so the blob has to REACH the new paint
                    // rather than merely thicken — otherwise the last stretch of a long sweep stops
                    // showing up at all.
                    //
                    // And in that case the pool cap is DELIBERATELY ignored. The cap is there to
                    // stop standing still from opening a hole the size of the wall, which is a
                    // tuning choice; painted area appearing in the aperture at all is not. When the
                    // two conflict the paint wins, or the promise that every patch you covered is
                    // part of the portal quietly stops being true at the far end of a long sweep.
                    float grown = full
                        ? Mathf.Max(pooled, gap + radius)
                        : Mathf.Min(pooled, Mathf.Max(limit, existing.Radius));

                    // Drift towards the new paint, by the share of the blob this one just added.
                    float share = Mathf.Clamp01(radius * radius /
                                                Mathf.Max(grown * grown, 1e-4f)) * PoolDistance;

                    dabs[nearest] = new PortalDab(Vector2.Lerp(existing.Centre, centre, share),
                                                  grown);
                    return;
                }
            }

            dabs.Add(new PortalDab(centre, radius));
        }

        /// <summary>Is <paramref name="local"/> inside the opening? <paramref name="margin"/> widens it.</summary>
        public bool Contains(Vector2 local, float margin = 0f) => Field(local) <= margin;

        /// <summary>
        /// Signed distance to the edge in metres — negative inside, positive outside.
        ///
        /// The ellipse branch is an approximation, an exact ellipse distance needing a quartic, but
        /// it is exact ON the boundary, which is the only place anything reads it as a yes or a no.
        /// The dab branch is the polynomial smooth minimum folded in array order, so the shader —
        /// which folds the same array the same way — agrees with it everywhere.
        /// </summary>
        public float Field(Vector2 local)
        {
            if (dabs.Count == 0)
            {
                float a = Mathf.Max(ellipse.x * 0.5f, 1e-4f);
                float b = Mathf.Max(ellipse.y * 0.5f, 1e-4f);
                float u = local.x / a;
                float v = local.y / b;

                return (Mathf.Sqrt(u * u + v * v) - 1f) * Mathf.Min(a, b);
            }

            float field = (local - dabs[0].Centre).magnitude - dabs[0].Radius;

            for (int i = 1; i < dabs.Count; i++)
            {
                float next = (local - dabs[i].Centre).magnitude - dabs[i].Radius;
                field = SmoothMin(field, next, Smoothing);
            }

            return field;
        }

        /// <summary>
        /// Can something of this half-width and half-height get through?
        ///
        /// Ellipse mode keeps the original test verbatim — the narrower half of the traveller
        /// paired with the narrower semi-axis — because changing it would silently re-tune every
        /// aperture already in the game. Dab mode measures against the inscribed circle, which is
        /// the honest answer for a shape with no axes to speak of.
        /// </summary>
        public bool Fits(Vector2 girth)
        {
            if (dabs.Count == 0)
            {
                float narrow = Mathf.Max(Mathf.Min(ellipse.x, ellipse.y) * 0.5f, 1e-4f);
                float wide = Mathf.Max(Mathf.Max(ellipse.x, ellipse.y) * 0.5f, 1e-4f);

                float eu = girth.x / narrow;
                float ev = girth.y / wide;
                return eu * eu + ev * ev <= 1f;
            }

            float radius = Mathf.Max(InscribedRadius, 1e-4f);

            float u = girth.x / radius;
            float v = girth.y / radius;
            return u * u + v * v <= 1f;
        }

        /// <summary>
        /// The nearest point to <paramref name="local"/> that is inside the shape by at least
        /// <paramref name="clearance"/> metres.
        ///
        /// This is what stops a traveller entering through a lobe the far aperture does not have
        /// and arriving inside the wall. Two ellipses could do without it — they have the same
        /// outline, only scaled — but two sprayed blobs never do.
        ///
        /// A point that is already inside is returned UNTOUCHED, however close to the edge it is,
        /// and the clearance only decides how far in an outside point is pulled. Requiring the
        /// clearance of a point that was already in the opening was the first version, and it
        /// quietly nudged every ordinary rim-skimming traversal in the game sideways by a tenth of
        /// a metre — a behaviour change to a system that was working, bought for nothing, since a
        /// point inside the entry ellipse is inside an identical exit ellipse by construction.
        /// </summary>
        public Vector2 ClampInside(Vector2 local, float clearance)
        {
            if (Field(local) <= 0f) return local;

            if (dabs.Count == 0)
            {
                float a = Mathf.Max(ellipse.x * 0.5f - clearance, 1e-3f);
                float b = Mathf.Max(ellipse.y * 0.5f - clearance, 1e-3f);
                float u = local.x / a;
                float v = local.y / b;
                float radius = Mathf.Sqrt(u * u + v * v);

                return radius <= 1f ? local : local / radius;
            }

            PortalDab dab = dabs[Nearest(local)];
            float reach = Mathf.Max(dab.Radius - clearance, 0f);
            Vector2 offset = local - dab.Centre;

            if (offset.sqrMagnitude < 1e-6f) return dab.Centre;

            return dab.Centre + offset.normalized * Mathf.Min(offset.magnitude, reach);
        }

        /// <summary>
        /// How many dabs a stroke of <paramref name="distance"/> metres is laid as.
        ///
        /// At fifteen ticks a second a fast flick moves metres between them, and one dab per tick
        /// would paint a dotted line. Interpolating is pure arithmetic on two points every machine
        /// already has, so it needs nothing on the wire — and each interpolated dab costs paint
        /// like any other, or a flick would be free area.
        /// </summary>
        public static int StrokeSteps(float distance, float radius)
        {
            float spacing = Mathf.Max(radius * StrokeSpacing, 1e-3f);
            return Mathf.Clamp(1 + Mathf.FloorToInt(distance / spacing), 1, MaxStrokeSteps);
        }

        /// <summary>
        /// Fill <paramref name="buffer"/> with the dabs as the shader wants them — xy centre
        /// relative to <see cref="Bounds"/>' centre, z radius — and answer how many there are.
        /// </summary>
        public int WriteShaderData(Vector4[] buffer)
        {
            Derive();

            int count = Mathf.Min(dabs.Count, buffer.Length);
            Vector2 origin = bounds.center;

            for (int i = 0; i < count; i++)
            {
                PortalDab dab = dabs[i];
                buffer[i] = new Vector4(dab.Centre.x - origin.x, dab.Centre.y - origin.y,
                                        dab.Radius, 0f);
            }

            for (int i = count; i < buffer.Length; i++) buffer[i] = Vector4.zero;

            return count;
        }

        /// <summary>Index of the dab whose centre is closest to <paramref name="point"/>.</summary>
        private int Nearest(Vector2 point)
        {
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < dabs.Count; i++)
            {
                float distance = (dabs[i].Centre - point).sqrMagnitude;
                if (distance >= best) continue;

                best = distance;
                nearest = i;
            }

            return nearest;
        }

        /// <summary>The polynomial smooth minimum. Mirrored exactly in PortalStencil.hlsl.</summary>
        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>
        /// Recompute the bounds, the centroid and the inscribed radius.
        ///
        /// Lazily and cached, because a spray changes the shape fifteen times a second and every
        /// one of those would otherwise re-sample a 16x16 grid from three different callers in the
        /// same frame.
        /// </summary>
        private void Derive()
        {
            if (derived) return;
            derived = true;

            if (dabs.Count == 0)
            {
                bounds = new Rect(-ellipse.x * 0.5f, -ellipse.y * 0.5f, ellipse.x, ellipse.y);
                centroid = Vector2.zero;
                inscribed = Mathf.Min(ellipse.x, ellipse.y) * 0.5f;
                return;
            }

            // The smooth union bulges slightly outside the discs themselves, so the box is padded.
            // A box that clipped the shape would clip the swept volume and the quads with it.
            float pad = Smoothing * 0.5f;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            Vector2 sum = Vector2.zero;
            float deepest = 0f;

            for (int i = 0; i < dabs.Count; i++)
            {
                PortalDab dab = dabs[i];

                minX = Mathf.Min(minX, dab.Centre.x - dab.Radius - pad);
                minY = Mathf.Min(minY, dab.Centre.y - dab.Radius - pad);
                maxX = Mathf.Max(maxX, dab.Centre.x + dab.Radius + pad);
                maxY = Mathf.Max(maxY, dab.Centre.y + dab.Radius + pad);

                sum += dab.Centre;

                // Seeded with the dab radii, so a shape smaller than one grid cell still reports
                // something sensible rather than zero.
                deepest = Mathf.Max(deepest, dab.Radius);
            }

            bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            centroid = sum / dabs.Count;

            for (int y = 0; y < InscribedSamples; y++)
            {
                float v = (y + 0.5f) / InscribedSamples;

                for (int x = 0; x < InscribedSamples; x++)
                {
                    float u = (x + 0.5f) / InscribedSamples;
                    var point = new Vector2(bounds.xMin + u * bounds.width,
                                            bounds.yMin + v * bounds.height);

                    deepest = Mathf.Max(deepest, -Field(point));
                }
            }

            inscribed = deepest;
        }
    }
}
