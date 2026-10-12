using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The reduced meshes a character's far levels of detail wear: one asset per source mesh and
    /// level, in <see cref="CharacterLodProfile.ReducedMeshFolder"/> beside the source model.
    ///
    /// <para>
    /// <c>MeshLodUtility</c> simplifies a copy of the source into Mesh LOD levels that share ONE
    /// vertex buffer, and a skinned renderer deforms that whole buffer whatever level it draws. So the
    /// level's triangles are lifted out into a mesh of their own holding only the vertices they use,
    /// with the source's bone weights, bind poses and blend shapes carried across. The simplifier does
    /// not weigh skin weights, so a merged vertex keeps one of its originals' weights.
    /// </para>
    ///
    /// <para>
    /// Each asset is stamped with its source model's import hash, so it is regenerated after a
    /// re-export and reused otherwise. An existing asset is overwritten in place to keep its GUID —
    /// the running Editor may keep drawing the old data until the mesh is unloaded (see
    /// StylizedEyes' Gotchas).
    /// </para>
    /// </summary>
    public static class CharacterLodMeshes
    {
        private const int UvChannels = 8;

        /// <summary>
        /// <paramref name="source"/> reduced to Mesh LOD <paramref name="level"/>, or the source
        /// itself when it is too small to reduce or the simplifier cannot reach that level.
        /// </summary>
        public static Mesh Reduced(Mesh source, int level, CharacterLodProfile profile)
        {
            if (Triangles(source) < profile.MinTrianglesToReduce) return source;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"[CharacterLodMeshes] '{source.name}' is not an asset; worn unreduced.");
                return source;
            }

            string path = PathFor(sourcePath, source, level, profile);
            string stamp = $"{AssetDatabase.GetAssetDependencyHash(sourcePath)}|{source.name}|{level}";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && AssetImporter.GetAtPath(path).userData == stamp) return existing;

            var simplified = Object.Instantiate(source);
            try
            {
                MeshLodUtility.GenerateMeshLods(simplified, level);
                if (simplified.lodCount <= level)
                {
                    Debug.LogWarning($"[CharacterLodMeshes] '{source.name}' simplifies to only " +
                                     $"{simplified.lodCount - 1} level(s), not {level}; worn unreduced.");
                    return source;
                }

                var reduced = Extract(simplified, level, Path.GetFileNameWithoutExtension(path));
                return Store(reduced, existing, path, stamp);
            }
            finally
            {
                Object.DestroyImmediate(simplified);
            }
        }

        private static string PathFor(string sourcePath, Mesh source, int level, CharacterLodProfile profile)
        {
            string folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/') + "/" + profile.ReducedMeshFolder;
            SculptCharacterBuilder.EnsureFolder(folder);
            return $"{folder}/{Path.GetFileNameWithoutExtension(sourcePath)}_{source.name}_LOD{level}.asset";
        }

        private static Mesh Store(Mesh reduced, Mesh existing, string path, string stamp)
        {
            if (existing != null)
            {
                EditorUtility.CopySerialized(reduced, existing);
                Object.DestroyImmediate(reduced);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(reduced, path);
            }

            AssetDatabase.SaveAssets();
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = stamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static int Triangles(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        /// <summary>Level <paramref name="level"/>'s triangles, over only the vertices they use.</summary>
        private static Mesh Extract(Mesh simplified, int level, string name)
        {
            var remap = new int[simplified.vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var kept = new List<int>();
            var submeshes = new int[simplified.subMeshCount][];

            for (int s = 0; s < submeshes.Length; s++)
            {
                int[] indices = simplified.GetIndices(s, level, true);
                for (int i = 0; i < indices.Length; i++)
                {
                    int v = indices[i];
                    if (remap[v] < 0)
                    {
                        remap[v] = kept.Count;
                        kept.Add(v);
                    }
                    indices[i] = remap[v];
                }
                submeshes[s] = indices;
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = kept.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            CopyVertices(simplified, mesh, kept);
            CopySkin(simplified, mesh, kept);
            CopyBlendShapes(simplified, mesh, kept);

            mesh.subMeshCount = submeshes.Length;
            for (int s = 0; s < submeshes.Length; s++)
                mesh.SetIndices(submeshes[s], MeshTopology.Triangles, s, false);
            mesh.bounds = simplified.bounds;
            return mesh;
        }

        private static void CopyVertices(Mesh from, Mesh to, List<int> kept)
        {
            to.SetVertices(Pick(from.vertices, kept));
            if (from.HasVertexAttribute(VertexAttribute.Normal)) to.SetNormals(Pick(from.normals, kept));
            if (from.HasVertexAttribute(VertexAttribute.Tangent)) to.SetTangents(Pick(from.tangents, kept));
            if (from.HasVertexAttribute(VertexAttribute.Color)) to.SetColors(Pick(from.colors, kept));

            var uvs = new List<Vector4>();
            for (int channel = 0; channel < UvChannels; channel++)
            {
                var attribute = VertexAttribute.TexCoord0 + channel;
                if (!from.HasVertexAttribute(attribute)) continue;

                from.GetUVs(channel, uvs);
                var picked = Pick(uvs, kept);
                switch (from.GetVertexAttributeDimension(attribute))
                {
                    case 2: to.SetUVs(channel, picked.ConvertAll(uv => (Vector2)uv)); break;
                    case 3: to.SetUVs(channel, picked.ConvertAll(uv => (Vector3)uv)); break;
                    default: to.SetUVs(channel, picked); break;
                }
            }
        }

        private static void CopySkin(Mesh from, Mesh to, List<int> kept)
        {
            to.bindposes = from.bindposes;
            NativeArray<byte> perVertex = from.GetBonesPerVertex();
            if (perVertex.Length == 0) return;

            NativeArray<BoneWeight1> weights = from.GetAllBoneWeights();
            var start = new int[perVertex.Length];
            for (int v = 1; v < start.Length; v++) start[v] = start[v - 1] + perVertex[v - 1];

            var keptPerVertex = new NativeArray<byte>(kept.Count, Allocator.Temp);
            var keptWeights = new List<BoneWeight1>();
            for (int i = 0; i < kept.Count; i++)
            {
                int v = kept[i];
                keptPerVertex[i] = perVertex[v];
                for (int w = 0; w < perVertex[v]; w++) keptWeights.Add(weights[start[v] + w]);
            }

            var keptWeightArray = new NativeArray<BoneWeight1>(keptWeights.ToArray(), Allocator.Temp);
            to.SetBoneWeights(keptPerVertex, keptWeightArray);
            keptWeightArray.Dispose();
            keptPerVertex.Dispose();
        }

        private static void CopyBlendShapes(Mesh from, Mesh to, List<int> kept)
        {
            var positions = new Vector3[from.vertexCount];
            var normals = new Vector3[from.vertexCount];
            var tangents = new Vector3[from.vertexCount];
            for (int shape = 0; shape < from.blendShapeCount; shape++)
            {
                for (int frame = 0; frame < from.GetBlendShapeFrameCount(shape); frame++)
                {
                    from.GetBlendShapeFrameVertices(shape, frame, positions, normals, tangents);
                    to.AddBlendShapeFrame(from.GetBlendShapeName(shape), from.GetBlendShapeFrameWeight(shape, frame),
                        Pick(positions, kept).ToArray(), Pick(normals, kept).ToArray(), Pick(tangents, kept).ToArray());
                }
            }
        }

        private static List<T> Pick<T>(IList<T> values, List<int> kept)
        {
            var picked = new List<T>(kept.Count);
            foreach (int v in kept) picked.Add(values[v]);
            return picked;
        }
    }
}
