# Visor 1 — Foundation & Health Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish `VisorStyle` as the visor's one design language, and move the player's health readout into the helmet layer as a reusable gauge, with a three-state **H** toggle that can no longer blind the player.

**Architecture:** `HelmetHUDController` becomes the root of the visor and spawns two sublayers — **Vitals** (things you play by) and **Annotations** (things that describe the world). A new `VisorStyle` static owns palette, type ramp, geometry and runtime-generated sprites, mirroring how `UITheme` caches its own. `VisorGauge` is a reusable readout bound to an `IVisorGaugeSource`; this plan wires one instance to `HealthComponent` and deletes `HealthUI` along with the authored health objects in `PlayerHUD.prefab`.

**Tech Stack:** Unity 6000.3.11f1, uGUI + TextMeshPro, EditMode NUnit, no imported UI art.

**Spec:** [`docs/superpowers/specs/2026-09-04-helmet-visor-ui-design.md`](../specs/2026-09-04-helmet-visor-ui-design.md) — sections 1, 6, 7, 8, 9.

---

## Before you start — how this repo verifies

Three things, and none of them are what you expect from a normal C# project:

| Goal | Command | Notes |
| --- | --- | --- |
| **Fast compile check** | `python3 tools/typecheck.py` | Prints `No errors.` Use this after every code step. Seconds, no Editor needed. |
| **Run one test fixture** | Via unity-mcp: `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("VisorStyleTests")` | Needs a live Unity Editor. **There is no `unity -batchmode -runTests` path in this repo — do not invent one.** |
| **Read the verdict** | `cat Temp/headless_tests.txt` | Absence of the file means *still running*. Presence of `DONE` means finished. Poll; never assume. |

**Test placement is not free choice.** `Assets/Game/Tests/EditMode/` has an asmdef that **cannot reference `Assembly-CSharp`**, and everything in this plan lives in `Assembly-CSharp`. All tests here therefore go in **`Assets/Game/Editor/Tests/`**, which has no asmdef and auto-references `Assembly-CSharp`, `UnityEditor` and `nunit.framework`.

**`Awake` and `Start` do not run** on a `new GameObject().AddComponent<T>()` in an EditMode test. Any component you want to test must expose the logic through a method the test can call directly.

**Commits are blocked for Claude by a repo hook.** The commit steps below are written out so a human (or an allowed session) can run them. If you are an agent and the hook fires, leave the work staged and say so — do not work around it.

---

## File structure

| File | Responsibility |
| --- | --- |
| **Create** `Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs` | The visor's one design language: palette, type ramp, geometry, generated sprites. No behaviour. |
| **Create** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs` | What a gauge reads: current, max, label, and the thresholds. |
| **Create** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs` | One reusable gauge: label, track, hatched danger zone, number. Draws itself. |
| **Create** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs` | Adapts `HealthComponent` to `IVisorGaugeSource`. |
| **Create** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs` | The restrained parallax lag applied to the layer root. |
| **Create** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs` | The spawn sweep. Purely visual; never gates input. |
| **Modify** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs` | Becomes the visor root: spawns sublayers + modules, stops binding health. |
| **Modify** `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs` | **H** becomes three-state; persists through `GameSettings`. |
| **Modify** `Assets/Game/Scripts/Core/Settings/GameSettings.cs` | Adds `VisorDetail` + `ReduceVisorMotion`; bumps `SchemaVersion`. |
| **Delete** `Assets/Game/Scripts/Presentation/UI/HUD/HealthUI.cs` (+ `.meta`) | Superseded by `VisorGauge`. Its serialized-reference wiring is a documented failure mode. |
| **Modify** `Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab` | Remove the authored `Health` / `HealthBar` / `HealthText` / `maxHealthText` objects. |
| **Create** `Assets/Game/Editor/Tests/VisorStyleTests.cs` | Sprite caching, palette sourcing. |
| **Create** `Assets/Game/Editor/Tests/VisorGaugeTests.cs` | Fraction maths, threshold states, colour-is-never-alone. |
| **Create** `Assets/Game/Editor/Tests/VisorVisibilityTests.cs` | Three-state cycle, and that Vitals survives the middle state. |
| **Create** `Assets/Game/Editor/Tests/PlayerHudWiringTests.cs` | The prefab no longer carries `HealthUI`, and does carry the visor root. |

---

## Task 1: `VisorStyle`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs`
- Test: `Assets/Game/Editor/Tests/VisorStyleTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/VisorStyleTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

public class VisorStyleTests
{
    [Test]
    public void TrackSpriteIsCachedPerHeight()
    {
        // UITheme.Rounded is cached per radius because a 9-sliced sprite whose border exceeds
        // its rect draws its corners over each other. The same rule applies here, and an
        // uncached generator would also leak a texture per frame.
        Sprite first = VisorStyle.Track(6);
        Sprite again = VisorStyle.Track(6);
        Sprite other = VisorStyle.Track(10);

        Assert.AreSame(first, again, "Same height must return the same cached sprite.");
        Assert.AreNotSame(first, other, "A different height is a different sprite.");
    }

    [Test]
    public void AlarmColoursComeFromTheModelLibraryPalette()
    {
        // Lifted from Assets/Game/Art/Models/_Source~/PALETTE.md so the visor's alarm is the
        // same amber that glows on the rig. If these drift, the HUD stops matching the world.
        AssertHex(VisorStyle.Alarm, 1f, 0.702f, 0.278f);      // Mat_Emissive_Amber   #FFB347
        AssertHex(VisorStyle.Critical, 0.851f, 0.329f, 0.122f); // Mat_Paint_Safety_Orange #D9541F
    }

    [Test]
    public void InkIsTheOnlyColourUsedForNormalReadouts()
    {
        // The whole point of "one coherent look": nothing warm may appear outside an alarm.
        Assert.AreNotEqual(VisorStyle.Ink, VisorStyle.Alarm);
        Assert.Greater(VisorStyle.Ink.b, VisorStyle.Ink.r, "Ink must read as blue.");
        Assert.Greater(VisorStyle.Alarm.r, VisorStyle.Alarm.b, "Alarm must read as warm.");
    }

    private static void AssertHex(Color actual, float r, float g, float b)
    {
        Assert.AreEqual(r, actual.r, 0.002f);
        Assert.AreEqual(g, actual.g, 0.002f);
        Assert.AreEqual(b, actual.b, 0.002f);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Via unity-mcp: `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("VisorStyleTests")`
Then poll: `cat Temp/headless_tests.txt`
Expected: the file reports a compile failure — `VisorStyle` does not exist yet.

- [ ] **Step 3: Write `VisorStyle`**

Create `Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The visor's design language — the third in the project, and the one that governs
    /// everything drawn on the inside of the helmet.
    ///
    /// <para>
    /// <see cref="UITheme"/> is the look of full-screen MENUS you read. <see cref="VisorStyle"/>
    /// is light projected on glass a few centimetres from the player's eye: thin strokes, wide
    /// tracking, everything glowing slightly, and one colour.
    /// </para>
    /// <para>
    /// <b>Blue is the language; warm is the alarm.</b> <see cref="Ink"/> draws every normal
    /// readout. <see cref="Alarm"/> and <see cref="Critical"/> are spent ONLY on danger, which is
    /// what makes an alarm unmissable without being loud — nothing else on the visor is ever warm.
    /// The two warm values are lifted from the model library's material table
    /// (<c>Assets/Game/Art/Models/_Source~/PALETTE.md</c>) rather than invented, so the amber on
    /// the visor is the amber that glows on the rig. The hex is written beside each because that
    /// table is the source and this is a copy of it.
    /// </para>
    /// <para>
    /// Colour is never the only signal (<c>GDC-L1-UX-0003</c>, <c>GDC-L1-UX-0006</c>): every alarm
    /// state also changes shape — see <see cref="HatchSprite"/> — and wording. Nothing here is an
    /// imported asset, for the reason <see cref="UITheme"/> gives at length.
    /// </para>
    /// </summary>
    public static class VisorStyle
    {
        // ── The palette ──────────────────────────────────────────────────────

        /// <summary>The one colour of the visor. Every normal readout is drawn in it.</summary>
        public static readonly Color Ink = new(0.478f, 0.831f, 1f, 1f);          // #7AD4FF

        /// <summary>Ink at reading weight for secondary rows — chat, expired messages.</summary>
        public static readonly Color InkDim = new(0.478f, 0.831f, 1f, 0.62f);

        /// <summary>Ink at the edge of legibility. Tracks, hairlines, empty gauge.</summary>
        public static readonly Color InkFaint = new(0.478f, 0.831f, 1f, 0.16f);

        /// <summary>Mat_Emissive_Amber, #FFB347. A gauge past its warning threshold.</summary>
        public static readonly Color Alarm = new(1f, 0.702f, 0.278f, 1f);

        /// <summary>Mat_Paint_Safety_Orange, #D9541F. Critical only: damage arcs, alarms.</summary>
        public static readonly Color Critical = new(0.851f, 0.329f, 0.122f, 1f);

        // ── Type ramp ────────────────────────────────────────────────────────
        //
        // Four sizes, in reference pixels at UIScale's 1920x1080. Wide tracking on the small
        // sizes is what makes uppercase labels read as machine print rather than as shouting.

        /// <summary>Uppercase field labels: "O2 SUPPLY". Wide tracking.</summary>
        public const int LabelSize = 15;

        /// <summary>Message and chat rows.</summary>
        public const int BodySize = 17;

        /// <summary>The big number on a gauge.</summary>
        public const int ReadoutSize = 38;

        /// <summary>Distances on markers, units beside a readout.</summary>
        public const int MicroSize = 13;

        /// <summary>Tracking applied to <see cref="LabelSize"/> text, in TMP units.</summary>
        public const float LabelTracking = 12f;

        // ── Geometry ─────────────────────────────────────────────────────────

        /// <summary>Stroke weight of every line the visor draws, in reference pixels.</summary>
        public const float Stroke = 1.5f;

        /// <summary>Height of a gauge's track.</summary>
        public const int TrackHeight = 6;

        /// <summary>Width of a gauge, label and number included.</summary>
        public const float GaugeWidth = 250f;

        /// <summary>Margin from the canvas edge to any pinned readout.</summary>
        public const float ScreenMargin = 64f;

        // ── Motion ───────────────────────────────────────────────────────────
        //
        // "Alive, restrained." Motion is a signal, not a texture: idle movement stays under the
        // threshold of noticing so that the movement which MEANS something still reads.
        // GDC-L1-FEEL-0004's recorded disagreement is the constraint — reflexive juice obscures
        // game state. Every value here is deliberately small.

        /// <summary>How far the layer lags behind a head turn, in reference pixels at full rate.</summary>
        public const float SwayPixels = 9f;

        /// <summary>How quickly the layer catches back up, per second.</summary>
        public const float SwayRecovery = 6f;

        /// <summary>Seconds a changed readout stays bloomed.</summary>
        public const float BloomSeconds = 0.18f;

        /// <summary>Brightness multiplier at the peak of a bloom.</summary>
        public const float BloomStrength = 1.9f;

        /// <summary>Seconds the boot sweep takes. Purely visual — it never gates input.</summary>
        public const float BootSeconds = 0.7f;

        // ── Generated sprites ────────────────────────────────────────────────
        //
        // Cached per parameter for the reason UITheme.Rounded is: a generator called from a draw
        // path allocates a texture per call, and a 9-sliced sprite whose border exceeds its rect
        // draws its corners over each other.

        private static readonly Dictionary<int, Sprite> trackByHeight = new();
        private static Sprite hatchSprite;
        private static Sprite bracketSprite;

        /// <summary>A gauge track: a rounded capsule of the given height.</summary>
        public static Sprite Track(int height)
        {
            height = Mathf.Clamp(height, 2, 64);
            if (trackByHeight.TryGetValue(height, out Sprite cached) && cached != null) return cached;

            Sprite made = Capsule(height, $"Visor_Track{height}");
            trackByHeight[height] = made;
            return made;
        }

        /// <summary>
        /// Diagonal hatching. This is the SHAPE half of an alarm — the danger zone that appears on
        /// a gauge's track when it crosses its threshold, so the state is legible without colour.
        /// </summary>
        public static Sprite HatchSprite => Ensure(ref hatchSprite, () => Hatch(32, "Visor_Hatch"));

        /// <summary>One corner of the interaction bracket. Rotated into the other three.</summary>
        public static Sprite BracketSprite => Ensure(ref bracketSprite, () => BracketCorner(32, "Visor_Bracket"));

        private static Sprite Ensure(ref Sprite cached, System.Func<Sprite> make)
        {
            if (cached == null) cached = make();
            return cached;
        }

        private static Sprite Capsule(int height, string name)
        {
            int size = Mathf.NextPowerOfTwo(height * 2);
            var tex = NewTexture(size, name);
            float radius = height * 0.5f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance to the capsule's spine, which runs horizontally through the middle.
                float dy = Mathf.Abs(y - size * 0.5f + 0.5f);
                float alpha = Mathf.Clamp01(radius - dy + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            tex.Apply();
            int border = Mathf.Max(1, height / 2);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, 0, border, 0));
        }

        private static Sprite Hatch(int size, string name)
        {
            var tex = NewTexture(size, name);
            tex.wrapMode = TextureWrapMode.Repeat;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // A 45-degree stripe, on for a third of its period.
                int band = (x + y) % 8;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, band < 3 ? 1f : 0f));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BracketCorner(int size, string name)
        {
            var tex = NewTexture(size, name);
            int arm = Mathf.Max(2, Mathf.RoundToInt(Stroke));

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onCorner = x < arm || y < arm;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, onCorner ? 1f : 0f));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`

- [ ] **Step 5: Run the test to verify it passes**

Via unity-mcp: `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("VisorStyleTests")`
Then: `cat Temp/headless_tests.txt`
Expected: `PASSED=3 FAILED=0`, then `DONE`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs \
        Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs.meta \
        Assets/Game/Editor/Tests/VisorStyleTests.cs \
        Assets/Game/Editor/Tests/VisorStyleTests.cs.meta
git commit -m "feat(ui): VisorStyle — the visor's design language

Blue is the language, warm is the alarm. Alarm colours lifted from the
model library palette so the HUD matches the rig. Sprites generated at
runtime and cached per parameter, as UITheme does."
```

---

## Task 2: `IVisorGaugeSource` and `VisorGauge`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs`
- Create: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs`
- Test: `Assets/Game/Editor/Tests/VisorGaugeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/VisorGaugeTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

public class VisorGaugeTests
{
    private sealed class FakeSource : IVisorGaugeSource
    {
        public float Current { get; set; }
        public float Max { get; set; } = 100f;
        public string Label => "TEST";
        public float WarnFraction => 0.30f;
        public float AlarmFraction => 0.10f;
        public bool Available => true;
    }

    [Test]
    public void FractionIsSafeWhenMaxIsZero()
    {
        // A gauge bound to a source that has not spawned yet must not divide by zero and must
        // not draw a full bar, which would read as "you are fine" at the worst possible moment.
        var source = new FakeSource { Current = 5f, Max = 0f };
        Assert.AreEqual(0f, VisorGauge.FractionOf(source));
    }

    [Test]
    public void FractionIsClampedToTheTrack()
    {
        var source = new FakeSource { Current = 250f, Max = 100f };
        Assert.AreEqual(1f, VisorGauge.FractionOf(source));
    }

    [Test]
    public void ThresholdsSelectTheState()
    {
        var source = new FakeSource { Max = 100f };

        source.Current = 80f;
        Assert.AreEqual(VisorGauge.State.Normal, VisorGauge.StateOf(source));

        source.Current = 25f;
        Assert.AreEqual(VisorGauge.State.Warning, VisorGauge.StateOf(source));

        source.Current = 5f;
        Assert.AreEqual(VisorGauge.State.Critical, VisorGauge.StateOf(source));
    }

    [Test]
    public void ExactlyOnTheThresholdIsNotYetTheWorseState()
    {
        // A gauge that flips at exactly 30% flickers between two states while a value hovers
        // there. The boundary belongs to the calmer state.
        var source = new FakeSource { Max = 100f, Current = 30f };
        Assert.AreEqual(VisorGauge.State.Normal, VisorGauge.StateOf(source));
    }

    [Test]
    public void EveryAlarmStateChangesShapeAndWordNotOnlyColour()
    {
        // GDC-L1-UX-0003: never encode critical information in colour alone. A colourblind
        // player must be able to read the state from the hatching and the suffix.
        Assert.IsFalse(VisorGauge.ShowsHatch(VisorGauge.State.Normal));
        Assert.IsTrue(VisorGauge.ShowsHatch(VisorGauge.State.Warning));
        Assert.IsTrue(VisorGauge.ShowsHatch(VisorGauge.State.Critical));

        Assert.IsEmpty(VisorGauge.SuffixFor(VisorGauge.State.Normal));
        Assert.IsNotEmpty(VisorGauge.SuffixFor(VisorGauge.State.Warning));
        Assert.IsNotEmpty(VisorGauge.SuffixFor(VisorGauge.State.Critical));
        Assert.AreNotEqual(VisorGauge.SuffixFor(VisorGauge.State.Warning),
                           VisorGauge.SuffixFor(VisorGauge.State.Critical));
    }

    [Test]
    public void ColourFollowsTheState()
    {
        Assert.AreEqual(VisorStyle.Ink,      VisorGauge.ColourFor(VisorGauge.State.Normal));
        Assert.AreEqual(VisorStyle.Alarm,    VisorGauge.ColourFor(VisorGauge.State.Warning));
        Assert.AreEqual(VisorStyle.Critical, VisorGauge.ColourFor(VisorGauge.State.Critical));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Via unity-mcp: `RunEditModeDeferred("VisorGaugeTests")`, then `cat Temp/headless_tests.txt`
Expected: compile failure — `IVisorGaugeSource` and `VisorGauge` do not exist.

- [ ] **Step 3: Write `IVisorGaugeSource`**

Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs`:

```csharp
namespace SpaceGame.Presentation
{
    /// <summary>
    /// What a <see cref="VisorGauge"/> reads. One interface so the gauge is written once and the
    /// suit's two survival numbers — integrity and oxygen — are two instances of it rather than
    /// two copies of the same drawing code.
    /// </summary>
    public interface IVisorGaugeSource
    {
        /// <summary>The value now.</summary>
        float Current { get; }

        /// <summary>The value at full. May legitimately be 0 before the source has spawned.</summary>
        float Max { get; }

        /// <summary>Uppercase field label, e.g. "SUIT INTEGRITY".</summary>
        string Label { get; }

        /// <summary>Fraction at or below which the gauge reads as a warning.</summary>
        float WarnFraction { get; }

        /// <summary>Fraction at or below which the gauge reads as critical.</summary>
        float AlarmFraction { get; }

        /// <summary>
        /// False while the underlying component has not resolved yet. A gauge hides rather than
        /// drawing a confident zero — see the UI doc's "a HUD element stays blank" symptom.
        /// </summary>
        bool Available { get; }
    }
}
```

- [ ] **Step 4: Write `VisorGauge`**

Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Core;   // GameSettings.ReduceVisorMotion

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One readout on the visor: an uppercase label, a track with a fill, a hatched danger zone
    /// that appears when the value crosses a threshold, and the number.
    ///
    /// <para>
    /// Built in code, drawn in <see cref="VisorStyle"/>, bound to an
    /// <see cref="IVisorGaugeSource"/>. The static helpers carry all the decisions so they can be
    /// tested without a canvas — <c>Awake</c> does not run on an <c>AddComponent</c> in an
    /// EditMode test, so nothing important may live there.
    /// </para>
    /// </summary>
    public class VisorGauge : MonoBehaviour
    {
        public enum State { Normal, Warning, Critical }

        /// <summary>Which way the gauge reads. Right-aligned gauges live on the right edge.</summary>
        public enum Align { Left, Right }

        // ── Decisions, static and testable ───────────────────────────────────

        /// <summary>
        /// The fill fraction, 0 to 1. Zero when <see cref="IVisorGaugeSource.Max"/> is zero: a
        /// source that has not spawned must not draw a full bar, which reads as "you are fine".
        /// </summary>
        public static float FractionOf(IVisorGaugeSource source)
        {
            if (source == null || source.Max <= 0f) return 0f;
            return Mathf.Clamp01(source.Current / source.Max);
        }

        /// <summary>
        /// Which state the value is in. The boundary belongs to the calmer state, so a value
        /// hovering exactly on a threshold does not flicker between two presentations.
        /// </summary>
        public static State StateOf(IVisorGaugeSource source)
        {
            float fraction = FractionOf(source);
            if (source == null) return State.Normal;
            if (fraction < source.AlarmFraction) return State.Critical;
            if (fraction < source.WarnFraction) return State.Warning;
            return State.Normal;
        }

        /// <summary>The colour half of the signal.</summary>
        public static Color ColourFor(State state) => state switch
        {
            State.Critical => VisorStyle.Critical,
            State.Warning => VisorStyle.Alarm,
            _ => VisorStyle.Ink,
        };

        /// <summary>
        /// The shape half. GDC-L1-UX-0003: colour is never the only signal, so a warning also
        /// grows a hatched danger zone on its track.
        /// </summary>
        public static bool ShowsHatch(State state) => state != State.Normal;

        /// <summary>The word half. Read aloud by nothing, but readable without colour vision.</summary>
        public static string SuffixFor(State state) => state switch
        {
            State.Critical => "CRITICAL",
            State.Warning => "LOW",
            _ => string.Empty,
        };

        // ── Drawing ──────────────────────────────────────────────────────────

        private IVisorGaugeSource source;
        private Align align;

        private CanvasGroup group;
        private TextMeshProUGUI labelText;
        private TextMeshProUGUI valueText;
        private TextMeshProUGUI suffixText;
        private Image trackImage;
        private Image fillImage;
        private Image hatchImage;

        private State lastState = State.Normal;
        private float bloomUntil;

        /// <summary>
        /// Builds the gauge under <paramref name="parent"/> and binds it. Called by
        /// <see cref="HelmetHUDController"/>; there is no authored prefab for this.
        /// </summary>
        public static VisorGauge Create(RectTransform parent, string name, Align align,
                                        IVisorGaugeSource source)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            var gauge = rect.gameObject.AddComponent<VisorGauge>();
            gauge.align = align;
            gauge.source = source;
            gauge.Build(rect);
            return gauge;
        }

        private void Build(RectTransform rect)
        {
            bool right = align == Align.Right;
            float m = VisorStyle.ScreenMargin;

            // Pinned to the top corner. Anchors rather than offsets, so it stays put on every
            // canvas UIScale can produce.
            rect.anchorMin = rect.anchorMax = new Vector2(right ? 1f : 0f, 1f);
            rect.pivot = new Vector2(right ? 1f : 0f, 1f);
            rect.anchoredPosition = new Vector2(right ? -m : m, -m);
            rect.sizeDelta = new Vector2(VisorStyle.GaugeWidth, 86f);

            group = rect.gameObject.AddComponent<CanvasGroup>();

            TextAlignmentOptions side = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            labelText = UIBuilder.LabelIn(rect, "Label", "", VisorStyle.LabelSize, VisorStyle.InkDim, side);
            PinRow((RectTransform)labelText.transform, 0f, 18f);
            labelText.characterSpacing = VisorStyle.LabelTracking;

            RectTransform trackRect = UIBuilder.Rect("Track", rect);
            PinRow(trackRect, 22f, VisorStyle.TrackHeight);
            trackImage = UIBuilder.Sprite(trackRect, VisorStyle.Track(VisorStyle.TrackHeight),
                                          VisorStyle.InkFaint);

            RectTransform fillRect = UIBuilder.Rect("Fill", trackRect);
            UIBuilder.Fill(fillRect);
            fillImage = UIBuilder.Sprite(fillRect, VisorStyle.Track(VisorStyle.TrackHeight), VisorStyle.Ink);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = right ? 1 : 0;

            RectTransform hatchRect = UIBuilder.Rect("Hatch", trackRect);
            UIBuilder.Fill(hatchRect);
            hatchImage = UIBuilder.Sprite(hatchRect, VisorStyle.HatchSprite, VisorStyle.Alarm);
            hatchImage.type = Image.Type.Tiled;
            hatchImage.enabled = false;

            valueText = UIBuilder.LabelIn(rect, "Value", "", VisorStyle.ReadoutSize, VisorStyle.Ink, side);
            PinRow((RectTransform)valueText.transform, 34f, 44f);

            suffixText = UIBuilder.LabelIn(rect, "Suffix", "", VisorStyle.MicroSize, VisorStyle.Alarm, side);
            PinRow((RectTransform)suffixText.transform, 76f, 16f);
            suffixText.characterSpacing = VisorStyle.LabelTracking;

            Refresh(instant: true);
        }

        private static void PinRow(RectTransform row, float fromTop, float height)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0f, 0f);
            row.offsetMax = new Vector2(0f, 0f);
            row.anchoredPosition = new Vector2(0f, -fromTop);
            row.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>Points the gauge at a different source. Safe with null.</summary>
        public void Bind(IVisorGaugeSource next)
        {
            source = next;
            Refresh(instant: true);
        }

        private void Update() => Refresh(instant: false);

        private void Refresh(bool instant)
        {
            bool available = source != null && source.Available;
            if (group != null) group.alpha = available ? 1f : 0f;
            if (!available) return;

            float fraction = FractionOf(source);
            State state = StateOf(source);

            if (state != lastState)
            {
                // A state change is the one thing on this gauge worth a bloom. Ambient motion is
                // deliberately absent: motion is a signal, not a texture.
                lastState = state;
                bloomUntil = Time.unscaledTime + VisorStyle.BloomSeconds;
            }

            Color colour = ColourFor(state);
            if (!instant && Time.unscaledTime < bloomUntil && !GameSettings.ReduceVisorMotion)
                colour *= VisorStyle.BloomStrength;

            labelText.text = source.Label;
            valueText.text = Mathf.CeilToInt(source.Current).ToString();
            valueText.color = colour;
            fillImage.fillAmount = fraction;
            fillImage.color = colour;

            hatchImage.enabled = ShowsHatch(state);
            hatchImage.color = new Color(colour.r, colour.g, colour.b, 0.5f);

            string suffix = SuffixFor(state);
            suffixText.text = suffix;
            suffixText.color = colour;
            suffixText.gameObject.SetActive(suffix.Length > 0);
        }
    }
}
```

- [ ] **Step 5: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`
**If it reports that `GameSettings.ReduceVisorMotion` does not exist, that is expected — Task 3 adds it.** Do Task 3 before re-running, or temporarily read `false`. Prefer doing Task 3 first if you hit this.

- [ ] **Step 6: Run the tests**

Via unity-mcp: `RunEditModeDeferred("VisorGaugeTests")`, then `cat Temp/headless_tests.txt`
Expected: `PASSED=6 FAILED=0`, then `DONE`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs.meta \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs.meta \
        Assets/Game/Editor/Tests/VisorGaugeTests.cs \
        Assets/Game/Editor/Tests/VisorGaugeTests.cs.meta
git commit -m "feat(ui): VisorGauge — one reusable visor readout

Thresholds pick the state; the boundary belongs to the calmer state so a
hovering value cannot flicker. Every alarm state changes hatch and suffix
as well as colour (GDC-L1-UX-0003)."
```

---

## Task 3: `GameSettings` — visor detail and motion reduction

**Files:**
- Modify: `Assets/Game/Scripts/Core/Settings/GameSettings.cs`

- [ ] **Step 1: Add the backing fields**

In `GameSettings.cs`, beside the other private backing fields (search for `private static bool invertHotbarScroll;`), add:

```csharp
        private static int visorDetail;
        private static bool reduceVisorMotion;
```

- [ ] **Step 2: Add the properties**

Immediately after the `InvertHotbarScroll` property, add:

```csharp
        // ---------------------------------------------------------------- visor

        /// <summary>Full visor: vitals and annotations both drawn.</summary>
        public const int VisorDetailFull = 0;

        /// <summary>Vitals only — gauges, hotbar, reticle. The markers and text go away.</summary>
        public const int VisorDetailVitals = 1;

        /// <summary>Nothing. The screenshot state.</summary>
        public const int VisorDetailOff = 2;

        /// <summary>
        /// How much of the helmet visor is drawn, cycled by H. Three states rather than two
        /// because health lives on the visor now: a plain on/off toggle would let the player
        /// hide their own health bar, which the old two-state toggle deliberately never could.
        /// </summary>
        public static int VisorDetail
        {
            get { EnsureLoaded(); return visorDetail; }
            set => SetInt(ref visorDetail, Mathf.Clamp(value, VisorDetailFull, VisorDetailOff), "VisorDetail");
        }

        /// <summary>
        /// Switches off the visor's idle motion — the sway, the boot sweep and the bloom.
        /// A vestibular-accessibility control in the same family as
        /// <see cref="CameraShakeIntensity"/>, not a polish dial (GDC-L1-UX-0006).
        /// </summary>
        public static bool ReduceVisorMotion
        {
            get { EnsureLoaded(); return reduceVisorMotion; }
            set => SetBool(ref reduceVisorMotion, value, "ReduceVisorMotion");
        }
```

- [ ] **Step 3: Add `SetInt`**

The file has `SetFloat` (line ~457) and `SetBool` (line ~468) but **no `SetInt`** — it has to be added. Put it directly after `SetBool`, matching its shape exactly:

```csharp
        private static void SetInt(ref int field, int value, string key)
        {
            EnsureLoaded();
            if (field == value) return;

            field = value;
            PlayerPrefs.SetInt(Prefix + key, value);
            Changed?.Invoke();
        }
```

**Open `SetBool` first and copy its exact body shape** — if it calls `PlayerPrefs.Save()`, or guards `Changed` differently, match that rather than the sketch above. The two must not diverge.

- [ ] **Step 4: Load them**

Find the private loader body where every other field is read from `PlayerPrefs`, and add alongside them:

```csharp
            visorDetail = PlayerPrefs.GetInt(Prefix + "VisorDetail", VisorDetailFull);
            reduceVisorMotion = PlayerPrefs.GetInt(Prefix + "ReduceVisorMotion", 0) == 1;
```

Match the exact shape of the surrounding lines — if the loader uses a helper rather than `PlayerPrefs.GetInt` directly, use the helper. Add both fields to `ResetToDefaults()` too, at their defaults (`VisorDetailFull` and `false`).

- [ ] **Step 5: Bump the schema version**

Change:

```csharp
        private const int SchemaVersion = 1;
```

to:

```csharp
        private const int SchemaVersion = 2;
```

- [ ] **Step 6: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Core/Settings/GameSettings.cs
git commit -m "feat(settings): visor detail level and motion reduction

Three-state visor detail, because health lives on the visor now and a
two-state toggle could hide the player's own health bar. Motion reduction
is a vestibular control alongside camera shake."
```

---

## Task 4: `HelmetHUDController` becomes the visor root

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs`
- Create: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs`

- [ ] **Step 1: Write `HealthGaugeSource`**

Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs`:

```csharp
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Adapts a <see cref="HealthComponent"/> to <see cref="IVisorGaugeSource"/>.
    ///
    /// <para>
    /// Holds the component rather than the numbers, so there is nothing to keep in step: the
    /// gauge reads through to live health every frame. It also means a null health — which is
    /// legitimate for a frame or more while Netcode publishes the local player — reports
    /// <see cref="Available"/> false rather than a confident zero.
    /// </para>
    /// </summary>
    public class HealthGaugeSource : IVisorGaugeSource
    {
        private HealthComponent health;

        /// <summary>Points the source at a health component. Safe with null.</summary>
        public void Bind(HealthComponent next) => health = next;

        /// <summary>The component currently read, or null.</summary>
        public HealthComponent Health => health;

        public float Current => health != null ? health.GetHealth : 0f;
        public float Max => health != null ? health.GetMaxHealth : 0f;
        public string Label => "SUIT INTEGRITY";
        public float WarnFraction => 0.35f;
        public float AlarmFraction => 0.15f;
        public bool Available => health != null && health.GetMaxHealth > 0;
    }
}
```

- [ ] **Step 2: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`

- [ ] **Step 3: Rewrite `HelmetHUDController`'s subsystem construction**

Open `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs`.

Replace the `EnsureSubsystems` method and add the sublayers. The controller keeps its existing health resolution (`ResolveHealth`, `RebindHealth`, the `Update` retry) because the gauge needs the same answer — but it now **feeds the gauge** rather than subscribing to damage itself. Delete `BindHealth`, `HandleDamage`, `boundHealth`, `damageForFullFlash` and the `OnDamage` subscription entirely; the vignette binds its own source in a later plan.

Add these fields:

```csharp
        [Header("Subsystems")]
        [SerializeField] private HelmetDangerVignette dangerVignette;
        [SerializeField] private HelmetNavMarkers navMarkers;

        /// <summary>Things you play by. Never hidden by the middle H state.</summary>
        public RectTransform Vitals { get; private set; }

        /// <summary>Things that describe the world. Hidden by the middle H state.</summary>
        public RectTransform Annotations { get; private set; }

        private readonly HealthGaugeSource healthSource = new();
        private VisorGauge integrityGauge;
```

Replace `EnsureSubsystems` with:

```csharp
        private void EnsureSubsystems()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            Vitals ??= MakeLayer("Vitals");
            Annotations ??= MakeLayer("Annotations");

            if (integrityGauge == null)
                integrityGauge = VisorGauge.Create(Vitals, "IntegrityGauge",
                                                   VisorGauge.Align.Right, healthSource);

            if (dangerVignette == null)
                dangerVignette = MakeLayer("DangerVignette", Vitals).gameObject
                                 .AddComponent<HelmetDangerVignette>();

            if (navMarkers == null)
                navMarkers = MakeLayer("NavMarkers", Annotations).gameObject
                             .AddComponent<HelmetNavMarkers>();

            // One camera decision for the whole helmet. Pushed down only when it was authored:
            // writing a null here would clear a camera the nav markers had wired themselves, and
            // null on either of them already means "use Camera.main", live, every frame.
            if (referenceCamera != null && navMarkers != null)
                navMarkers.ReferenceCamera = referenceCamera;
        }

        private RectTransform MakeLayer(string name, RectTransform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
```

Replace `RebindHealth` and `Update` with:

```csharp
        /// <summary>
        /// Points the visor at the health of the player wearing it. Safe to call repeatedly.
        /// </summary>
        public void RebindHealth()
        {
            healthSource.Bind(ResolveHealth());
            if (integrityGauge != null) integrityGauge.Bind(healthSource);
        }

        private void Update()
        {
            // Retried until it lands. The player object this HUD hangs under is spawned
            // asynchronously and its chunk is still streaming, so OnEnable's attempt is allowed to
            // come back empty; the cost while it does is one walk up the parent chain per frame,
            // and it stops the moment there is something to bind.
            if (healthSource.Health == null)
                RebindHealth();

            // Nav markers still need their per-frame projection update.
            if (navMarkers != null)
                navMarkers.Tick(out _, out _);
        }
```

Delete `OnDisable`'s `BindHealth(null)` call; replace the method body with `healthSource.Bind(null);`.

Update the class doc comment: it currently promises it "subscribes to the assigned HealthComponent's OnDamage event and grows both warning lines". It no longer does. Say instead that it spawns the two sublayers and the modules, and that each module binds its own source.

- [ ] **Step 4: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs.meta
git commit -m "feat(ui): helmet controller spawns Vitals and Annotations sublayers

Health becomes a VisorGauge on the Vitals layer. The controller stops
subscribing to damage; each module binds its own source."
```

---

## Task 5: Three-state **H**

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs`
- Test: `Assets/Game/Editor/Tests/VisorVisibilityTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/VisorVisibilityTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Presentation;

public class VisorVisibilityTests
{
    [Test]
    public void CycleGoesFullThenVitalsThenOffThenBack()
    {
        Assert.AreEqual(GameSettings.VisorDetailVitals,
                        HelmetOverlayVisibility.NextDetail(GameSettings.VisorDetailFull));
        Assert.AreEqual(GameSettings.VisorDetailOff,
                        HelmetOverlayVisibility.NextDetail(GameSettings.VisorDetailVitals));
        Assert.AreEqual(GameSettings.VisorDetailFull,
                        HelmetOverlayVisibility.NextDetail(GameSettings.VisorDetailOff));
    }

    [Test]
    public void VitalsSurviveTheMiddleState()
    {
        // The whole reason this toggle has three states: health lives on the visor now, and the
        // middle state must never hide the readouts the player plays by.
        Assert.IsTrue(HelmetOverlayVisibility.ShowsVitals(GameSettings.VisorDetailFull));
        Assert.IsTrue(HelmetOverlayVisibility.ShowsVitals(GameSettings.VisorDetailVitals));
        Assert.IsFalse(HelmetOverlayVisibility.ShowsVitals(GameSettings.VisorDetailOff));
    }

    [Test]
    public void AnnotationsAreOnlyDrawnInTheFullState()
    {
        Assert.IsTrue(HelmetOverlayVisibility.ShowsAnnotations(GameSettings.VisorDetailFull));
        Assert.IsFalse(HelmetOverlayVisibility.ShowsAnnotations(GameSettings.VisorDetailVitals));
        Assert.IsFalse(HelmetOverlayVisibility.ShowsAnnotations(GameSettings.VisorDetailOff));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Via unity-mcp: `RunEditModeDeferred("VisorVisibilityTests")`, then `cat Temp/headless_tests.txt`
Expected: compile failure — those three static methods do not exist.

- [ ] **Step 3: Rewrite `HelmetOverlayVisibility`**

Replace the body of the class (keeping the file's `using`s, adding `using SpaceGame.Core;`):

```csharp
    /// <summary>
    /// H cycles how much of the helmet visor is drawn: Full → Vitals only → Off → Full.
    ///
    /// <para>
    /// It used to be a plain on/off switch, and its comment said the health, crosshair and hotbar
    /// deliberately stayed OUT of the toggled layer because they are "readouts you play by". They
    /// are on the visor now, so a two-state toggle would let the player hide their own health bar.
    /// The middle state is what preserves the original intent: the annotations — markers, message
    /// text, chat — go away, and everything you play by stays.
    /// </para>
    /// <para>
    /// Lives on the PlayerHUD canvas root rather than on the layer it switches, for the obvious
    /// reason: a component cannot re-enable a GameObject it just deactivated.
    /// </para>
    /// </summary>
    public class HelmetOverlayVisibility : MonoBehaviour
    {
        [Tooltip("Action in the project-wide input asset that cycles the visor. Bound to H.")]
        [SerializeField] private string toggleActionName = "Hud";

        private InputAction toggleAction;
        private HelmetHUDController helmet;

        /// <summary>The state after this one. Wraps.</summary>
        public static int NextDetail(int detail) =>
            detail >= GameSettings.VisorDetailOff ? GameSettings.VisorDetailFull : detail + 1;

        /// <summary>Whether the things you play by are drawn at this detail level.</summary>
        public static bool ShowsVitals(int detail) => detail != GameSettings.VisorDetailOff;

        /// <summary>Whether the things that describe the world are drawn at this detail level.</summary>
        public static bool ShowsAnnotations(int detail) => detail == GameSettings.VisorDetailFull;

        private void Awake()
        {
            // includeInactive, so a visor the player switched off last session is still found
            // rather than leaving H doing nothing.
            helmet = GetComponentInChildren<HelmetHUDController>(includeInactive: true);
            if (helmet == null)
                Debug.LogWarning("[HelmetOverlayVisibility] No HelmetHUDController under this canvas — nothing to toggle.", this);

            toggleAction = InputSystem.actions?.FindAction(toggleActionName);
            if (toggleAction == null)
                Debug.LogWarning($"[HelmetOverlayVisibility] Input action '{toggleActionName}' not found.", this);
        }

        private void OnEnable() => Apply(GameSettings.VisorDetail);

        private void Update()
        {
            // The UI action map stays live under every menu, so the press has to be qualified: H
            // belongs to the player, not to whatever panel is on top of them.
            if (toggleAction != null && toggleAction.WasPressedThisFrame() && GameplayMenuScope.AcceptsGameplayInput)
                SetDetail(NextDetail(GameSettings.VisorDetail));
        }

        /// <summary>Stores the choice and applies it. The chosen state survives a quit.</summary>
        public void SetDetail(int detail)
        {
            GameSettings.VisorDetail = detail;
            Apply(detail);
        }

        private void Apply(int detail)
        {
            if (helmet == null) return;

            // The root stays active at every level: it owns the sublayers, and a controller that
            // deactivated itself could not build them on the way back.
            helmet.gameObject.SetActive(true);
            if (helmet.Vitals != null) helmet.Vitals.gameObject.SetActive(ShowsVitals(detail));
            if (helmet.Annotations != null) helmet.Annotations.gameObject.SetActive(ShowsAnnotations(detail));
        }
    }
```

- [ ] **Step 4: Type-check and run the tests**

Run: `python3 tools/typecheck.py` → `No errors.`
Via unity-mcp: `RunEditModeDeferred("VisorVisibilityTests")` → `cat Temp/headless_tests.txt`
Expected: `PASSED=3 FAILED=0`, then `DONE`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs \
        Assets/Game/Editor/Tests/VisorVisibilityTests.cs \
        Assets/Game/Editor/Tests/VisorVisibilityTests.cs.meta
git commit -m "feat(ui): H cycles visor detail Full -> Vitals -> Off

Health lives on the visor now, so a two-state toggle could hide the
player's own health bar. Choice persists in GameSettings."
```

---

## Task 6: Delete `HealthUI` and strip the prefab

**Files:**
- Delete: `Assets/Game/Scripts/Presentation/UI/HUD/HealthUI.cs` and `.meta`
- Modify: `Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab`
- Test: `Assets/Game/Editor/Tests/PlayerHudWiringTests.cs`

- [ ] **Step 1: Write the failing wiring test**

Create `Assets/Game/Editor/Tests/PlayerHudWiringTests.cs`:

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Presentation;

public class PlayerHudWiringTests
{
    private const string HudPath = "Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab";

    private static GameObject LoadHud()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Assert.IsNotNull(hud, $"PlayerHUD prefab missing at {HudPath}");
        return hud;
    }

    [Test]
    public void TheHudCarriesTheVisorRoot()
    {
        Assert.IsNotNull(LoadHud().GetComponentInChildren<HelmetHUDController>(includeInactive: true),
                         "PlayerHUD must carry a HelmetHUDController — it is the visor root.");
    }

    [Test]
    public void TheHudCarriesTheVisorToggle()
    {
        Assert.IsNotNull(LoadHud().GetComponentInChildren<HelmetOverlayVisibility>(includeInactive: true),
                         "PlayerHUD must carry HelmetOverlayVisibility on the canvas root.");
    }

    [Test]
    public void NoAuthoredHealthObjectsRemain()
    {
        // HealthUI is deleted and its authored objects with it. A leftover object here draws a
        // second health bar in the old warm palette, which is exactly the incoherence this
        // whole change exists to remove.
        foreach (Transform t in LoadHud().GetComponentsInChildren<Transform>(includeInactive: true))
        {
            Assert.AreNotEqual("Health", t.name, "Authored Health object still on PlayerHUD.");
            Assert.AreNotEqual("HealthBar", t.name, "Authored HealthBar object still on PlayerHUD.");
            Assert.AreNotEqual("HealthText", t.name, "Authored HealthText object still on PlayerHUD.");
            Assert.AreNotEqual("maxHealthText", t.name, "Authored maxHealthText object still on PlayerHUD.");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Via unity-mcp: `RunEditModeDeferred("PlayerHudWiringTests")`, then `cat Temp/headless_tests.txt`
Expected: `NoAuthoredHealthObjectsRemain` FAILS — those objects are still in the prefab.

- [ ] **Step 3: Find every reference to `HealthUI` before deleting it**

Run:

```bash
grep -rn 'HealthUI' Assets/Game --include='*.cs'
grep -rln 'HealthUI' Assets/Game --include='*.prefab' --include='*.unity'
```

Every C# hit must be removed or repointed at `VisorGauge` before the file is deleted. **Do not skip the scene/prefab grep** — a deleted script leaves the field null silently, which is a documented failure mode in this repo.

- [ ] **Step 4: Delete the script**

```bash
git rm Assets/Game/Scripts/Presentation/UI/HUD/HealthUI.cs \
       Assets/Game/Scripts/Presentation/UI/HUD/HealthUI.cs.meta
```

- [ ] **Step 5: Strip the authored objects from the prefab**

Open `Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab` in the Unity Editor (**not** by hand-editing YAML — this prefab has nested references and the meta ids must stay consistent). Delete the `Health` object and its `HealthBar`, `HealthText` and `maxHealthText` children. Save.

Confirm the prefab actually saved — see the repo's known `AssetDatabase` read-only failure, where prefab saves are silently discarded. Re-open the prefab and check the objects are gone before continuing.

- [ ] **Step 6: Type-check and run the tests**

Run: `python3 tools/typecheck.py` → `No errors.`
Via unity-mcp: `RunEditModeDeferred("PlayerHudWiringTests")` → `cat Temp/headless_tests.txt`
Expected: `PASSED=3 FAILED=0`, then `DONE`.

- [ ] **Step 7: Commit**

```bash
git add -A Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab \
           Assets/Game/Scripts/Presentation/UI/HUD/ \
           Assets/Game/Editor/Tests/PlayerHudWiringTests.cs \
           Assets/Game/Editor/Tests/PlayerHudWiringTests.cs.meta
git commit -m "refactor(ui): delete HealthUI, health is a visor gauge now

Its serialized-reference wiring is the documented cause of a HUD element
staying blank until something happens to it."
```

---

## Task 7: `VisorSway` and `VisorBoot`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs`
- Create: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs`

- [ ] **Step 1: Write `VisorSway`**

Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs`:

```csharp
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Lags the whole visor a few pixels behind the player's head turn, then eases it back.
    ///
    /// <para>
    /// This is the one thing that makes the layer read as light projected on glass in front of
    /// the eye rather than as a flat overlay drawn on the monitor. It is deliberately tiny:
    /// motion is a signal, not a texture, and idle movement that rises above the threshold of
    /// noticing starts competing with the movement that means something
    /// (<c>GDC-L1-FEEL-0004</c>, recorded disagreement).
    /// </para>
    /// <para>
    /// Honours <see cref="GameSettings.ReduceVisorMotion"/>, which is a vestibular control, not a
    /// polish dial.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VisorSway : MonoBehaviour
    {
        [Tooltip("Peak offset, in reference pixels, at a fast head turn.")]
        [SerializeField, Min(0f)] private float pixels = VisorStyle.SwayPixels;

        [Tooltip("How quickly the layer eases back to centre, per second.")]
        [SerializeField, Min(0.1f)] private float recovery = VisorStyle.SwayRecovery;

        [Tooltip("Degrees per second of head turn that produces the peak offset.")]
        [SerializeField, Min(1f)] private float degreesForFullSway = 220f;

        private RectTransform rect;
        private Quaternion lastRotation;
        private Vector2 offset;
        private bool hasLast;

        private void Awake() => rect = (RectTransform)transform;

        private void LateUpdate()
        {
            Camera view = Camera.main;
            if (rect == null || view == null) return;

            if (GameSettings.ReduceVisorMotion)
            {
                offset = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                hasLast = false;
                return;
            }

            Quaternion now = view.transform.rotation;
            if (!hasLast)
            {
                lastRotation = now;
                hasLast = true;
                return;
            }

            // Yaw and pitch deltas separately: a head turn drags the layer sideways, a look up
            // or down drags it vertically. Signed, so the lag trails the movement.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector3 euler = (Quaternion.Inverse(lastRotation) * now).eulerAngles;
            float yaw = Mathf.DeltaAngle(0f, euler.y) / dt;
            float pitch = Mathf.DeltaAngle(0f, euler.x) / dt;
            lastRotation = now;

            var target = new Vector2(
                Mathf.Clamp(-yaw / degreesForFullSway, -1f, 1f) * pixels,
                Mathf.Clamp(pitch / degreesForFullSway, -1f, 1f) * pixels);

            offset = Vector2.Lerp(offset, target, 1f - Mathf.Exp(-recovery * dt));
            rect.anchoredPosition = offset;
        }
    }
}
```

- [ ] **Step 2: Write `VisorBoot`**

Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs`:

```csharp
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The visor's power-on: a short brightness sweep the first time the layer appears.
    ///
    /// <para>
    /// Purely visual. It never gates input and never delays a readout being legible
    /// (<c>GDC-L1-ANIM-0002</c>): the gauges are readable from frame one and the sweep simply
    /// rides over them. Runs on unscaled time, like every other UI animation in the project.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class VisorBoot : MonoBehaviour
    {
        [Tooltip("Seconds the sweep takes.")]
        [SerializeField, Min(0f)] private float seconds = VisorStyle.BootSeconds;

        [Tooltip("Alpha the layer starts at. It never starts fully invisible — the readouts must " +
                 "be legible from the first frame.")]
        [SerializeField, Range(0f, 1f)] private float startAlpha = 0.35f;

        private CanvasGroup group;
        private float elapsed;
        private bool running;

        private void Awake() => group = GetComponent<CanvasGroup>();

        private void OnEnable()
        {
            elapsed = 0f;
            running = !GameSettings.ReduceVisorMotion;
            if (group != null) group.alpha = running ? startAlpha : 1f;
        }

        private void Update()
        {
            if (!running || group == null) return;

            elapsed += Time.unscaledDeltaTime;
            float t = seconds <= 0f ? 1f : Mathf.Clamp01(elapsed / seconds);
            group.alpha = Mathf.Lerp(startAlpha, 1f, t);

            if (t >= 1f) running = false;
        }
    }
}
```

- [ ] **Step 3: Attach both in `HelmetHUDController.EnsureSubsystems`**

At the top of `EnsureSubsystems`, after `Stretch(rt);`, add:

```csharp
            if (GetComponent<CanvasGroup>() == null) gameObject.AddComponent<CanvasGroup>();
            if (GetComponent<VisorBoot>() == null) gameObject.AddComponent<VisorBoot>();
            if (GetComponent<VisorSway>() == null) gameObject.AddComponent<VisorSway>();
```

- [ ] **Step 4: Type-check**

Run: `python3 tools/typecheck.py`
Expected: `No errors.`

- [ ] **Step 5: Verify by playing**

Enter play mode. Confirm, in order:
1. The visor fades up over roughly 0.7 s, and the integrity number is **readable during the fade**.
2. Turning the view fast drags the gauges a few pixels and they ease back.
3. Setting `GameSettings.ReduceVisorMotion = true` stops both, and the layer sits centred at full alpha.
4. Taking damage moves the integrity gauge and blooms it once.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs.meta \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs.meta \
        Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs
git commit -m "feat(ui): restrained visor motion — sway and boot sweep

The sway is what makes the layer read as light on glass rather than an
overlay. Both honour ReduceVisorMotion; the boot never gates input."
```

---

## Task 8: Multiplayer and persistence verification

This plan adds no networked state and no saved world state — but per `CLAUDE.md` that has to be **stated and checked**, not assumed.

- [ ] **Step 1: Verify on an actual client**

Launch a host and a second peer. On the **client**:
1. The integrity gauge shows the **client's own** health, not the host's. Damage the client and watch only its gauge move.
2. Damage the host and confirm the client's gauge does **not** move.
3. **H** cycles the client's own visor and does not affect the host's.

This is the failure this design guards against: `HelmetHUDController.ResolveHealth` uses `GameplayMenuScope.FindLocalPlayer(this)` precisely because a `"Player"` tag search returns an arbitrary player once a second one exists.

- [ ] **Step 2: Verify persistence**

1. Press **H** twice (leaving the visor Off), quit to the main menu, relaunch. The visor is still Off.
2. Confirm nothing visor-shaped appears in the world save JSON — the visor's only persisted state is in `PlayerPrefs` via `GameSettings`, deliberately.

- [ ] **Step 3: Record the result**

If either check fails, stop and fix before moving to plan 2. Note the outcome in the PR description.

---

## Task 9: Documentation

Per `CLAUDE.md`, documenting the change is part of the change.

- [ ] **Step 1: Create `docs/AI/systems/Visor.md`**

Frontmatter must include `system`, `layer: presentation`, `summary`, `paths`, `symptoms`, `reads_with`, `updated: 2026-09-04`. Body follows the house shape: **Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending**.

`symptoms:` entries to include, phrased as what you *saw*:
- `"the health bar is gone after I pressed H"` (the reason H has three states)
- `"my health gauge shows another player's health"` (the tag-search failure)
- `"the visor draws a full bar before the player has spawned"` (the `Max == 0` rule)
- `"a gauge flickers between two colours while the value sits on a threshold"`
- `"the visor is a flat overlay and does not feel like it is on glass"` (`ReduceVisorMotion` is on)

`Gotchas` must record: `Max == 0` reports unavailable rather than zero; the threshold boundary belongs to the calmer state; `VisorStyle` sprites are cached per parameter for `UITheme.Rounded`'s reason; the visor root must stay active at every detail level.

- [ ] **Step 2: Update `docs/AI/systems/UI.md`**

- Delete the `HealthUI` row from **Pages & widgets**.
- Update the `HelmetHUDController`, `HelmetOverlayVisibility` rows to describe the sublayers and three-state H.
- In **Model**, change *"Two design languages"* to three, and describe `VisorStyle`.
- Add `VisorStyle`, `VisorGauge` to **Key types**.
- Bump `updated:`.

- [ ] **Step 3: Add the Human entry**

Add a short plain-language paragraph on the visor to `docs/Human/the-systems.md`. **The validator fails without it.**

- [ ] **Step 4: Regenerate and validate**

```bash
python3 tools/docs_check.py --index
```

Expected: regenerates `INDEX.md` + `ROUTING.md`, then validates clean. `INDEX.md` and `ROUTING.md` are generated — never hand-edit them.

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs: visor foundation — new Visor.md, UI.md updated"
```

---

## Done when

- `python3 tools/typecheck.py` → `No errors.`
- `VisorStyleTests`, `VisorGaugeTests`, `VisorVisibilityTests`, `PlayerHudWiringTests` all pass.
- A client sees its own health in the visor gauge, in blue, at the top right.
- **H** cycles Full → Vitals → Off, never hides health in the middle state, and the choice survives a restart.
- `python3 tools/docs_check.py --index` validates clean.

## What this plan deliberately does not do

Covered by the following plans, so do not start them here:

- **Plan 2** — `VisorReticle` (crosshair, interaction bracket, look-at info box), hotbar restyle, `HelmetNavMarkers` restyle, retiring `HotbarStyle`.
- **Plan 3** — `SystemMessages`, the `PlayerHints` adapter, message stack, warning banner, `VisorChatList`.
- **Plan 4** — `SuitOxygen`, `BreathableVolume`, the bottle use verb, suffocation, the oxygen gauge.
- **Plan 5** — directional damage: the `HealthComponent` source event, the owner-targeted RPC, bearing-placed arcs.
