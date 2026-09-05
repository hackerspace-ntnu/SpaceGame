using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The one place that says how big every held item is, and why.
    ///
    /// <para><b>The anchor is the Dragon Bazooka at 1.25 m.</b> It was tuned by eye against the
    /// astronaut's hand and kept; everything else is placed relative to it. That is deliberate and
    /// it is not realism — this rig's hand is roughly 1.7x a human's (see the Sucker Puncher's
    /// cavity notes), so a pistol at a real pistol's 0.25 m reads as a toy in it. Sizes are tuned
    /// for the sensation of holding the thing, not for physical accuracy
    /// (<c>GDC-L1-FEEL-0007</c>).</para>
    ///
    /// <para><b>Brackets, not a multiplier.</b> Scaling every item by one factor was considered and
    /// is wrong: the items already at the anchor would overshoot it (a 2x staff is 2.8 m), and the
    /// bazooka the size was chosen from would itself move. So the range compresses upward into
    /// named brackets instead. What a bracket buys is silhouette: a player should be able to tell a
    /// sidearm from a hand tool from a phial at a glance, which a single flat size destroys
    /// (<c>GDC-L1-UX-0003</c>, readability and hierarchy).</para>
    ///
    /// <para><b><see cref="Bracket.Fitted"/> items are pinned and must stay pinned.</b> Four items
    /// are worn against the body rather than gripped, so their size is set by anatomy and an
    /// aesthetic bump would break the fit rather than restyle it — the Sucker Puncher's hand cavity
    /// is measured (its builder says any scaling at all invalidates it), the Repulsor Gauntlet is a
    /// cuff the forearm slides through, the Item Scanner is a forearm terminal whose
    /// <c>holdSize 0</c> deliberately means "keep the size the artist built", and the Wing Pack is
    /// worn on the back. They are listed here with <c>From == To</c> on purpose: the roster is the
    /// whole set of gripped prefabs, so "was this one considered?" is answerable without grepping.
    /// </para>
    ///
    /// <para><b>This ladder is the HAND only.</b> How big an item is lying on the pack is a second,
    /// independent number — <c>ItemGrip.packSize</c>, which falls back to <c>holdSize</c> wherever
    /// nobody authored it. The two questions genuinely differ: this ladder answers "what feels
    /// right to hold in a hand 1.7x a human's", the pack answers "what does this read as among a
    /// dozen others on a finite mat, and how much of that mat is it worth". Four gripped items diverge
    /// today — the Grappling Hook, the Lasso, the Leash and the Portal Gun — plus the whole ship-part
    /// family on one shared 0.8, and <see cref="Audit"/> names any prefab that does. Grep
    /// <c>packSize:</c> for the current set.</para>
    ///
    /// <para>Where those four landed is worth writing down, because it is the only statement
    /// anywhere of what frame the mat is in: 0.27, 0.36, 0.54 and 0.54, against true modelled sizes
    /// of 0.160, 0.267, 0.382 and 0.4445 m. Each is its real size rounded up to the next 0.09 m
    /// webbing pitch plus one cell of margin — so <b>the mat is in true-world metres and this
    /// ladder is not</b>, and an item whose hold size happens to sit near life size (the Dragon
    /// Bazooka, hold 1.25 against a true 1.37) needs no second number at all. The gun needed the
    /// biggest correction on the roster because it carries the biggest inflation: a 0.4445 m fire
    /// extinguisher on the Gun bracket is 2.8x life size.</para>
    ///
    /// <para><b>Idempotent.</b> Every entry names the value it expects to find before it will write,
    /// so a second run reports "already at 1.25" rather than climbing the ladder twice.</para>
    ///
    /// <para><b>Verified out loud.</b> Unity discards prefab saves outright when the AssetDatabase
    /// goes read-only, and says nothing. So the last thing <see cref="Apply"/> does is re-load
    /// every prefab it wrote, off disk, and assert the number actually landed.</para>
    ///
    /// <para>Two of these prefabs are additionally authored by builders in
    /// <c>Assets/Game/Editor/AssetPipeline/</c>, which write <c>holdSize</c> themselves and replace
    /// their prefab wholesale. Their sources carry the same number as this table; changing a value
    /// here means changing it there too, and <see cref="Audit"/> names the ones that apply.</para>
    /// </summary>
    public static class ItemScaleLadder
    {
        /// <summary>How far an item sits up the ladder. Ordering is smallest to largest.</summary>
        private enum Bracket
        {
            /// <summary>Worn against the body. Sized by anatomy — never by this ladder.</summary>
            Fitted,

            /// <summary>Drunk, thrown or channelled. Reads as a phial, not a weapon.</summary>
            Consumable,

            /// <summary>Carried in one hand and used at arm's length.</summary>
            HandTool,

            /// <summary>Two hands or a braced arm; bulky enough to change the silhouette.</summary>
            BigTool,

            /// <summary>Shoots. The bracket the anchor's size was asked for by name.</summary>
            Gun,

            /// <summary>The anchor itself, and what already matches it.</summary>
            Anchor,
        }

        /// <summary>
        /// The size of the item the whole ladder is measured against, in metres. Every other
        /// bracket is a fraction of this rather than a number of its own, so retuning the anchor
        /// moves the family with it instead of stranding it.
        /// </summary>
        private const float AnchorSize = 1.25f;

        /// <summary>Metres of slop when matching a table's expected value against a prefab.</summary>
        private const float Slack = 1e-3f;

        /// <summary>
        /// Why all seven gauntlets sit at zero, in one place rather than seven near-identical
        /// sentences. Zero is not "unset" to <c>ItemGrip</c>: it means "keep the size the artist
        /// built", which is the whole point of a family authored at the suit's true scale — and
        /// since 2026-09-04 it is what the mat uses too, because the bracer that made a gauntlet
        /// too bulky to lie down is worn permanently and is not part of the item any more.
        /// </summary>
        private const string GauntletWhy =
            "PINNED at 0: a forearm gauntlet since 2026-09-02, authored against the deck of the " +
            "bracer the player wears and worn at GauntletFit scale 1, so 'the size the artist " +
            "built' IS the size — on the arm and, since the bracer left the model on 2026-09-04, " +
            "on the mat as well. See GauntletPrefab.PackSize";

        private readonly struct Step
        {
            public readonly string Path;
            public readonly Bracket Bracket;

            /// <summary>The value this entry refuses to run against anything but.</summary>
            public readonly float From;

            /// <summary>Where it lands. Equal to <see cref="From"/> for a pinned item.</summary>
            public readonly float To;

            public readonly string Why;

            public Step(string path, Bracket bracket, float from, float to, string why)
            {
                Path = path;
                Bracket = bracket;
                From = from;
                To = to;
                Why = why;
            }

            public bool IsPinned => Mathf.Abs(To - From) <= Slack;
        }

        private const string Gadgets = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/";
        private const string Guns = "Assets/Game/Prefabs/Items/Artifacts/Guns/";
        private const string Supplies = "Assets/Game/Prefabs/Items/Supplies/";

        /// <summary>
        /// Why the three oxygen-plant supplies share one size, in one place rather than three
        /// near-identical sentences.
        ///
        /// <para>
        /// A pressure bottle (true 0.54 m) and a slab battery (true 0.55 m) are within 2% of each
        /// other, so one bracket is honest: what tells them apart is silhouette — a cylinder with
        /// an orange collar against a green-cornered brick — which is exactly what a bracket is for
        /// (<c>GDC-L1-UX-0003</c>). 0.90 is two thirds of the anchor: bulky enough to read as
        /// two-handed hardware and clear of the guns above it. Both are hugged rather than gripped,
        /// so they carry <c>HoldStyle.TwoHanded</c>.
        /// </para>
        /// <para>
        /// PINNED because <c>OxygenGearBuilder</c> writes these prefabs wholesale on every run and
        /// authors this same number; moving it means moving it there.
        /// </para>
        /// </summary>
        private const string SupplyWhy =
            "PINNED at 0.90, authored by OxygenGearBuilder: a two-handed 0.54 m bottle and a " +
            "0.55 m battery in a hand 1.7x a human's, two thirds of the anchor. The bottle is " +
            "turned a quarter about X inside its prefab so it LIES DOWN on the mat, and the " +
            "grip's rotationOffset carries the exact inverse — so this hand size, and the pose " +
            "it was tuned with, are untouched by that. Its share of the mat is a separate " +
            "number — see PackSizeTests";

        /// <summary>
        /// Every prefab in the project that carries an <see cref="ItemGrip"/>, with the size it
        /// lands on and the reason. Pinned entries carry <c>From == To</c>.
        /// </summary>
        private static readonly Step[] Ladder =
        {
            // ── Anchor: already the size that was asked for ───────────────────────────────
            new(Gadgets + "DragonBazooka.prefab", Bracket.Anchor, AnchorSize, AnchorSize,
                "the item the whole ladder is measured against; tuned by eye and kept"),

            new(Gadgets + "LaserStaff.prefab", Bracket.Anchor, 1.35f, 1.35f,
                "a staff is meant to out-reach the launcher, and 1.35 already does; " +
                "it stands on end in the pack rather than lying down, so its length costs no mat"),

            // ── Guns: the bracket the anchor's size was asked for by name ─────────────────
            new(Guns + "Gun.prefab", Bracket.Gun, 0.60f, AnchorSize,
                "the starting sidearm read as a toy at 0.60 against a 1.25 launcher"),

            new(Guns + "CixinGunEquipped.prefab", Bracket.Gun, 0.70f, 1.20f,
                "ball lightning is a sidearm, so it sits just under the launcher rather than level " +
                "with it — the one place in this bracket where silhouette beats uniformity"),

            new("Assets/Game/Prefabs/Items/Artifacts/Portals/PortalGun.prefab", Bracket.Gun,
                0.60f, AnchorSize,
                "two-handed and held by a top handle with the body hanging below, so it carries " +
                "the full size without the grip point moving"),

            new(Gadgets + "GravelBlaster.prefab", Bracket.Gun, 1.05f, AnchorSize,
                "was already within a fifth of the anchor; the gap read as an accident, not a class"),

            new(Gadgets + "RobotPistolModel.prefab", Bracket.Gun, 0.45f, 1.10f,
                "a pistol, so it stays the smallest thing in the bracket while still reading as a gun"),

            // ── Hand tools: one hand, used at arm's length ────────────────────────────────

            new(Gadgets + "RocketArtifact.prefab", Bracket.HandTool, 0.40f, 0.65f,
                "a turret carried to where it is put down; the same bulk as the scanner"),

            new(Gadgets + "Lasso.prefab", Bracket.HandTool, 0.30f, 0.60f,
                "sized on its Coil via sizeReference, so the rope follows the handle rather than " +
                "the handle shrinking to fit the rope"),


            // ── Big tools: two hands, and bulky enough to change the silhouette ───────────
            new(Supplies + "OxygenTank.prefab", Bracket.BigTool, 0.90f, 0.90f, SupplyWhy),
            new(Supplies + "Battery.prefab", Bracket.BigTool, 0.90f, 0.90f, SupplyWhy),

            // ── Consumables: read as a phial, not a weapon ────────────────────────────────
            new(Gadgets + "AntiGravityPotion.prefab", Bracket.Consumable, 0.30f, 0.50f,
                "a bottle this hand could plausibly drink from; deliberately the bottom of the ladder"),

            new(Gadgets + "LightningSpell.prefab", Bracket.Consumable, 0.30f, 0.50f,
                "matched to the potion — both are held Relaxed and should read as the same class"),

            // ── Fitted: sized by anatomy. Do not move these. ──────────────────────────────
            // The six gauntlets are one family and answer this question once, in
            // GauntletPrefab.HoldSize. Their models are built on components/props/gauntlet_base.blend,
            // which is modelled against the rig's own forearm, so a hold size here would be a
            // second opinion about how big an arm is. They keep separate rows rather than a loop
            // because the roster is meant to be the whole set of gripped prefabs, readable as a
            // list: "was this one considered?" is answerable without grepping.
            new(Gadgets + "SuckerPuncher.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),
            new(Gadgets + "RepulsorGauntlet.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),
            new(Gadgets + "ItemScanner.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),
            new(Gadgets + "GrapplingHook.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),
            new(Gadgets + "Leash.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),
            new(Gadgets + "RuinScanner.prefab", Bracket.Fitted, 0f, 0f, GauntletWhy),

            new("Assets/Game/Prefabs/Items/Equipment/WingPack.prefab", Bracket.Fitted, 1.26f, 1.26f,
                "PINNED: worn across the back, so its span is the wearer's, not the ladder's"),
        };

        // ── Menu ─────────────────────────────────────────────────────────────

        [MenuItem("Tools/SpaceGame/Items/Apply Item Scale Ladder")]
        public static void Apply()
        {
            var log = new StringBuilder("Item scale ladder\n");
            int changed = 0;
            int pinned = 0;

            foreach (Step step in Ladder)
            {
                if (step.IsPinned)
                {
                    pinned++;
                    continue;
                }

                if (ApplyOne(step, log)) changed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Every measurement is cached per prefab for the life of a session, and the sizes they
            // were taken from just moved. Without this the pack lays gear out at yesterday's size.
            ItemFootprint.ClearCache();

            log.Append("  changed  ").Append(changed).Append(" of ")
               .Append(Ladder.Length - pinned).Append(" movable (").Append(pinned)
               .Append(" pinned as fitted)\n");

            if (Verify(log)) Debug.Log(log.ToString());
        }

        /// <summary>Report where every item sits and what would move, without writing anything.</summary>
        [MenuItem("Tools/SpaceGame/Items/Audit Item Scale Ladder")]
        public static void Audit()
        {
            var log = new StringBuilder("Item scale ladder (audit only)\n");

            foreach (Step step in Ladder)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(step.Path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(step.Path);

                if (prefab == null)
                {
                    log.Append("  MISSING  ").Append(step.Path).Append('\n');
                    continue;
                }

                var grip = prefab.GetComponent<ItemGrip>();

                if (grip == null)
                {
                    log.Append("  ").Append(name).Append("\n    NO GRIP  nothing to size\n");
                    continue;
                }

                // ItemFootprint measures in the PACK's frame, which is PackScale.Factor larger
                // than the hand's — so this row is deliberately not holdSize x the prefab's
                // proportions, and reading it as one is how somebody "corrects" a bracket that was
                // already right.
                Vector3 size = ItemFootprint.SizeOf(prefab);

                log.Append("  ").Append(name).Append("  [").Append(step.Bracket).Append("]\n")
                   .Append("    now      holdSize ").Append(grip.HoldSize.ToString("F3"))
                   .Append(", size on the mat ").Append(size.ToString("F3")).Append('\n');

                // Only when it actually diverges. Printing "packSize == holdSize" on sixteen of
                // nineteen rows would bury the three that carry a real second number.
                if (Mathf.Abs(grip.PackSize - grip.HoldSize) > Slack)
                    log.Append("    on pack  packSize ").Append(grip.PackSize.ToString("F3"))
                       .Append(" — deliberately not the hand size\n");

                log.Append(step.IsPinned ? "    pinned   " : "    would    ")
                   .Append(step.IsPinned
                       ? "left alone"
                       : $"{step.From:F3} -> {step.To:F3}")
                   .Append("\n    because  ").Append(step.Why).Append('\n');
            }

            Debug.Log(log.ToString());
        }

        // ── One prefab ───────────────────────────────────────────────────────

        private static bool ApplyOne(Step step, StringBuilder log)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(step.Path);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(step.Path) == null)
            {
                log.Append("  MISSING  ").Append(step.Path).Append('\n');
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(step.Path);

            if (contents == null)
            {
                log.Append("  FAILED   could not open ").Append(step.Path).Append('\n');
                return false;
            }

            try
            {
                var grip = contents.GetComponent<ItemGrip>();

                if (grip == null)
                {
                    log.Append("  ").Append(name)
                       .Append("\n    SKIPPED  no ItemGrip to size\n");
                    return false;
                }

                if (Mathf.Abs(grip.HoldSize - step.From) > Slack)
                {
                    log.Append("  ").Append(name).Append("\n    SKIPPED  holdSize is ")
                       .Append(grip.HoldSize.ToString("F3")).Append(", expected ")
                       .Append(step.From.ToString("F3"))
                       .Append(Mathf.Abs(grip.HoldSize - step.To) <= Slack
                           ? " — already at its target\n"
                           : " — retuned since this table was written\n");
                    return false;
                }

                var serialized = new SerializedObject(grip);
                SerializedProperty property = serialized.FindProperty("holdSize");

                if (property == null)
                {
                    log.Append("  ").Append(name)
                       .Append("\n    FAILED   ItemGrip has no 'holdSize' field any more\n");
                    return false;
                }

                property.floatValue = step.To;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, step.Path, out bool success);

                if (!success)
                {
                    log.Append("  FAILED   could not save ").Append(step.Path)
                       .Append(" — is the AssetDatabase read-only?\n");
                    return false;
                }

                log.Append("  ").Append(name).Append("  [").Append(step.Bracket).Append("]\n")
                   .Append("    scaled   ").Append(step.From.ToString("F3")).Append(" -> ")
                   .Append(step.To.ToString("F3"))
                   .Append("\n    because  ").Append(step.Why).Append('\n');
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ── Proof ────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-read every prefab off disk and confirm it carries the size the table asked for.
        /// A read-only AssetDatabase drops prefab saves without raising anything, so a run that
        /// reports success having written nothing is a real outcome this has to catch.
        /// </summary>
        private static bool Verify(StringBuilder log)
        {
            var problems = new StringBuilder();

            foreach (Step step in Ladder)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(step.Path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(step.Path);

                if (prefab == null)
                {
                    problems.Append("    ").Append(name).Append(" is missing from disk\n");
                    continue;
                }

                var grip = prefab.GetComponent<ItemGrip>();

                if (grip == null)
                {
                    problems.Append("    ").Append(name).Append(" has no ItemGrip\n");
                    continue;
                }

                if (Mathf.Abs(grip.HoldSize - step.To) > Slack)
                {
                    problems.Append("    ").Append(name).Append(" reads ")
                            .Append(grip.HoldSize.ToString("F3")).Append(" on disk, expected ")
                            .Append(step.To.ToString("F3")).Append('\n');
                }
            }

            if (problems.Length == 0)
            {
                log.Append("  VERIFIED all ").Append(Ladder.Length)
                   .Append(" prefabs carry their ladder size on disk\n");
                return true;
            }

            log.Append("  NOT VERIFIED\n").Append(problems);
            Debug.LogError(log.ToString());
            return false;
        }
    }
}
