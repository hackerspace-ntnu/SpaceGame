// A resident draws its tool for work and puts it on the belt for everything else, travel included, so an idle or
// walking body has free hands (and no hold pose bending its spine).
using System;
using NUnit.Framework;
using SpaceGame.Agents.Residents;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ResidentHandsRuleTests
    {
        private static readonly Activity[] WorkedWithTheTool =
        {
            Activity.None, Activity.Work, Activity.Patrol, Activity.Trip, Activity.Stalking, Activity.Expedition,
        };

        private static readonly Activity[] DoneWithFreeHands =
        {
            Activity.Sleep, Activity.Break, Activity.Stroll, Activity.Hearth, Activity.Walking, Activity.Talking,
            Activity.Sitting, Activity.Sheltering, Activity.Amble, Activity.Climbing,
        };

        [Test]
        public void EveryActivityIsDecidedOnPurpose()
        {
            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            {
                bool tool = Array.IndexOf(WorkedWithTheTool, activity) >= 0;
                bool free = Array.IndexOf(DoneWithFreeHands, activity) >= 0;
                Assert.IsTrue(tool ^ free ^ (activity == Activity.Chore),
                              $"{activity} is in neither list, or in both: decide whether it needs the tool");
            }
        }

        [Test]
        public void WorkAndPatrolAndTripsAreDoneWithTheTool()
        {
            foreach (Activity activity in WorkedWithTheTool)
            foreach (bool atPlace in new[] { true, false })
                Assert.AreEqual(HandContents.Tool, ResidentHandsRule.Wanted(activity, atPlace, false, true), activity.ToString());
        }

        [Test]
        public void ConversationsRestAndTravelAreDoneWithAnEmptyHand()
        {
            foreach (Activity activity in DoneWithFreeHands)
            foreach (bool atPlace in new[] { true, false })
                Assert.AreEqual(HandContents.Empty, ResidentHandsRule.Wanted(activity, atPlace, false, true), activity.ToString());
        }

        [Test]
        public void AStandInOnTheRoadCarriesItsWeaponThoughItCouldStowIt()
        {
            Assert.IsTrue(ResidentHandsRule.NeedsTool(Activity.Expedition, false), "walking the road is done armed (spec §6.1)");
            Assert.AreEqual(HandContents.Tool, ResidentHandsRule.Wanted(Activity.Expedition, false, false, true));
        }

        [Test]
        public void ATalkingResidentHasPutItsToolAway()
        {
            Assert.AreEqual(HandContents.Empty, ResidentHandsRule.Wanted(Activity.Talking, false, false, true));
        }

        [Test]
        public void AChoreDrawsTheToolAtItsSpotAndWalksBetweenSpotsWithoutIt()
        {
            Assert.AreEqual(HandContents.Tool, ResidentHandsRule.Wanted(Activity.Chore, true, false, true), "at the spot");
            Assert.AreEqual(HandContents.Empty, ResidentHandsRule.Wanted(Activity.Chore, false, false, true), "on the way to the next");
        }

        [Test]
        public void AToolThatCannotBeStowedStaysInTheHand()
        {
            foreach (Activity activity in DoneWithFreeHands)
                Assert.AreEqual(HandContents.Tool, ResidentHandsRule.Wanted(activity, false, false, false),
                                $"{activity}: nothing draws it on the body, so it must not leave the hand");
        }

        [Test]
        public void HandsOnACartHoldNothingElse_WhateverTheActivityAndWhateverIsToBeCarried()
        {
            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            foreach (bool atPlace in new[] { true, false })
            foreach (bool carrying in new[] { true, false })
            foreach (bool stowable in new[] { true, false })
                Assert.AreEqual(HandContents.Empty, ResidentHandsRule.Wanted(activity, atPlace, carrying, stowable, pushing: true), activity.ToString());
        }

        [Test]
        public void ACarriedItemWinsTheHandWhateverTheActivity()
        {
            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            foreach (bool atPlace in new[] { true, false })
            foreach (bool stowable in new[] { true, false })
                Assert.AreEqual(HandContents.Carried, ResidentHandsRule.Wanted(activity, atPlace, true, stowable), activity.ToString());
        }

        [Test]
        public void AStationCueEmptiesTheHandWhenItsClipsAreBareHandedOrMoveBothArmsLikeAGesture()
        {
            Assert.IsFalse(ResidentHandsRule.EmptyHandsFor(null), "no station: the tool stays where the activity put it");

            CharacterCue cue = ScriptableObject.CreateInstance<CharacterCue>();
            try
            {
                Assert.IsFalse(ResidentHandsRule.EmptyHandsFor(cue), "a loop done with the tool (hammering, mining) keeps it in the hand");

                Set(cue, "bareHands", true);
                Assert.IsTrue(ResidentHandsRule.EmptyHandsFor(cue), "wiping and rummaging are done with empty hands");

                Set(cue, "bareHands", false);
                Set(cue, "needsFreeHands", true);
                Assert.IsTrue(ResidentHandsRule.EmptyHandsFor(cue),
                              "a gesture loop is never played over a held tool, so a worker holding one would stand there doing nothing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void ATraderAtItsStallPutsTheScalesAwayToExplain()
        {
            var stall = AssetDatabase.LoadAssetAtPath<SpaceGame.World.SpotUse>("Assets/Game/ScriptableObjects/Settlements/Spots/Stall.asset");
            Assert.IsNotNull(stall, "no Stall spot use");
            Assert.IsTrue(ResidentHandsRule.EmptyHandsFor(stall.holdCue),
                          "the stall's explaining loops move both arms: with the scales in the hand the trader stood motionless all day");
        }

        private static void Set(CharacterCue cue, string field, bool value)
        {
            var so = new SerializedObject(cue);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
