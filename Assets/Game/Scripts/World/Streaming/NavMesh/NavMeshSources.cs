using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// Turns scene collision into NavMesh build sources. Shared by every bake in the project — the
    /// author-time world bake (<c>WorldNavMeshBaker</c>) and a settlement's throwaway bake for
    /// placing its characters (<see cref="SettlementWalkableArea"/>) — so what counts as walkable
    /// ground is decided in exactly one place. Runtime assembly so both can reach it.
    /// </summary>
    public static class NavMeshSources
    {
        /// <summary>
        /// Whether a collider is part of the ground the world walks on, as opposed to something
        /// that walks on it or moves across it.
        ///
        /// <para>
        /// Two things are excluded besides triggers and layers. A <b>non-kinematic</b> body is
        /// scenery that moves; baking it fixes it in place forever. And anything under a
        /// <see cref="NavMeshAgent"/> is a walker: every NavMesh creature here is a <b>kinematic</b>
        /// body with a solid collider (AgentSystem.md), so the kinematic rule alone let a patrol
        /// robot hand-placed in a chunk scene bake in as an obstacle — six robot-shaped holes in the
        /// first Clanker settlement, each exactly where a robot would spawn and find no mesh under
        /// its feet. Measured 2026-09-07: no mesh within 0.35 m of any of the four robots probed,
        /// mesh everywhere around them.
        /// </para>
        /// <para>
        /// A collider sharing its object with a <b>carving</b> <see cref="NavMeshObstacle"/> is a
        /// door or gate leaf: the obstacle cuts it out at runtime while it is shut. Baking it as well
        /// would seal the doorway for good, so opening a pen gate would never let its stock out.
        /// A bake that wants the world as it is at the start of play -- every door shut -- passes
        /// <paramref name="doorsShut"/> to keep those leaves.
        /// </para>
        /// </summary>
        public static bool IsBakeable(Collider col, LayerMask mask, bool doorsShut = false)
        {
            if (col == null || col.isTrigger) return false;
            if (!InMask(mask, col.gameObject.layer)) return false;

            var body = col.attachedRigidbody;
            if (body != null && !body.isKinematic) return false;

            if (!doorsShut && col.TryGetComponent(out NavMeshObstacle obstacle) && obstacle.carving) return false;

            return col.GetComponentInParent<NavMeshAgent>(true) == null;
        }

        public static bool InMask(LayerMask mask, int layer) => (mask.value & (1 << layer)) != 0;

        /// <summary>A terrain as a source, at its own position. Terrains never rotate or scale.</summary>
        public static NavMeshBuildSource FromTerrain(Terrain terrain) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Terrain,
            sourceObject = terrain.terrainData,
            transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
            area = 0,
        };

        /// <summary>
        /// Collider to NavMesh source. Lifted from the runtime <c>NavMeshSourceCache</c> the world
        /// bake replaced — the mapping was correct, it was the per-frame rebuilding around it that was not.
        /// </summary>
        public static bool TryColliderToSource(Collider col, out NavMeshBuildSource src)
        {
            src = default;
            var t = col.transform;

            switch (col)
            {
                case MeshCollider mc:
                    if (mc.sharedMesh == null || !mc.sharedMesh.isReadable) return false;
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mc.sharedMesh,
                        transform = t.localToWorldMatrix,
                        area = 0,
                    };
                    return true;

                case BoxCollider bc:
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(t.TransformPoint(bc.center), t.rotation,
                                                  Vector3.Scale(t.lossyScale, bc.size)),
                        size = Vector3.one,
                        area = 0,
                    };
                    return true;

                case SphereCollider sc:
                {
                    float s = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Sphere,
                        transform = Matrix4x4.TRS(t.TransformPoint(sc.center), t.rotation, Vector3.one),
                        size = Vector3.one * (sc.radius * 2f * s),
                        area = 0,
                    };
                    return true;
                }

                case CapsuleCollider cc:
                {
                    float s = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Capsule,
                        transform = Matrix4x4.TRS(t.TransformPoint(cc.center), t.rotation, Vector3.one),
                        size = new Vector3(cc.radius * 2f * s, cc.height * s, cc.radius * 2f * s),
                        area = 0,
                    };
                    return true;
                }
            }

            return false;
        }
    }
}
