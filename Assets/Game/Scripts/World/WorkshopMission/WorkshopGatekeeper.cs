// The Shellback who got out of the workshop before the gate jammed. Frantic while it is shut: he paces in front of
// it, and the first time a player comes near he shouts for them and tells them what happened without waiting to be
// asked; talked to after that, he gives hints. The moment the gate opens he says so and hurries in to the main room,
// where he gives the player the mission when they reach him. When the gear comes home he thanks them and offers the
// reactor core in the vault for their ship, and when the hoist has fitted it he sees them off.
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class WorkshopGatekeeper : WorkshopShellback
    {
        private const string GearsToken = "{gears}";

        [Header("Lines")]
        [Tooltip("Shouted the first time a player comes near while the gate is shut, before the plea.")]
        [SerializeField, TextArea(1, 3)] private string callOut = "Hey! You there! Over here - please!";

        [Tooltip("What happened: said unasked, one after another, the first time a player comes near.")]
        [SerializeField, TextArea(2, 4)] private string[] plea =
        {
            "Thank the tides, somebody came! Raiders - a whole gang of them - broke into our workshop last night!",
            "On their way out they smashed the gate's gear train and threw the gears all over the sand. Now it won't open!",
            "My family's shut in there and I'm stuck out here. Please, help me get the gate open!",
            "Put the loose gears back on the pegs. When the chain of gears reaches the lock, it lets go.",
        };

        [Tooltip("Said in turn when talked to after the plea while the gate is shut. " + GearsToken + " is how many gears still lie in the sand.")]
        [SerializeField, TextArea(2, 4)] private string[] hints =
        {
            "A gear that meshes with a turning one turns too. Follow the chain across to the lock on the other leaf.",
            GearsToken + " still lying in the sand. Every one of them belongs on that gate.",
            "If a gear won't go on, it would hit its neighbour. Try another peg.",
        };

        [Tooltip("Said unasked the moment the gate opens.")]
        [SerializeField, TextArea(2, 4)] private string[] opened =
        {
            "It's moving - you did it! The gate's open!",
            "Come inside, quick - my family's in the main hall. I'll meet you in the middle!",
        };

        [Tooltip("Said when talked to on the way in, while everyone gathers in the main hall.")]
        [SerializeField, TextArea(2, 4)] private string[] gathering =
        {
            "Come on, come on - the middle of the main hall!",
        };

        [Tooltip("Said unasked to everyone at the meeting: the new mission.")]
        [SerializeField, TextArea(2, 4)] private string[] briefing =
        {
            "Everyone - this is the one who opened the gate!",
            "But the raiders took something worse than our gate. They stole the Heart Gear - the big brass cog that drives this whole workshop.",
            "Without it nothing down here will run for long. They can't have got far with a cog that heavy.",
            "They went out through the gate. Find their trail out there - your scanner will pick up anything they dropped.",
            "Track them down and bring the Heart Gear home. We'll be waiting right here.",
        };

        [Tooltip("Said in turn while the thieves still have the gear.")]
        [SerializeField, TextArea(2, 4)] private string[] hunting =
        {
            "Start at the gate and follow what they dropped. Your scanner will pick it up.",
            "Careful. They're armed, and they won't hand it back nicely. If they spot you, whoever has the gear will run.",
        };

        [Tooltip("Said when talked to by somebody who has the gear.")]
        [SerializeField, TextArea(2, 4)] private string[] carrying =
        {
            "Is that - the Heart Gear! Bring it here, quick!",
        };

        [Tooltip("Said unasked to everyone near when the gear comes home.")]
        [SerializeField, TextArea(2, 4)] private string[] thanks =
        {
            "The Heart Gear! You brought it home!",
            "We owe you more than we can say. Our workshop is yours to walk whenever you like.",
        };

        [Tooltip("Said unasked straight after the thanks: the vault's reactor core, offered for the player's ship.")]
        [SerializeField, TextArea(2, 4)] private string[] vaultOffer =
        {
            "And that ship of yours - we can help you fix it.",
            "We kept one part that fits a ship like yours: a sealed reactor core, locked away in our vault. We can install it for you.",
            "But you'll have to find the vault. It's hidden deep in the caves, behind the turning rooms.",
            "Turn this main room with its big hand cog and the hidden hallways open up. From there, turn each room to find the way through.",
            "Bring the core back, take it up to the crane console on the third floor, hang it on the hook and lower it onto your ship. We'll bolt it in.",
        };

        [Tooltip("Said in turn while the core is still to be fetched.")]
        [SerializeField, TextArea(2, 4)] private string[] vaultHints =
        {
            "The vault lies past the turning rooms. Turn the main room to face a new hallway, and follow it in.",
            "Every room has its own hand cog. Hold it and the room turns; its doorway stays wherever you leave it.",
            "The vault door has a gear lock, and all five of its cogs are off it. They lie on the floor in front of the door; every one of them is needed.",
            "That core is far too heavy to bolt on by hand. The hoist does it - work it from the console on the third floor.",
        };

        [Tooltip("Said when talked to by somebody who has the core in their hands.")]
        [SerializeField, TextArea(2, 4)] private string[] carryingCore =
        {
            "That's the core! Up to the crane console on the third floor - bring the hook over and hang it on.",
        };

        [Tooltip("Said in turn while the core hangs on the hoist's hook.")]
        [SerializeField, TextArea(2, 4)] private string[] coreOnHook =
        {
            "Now lower it onto your ship's reactor mount. The console shows you where it goes - then let go.",
        };

        [Tooltip("Said unasked to everyone near when the hoist has fitted the core.")]
        [SerializeField, TextArea(2, 4)] private string[] repaired =
        {
            "There - it's in! Your ship has its reactor core back.",
            "Safe travels, friend. Our workshop is yours whenever you need it.",
        };

        [Tooltip("Said in turn once it is all over.")]
        [SerializeField, TextArea(2, 4)] private string[] afterwards =
        {
            "Thanks to you, the old place runs again.",
            "Come back any time, friend.",
        };

        [Header("Panic")]
        [Tooltip("How far to either side of his spot he paces while the gate is shut, in metres.")]
        [SerializeField, Min(0f)] private float paceRadius = 2.5f;

        [Tooltip("How fast he paces, as a share of his walking speed.")]
        [SerializeField, Min(0.1f)] private float paceSpeed = 1.4f;

        [Tooltip("How close a player must come for him to call out and tell his story, in metres.")]
        [SerializeField, Min(1f)] private float callRadius = 14f;

        private bool pacingLeft;
        private bool toldStory;
        private int pleaLine;

        // ── Server ───────────────────────────────────────────────────────────

        /// <summary>Server, every think while the gate is shut: pace beside <paramref name="stand"/>, or stand and face a player who came near.</summary>
        public void Plead(Pose stand, Transform nearestPlayer)
        {
            if (!IsAlive || Goal == null) return;

            if (nearestPlayer != null && Within(nearestPlayer, callRadius))
            {
                Goal.TrySetSampled(transform.position, ArriveRadius, "gatekeeper", hold: true, facePoint: nearestPlayer.position);
                FaceIfNear(nearestPlayer);
                return;
            }

            if (Goal.HasArrived || !Goal.HasGoal) pacingLeft = !pacingLeft;
            Vector3 side = stand.position + stand.right * (pacingLeft ? -paceRadius : paceRadius);
            Goal.TrySetSampled(side, ArriveRadius, "gatekeeper pacing", hold: true,
                               facePoint: stand.position + stand.forward * 5f, speedMultiplier: paceSpeed);
        }

        // ── Every machine ────────────────────────────────────────────────────

        protected override void Update()
        {
            WorkshopMission at = Mission;
            if (at != null && IsAlive && !toldStory && at.Phase == WorkshopMissionPhase.Locked)
            {
                Transform local = GameplayMenuScope.LocalPlayerTransform;
                if (local != null && Within(local, callRadius))
                {
                    toldStory = true;
                    pleaLine = plea.Length;
                    var story = new string[plea.Length + 1];
                    story[0] = callOut;
                    plea.CopyTo(story, 1);
                    Speak(story);
                    at.HearPlea();
                }
            }
            base.Update();
        }

        protected override string NextLine(WorkshopMission at)
        {
            switch (at.Phase)
            {
                case WorkshopMissionPhase.Locked:
                    if (pleaLine < plea.Length) return plea[pleaLine++];
                    string hint = Turn(at, hints);
                    int loose = at.Gate != null ? at.Gate.LooseGears : 0;
                    return hint.Replace(GearsToken, loose == 1 ? "One gear" : $"{loose} gears");
                case WorkshopMissionPhase.Gathering:
                    return Turn(at, gathering);
                case WorkshopMissionPhase.Hunting:
                    return Turn(at, at.GearCarried ? carrying : hunting);
                case WorkshopMissionPhase.Vault:
                    return Turn(at, at.GearCarried ? carryingCore : at.Crane != null && at.Crane.IsLoaded ? coreOnHook : vaultHints);
                default:
                    return Turn(at, afterwards);
            }
        }

        protected override void OnTalkedTo(WorkshopMission at)
        {
            if (at.Phase != WorkshopMissionPhase.Locked) return;
            toldStory = true;
            at.HearPlea();
        }

        protected override void OnMissionChanged(bool live)
        {
            if (!live) return;

            string[] lines = Mission.Phase switch
            {
                WorkshopMissionPhase.Gathering => opened,
                WorkshopMissionPhase.Hunting => briefing,
                WorkshopMissionPhase.Vault => Join(thanks, vaultOffer),
                WorkshopMissionPhase.Repaired => repaired,
                _ => null,
            };
            if (lines != null) Speak(lines);
        }

        private static string[] Join(string[] first, string[] then)
        {
            var both = new string[first.Length + then.Length];
            first.CopyTo(both, 0);
            then.CopyTo(both, first.Length);
            return both;
        }
    }
}
