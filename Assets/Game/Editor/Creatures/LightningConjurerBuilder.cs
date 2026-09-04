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

        /// The two generated clips, kept beside the controller that is the only thing
        /// referencing them. Regenerated wholesale on every build, like everything else here.
        private const string SleepClipPath = ControllerDir + "/LightningConjurer_Sleep.anim";
        private const string AwakenClipPath = ControllerDir + "/LightningConjurer_Awakening.anim";
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

        /// Crossfade between Idle and Walk. Long enough not to snap, short enough that the
        /// idle hover is not visibly mixed into the first stride -- which is the whole
        /// reason those are two states rather than two children of one blend tree.
        private const float LocomotionBlend = 0.25f;

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
            // Sleep and Awakening are NOT here. They are generated -- see BuildEyeClips --
            // because the only thing that moves in either is the eyelid's blend shapes, and
            // Blender exports shape-key animation as its own FBX take that Unity's clip
            // slicer cannot reach. anim.py's header says the same from the other side.
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
        private static AnimationEvent[] FootPlantEvents()
        {
            return FootPlantFrames.Select(f => new AnimationEvent
            {
                time = (f - 1) / Fps,
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

            BuildEyeClips();
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
            BuildEyeClips();
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
                               "Awakening state - not reporting success. BuildEyeClips failed " +
                               "to write them, so the creature would spawn into an empty entry " +
                               "state and never move again.");
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

        /// Authors the Sleep and Awakening clips, which the FBX cannot carry.
        ///
        /// WHY THESE ARE GENERATED AND THE OTHER THREE ARE NOT. The only thing that animates in
        /// either is the Eyelid's two blend shapes, and Blender exports shape-key animation as
        /// its OWN FBX take -- "Key|ConjurerRig|Idle" and friends -- one per (object, action)
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
        private static void BuildEyeClips()
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
        /// 0 and the stroll at WalkSpeed, every departure spent the ~1.1 s the NavMeshAgent
        /// takes to accelerate somewhere inside that mixture, which is the "it plays idle
        /// and walk at the same time" this replaces.
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
        /// The graph is five states and it is deliberately not symmetric:
        ///
        ///     [entry] -> Sleep -> Awakening -> Idle <-> Walk
        ///                                       ^        ^
        ///                                       +- Attack +      (from Any State, gated Awake)
        ///
        /// Sleep and Awakening are entered once, in that order, and never again -- see the
        /// one-way note where they are built. Everything after them is the usual locomotion
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
            AnimatorStateTransition start = idle.AddTransition(walking);
            start.hasExitTime = false;
            start.hasFixedDuration = true;
            start.duration = LocomotionBlend;
            start.AddCondition(
                AnimatorConditionMode.Greater, MoveEnterSpeed, ForwardSpeedParameter);

            AnimatorStateTransition stop = walking.AddTransition(idle);
            stop.hasExitTime = false;
            stop.hasFixedDuration = true;
            stop.duration = LocomotionBlend;
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

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = TargetHeight;
            capsule.radius = BlenderBodyWidth * Scale * 0.5f;   // tracks the model, not a magic number
            capsule.center = new Vector3(0f, TargetHeight * 0.5f, 0f);

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
        /// NOT here: NetworkedHealthComponent. This creature has no HealthComponent at all yet,
        /// so there is no health to replicate; it can hurt a player and cannot be hurt back.
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

        private static void WireMotor(GameObject root, Animator animator)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;
            agent.angularSpeed = 45f;
            // A time constant in disguise: the agent sheds speed at this rate, so 4 is
            // roughly a quarter-second run-down and about a metre of coast at the stroll.
            // Lower reads as mass and overshoots every destination by half a stride.
            agent.acceleration = 4f;
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
            SetFloat(adso, "measuredRunSpeed", (WalkSpeed + RunSpeed) * 0.5f);
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
