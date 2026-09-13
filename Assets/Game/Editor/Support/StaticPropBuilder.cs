// Shared machinery for turning an imported static model FBX into a usable prefab:
// import settings, fitted colliders, a cull LODGroup and the static flags.
//
// Extracted from BuildingPrefabBuilder when NomadSettlementBuilder wanted the same
// four steps on forty more buildings. The comments travelled with the code they
// explain -- each one records a failure that shipped once.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class StaticPropBuilder
    {
        /// <summary>How a matched renderer is turned into collision.</summary>
        public enum Fit
        {
            /// <summary>No collider at all.</summary>
            None = 0,
            /// <summary>Axis-aligned box over the renderer bounds.</summary>
            Box = 1,
            /// <summary>Box flattened to the top face -- walkable surface only.</summary>
            Surface = 2,
            /// <summary>Convex MeshCollider -- silhouette actually matters.</summary>
            Convex = 3,
        }

        // -------------------------------------------------------------------
        // Colliders
        // -------------------------------------------------------------------

        // Boxes are derived from Renderer.bounds (world, axis-aligned) and then
        // converted into the collider's own local space.
        //
        // Using mesh.bounds directly here is WRONG on these models and was the
        // first version's bug. Measured: the FBX root sits at scale 1, but every
        // mesh child sits at localScale 100 with a -90 deg X rotation baked in
        // by the Blender export. So the shared mesh is authored in centi-units
        // (a 0.97 m walkway tile has mesh.bounds.size 0.010) and its local Y is
        // the world's Z. Copying mesh.bounds onto a BoxCollider produced a
        // constant 25 m Z extent on every part and flattened the wrong axis.
        //
        // Going through world space and back is immune to both: whatever
        // rotation and scale the child carries, InverseTransform* undoes exactly
        // it. The cost is that a rotated part gets a world-aligned box slightly
        // larger than the part itself -- acceptable for buildings, and the
        // Convex fit exists for the cases where it is not (raked legs, booms).
        public static void AddBox(Renderer r, bool surfaceOnly)
        {
            Bounds world = r.bounds;
            Transform t = r.transform;

            // World-space box, expressed in the collider's local axes. Scale is
            // divided out via lossyScale rather than InverseTransformVector so
            // the extent stays axis-aligned instead of being re-rotated.
            Vector3 ls = t.lossyScale;
            Vector3 safeScale = new Vector3(
                Mathf.Approximately(ls.x, 0f) ? 1f : ls.x,
                Mathf.Approximately(ls.y, 0f) ? 1f : ls.y,
                Mathf.Approximately(ls.z, 0f) ? 1f : ls.z);

            Vector3 worldCenter = world.center;
            Vector3 worldSize = world.size;

            if (surfaceOnly)
            {
                // Walkway and deck meshes include their guard rails, so a
                // Mesh_Walk_* is ~2.9 m tall for a surface ~0.1 m thick.
                // Colliding the full bounds would put an invisible ceiling over
                // the walkway and box the player in at chest height. Keep the
                // top face and give it a slab thickness instead. This is done in
                // WORLD Y, which is the only axis that means "up" regardless of
                // how the child is rotated.
                const float SlabThickness = 0.25f;
                float top = world.max.y;
                worldCenter = new Vector3(worldCenter.x,
                                          top - (SlabThickness * 0.5f),
                                          worldCenter.z);
                worldSize = new Vector3(worldSize.x, SlabThickness, worldSize.z);
            }

            var box = r.gameObject.AddComponent<BoxCollider>();

            // Undo rotation for the centre, and scale for the extent.
            Vector3 localCenter = t.InverseTransformPoint(worldCenter);
            Quaternion inv = Quaternion.Inverse(t.rotation);
            Vector3 rotatedSize = inv * worldSize;
            Vector3 localSize = new Vector3(
                Mathf.Abs(rotatedSize.x) / Mathf.Abs(safeScale.x),
                Mathf.Abs(rotatedSize.y) / Mathf.Abs(safeScale.y),
                Mathf.Abs(rotatedSize.z) / Mathf.Abs(safeScale.z));

            box.center = localCenter;
            box.size = localSize;
        }

        // Convex hulls are capped at Unity's 255-face limit by the cooker, so
        // they are cheap; they exist only where a box genuinely lies about the
        // shape (raked legs, crane booms, conveyor runs).
        public static bool AddConvex(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            if (!mf.sharedMesh.isReadable) return false;

            var mc = r.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;
            return true;
        }

        // -------------------------------------------------------------------
        // LODs
        //
        // These models ship ONE mesh per part and no decimated variants -- the
        // exporters never generated any. A real LOD chain therefore cannot be
        // built here without authoring geometry, which is the art side's call
        // and not something this script should invent.
        //
        // What IS built is a single-level LODGroup acting as a cull group: one
        // LOD0 covering every renderer, culled at a few percent of screen
        // height. That still buys the thing these buildings most need -- every
        // renderer dropping out in one test instead of being frustum-culled
        // individually -- and it gives a level designer a real LODGroup to hang
        // decimated meshes off later without rebuilding the prefab.
        //
        // Unity's own mesh LOD generation (generateMeshLods) is left off: it is
        // per-import, it would triple asset size on 300 k-triangle FBXs, and it
        // decimates hard-surface panelling badly.
        // -------------------------------------------------------------------
        public static void BuildLodGroup(GameObject root, Renderer[] renderers, float cullRatio)
        {
            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();

            var lods = new LOD[1];
            lods[0] = new LOD(cullRatio, renderers);
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        // -------------------------------------------------------------------
        // Static flags, layers and navigation
        //
        // Layer: everything stays on Default (0), deliberately.
        //
        //   The obvious move is to put decks and walkways on Ground (7). It
        //   buys nothing here and risks harm. Both ground probes in this
        //   project -- Movement.IsGrounded and HoverGroundSensor -- default
        //   their masks to ~0, i.e. every layer, so a deck is already walkable
        //   on Default. And the vehicle-climbs-the-building failure is not a
        //   layer problem: it was ground probes hitting a rider's dynamic
        //   Rigidbody, fixed in the probes themselves by rejecting non-kinematic
        //   attachedRigidbody hits. Moving buildings to Ground would not have
        //   prevented it and would silently change what PerceptionModule treats
        //   as sight-blocking (its fallback mask is Default|Ground|Interior, so
        //   both are occluders either way).
        //
        // Navigation: marked navmesh-static rather than given NavMeshObstacles.
        //   These are immovable scene geometry, so baking them is strictly
        //   cheaper than carving every frame. NavMeshObstacle is for things that
        //   move or appear at runtime, which none of these do.
        //
        // Lightmapping: ContributeGI is deliberately NOT set. generateSecondaryUV
        //   is 0 on these imports, so there are no lightmap UVs; flagging
        //   ContributeGI without them produces a bake with overlapping charts
        //   and black splotches. Turning UV generation on is a slow, one-way
        //   import change on large meshes, so it is left to whoever decides
        //   these buildings should be lightmapped.
        //
        // Netcode: no NetworkObject, deliberately.
        //   These are static scene-placed geometry. They never move, never
        //   spawn at runtime and have no replicated state -- every client builds
        //   an identical copy from the same chunk scene, so there is nothing to
        //   synchronise. Adding NetworkObject would cost a spawn message and a
        //   registry entry per building for zero behaviour.
        //
        //   Note the standing trap if that ever changes: an unregistered network
        //   prefab fails ONLY on clients, so a solo playtest as host will never
        //   reveal it. Anything given a NetworkObject here must also be
        //   registered in Assets/DefaultNetworkPrefabs.asset.
        //
        //   SceneTracked is likewise not added -- that is for entities that move
        //   between chunks (vehicles, NPCs). A building belongs to exactly one
        //   chunk for its whole life.
        // -------------------------------------------------------------------
        public static void MarkStatic(GameObject root)
        {
            var flags = StaticEditorFlags.BatchingStatic
                      | StaticEditorFlags.OccluderStatic
                      | StaticEditorFlags.OccludeeStatic
                      | StaticEditorFlags.NavigationStatic
                      | StaticEditorFlags.ReflectionProbeStatic;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        // -------------------------------------------------------------------
        // Model import
        // -------------------------------------------------------------------

        public static void ConfigureImporter(string fbx)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer == null) return;

            bool dirty = false;

            // Read/write is required for the convex MeshColliders above: a
            // non-readable mesh cannot be cooked into one, and the failure is a
            // silent null sharedMesh rather than an error.
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }

            // These arrive at scale 1 and must stay there -- every collider
            // extent measured off them is a true metre.
            if (!importer.useFileScale) { importer.useFileScale = true; dirty = true; }
            if (!Mathf.Approximately(importer.globalScale, 1f))
            {
                importer.globalScale = 1f;
                dirty = true;
            }

            // Static set dressing: no rig, no clips. Importing animation yields
            // nothing but an Animator the caller would have to strip. Any bones
            // must survive as transforms, though, so the hierarchy is kept.
            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
                dirty = true;
            }

            // generateSecondaryUV is left alone on purpose -- see MarkStatic.

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = new List<string>(path.Split('/'));
            string built = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
