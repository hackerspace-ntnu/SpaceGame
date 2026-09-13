using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The counter-scale, and the totality of the lookup that feeds it.
    ///
    /// <para>
    /// A holder is authored 1 m cubed and stretched non-uniformly to the item it covers, so a strap
    /// over the LaserStaff is scaled 22x along its length relative to across it. Everything
    /// rigid under a <c>HARD_</c> empty has to be pulled back out of that stretch, and when it is
    /// not the failure reads as a modelling mistake — buckles the size of dinner plates — rather
    /// than a code one, so nobody looks here. Hence a test.
    /// </para>
    /// <para>
    /// The fit is a size ON THE MAT, so it is written in the frame the staff was authored in and
    /// put through <see cref="M"/>. The counter-scale is a ratio and cares about neither — which is
    /// the point: it has to hold at any scale of pack.
    /// </para>
    /// </summary>
    public class HolderBuilderTests
    {
        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today. See <c>PackLayoutTests</c> for the reasoning.
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        /// <summary>Stands in for the pack's SURF_ empty, FBX centimetre convention and all.</summary>
        private GameObject surface;

        /// <summary>
        /// What the LaserStaff measures on the mat: long along X, slim across the other two. Its
        /// authored 1.35 m is 22.5x its 0.06 m girth, and the 2026-09-01 enlargement multiplied
        /// both by <see cref="PackScale.Factor"/>, so the stretch the holder has to undo is the
        /// same stretch it always was.
        /// </summary>
        private static readonly Vector3 StaffFit = new(M(1.35f), M(0.06f), M(0.06f));

        /// <summary>The pack's FBX arrives 100x, so anything parented into it inherits that.</summary>
        private const float SurfaceLossyScale = 100f;

        [TearDown]
        public void TearDown()
        {
            if (surface != null) Object.DestroyImmediate(surface);
            surface = null;
        }

        /// <summary>
        /// A holder root fitted to <paramref name="fit"/> under a 100x surface, exactly as
        /// <see cref="HolderBuilder.Build"/> seats one. Returns the root.
        /// </summary>
        private Transform FittedHolder(Vector3 fit)
        {
            surface = new GameObject("SURF_Test");
            surface.transform.localScale = Vector3.one * SurfaceLossyScale;

            var holder = new GameObject("Holder_Webbing");
            holder.transform.SetParent(surface.transform, false);
            holder.transform.localScale = fit / SurfaceLossyScale;

            return holder.transform;
        }

        private static Transform Child(Transform parent, string name, float localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * localScale;
            return go.transform;
        }

        private static void AssertScale(Vector3 expected, Vector3 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f, $"{what} x");
            Assert.AreEqual(expected.y, actual.y, 1e-4f, $"{what} y");
            Assert.AreEqual(expected.z, actual.z, 1e-4f, $"{what} z");
        }

        /// <summary>
        /// The test this whole file exists for. A 4 cm buckle on a strap stretched the whole length
        /// of a staff is still 4 cm across every axis; the webbing it is riding on is not. The
        /// buckle's 0.04 is a local SCALE under the holder root, not a length on the mat, so it
        /// does not ride <see cref="M"/> — it is 1 authored metre counter-scaled back to 1, times
        /// the 0.04 the modeller drew.
        /// </summary>
        [Test]
        public void HardwareKeepsItsAuthoredWorldSizeThroughANonUniformFit()
        {
            Transform holder = FittedHolder(StaffFit);

            Transform webbing = Child(holder, "Tape_Strap", 1f);
            Transform buckle = Child(Child(holder, "HARD_Buckle", 1f), "Buckle_Mesh", 0.04f);

            HolderBuilder.CounterScaleHardware(holder, StaffFit);

            // 1 authored unit across the buckle's subtree is 1 world unit again, so the part the
            // modeller drew at 0.04 is 0.04 on screen — the same on the LaserStaff as on a Leash.
            AssertScale(Vector3.one * 0.04f, buckle.lossyScale, "buckle");

            // And the soft part is untouched: it still spans the whole item, which is the point of
            // the stretch. A counter-scale that caught everything would just undo the fit.
            AssertScale(StaffFit, webbing.lossyScale, "webbing");
        }

        /// <summary>
        /// Two ways the walk can go wrong on a real rig: hardware buried under a soft group, and
        /// hardware nested inside hardware.
        /// </summary>
        [Test]
        public void TheWalkFindsBuriedHardwareAndDoesNotCorrectItTwice()
        {
            Transform holder = FittedHolder(StaffFit);

            // A modeller groups the two strap ends; the buckles hang off the groups, not the root.
            Transform buried = Child(Child(holder, "Strap_End_A", 1f), "HARD_Buckle", 1f);

            // A snap gate modelled as part of the buckle it lives on. Correcting it a second time
            // would divide by the fit again and shrink it to nothing along the stretch axis.
            Transform nested = Child(buried, "HARD_SnapGate", 0.5f);

            HolderBuilder.CounterScaleHardware(holder, StaffFit);

            AssertScale(Vector3.one, buried.lossyScale, "buried buckle");
            AssertScale(Vector3.one * 0.5f, nested.lossyScale, "nested snap gate");
        }

        /// <summary>
        /// <see cref="HolderLibrary.PrefabFor"/> is asked on every layout rebuild for whatever kind
        /// <c>ItemFootprint</c> guessed, and the guess can name art nobody has modelled. It has to
        /// answer for every kind — an exception there would take the whole display down for a
        /// cosmetic gap.
        /// </summary>
        [Test]
        public void TheLookupAnswersForEveryKindEvenUnmappedOnes()
        {
            var prefab = new GameObject("Holder_Webbing_Prefab");
            var library = ScriptableObject.CreateInstance<HolderLibrary>();

            try
            {
                var so = new UnityEditor.SerializedObject(library);
                UnityEditor.SerializedProperty entries = so.FindProperty("entries");
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("kind").enumValueIndex =
                    (int)HolderKind.Webbing;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue =
                    prefab;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreSame(prefab, library.PrefabFor(HolderKind.Webbing));

                foreach (HolderKind kind in System.Enum.GetValues(typeof(HolderKind)))
                {
                    if (kind == HolderKind.Webbing) continue;

                    // Null, not a throw. The warning it logs is deliberate and only fires once.
                    Assert.IsNull(library.PrefabFor(kind), $"{kind} should be unmapped");
                    Assert.IsFalse(library.Has(kind), $"{kind} should report as unmapped");
                }
            }
            finally
            {
                Object.DestroyImmediate(library);
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
