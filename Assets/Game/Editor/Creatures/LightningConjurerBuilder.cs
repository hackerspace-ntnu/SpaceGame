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
// What that trade actually costs, so nobody re-litigates it by accident: the baked
// walk is NOT foot-locked. _Source~/stride.py measures the planted foot sliding
// across a range of 6.6 to 11.5 m/s about its 8.99 m/s mean, and no amount of
// tuning removes a slide from a clip that has no idea where the ground is. What CAN
// be removed is the systematic half of it -- a clip played at a rate that does not
// match the speed the body is travelling -- and that is what StrideSpeed, the blend
// tree's thresholds and AgentAnimatorDriver.animatorSpeedScale are all for below.
// Slopes and uneven ground are simply not modelled: the agent's transform slides
// along the NavMesh and the feet go where the clip puts them.
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
        private const string ScenePath = "Assets/Game/Scenes/Tests/Marius test scene.unity";
        private const string InstanceName = "LightningConjurer";
        private const string MaterialDir = "Assets/Game/Art/Materials/Palette";
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
        /// MEASURED, not derived. A closed form over the thigh swing alone ignores the
        /// knee: the shin flexes through swing and carries the contact further back than
        /// the hip angle by itself accounts for. _Source~/stride.py samples the planted
        /// foot's actual backward velocity across the stance frames and reports the mean,
        /// which is this number. Re-run it after ANY change to SW, KN or the cycle length
        /// in anim.py, and put the answer here.
        ///
        /// Three things downstream ARE this number, and they have to move together or the
        /// feet skate: RunSpeed below, the blend tree's top threshold, and
        /// AgentAnimatorDriver.animatorSpeedScale. All three are written in terms of this
        /// constant precisely so that re-measuring is a one-line change.
        ///
        /// It does not eliminate the slide -- the clip is not foot-locked, and stride.py
        /// measures the instantaneous speed ranging over 6.6 to 11.5 m/s about this mean.
        /// It removes the systematic part, which is the part that reads as a bug.
        private const float StrideSpeed = 8.99f;

        /// Top speed, and the speed at which the clip plays at its authored rate.
        ///
        /// Pinned to the clip rather than chosen: at exactly StrideSpeed the animator runs
        /// at 1.0 and the walk cycle is the one anim.py authored. Nine metres a second
        /// sounds absurd until you remember the stride is nearly ten metres long and the
        /// cycle is 2.4 seconds -- this is a slow, heavy gait on a machine six times the
        /// player's height, not a sprint.
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
            public Clip(string name, string take, int first, int last)
            {
                Name = name; Take = take; First = first; Last = last;
            }
        }

        private const float Fps = 30f;

        /// Frames where a foot's lowest point reaches the ground in the Walk clip,
        /// measured by _Source~/contacts.py rather than eyeballed. The two sit exactly 36
        /// frames apart, which is half of the 72-frame cycle -- the check that the gait is
        /// actually symmetric.
        private static readonly int[] FootPlantFrames = { 7, 43 };

        // Frame ranges match the actions authored in anim.py. Both are cycles whose
        // last frame duplicates the first, so they loop without a seam. Slowed from
        // 40/90 to 72/120 frames when the creature doubled in size.
        private static readonly Clip[] Clips =
        {
            new Clip("Idle", "ConjurerRig|Idle", 1, 120),
            new Clip("Walk", "ConjurerRig|Walk", 1, 73),
            new Clip("Attack", "ConjurerRig|Attack", 1, AttackFrames),
        };

        /// Length of the Attack clip, straight off the action anim.py authors.
        ///
        /// NOT a cycle, unlike the other two: it runs neutral-to-neutral once, so whatever
        /// it hands back to has nothing to blend away.
        private const int AttackFrames = 90;

        /// Wind-up before the bolt lands, in seconds.
        ///
        /// Derived from the clip rather than typed, and that matters: ConjurerCastModule
        /// times the strike off this number while the Animator times the picture off the
        /// clip. Two hand-entered threes would drift the moment the animation is re-timed,
        /// and the failure -- a bolt landing while the hands are still opening -- looks like
        /// a bug in the VFX rather than a number nobody updated.
        private const float CastSeconds = AttackFrames / Fps;

        /// Measured from the START of a cast, so at 3 s of casting this leaves 2 s of
        /// recovery and the creature throws once every five seconds.
        private const float CastCooldown = 5f;

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
            BuildShakeData();
            ConfigureImporter();
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

            Debug.Log($"[LightningConjurer] Built. Height {TargetHeight:0.00} m " +
                      $"(scale {Scale:0.0000}); walks its baked clip on a NavMeshAgent at " +
                      $"{WalkSpeed:0.00} m/s, runs at {RunSpeed:0.00} m/s, clip authored at " +
                      $"{StrideSpeed:0.00} m/s.");
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
                // Idle and Walk are cycles; Attack is not. Marking a one-shot as looping
                // costs nothing while the exit transition works and hides a spin-forever
                // bug the moment it does not.
                loopTime = c.Name != "Attack",
                loopPose = c.Name != "Attack",
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
        /// There is no Attack state because there is no attack clip -- anim.py authors Idle
        /// and Walk and nothing else -- and no combat module on the prefab to fire one. When
        /// both exist the shape is the one the golem uses: an Attack state entered from Any
        /// State on the trigger CloseCombatModule.attackAnimTrigger names.
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

            // Idle, not Walk: the creature is spawned standing, and a default state that
            // strides on the first frame is visible every time one is placed or loaded.
            root.defaultState = idle;

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

            // Exit time, unlike every other transition here: this one IS about the clip
            // reaching its end rather than about what the motor is doing. Landing back on
            // Idle is safe even if the creature is walking -- Idle's own condition sends it
            // straight on to Walk on the next frame.
            AnimatorStateTransition recover = attack.AddTransition(idle);
            recover.hasExitTime = true;
            recover.exitTime = 1f;
            recover.hasFixedDuration = true;
            recover.duration = 0.25f;

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
            // Tracks the target through the wind-up and lands on it, rather than striking
            // where it stood when the cast began. Flip to WhereItCommitted (1) if the
            // attack starts reading as unavoidable -- see the module's header.
            SetEnum(kso, "aim", (int)CastAim.TracksTarget);

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

            // Long enough to outlast the graph, short enough that spent bolts do not pile
            // up. Lightning.prefab has no self-destruct of its own.
            SetFloat(kso, "vfxLifetime", 5f);
            SetFloat(kso, "drawHeight", DrawHeight);
            // The Animator is on the MODEL CHILD, not on root, so this has to search.
            SetProp(kso, "animator", root.GetComponentInChildren<Animator>(true));
            kso.ApplyModifiedPropertiesWithoutUndo();

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
