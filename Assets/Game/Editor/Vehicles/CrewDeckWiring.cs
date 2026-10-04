// The crew half of every Strider carrier, house or barge: lookout posts on the open deck, a gangway at
// the hull's feet, and the components that seat a crew there and put them ashore at a stop (ChairPose,
// VesselSeats, CrewShift). Each builder measures its own spots -- a house from its deck and hull
// colliders, a barge from its roof -- and hands them here, so a carrier's crew is wired one way.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class CrewDeckWiring
    {
        public const string PostsName = "CrewPosts";
        public const string GangwayName = "Gangway";

        /// <summary>
        /// One post per spot (root space), each facing outward from <paramref name="deckCentre"/> so its
        /// lookout watches the desert; a gangway at <paramref name="gangwayLocal"/>; then the seats and
        /// the crew shift that uses both. <paramref name="gangwayNavMeshReach"/> is how far an unseat
        /// from the gangway may look for walkable ground.
        /// </summary>
        public static void AddCrewDeck(GameObject root, IReadOnlyList<Vector3> postSpots, Vector3 deckCentre,
                                       Vector3 gangwayLocal, float gangwayNavMeshReach)
        {
            var seatsRoot = new GameObject(PostsName).transform;
            seatsRoot.SetParent(root.transform, false);
            var posts = new Transform[postSpots.Count];
            for (int i = 0; i < posts.Length; i++)
            {
                var post = new GameObject($"Post_{i}").transform;
                post.SetParent(seatsRoot, false);
                post.localPosition = postSpots[i];
                Vector3 outward = postSpots[i] - deckCentre;
                outward.y = 0f;
                post.localRotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
                posts[i] = post;
            }

            var gangway = new GameObject(GangwayName).transform;
            gangway.SetParent(root.transform, false);
            gangway.localPosition = gangwayLocal;

            var chair = root.AddComponent<ChairPose>();
            var seats = root.AddComponent<VesselSeats>();
            SerializedFields.Edit(seats, so =>
            {
                SerializedProperty array = so.FindProperty("seats");
                array.arraySize = posts.Length;
                for (int i = 0; i < posts.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = posts[i];
                SerializedFields.SetFloat(so, "navMeshReach", gangwayNavMeshReach);
                SerializedFields.Set(so, "chairPose", chair);
            });

            var shift = root.AddComponent<CrewShift>();
            SerializedFields.Edit(shift, so => SerializedFields.Set(so, "gangway", gangway));
        }
    }
}
