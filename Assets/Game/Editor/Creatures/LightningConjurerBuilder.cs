// Builds every Unity-side asset the Lightning Conjurer needs, from the exported FBX up.
//
// The FBX comes out of Blender via the rig/anim/export scripts kept beside the
// model in Assets/Game/Art/Models/Creatures/Robotic/LightningConjurer/_Source~/.
// Everything below that -- import settings, animation clips, the animator
// controller, the prefab, and the test-scene instance -- is generated here rather
// than hand-authored, for the same reason GolemBuilder and VrescalBuilder exist:
// a prefab wired by hand is a prefab nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller, prefab and scene instance are rebuilt in place.
//
// ---- the legs are animation, not IK ------------------------------------
//
// This creature walks on its baked Walk clip, driven by a NavMeshAgent, exactly as
// the golem, the dune rat and the vrescal do. It is a stock NavMesh creature:
//
//     NavMeshAgent + NavMeshAgentMotor + Animator + AgentAnimatorDriver
//
// It used to be the other thing. ConjurerLocomotion + ConjurerDriver solved the
// legs procedurally against the real ground through Assets/Game/Scripts/Locomotion,
// and the whole stack -- the two components, the rig-discovery tests, the
// swing-to-stance footstep hook -- has been taken back out. Nothing under
// Assets/Game/Scripts/Locomotion is referenced from this creature any more; the
// walker system is untouched and still carries the ostrich, the horse, the crab and
// the humanoid robot.
//
// What that trade actually costs, so nobody re-litigates it by accident. The walk
// clip IS foot-locked on flat ground -- anim.py solves the legs from a constant-speed
// contact trajectory and _Source~/stride.py asserts the planted foot holds 8.99 m/s
// to within 0.05% -- so the creature does not skate at the speed the clip was
// authored at. What it still cannot do is know about the WORLD: play the clip at a
// rate that does not match the body's speed and the lock is worthless, which is what
// StrideSpeed, the blend tree's thresholds and AgentAnimatorDriver.animatorSpeedScale
// are all for below. Slopes and uneven ground are not modelled at all -- the agent's
// transform slides along the NavMesh and the feet go where the clip puts them.
//
// The rig keeps walkerize.py's Coxa_/Hip_/Knee_/Ankle_/Foot_ naming. Nothing needs
// it now, but anim.py keys those bone names and re-running the pipeline against the
// old names would produce an FBX with no curves. The cold-start order is unchanged:
// rig.py -> walkerize.py -> anim.py -> export.py.
//
// Re-run from: Tools > Creatures > Build Lightning Conjurer
using FirstGearGames.SmoothCameraShaker;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class LightningConjurerBuilder
    {
        private const string ModelDir =
            "Assets/Game/Art/Models/Creatures/Robotic/LightningConjurer";
        private const string Fbx = ModelDir + "/LightningConjurer.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/LightningConjurer.controller";
        private const string PrefabDir = "Assets/Game/Prefabs/Agents/creatures";
        private const string PrefabPath = PrefabDir + "/LightningConjurer.prefab";

        /// The three generated clips, kept beside the controller that is the only thing
        /// referencing them. Regenerated wholesale on every build, like everything else here.
        private const string SleepClipPath = ControllerDir + "/LightningConjurer_Sleep.anim";
        private const string AwakenClipPath = ControllerDir + "/LightningConjurer_Awakening.anim";
        private const string DeathClipPath = ControllerDir + "/LightningConjurer_Death.anim";
        private const string ScenePath = "Assets/Game/Scenes/Tests/Marius test scene.unity";
        private const string InstanceName = "LightningConjurer";
        private const string MaterialDir = "Assets/Game/Art/Materials/Palette";

        /// The body's single material and the shader behind it. Not in the palette
        /// folder: every asset in there is a flat colour from PALETTE.md, and this
        /// one has no colour of its own -- the mesh carries it.
        private const string WeatheredShader = "SpaceGame/ConjurerWeathered";
        private const string WeatheredMatPath =
            "Assets/Game/Art/Materials/Creatures/Mat_Weathered_Blend.mat";
        private const string ShakeDir = "Assets/Game/ScriptableObjects/Shake";
        private const string ShakeDataPath = ShakeDir + "/ConjurerFootstepShake.asset";

        /// The bolt the cast draws.
        ///
        /// NOT Lightning.prefab, which is what the player's LightningSpell throws. That one is
        /// a VFX Graph whose length is baked into the graph and exposed nowhere, so at a 100 m
        /// draw height it hangs in the sky with nothing under it. LightningBoltEffect draws the
        /// bolt as geometry between two points it is GIVEN, so it spans whatever it is asked to
        /// span -- on the same SpaceGame/LightningBeam shader, in blue rather than the laser
        /// staff's red.
        private const string LightningVfxPath =
            "Assets/Game/Prefabs/VisualEffects/Lightning/ConjurerLightningBolt.prefab";

        /// The charge that gathers on the staff, built by BuildStaffCharge.
        ///
        /// Replaces ConjurerChestCharge.prefab, which lit a ring in the creature's chest
        /// and ran arcs inward to the two palms hovering either side of it. Both the ring
        /// and the pose are gone: _Source~/staff.py deleted the charger and gave the
        /// creature a staff, and the arcs now run from the emitter above the turbine and
        /// increasingly UP into the sky, because that is where the bolt is coming from.
        ///
        /// Generated rather than hand-authored for the same reason the prefab is: its arc
        /// widths, core size and fan radius are sized against the MODEL, whose scale is
        /// derived here. An asset dragged together by hand goes stale the moment the model
        /// is re-exported at a different size.
        private const string ChargeVfxPath =
            "Assets/Game/Prefabs/VisualEffects/Lightning/ConjurerStaffCharge.prefab";

        /// Enough that the turbine is lit all round and a growing share can peel off into
        /// the sky without the fan going dark. ConjurerStaffCharge splits them by index,
        /// so this only wants to be big enough that a fraction of it is still several.
        private const int ChargeArcCount = 10;

        /// Arc ribbon width. Two orders of magnitude below the strike's 0.6-1.4 m, because
        /// this one spans a turbine and that one spans the distance from the clouds.
        private const float ChargeArcWidth = 0.045f;

        /// The staff's own size factor, mirroring _Source~/staff.py's SIZE. Kept here so
        /// the numbers below can be read against the ones in that file rather than being
        /// pre-multiplied and unrecognisable.
        private const float StaffSize = 0.75f;

        /// The turbine, in metres, for the arcs that play over it. Both derived from
        /// _Source~/staff.py rather than typed: FAN_R1 is the blade tip radius, and the
        /// drop is the gap from the fan's hub (HUB_Z 38.0) up to the emitter
        /// (TOP_Z 41.975) that ConjurerStaffCharge hangs off.
        private const float StaffFanRadiusBlender = 4.60f * StaffSize;
        private const float StaffFanDropBlender = 41.975f - 38.00f;
        private static float ChargeFanRadius => StaffFanRadiusBlender * Scale;
        private static float ChargeFanDrop => StaffFanDropBlender * Scale;

        /// How far the skyward arcs reach at full charge, in metres. Deliberately several
        /// times the fan's own size: these are the part of the effect that says the answer
        /// is coming from above, and an arc that only just clears the turbine says nothing.
        private const float ChargeSkyReach = 16f;

        /// Whether the cast paints a ring on the ground where the bolt will land.
        ///
        /// OFF, by request. Flip it back to true and the ring returns: it gates the build
        /// and the wiring together, so nothing else has to change.
        ///
        /// Worth knowing what it costs, because the ring was not decoration. A falling
        /// bolt cannot be blocked, and it cannot be dodged by angle the way a fired line
        /// can -- it simply arrives on the point the caster picked. The ring was the whole
        /// of the player's warning, so with it off the attack is damage on a timer and the
        /// only counterplay left is reading the CREATURE: the four-and-a-half second cast,
        /// the staff coming up, the turbine spinning, and the emitter lighting. That is a
        /// real telegraph and it may well be the one you want -- it just all lives on the
        /// creature now, and at range it is the only thing there is.
        private const bool GroundWarning = false;

        /// The mark on the ground under the strike, built by BuildStrikeTelegraph.
        /// Only built and wired when GroundWarning is on.
        private const string TelegraphVfxPath =
            "Assets/Game/Prefabs/VisualEffects/Lightning/ConjurerStrikeWarning.prefab";

        /// The generated annulus the warning ring is drawn with, kept beside the prefab.
        ///
        /// A mesh asset rather than a scaled primitive because Unity has no ring: a flat
        /// cylinder gives a filled disc, and a filled disc under the player's feet hides
        /// the ground they are trying to run across. The outline is the readable shape.
        private const string RingMeshPath =
            "Assets/Game/Art/Models/Generated/StrikeWarningRing.asset";

        /// Ring proportions. The mesh is authored at radius 1 and scaled to the blast
        /// radius at runtime, so this is the fraction of that radius the band occupies.
        private const float RingThickness = 0.12f;
        private const int RingSegments = 64;

        /// How high the warning column starts, in metres. Tall enough to be visible from
        /// outside the blast when the cast begins, and it descends to nothing as the bolt
        /// arrives -- that fall is what tells the player how long is left.
        private const float TelegraphColumnHeight = 55f;

        /// How far the ring looks for ground, in metres. Short on purpose: it is meant to
        /// find the floor under the target's feet, and a long probe finds the canyon floor
        /// instead when somebody is standing on a ledge.
        private const float TelegraphGroundProbe = 8f;
        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string RobotFactionPath = FactionDir + "/RobotFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        /// How close a player must come before the creature wakes up, in metres.
        ///
        /// This was 10 m, and 10 m was right while the creature had no attack: it read as
        /// something inert you walk up to and disturb rather than a sentry with a picket
        /// line. A caster cannot be that. It has to notice you far enough out to have
        /// somewhere to stand, so acquisition now sits just OUTSIDE CastRange -- the margin
        /// is what stops a player on the boundary flipping it between casting and inert.
        private const float ActivationRange = 28f;

        /// Distance at which a cast will START, and the outer edge of the engagement.
        ///
        /// Sized so the three-second wind-up is survivable, which is the entire point of the
        /// behaviour: at 25 m a player who sees the cup light up has time to break the
        /// 3.5 m blast radius on foot. Shorten this and the wind-up stops being counterplay
        /// and becomes a delay before an unavoidable hit.
        private const float CastRange = 25f;

        /// Where ChaseModule parks once it has closed.
        ///
        /// Inside CastRange so the creature is always able to throw from where it stands,
        /// and far enough out that it does not walk its 2.4 m capsule into the player's face
        /// during the two seconds between casts. This is the number that makes it read as a
        /// caster holding its distance rather than a brawler that happens to throw lightning.
        private const float StandoffDistance = 18f;

        /// One URP material per palette entry the model uses.
        ///
        /// These have to exist Unity-side because FBX material export is lossy: it
        /// carries a base colour and nothing else. Metallic, smoothness and above
        /// all EMISSION do not survive the trip, and the palette's own "Emissive"
        /// materials sit at emission strength 0 in palette.blend anyway - there the
        /// category records intent and hue, not glow. So the glow is authored here.
        ///
        /// Colours are the palette hex written straight as hex/255, matching
        /// DuneRat.mat (which stores 0.905882 for the #E7B345 of Mat_Hide_Sand_Pale
        /// rather than its linearised 0.799). Consistency with the project's
        /// existing materials matters more here than colour-space theory.
        private readonly struct Pal
        {
            public readonly string Name;
            public readonly int Hex;
            public readonly float Metallic, Roughness, Emission;
            public Pal(string name, int hex, float metallic, float roughness, float emission = 0f)
            {
                Name = name; Hex = hex; Metallic = metallic;
                Roughness = roughness; Emission = emission;
            }
            public Color Colour => new Color(((Hex >> 16) & 0xFF) / 255f,
                                             ((Hex >> 8) & 0xFF) / 255f,
                                             (Hex & 0xFF) / 255f, 1f);
        }

        private static readonly Pal[] Palette =
        {
            // The body, and it is a RAMP rather than one colour. rustify.py spreads
            // these three over the creature through a warped noise field -- dusty
            // khaki up top where the sun hits, bare grey through the body,
            // verdigris green down at the feet where water sits. One flat colour
            // over 48 parts reads as a repaint; three read as weathering.
            //
            // Every ramp entry must be listed here or the missing ones import as
            // default grey however good the .blend looks: the FBX remaps materials
            // BY NAME onto these assets, and a name with no entry matches nothing.
            //
            // Steel_Worn and Copper_Oxide are further down this array already and
            // are NOT repeated -- they are shared palette entries this creature
            // reuses rather than materials it owns. Only the khaki was added.
            new Pal("Mat_Metal_Patina_Khaki",    0xBFA070, 0.60f, 0.80f),
            // The rust family the creature wore before the grey/green/brown brief.
            // Kept listed because the FBX remap is by name and an older export --
            // or a rollback of rustify.py's RAMP -- would otherwise import grey.
            new Pal("Mat_Metal_Rust_Pale",       0xC6884A, 0.35f, 0.85f),
            new Pal("Mat_Metal_Rust_Heavy",      0x9A5D1D, 0.50f, 1.00f),
            new Pal("Mat_Metal_HullRust_Orange", 0x764E2A, 0.15f, 0.72f),
            new Pal("Mat_Metal_Rust_Deep",       0x4E3418, 0.40f, 1.00f),
            new Pal("Mat_Metal_Steel_Dark",      0x3A3E42, 1.00f, 0.45f),
            new Pal("Mat_Metal_Steel_Worn",      0x7A7D80, 1.00f, 0.55f),
            new Pal("Mat_Metal_Brass_Tarnished", 0x9C7B3F, 1.00f, 0.45f),
            new Pal("Mat_Metal_Chrome_Scuffed",  0xC9CDD2, 1.00f, 0.22f),
            new Pal("Mat_Metal_Copper_Oxide",    0x4E8C7A, 0.80f, 0.60f),
            new Pal("Mat_Neutral_Slate_Dark",    0x1F2736, 0.00f, 0.70f),
            new Pal("Mat_Neutral_Black_Matte",   0x272727, 0.00f, 0.55f),
            new Pal("Mat_Paint_White_Arctic",    0xD6DAD9, 0.35f, 0.58f),
            // The iris, the palm emitters and the halo share this one material, so
            // its intensity is a compromise: the halo is a big surface and blows
            // out long before a surface the size of the iris does. 2.0 reads as a
            // lit crystal on the halo while still carrying the eye.
            new Pal("Mat_Emissive_Portal_Blue",  0x2FB8FF, 0.00f, 0.15f, 2.0f),
        };

        // ---- Geometry, in the .blend's own units (Z up, model faces +X) --------
        // Measured off the source meshes; see the rig table in rig.py.
        private const float BlenderFloor = 2.757f;   // lowest point of both feet
        private const float BlenderTop = 37.49f;     // top of Eyelid, i.e. the body
        private const float BodyX = 0.19f;           // body centre line
        private const float BodyY = -0.06f;
        private const float BlenderBodyWidth = 9.3f;  // the head/body sphere across

        // The player model (AstronautArmature) is 3.019 m to the top of the head;
        // the brief was three times that, then doubled again to six.
        private const float PlayerHeight = 3.019f;
        private const float TargetHeight = PlayerHeight * 6f;

        /// Metres per Blender unit. Applied via ModelImporter.globalScale, NOT by
        /// scaling the armature: see ConfigureImporter.
        private static float Scale => TargetHeight / (BlenderTop - BlenderFloor);

        // ---- the passenger seat ------------------------------------------------
        //
        /// The bone a player rides on.
        ///
        /// This creature has no shoulder to sit astride. Its arms FLOAT: rig.py puts ArmRoot
        /// out at |y| 6.3-6.4, past the 4.65-unit radius of the head/body sphere, so there is
        /// clear air between the body and the arm and no neck to straddle. What there is, is the
        /// top of the arm assembly -- and anim.py pins ArmRoot outright ("the shoulder does not
        /// move, and the arm is posed by ANGLE"), so it is the one place on a moving arm that
        /// does not swing out from under a passenger when the staff comes up.
        ///
        /// Swap to "ArmRoot.R" for the other shoulder, or to "Spine" to ride the back.
        private const string SeatBone = "ArmRoot.L";

        /// Where the rider sits, relative to the shoulder bone, in the CARRIER's own frame.
        ///
        /// Measured off the built prefab rather than guessed, and the two measurements pull in
        /// opposite directions:
        ///
        ///   * The seat surface IS the bone. The shoulder cap (Cylinder.004, 1.49 m across) tops
        ///     out at exactly ArmRoot.L's own height -- the bone head sits on the crown of the
        ///     cap -- so nothing needs lifting clear of it.
        ///   * The player's transform origin is at their MIDDLE, not their feet. On
        ///     PlayerCharacter.prefab the hips sit 0.29 m above the origin and the soles 0.90 m
        ///     below it, so seating them at the surface would bury the pelvis in the shoulder.
        ///
        /// Net: drop them 0.15 m, which rests a SEATED pelvis just clear of the cap with the
        /// shins hanging a metre down its side, and nudge them forward so those shins swing past
        /// the front edge of the arm rather than through it.
        private static readonly Vector3 SeatOffset = new Vector3(0f, -0.15f, 0.35f);

        /// How close to the shoulder, on the ground, a player has to stand before the machine
        /// offers them the seat. Horizontal distance to the seat bone -- see MountModule's
        /// maxMountDistance for why height is left out of it.
        ///
        /// Three metres against a body column of half-width 2.4 m and a bone 3.27 m off the
        /// centreline: a player up against the left face stands well inside it, one on the right
        /// face is 5.7 m from the bone and gets nothing.
        private const float ShoulderBoardingRadius = 3f;

        // ---- the arms are solid ------------------------------------------------
        //
        /// One collider per arm segment, and what each one must NOT swallow.
        ///
        /// The exclusions are the whole difficulty. Every segment is the PARENT of the next in
        /// the chain, so a naive sweep of everything under UpperArm.L collects the forearm and
        /// the hand as well and boxes the entire arm as one slab; and Hand.R additionally
        /// parents the Staff, whose tip is up at 20.45 m -- eleven metres above the hand -- so
        /// swallowing it wraps the fist in a box taller than the creature.
        ///
        /// Fingers are deliberately NOT excluded. They are small, they belong to the hand, and
        /// six colliders on this creature is already the interesting end of what an animated
        /// compound body should carry.
        private static readonly (string bone, string[] excluding)[] ArmColliders =
        {
            ("UpperArm.L", new[] { "Forearm.L" }),
            ("Forearm.L",  new[] { "Hand.L" }),
            ("Hand.L",     new string[0]),
            ("UpperArm.R", new[] { "Forearm.R" }),
            ("Forearm.R",  new[] { "Hand.R" }),
            ("Hand.R",     new[] { "Staff" }),
        };

        /// Where a dismounting rider is put down: beside the machine, on the ground.
        ///
        /// Load-bearing on something this size. MountModule's fallback is one body-width to the
        /// side of the mount's own origin -- which for a seat fifteen metres up is a fifteen
        /// metre drop. Clear of the 2.4 m capsule and of both feet.
        private static readonly Vector3 DismountOffset = new Vector3(4.5f, 0.2f, 0f);

        /// Ground speed the Walk clip is authored at, in m/s. Load-bearing.
        ///
        /// AUTHORED, and then verified. anim.py builds the walk out of a foot trajectory
        /// whose stance leg travels at one constant speed, so the clip has exactly one ground
        /// speed rather than an average of several: HALF and WALK_FRAMES there are chosen to
        /// land on this number, and _Source~/stride.py re-measures the planted foot and
        /// ASSERTS it, rather than reporting a mean somebody then has to copy across.
        ///
        /// It used to be a mean, and the mean was 25% high -- taken over sixteen frames of a
        /// cycle whose foot speed swung from 6.6 to 11.5 m/s, on a clip that was not
        /// foot-locked at all. Everything downstream was then playing a 7.2 m/s walk as if it
        /// were a 9 m/s one, which is a fifth of the creature's speed spent skating.
        ///
        /// Three things downstream ARE this number, and they have to move together or the
        /// feet skate: RunSpeed below, the blend tree's top threshold, and
        /// AgentAnimatorDriver.animatorSpeedScale. All three are written in terms of this
        /// constant precisely so that re-measuring is a one-line change.
        private const float StrideSpeed = 8.99f;

        /// Top speed, and the speed at which the clip plays at its authored rate.
        ///
        /// Pinned to the clip rather than chosen: at exactly StrideSpeed the animator runs
        /// at 1.0 and the walk cycle is the one anim.py authored. Nine metres a second sounds
        /// absurd until you remember the stride is a full nine metres and the cycle is 1.73
        /// seconds -- this is a heavy gait on a machine six times the player's height, not a
        /// sprint.
        private const float RunSpeed = StrideSpeed;

        /// The stroll. Everything that is not chasing moves at this.
        ///
        /// Half speed, which the blend tree pays for with half PLAYBACK RATE rather than
        /// by blending toward Idle -- see BuildController. Moving at a fraction of a clip's
        /// authored speed while playing it at a fixed rate is the single most likely way
        /// this creature ends up skating.
        private const float WalkSpeed = RunSpeed * 0.5f;

        /// The animator parameter carrying FORWARD speed on THIS creature. Not SpeedY.
        ///
        /// AgentAnimatorDriver converts world velocity into the space of the transform its
        /// Animator sits on, and writes x to SpeedX and z to SpeedY. On the golem that
        /// Animator is the prefab root, so forward is +Z and forward speed lands in SpeedY
        /// -- which is why every other creature controller in this project blends on SpeedY.
        ///
        /// Here the Animator is on the MODEL CHILD, and that child is yawed ModelYaw so the
        /// model's own +X forward lines up with the root's +Z (see BuildPrefab). Walking
        /// forward therefore reads as +X in the Animator's space and SpeedY stays at zero.
        /// Blend on SpeedY here and the creature slides everywhere in its idle pose, with a
        /// clean console.
        private const string ForwardSpeedParameter = "SpeedX";

        /// Yaw applied to the model child so the creature faces the root's +Z. The two
        /// things that depend on it -- BuildPrefab and ForwardSpeedParameter above -- are
        /// written off this constant so the link is visible rather than remembered.
        private const float ModelYaw = -90f;

        /// Forward speed at which the creature counts as walking, and the lower speed at
        /// which it counts as stopped again. Two numbers rather than one because a single
        /// threshold flickers.
        ///
        /// SpeedX is the SIGNED forward component of velocity, and NavMeshAgentMotor turns
        /// the body toward its path at faceRotateSpeed rather than instantly. Through a
        /// sharp corner the velocity points briefly across the body, forward speed dips,
        /// and one threshold would drop to Idle and back for a few frames mid-stride.
        /// Leaving on 0.5 and returning on 0.25 costs nothing and absorbs that.
        private const float MoveEnterSpeed = 0.5f;
        private const float MoveExitSpeed = 0.25f;

        /// Crossfade from Idle into Walk. Long enough not to snap, short enough that the
        /// idle hover is not visibly mixed into the first stride -- which is the whole
        /// reason those are two states rather than two children of one blend tree.
        private const float LocomotionBlend = 0.25f;

        /// Crossfade the other way, from Walk down into Idle. Longer than LocomotionBlend
        /// on purpose, and the asymmetry is about the LEGS rather than about symmetry.
        ///
        /// Idle does not key the knees or the ankles at all -- it is the ambient hover, and
        /// with write defaults on, the legs land on the rest pose, which is the creature
        /// standing at full extension with both soles on the floor. So this crossfade is
        /// the only thing that brings a foot down out of mid-swing and gathers the stride
        /// back under the hips, and at 0.25 s it did that fast enough to read as a snap.
        ///
        /// It is also the window ConjurerCastModule waits out before it will start a cast
        /// (see its settle phase), so lengthening it lengthens the pause before the staff
        /// goes up. Both numbers are visible; this is the one that decides whether the stop
        /// looks like a stop.
        private const float WalkStopBlend = 0.35f;

        private readonly struct Clip
        {
            public readonly string Name, Take;
            public readonly int First, Last;
            public readonly bool Loop;
            public Clip(string name, string take, int first, int last, bool loop)
            {
                Name = name; Take = take; First = first; Last = last; Loop = loop;
            }
        }

        private const float Fps = 30f;

        /// Frames where a foot lands in the Walk clip.
        ///
        /// NOT eyeballed and not measured after the fact either: anim.py places the two
        /// touchdowns at whole frames on purpose (PHASE0 is a quarter cycle), and
        /// _Source~/contacts.py confirms them. They sit exactly 26 frames apart, half of
        /// the 52-frame cycle -- the check that the gait is symmetric.
        private static readonly int[] FootPlantFrames = { 14, 40 };

        /// Frames in the Walk cycle, and the one number that decides its cadence.
        ///
        /// 52, down from 72. The clip is foot-locked now (see anim.py) so its stride and
        /// its frame count TOGETHER fix the ground speed, and these are the pair that
        /// lands on StrideSpeed exactly. Change either in anim.py and stride.py will say
        /// so -- it asserts the clip's measured speed rather than reporting it.
        private const int WalkFrames = 52;

        // Frame ranges match the actions authored in anim.py. Both cycles have a last
        // frame duplicating the first, so they loop without a seam.
        private static readonly Clip[] Clips =
        {
            new Clip("Idle", "ConjurerRig|Idle", 1, 120, true),
            new Clip("Walk", "ConjurerRig|Walk", 1, WalkFrames + 1, true),
            new Clip("Attack", "ConjurerRig|Attack", 1, AttackFrames, false),
            // Sleep, Awakening and Death are NOT here. They are generated -- see
            // BuildAuthoredClips -- because the eyelid's blend shapes are the whole of the
            // first two and the point of the third, and Blender exports shape-key animation
            // as its own FBX take that Unity's clip slicer cannot reach. anim.py's header
            // says the same from the other side.
        };

        /// The Eyelid mesh's two shape keys, spelled exactly as the .blend spells them --
        /// including the capital on one and not the other. A name that does not match is not an
        /// error anywhere; it is a clip with a curve nothing is listening to.
        private const string EyeTopShape = "Top open";
        private const string EyeBottomShape = "Bottom Open";

        /// How long the eye takes to open, in seconds, and therefore the Awakening clip's
        /// length. DormantModule stands still for exactly this long after it fires the trigger,
        /// and the builder writes the number onto it rather than leaving two copies to drift.
        ///
        /// Short, because there is nothing else to the wake-up: the body does not move, so a
        /// long open is just a slow eye rather than a beat.
        private const float AwakenSeconds = 1.2f;

        /// Length of the held Sleep loop. Nothing in it changes, so this is arbitrary -- but
        /// not one frame: a zero-length clip is a division by zero in the animator's normalised
        /// time and Unity logs about it every frame.
        private const float SleepSeconds = 1f;

        // ---- the collapse ------------------------------------------------------------------
        //
        // Death is authored in this file for the same reason Sleep and Awakening are: the half
        // of it that matters is the EYE, the eye is two blend shapes, and Blender exports
        // shape-key animation as its own FBX take that Unity's clip slicer never looks at. Once
        // the clip has to be written by hand anyway, the body may as well fall in it -- see
        // WriteDeathClip, which poses the rig off the same Idle first frame the other two hold.
        //
        // It goes down in stages rather than over in one piece, and that is the whole point of
        // the timings below. A machine that simply pivots on its heels reads as a felled tree,
        // which is a thing happening TO it; a machine that loses one leg, then the other, then
        // its balance reads as something failing inside it, and it gives the player three
        // separate moments to watch instead of one:
        //
        //     0.00  power fails    the shutter judders shut, the head drops, the arms sag
        //     0.30  left knee      the leg gives out and the body drops onto it
        //     0.85  right knee     the other follows; it is kneeling, still upright
        //     1.20  teeter         a beat, held, doing nothing
        //     1.35  it goes over   backwards about the knees, accelerating
        //     1.95  impact         one damped rock; head and arms whip forward
        //     2.45  still          held, so the state has a corpse pose to sit on

        /// How long the eye takes to fail. Half of AwakenSeconds: waking up is a deliberate act
        /// and this is a power cut.
        private const float DeathShutterSeconds = 0.45f;

        /// When the first leg gives, and how long each of the two takes to fold. The first is
        /// slower because it is also the whole body's drop -- five metres of hip -- and the
        /// second is only the free leg swinging in beside it.
        private const float DeathFirstKneeDelay = 0.30f;
        private const float DeathFirstKneeSeconds = 0.55f;
        private const float DeathSecondKneeSeconds = 0.35f;

        /// The beat between kneeling and falling. It is doing nothing at all here, and that is
        /// what makes the fall land: without a pause the collapse is one continuous slump and
        /// the player never sees the kneeling silhouette that the two knee drops just built.
        private const float DeathTeeterSeconds = 0.15f;

        /// The fall itself, knees to floor, and the ring-down after it lands.
        private const float DeathFallSeconds = 0.60f;
        private const float DeathSettleSeconds = 0.50f;

        /// Corpse, held on the last frame. The Death state has no exit, so the animator sits on
        /// that pose until the despawn timer takes the body -- but the clip still has to BE this
        /// long, because the head and the arms are still whipping through the settle and a clip
        /// that ends on the impact cuts them off mid-swing.
        private const float DeathHoldSeconds = 0.50f;

        /// The phase boundaries, and the clip's length. Derived rather than typed, because three
        /// camera-shake events have to land exactly on the two knees and the body, and a second
        /// set of numbers saying when those are would drift away from the first in silence.
        private static float DeathFirstKneeDown => DeathFirstKneeDelay + DeathFirstKneeSeconds;
        private static float DeathSecondKneeDown => DeathFirstKneeDown + DeathSecondKneeSeconds;
        private static float DeathFallStart => DeathSecondKneeDown + DeathTeeterSeconds;
        private static float DeathImpactSeconds => DeathFallStart + DeathFallSeconds;
        private static float DeathSeconds =>
            DeathImpactSeconds + DeathSettleSeconds + DeathHoldSeconds;

        /// How far the thigh leans off vertical when the knee is down. This one number decides
        /// the whole kneeling pose and, through it, most of the collapse: the hips end exactly a
        /// thigh's length above the knee times its cosine, so a bigger tilt is a lower, more
        /// folded kneel and a smaller one barely bends. WriteDeathClip derives the hip drop, the
        /// support leg's fold and the topple's pivot from it rather than from any typed height.
        private const float DeathKneelTiltDegrees = 25f;

        /// How far over it goes once it is kneeling, in degrees about the model's +Z. That axis
        /// is the creature's own right-to-left line -- the model faces its own +X, see ModelYaw
        /// -- so a positive angle pitches it onto its BACK, which is the direction worth
        /// choosing: the eye has just shut, and this is what leaves it facing the sky.
        ///
        /// DERIVED from the tilt, and it has to be. The body turns about the KNEES, which are on
        /// the ground, so this angle is the one that swings the thigh from its kneeling tilt
        /// down to flat -- ninety degrees minus the tilt, exactly. Type a bigger number and the
        /// thigh keeps going past horizontal and drives the hips through the floor.
        private static float DeathToppleDegrees => 90f - DeathKneelTiltDegrees;

        /// The last of the lie-back, taken out of the spine rather than the hips.
        ///
        /// The topple can only rotate the body as far as the thigh can lie down, which leaves the
        /// torso propped a good twenty degrees off the ground. A back that arches over the folded
        /// legs is both what actually happens and the cheapest way to get the head, which is the
        /// part the player is looking at, all the way down.
        private const float DeathSpineArchDegrees = 20f;

        /// How far the far end rocks back UP off the impact. A machine this size does not
        /// bounce, but it does not stop dead either -- and the hump is SUBTRACTED from the
        /// topple, never added, because adding it drives the body through the floor.
        private const float DeathBounceDegrees = 4.5f;

        /// The shock each knee landing sends through the head and the arms, and how long it
        /// takes to die away. It is spent on those rather than on the hips deliberately: the
        /// hips are what the legs are solved against, and a body that overshoots its own
        /// kneeling height is a body whose knee goes through the floor. Nothing hangs off the
        /// head, so it can be thrown about freely -- and a head snapping down twice is what
        /// tells the player those were two separate impacts rather than one long slump.
        private const float DeathKneeShockDegrees = 9f;
        private const float DeathShockSeconds = 0.22f;

        /// Strength handed to FootstepCameraShake.OnFootPlant at each landing. Above one for the
        /// body because the shake asset is sized to a footfall and this is the whole machine at
        /// once; the knees are half of that, because a knee is about a footfall's worth of mass
        /// arriving rather harder.
        private const float DeathImpactShake = 2.2f;
        private const float DeathKneeShake = 1.1f;

        /// Head droop as the power goes, and the arms' matching flop. Body-relative and
        /// permanent: this is the pose the corpse keeps.
        private const float DeathHeadDropDegrees = 14f;
        private const float DeathArmDropDegrees = 22f;

        /// The hands are held out beside the body by nothing visible -- ArmRoot.L/R hang off the
        /// spine with no arm in between, which is the rig record's "free-floating arms" -- so
        /// whatever holds them up failing is the most legible thing that can happen to this
        /// silhouette. They sag by this fraction of their own height above the hips, and they
        /// sag straight down in MODEL space rather than in the body's, so they visibly come away
        /// from the chest as it goes over.
        private const float DeathArmSagFraction = 0.45f;

        /// Half the body's depth, as a fraction of its height -- both figures straight off the
        /// two Blender measurements at the top of this file, so this is a RATIO and carries no
        /// units to get wrong.
        ///
        /// It is most of what the machine has to rise by on the way down. A body lying on its
        /// back rests on its back, and the bone chain this clip rotates runs up the MIDDLE of it,
        /// so a topple that leaves the spine on the ground plane leaves half the creature under
        /// it -- most visibly the head, which is the widest part and the one the player is
        /// looking at.
        ///
        /// The lift comes in with the sine of the pitch, so it is nothing at all while the machine
        /// is kneeling -- the knees really are on the floor for that whole beat -- and all of it
        /// once the body is flat. The folded legs come up with it, which is what legs do when you
        /// go over backwards.
        ///
        /// It is tempting to discount the height of the knee the body turns about, on the grounds
        /// that the pivot is already a limb's thickness up. That is wrong, and measurably so: the
        /// head ends at the far end of the body from the pivot, and where it lands is set by the
        /// rotation rather than by how high the rotation started. Discounting it puts the eye
        /// nearly a metre into the ground.
        private const float DeathClearanceFraction =
            BlenderBodyWidth * 0.5f / (BlenderTop - BlenderFloor);

        /// What everything hung off the body does while the body is turning: it trails.
        ///
        /// Done by differencing the topple against ITSELF a moment earlier, weighted per part.
        /// That costs one curve evaluation, it is exactly zero whenever the body is still -- so
        /// it disturbs neither the standing pose at the top of the clip nor the corpse at the
        /// bottom -- and it whips the right way round on the impact for nothing, because a body
        /// decelerating is the same difference with the sign flipped.
        private const float DeathLagSeconds = 0.10f;
        private const float DeathHeadLag = 0.55f;

        /// Lower than it wants to be. The arms trail furthest at the moment the body stops, which
        /// is the moment they are swinging at a floor that is now right underneath them -- at 1.15
        /// the hands went two and a half metres through it on the landing frame.
        private const float DeathArmLag = 0.7f;

        /// How close a hostile gets before the eye opens.
        ///
        /// Expressed against CastRange rather than typed, and INSIDE it deliberately: a target
        /// sitting exactly on the creature's own weapon range would flicker the eye open and shut
        /// as it drifted a step either way. At four fifths there is a real margin before the
        /// creature can actually fight back.
        private const float WakeRadius = CastRange * 0.8f;

        /// Length of the Attack clip, straight off the action anim.py authors.
        ///
        /// NOT a cycle, unlike the other two: it runs neutral-to-neutral once, so whatever
        /// it hands back to has nothing to blend away.
        private const int AttackFrames = 135;

        /// The frame the bolt lands on.
        ///
        /// This and AttackFrames used to be one number -- the clip fired on its last
        /// frame, so its LENGTH was also its wind-up. They are two now, because the clip
        /// has a recoil after the strike: 135 frames long, striking at 120. Conflating
        /// them puts the lightning against the wrong frame of the animation, and that
        /// failure reads as a bug in the VFX rather than as a number nobody updated.
        ///
        /// anim.py's third beat -- the staff thrusting up and the free hand snapping out
        /// to point -- starts at frame 105 and is deliberately NOT a constant here. It
        /// used to be, because the chest charge had to converge its arcs on the aperture
        /// when the hands moved; nothing on the Unity side is keyed to it any more.
        private const int FireFrame = 120;

        /// Wind-up before the bolt lands, in seconds.
        ///
        /// Derived from FireFrame rather than typed, and deliberately NOT from the clip
        /// length: ConjurerCastModule commits on this number while the Animator plays the
        /// whole 4.5 s, and the half second between them is the recoil.
        private const float CastSeconds = FireFrame / Fps;

        /// How long before impact the aim stops following the target, in seconds.
        ///
        /// This is the dodge window, and it is the single number that decides whether the
        /// attack is fair. The strike falls out of the sky, so there is no cover and no
        /// angle to beat it with -- all the player has is this last second and their own
        /// legs, and the ring on the ground telling them which way to use them.
        ///
        /// One second against a 3.5 m blast radius asks for 3.5 m/s to clear it from dead
        /// centre, which the player's run speed covers with something to spare but their
        /// walk does not. That is the intended shape: reacting is enough, strolling is not.
        private const float CastAimLockSeconds = 1f;

        /// Measured from the START of a cast. The clip itself takes 4.5 s, so this leaves
        /// about two seconds of standing before the creature can throw again.
        private const float CastCooldown = 6.5f;

        /// Forward speed, in m/s, below which the body counts as stopped and the staff may
        /// go up.
        ///
        /// The creature decides to cast at CastRange while it is still running, and a
        /// NavMeshAgent does not stop on the frame you ask it to -- it coasts down at
        /// `acceleration`. Firing the trigger on the decision frame plants the feet in the
        /// attack pose and skates the whole braking distance. So the module settles first,
        /// and this is the threshold it settles TO.
        ///
        /// Not zero, deliberately: a NavMeshAgent's velocity converges on zero without ever
        /// arriving, so a zero threshold would always fall through to the timeout instead.
        /// Below MoveExitSpeed, so the animator has already dropped out of Walk into Idle by
        /// the time the cast starts -- the picture and the decision agree about "stopped".
        private const float CastSettleSpeed = 0.2f;

        /// Longest the creature will stand there braking before casting anyway, in seconds.
        ///
        /// A ceiling, not a target. Three things have to fit under it: the run-down (about
        /// a second at WireMotor's acceleration), then the rest of the stride the body
        /// stopped in the middle of -- the settle holds the cadence and releases on a
        /// FootPlantFrame, so at worst half a cycle, and at the stroll's half rate that half
        /// cycle is 0.87 s -- and then WalkStopBlend's 0.35 s on the end. In the ordinary
        /// case the settle finishes well inside this.
        ///
        /// It is here for the cases that never converge -- wedged against geometry, shoved
        /// by a vehicle, standing on something that is itself moving -- because a conjurer
        /// that answers those by never attacking again is a far worse bug than the
        /// occasional short skate.
        private const float CastSettleTimeout = 5f;

        /// The animator state the creature has to be STANDING in before the staff goes up.
        ///
        /// Named here rather than left to the module's own default because it has to match
        /// the state this builder creates, and the two files are edited apart. Rename the
        /// Idle state below and this is what has to move with it -- the symptom otherwise
        /// is a creature that pauses for the full CastSettleTimeout before every cast and
        /// says nothing about why.
        private const string CastSettledState = "Idle";

        /// Thickness of the fired bolt's sweep, in metres.
        ///
        /// Between the strike ribbon's own 0.6 m top and 1.4 m tail, so the volume that
        /// gets billed is about the volume that gets drawn. A thinner sweep than the
        /// picture slips through gaps the player can plainly see it should not.
        private const float BeamRadius = 0.6f;

        /// What the bolt takes off, in whole hit points.
        ///
        /// A tenth of what the player's own LightningSpell hits for, and the gap is the
        /// point rather than an oversight. They fire the same LightningStrike, but the
        /// player throws theirs on demand at whatever they are looking at, while this one
        /// announces itself three seconds ahead and lands where you WERE. A telegraphed,
        /// dodgeable attack that still took a third of your health would make the telegraph
        /// pointless -- you would fight the cooldown rather than the creature.
        private const int CastDamage = 10;

        /// What it takes to put the machine down.
        ///
        /// Deliberately low for something this size -- a fifth of a rock golem's 420, and less
        /// than a dune rat's 90 buys after the rat's fur. The creature's defence is the range it
        /// fights at, not the hits it can absorb: it opens at 28 m and drops bolts on where you
        /// stood, so the fight is decided by whether you can close the distance at all. A health
        /// pool sized off its silhouette instead would make that closing run the whole encounter.
        private const int MaxHealth = 100;

        /// How far above the impact point the bolt is DRAWN from.
        ///
        /// Presentation only -- LightningStrike.Damage is always billed at the ground point,
        /// so this cannot move where the attack hurts. It decides how much sky the bolt
        /// falls through before it lands.
        ///
        /// Set here rather than left to the module's serialized default, which is the 10 m
        /// the player's LightningSpell uses. A field the builder does not write is a field
        /// that silently reverts on the next rebuild.
        private const float DrawHeight = 100f;

        /// How wide the strike bites, measured from where the bolt earths.
        ///
        /// Unchanged from the player's spell, and it is the number doing the work now that
        /// the damage is small: what makes the attack matter is being forced to move, not
        /// what it costs when it connects.
        private const float CastBlastRadius = 3.5f;

        /// One event per footfall, at the frame the contact actually lands.
        /// AnimationEvent.time is seconds from the clip start, and the clip starts at
        /// frame 1, hence (frame - 1) / fps.
        ///
        /// Back on the clip, where they belong again. While the legs were procedural the
        /// cadence changed with speed and terrain and a baked event's could not, so the
        /// shake was driven off the gait's own swing-to-stance edge instead. A baked walk
        /// has a fixed cadence by definition, so the clip is once more the only thing that
        /// knows when a foot lands.
        /// The two footfalls, as fractions of the cycle.
        ///
        /// NORMALIZED, 0-1, not seconds. ModelImporterClipAnimation.events is the one place
        /// in this file that measures time that way, and getting it wrong does far more than
        /// misplace a camera shake: an event past 1 EXTENDS THE CLIP to reach it, and the
        /// clip pads the gap by holding its last keyframe.
        ///
        /// This was `(f - 1) / Fps`, seconds, and the second plant landed at 1.3. The walk
        /// imported 2.25 s long against 1.70 s of actual keys, so the last quarter of every
        /// cycle was one frozen frame -- with a foot off the ground, because frame 52 is the
        /// passing pose. The creature glided through a fifth of a second with a leg hanging
        /// in the air, every stride, and it read as the animation pausing whenever the body
        /// slowed down enough to see it.
        ///
        /// Divided by WalkFrames because that is Last - First for this clip: frame f sits
        /// (f - 1) frames into a cycle WalkFrames long.
        private static AnimationEvent[] FootPlantEvents()
        {
            return FootPlantFrames.Select(f => new AnimationEvent
            {
                time = (f - 1) / (float)WalkFrames,
                functionName = "OnFootPlant",
                floatParameter = 1f,
            }).ToArray();
        }

        /// A heavier, shorter shake than DamageShake: a footfall is a single vertical
        /// jolt that dies quickly, not the sustained rattle of taking a hit.
        private static void BuildShakeData()
        {
            EnsureFolder(ShakeDir);
            if (AssetDatabase.LoadAssetAtPath<ShakeData>(ShakeDataPath) != null) return;

            ShakeData data = ScriptableObject.CreateInstance<ShakeData>();
            AssetDatabase.CreateAsset(data, ShakeDataPath);

            var so = new SerializedObject(data);
            void F(string n, float v) { so.FindProperty(n).floatValue = v; }
            F("_totalDuration", 0.45f);
            F("_fadeInDuration", 0f);
            F("_fadeOutDuration", 0.35f);
            F("_magnitude", 0.8f);
            F("_magnitudeNoise", 0.15f);
            F("_roughness", 12f);
            F("_roughnessNoise", 0.2f);
            // Mostly a vertical thump, with a little roll so it does not read as a
            // pure elevator drop.
            so.FindProperty("_positionalInfluence").vector3Value = new Vector3(0.25f, 1f, 0.25f);
            so.FindProperty("_rotationalInfluence").vector3Value = new Vector3(0.3f, 0f, 0.6f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private static void SetFloatArray(SerializedObject so, string field, float[] values)
        {
            SerializedProperty prop = so.FindProperty(field);
            prop.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).floatValue = values[i];
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// The prefab and its controller only, leaving the test scene alone.
        ///
        /// `Build` opens and saves "Marius test scene" so it can refresh the instance in it, which
        /// is right when the MODEL has changed and wrong when only a tuning number has: it closes
        /// whatever scene you were working in to do it. This is the same prefab, built the same way,
        /// for the far more common case.
        ///
        /// Same caveat as `Build`: SaveAsPrefabAsset replaces the asset file wholesale, so the save
        /// id goes with it and Tools > Save System > Wire Saveable Prefabs has to be run afterwards.
        [MenuItem("Tools/Creatures/Build Lightning Conjurer (prefab only)")]
        public static void BuildPrefabOnly()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"[LightningConjurer] No FBX at {Fbx}.");
                return;
            }

            BuildAuthoredClips();
            BuildPrefab(BuildController());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LightningConjurer] Prefab rebuilt; test scene left untouched.");
        }

        [MenuItem("Tools/Creatures/Build Lightning Conjurer")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"[LightningConjurer] No FBX at {Fbx}. " +
                               "Re-export it from the .blend first.");
                return;
            }

            BuildMaterials();
            EnsureFolder("Assets/Game/Art/Materials/Creatures");
            BuildWeatheredMaterial();
            // After the materials: the core takes Mat_Emissive_Portal_Blue, which
            // BuildMaterials is what creates.
            BuildStaffCharge();
            if (GroundWarning) BuildStrikeTelegraph();
            BuildShakeData();
            ConfigureImporter();
            // After the importer, before the controller: it samples the imported Idle clip for
            // its held pose, so it needs the FBX's clips to exist and the controller needs its
            // output to exist.
            BuildAuthoredClips();
            AnimatorController controller = BuildController();
            GameObject prefab = BuildPrefab(controller);
            AddToTestScene(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // An EMPTY blend tree is the failure worth catching. That is what an FBX whose
            // clips did not import produces, and unlike a state with no motion at all it is
            // invisible in the inspector until something plays it: the states exist, the
            // tree exists, and the creature simply stands in its bind pose forever.
            AnimatorState[] states = controller.layers[0].stateMachine.states
                .Select(c => c.state).ToArray();
            AnimatorState idleState = states.FirstOrDefault(s => s.name == "Idle");
            AnimatorState walkState = states.FirstOrDefault(s => s.name == "Walk");
            AnimatorState attackState = states.FirstOrDefault(s => s.name == "Attack");
            AnimatorState sleepState = states.FirstOrDefault(s => s.name == "Sleep");
            AnimatorState awakenState = states.FirstOrDefault(s => s.name == "Awakening");
            AnimatorState deathState = states.FirstOrDefault(s => s.name == "Death");
            var tree = walkState?.motion as BlendTree;
            if (idleState == null || idleState.motion == null ||
                attackState == null || attackState.motion == null || tree == null ||
                tree.children.Length != 2 || tree.children.Any(c => c.motion == null))
            {
                Debug.LogError("[LightningConjurer] Controller is missing a populated Idle " +
                               "state, Attack state or Walk cadence tree - not reporting " +
                               "success. The FBX's clips are missing; re-run " +
                               "_Source~/anim.py and re-export.");
                return;
            }

            // Separately, and just as fatal: the ENTRY state is Sleep. An unpopulated one is a
            // creature that spawns frozen in its bind pose and never leaves it, which looks
            // nothing like the missing-FBX failure above and has a different cause.
            if (sleepState == null || sleepState.motion == null ||
                awakenState == null || awakenState.motion == null)
            {
                Debug.LogError("[LightningConjurer] Controller has no populated Sleep or " +
                               "Awakening state - not reporting success. BuildAuthoredClips " +
                               "failed to write them, so the creature would spawn into an " +
                               "empty entry state and never move again.");
                return;
            }

            // And the same check on Death, which fails differently again: the creature dies
            // correctly in every respect -- the sound, the loot, the despawn -- and simply never
            // falls over, which reads as the trigger not being sent rather than as a missing clip.
            if (deathState == null || deathState.motion == null)
            {
                Debug.LogError("[LightningConjurer] Controller has no populated Death state - " +
                               "not reporting success. WriteDeathClip failed to write the clip, " +
                               "so a killed conjurer would stand where it died until it faded.");
                return;
            }

            Debug.Log($"[LightningConjurer] Built. Height {TargetHeight:0.00} m " +
                      $"(scale {Scale:0.0000}); walks its baked clip on a NavMeshAgent at " +
                      $"{WalkSpeed:0.00} m/s, runs at {RunSpeed:0.00} m/s, clip authored at " +
                      $"{StrideSpeed:0.00} m/s.");
        }

        /// Builds the staff charge prefab: an emissive core at the emitter, a point
        /// light, and a handful of arcs that ConjurerStaffCharge re-points between the
        /// emitter and the turbine below it, turning more and more of them skyward as
        /// the wind-up runs.
        ///
        /// The arcs are LightningBoltEffect, the same component the strike uses, with
        /// `duration` set to ZERO. That component reads a non-positive duration as
        /// "do not destroy yourself" (see its Update), which turns a one-shot bolt
        /// into a persistent arc that keeps re-kinking -- exactly what a four-second
        /// charge needs, and without churning hundreds of instances through Instantiate.
        ///
        /// The arc material is taken off the strike prefab rather than named here,
        /// so the charge and the bolt it becomes cannot drift apart.
        private static GameObject BuildStaffCharge()
        {
            Material arcMat = ArcMaterial();

            var root = new GameObject("ConjurerStaffCharge");

            // No emissive sphere. It used to swell at the emitter through the wind-up,
            // carried over from the chest charge where a ball growing inside a ring was
            // the whole picture; on the end of a staff it read as a blue balloon on a
            // stick and it hid the turbine, which is the part that actually tells the
            // player what is happening. The light below does the lighting the sphere was
            // really there for, without drawing a shape.
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            Light glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.18f, 0.72f, 1f);
            glow.range = 14f;      // it is up in the air now, not down in a chest
            glow.intensity = 1f;
            glow.shadows = LightShadows.None;   // 5 s at a time, on a moving staff

            var arcs = new LightningBoltEffect[ChargeArcCount];
            for (int i = 0; i < ChargeArcCount; i++)
            {
                var go = new GameObject($"Arc{i}");
                go.transform.SetParent(root.transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = arcMat;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                var fx = go.AddComponent<LightningBoltEffect>();
                var fso = new SerializedObject(fx);
                SetProp(fso, "line", lr);
                // More segments than the chest charge had: these arcs are metres long
                // rather than centimetres once they start reaching for the sky, and ten
                // kinks over sixteen metres reads as a folded wire.
                SetInt(fso, "segments", 18);
                SetFloat(fso, "spread", 0.22f);
                SetFloat(fso, "maxOffset", 0.35f);
                SetFloat(fso, "restrikeRate", 30f);
                SetFloat(fso, "duration", 0f);        // persist; see the summary above
                SetFloat(fso, "startWidth", ChargeArcWidth);
                SetFloat(fso, "endWidth", ChargeArcWidth);
                SetFloat(fso, "fallbackDrop", 0f);
                fso.ApplyModifiedPropertiesWithoutUndo();

                arcs[i] = fx;
            }

            var charge = root.AddComponent<ConjurerStaffCharge>();
            var bso = new SerializedObject(charge);
            SetProp(bso, "glow", glow);
            // Derived, like everything else timed off the clip: the glow peaks exactly as
            // the bolt lands.
            SetFloat(bso, "chargeSeconds", CastSeconds);
            SetFloat(bso, "fanRadius", ChargeFanRadius);
            SetFloat(bso, "fanDrop", ChargeFanDrop);
            SetFloat(bso, "skyReach", ChargeSkyReach);

            SerializedProperty arr = Find(bso, "arcs");
            if (arr != null)
            {
                arr.arraySize = arcs.Length;
                for (int i = 0; i < arcs.Length; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = arcs[i];
            }
            bso.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ChargeVfxPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// The material both the arcs and the ground warning are drawn with.
        ///
        /// Lifted off the strike prefab's own LineRenderer rather than named here, so the
        /// wind-up, the warning and the bolt they resolve into cannot drift apart. If the
        /// bolt is ever restyled, all three follow.
        private static Material ArcMaterial()
        {
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(LightningVfxPath);
            var line = bolt != null ? bolt.GetComponentInChildren<LineRenderer>(true) : null;
            Material mat = line != null ? line.sharedMaterial : null;

            if (mat == null)
                Debug.LogWarning("[LightningConjurer] No material on the strike prefab; " +
                                 "the charge arcs and the ground warning will draw " +
                                 "untextured.");
            return mat;
        }

        /// Builds the ground warning prefab: a ring at the blast radius, a column of glow
        /// descending onto it, and a light.
        ///
        /// This is the player's entire counterplay against the sky strike, which cannot be
        /// blocked or dodged by angle -- see ConjurerCastModule's header for why it is not
        /// really optional. It is generated here rather than authored so that its ring is
        /// always the blast radius: a hand-made warning that says 3 m while the blast bills
        /// 3.5 m teaches the player something false, and the first time they learn it is by
        /// dying just outside a ring they had cleared.
        private static GameObject BuildStrikeTelegraph()
        {
            var root = new GameObject("ConjurerStrikeWarning");

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(root.transform, false);
            var mf = ringGo.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildRingMesh();
            var mr = ringGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArcMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // A stretched cylinder, and the collider that comes with the primitive has to
            // go: the blast's own OverlapSphere runs at this exact point, and a 55 m
            // capsule standing on it would be the first thing every strike found.
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "Column";
            Object.DestroyImmediate(column.GetComponent<Collider>());
            column.transform.SetParent(root.transform, false);
            var cr = column.GetComponent<MeshRenderer>();
            cr.sharedMaterial = ArcMaterial();
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cr.receiveShadows = false;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            Light glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.25f, 0.7f, 1f);
            glow.range = 12f;
            glow.intensity = 1.5f;
            glow.shadows = LightShadows.None;

            var tel = root.AddComponent<StrikeTelegraph>();
            var tso = new SerializedObject(tel);
            SetProp(tso, "ring", ringGo.transform);
            SetProp(tso, "column", column.transform);
            SetProp(tso, "glow", glow);
            SetFloat(tso, "warningSeconds", CastSeconds);
            SetFloat(tso, "radius", CastBlastRadius);
            SetFloat(tso, "columnHeight", TelegraphColumnHeight);
            SetFloat(tso, "groundProbe", TelegraphGroundProbe);
            tso.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TelegraphVfxPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// A flat annulus in the XZ plane, outer radius 1, saved as a mesh asset.
        ///
        /// Radius 1 so StrikeTelegraph can scale it to whatever blast radius it is handed
        /// without the mesh needing to be rebuilt, and flat in XZ so that scaling is a
        /// plain (r, 1, r) and never distorts the band's width.
        ///
        /// Double-sided, by emitting each quad twice with opposite winding. It lies within
        /// a few centimetres of the ground and the player's camera can end up under it on
        /// a slope or a rise; a single-sided ring simply vanishes from those angles, which
        /// is the one thing a warning must never do.
        private static Mesh BuildRingMesh()
        {
            int n = RingSegments;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i] = new Vector3(c, 0f, s);                                  // outer
                verts[n + i] = new Vector3(c, 0f, s) * (1f - RingThickness);       // inner
                uvs[i] = new Vector2(i / (float)n, 1f);
                uvs[n + i] = new Vector2(i / (float)n, 0f);
            }

            var tris = new int[n * 12];
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int o0 = i, o1 = j, i0 = n + i, i1 = n + j;

                tris[t++] = o0; tris[t++] = i0; tris[t++] = o1;
                tris[t++] = o1; tris[t++] = i0; tris[t++] = i1;

                tris[t++] = o1; tris[t++] = i0; tris[t++] = o0;   // and the same, reversed
                tris[t++] = i1; tris[t++] = i0; tris[t++] = o1;
            }

            EnsureFolder("Assets/Game/Art/Models/Generated");

            // Rewritten IN PLACE when it already exists, rather than replaced. CreateAsset
            // over a live asset mints a new object and breaks every pointer to the old one,
            // so the warning prefab saved by a previous run would come back from a rebuild
            // with a missing mesh -- a ring that is simply not drawn, which is the one
            // failure this effect must not have.
            //
            // Building the arrays first and only then deciding is what keeps this from
            // leaking: `new Mesh()` up front would be an orphaned object on the update
            // path, and Unity does not collect those.
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh();
            else mesh.Clear();

            mesh.name = "StrikeWarningRing";
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (fresh) AssetDatabase.CreateAsset(mesh, RingMeshPath);
            else EditorUtility.SetDirty(mesh);

            return mesh;
        }

        /// The one material the weathered body wears.
        ///
        /// Its colour is not in here. rustify.py bakes a khaki -> grey -> verdigris
        /// ramp into the mesh as a per-vertex colour attribute, and
        /// SpaceGame/ConjurerWeathered reads that as base colour -- which is the
        /// whole reason a custom shader exists, since URP/Lit ignores vertex colour
        /// entirely. The GPU interpolating that attribute across each triangle is
        /// where the gradients come from; assigning palette materials per object or
        /// per face, which is what this used to do, can only ever produce hard steps
        /// at polygon edges.
        ///
        /// Vertex ALPHA carries how corroded each point is and drives metallic and
        /// smoothness together, so the numbers below are the two ENDS of that range
        /// rather than one surface.
        private static Material BuildWeatheredMaterial()
        {
            Shader shader = Shader.Find(WeatheredShader);
            if (shader == null)
            {
                Debug.LogError($"[LightningConjurer] Shader '{WeatheredShader}' not " +
                               "found. The body will import untextured; check " +
                               "Assets/Game/Art/Shaders/ConjurerWeathered.shader " +
                               "compiled.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(WeatheredMatPath);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);
            else mat.shader = shader;

            mat.SetColor("_BaseColor", Color.white);   // tint only; mesh owns the colour

            // Dry, bare metal at one end; oxide at the other. Corrosion is not a
            // conductor, so metallic collapses as weathering rises.
            // Held well below a bare-metal 1.0 even at the dry end. A fully
            // metallic surface has no diffuse response at all, so with only a
            // sun and a dim sky to reflect it reads as near-black -- which is
            // exactly how the first build of this looked.
            mat.SetFloat("_Metallic", 0.45f);
            mat.SetFloat("_MetallicWeathered", 0.08f);
            mat.SetFloat("_Smoothness", 0.40f);
            mat.SetFloat("_SmoothnessWeathered", 0.10f);

            // Detail finer than the mesh can carry. Vertex spacing on this model is
            // about 0.09 m, so anything above ~11 cycles/m has to come from here.
            mat.SetFloat("_GrungeScale", 6.0f);
            mat.SetFloat("_GrungeAmount", 0.22f);
            mat.SetFloat("_GrungeContrast", 1.6f);

            // Runs travel downward. Weathering with no vertical bias reads as
            // camouflage rather than as age.
            mat.SetFloat("_StreakScale", 2.5f);
            mat.SetFloat("_StreakStretch", 7.0f);
            mat.SetFloat("_StreakAmount", 0.18f);

            if (isNew) AssetDatabase.CreateAsset(mat, WeatheredMatPath);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// Creates or updates a URP material per palette entry, in a shared folder
        /// so a later model using the same palette entry reuses the asset rather
        /// than minting a second copy of the same grey.
        private static void BuildMaterials()
        {
            EnsureFolder(MaterialDir);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[LightningConjurer] URP Lit shader not found.");
                return;
            }

            foreach (Pal p in Palette)
            {
                string path = $"{MaterialDir}/{p.Name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew) mat = new Material(lit);
                else mat.shader = lit;

                mat.SetColor("_BaseColor", p.Colour);
                mat.SetFloat("_Metallic", p.Metallic);
                mat.SetFloat("_Smoothness", 1f - p.Roughness);   // URP is smoothness, palette is roughness

                if (p.Emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    mat.SetColor("_EmissionColor", p.Colour * p.Emission);
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    mat.SetColor("_EmissionColor", Color.black);
                }

                if (isNew) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid. The conjurer is a two-legged sphere with two
            // detached, free-floating arms and no torso or spine to speak of;
            // there is no humanoid bone map that survives contact with it.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importNormals = ModelImporterNormals.Import;

            // The FBX is written in Blender's own Z-up axes and Unity is asked to
            // bake the Z-up -> Y-up conversion into the data.
            //
            // This is load-bearing. Every part of this model is a rigid mesh
            // bone-parented to the skeleton rather than skinned, and for that kind
            // of rig Unity discards the armature node's own transform. Putting the
            // conversion (or the metre scale) on the armature in Blender therefore
            // survives in the animation curves but vanishes from the bind pose --
            // the creature stands correctly only while a clip is playing and
            // collapses the moment one stops. GolemBuilder hit exactly this and
            // documents it; the export script leaves the armature at identity for
            // the same reason.
            importer.bakeAxisConversion = true;

            // Metre scale belongs here, for the same reason: globalScale is applied
            // to the bind pose and the curves alike. Unit conversion stays ON and
            // the scale factor rides on top of it -- the combination the rest of the
            // project's models use (ostrich_rigged imports at globalScale 0.13742
            // with useFileUnits 1).
            importer.useFileScale = true;
            importer.globalScale = Scale;

            // 52 separate parts bone-parented to the skeleton, so they exist as real
            // child transforms. Optimising the hierarchy away would delete the very
            // transforms the clips animate and the creature would import as a
            // motionless pile of components.
            importer.optimizeGameObjects = false;
            importer.optimizeBones = false;

            importer.clipAnimations = Clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.Name,
                takeName = c.Take,
                // FootstepCameraShake.OnFootPlant, twice per cycle. Only on Walk: the
                // Idle clip never puts a foot down.
                events = c.Name == "Walk" ? FootPlantEvents() : new AnimationEvent[0],
                firstFrame = c.First,
                lastFrame = c.Last,
                // Idle and Walk are cycles; Attack is not. Marking a one-shot as looping costs
                // nothing while the exit transition works and hides a spin-forever bug the
                // moment it does not.
                loopTime = c.Loop,
                loopPose = c.Loop,
                wrapMode = WrapMode.Loop,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
                keepOriginalOrientation = true,
                lockRootRotation = true,
                lockRootHeightY = true,
                lockRootPositionXZ = true,
            }).ToArray();

            // Point every material slot in the FBX at the authored URP asset. The
            // key is the material NAME as Blender wrote it, which is the palette
            // name because the .blend links its materials straight from
            // palette.blend rather than making local copies.
            int remapped = 0;
            foreach (Pal p in Palette)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{p.Name}.mat");
                if (mat == null) continue;
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), p.Name), mat);
                remapped++;
            }

            // The body's own material, which is not a palette entry. Almost every
            // mesh in the FBX now references this one name -- rustify.py collapsed
            // the per-face submeshes back into a single material once the colour
            // moved into the vertex attribute -- so missing this remap leaves the
            // whole creature on an imported stand-in that ignores vertex colour and
            // renders flat white.
            var weathered = AssetDatabase.LoadAssetAtPath<Material>(WeatheredMatPath);
            if (weathered != null)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material),
                                                            "Mat_Weathered_Blend"),
                    weathered);
                remapped++;
            }

            Debug.Log($"[LightningConjurer] Remapped {remapped} materials.");

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // SaveAndReimport does not guarantee the clips are queryable by the time
            // it returns. Without this the very next LoadAllAssetsAtPath can come
            // back with no AnimationClips at all, the blend tree gets no motions,
            // and the build finishes "successfully" with an empty controller --
            // which is exactly what happened on the second run of this builder.
            AssetDatabase.ImportAsset(
                Fbx, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        /// Authors the Sleep, Awakening and Death clips, which the FBX cannot carry.
        ///
        /// WHY THESE ARE GENERATED AND THE OTHER THREE ARE NOT. The only thing that animates in
        /// the first two is the Eyelid's two blend shapes, and Blender exports shape-key animation
        /// as its OWN FBX take -- "Key|ConjurerRig|Idle" and friends -- one per (object, action)
        /// pair. Unity's clip slicer reads takes by name and never looks at those, which is why
        /// every clip in this creature's FBX carries a frozen copy of the lid and none of them
        /// can move it. Authoring the two clips here sidesteps the whole problem, and it buys
        /// something else worth having: the sleeping pose is Idle's own first frame, sampled,
        /// so the hand-off out of Awakening into Idle is exactly a no-op on every bone.
        ///
        /// The body curves are held FLAT rather than left out. A clip with no curve for a bone
        /// is not a clip that holds the bone still -- the states here run with write-defaults
        /// OFF (see BuildController), so an unwritten bone keeps whatever the last state left
        /// on it, and a creature that fell asleep would sleep in its last walk pose.
        private static void BuildAuthoredClips()
        {
            AnimationClip idle = FindClip("Idle");

            // Path from the Animator's own transform. The Animator lives on the model child,
            // which IS the FBX root, so a path computed against the imported asset is the path
            // the clip needs -- no instantiation required.
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            string eyelidPath = null;
            foreach (SkinnedMeshRenderer smr in
                     source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null ||
                    smr.sharedMesh.GetBlendShapeIndex(EyeTopShape) < 0) continue;
                eyelidPath = AnimationUtility.CalculateTransformPath(smr.transform,
                                                                    source.transform);
                break;
            }

            if (eyelidPath == null)
            {
                // Loud rather than fatal: the creature still sleeps and wakes on schedule, it
                // just never blinks, and that is a much harder thing to diagnose from the
                // symptom than from this line.
                Debug.LogWarning(
                    "[LightningConjurer] No renderer in the FBX carries a " +
                    $"'{EyeTopShape}' blend shape, so Sleep and Awakening will hold the pose " +
                    "with the eye already open. The usual cause is export.py failing to bake " +
                    "the Eyelid's Solidify into its shape keys -- Blender's FBX exporter drops " +
                    "shape keys off any mesh it has to evaluate.");
            }

            // The IMPORTED clips carry the lid too, and this is the check that they carry it
            // OPEN. Blender bakes each shape key's export-time value into every animation stack
            // as a constant channel, and Unity reads those onto the armature take -- so Idle,
            // Walk and Attack all animate the eyelid whether anyone meant them to or not. If
            // that constant is 0, the frame after Awakening finishes is the frame Idle shuts
            // the eye again, and no amount of write-defaults fiddling on this side can outvote
            // a curve. The fix is one line in export.py; see EXPORT_OPEN there.
            if (eyelidPath != null)
            {
                var held = new EditorCurveBinding();
                bool found = false;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(idle))
                {
                    if (b.path != eyelidPath ||
                        b.propertyName != $"blendShape.{EyeTopShape}") continue;
                    held = b;
                    found = true;
                    break;
                }

                // A MISSING curve is just as wrong as a shut one, and quieter. States run with
                // write defaults on, so a clip that does not animate the lid hands it back to
                // the prefab's own weight -- which is the imported mesh's 0, the closed lid.
                float lid = found ? AnimationUtility.GetEditorCurve(idle, held).Evaluate(0f) : -1f;
                if (lid < 99f)
                {
                    Debug.LogError(
                        $"[LightningConjurer] The imported Idle clip leaves '{EyeTopShape}' at " +
                        (found ? $"{lid:0}" : "no curve at all") + " rather than 100, so the " +
                        "creature will shut its eye the instant it finishes waking up. Re-export " +
                        "with export.py's EXPORT_OPEN carrying both lid shape keys.");
                }
            }

            EnsureFolder(ControllerDir);
            WriteEyeClip(SleepClipPath, idle, eyelidPath, SleepSeconds, 0f, 0f, loop: true);
            WriteEyeClip(AwakenClipPath, idle, eyelidPath, AwakenSeconds, 0f, 1f, loop: false);
            WriteDeathClip(DeathClipPath, idle, eyelidPath);
        }

        /// One held-pose clip, with the lid driven from `from` to `to` across its length.
        private static void WriteEyeClip(string path, AnimationClip pose, string eyelidPath,
                                         float length, float from, float to, bool loop)
        {
            var clip = new AnimationClip { frameRate = Fps };

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(pose))
            {
                float held = AnimationUtility.GetEditorCurve(pose, binding).Evaluate(0f);
                AnimationUtility.SetEditorCurve(
                    clip, binding,
                    new AnimationCurve(new Keyframe(0f, held), new Keyframe(length, held)));
            }

            if (eyelidPath != null)
            {
                // Staggered, not moved together: the top lifts first and the bottom follows a
                // third of the way in. They are separate shape keys precisely so this costs
                // nothing, and a shutter whose halves part in lockstep reads as one object
                // splitting rather than as an eye opening.
                Lid(clip, eyelidPath, EyeTopShape, length, from, to, 0f, 0.7f);
                Lid(clip, eyelidPath, EyeBottomShape, length, from, to, 0.3f, 1f);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
        }

        /// One blend-shape curve, weighted 0..100, over the [begin, end] slice of the clip.
        private static void Lid(AnimationClip clip, string eyelidPath, string shape,
                                float length, float from, float to, float begin, float end)
        {
            var curve = new AnimationCurve();
            if (Mathf.Approximately(from, to))
            {
                curve.AddKey(0f, from * 100f);
                curve.AddKey(length, from * 100f);
            }
            else
            {
                curve = AnimationCurve.EaseInOut(begin * length, from * 100f,
                                                 end * length, to * 100f);
                // Flat outside the slice, or the eased segment extrapolates and the lid
                // overshoots past shut on the way in.
                if (begin > 0f) curve.AddKey(new Keyframe(0f, from * 100f));
                if (end < 1f) curve.AddKey(new Keyframe(length, to * 100f));
            }

            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(eyelidPath, typeof(SkinnedMeshRenderer),
                                              $"blendShape.{shape}"),
                curve);
        }


        /// How far one knee has folded at `t`, 0 to 1.
        ///
        /// Ease IN and nothing else. A leg that gives out does not lower the body -- it holds,
        /// and then it does not, and the fastest part of the motion is the frame before the knee
        /// hits the ground. Any ease at the bottom reads as the machine kneeling on purpose.
        private static float DeathKnee(float t, float begin, float seconds)
        {
            float u = Mathf.Clamp01((t - begin) / seconds);
            return 1f - Mathf.Cos(u * Mathf.PI * 0.5f);
        }

        /// The body's pitch, in degrees, at `t` seconds into the collapse. Zero until both knees
        /// are down and the teeter is over: this creature falls FROM a kneel, not from standing.
        private static float DeathTopple(float t)
        {
            if (t <= DeathFallStart) return 0f;

            if (t < DeathImpactSeconds)
            {
                float u = (t - DeathFallStart) / DeathFallSeconds;
                // Ease in, again, and for the same reason: this is gravity rather than a move.
                return DeathToppleDegrees * (1f - Mathf.Cos(u * Mathf.PI * 0.5f));
            }

            float s = (t - DeathImpactSeconds) / DeathSettleSeconds;
            if (s >= 1f) return DeathToppleDegrees;

            // Two decaying humps rather than a sine: |sin| never goes negative, so the far end
            // lifts off the floor and comes back down twice, and at no point in the ring-down
            // does the body rock past flat and into the ground.
            return DeathToppleDegrees
                 - DeathBounceDegrees * Mathf.Abs(Mathf.Sin(s * Mathf.PI * 2f)) * (1f - s);
        }

        /// The kick the head and arms take when something lands, at `t`. One hump per knee, both
        /// gone by the time the body itself goes over.
        private static float DeathShock(float t)
        {
            return Hump(t - DeathFirstKneeDown) + Hump(t - DeathSecondKneeDown);

            static float Hump(float since)
            {
                float s = since / DeathShockSeconds;
                if (s <= 0f || s >= 1f) return 0f;
                return Mathf.Sin(s * Mathf.PI) * (1f - s);
            }
        }

        /// The shutter's travel while the power fails, as fractions of DeathShutterSeconds
        /// against fractions of open -- 1 is wide open, the weight Awakening leaves behind, and
        /// 0 is shut.
        ///
        /// It judders rather than sweeping. A clean sweep is the motion this creature makes
        /// going to SLEEP, and the two moments have to be told apart at a glance: one is a
        /// machine deciding to close its eye, and this one is the eye closing on it.
        private static readonly float[] DeathLidTimes = { 0f, 0.14f, 0.24f, 0.45f, 0.56f, 0.80f };
        private static readonly float[] DeathLidOpen = { 1f, 0.52f, 0.66f, 0.20f, 0.31f, 0f };

        /// One leg, measured off the standing pose and posed in the model's sagittal plane.
        ///
        /// Everything about this rig's legs is planar: the model faces its own +X with +Y up,
        /// the two legs are separated along +Z, and every joint the collapse touches turns about
        /// +Z. So a leg is two lengths and three angles, and posing one is trigonometry rather
        /// than a solver -- which is why there is no IK package anywhere near this file.
        private struct DeathLeg
        {
            public string Hip, Knee, Ankle;      // the three bones that turn
            public Vector2 HipRest, AnkleRest;   // model XY, standing
            public Vector2 AnkleKneel;           // model XY, once the knee is down
            public float Thigh, Shin;            // segment lengths
            public float ThighRest, ShinRest;    // model-space segment directions, degrees
        }

        /// Where the thigh and the shin have to point for the ankle to land on `target`.
        ///
        /// The two-link planar case, which has a closed form. The only judgement in it is which
        /// way the knee bends, and that is not a guess: sampling the Walk clip puts the knee on
        /// the +X side of the hip-to-ankle line on every frame of the cycle, so `+ spread` is
        /// this creature's knee and `- spread` is that knee inverted.
        private static void DeathReach(in DeathLeg leg, Vector2 hip, Vector2 target,
                                       out float thigh, out float shin)
        {
            Vector2 span = target - hip;

            // Clamped just inside both limits. Dead on the outer one the spread is an acos of
            // exactly 1, which is fine, but a hair past it is an acos of 1.0000001, which is NaN
            // -- and a NaN in a rotation curve poisons every frame after it.
            float reach = Mathf.Clamp(span.magnitude,
                                      Mathf.Abs(leg.Thigh - leg.Shin) + 1e-6f,
                                      leg.Thigh + leg.Shin - 1e-6f);

            float toTarget = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;
            float spread = Mathf.Acos(Mathf.Clamp(
                (leg.Thigh * leg.Thigh + reach * reach - leg.Shin * leg.Shin)
                / (2f * leg.Thigh * reach), -1f, 1f)) * Mathf.Rad2Deg;

            thigh = toTarget + spread;

            Vector2 knee = hip + leg.Thigh * new Vector2(
                Mathf.Cos(thigh * Mathf.Deg2Rad), Mathf.Sin(thigh * Mathf.Deg2Rad));
            shin = Mathf.Atan2(target.y - knee.y, target.x - knee.x) * Mathf.Rad2Deg;
        }

        /// Authors the Death clip: the eye fails, the legs go one at a time, and the machine
        /// falls backwards off its own knees.
        ///
        /// Everything the collapse needs is read back out of `pose` -- the imported Idle clip's
        /// first frame -- rather than out of the hierarchy or out of numbers measured off the
        /// .blend. Forward kinematics over the clip's own curves gives the model-space position
        /// and orientation of every bone in the standing pose, and the leg segments' lengths
        /// fall out of that. So the kneel is as deep as this creature's thigh says it is, the
        /// support leg folds as far as it has to, and the topple turns about wherever the knees
        /// actually end up. Re-export the model with longer legs and the clip follows it;
        /// nothing here holds a copy of anything.
        ///
        /// Bones are found by LEAF NAME, the way HidePartsOnDeath finds the staff, so a rig
        /// re-parented in Blender does not quietly produce a clip that animates nothing.
        private static void WriteDeathClip(string path, AnimationClip pose, string eyelidPath)
        {
            // ---- the standing pose, as local position and rotation per bone -----------------
            var localPos = new System.Collections.Generic.Dictionary<string, Vector3>();
            var localRot = new System.Collections.Generic.Dictionary<string, Quaternion>();

            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(pose))
            {
                if (b.type != typeof(Transform)) continue;

                int axis = "xyzw".IndexOf(b.propertyName[b.propertyName.Length - 1]);
                if (axis < 0) continue;

                float v = AnimationUtility.GetEditorCurve(pose, b).Evaluate(0f);

                if (b.propertyName.StartsWith("m_LocalPosition", System.StringComparison.Ordinal))
                {
                    localPos.TryGetValue(b.path, out Vector3 p);
                    p[axis] = v;
                    localPos[b.path] = p;
                }
                else if (b.propertyName.StartsWith("m_LocalRotation", System.StringComparison.Ordinal))
                {
                    localRot.TryGetValue(b.path, out Quaternion q);
                    q[axis] = v;
                    localRot[b.path] = q;
                }
            }

            // Model space, by walking the path one segment at a time. Scale is left out because
            // this rig has none -- every bone imports at 1, and a scaled bone would need the
            // whole matrix rather than a position and a quaternion.
            (Vector3 pos, Quaternion rot) Fk(string bone)
            {
                var pos = Vector3.zero;
                var rot = Quaternion.identity;
                int from = 0;
                while (true)
                {
                    int slash = bone.IndexOf('/', from);
                    string prefix = slash < 0 ? bone : bone.Substring(0, slash);
                    if (localPos.TryGetValue(prefix, out Vector3 lp)) pos += rot * lp;
                    if (localRot.TryGetValue(prefix, out Quaternion lr)) rot *= lr;
                    if (slash < 0) return (pos, rot);
                    from = slash + 1;
                }
            }

            string Bone(string leaf)
            {
                foreach (string p in localRot.Keys)
                    if (p == leaf || p.EndsWith("/" + leaf, System.StringComparison.Ordinal))
                        return p;
                return null;
            }

            string Parent(string bone)
            {
                int slash = bone.LastIndexOf('/');
                return slash < 0 ? string.Empty : bone.Substring(0, slash);
            }

            Vector2 Flat(string bone) { Vector3 p = Fk(bone).pos; return new Vector2(p.x, p.y); }
            float Aim(Vector2 from, Vector2 to) =>
                Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

            string rootBone = Bone("Root");
            string footL = Bone("Foot_L");
            string footR = Bone("Foot_R");

            // Fatal rather than loud. The flourishes further down degrade to nothing when their
            // bone is missing, but without the root and the two feet there is no ground to
            // measure from and no collapse at all -- and a Death state holding a clip that
            // stands still is the failure that reads as "the trigger is not firing" and costs an
            // afternoon.
            if (rootBone == null || footL == null || footR == null)
                throw new System.InvalidOperationException(
                    "[LightningConjurer] The Death clip is built around the leg chain, and the " +
                    "imported Idle clip animates Root=" + (rootBone ?? "<missing>") +
                    ", Foot_L=" + (footL ?? "<missing>") + ", Foot_R=" + (footR ?? "<missing>") +
                    ". Those names come from _Source~/walkerize.py; re-run it and re-export.");

            string headBone = Bone("Head");
            string hipsBone = Bone("Hips");
            string spineBone = Bone("Spine");
            string armLBone = Bone("ArmRoot.L");
            string armRBone = Bone("ArmRoot.R");

            // The floor, taken from the foot bones, which import sitting exactly on it.
            float groundY = (Fk(footL).pos.y + Fk(footR).pos.y) * 0.5f;

            // ---- the two legs, and the kneeling pose they imply ------------------------------
            var legs = new System.Collections.Generic.List<DeathLeg>();
            foreach (string side in new[] { "L", "R" })
            {
                string hip = Bone("Hip_" + side);
                string knee = Bone("Knee_" + side);
                string ankle = Bone("Ankle_" + side);
                string foot = Bone("Foot_" + side);
                if (hip == null || knee == null || ankle == null || foot == null) continue;

                var leg = new DeathLeg
                {
                    Hip = hip,
                    Knee = knee,
                    Ankle = ankle,
                    HipRest = Flat(hip),
                    AnkleRest = Flat(ankle),
                    Thigh = (Flat(knee) - Flat(hip)).magnitude,
                    Shin = (Flat(ankle) - Flat(knee)).magnitude,
                    ThighRest = Aim(Flat(hip), Flat(knee)),
                    ShinRest = Aim(Flat(knee), Flat(ankle)),
                };

                // How high a joint sits when its limb is lying on the floor, measured rather
                // than guessed: standing, the ankle is exactly that far above the sole. It is
                // what stops the kneeling pose from burying the knee and the shin in the ground.
                float limb = leg.AnkleRest.y - groundY;

                // The kneel, in one line of trigonometry. Knee on the floor, a tilt's worth
                // forward of the hip; shin lying straight back from it; ankle wherever that puts
                // it. Everything else about the pose -- how far the hips drop, how hard the
                // other leg has to fold -- is a consequence of this and of the thigh's length.
                var knees = new Vector2(
                    leg.HipRest.x + leg.Thigh * Mathf.Sin(DeathKneelTiltDegrees * Mathf.Deg2Rad),
                    groundY + limb);
                leg.AnkleKneel = knees - new Vector2(leg.Shin, 0f);

                legs.Add(leg);
            }

            if (legs.Count == 0)
                throw new System.InvalidOperationException(
                    "[LightningConjurer] The Death clip needs Hip_/Knee_/Ankle_/Foot_ bones on " +
                    "at least one side to kneel with, and the rig has none under those names.");

            // How far the hips fall, and where the knees end up: both straight off the pose
            // above. The hips sit a thigh's length above the knee, foreshortened by the tilt.
            DeathLeg first = legs[0];
            float limbRest = first.AnkleRest.y - groundY;
            float kneelHipY = groundY + limbRest
                            + first.Thigh * Mathf.Cos(DeathKneelTiltDegrees * Mathf.Deg2Rad);
            float sink = first.HipRest.y - kneelHipY;

            // The topple turns about the knees, because by then the knees are what the machine
            // is standing on. That has a consequence worth spelling out, because it is what
            // makes the last phase almost free: the knee is a FIXED point, so counter-turning
            // the knee joint by the same angle leaves the whole shin and foot exactly where they
            // were lying, and the fall becomes the thigh swinging down flat while the torso goes
            // over the top of it. Which is what falling backwards out of a kneel looks like.
            var pivot = new Vector3(
                first.HipRest.x + first.Thigh * Mathf.Sin(DeathKneelTiltDegrees * Mathf.Deg2Rad),
                groundY + limbRest, 0f);

            localPos.TryGetValue(rootBone, out Vector3 rootRest);
            localRot.TryGetValue(rootBone, out Quaternion rootRestRot);

            // In the model's own units, derived from the rig rather than converted from metres:
            // how far the body still has to rise as it lies down, and how far the hands drop.
            float clearance = 0f;
            if (headBone != null)
                clearance = (Fk(headBone).pos.y - groundY) * DeathClearanceFraction;

            float armSag = 0f;
            if (armLBone != null && hipsBone != null)
                armSag = (Fk(armLBone).pos.y - Fk(hipsBone).pos.y) * DeathArmSagFraction;

            // A model-space delta on a bone, expressed in that bone's parent's frame.
            //
            // The parent is TURNING while this plays, and this deliberately ignores that: it
            // conjugates by the parent's STANDING orientation, so the delta rides with the body
            // rather than being pinned to the world. That is plainly right for the droops, which
            // are poses the corpse keeps. It is also right for the trailing, which wants the
            // opposite -- and gets it anyway, because every delta here turns about the same
            // model +Z the topple does, and rotations about a shared axis commute, so the body's
            // own rotation cancels out of the conjugation either way.
            Quaternion Local(string bone, float degrees)
            {
                Quaternion parent = Fk(Parent(bone)).rot;
                localRot.TryGetValue(bone, out Quaternion rest);
                return Quaternion.Inverse(parent)
                     * Quaternion.AngleAxis(degrees, Vector3.forward)
                     * parent * rest;
            }

            // ---- bake -----------------------------------------------------------------------
            //
            // Ceil, and the last sample clamped to the end. DeathSeconds is a sum of phase
            // lengths and lands on a whole frame only by luck; round it down and the baked
            // curves stop short of the held ones, leaving a sliver of clip in which the body
            // holds its last baked frame while everything else is still being written.
            int frames = Mathf.CeilToInt(DeathSeconds * Fps) + 1;
            var times = new float[frames];
            var rootRot = new Quaternion[frames];
            var rootPos = new Vector3[frames];
            var headRot = new Quaternion[frames];
            var spineRot = new Quaternion[frames];
            var armRotL = new Quaternion[frames];
            var armRotR = new Quaternion[frames];
            var armPosL = new Vector3[frames];
            var armPosR = new Vector3[frames];
            var jointRot = new Quaternion[legs.Count * 3][];
            for (int i = 0; i < jointRot.Length; i++) jointRot[i] = new Quaternion[frames];

            // One orientation for both arms: they hang off the same spine, and taking it from
            // whichever of them the rig actually has keeps the sag correct if one is missing.
            string armAnchor = armLBone ?? armRBone;
            Quaternion armParent =
                armAnchor != null ? Fk(Parent(armAnchor)).rot : Quaternion.identity;
            localPos.TryGetValue(armLBone ?? string.Empty, out Vector3 armRestL);
            localPos.TryGetValue(armRBone ?? string.Empty, out Vector3 armRestR);

            for (int f = 0; f < frames; f++)
            {
                float t = Mathf.Min(f / Fps, DeathSeconds);
                times[f] = t;

                // ---- the legs ------------------------------------------------------------
                //
                // The first leg's fold IS the body's drop -- the hips ride it down -- so both
                // are the same curve. The second only has to swing in beside it.
                float foldA = DeathKnee(t, DeathFirstKneeDelay, DeathFirstKneeSeconds);
                float foldB = DeathKnee(t, DeathFirstKneeDown, DeathSecondKneeSeconds);
                float topple = DeathTopple(t);
                Quaternion body = Quaternion.AngleAxis(topple, Vector3.forward);

                for (int i = 0; i < legs.Count; i++)
                {
                    DeathLeg leg = legs[i];
                    float fold = i == 0 ? foldA : foldB;

                    // Both legs are solved to a target rather than posed by angle, and the only
                    // difference between them is where that target is. The folding one's ankle
                    // slides back along the floor to where the kneel wants it; the standing
                    // one's stays nailed to the spot until its own turn comes. Interpolating the
                    // TARGET rather than the joint angles is what keeps the foot at a sensible
                    // height the whole way down -- both ends of that slide are at the same
                    // height, so the middle is too, and the toe never scythes through the floor.
                    Vector2 hip = leg.HipRest - new Vector2(0f, sink * foldA);
                    Vector2 target = Vector2.Lerp(leg.AnkleRest, leg.AnkleKneel, fold);

                    DeathReach(in leg, hip, target, out float thigh, out float shin);

                    float hipTurn = thigh - leg.ThighRest;
                    float shinTurn = shin - leg.ShinRest;

                    // The counter-turn that leaves the shin lying exactly where it fell. The
                    // knee is the pivot the body is going over, so subtracting the topple here
                    // pins everything below it: shin, ankle, foot, all still on the floor while
                    // the thigh swings down and the torso goes over.
                    shinTurn -= topple;

                    jointRot[i * 3 + 0][f] = Local(leg.Hip, hipTurn);
                    jointRot[i * 3 + 1][f] = Local(leg.Knee, shinTurn - hipTurn);
                    // The foot keeps the orientation it stands in, all the way through, which is
                    // one subtraction and is why there is no curve for it. Standing, that is a
                    // sole flat on the floor. Kneeling, the ankle has come to rest at exactly the
                    // height it stands at -- the kneel is built off that measurement -- so the
                    // same orientation puts the toe back on the ground and the machine ends up
                    // kneeling on the ball of its foot, which is where a kneeling leg puts it.
                    // Turning the foot down flat with the shin instead buries half of it.
                    jointRot[i * 3 + 2][f] = Local(leg.Ankle, -shinTurn);
                }

                // ---- the body ------------------------------------------------------------
                //
                // Sink first, then turn about the knees, then rise onto its own back. Order
                // matters: the pivot is a point in the SUNK pose, which is where the knees are.
                Vector3 sunk = rootRest - new Vector3(0f, sink * foldA, 0f);
                rootRot[f] = body * rootRestRot;
                rootPos[f] = pivot + body * (sunk - pivot)
                           + Vector3.up * (clearance * Mathf.Sin(topple * Mathf.Deg2Rad));

                // Negative degrees pitch a part FORWARD, against the way the body is going.
                float lag = topple - DeathTopple(t - DeathLagSeconds);
                float shock = DeathShock(t) * DeathKneeShockDegrees;

                // The droop comes in with the power failing, not with the fall -- the head is
                // already down before the first knee goes.
                float limp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / DeathShutterSeconds));

                // The droops fade out as the body goes horizontal, and they fade by the cosine
                // of its pitch because that is the share of gravity still pulling ACROSS the
                // part rather than along it. Standing, all of it. Lying down, none of it -- and
                // a corpse whose arms stayed bent at the angle they took while it was upright is
                // a corpse with its hands driven three metres into the floor.
                float hang = Mathf.Cos(topple * Mathf.Deg2Rad);

                if (spineBone != null)
                    spineRot[f] = Local(spineBone,
                                        DeathSpineArchDegrees * Mathf.Clamp01(topple / DeathToppleDegrees));

                if (headBone != null)
                    headRot[f] = Local(headBone, -(DeathHeadDropDegrees * limp * hang
                                                   + DeathHeadLag * lag + shock));

                float armTurn = -(DeathArmDropDegrees * limp * hang + DeathArmLag * lag + shock);

                // Down in the BODY's frame, not the world's. Tracking world-down past the point
                // where the chest goes horizontal buries the hands: the arms are then hanging
                // off an attachment two metres above the floor and still being pushed two metres
                // straight down through it.
                Vector3 sag =
                    Quaternion.Inverse(armParent) * (Vector3.down * (armSag * limp * hang));

                if (armLBone != null)
                {
                    armRotL[f] = Local(armLBone, armTurn);
                    armPosL[f] = armRestL + sag;
                }

                if (armRBone != null)
                {
                    armRotR[f] = Local(armRBone, armTurn);
                    armPosR[f] = armRestR + sag;
                }
            }

            // ---- write ----------------------------------------------------------------------
            var clip = new AnimationClip { frameRate = Fps };

            var driven = new System.Collections.Generic.HashSet<string> { rootBone };
            if (headBone != null) driven.Add(headBone);
            if (spineBone != null) driven.Add(spineBone);
            if (armLBone != null) driven.Add(armLBone);
            if (armRBone != null) driven.Add(armRBone);
            foreach (DeathLeg leg in legs) { driven.Add(leg.Hip); driven.Add(leg.Knee); driven.Add(leg.Ankle); }

            // Every bone held at the standing pose first, exactly as the Sleep and Awakening
            // clips do it and for the same reason: a bone with no curve is not a bone held
            // still. The driven ones are overwritten below.
            //
            // Their EULER curves are dropped rather than overwritten. An imported clip carries
            // both m_LocalRotation and the editor-side localEulerAngles for every bone; the
            // euler pair wins where both are present, so a driven bone left holding a flat euler
            // curve is a bone that does not move at all -- on a clip whose quaternion curves
            // look perfectly correct in the inspector.
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(pose))
            {
                if (driven.Contains(binding.path) && binding.propertyName.StartsWith(
                        "localEulerAngles", System.StringComparison.Ordinal))
                    continue;

                float held = AnimationUtility.GetEditorCurve(pose, binding).Evaluate(0f);
                AnimationUtility.SetEditorCurve(
                    clip, binding,
                    new AnimationCurve(new Keyframe(0f, held), new Keyframe(DeathSeconds, held)));
            }

            BakeRotation(clip, rootBone, times, rootRot);
            BakePosition(clip, rootBone, times, rootPos);
            if (headBone != null) BakeRotation(clip, headBone, times, headRot);
            if (spineBone != null) BakeRotation(clip, spineBone, times, spineRot);

            for (int i = 0; i < legs.Count; i++)
            {
                BakeRotation(clip, legs[i].Hip, times, jointRot[i * 3 + 0]);
                BakeRotation(clip, legs[i].Knee, times, jointRot[i * 3 + 1]);
                BakeRotation(clip, legs[i].Ankle, times, jointRot[i * 3 + 2]);
            }

            if (armLBone != null)
            {
                BakeRotation(clip, armLBone, times, armRotL);
                BakePosition(clip, armLBone, times, armPosL);
            }

            if (armRBone != null)
            {
                BakeRotation(clip, armRBone, times, armRotR);
                BakePosition(clip, armRBone, times, armPosR);
            }

            if (eyelidPath != null)
            {
                DeathLid(clip, eyelidPath, EyeTopShape, 0f);
                // The bottom half trails the top by a frame and a half, for the same reason the
                // wake-up staggers them: a shutter whose halves move in lockstep reads as one
                // object splitting rather than as an eye.
                DeathLid(clip, eyelidPath, EyeBottomShape, DeathShutterSeconds * 0.08f);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // Three landings, felt rather than seen: a knee, the other knee, and the whole
            // machine. FootstepCameraShake sits on the model child -- the same object the
            // Animator is on, which is the only object an animation event can reach -- and its
            // OnFootPlant takes a strength precisely so that a landing can weigh more than a
            // step. Its own minInterval is 0.2 s, comfortably under the gaps here.
            //
            // SECONDS, unlike the walk's two foot plants. Those go through
            // ModelImporterClipAnimation, whose events are normalised 0-1 and whose overrun
            // EXTENDS the clip; this is AnimationClip's own API, which reads the time literally.
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                Land(DeathFirstKneeDown, DeathKneeShake),
                Land(DeathSecondKneeDown, DeathKneeShake),
                Land(DeathImpactSeconds, DeathImpactShake),
            });

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);

            static AnimationEvent Land(float at, float strength) => new AnimationEvent
            {
                time = at,
                functionName = "OnFootPlant",
                floatParameter = strength,
            };
        }

        /// One shutter half failing shut, then held there for the rest of the clip.
        private static void DeathLid(AnimationClip clip, string eyelidPath, string shape,
                                     float delay)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i < DeathLidTimes.Length; i++)
                curve.AddKey(new Keyframe(DeathLidTimes[i] * DeathShutterSeconds + delay,
                                          DeathLidOpen[i] * 100f));

            // Without this last key the final tangent carries the weight somewhere across two
            // seconds of corpse, and the eye drifts back open on the floor.
            curve.AddKey(new Keyframe(DeathSeconds, 0f));
            Linearize(curve);

            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(eyelidPath, typeof(SkinnedMeshRenderer),
                                              $"blendShape.{shape}"),
                curve);
        }

        /// One bone's rotation, four curves, baked at the clip's frame rate.
        private static void BakeRotation(AnimationClip clip, string path, float[] times,
                                         Quaternion[] values)
        {
            // q and -q are the same orientation, and a curve that steps between them interpolates
            // the long way round -- a bone spinning through most of a turn in one frame. Nothing
            // here produces that, because every value is a continuous turn off the same rest
            // pose; this is one line to make sure nothing here ever starts to.
            for (int i = 1; i < values.Length; i++)
                if (Quaternion.Dot(values[i - 1], values[i]) < 0f)
                    values[i] = new Quaternion(-values[i].x, -values[i].y,
                                               -values[i].z, -values[i].w);

            for (int axis = 0; axis < 4; axis++)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < times.Length; i++)
                    curve.AddKey(new Keyframe(times[i], values[i][axis]));
                Linearize(curve);

                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                                                  "m_LocalRotation." + "xyzw"[axis]),
                    curve);
            }
        }

        /// One bone's local position, three curves, baked the same way.
        private static void BakePosition(AnimationClip clip, string path, float[] times,
                                         Vector3[] values)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < times.Length; i++)
                    curve.AddKey(new Keyframe(times[i], values[i][axis]));
                Linearize(curve);

                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                                                  "m_LocalPosition." + "xyz"[axis]),
                    curve);
            }
        }

        /// Straight lines between keys.
        ///
        /// A Keyframe built from a time and a value alone gets FLAT tangents, and a curve of
        /// those is a staircase: every frame eases into and out of a hold, which at thirty of
        /// them a second is a visible stutter down the whole fall. Smoothing fixes that and
        /// brings its own problem -- smoothed tangents overshoot, and an overshoot on a
        /// blend-shape weight carries the eyelid past shut and back open again. The bone curves
        /// are baked at the clip's own frame rate, so there is nothing between two keys to be
        /// smooth about in the first place.
        private static void Linearize(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                Keyframe key = curve[i];
                if (i > 0)
                    key.inTangent = (key.value - curve[i - 1].value) /
                                    Mathf.Max(1e-6f, key.time - curve[i - 1].time);
                if (i < curve.length - 1)
                    key.outTangent = (curve[i + 1].value - key.value) /
                                     Mathf.Max(1e-6f, curve[i + 1].time - key.time);
                curve.MoveKey(i, key);
            }
        }

        private static AnimationClip FindClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null)
            {
                string found = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(Fbx)
                    .OfType<AnimationClip>().Select(c => c.name));
                throw new System.InvalidOperationException(
                    $"[LightningConjurer] Clip '{name}' missing from the FBX. " +
                    $"Clips present: [{(found.Length == 0 ? "none" : found)}]. " +
                    "Building on would produce an animator with no motion. An FBX that " +
                    "imports with NO clips at all usually means an action in the .blend is " +
                    "keying bones that no longer exist -- the exporter skips those silently. " +
                    "Re-run _Source~/walkerize.py, which retargets and verifies them.");
            }
            return clip;
        }

        /// Two states -- Idle and Walk -- with the blend tree demoted to choosing CADENCE.
        ///
        /// It used to be one state: a single 1-D tree with Idle at threshold 0 and the walk
        /// clip at the two speeds above it. That is the shape every other creature here
        /// uses, and on those creatures it is right. On this one it is not, and the reason
        /// is what this creature's Idle clip actually contains.
        ///
        /// It is not a rest pose with the legs under it. It is the ambient hover -- body
        /// breathing, arms drifting, halo turning -- and the legs do not move in it at all.
        /// Blending it against Walk therefore does not produce a slower walk, it produces a
        /// HALF-AMPLITUDE one: legs barely lifting while the body slides along. With Idle at
        /// 0 and the stroll at WalkSpeed, every departure spent the half-second or so the
        /// NavMeshAgent takes to accelerate somewhere inside that mixture, which is the "it
        /// plays idle and walk at the same time" this replaces.
        ///
        /// So standing versus walking is a TRANSITION, not a blend:
        ///
        ///     Idle  --(SpeedX above MoveEnterSpeed)--&gt;  Walk
        ///     Idle  &lt;--(SpeedX below MoveExitSpeed)--   Walk
        ///
        /// and the tree inside Walk holds only the thing that genuinely is a blend:
        ///
        ///     Walk @ WalkSpeed (4.50 m/s)   rate 0.50
        ///     Walk @ RunSpeed  (8.99 m/s)   rate 1.00
        ///
        /// One clip at two thresholds with a playback rate attached to each, so the tree
        /// interpolates cadence across the range rather than amplitude and the feet track
        /// the ground at both ends. Below WalkSpeed the tree clamps to the stroll's rate;
        /// the creature is only ever down there while accelerating through it.
        ///
        /// The thresholds are true m/s, which only holds because the prefab sets
        /// AgentAnimatorDriver's two scale factors to 1 -- by default it multiplies velocity
        /// by 3x and the tree would sit pinned at the top child forever.
        ///
        /// The blend parameter and both conditions read ForwardSpeedParameter, and it is
        /// SpeedX rather than the SpeedY every other creature here uses. Not a typo; see
        /// the constant.
        ///
        /// The graph is six states and it is deliberately not symmetric:
        ///
        ///     [entry] -> Sleep -> Awakening -> Idle <-> Walk
        ///                                       ^        ^
        ///                                       +- Attack +      (from Any State, gated Awake)
        ///                                          Death        (from Any State, gated on nothing)
        ///
        /// Sleep and Awakening are entered once, in that order, and never again -- see the
        /// one-way note where they are built. Death is the mirror of that: entered from
        /// anywhere, at any time, and never left. Everything in between is the usual locomotion
        /// pair plus an Attack hung off Any State.
        private static AnimatorController BuildController()
        {
            EnsureFolder(ControllerDir);
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // These names are AgentAnimatorDriver's, verbatim, misspellings and all: it
            // calls SetFloat/SetBool on them unconditionally every frame, and a parameter
            // it cannot find is a warning per frame per creature.
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            // ConjurerCastModule.castAnimTrigger names this one.
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            // Two names for one event, as on the golem, the dune rat and the vrescal:
            // HealthReactionModule.dieAnimTrigger sends "Death" and AgentAnimatorDriver.TriggerDie
            // sends "Die", and which of the two reaches this creature depends on what killed it.
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            // DormantModule names these two. Wake is the edge out of the entry state; Awake is a
            // LATCH, set once when the eye finishes opening and never cleared, which is what
            // makes "you can never go back to sleep" a property of the graph rather than of the
            // module's good behaviour.
            controller.AddParameter("Wake", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Awake", AnimatorControllerParameterType.Bool);

            var tree = new BlendTree
            {
                name = "Cadence",
                blendType = BlendTreeType.Simple1D,
                blendParameter = ForwardSpeedParameter,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            AnimationClip walk = FindClip("Walk");
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(walk, RunSpeed);

            // Playback rate per child. ChildMotion is a struct, so the array has to be
            // read out, edited and assigned back -- editing tree.children[i] in place
            // compiles and does nothing.
            ChildMotion[] children = tree.children;
            children[0].timeScale = WalkSpeed / StrideSpeed;
            children[1].timeScale = RunSpeed / StrideSpeed;
            tree.children = children;

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            AnimatorState idle = root.AddState("Idle");
            idle.motion = FindClip("Idle");

            AnimatorState walking = root.AddState("Walk");
            walking.motion = tree;

            // ---- the one-way half of the graph -------------------------------------------
            //
            //     Sleep -> Awakening -> Idle <-> Walk,  Idle/Walk <-> Attack
            //
            // and nothing anywhere targets Sleep or Awakening again. That is deliberate and it
            // is enforced here rather than in DormantModule: the module could be re-enabled, or
            // added twice, or a designer could drop another one on an instance, and none of
            // that can put the creature back to sleep if the graph has no edge for it.
            AnimatorState sleep = root.AddState("Sleep");
            sleep.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(SleepClipPath);

            AnimatorState awakening = root.AddState("Awakening");
            awakening.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(AwakenClipPath);

            // Sleep, not Idle: every conjurer in the world is asleep until something walks up to
            // it, and an entry state that stands there alert is visible the moment one spawns
            // off screen. DormantModule is the only thing that fires the trigger out of it.
            root.defaultState = sleep;

            // Duration zero. There is nothing to cross-fade: the two clips hold the SAME pose,
            // and the only property that differs is the lid, which Awakening is about to drive
            // from exactly where Sleep left it.
            AnimatorStateTransition rouse = sleep.AddTransition(awakening);
            rouse.hasExitTime = false;
            rouse.hasFixedDuration = true;
            rouse.duration = 0f;
            rouse.AddCondition(AnimatorConditionMode.If, 0f, "Wake");

            // Exit time, because this one IS about the clip finishing -- the eye has to be open
            // before anything else happens. Landing on Idle is safe even if the creature is
            // already being asked to walk: Idle's own condition forwards it on the next frame.
            AnimatorStateTransition risen = awakening.AddTransition(idle);
            risen.hasExitTime = true;
            risen.exitTime = 1f;
            risen.hasFixedDuration = true;
            risen.duration = 0.1f;

            // hasExitTime false on both: these follow the motor, and exit time would make
            // the creature finish the cycle it is in before admitting it had stopped.
            //
            // That is a real temptation on the stop edge in particular -- ending on a
            // touchdown would put both soles flat before the blend even starts -- and it is
            // still the wrong trade here. The Cadence tree clamps to half playback rate
            // below its lower threshold, so a walk held open waiting for frame 14 or 40
            // keeps stepping for up to 1.7 s with the body already stationary. That is
            // marching on the spot, which is the same foot-skate as the glide with the sign
            // flipped. WalkStopBlend brings the legs down instead.
            AnimatorStateTransition start = idle.AddTransition(walking);
            start.hasExitTime = false;
            start.hasFixedDuration = true;
            start.duration = LocomotionBlend;
            start.AddCondition(
                AnimatorConditionMode.Greater, MoveEnterSpeed, ForwardSpeedParameter);

            AnimatorStateTransition stop = walking.AddTransition(idle);
            stop.hasExitTime = false;
            stop.hasFixedDuration = true;
            stop.duration = WalkStopBlend;
            stop.AddCondition(
                AnimatorConditionMode.Less, MoveExitSpeed, ForwardSpeedParameter);

            // Attack hangs off Any State rather than off Idle and Walk separately: the cast
            // can start from either, and a trigger that only some states listen for is the
            // kind of thing that works in testing and fails the first time a creature is
            // ambushed mid-stride.
            AnimatorState attack = root.AddState("Attack");
            attack.motion = FindClip("Attack");

            AnimatorStateTransition cast = root.AddAnyStateTransition(attack);
            cast.hasExitTime = false;
            cast.hasFixedDuration = true;
            cast.duration = 0.15f;
            // Without this, the Any State edge re-enters Attack from Attack and a second
            // trigger during a cast restarts the wind-up while the module keeps its own
            // clock -- the bolt then lands halfway through the animation.
            cast.canTransitionToSelf = false;
            cast.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            // "Any State" includes Sleep and Awakening, which is the whole cost of hanging the
            // attack off it. The latch is what buys the convenience back: before the eye is open
            // this edge cannot fire, so a sleeping creature cannot be startled straight into a
            // cast and the sequence really is sleep -> awakening -> everything else.
            cast.AddCondition(AnimatorConditionMode.If, 0f, "Awake");

            // Exit time, unlike every other transition here: this one IS about the clip
            // reaching its end rather than about what the motor is doing. Landing back on
            // Idle is safe even if the creature is walking -- Idle's own condition sends it
            // straight on to Walk on the next frame.
            AnimatorStateTransition recover = attack.AddTransition(idle);
            recover.hasExitTime = true;
            recover.exitTime = 1f;
            recover.hasFixedDuration = true;
            recover.duration = 0.25f;

            // Death, off Any State like the cast and for the same reason -- this creature can be
            // killed while asleep, while walking, or three frames into a wind-up -- but with none
            // of the cast's gating. In particular NOT gated on Awake: a conjurer that never got
            // the chance to open its eye can still be shot, and the clip closes a shut eye over
            // itself harmlessly.
            //
            // No way back out. The clip ends on the corpse pose and holds it, so there is no exit
            // transition and no separate corpse state; HealthReactionModule's despawn timer is
            // what eventually takes the body.
            AnimatorState death = root.AddState("Death");
            death.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath);

            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition dies = root.AddAnyStateTransition(death);
                dies.hasExitTime = false;
                dies.hasFixedDuration = true;
                // Short, but not zero. The clip's first frame IS the standing pose, so there is
                // nothing to cross-fade FROM if the creature was idle -- and if it was mid-stride
                // or mid-cast there very much is, and snapping the legs together to start the
                // fall is the one thing that would give the trick away.
                dies.duration = 0.12f;
                dies.canTransitionToSelf = false;
                dies.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            }

            // Write defaults are left ON, which is Unity's default and is deliberately NOT
            // load-bearing here. The eyelid is the property that would care -- it is animated by
            // some states and not others, which is exactly the case write defaults exist for --
            // and the answer is that it is animated by ALL of them: the FBX's three takes carry
            // it held open (export.py, EXPORT_OPEN) and the two generated clips drive it. So
            // whichever way the flag is set, no state can silently restore a stale lid.
            //
            // BuildEyeClips is what keeps that true; it fails the build if the imported clips
            // stop holding the lid open.

            // IsGrounded defaults true so the creature is not treated as falling on the
            // first frame, before AgentAnimatorDriver has written anything.
            AnimatorControllerParameter[] ps = controller.parameters;
            foreach (AnimatorControllerParameter param in ps)
                if (param.name == "IsGrounded") param.defaultBool = true;
            controller.parameters = ps;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject BuildPrefab(AnimatorController controller)
        {
            EnsureFolder(PrefabDir);
            var root = new GameObject(InstanceName);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);

            // The model is built facing Blender +X, which lands on Unity +X. Yaw the
            // model child so the prefab ROOT's forward (+Z) is the creature's
            // forward -- that is the axis every motor and facing module works in.
            //
            // This lives on a child rather than being baked into the mesh data
            // because baking it would mean rotating the artist's geometry in the
            // .blend, and because a visible -90 on a transform is something anyone
            // can find and correct later.
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);

            // Setting localRotation from script leaves m_LocalEulerAnglesHint at zero.
            // The quaternion is what renders, so the model looks right either way, but
            // the Inspector reads the hint to decide which of the equivalent Euler
            // triples to show -- leave it and the rotation field can read (0,0,0) on a
            // transform that is visibly yawed, which is exactly the kind of thing
            // someone later "fixes" by dragging it back.
            var hint = new SerializedObject(model.transform);
            hint.FindProperty("m_LocalEulerAnglesHint").vector3Value = new Vector3(0f, ModelYaw, 0f);
            hint.ApplyModifiedPropertiesWithoutUndo();

            // Drop the body's centre-bottom onto the prefab origin. Blender (x,y,z)
            // imports as Unity (x, z, -y) once bakeAxisConversion has run.
            var footInModel = new Vector3(BodyX, BlenderFloor, -BodyY) * Scale;
            model.transform.localPosition = -(model.transform.localRotation * footInModel);

            // The eyelid is deliberately NOT touched here. Its weights stay at the imported
            // mesh's own 0, which is the closed lid -- and that is the honest preview: a conjurer
            // spawns asleep, so a shut eye in the project window and the scene view is what the
            // thing actually looks like when it is put down. Every clip drives the lid from its
            // first frame anyway, so nothing runtime depends on the resting value. (Setting it
            // does not work by the obvious route in any case: `model` is a nested prefab
            // instance, and neither SetBlendShapeWeight nor a SerializedObject edit of
            // m_BlendShapeWeights survives SaveAsPrefabAsset as a recorded modification.)

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // The motor owns movement, never the clip.
            animator.applyRootMotion = false;

            // Required for this rig specifically. It is 52 bone-parented renderers
            // rather than one skinned mesh, so Unity culls it against bind-pose
            // bounds that do not follow the animation; with the default culling mode
            // it freezes mid-stride whenever it thinks it is off screen.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // The BODY only. This capsule is sized to the head/body sphere and stands on the
            // creature's centreline, and the arms do not live anywhere near that centreline --
            // rig.py hangs them off ArmRoot at |x| 3.3 m, a clear metre outside a 2.4 m radius.
            // So the capsule is right for what it covers and simply does not reach the arms;
            // WireLimbColliders below is what makes them solid.
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = TargetHeight;
            capsule.radius = BlenderBodyWidth * Scale * 0.5f;   // tracks the model, not a magic number
            capsule.center = new Vector3(0f, TargetHeight * 0.5f, 0f);

            WireLimbColliders(root);

            // Kinematic, gravity off. The NavMeshAgent owns the transform, so a dynamic
            // body would fight it every frame and win. The Rigidbody is here anyway because
            // without one every collider on this object is a STATIC collider, and moving
            // static colliders makes PhysX rebuild its broadphase every frame.
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            WireMotor(root, animator);
            WireBrain(root);
            WireNetworking(root);
            // After WireNetworking: NetworkedHealthComponent is a NetworkBehaviour and wants the
            // NetworkObject to already be on the object. Before SaveablePolicy.Ensure below,
            // which decides on HealthSaveable by looking for a HealthComponent.
            WireHealth(root);
            // After WireNetworking (MountNetworkSync wants the NetworkObject) and WireBrain (the
            // seat hides the rider from the EntityFaction and AgentTargeting that method adds).
            // Before SaveablePolicy.Ensure below, which decides on MountSaveable by looking for a
            // MountModule -- without that a player riding at save time comes back on the ground.
            WireShoulderSeat(root);

            // Footstep camera shake, driven by the two OnFootPlant events baked onto the
            // Walk clip in ConfigureImporter.
            //
            // On the MODEL, not the root, and that is the whole of whether it works. Unity
            // delivers an animation event only to components on the same GameObject as the
            // Animator that fired it -- it does not search parents -- and the Animator is on
            // the model child here because that is where the FBX put it. Sitting on the root
            // this component is never called and Unity logs "has no receiver" twice a stride.
            var footstep = model.AddComponent<Presentation.FootstepCameraShake>();
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(ShakeDataPath);
            if (shake != null) SetField(footstep, "shakeData", shake);

            // Save support, decided by the POLICY rather than by a list written out here.
            //
            // AgentController implements IPersistentEntity, so this creature is save-eligible with
            // no extra opt-in -- but the savers still have to be present or it reloads at its
            // authored position with its gait mid-stride. They go in the BUILDER because this
            // script overwrites the prefab wholesale on every re-run, which is exactly how the
            // Golem lost its SaveableEntity.
            //
            // SaveablePolicy.Ensure is the same call Tools > Save System > Wire Saveable Prefabs
            // makes, and the same one PersistenceProbe asserts against. Naming the components here
            // instead -- which is what this did first -- means the builder holds a second opinion
            // about which savers this prefab needs, and the moment the policy learns about a new
            // one the two disagree and the persistence sweep fails. Asking the policy cannot drift.
            if (SaveablePolicy.Ensure(root, out string savers))
                Debug.Log($"[LightningConjurer] Save wiring added: {savers}");

            // The savers are on the prefab now, but its prefabId is NOT: that lives in the asset
            // file, and SaveAsPrefabAsset below replaces the file wholesale, so every rebuild
            // blanks it. Only Tools > Save System > Wire Saveable Prefabs can stamp it back, and
            // it is deliberately not called from here because it sweeps every prefab in the
            // project -- far more than building one creature should touch. So: say so, every time,
            // rather than leaving it to be remembered.
            Debug.LogWarning("[LightningConjurer] Rebuilt prefab needs its save id re-stamped. " +
                             "Run Tools > Save System > Wire Saveable Prefabs, or SaveWiringOnDisk" +
                             "Tests will fail and the creature will be dropped on load in a build.");

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // A NetworkObject created by script ships GlobalObjectIdHash 0, and NGO silently
            // DROPS all but one prefab when several share a hash -- so a conjurer left at 0
            // can take an unrelated creature offline with it. The hash is filled in by the
            // component's own OnValidate, which only resolves against the saved ASSET, so the
            // file has to be re-imported and then reserialized or the corrected value never
            // reaches the YAML. Same three lines DuneFoilBuilder ends on, for the same reason.
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
            return saved;
        }

        /// NavMeshAgent + NavMeshAgentMotor: the agent owns the transform, the clip owns
        /// the pose, and neither knows the other exists.
        ///
        /// Every number here is a consequence of ONE fact -- this creature is 18.1 m tall,
        /// six times the player -- and the three that matter are:
        ///
        ///   * SPEED IS THE CLIP'S, not a taste decision. RunSpeed is StrideSpeed, so the
        ///     animator plays at 1.0 and the walk is the one that was authored. Choosing a
        ///     speed here and leaving the clip alone is how a creature ends up moonwalking.
        ///   * THE AGENT IS SIZED FOR REAL. radius tracks the body sphere and height tracks
        ///     the model, so avoidance and the capsule agree with what you can see. Note the
        ///     project bakes ONE NavMesh agent type (radius 0.5, height 2, see
        ///     ProjectSettings/NavMeshAreas.asset), so this creature walks a surface carved
        ///     for something a fifth its width: it paths fine across open ground and will
        ///     clip scenery in tight places. Baking a second agent type is the fix if that
        ///     ever matters.
        ///   * IT TURNS SLOWLY, ON PURPOSE. 45 deg/s is the same "ponderous" decision the
        ///     procedural version carried in maxYawRate, and it is most of what sells the
        ///     mass now that nothing else about the movement does.
        /// Everything that makes the creature exist on machines other than the one running it.
        ///
        /// Matches the golem, which is the closest thing to a reference: NetworkObject, NetRelay,
        /// ClientNetworkTransform, NetAuthority. Four components and no configuration beyond the
        /// sync axes -- everything creature-specific about the replication is in
        /// ConjurerCastModule's two broadcasts.
        ///
        /// WHAT EACH ONE BUYS:
        ///
        ///   NetworkObject   makes the thing addressable at all. Without it the whole entity is
        ///                   invisible to netcode and every send degrades to a local dispatch --
        ///                   which is exactly the "works for the host, clients see nothing" case.
        ///   NetRelay        carries the NetMessaging channel. Without it the cast broadcasts log
        ///                   "handled message N locally" and go nowhere.
        ///   NetworkTransform replicates the body. This is what makes the WALK animation work on
        ///                   peers for free: AgentAnimatorDriver measures the transform on any
        ///                   frame nobody drove it, so a replicated pose animates itself.
        ///   NetAuthority    switches off AgentController, the motor and the NavMeshAgent on any
        ///                   machine that is only watching. Without it every peer runs its own
        ///                   brain, picks its own wander destinations, and fights the replicated
        ///                   transform -- and every peer casts its own bolt.
        ///
        /// ClientNetworkTransform rather than the stock one because that is what every other
        /// creature here uses. It is owner-authoritative, and the owner of a server-spawned
        /// creature IS the server, so it behaves as server-authoritative -- while leaving the
        /// door open for a creature that is ever handed to a client.
        ///
        /// NetworkedHealthComponent is NOT here either -- it goes on in WireHealth beside the
        /// HealthComponent it replicates, because the two are one decision and splitting them
        /// across two methods is how one of them gets forgotten.
        private static void WireNetworking(GameObject root)
        {
            var netObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            netObject.DontDestroyWithOwner = true;

            root.AddComponent<SpaceGame.Core.NetRelay>();

            var netTransform = root.AddComponent<SpaceGame.Core.ClientNetworkTransform>();
            // Every position and rotation axis, explicitly. An unsynced axis is one the local
            // copy never has corrected, so the drift accumulates for the whole session.
            netTransform.SyncPositionX = true;
            netTransform.SyncPositionY = true;
            netTransform.SyncPositionZ = true;
            netTransform.SyncRotAngleX = true;
            netTransform.SyncRotAngleY = true;
            netTransform.SyncRotAngleZ = true;
            netTransform.InLocalSpace = false;
            netTransform.Interpolate = true;

            root.AddComponent<SpaceGame.Core.NetAuthority>();
        }

        /// Makes the creature killable. Until this existed it could hurt a player and could not
        /// be hurt back -- every shot at it found no IDamageable and passed straight through.
        ///
        /// Three components, and each one covers a case the other two do not:
        ///
        ///   HealthComponent           IS the IDamageable. NetDamage and AgentProjectile both
        ///                             resolve their victim with GetComponentInParent, so on the
        ///                             root it catches hits landing on any part of the rig.
        ///   HealthReactionModule      turns the death into something that happened: the hurt and
        ///                             death sounds, a noise event other agents can hear, and the
        ///                             despawn timer. Without it a killed conjurer stands at zero
        ///                             health, still casting, forever.
        ///   NetworkedHealthComponent  makes the server the only machine that may spend that
        ///                             health, and publishes what is left to everyone else.
        ///                             Without it a client's shots are decided locally and the
        ///                             two machines disagree about whether it is dead.
        ///
        /// The capsule that catches those hits is already on the root, sized to the model, so
        /// nothing extra is needed to be shootable.
        private static void WireHealth(GameObject root)
        {
            var health = root.AddComponent<HealthComponent>();
            var hso = new SerializedObject(health);
            SetInt(hso, "maxHealth", MaxHealth);
            // Full, not zero. currentHealth is a serialized field with its own default, and a
            // prefab that ships at a different number than its maximum spawns pre-damaged.
            SetInt(hso, "currentHealth", MaxHealth);
            hso.ApplyModifiedPropertiesWithoutUndo();

            var reaction = root.AddComponent<HealthReactionModule>();
            var rso = new SerializedObject(reaction);
            // "Hurt" CLEARED and "Death" kept, which is a split the module supports and the
            // controller demands. SetTrigger on a name the controller does not declare is a
            // warning per call, so a creature being shot would fill the console -- and there is
            // no Hurt state here, because a machine this size flinching at a rifle round reads as
            // a bug rather than as a reaction. Empty strings are skipped outright by the module.
            //
            // "Death" is declared (see BuildController) and does have a state and a clip behind
            // it, which is what changed: the collapse used to be a thing this file explained the
            // absence of. WriteDeathClip authors it -- in C# rather than in _Source~/anim.py,
            // because the eye shutting is half the animation and the eye is blend shapes, which
            // is exactly the export route that does not survive the trip through the FBX.
            SetString(rso, "hurtAnimTrigger", string.Empty);
            SetString(rso, "dieAnimTrigger", "Death");
            // It is a large machine and it dies loudly. Both radii are above the module's own
            // defaults for the same reason ActivationRange is 28 m: everything about this
            // creature is scaled to a body six times the player's height.
            SetFloat(rso, "damageNoiseRadius", 25f);
            SetFloat(rso, "deathNoiseRadius", 40f);
            // Switches off AgentController on death, which is what stops the corpse casting.
            SetBool(rso, "disableAgentOnDeath", true);
            // Long enough to walk up to the thing you just killed, and comfortably longer than
            // the collapse: the body has been lying still for nine seconds by the time it fades.
            SetFloat(rso, "despawnDelay", 12f);
            rso.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetworkedHealthComponent>();

            WireLoot(root);
        }

        /// What the machine leaves behind: its staff, every time.
        ///
        /// A guaranteed drop rather than a roll. This is a four-and-a-half-second telegraphed cast
        /// on a hundred-hit-point machine that hits for ten in a 3.5 m radius from anywhere within
        /// twenty-five metres -- a fight the player has to learn rather than survive, and the staff
        /// is what learning it is FOR. A coin flip on a creature this rare reads as the drop being
        /// broken, not as luck.
        ///
        /// Note the ordering this depends on: ConjurerStaffBuilder mints the item asset, so it has
        /// to have been run at least once. The table is still added when the asset is missing --
        /// empty, and loudly -- because a silently absent loot table is exactly the kind of thing
        /// that is noticed six sessions later.
        private static void WireLoot(GameObject root)
        {
            var loot = root.AddComponent<EntityLootTable>();
            var lso = new SerializedObject(loot);

            var staff = AssetDatabase.LoadAssetAtPath<InventoryItem>(ConjurerStaffBuilder.ItemPath);

            if (staff == null)
            {
                Debug.LogWarning(
                    "[LightningConjurer] No staff item at " + ConjurerStaffBuilder.ItemPath +
                    ". The creature will drop nothing -- run Tools/Build Conjurer Staff Artifact " +
                    "first, then build this again.");
            }
            else
            {
                SerializedProperty entries = lso.FindProperty("lootEntries");
                entries.arraySize = 1;

                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("item").objectReferenceValue = staff;
                entry.FindPropertyRelative("dropChance").floatValue = 1f;
                entry.FindPropertyRelative("quantity").intValue = 1;
            }

            // Held until the body goes, rather than paid out the instant the health hits zero.
            //
            // The drop used to land while the creature was still standing over it -- and now that
            // there is a three-second collapse to watch, it would land while the thing was still
            // on its way to its knees, which reads as the staff belonging to something else
            // entirely. Waiting for the despawn makes the pickup the thing that REPLACES the
            // corpse: the body fades, the staff is lying where it fell.
            //
            // The cost is the wait, and the wait is HealthReactionModule's despawnDelay -- twelve
            // seconds below. That is the dial if it ever feels long; it is not a number this
            // component knows anything about.
            SetBool(lso, "dropOnDespawn", true);
            lso.ApplyModifiedPropertiesWithoutUndo();

            // And take the staff out of the corpse's fist as it dies.
            //
            // It no longer overlaps with the real one -- that arrives twelve seconds later, when
            // the body goes -- so this is not about two staffs any more. It is about the staff
            // being fourteen metres long and held upright: the collapse drops the hand that holds
            // it by five metres onto a knee, which puts three metres of it through the floor and
            // leaves it lying across the corpse afterwards. A machine that lets go of its weapon
            // as it dies is also simply the better read.
            var shed = root.AddComponent<HidePartsOnDeath>();
            var sso = new SerializedObject(shed);
            SerializedProperty parts = sso.FindProperty("partNames");
            parts.arraySize = ConjurerStaffBuilder.StaffParts.Length;
            for (int i = 0; i < ConjurerStaffBuilder.StaffParts.Length; i++)
                parts.GetArrayElementAtIndex(i).stringValue = ConjurerStaffBuilder.StaffParts[i];
            sso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireMotor(GameObject root, Animator animator)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;
            agent.angularSpeed = 45f;
            // A time constant in disguise: the agent sheds speed at this rate, so at
            // RunSpeed this is a one-second run-down and about 4.5 m of coast -- and at the
            // stroll, half a second and roughly a metre. Lower reads as more mass and
            // overshoots every destination by half a stride.
            //
            // It was 4, which is 2.25 s and TEN METRES to stop from a run, and that number
            // was the visible half of a bug rather than a style: ConjurerCastModule decides
            // to cast at 25 m, and a ten-metre coast meant the staff went up while the body
            // was still travelling. The module now waits for the body to come to rest before
            // it starts (see its settle phase), so this figure is what sets how long that
            // wait IS -- keep it in step with ConjurerCastModule.settleTimeout, which is the
            // ceiling on how long the creature will stand there braking before casting
            // anyway.
            agent.acceleration = 9f;
            agent.radius = BlenderBodyWidth * Scale * 0.5f;   // tracks the model
            agent.height = TargetHeight;
            // Overwritten per intent by the motor; this is only what a parked agent falls
            // back to. Sized to the machine either way -- an 18 m robot cannot be within
            // NavMeshAgent's default 0 m of anything.
            agent.stoppingDistance = 12f;
            agent.autoBraking = true;
            // Low quality, deliberately. High-quality avoidance on a 2.4 m radius pushes
            // this thing metres sideways to dodge a player standing inside its own
            // footprint, which reads as the machine flinching.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var motor = root.AddComponent<NavMeshAgentMotor>();
            var mso = new SerializedObject(motor);
            SetProp(mso, "agent", agent);
            // Walk is half of the agent's speed, and the blend tree's middle threshold is
            // that same half -- change one and the creature skates.
            SetFloat(mso, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            // Slow, for the same reason angularSpeed is. This one only applies to
            // StopAndFace-style intents, where the body turns without travelling.
            SetFloat(mso, "faceRotateSpeed", 1.5f);
            // Matches the wander's sampleDistance. The default 6 m is a fraction of one
            // stride on this machine, and an agent dropped slightly off the mesh with
            // nothing inside 6 m simply never attaches and stands there.
            SetFloat(mso, "navMeshSnapDistance", 25f);
            mso.ApplyModifiedPropertiesWithoutUndo();

            var animDriver = root.AddComponent<AgentAnimatorDriver>();
            var adso = new SerializedObject(animDriver);
            SetProp(adso, "animator", animator);
            // Both scales to 1 so the blend parameter arrives as true m/s and the tree's
            // thresholds mean what they say. Left at their defaults the driver multiplies
            // velocity by 3 and every speed above 3 m/s pins the tree at its top child.
            SetFloat(adso, "animationSpeedMultiplier", 1f);
            SetFloat(adso, "walkAnimBoost", 1f);
            // The clip is authored at StrideSpeed and the agent tops out at RunSpeed, so
            // this is 1 by construction. It is written as the ratio anyway: re-measure the
            // stride and this corrects itself instead of quietly becoming wrong.
            SetFloat(adso, "animatorSpeedScale", RunSpeed / StrideSpeed);
            // Only used on a machine that is watching rather than driving this creature,
            // which has no way to know whether the intent was a run. Halfway between the
            // two speeds is the least-wrong place to put the line.
            // Where a stop is allowed to put the walk down. ConjurerCastModule's settle
            // holds the cadence through the run-down rather than letting the blend tree wind
            // it toward zero, and this is the list of places the hold may let go of it.
            //
            // FootPlantFrames, not the loop point. Frame 1 of this cycle is the PASSING
            // pose -- anim.py's first contact is a quarter cycle in -- so releasing at the
            // cycle boundary would start the blend into Idle from a foot in mid-air, which
            // is the whole thing the hold is there to stop. The clip's last frame duplicates
            // its first, so the cycle is WalkFrames long and frame f sits at (f - 1) / that.
            SetFloatArray(adso, "strideEndPhases",
                          FootPlantFrames.Select(f => (f - 1) / (float)WalkFrames).ToArray());
            adso.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Roam; wake when a player comes inside ActivationRange; then follow.
        ///
        /// Composed, not coded. There is no conjurer-specific brain class and there should not
        /// be one -- and now that the movement is stock too, there is no conjurer-specific
        /// anything on this prefab. This is four stock components and two priority numbers:
        ///
        ///   EntityFaction    makes it visible to targeting at all. Without it the creature
        ///                    can never acquire anything, silently.
        ///   AgentTargeting   owns WHO. Every module reads its answer, which is what stops a
        ///                    creature chasing one entity while facing another.
        ///   ChaseModule      owns how to get there, at Reactive priority.
        ///   WanderModule     owns what it does with the rest of its life, at Fallback.
        ///
        /// This used to have NO Fallback module at all, on purpose: with nothing at the bottom
        /// of the ladder AgentController.EvaluateModules falls off the end, returns
        /// MoveIntent.Idle, and the machine holds position -- inert until disturbed. That was the
        /// original brief and the wander is a deliberate change to it, not a slip.
        ///
        /// Chase still sits above it, so the reaction is unchanged: a player inside 10 m
        /// interrupts the roam mid-stride and does not have to wait for it to finish.
        ///
        /// WANDERING NEEDS A BAKED NAVMESH. WanderModule picks its destinations with
        /// NavMesh.SamplePosition, which fails everywhere in a scene whose NavMeshSurface has
        /// never been baked -- the module then returns null every tick and the creature stands
        /// exactly as it did before, with a clean console. "Marius test scene" has one baked;
        /// Ferdinand_Test_world does not.
        private static void WireBrain(GameObject root)
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RobotFactionPath);
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);
            if (faction == null || table == null)
            {
                Debug.LogError("[LightningConjurer] Faction assets missing; the creature will " +
                               "never acquire a target. Expected " + RobotFactionPath + " and " +
                               RelationshipsPath + ".");
            }

            // RobotFaction is already Hostile toward PlayerFaction in GlobalRelationships.asset,
            // so no new row is needed and none should be added -- that table is global, and a
            // row added here changes every robot in the game.
            var entityFaction = root.AddComponent<EntityFaction>();
            SetField(entityFaction, "faction", faction);
            SetField(entityFaction, "relationshipTable", table);

            // Added explicitly rather than left to AgentController's Awake, because the ranges
            // below are the entire behaviour and an auto-added component would carry defaults
            // (35 m acquisition) that are nothing like the brief.
            var targeting = root.AddComponent<AgentTargeting>();
            var tso = new SerializedObject(targeting);
            SetEnum(tso, "relationship", (int)FactionRelationship.Hostile);
            SetFloat(tso, "acquisitionRange", ActivationRange);
            // Above acquisition so a player hovering exactly on the line does not flip the
            // creature between chasing and inert every frame.
            SetFloat(tso, "loseRange", ActivationRange * 1.4f);
            // Distance alone decides, which is what "comes within 10 metres" means. With line
            // of sight required, walking up behind its own leg would leave it inert.
            SetBool(tso, "requireLineOfSightToAcquire", false);
            SetFloat(tso, "proximityAcquireRange", ActivationRange);
            tso.ApplyModifiedPropertiesWithoutUndo();

            var chase = root.AddComponent<ChaseModule>();
            var cso = new SerializedObject(chase);
            // Set EXPLICITLY. Unity does not call Reset() for AddComponent, so a module added
            // from a script keeps the serialized default of Fallback (0) -- which here would
            // leave the one module that makes this creature move sitting at the bottom of the
            // ladder for no reason.
            SetInt(cso, "priority", ModulePriority.Reactive);
            // Sized against the ACQUISITION RANGE, not just against the creature.
            //
            // This was 8 m, reasoned from the creature's size alone -- an 18 m robot stopping at
            // ChaseModule's default 1.3 m would put a foot on the player. True, and useless: with
            // ActivationRange at 10 m it left a two-metre chase band. The creature noticed you at
            // 10 m, took two steps, decided it had arrived, and stood there -- which from the
            // outside is indistinguishable from never having reacted at all.
            //
            // The floor that actually matters is the capsule: its radius is BlenderBodyWidth/2
            // (~2.4 m), so anything under that walks the body through the player. 4 m clears it
            // with margin and leaves a real chase band -- 4 m out to the 15 m lose range.
            //
            // Now sized off the CAST rather than off the capsule. 4 m was right for a
            // creature whose only move was to walk up to you; a caster that closes to 4 m
            // has thrown away the stand-off its three-second wind-up depends on. It parks
            // at StandoffDistance, comfortably inside CastRange, and throws from there.
            SetFloat(cso, "chaseStopDistance", StandoffDistance);
            SetFloat(cso, "chaseSpeedMultiplier", 1f);
            cso.ApplyModifiedPropertiesWithoutUndo();

            // Roam. Every distance here is sized against the MACHINE, and the one that matters is
            // stopDistance: WanderModule's own default is 0.2 m, NavMeshAgentMotor writes the
            // intent's number straight into agent.stoppingDistance, and an 18 m robot with a
            // ten-metre stride can never be within 20 cm of anything. It would arrive at no
            // destination, ever, and grind at the last one forever.
            var wander = root.AddComponent<WanderModule>();
            var wso = new SerializedObject(wander);
            // Explicit, like ChaseModule's: Unity does not call Reset() for AddComponent.
            SetInt(wso, "priority", ModulePriority.Fallback);
            SetBool(wso, "limitWanderRadius", true);
            SetFloat(wso, "wanderRadius", 150f);
            // Matches the driver's navMeshSampleDistance: a destination the driver cannot get a
            // path to is a destination the wander should not have picked.
            SetFloat(wso, "sampleDistance", 25f);
            // Comfortably past stopDistance, or it picks somewhere it has already arrived at
            // and the machine spends its life re-rolling instead of walking. Four times it,
            // and no more than that.
            //
            // This was 40 m, sized off the stride alone, and 40 m is a distance that does not
            // EXIST in most places you would test this. A Unity plane at the scale "Marius
            // test scene" uses it is about 50 x 60 m, so from anywhere near the middle the
            // farthest reachable point is under 40 -- the wander rolled ten candidates a tick,
            // rejected every one for being too close, and the creature stood still forever
            // with a clean console and a baked NavMesh right under it. A roam radius may
            // exceed the world it is dropped in; a MINIMUM may not.
            SetFloat(wso, "minDestinationDistance", 25f);
            // Half its old value, because a NavMeshAgent brakes rather than coasting: at the
            // stroll it needs about 2.5 m to stop, so 12 m of stopping distance is 9 m of
            // standing still short of somewhere it was asked to go. Still well clear of the
            // 2.4 m capsule, and still looser than the chase's 4 m, which is the ordering
            // that matters.
            SetFloat(wso, "stopDistance", 6f);
            // 1, and the stroll comes from somewhere else on purpose.
            //
            // The contrast with the chase is real -- wander walks, chase runs -- but it is
            // NavMeshAgentMotor.walkSpeedMultiplier that draws it, because that is the number
            // the blend tree's middle threshold is paired with. ChaseModule asks to run and
            // gets RunSpeed; everything else does not and gets WalkSpeed. Scaling the speed a
            // second time here would land the creature between the tree's two walk children,
            // moving at a rate no child was authored for, and the feet would skate for no
            // reason anyone could find later.
            SetFloat(wso, "speedMultiplier", 1f);
            // It stands and thinks between legs of the roam. Long, because it is a giant.
            SetFloat(wso, "minWaitTime", 3f);
            SetFloat(wso, "maxWaitTime", 9f);
            wso.ApplyModifiedPropertiesWithoutUndo();

            // The attack. Sits at RangedAttack (22), above ChaseModule's Reactive (20), so a
            // target inside CastRange stops the chase and starts a cast instead of being
            // walked at. Out of range it passes and Chase gets the frame back.
            //
            // Damage and radius are the module's own, not the player's LightningSpell's:
            // they call the same LightningStrike, but what a bolt DOES is balanced per
            // caster. See CastDamage for why this one hits for a tenth of what the player's
            // artifact does.
            var cast = root.AddComponent<ConjurerCastModule>();
            var kso = new SerializedObject(cast);
            // Explicit, like the other two: Unity does not call Reset() for AddComponent, so
            // a module added from a script keeps the serialized default of Fallback (0) --
            // which would put the attack BELOW the wander and it would never fire.
            SetInt(kso, "priority", ModulePriority.RangedAttack);
            SetFloat(kso, "castRange", CastRange);
            // Come to a stop BEFORE the staff goes up. Without these the creature decides to
            // cast at CastRange at a dead run and skates its whole braking distance in the
            // attack pose. See CastSettleSpeed and WireMotor's acceleration -- the two are a
            // pair, and changing the acceleration changes how long this wait takes.
            SetFloat(kso, "settleSpeed", CastSettleSpeed);
            // The second gate, and the one that keeps a leg from hanging in mid-air: the
            // body stops before the ANIMATOR does, and a Cast trigger fired inside the
            // WalkStopBlend crossfade interrupts it rather than following it. See the
            // module's header.
            SetString(kso, "settledAnimState", CastSettledState);
            SetFloat(kso, "settleTimeout", CastSettleTimeout);
            SetFloat(kso, "castSeconds", CastSeconds);
            SetFloat(kso, "cooldownSeconds", CastCooldown);
            SetFloat(kso, "damageRadius", CastBlastRadius);
            SetInt(kso, "damage", CastDamage);
            // Line of sight to BEGIN only. Once committed the cast finishes and strikes the
            // remembered spot whatever happens -- that is what makes stepping behind cover
            // mid-wind-up work as an escape rather than as a cancel button.
            SetBool(kso, "requireLineOfSight", true);
            // Tracks the target through most of the wind-up and then LOCKS, a second
            // before the bolt lands. That last second is the counterplay: standing still
            // is punished and a late move beats it.
            //
            // It is the mode this attack needs rather than a preference. The strike falls
            // out of the sky, so it cannot be blocked by cover or beaten by an angle --
            // under TracksTarget (0) it would be unavoidable damage on a timer, and under
            // WhereItCommitted (1) a player who simply keeps walking is never hit at all.
            SetEnum(kso, "aim", (int)CastAim.TracksThenCommits);
            SetFloat(kso, "aimLockSeconds", CastAimLockSeconds);

            // Assigned here rather than left for someone to drag in, for the same reason
            // everything else on this prefab is: a slot filled by hand is a slot that is
            // empty again after the next rebuild. An unassigned bolt is silent -- the cast
            // damages correctly and draws nothing, which reads as the attack not firing.
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(LightningVfxPath);
            if (bolt != null)
                SetProp(kso, "lightningVFXPrefab", bolt);
            else
                Debug.LogWarning($"[LightningConjurer] No lightning VFX at " +
                                 $"{LightningVfxPath} - the cast will damage correctly and " +
                                 "draw nothing.");

            // The charge on the staff. Without this the wind-up is four seconds of a
            // creature standing still with a stick in the air -- the pose reads as a
            // charge only because something is visibly charging.
            //
            // The module parents it to chargeSocketBone and destroys it when the bolt
            // lands, so its whole lifetime is handled here.
            var charge = AssetDatabase.LoadAssetAtPath<GameObject>(ChargeVfxPath);
            if (charge != null)
                SetProp(kso, "chargeVFXPrefab", charge);
            else
                Debug.LogWarning($"[LightningConjurer] No charge effect at {ChargeVfxPath} - " +
                                 "the wind-up will play with a dark turbine.");

            // staff.py puts this bone at the emitter above the turbine, so the effect
            // rides the staff through the whole raise for free.
            SetString(kso, "chargeSocketBone", "StaffTip");

            // The warning on the ground, when GroundWarning is on.
            //
            // The null branch is not "skip it" -- it WRITES null. A field the builder
            // leaves alone is a field that keeps whatever the last build put there, so
            // turning the ring off by not wiring it would leave every already-built prefab
            // still pointing at the old asset and still drawing it. Same reason every
            // other field on this prefab is written explicitly.
            if (GroundWarning)
            {
                var warning = AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphVfxPath);
                if (warning != null)
                    SetProp(kso, "telegraphPrefab", warning);
                else
                    Debug.LogError($"[LightningConjurer] No ground warning at " +
                                   $"{TelegraphVfxPath} - the sky strike will land with no " +
                                   "warning on the ground, which makes it unavoidable.");
            }
            else
            {
                SetProp(kso, "telegraphPrefab", null);
            }

            // LINE FIRE ONLY, and the staff's emitter is where a line would leave from.
            // Written here rather than left to the module's serialized default for the same
            // reason as everything else on this prefab: a field the builder does not write
            // is a field that silently reverts.
            SetString(kso, "muzzleBone", "StaffTip");

            // Dropped out of the sky onto the target. This is the creature's attack now;
            // the line-fired path is still in the module behind this flag -- see its header
            // for why it was kept.
            SetBool(kso, "skyStrike", true);
            SetFloat(kso, "beamRadius", BeamRadius);

            // Long enough to outlast the graph, short enough that spent bolts do not pile
            // up. Lightning.prefab has no self-destruct of its own.
            SetFloat(kso, "vfxLifetime", 5f);
            SetFloat(kso, "drawHeight", DrawHeight);
            // The Animator is on the MODEL CHILD, not on root, so this has to search.
            SetProp(kso, "animator", root.GetComponentInChildren<Animator>(true));
            kso.ApplyModifiedPropertiesWithoutUndo();

            // Asleep, standing, until someone comes close. Scripted priority, which is the top of
            // the ladder, and while it is asleep it returns Idle every frame -- so cast, chase
            // and wander are all starved for the length of the sequence and no other module on
            // this prefab needed to learn the word "dormant". The body never moves; only the
            // eyelid does. The instant the eye finishes opening the module switches itself off
            // and they start winning frames on the very next tick.
            //
            // On the PREFAB, so every conjurer in the world starts asleep. Delete the component
            // from an instance to get one that is simply standing there, eye open.
            var dormant = root.AddComponent<DormantModule>();
            var dso = new SerializedObject(dormant);
            // Explicit, like every other module here: Unity does not call Reset() for
            // AddComponent, so a script-added module keeps the serialized default of Fallback
            // (0). At 0 this would tie with the wander and the creature would occasionally walk
            // off asleep instead of waking up.
            SetInt(dso, "priority", ModulePriority.Scripted);
            SetFloat(dso, "wakeRadius", WakeRadius);
            // Written from the clip's own length rather than typed twice. The module stands the
            // creature still for exactly as long as the Awakening state plays, and the two
            // drifting apart is either a creature that acts before its eye is open or one that
            // stands blinking at nothing.
            SetFloat(dso, "awakenSeconds", AwakenSeconds);
            // The Animator is on the MODEL CHILD, not on root, so this cannot be left to a
            // GetComponent on the same object.
            SetProp(dso, "animator", root.GetComponentInChildren<Animator>(true));
            dso.ApplyModifiedPropertiesWithoutUndo();

            var agent = root.AddComponent<AgentController>();
            var aso = new SerializedObject(agent);
            SetProp(aso, "MotorComponent", root.GetComponent<NavMeshAgentMotor>());
            // Assigned, not left null. AgentController ticks this with the motor's velocity
            // once per frame, and it is the only thing that ever writes the blend parameter --
            // an unassigned driver is a creature that slides everywhere in its idle pose.
            SetProp(aso, "animatorDriver", root.GetComponent<AgentAnimatorDriver>());
            SetFloat(aso, "nearbyAgentScanRadius", 0f);   // no flocking; skips the neighbour scan
            aso.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Makes the arms exist, as far as anything that casts a ray is concerned.
        ///
        /// Before this the creature carried exactly one collider -- a capsule on its centreline,
        /// sized to the head/body sphere. Everything you can SEE outside that radius was empty
        /// air to physics: the shoulders, the upper arms, the forearms and the fists. A grapple
        /// aimed at the shoulder passed straight through and only the eye, which is on the body
        /// column, could be hit. That also made the passenger seat unreachable by the one tool
        /// that could plausibly get a player fifteen metres up.
        ///
        /// Widening the capsule is not the fix. The arms hang a metre clear of the body with
        /// daylight between, so a radius that reached them would be a 4 m cylinder of solid
        /// nothing -- you would grapple to the gap, the melee and chase distances that are all
        /// reasoned off "the capsule is 2.4 m" would silently change, and the machine would
        /// shoulder the player aside from a metre further out. One collider per segment is what
        /// the shape actually is.
        ///
        /// Sized from the MESHES rather than typed. Every other measurement in this file is
        /// derived so that re-exporting the model at a different size corrects it instead of
        /// leaving it quietly wrong, and a hand-typed hitbox is exactly the thing that goes
        /// stale the first time staff.py or hands.py moves a part.
        ///
        /// The colliders go ON the bones, so they follow the animation for free -- no per-frame
        /// component, no second hierarchy to keep in step. They join the root's kinematic
        /// Rigidbody as a compound body, which is also what makes them cheap to move.
        private static void WireLimbColliders(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            foreach ((string boneName, string[] excluding) in ArmColliders)
            {
                Transform bone = all.FirstOrDefault(t => t.name == boneName);
                if (bone == null)
                {
                    Debug.LogWarning($"[LightningConjurer] No bone '{boneName}' to hang a collider " +
                                     "on; that limb stays untouchable. Did rig.py rename it?");
                    continue;
                }

                Transform[] excluded = excluding
                    .Select(name => all.FirstOrDefault(t => t.name == name))
                    .Where(t => t != null)
                    .ToArray();

                if (!TryMeasureInBoneSpace(bone, excluded, out Vector3 centre, out Vector3 size))
                {
                    Debug.LogWarning($"[LightningConjurer] Bone '{boneName}' has no mesh of its " +
                                     "own to size a collider from; skipped.");
                    continue;
                }

                var box = bone.gameObject.AddComponent<BoxCollider>();
                box.center = centre;
                box.size = size;
            }
        }

        /// The snug box around everything drawn by <paramref name="bone"/> itself, in the bone's
        /// OWN space.
        ///
        /// Bone space, not world, and that is the point: an arm bone is rotated to lie along the
        /// arm, so a world-axis-aligned box round a raised forearm is a loose diagonal slab that
        /// gets looser the further the arm swings. Measured against the bone it is snug in every
        /// pose, and because a BoxCollider's centre and size are read in that same space, it
        /// simply follows the animation.
        ///
        /// Mesh bounds rather than <c>Renderer.bounds</c>, for the same reason -- the latter is
        /// already world-axis-aligned and has thrown the orientation away before we see it.
        ///
        /// The rig's bones carry a scale of 100 (the import bakes the model's scale into the
        /// hierarchy), and everything here stays in bone-local units, so that factor cancels
        /// out on both sides and never has to appear as a magic number.
        private static bool TryMeasureInBoneSpace(Transform bone, Transform[] excluded,
                                                  out Vector3 centre, out Vector3 size)
        {
            centre = Vector3.zero;
            size = Vector3.zero;

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            bool any = false;

            foreach (MeshFilter filter in bone.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                if (excluded.Any(e => filter.transform.IsChildOf(e)))
                    continue;

                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 toBone = bone.worldToLocalMatrix * filter.transform.localToWorldMatrix;

                // All eight corners. Transforming the centre and extents alone is only correct
                // when the two spaces are axis-aligned, which is exactly what they are not here.
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 inBone = toBone.MultiplyPoint3x4(point);
                    min = Vector3.Min(min, inBone);
                    max = Vector3.Max(max, inBone);
                    any = true;
                }
            }

            if (!any)
                return false;

            centre = (min + max) * 0.5f;
            size = max - min;
            return true;
        }

        /// Lets a player ride on the machine's shoulder without becoming its problem.
        ///
        /// Composed from the stock mount stack, with one thing deliberately ABSENT:
        ///
        ///   MountModule       the seat, the camera, the rider's component handoff, the dismount.
        ///                     seatBone points it at the rig, which a serialized Transform cannot
        ///                     reach into, and allowAISelfMovementWhenMounted leaves the brain
        ///                     running -- the creature wanders, hunts and casts with a passenger
        ///                     aboard exactly as it does without one.
        ///   MountNetworkSync  replicates the seating, and -- seeing no SteerModule -- leaves the
        ///                     creature owned by the server rather than handing an AI to the
        ///                     passenger's PC. See MountModule.RiderDrives.
        ///   PassengerSeat     hides the rider from this creature's own targeting for as long as
        ///                     they are aboard, holds them on the animated bone, and gives them
        ///                     Escape to get down with.
        ///   MountedRiderPose  sits them, instead of standing them to attention on the shoulder.
        ///
        ///   NO SteerModule    which is the whole point. There is no input path from this seat to
        ///                     the motor, so the passenger is cargo and the machine is still an
        ///                     agent rather than a vehicle.
        ///
        /// Consequence worth knowing before it reads as a bug: a conjurer will not wake for, look
        /// at, chase or strike the player sitting on it, and it will not retaliate if that player
        /// shoots it. The exemption is total and it is scoped to THIS creature -- every other
        /// robot in the world still sees the passenger perfectly well, and will happily fire on a
        /// shoulder this one is carrying.
        private static void WireShoulderSeat(GameObject root)
        {
            // A real child of the prefab root, not a bone: the rider is put down here, so it has to
            // stay at ground level whatever the legs are doing.
            var dismount = new GameObject("DismountPoint");
            dismount.transform.SetParent(root.transform, false);
            dismount.transform.localPosition = DismountOffset;

            var mount = root.AddComponent<MountModule>();
            var mso = new SerializedObject(mount);

            // Resolved by NAME. The rig is a nested prefab instance and a serialized Transform
            // cannot point inside one -- the same reason ConjurerCastModule finds StaffTip this way.
            SetString(mso, "seatBone", SeatBone);
            SetProp(mso, "dismountPoint", dismount.transform);

            // How close you have to be standing to be offered the shoulder.
            //
            // Without it the seat is offered from anywhere the interaction ray reaches any part of
            // the machine, and this machine is one 4.8 m body column from the ground to the head --
            // so every side of it, several metres out, put "ride" on screen and fired the player
            // sixteen metres up onto a shoulder they were nowhere near.
            //
            // Measured horizontally from the seat bone, which sits 3.27 m out from the centreline
            // on the LEFT and 15.75 m up. So the ring this cuts is a circle on the ground under
            // the left arm: pressed against that side of the body the rider is well inside it,
            // stood off the right side they are 5.7 m away and it is closed. Which is the rule
            // that was wanted -- walk round to the arm you intend to sit on.
            SetFloat(mso, "maxMountDistance", ShoulderBoardingRadius);

            // The one flag that makes this a passenger seat rather than a saddle. Off -- the
            // default -- MountModule disables every other behaviour module for the duration, and
            // an 18 m robot with a rider aboard would stand rooted to the spot.
            SetBool(mso, "allowAISelfMovementWhenMounted", true);

            // Third person, and not merely as a preference. Mounted FIRST person gives the rider
            // pitch only -- yaw is the mount's heading, because on a steered mount yaw IS the
            // steering -- so a passenger in first person could look up and down and nowhere else.
            // The orbit camera is the only one that lets them look around.
            SetEnum(mso, "defaultPerspective", (int)MountModule.CameraPerspective.ThirdPerson);
            // Sized to the machine. The boom hangs off the RIDER, so the stock 4.5 m frames a
            // player sitting in mid-air with the thing they are riding out of shot behind them.
            SetFloat(mso, "thirdPersonDistance", 11f);
            SetFloat(mso, "thirdPersonLookAhead", 5f);
            // Yaw-only orbit: this creature walks upright and never pitches, so the horizon should
            // stay level whatever the stride does to the body.
            SetBool(mso, "followMountPitch", false);
            mso.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<MountNetworkSync>();

            var seat = root.AddComponent<PassengerSeat>();
            var pso = new SerializedObject(seat);
            SetProp(pso, "mountModule", mount);
            // In the CARRIER's frame, not the bone's. The bone already puts the rider over the
            // correct shoulder; what is left is a nudge along the machine's own up and forward
            // axes, which mean the same thing whichever way the arm happens to be swung.
            SetVector3(pso, "seatOffset", SeatOffset);
            pso.ApplyModifiedPropertiesWithoutUndo();

            // Sits the rider down. Without it a player rides the shoulder standing bolt upright,
            // which is the pose PlayerMovement.ForceIdleAnimation leaves them in. The stock values
            // are solved against the ostrich's barrel, so the legs are re-angled here to hang off
            // an edge rather than grip round one.
            var pose = root.AddComponent<MountedRiderPose>();
            var rso = new SerializedObject(pose);
            SetProp(rso, "mountModule", mount);
            SetVector3(rso, "upperLegRotation", new Vector3(-72f, 0f, -7f));
            SetVector3(rso, "lowerLegRotation", new Vector3(-72f, 0f, 0f));
            rso.ApplyModifiedPropertiesWithoutUndo();
        }

        // Private [SerializeField] fields are not reachable from an editor script any other
        // way, and making them public purely so this could set them would widen the runtime API
        // for a build-time convenience. A missing name warns loudly rather than silently doing
        // nothing -- a typo here is a tuning value that never lands.
        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[LightningConjurer] {so.targetObject.GetType().Name} has no " +
                                 $"serialized field '{field}'; it was renamed or removed.");
            return p;
        }

        private static void SetProp(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.stringValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static void SetEnum(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.enumValueIndex = value;
        }

        private static void SetVector3(SerializedObject so, string field, Vector3 value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.vector3Value = value;
        }

        private static void AddToTestScene(GameObject prefab)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[LightningConjurer] Scene save declined; prefab " +
                                 "built but not added to the test scene.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Idempotent: replace a previous instance rather than stacking copies.
            GameObject existing = scene.GetRootGameObjects()
                .FirstOrDefault(g => g.name == InstanceName);
            if (existing != null) Object.DestroyImmediate(existing);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = InstanceName;

            // Clear of the Artifact at the origin, well inside the ground plane,
            // and facing +Z towards MovementCamera.
            instance.transform.SetPositionAndRotation(new Vector3(8f, 0f, 0f),
                                                      Quaternion.identity);

            // Without a CameraShaker in the scene the footstep events fire into
            // nothing: CameraShakerHandler.Shake returns null when there is no
            // default shaker, silently. The real player camera prefab
            // ("Assets/Game/Prefabs/Camera/3rd person.prefab") already carries one,
            // but this test scene has plain cameras, so give one a shaker here.
            GameObject[] roots = scene.GetRootGameObjects();
            Camera cam = roots.Select(g => g.GetComponentInChildren<Camera>(true))
                              .FirstOrDefault(c => c != null && c.name == "MovementCamera")
                        ?? roots.Select(g => g.GetComponentInChildren<Camera>(true))
                                .FirstOrDefault(c => c != null);
            if (cam != null && cam.GetComponent<CameraShaker>() == null)
            {
                cam.gameObject.AddComponent<CameraShaker>();
                Debug.Log($"[LightningConjurer] Added a CameraShaker to '{cam.name}' " +
                          "so the footstep shake is visible in the test scene.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
