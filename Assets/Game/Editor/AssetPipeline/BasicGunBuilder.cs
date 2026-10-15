using FirstGearGames.SmoothCameraShaker;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Turns the basic gun into a <see cref="PelletGunArtifact"/>: one hitscan round a press, a
    /// long bright tracer, and the gravel blaster's whole feedback stack (muzzle flash, report,
    /// impact puffs, flinch, recoil, FOV punch, camera kick) scaled down to a rifle.
    ///
    /// <para>
    /// Edits <c>Gun.prefab</c> IN PLACE rather than rebuilding it, unlike
    /// <see cref="GravelBlasterBuilder"/>: the model, grip, colliders, NetworkObject hash and
    /// save id are all already right, and its GUID is what <c>basicgun.asset</c>, the network
    /// prefab list, every NPC roster and every save file point at. Re-runnable — each run strips
    /// what the last one added before adding it again. Tuning belongs in the numbers below, not
    /// in the Inspector.
    /// </para>
    /// <para>
    /// Where it sits beside the gravel blaster (GDC-L1-BAL-0004): the blaster is 150 damage in a
    /// corridor that falls to grit past 15 m; this is 18 a round, ~3 rounds a second, that still
    /// lands at nearly full weight 150 m out. Neither is a strictly better number.
    /// </para>
    /// </summary>
    public static class BasicGunBuilder
    {
        private const string LogTag = "BasicGun";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Guns/Gun.prefab";
        private const string ShakePath = "Assets/Game/ScriptableObjects/Shake/BasicGunShake.asset";

        /// <summary>The prefab's existing muzzle transform, at the front of the barrel.</summary>
        private const string MuzzleName = "barrel";

        /// <summary>Containers this builder owns, so a re-run knows exactly what to replace.</summary>
        private const string MuzzleFxName = "MuzzleFx";
        private const string ShotFxName = "ShotFx";

        /// <summary>
        /// Left over from when this was a <c>Weapon</c>: its magazine (there is no reload in this
        /// game, so thirty rounds made it a gun that stopped working forever) and a second grip
        /// nothing reads.
        /// </summary>
        private static readonly string[] RetiredChildren = { "magazine", "handle2" };

        // ── The shot ──
        private const int PelletCount = 1;
        private const float SpreadAngle = 0.35f;
        private const float Range = 300f;
        private const int RoundDamage = 18;
        private const float FullDamageRange = 150f;
        private const float FarDamageFraction = 0.6f;
        private const float RefireSeconds = 0.3f;
        private const float GunshotNoiseRadius = 45f;

        // ── What a hit does ──
        private const float HitKickSpeed = 2.5f;
        private const float HitKickTilt = 8f;
        private const float StaggerDistance = 0.6f;
        private const float StaggerHeight = 0.15f;
        private const float StaggerSeconds = 0.15f;

        // ── What firing does to the holder ──
        private const float RecoilSpeed = 1.2f;
        private const float RecoilUpwardBias = 0.05f;
        private const float FovKick = 2.5f;
        private const float FovKickSeconds = 0.1f;

        // ── What it looks like ──
        /// <summary>A streak at this speed and stretch is ~7 m long: it reads at 300 m without swallowing a close shot.</summary>
        private const float TracerSpeed = 320f;
        private const float TracerLinger = 0.05f;
        private const float ShakeMagnitude = 0.35f;
        private const float FirstPersonShake = 0.5f;
        private const float ShakeRadius = 15f;

        [MenuItem("Tools/Build Basic Gun Artifact")]
        public static void Build()
        {
            if (!PelletGunFxBuilder.TryLoadMaterials(LogTag, out PelletGunFxBuilder.Materials materials))
                return;

            ShakeData shake = PelletGunFxBuilder.EnsureShake(ShakePath, LogTag);
            if (shake == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform muzzle = root.transform.Find(MuzzleName);
                if (muzzle == null)
                {
                    Debug.LogError($"[{LogTag}] No '{MuzzleName}' child on {PrefabPath}.");
                    return;
                }

                StripPreviousBuild(root, muzzle);

                PelletGunFx fx = BuildFx(root, muzzle, materials, shake);
                ConfigureArtifact(root.AddComponent<PelletGunArtifact>(), fx);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[{LogTag}] Rebuilt {PrefabPath} as a PelletGunArtifact.");
        }

        /// <summary>
        /// Everything a previous build — or the old <c>Weapon</c> version — left behind. The old
        /// <c>BasicGun</c> component's script no longer exists, so it is found as a missing script.
        /// </summary>
        private static void StripPreviousBuild(GameObject root, Transform muzzle)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

            // The artifact first: it references the FX component.
            foreach (PelletGunArtifact artifact in root.GetComponents<PelletGunArtifact>())
                Object.DestroyImmediate(artifact);
            foreach (PelletGunFx fx in root.GetComponents<PelletGunFx>())
                Object.DestroyImmediate(fx);

            DestroyChild(muzzle, MuzzleFxName);
            DestroyChild(root.transform, ShotFxName);
            foreach (string retired in RetiredChildren)
                DestroyChild(root.transform, retired);
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        /// <summary>
        /// A rifle's muzzle: a tight flash of sparks and powder and a small pressure wave, but no
        /// gravel — nothing leaves this barrel except the round.
        /// </summary>
        private static PelletGunFx BuildFx(GameObject root, Transform muzzle,
                                           PelletGunFxBuilder.Materials materials, ShakeData shake)
        {
            Transform muzzleFx = NewContainer(MuzzleFxName, muzzle);
            Transform shotFx = NewContainer(ShotFxName, root.transform);

            PelletGunFx fx = PelletGunFxBuilder.AddFx(root, new PelletGunFxBuilder.Rig
            {
                Muzzle = muzzle,
                MuzzleSparks = PelletGunFxBuilder.BuildSparks(muzzleFx, materials.Spark,
                                                              "MuzzleSparks", count: 28, cone: 9f),
                MuzzleDust = PelletGunFxBuilder.BuildDust(muzzleFx, materials.Smoke, "MuzzleDust",
                                                          count: 10, cone: 10f),
                MuzzleSmoke = PelletGunFxBuilder.BuildMuzzleSmoke(muzzleFx, materials.Smoke,
                                                                  count: 6),
                BlastWave = PelletGunFxBuilder.BuildBlastWave(muzzleFx, materials.Smoke, count: 2,
                                                              sizeScale: 0.45f),
                MuzzleFlash = PelletGunFxBuilder.BuildMuzzleFlash(muzzleFx, range: 8f,
                                                                  intensity: 10f),
                Tracers = PelletGunFxBuilder.BuildTracers(shotFx, materials.Spark,
                                                          minSize: 0.05f, maxSize: 0.07f,
                                                          velocityScale: 0.022f, lengthScale: 2f),
                ImpactSparks = PelletGunFxBuilder.BuildImpactSparks(shotFx, materials.Spark),
                ImpactDust = PelletGunFxBuilder.BuildImpactDust(shotFx, materials.Smoke),
                ImpactDebris = PelletGunFxBuilder.BuildImpactDebris(shotFx, materials.Debris),
                Shake = shake,
            });

            var so = new SerializedObject(fx);
            so.FindProperty("tracerSpeed").floatValue = TracerSpeed;
            so.FindProperty("tracerLinger").floatValue = TracerLinger;
            // One round is one impact; a bigger puff than a single gravel pellet's so a hit at
            // range is visible at all.
            so.FindProperty("impactCounts").vector3IntValue = new Vector3Int(14, 7, 6);
            so.FindProperty("maxImpactsDrawn").intValue = PelletCount;
            so.FindProperty("shakeMagnitude").floatValue = ShakeMagnitude;
            so.FindProperty("firstPersonShake").floatValue = FirstPersonShake;
            so.FindProperty("shakeRadius").floatValue = ShakeRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        private static Transform NewContainer(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ConfigureArtifact(PelletGunArtifact artifact, PelletGunFx fx)
        {
            var so = new SerializedObject(artifact);
            so.FindProperty("fx").objectReferenceValue = fx;

            so.FindProperty("pelletCount").intValue = PelletCount;
            so.FindProperty("spreadAngle").floatValue = SpreadAngle;
            so.FindProperty("range").floatValue = Range;
            so.FindProperty("pelletDamage").intValue = RoundDamage;
            so.FindProperty("fullDamageRange").floatValue = FullDamageRange;
            so.FindProperty("farDamageFraction").floatValue = FarDamageFraction;
            so.FindProperty("refireSeconds").floatValue = RefireSeconds;
            so.FindProperty("gunshotNoiseRadius").floatValue = GunshotNoiseRadius;

            so.FindProperty("fullHitKickSpeed").floatValue = HitKickSpeed;
            so.FindProperty("kickTilt").floatValue = HitKickTilt;
            so.FindProperty("staggerDistance").floatValue = StaggerDistance;
            so.FindProperty("staggerHeight").floatValue = StaggerHeight;
            so.FindProperty("staggerSeconds").floatValue = StaggerSeconds;

            so.FindProperty("recoilSpeed").floatValue = RecoilSpeed;
            so.FindProperty("recoilUpwardBias").floatValue = RecoilUpwardBias;
            so.FindProperty("fovKick").floatValue = FovKick;
            so.FindProperty("fovKickSeconds").floatValue = FovKickSeconds;

            // The report rides reportId, not useSoundId: PlayUse sounds useSoundId on every press,
            // including the ones refireSeconds turns away. See PelletGunArtifact.refireSeconds.
            SetEnum(so, "useSoundId", "None");
            SetEnum(so, "reportId", "WeaponGunFire");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Set an enum field by NAME, so a renumbered SfxId cannot silently pick another sound.</summary>
        private static void SetEnum(SerializedObject so, string field, string valueName)
        {
            SerializedProperty property = so.FindProperty(field);
            int index = System.Array.IndexOf(property.enumNames, valueName);
            if (index < 0)
            {
                Debug.LogError($"[{LogTag}] '{valueName}' is not a value of {field}.");
                return;
            }

            property.enumValueIndex = index;
        }
    }
}
