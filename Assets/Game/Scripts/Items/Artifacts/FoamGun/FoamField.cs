// Every live foam blob in the world, and the shader field the blobs weld through.
//
// THE INTEGRATION CONTRACT. FoamSurface.shader shades each fragment against the smooth union of
// its NEIGHBOURS, read from two shader globals that gameplay code has to upload:
//
//     float4 _FoamBlobs[192]  xyz = world centre, w = world radius
//     int    _FoamBlobCount
//
// With the count at 0 nothing errors, nothing goes black, and every blob simply falls back to its
// own mesh normal — which is a correctly shaded sphere. That is precisely the trap: a pile of
// intersecting spheres is the failure the design named, and it degrades in silence. So the upload
// is here, on the registry itself, rather than on some component somebody has to remember to place.
//
// WHY IT UPLOADS PER CAMERA. The two values are globals, and which 192 of them are worth sending
// depends on where you are looking from — a split screen, a schematic stage and the ship's terminal
// all render their own camera in one frame. RenderPipelineManager.beginCameraRendering is the same
// hook PlayerLook already uses to answer a per-camera question, and it hands us the camera rather
// than making us guess at one (Camera.main is never the player's camera in this project).
//
// PAST 192. The nearest 192 to the camera weld; the rest render, and each of those falls back to its
// own mesh normal. A distant mass therefore reads as separate balls while the one at your feet is
// one substance, which is the right way round for a bounded per-fragment cost (GDC-L1-PERF-0004 —
// the array is the budget, and the shader pays for every entry on every foam pixel).
using System;
using System.Collections.Generic;
using SpaceGame.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// The set of foam blobs standing in the world on this machine: what the shader welds them
    /// through, and what the gun asks when it needs to know whose foam is oldest.
    /// </summary>
    public static class FoamField
    {
        /// <summary>
        /// Must match <c>FOAM_MAX_BLOBS</c> in <c>FoamSurface.hlsl</c>. The uploaded array is this
        /// long every time regardless of how many are in use: Unity fixes a global array's size at
        /// the first upload and silently truncates a longer one later.
        /// </summary>
        public const int MaxBlobs = 192;

        private static readonly List<FoamBlob> Live = new List<FoamBlob>();
        private static readonly List<FoamBlob> Selected = new List<FoamBlob>(MaxBlobs);
        private static readonly Vector4[] Packed = new Vector4[MaxBlobs];

        private static readonly int BlobsId = Shader.PropertyToID("_FoamBlobs");
        private static readonly int CountId = Shader.PropertyToID("_FoamBlobCount");

        /// <summary>Where the sort in progress is measuring from.</summary>
        private static Vector3 sortOrigin;

        /// <summary>
        /// Distance from the camera to the blob's SURFACE, so a big blob the camera is standing
        /// inside cannot lose its slot to a small one parked slightly nearer.
        /// </summary>
        private static readonly Comparison<FoamBlob> ByDistance = (a, b) =>
            SortKey(a).CompareTo(SortKey(b));

        private static bool listening;

        /// <summary>How many blobs the last upload sent. Zero means the shader is on its fallback.</summary>
        public static int UploadedCount { get; private set; }

        // Statics survive a world unload, a return to the menu and — with Enter Play Mode Options on,
        // which they are here — play mode itself. A list still holding last session's destroyed
        // blobs uploads a field of nulls on the first frame of the next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (listening)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                listening = false;
            }

            Live.Clear();
            Selected.Clear();
            UploadedCount = 0;
        }

        /// <summary>
        /// A blob has appeared on this machine. Called from the blob's own OnEnable, so it happens
        /// on every machine rather than only where the spawn was decided — the shader needs the
        /// field everywhere, and a peer whose foam never registered draws the pile of balls.
        /// </summary>
        public static void Register(FoamBlob blob)
        {
            if (blob == null || Live.Contains(blob)) return;

            Live.Add(blob);

            if (listening) return;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            listening = true;
        }

        /// <summary>A blob is gone. Mirrors <see cref="Register"/>.</summary>
        public static void Unregister(FoamBlob blob)
        {
            if (!Live.Remove(blob) || Live.Count > 0) return;

            // The count is zeroed BEFORE the hook goes, not after: unhooking on a non-zero count
            // would leave the shader reading a stale array of blobs that no longer exist, which is
            // a fillet welded onto empty air rather than nothing at all.
            UploadedCount = 0;
            Shader.SetGlobalInt(CountId, 0);

            if (!listening) return;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            listening = false;
        }

        /// <summary>
        /// How many blobs <paramref name="sprayer"/> has standing. Server-side only — a blob's
        /// sprayer is not replicated, because the budget is the only thing that ever asks.
        /// </summary>
        public static int CountFor(GameObject sprayer)
        {
            if (sprayer == null) return 0;

            int count = 0;
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null && Live[i].Sprayer == sprayer) count++;

            return count;
        }

        /// <summary>
        /// The first blob <paramref name="sprayer"/> laid that is still standing, or null. Spawn
        /// order, because <see cref="Live"/> is appended to — the oldest is the one whose ramp the
        /// player has most likely finished with.
        /// </summary>
        public static FoamBlob OldestOf(GameObject sprayer)
        {
            if (sprayer == null) return null;

            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null && Live[i].Sprayer == sprayer) return Live[i];

            return null;
        }

        /// <summary>
        /// Where one chord of a spray arc first meets standing foam — a
        /// <see cref="SprayArc.ChordTest"/>, handed to the trace so a dab lands on the mass
        /// instead of through it.
        ///
        /// <para>
        /// It is here rather than on the gun because the gun does not know what foam exists; this
        /// registry is the only thing that does. It answers off each lump's COMMITTED volume, not
        /// its collider — see <see cref="FoamBlob.TryHitCommitted"/> for why the two differ for
        /// the first seconds of a lump's life, which is exactly the window a held trigger fills.
        /// </para>
        /// <para>
        /// Asked on the OWNER's machine only, fifteen times a second, as part of describing a dab.
        /// Nothing about it crosses the wire: the owner has always been the machine that decides
        /// where its own foam lands, and the server still spawns from the point it is sent.
        /// </para>
        /// </summary>
        public static bool FirstAlong(Vector3 from, Vector3 to, out Vector3 point,
                                      out Vector3 normal, out float distance)
        {
            point = default;
            normal = Vector3.up;
            distance = float.PositiveInfinity;

            float length = Vector3.Distance(from, to);
            if (length < 1e-5f) return false;

            bool found = false;

            for (int i = 0; i < Live.Count; i++)
            {
                FoamBlob blob = Live[i];

                // Nulls are swept by Push rather than here: this runs inside an arc walk, and a
                // list edited mid-walk is a surprise the caller cannot see. Skipping is enough.
                if (blob == null || !blob.isActiveAndEnabled) continue;

                // A chord cannot reach a lump further off than its own length plus that lump's
                // reach. Twenty chords a tick against a full field is 2560 ellipsoid solves, and
                // this rejects almost all of them on a subtraction and a compare.
                float reach = length + blob.CommittedRadius;
                if ((blob.Centre - from).sqrMagnitude > reach * reach) continue;

                if (!blob.TryHitCommitted(from, to, out Vector3 hitPoint, out Vector3 hitNormal,
                                          out float hitDistance)) continue;

                if (hitDistance >= distance) continue;

                point = hitPoint;
                normal = hitNormal;
                distance = hitDistance;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Push <paramref name="point"/> out of every committed foam volume it sits inside, and say
        /// where it ends up. One relaxation pass for <see cref="FoamSettle"/>.
        ///
        /// <para>
        /// It is a Gauss-Seidel sweep rather than a simultaneous one: each lump is pushed out of in
        /// turn, against the position the lump before it left behind. That converges in far fewer
        /// passes than averaging the pushes, and a settle runs only a handful of passes.
        /// </para>
        /// <para>
        /// Like <see cref="FirstAlong"/> it asks each lump for the volume it has COMMITTED to fill
        /// rather than the one it occupies right now, and for the same reason: at the dab rate every
        /// lump a settling dab has to climb over is still a pebble, so relaxing against the current
        /// colliders would let a whole second of spray settle through the mound and pool on the
        /// ground under it.
        /// </para>
        /// </summary>
        /// <param name="clearance">
        /// How far outside a lump's own extents the point must end up — the settling lump's radius,
        /// less however much the two are allowed to interpenetrate.
        /// </param>
        /// <param name="onFoam">
        /// Was the point inside any lump at all? It is asked separately from "did it move", and the
        /// difference is what decides whether foam piles or pancakes: a lump that has come to rest
        /// ON the shoulder of its neighbour is pushed by a hair and then not at all, so a caller
        /// reading movement concludes it is in open air and drops it to the floor. Every lump laid
        /// on the mound then ends up at ground level and the mound never gets a second layer.
        /// </param>
        public static Vector3 PushOutOfCommitted(Vector3 point, float clearance, out bool onFoam)
        {
            onFoam = false;

            for (int i = 0; i < Live.Count; i++)
            {
                FoamBlob blob = Live[i];

                // Nulls are swept by Push rather than here, as in FirstAlong: this runs inside a
                // solve, and a list edited mid-solve is a surprise the caller cannot see.
                if (blob == null || !blob.isActiveAndEnabled) continue;

                float reach = blob.CommittedRadius + clearance;
                if ((blob.Centre - point).sqrMagnitude > reach * reach) continue;

                if (!blob.PushOutOfCommitted(point, clearance, out Vector3 pushed)) continue;

                point = pushed;
                onFoam = true;
            }

            return point;
        }

        private static float SortKey(FoamBlob blob) =>
            blob != null
                ? Vector3.Distance(blob.Centre, sortOrigin) - blob.Radius
                : float.PositiveInfinity;

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;

            // A thumbnail and a reflection probe see the same globals as the game view and would
            // only re-sort the same field against a camera nobody is looking through.
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;

            Push(camera.transform.position);
        }

        /// <summary>
        /// Upload the blobs nearest <paramref name="viewpoint"/>. Exposed so the contract can be
        /// exercised without a render loop.
        /// </summary>
        public static void Push(Vector3 viewpoint)
        {
            sortOrigin = viewpoint;

            Selected.Clear();
            for (int i = 0; i < Live.Count; i++)
            {
                FoamBlob blob = Live[i];

                // A blob can be destroyed without OnDisable running — a scene unload does exactly
                // that — so the list is swept here rather than trusted.
                if (blob == null)
                {
                    Live.RemoveAt(i--);
                    continue;
                }

                if (!blob.isActiveAndEnabled) continue;

                Selected.Add(blob);
            }

            if (Selected.Count == 0)
            {
                UploadedCount = 0;
                Shader.SetGlobalInt(CountId, 0);

                // The sweep above is the other way the list can empty — a chunk or a scene unload
                // destroys blobs without OnDisable — so the hook is let go here too. Without it a
                // world with no foam in it still re-sorts an empty list once per camera per frame,
                // for the rest of the session.
                if (Live.Count == 0 && listening)
                {
                    RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                    listening = false;
                }

                return;
            }

            Selected.Sort(ByDistance);

            UploadedCount = Mathf.Min(Selected.Count, MaxBlobs);
            for (int i = 0; i < UploadedCount; i++)
            {
                Vector3 centre = Selected[i].Centre;

                // FieldRadius, not Radius: a lump is a squashed ellipsoid now, and the field
                // unions SPHERES. The mean half-extent is the sphere that best fits it; the
                // longest would weld a fillet onto empty air on two axes out of three.
                Packed[i] = new Vector4(centre.x, centre.y, centre.z, Selected[i].FieldRadius);
            }

            // The unused tail keeps whatever it held last frame; the shader never reads past
            // _FoamBlobCount, and clearing it would be writes a frame for nothing.
            Shader.SetGlobalVectorArray(BlobsId, Packed);
            Shader.SetGlobalInt(CountId, UploadedCount);
        }
    }
}
