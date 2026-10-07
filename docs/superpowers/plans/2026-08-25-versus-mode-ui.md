# Versus Mode UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second front-menu mode ("VS") whose host configures teams before the lobby, and whose lobby stands the astronauts in team clusters that can be joined and recoloured.

**Architecture:** All pure rules (limits, colour stepping, rank geometry) go in a new dependency-free `SpaceGame.Versus.Core` assembly so EditMode tests can reach them, mirroring the existing `SpaceGame.Minigame.Core` split. Everything that touches `Unity.Services.Lobbies` or uGUI stays in Assembly-CSharp and is guarded by Editor tests in `Assets/Game/Editor/Tests/`, which is the only test folder that can see Assembly-CSharp types. The lobby remains the single source of truth for team state; no new transport is introduced.

**Tech Stack:** Unity 6000.3.11f1, Unity Netcode for GameObjects, Unity Gaming Services (Lobby + Relay), TextMeshPro, NUnit.

**Spec:** `docs/superpowers/specs/2026-08-25-versus-mode-ui-design.md`

---

## Before you start

**Commits are gated in this repo.** A commit hook blocks `git commit` unless the user has asked for a commit in the current turn. Every task below ends with a commit step; if the hook refuses, stop and tell the user rather than retrying or working around it. Do not use `$(...)` command substitution in any git command — the same hook false-positives on it.

**How to verify a change compiles**, without the Editor and without the MCP bridge:

```bash
SP=/private/tmp/claude-501/-Users-ferdinandfremming-Documents-hackerspace-spillgruppen-SpaceGame/e44db02d-8bf5-4163-8391-9bc9dda29778/scratchpad
sed -e "s|^-out:.*|-out:\"$SP/Assembly-CSharp.dll\"|" -e "/^-refout:/d" \
    Library/Bee/artifacts/200b0aE.dag/Assembly-CSharp.rsp > "$SP/asmcs.rsp"
ED=/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/Resources/Scripting
"$ED/NetCoreRuntime/dotnet" "$ED/DotNetSdkRoslyn/csc.dll" "@$SP/asmcs.rsp"
```

Bee's source list goes stale. **Before believing a green result, check the `.rsp` actually lists the file you just wrote** (`grep VersusRules "$SP/asmcs.rsp"`). If it does not, append the missing `.cs` paths by hand — and remember that any `.cs` at or below a directory containing an `.asmdef` belongs to that assembly, not to Assembly-CSharp.

**How to run the tests:** click `Tools ▸ Tests ▸ Run EditMode Tests (headless)` in the Editor, then poll `Temp/headless_tests.txt` for `DONE`. Delete that file first, or you read the previous run's results. A result that comes back in seconds was truncated by a domain reload — re-run it. Six standing failures are pre-existing and not yours: `MountRiderComponentRestoreTests` ×2, `WingPackLaunchTests` ×2, `NpcPassengerTests` ×2 (all the documented `Time.time == 0` edit-mode artefact), plus Backpack (5), Lasso (1), GrappleSwing (1).

---

## File Structure

**Created — `SpaceGame.Versus.Core` (pure, tested):**

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Gameplay/Versus/Core/SpaceGame.Versus.Core.asmdef` | Assembly definition, no references |
| `Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs` | Team/size limits, seat math, occupancy guards, team names |
| `Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs` | Default team swatches, stepping past swatches other teams hold |
| `Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs` | Where each seat and team plate stands; how far back the camera goes |
| `Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs` | The team state that survives the load into the world |

**Created — Assembly-CSharp:**

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Presentation/UI/Widgets/MenuStepper.cs` | The `− 3 +` row, in the menu's language |
| `Assets/Game/Scripts/Presentation/UI/Pages/MenuChoiceUI.cs` | A generic "pick one of these, or Back" menu page |
| `Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs` | The pre-lobby rules page |
| `Assets/Game/Scripts/Core/Multiplayer/RosterSnapshot.cs` | The single value the lobby views render from |
| `Assets/Game/Scripts/Core/Multiplayer/VersusSetup.cs` | Teams+size handed to lobby creation |
| `Assets/Game/Editor/Menus/FrontMenuSetup.cs` | Relabels and rebinds MainMenu.unity's ButtonRow |

**Modified:**

| File | Change |
| --- | --- |
| `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs` | `StartStory` / `StartVersus` / `HostVersus` / `JoinVersus` / `EnterVersusLobby` |
| `Assets/Game/Scripts/Core/Multiplayer/LobbySessionOptions.cs` | Versus keys, option builders, lobby readers |
| `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs` | Versus create, team publish, rules update, snapshot |
| `Assets/Game/Scripts/Presentation/UI/Pages/LobbyUI.cs` | Explicit route, mode-filtered browser, versus hosting |
| `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyRosterView.cs` | Renders from a snapshot; host stepper strip |
| `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyPreviewRank.cs` | Team clusters, team plates, team colour cycler, camera fit |
| `Assets/Game/Scripts/Core/Multiplayer/PlayerIdentity.cs` | Publishes team and team colour while a versus session is active |
| `Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef` | References `SpaceGame.Versus.Core` |

**Deleted:** `Assets/Game/Scripts/Presentation/UI/Pages/MultiplayerChoiceUI.cs` (+ `.meta`), replaced by `MenuChoiceUI`.

---

## Task 1: The Versus.Core assembly and VersusRules

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/SpaceGame.Versus.Core.asmdef`
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs`
- Modify: `Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`
- Test: `Assets/Game/Tests/EditMode/VersusRulesTests.cs`

- [ ] **Step 1: Create the assembly definition**

`Assets/Game/Scripts/Gameplay/Versus/Core/SpaceGame.Versus.Core.asmdef`:

```json
{
    "name": "SpaceGame.Versus.Core",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`autoReferenced: true` is what lets Assembly-CSharp use these types without listing them — the same setting `SpaceGame.Minigame.Core` uses for the same reason.

- [ ] **Step 2: Add the reference to the test assembly**

In `Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`, add `"SpaceGame.Versus.Core"` to the `references` array, immediately after `"SpaceGame.Minigame.Core"`:

```json
        "SpaceGame.Persistence",
        "SpaceGame.Minigame.Core",
        "SpaceGame.Versus.Core",
        "SpaceGame.World.Streaming",
```

- [ ] **Step 3: Write the failing tests**

`Assets/Game/Tests/EditMode/VersusRulesTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The seat arithmetic behind the VS rules page and the lobby's host steppers.
    ///
    /// The interesting part is that teams and team size are not independent: their product is
    /// capped, so raising one has to be able to refuse rather than silently overrun the ceiling.
    /// </summary>
    public class VersusRulesTests
    {
        [Test]
        public void DefaultsFitTheCeiling()
        {
            Assert.LessOrEqual(VersusRules.Seats(VersusRules.DefaultTeams, VersusRules.DefaultTeamSize),
                               VersusRules.MaxSeats);
        }

        [Test]
        public void TeamsAreHeldWithinTheirOwnLimits()
        {
            Assert.AreEqual(VersusRules.MinTeams, VersusRules.ClampTeams(0, teamSize: 1));
            Assert.AreEqual(VersusRules.MaxTeams, VersusRules.ClampTeams(99, teamSize: 1));
        }

        [Test]
        public void TeamSizeIsHeldWithinItsOwnLimits()
        {
            Assert.AreEqual(VersusRules.MinTeamSize, VersusRules.ClampTeamSize(0, teams: 2));
            Assert.AreEqual(VersusRules.MaxTeamSize, VersusRules.ClampTeamSize(99, teams: 2));
        }

        /// <summary>The pair is what is capped, not either axis alone.</summary>
        [Test]
        public void TeamsCannotPushTheSeatTotalOverTheCeiling()
        {
            int teams = VersusRules.ClampTeams(VersusRules.MaxTeams, teamSize: VersusRules.MaxTeamSize);

            Assert.LessOrEqual(VersusRules.Seats(teams, VersusRules.MaxTeamSize), VersusRules.MaxSeats);
            Assert.GreaterOrEqual(teams, VersusRules.MinTeams, "clamping must never go below the floor");
        }

        [Test]
        public void TeamSizeCannotPushTheSeatTotalOverTheCeiling()
        {
            int size = VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, teams: VersusRules.MaxTeams);

            Assert.LessOrEqual(VersusRules.Seats(VersusRules.MaxTeams, size), VersusRules.MaxSeats);
            Assert.GreaterOrEqual(size, VersusRules.MinTeamSize);
        }

        // ───────────────────────────────────────────── the occupancy guards

        [Test]
        public void TeamSizeMayNotDropBelowTheFullestTeam()
        {
            int[] occupancy = { 3, 1 };

            Assert.IsFalse(VersusRules.CanSetTeamSize(2, occupancy, out string refusal));
            StringAssert.Contains(VersusRules.TeamName(0), refusal,
                "the refusal has to name the team that is in the way");
            Assert.IsTrue(VersusRules.CanSetTeamSize(3, occupancy, out _));
        }

        [Test]
        public void ATeamWithPlayersInItCannotBeRemoved()
        {
            int[] occupancy = { 1, 0, 2 };

            Assert.IsFalse(VersusRules.CanSetTeamCount(2, occupancy, out string refusal));
            StringAssert.Contains(VersusRules.TeamName(2), refusal);
        }

        [Test]
        public void AnEmptyTeamCanBeRemoved()
        {
            int[] threeTeams = { 1, 1, 0 };

            Assert.IsTrue(VersusRules.CanSetTeamCount(2, threeTeams, out _),
                          "the team being dropped has nobody in it");
        }

        [Test]
        public void GrowingIsAlwaysAllowed()
        {
            int[] occupancy = { 2, 2 };

            Assert.IsTrue(VersusRules.CanSetTeamSize(4, occupancy, out _));
            Assert.IsTrue(VersusRules.CanSetTeamCount(4, occupancy, out _));
        }

        [Test]
        public void EveryTeamHasAName()
        {
            for (int i = 0; i < VersusRules.MaxTeams; i++)
                Assert.IsNotEmpty(VersusRules.TeamName(i), $"team {i} has no name");
        }

        [Test]
        public void TeamNamesAreDistinct()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < VersusRules.MaxTeams; i++)
                Assert.IsTrue(seen.Add(VersusRules.TeamName(i)), $"team {i} reuses a name");
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:

```bash
rm -f Temp/headless_tests.txt
```

Expected: the run fails to compile, reporting `CS0103: The name 'VersusRules' does not exist`.

- [ ] **Step 5: Write VersusRules**

`Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs`:

```csharp
namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The seat arithmetic behind VS: how many teams, how big, and what the host is allowed to
    /// change once people are standing in them.
    ///
    /// <para>
    /// Kept free of Unity types and in its own assembly so the EditMode tests can reach it, the
    /// same split <see cref="MatchRules"/> already uses. The limits are hard ceilings rather than
    /// preferences: <see cref="MaxSeats"/> is what a VS host allocates on Relay, and Relay's
    /// allocation size cannot be changed after the fact.
    /// </para>
    ///
    /// <para>
    /// Teams and team size are <b>not independent</b>. Their product is what is capped, so each
    /// clamp takes the other axis as an argument — clamping them separately is how a host ends up
    /// with 8×12 and a lobby that can seat a third of it.
    /// </para>
    /// </summary>
    public static class VersusRules
    {
        /// <summary>Two is the floor because one team is not a versus.</summary>
        public const int MinTeams = 2;

        /// <summary>As many teams as there are distinct names below.</summary>
        public const int MaxTeams = 8;

        public const int MinTeamSize = 1;
        public const int MaxTeamSize = 12;

        /// <summary>
        /// What a VS host allocates on Relay, and therefore the hard ceiling on teams × size.
        ///
        /// Relay's allocation is sized once and for all when it is made, so the host allocates for
        /// this many and the lobby's advertised max follows the rules underneath it. A host who
        /// could grow past this would be advertising seats nobody can actually connect to.
        /// </summary>
        public const int MaxSeats = 24;

        /// <summary>Two teams of two — the smallest thing that is recognisably a match.</summary>
        public const int DefaultTeams = 2;

        public const int DefaultTeamSize = 2;

        /// <summary>
        /// The team names, in order. Spelled out rather than "TEAM 1" because these are read at a
        /// glance across a rank of astronauts, and a word is quicker to tell apart than a digit.
        /// </summary>
        private static readonly string[] Names =
        {
            "TEAM ONE", "TEAM TWO", "TEAM THREE", "TEAM FOUR",
            "TEAM FIVE", "TEAM SIX", "TEAM SEVEN", "TEAM EIGHT"
        };

        public static string TeamName(int team) =>
            team >= 0 && team < Names.Length ? Names[team] : "TEAM";

        public static int Seats(int teams, int teamSize) => teams * teamSize;

        /// <summary>
        /// Holds a team count inside its own limits and inside the seat ceiling.
        ///
        /// The seat clamp never wins over <see cref="MinTeams"/>: a team size large enough that
        /// even two teams overrun the ceiling is impossible by construction
        /// (<see cref="MaxTeamSize"/> × <see cref="MinTeams"/> is inside it), and the floor is what
        /// keeps this from ever returning something that is not a match.
        /// </summary>
        public static int ClampTeams(int teams, int teamSize)
        {
            int size = teamSize < MinTeamSize ? MinTeamSize : teamSize;
            int ceiling = MaxSeats / size;

            if (ceiling > MaxTeams) ceiling = MaxTeams;
            if (ceiling < MinTeams) ceiling = MinTeams;

            return Clamp(teams, MinTeams, ceiling);
        }

        public static int ClampTeamSize(int teamSize, int teams)
        {
            int count = teams < MinTeams ? MinTeams : teams;
            int ceiling = MaxSeats / count;

            if (ceiling > MaxTeamSize) ceiling = MaxTeamSize;
            if (ceiling < MinTeamSize) ceiling = MinTeamSize;

            return Clamp(teamSize, MinTeamSize, ceiling);
        }

        /// <summary>
        /// Whether the host may set this team size, given who is already standing where.
        ///
        /// Refused rather than reassigned: a player moved out of the team they chose, by someone
        /// else, with no warning, is worse than a host being told no. <paramref name="refusal"/> is
        /// a sentence fit to put straight on the lobby's status line.
        /// </summary>
        public static bool CanSetTeamSize(int teamSize, int[] occupancy, out string refusal)
        {
            refusal = null;
            if (occupancy == null) return true;

            for (int team = 0; team < occupancy.Length; team++)
            {
                if (occupancy[team] <= teamSize) continue;

                refusal = $"{TeamName(team)} has {occupancy[team]} players.";
                return false;
            }

            return true;
        }

        /// <summary>Whether the host may drop to this many teams. A team with anyone in it stays.</summary>
        public static bool CanSetTeamCount(int teams, int[] occupancy, out string refusal)
        {
            refusal = null;
            if (occupancy == null) return true;

            for (int team = teams; team < occupancy.Length; team++)
            {
                if (occupancy[team] <= 0) continue;

                refusal = $"{TeamName(team)} has {occupancy[team]} players.";
                return false;
            }

            return true;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
```

- [ ] **Step 6: Type-check**

Run the Roslyn pass from "Before you start". Expected: no errors mentioning `VersusRules`.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`, then:

```bash
grep -c "VersusRulesTests" Temp/headless_tests.txt
```

Expected: all 10 `VersusRulesTests` pass; total failures unchanged from the standing 10.

- [ ] **Step 8: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Versus Assets/Game/Tests/EditMode/VersusRulesTests.cs Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef
git commit -m "feat: versus seat rules and team limits"
```

---

## Task 2: Team colour rules

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs`
- Test: `Assets/Game/Tests/EditMode/TeamColorRulesTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Game/Tests/EditMode/TeamColorRulesTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rule that keeps two teams from wearing the same suit.
    ///
    /// The swatch count arrives as an argument rather than being read from SuitPalette: the palette
    /// lives in Assembly-CSharp, which an assembly definition cannot reference, and passing the
    /// count in is what keeps this testable at all.
    /// </summary>
    public class TeamColorRulesTests
    {
        private const int Swatches = 14;

        [Test]
        public void SteppingForwardSkipsASwatchAnotherTeamWears()
        {
            int[] taken = { 4 };

            Assert.AreEqual(5, TeamColorRules.Step(3, 1, Swatches, taken));
        }

        [Test]
        public void SteppingBackwardSkipsToo()
        {
            int[] taken = { 4 };

            Assert.AreEqual(3, TeamColorRules.Step(5, -1, Swatches, taken));
        }

        [Test]
        public void SteppingWrapsAroundThePalette()
        {
            Assert.AreEqual(0, TeamColorRules.Step(Swatches - 1, 1, Swatches, new int[0]));
            Assert.AreEqual(Swatches - 1, TeamColorRules.Step(0, -1, Swatches, new int[0]));
        }

        [Test]
        public void SteppingSkipsARunOfTakenSwatches()
        {
            int[] taken = { 4, 5, 6 };

            Assert.AreEqual(7, TeamColorRules.Step(3, 1, Swatches, taken));
        }

        /// <summary>
        /// With every other swatch spoken for there is nowhere to go, and the answer has to be the
        /// colour already worn rather than a hang or a duplicate.
        /// </summary>
        [Test]
        public void SteppingWithNowhereToGoStaysPut()
        {
            var taken = new int[Swatches - 1];
            for (int i = 0; i < taken.Length; i++) taken[i] = i + 1;

            Assert.AreEqual(0, TeamColorRules.Step(0, 1, Swatches, taken));
        }

        [Test]
        public void DefaultColorsAreAllDistinct()
        {
            int[] colors = TeamColorRules.DefaultColors(VersusRules.MaxTeams, Swatches);

            Assert.AreEqual(VersusRules.MaxTeams, colors.Length);

            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (int color in colors)
                Assert.IsTrue(seen.Add(color), "two teams start on the same swatch");
        }

        [Test]
        public void DefaultColorsAreInsideThePalette()
        {
            foreach (int color in TeamColorRules.DefaultColors(VersusRules.MaxTeams, Swatches))
            {
                Assert.GreaterOrEqual(color, 0);
                Assert.Less(color, Swatches);
            }
        }

        /// <summary>
        /// More teams than swatches cannot be made distinct, and the answer is a valid index for
        /// every team rather than an exception on a screen the host is looking at.
        /// </summary>
        [Test]
        public void DefaultColorsSurviveAPaletteSmallerThanTheTeamCount()
        {
            int[] colors = TeamColorRules.DefaultColors(6, swatchCount: 3);

            Assert.AreEqual(6, colors.Length);
            foreach (int color in colors)
            {
                Assert.GreaterOrEqual(color, 0);
                Assert.Less(color, 3);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0103: The name 'TeamColorRules' does not exist`.

- [ ] **Step 3: Write TeamColorRules**

`Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs`:

```csharp
namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Which swatch each team wears, and how a member steps their own team's colour without
    /// landing on one another team already has.
    ///
    /// <para>
    /// The palette itself is not visible from here. <c>SuitPalette</c> lives in Assembly-CSharp,
    /// which an assembly definition cannot reference, so the swatch count arrives as an argument
    /// and the caller does the decoding. That is not a workaround — it is what makes the rule
    /// testable without Unity, and the rule is the part worth testing.
    /// </para>
    ///
    /// <para>
    /// Skipping matters more here than it looks. Two teams in the same orange is not a cosmetic
    /// annoyance in a game where the only thing telling you who to shoot is the colour of a suit.
    /// </para>
    /// </summary>
    public static class TeamColorRules
    {
        /// <summary>
        /// The next swatch in <paramref name="direction"/> that no other team is wearing.
        ///
        /// Walks the palette at most once and gives up back on <paramref name="current"/>: with
        /// every other swatch taken there is genuinely nowhere to go, and standing still is the
        /// honest answer. Wraps, so the cycler is a loop rather than a slider with two dead ends.
        /// </summary>
        public static int Step(int current, int direction, int swatchCount, int[] takenByOtherTeams)
        {
            if (swatchCount <= 0) return 0;

            int stride = direction >= 0 ? 1 : -1;
            int candidate = current;

            for (int step = 0; step < swatchCount; step++)
            {
                candidate = ((candidate + stride) % swatchCount + swatchCount) % swatchCount;

                if (!IsTaken(candidate, takenByOtherTeams)) return candidate;
            }

            return current;
        }

        /// <summary>
        /// The swatch each team starts on, spread across the palette so neighbouring teams are as
        /// far apart on the wheel as the palette allows.
        ///
        /// <para>
        /// Distinct while the palette is large enough, and merely valid when it is not: more teams
        /// than swatches cannot all differ, and a host looking at the rules page needs a colour per
        /// team far more than they need this method to refuse.
        /// </para>
        /// </summary>
        public static int[] DefaultColors(int teams, int swatchCount)
        {
            var colors = new int[teams < 0 ? 0 : teams];
            if (colors.Length == 0 || swatchCount <= 0) return colors;

            for (int team = 0; team < colors.Length; team++)
                colors[team] = team < swatchCount
                    ? team * swatchCount / colors.Length % swatchCount
                    : team % swatchCount;

            return Distinguish(colors, swatchCount);
        }

        /// <summary>
        /// Pushes any duplicate onto the next free swatch, so an uneven spread cannot collide.
        ///
        /// The spread above can repeat when the team count does not divide the palette; rather
        /// than reason about which cases those are, every result is walked once and fixed.
        /// </summary>
        private static int[] Distinguish(int[] colors, int swatchCount)
        {
            for (int team = 1; team < colors.Length; team++)
            {
                if (team >= swatchCount) break;

                while (IsTakenBefore(colors, team))
                    colors[team] = (colors[team] + 1) % swatchCount;
            }

            return colors;
        }

        private static bool IsTakenBefore(int[] colors, int team)
        {
            for (int other = 0; other < team; other++)
                if (colors[other] == colors[team])
                    return true;

            return false;
        }

        private static bool IsTaken(int swatch, int[] taken)
        {
            if (taken == null) return false;

            foreach (int other in taken)
                if (other == swatch)
                    return true;

            return false;
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass. Expected: no errors.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 8 `TeamColorRulesTests` pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/TeamColorRules.cs Assets/Game/Tests/EditMode/TeamColorRulesTests.cs
git commit -m "feat: team colour stepping that cannot collide"
```

---

## Task 3: Rank layout geometry

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs`
- Test: `Assets/Game/Tests/EditMode/RankLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Game/Tests/EditMode/RankLayoutTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Where the astronauts stand.
    ///
    /// These assert relationships, not metres — the same discipline LobbyLayoutTests uses on the
    /// join page, and for the same reason: the numbers are worked out on paper and there is no way
    /// to look at the result from here, so what has to be pinned is that teams stay apart, seats
    /// stay in their team, and the whole rank stays inside the shot.
    /// </summary>
    public class RankLayoutTests
    {
        [Test]
        public void SeatsInOneTeamAreCloserThanTheGapBetweenTeams()
        {
            Assert.Less(RankLayout.SeatSpacing, RankLayout.TeamGap,
                        "the clusters would read as one line");
        }

        [Test]
        public void ASmallTeamStandsInOneRow()
        {
            Assert.AreEqual(1, RankLayout.RowsFor(RankLayout.MaxSeatsPerRow));
        }

        [Test]
        public void ALargeTeamWrapsToASecondRow()
        {
            Assert.AreEqual(2, RankLayout.RowsFor(RankLayout.MaxSeatsPerRow + 1));
        }

        [Test]
        public void EverySeatOfATeamIsPlacedSomewhereDifferent()
        {
            const int teams = 3;
            const int teamSize = 5;

            var seen = new System.Collections.Generic.HashSet<Vector3>();

            for (int team = 0; team < teams; team++)
                for (int seat = 0; seat < teamSize; seat++)
                    Assert.IsTrue(seen.Add(RankLayout.SeatPosition(team, seat, teams, teamSize)),
                                  $"team {team} seat {seat} stands inside someone else");
        }

        /// <summary>
        /// The gap is the whole point of the grouping, so it is asserted directly: the nearest two
        /// seats across a team boundary must be further apart than two seats inside one team.
        /// </summary>
        [Test]
        public void TeamsAreSeparatedByMoreThanTheirOwnSeatSpacing()
        {
            const int teams = 2;
            const int teamSize = 3;

            float insideTeam = Vector3.Distance(RankLayout.SeatPosition(0, 0, teams, teamSize),
                                                RankLayout.SeatPosition(0, 1, teams, teamSize));

            float acrossTeams = Vector3.Distance(RankLayout.SeatPosition(0, teamSize - 1, teams, teamSize),
                                                 RankLayout.SeatPosition(1, 0, teams, teamSize));

            Assert.Greater(acrossTeams, insideTeam);
        }

        [Test]
        public void ATeamCentreSitsBetweenItsOwnSeats()
        {
            const int teams = 2;
            const int teamSize = 4;

            float centre = RankLayout.TeamCenter(0, teams, teamSize).x;
            float first = RankLayout.SeatPosition(0, 0, teams, teamSize).x;
            float last = RankLayout.SeatPosition(0, teamSize - 1, teams, teamSize).x;

            Assert.GreaterOrEqual(centre, Mathf.Min(first, last));
            Assert.LessOrEqual(centre, Mathf.Max(first, last));
        }

        [Test]
        public void TheRankIsCentredOnTheAnchor()
        {
            const int teams = 4;
            const int teamSize = 3;

            float left = RankLayout.TeamCenter(0, teams, teamSize).x;
            float right = RankLayout.TeamCenter(teams - 1, teams, teamSize).x;

            Assert.AreEqual(0f, left + right, 0.001f, "the rank drifts off its anchor");
        }

        [Test]
        public void AWiderRankNeedsTheCameraFurtherBack()
        {
            float near = RankLayout.CameraDistance(RankLayout.TotalWidth(2, 2), 60f, margin: 1.2f);
            float far = RankLayout.CameraDistance(RankLayout.TotalWidth(6, 4), 60f, margin: 1.2f);

            Assert.Greater(far, near);
        }

        [Test]
        public void TheFullestRankStillFitsTheShot()
        {
            float width = RankLayout.TotalWidth(VersusRules.MaxTeams,
                                                VersusRules.MaxSeats / VersusRules.MaxTeams);
            float distance = RankLayout.CameraDistance(width, 60f, margin: 1.2f);

            // Half the frustum's width at that distance has to cover half the rank, with the
            // margin still to spare.
            float halfFrame = distance * Mathf.Tan(60f * 0.5f * Mathf.Deg2Rad);

            Assert.GreaterOrEqual(halfFrame, width * 0.5f);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0103: The name 'RankLayout' does not exist`.

- [ ] **Step 3: Write RankLayout**

`Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs`:

```csharp
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where every astronaut in the VS lobby stands, and how far back the camera has to go to see
    /// them all.
    ///
    /// <para>
    /// Positions are in the anchor's local space: +X runs along the line, +Z runs away from the
    /// camera into the second row. The rank is centred on the anchor, so adding a team pushes the
    /// existing ones outwards symmetrically rather than sliding the whole line sideways.
    /// </para>
    ///
    /// <para>
    /// Seats are addressed by index whether or not anyone is standing in them, which is what stops
    /// a figure sliding sideways because somebody else joined — the rule the four fixed slots
    /// already held before there were teams. Empty seats simply draw nothing.
    /// </para>
    ///
    /// <para>
    /// The wrap is what makes 24 possible at all. A team of twelve in one line is eighteen metres
    /// of astronaut, and six of those is a rank no camera pull-back can frame legibly; wrapped four
    /// wide, the same twelve is a block six metres across.
    /// </para>
    /// </summary>
    public static class RankLayout
    {
        /// <summary>
        /// Metres between figures inside a team. Matches the spacing the four-figure rank already
        /// used, where anything tighter had each shoulder occluding the next suit's colour.
        /// </summary>
        public const float SeatSpacing = 1.45f;

        /// <summary>Metres between a team's front row and the row behind it.</summary>
        public const float RowSpacing = 1.6f;

        /// <summary>
        /// Metres of empty sand between two teams, measured between their nearest seats.
        ///
        /// Comfortably more than <see cref="SeatSpacing"/>: the gap is the only thing saying these
        /// are two groups rather than one line, and a gap that merely exceeds the spacing reads as
        /// an uneven line rather than as a division.
        /// </summary>
        public const float TeamGap = 3.2f;

        /// <summary>How wide a team gets before it stands in two rows.</summary>
        public const int MaxSeatsPerRow = 4;

        public static int SeatsPerRow(int teamSize) =>
            teamSize < MaxSeatsPerRow ? Mathf.Max(1, teamSize) : MaxSeatsPerRow;

        public static int RowsFor(int teamSize) =>
            Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, teamSize) / (float)MaxSeatsPerRow));

        /// <summary>How far across one team's block is, from its first seat to its last.</summary>
        public static float TeamWidth(int teamSize) => (SeatsPerRow(teamSize) - 1) * SeatSpacing;

        /// <summary>The whole rank, from the leftmost seat to the rightmost.</summary>
        public static float TotalWidth(int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);
            return count * TeamWidth(teamSize) + (count - 1) * (TeamGap + SeatSpacing);
        }

        /// <summary>
        /// The middle of a team's block, which is where its nameplate hangs and what a player
        /// clicks to join.
        /// </summary>
        public static Vector3 TeamCenter(int team, int teams, int teamSize)
        {
            float pitch = TeamWidth(teamSize) + TeamGap + SeatSpacing;
            float offset = (team - (Mathf.Max(1, teams) - 1) * 0.5f) * pitch;

            return new Vector3(offset, 0f, 0f);
        }

        /// <summary>
        /// One seat, in the anchor's local space.
        ///
        /// A partly-filled last row is centred under the rows above it, so a team of five reads as
        /// four and one in the middle rather than four and one hanging off the left edge.
        /// </summary>
        public static Vector3 SeatPosition(int team, int seat, int teams, int teamSize)
        {
            int perRow = SeatsPerRow(teamSize);
            int row = seat / perRow;
            int column = seat % perRow;

            int inThisRow = Mathf.Min(perRow, Mathf.Max(1, teamSize) - row * perRow);
            float x = (column - (inThisRow - 1) * 0.5f) * SeatSpacing;

            Vector3 centre = TeamCenter(team, teams, teamSize);
            return new Vector3(centre.x + x, 0f, row * RowSpacing);
        }

        /// <summary>
        /// How far the camera has to sit from the rank's centre to hold <paramref name="width"/>
        /// metres across, with <paramref name="margin"/> as headroom (1.2 leaves a fifth of the
        /// frame as air).
        ///
        /// Takes the horizontal field of view, because the rank is a horizontal problem — a camera
        /// fitted on its vertical FOV frames a rank of four and clips a rank of twenty-four.
        /// </summary>
        public static float CameraDistance(float width, float horizontalFovDegrees, float margin)
        {
            float halfAngle = Mathf.Max(1f, horizontalFovDegrees) * 0.5f * Mathf.Deg2Rad;
            return Mathf.Max(0.01f, width * margin * 0.5f / Mathf.Tan(halfAngle));
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass. Expected: no errors.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 9 `RankLayoutTests` pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs Assets/Game/Tests/EditMode/RankLayoutTests.cs
git commit -m "feat: team cluster geometry for the lobby rank"
```

---

## Task 4: The versus session that survives the load

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs`
- Test: `Assets/Game/Tests/EditMode/VersusSessionTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Game/Tests/EditMode/VersusSessionTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The handful of values that have to cross the load into the world scene.
    ///
    /// A static, like MatchSettings and WorldSession before it, because the menu that knows them is
    /// destroyed by the load that needs them. The tests here are mostly about the clearing: a
    /// static that outlives a match is exactly how the next one starts wearing the last one's
    /// colours.
    /// </summary>
    public class VersusSessionTests
    {
        [SetUp]
        public void Reset() => VersusSession.Clear();

        [TearDown]
        public void Clean() => VersusSession.Clear();

        [Test]
        public void StartsInactive()
        {
            Assert.IsFalse(VersusSession.IsActive);
        }

        [Test]
        public void BeginRecordsTheMatch()
        {
            VersusSession.Begin(teamCount: 3, teamSize: 2, localTeam: 1, teamColors: new[] { 4, 9, 1 });

            Assert.IsTrue(VersusSession.IsActive);
            Assert.AreEqual(3, VersusSession.TeamCount);
            Assert.AreEqual(2, VersusSession.TeamSize);
            Assert.AreEqual(1, VersusSession.LocalTeam);
            Assert.AreEqual(9, VersusSession.ColorOf(1));
        }

        [Test]
        public void ClearForgetsEverything()
        {
            VersusSession.Begin(3, 2, 1, new[] { 4, 9, 1 });
            VersusSession.Clear();

            Assert.IsFalse(VersusSession.IsActive);
            Assert.AreEqual(0, VersusSession.TeamCount);
            Assert.AreEqual(-1, VersusSession.LocalTeam);
        }

        /// <summary>
        /// A team index from a peer on a build with more teams must not throw on the way to a
        /// suit colour. Falling back is what keeps a mismatched build looking wrong rather than
        /// crashing.
        /// </summary>
        [Test]
        public void AnUnknownTeamHasAColourRatherThanAnException()
        {
            VersusSession.Begin(2, 2, 0, new[] { 4, 9 });

            Assert.DoesNotThrow(() => VersusSession.ColorOf(7));
            Assert.GreaterOrEqual(VersusSession.ColorOf(7), 0);
            Assert.GreaterOrEqual(VersusSession.ColorOf(-1), 0);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0103: The name 'VersusSession' does not exist`.

- [ ] **Step 3: Write VersusSession**

`Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs`:

```csharp
namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What a VS match carries across the load into the world scene: which team this peer is on,
    /// how many there are, and what colour each one wears.
    ///
    /// <para>
    /// A static for the same reason <c>MatchSettings</c> and <c>WorldSession</c> are: the lobby
    /// that knows these values is destroyed by the very load that needs them, so there is no object
    /// to hang them off. Statics outlive returning to the menu, which is why
    /// <see cref="Clear"/> exists and why every route out of a match calls it — a session left
    /// standing is how the next match starts wearing the last one's colours.
    /// </para>
    ///
    /// <para>
    /// Only the LOCAL peer's team is here. Everyone else's arrives over the wire on
    /// <c>PlayerIdentity</c>, which is already the thing that replicates who a player is and what
    /// colour they are painted.
    /// </para>
    /// </summary>
    public static class VersusSession
    {
        /// <summary>Whether the world being entered is a versus match rather than a story world.</summary>
        public static bool IsActive { get; private set; }

        public static int TeamCount { get; private set; }

        public static int TeamSize { get; private set; }

        /// <summary>Which team this peer stands on, or -1 before that is known.</summary>
        public static int LocalTeam { get; private set; } = -1;

        private static int[] colors = System.Array.Empty<int>();

        public static void Begin(int teamCount, int teamSize, int localTeam, int[] teamColors)
        {
            IsActive = true;
            TeamCount = teamCount;
            TeamSize = teamSize;
            LocalTeam = localTeam;
            colors = teamColors ?? System.Array.Empty<int>();
        }

        public static void Clear()
        {
            IsActive = false;
            TeamCount = 0;
            TeamSize = 0;
            LocalTeam = -1;
            colors = System.Array.Empty<int>();
        }

        /// <summary>
        /// The swatch a team wears, or swatch 0 for a team this build has never heard of.
        ///
        /// Guarded rather than indexed because the team index can arrive from a peer — over the
        /// wire, from a build with a different team count — and a suit that is the wrong orange is
        /// a great deal easier to understand than a player who failed to spawn.
        /// </summary>
        public static int ColorOf(int team) =>
            team >= 0 && team < colors.Length ? colors[team] : 0;
    }
}
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass. Expected: no errors.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 4 `VersusSessionTests` pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/VersusSession.cs Assets/Game/Tests/EditMode/VersusSessionTests.cs
git commit -m "feat: versus session carried into the world scene"
```

---

---

## Task 5: The MenuStepper widget

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Widgets/MenuStepper.cs`
- Test: `Assets/Game/Editor/Tests/MenuStepperTests.cs`

This touches uGUI, so it lives in Assembly-CSharp and its test goes in `Assets/Game/Editor/Tests/` — the only test folder that can see Assembly-CSharp types.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/MenuStepperTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The number row the rules page and the lobby's host strip are both built from.
    ///
    /// Built for real rather than asserted on paper, because unlike the pinned layout constants
    /// this widget has behaviour: the chevrons have to call back, the value has to redraw, and the
    /// row has to stop at its own limits.
    /// </summary>
    public class MenuStepperTests
    {
        private RectTransform parent;

        [SetUp]
        public void Build()
        {
            var host = new GameObject("StepperHost", typeof(RectTransform));
            parent = (RectTransform)host.transform;
        }

        [TearDown]
        public void Clean()
        {
            if (parent != null) Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void ShowsTheValueItWasGiven()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            Assert.AreEqual("3", stepper.ValueLabel.text);
        }

        [Test]
        public void ThePlusChevronReportsOneMore()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, v => reported = v);

            stepper.Increase.onClick.Invoke();

            Assert.AreEqual(4, reported);
        }

        [Test]
        public void TheMinusChevronReportsOneLess()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, v => reported = v);

            stepper.Decrease.onClick.Invoke();

            Assert.AreEqual(2, reported);
        }

        /// <summary>
        /// The row reports what was asked for and redraws only from what it is told, so a caller
        /// that refuses the change leaves the number where it was rather than having to put it back.
        /// </summary>
        [Test]
        public void TheValueOnlyChangesWhenTheCallerSaysSo()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            stepper.Increase.onClick.Invoke();
            Assert.AreEqual("3", stepper.ValueLabel.text, "the row moved on its own");

            stepper.SetValue(4);
            Assert.AreEqual("4", stepper.ValueLabel.text);
        }

        [Test]
        public void ItStopsAtItsLimits()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 8, 2, 8, v => reported = v);

            stepper.Increase.onClick.Invoke();
            Assert.AreEqual(8, reported, "stepped past its ceiling");

            stepper.SetValue(2);
            stepper.Decrease.onClick.Invoke();
            Assert.AreEqual(2, reported, "stepped below its floor");
        }

        /// <summary>
        /// A locked row is what the lobby shows a joiner: the numbers are the host's, and a client
        /// handed live chevrons gets a control whose whole behaviour is to fail.
        /// </summary>
        [Test]
        public void ItCanBeShownWithoutBeingUsable()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            stepper.SetInteractable(false);

            Assert.IsFalse(stepper.Increase.interactable);
            Assert.IsFalse(stepper.Decrease.interactable);
            Assert.IsTrue(stepper.Root.gameObject.activeSelf, "a locked row still has to be readable");
        }

        [Test]
        public void ItFitsInsideTheColumnItIsBuiltFor()
        {
            Assert.LessOrEqual(
                MenuStepper.LabelWidth + MenuStepper.ChevronWidth * 2 + MenuStepper.ValueWidth,
                MenuEntry.ColumnWidth);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll `Temp/headless_tests.txt` for `DONE`.
Expected: compile failure, `CS0246: The type or namespace name 'MenuStepper' could not be found`.

- [ ] **Step 3: Write MenuStepper**

`Assets/Game/Scripts/Presentation/UI/Widgets/MenuStepper.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A number with a chevron either side of it — this menu's substitute for a slider.
    ///
    /// <para>
    /// The menu's language has no boxes and no coloured bars, so a numeric control cannot be drawn
    /// as either. What is left is the number and two things to press, which suits these screens
    /// better than a handle on a track: the values being tuned are small integers a host picks
    /// exactly, not proportions they feel their way towards.
    /// </para>
    ///
    /// <para>
    /// This shape already existed, privately, inside <c>MinigameConfigUI</c>. It is here so the VS
    /// rules page and the lobby's host strip share one implementation rather than becoming the
    /// second and third copies of it.
    /// </para>
    ///
    /// <para>
    /// <b>It reports, it does not decide.</b> A chevron raises <c>onChanged</c> with the value that
    /// was asked for and changes nothing; the caller redraws it with <see cref="SetValue"/> once it
    /// has decided. That split is what lets the lobby refuse a change — "Team One has 3 players" —
    /// without the row having already moved and needing to be put back.
    /// </para>
    /// </summary>
    public class MenuStepper
    {
        /// <summary>Wide enough for the longest label these screens use ("Team size").</summary>
        public const float LabelWidth = 330f;

        public const float ChevronWidth = 74f;
        public const float ValueWidth = 96f;

        /// <summary>Tall enough for the value at <see cref="MenuEntry.ActionSize"/>, plus air.</summary>
        public const float Height = 74f;

        // ASCII, not the arrow glyphs. The project's TMP default is LiberationSans SDF, which has
        // neither U+25C0 nor U+25B6 and no fallback that does — TMP substitutes U+25A1 and both
        // chevrons render as empty boxes. LobbyPreviewRank's colour cycler carries the same note
        // and the same two characters.
        private const string DecreaseGlyph = "<";
        private const string IncreaseGlyph = ">";

        /// <summary>The whole row, so a caller can show or hide it.</summary>
        public RectTransform Root { get; private set; }

        public TextMeshProUGUI ValueLabel { get; private set; }

        public Button Decrease { get; private set; }

        public Button Increase { get; private set; }

        private int value;
        private int min;
        private int max;

        /// <summary>
        /// Builds one row inside <paramref name="parent"/>.
        ///
        /// <paramref name="prefab"/> is the menu's own button, so the chevrons carry its hover
        /// animation and its two FMOD sounds; null builds them plainly in the same palette, the
        /// same fallback <see cref="MenuEntry"/> makes.
        /// </summary>
        public static MenuStepper Create(GameObject prefab, RectTransform parent, string label,
            int value, int min, int max, Action<int> onChanged)
        {
            var stepper = new MenuStepper { value = value, min = min, max = max };

            stepper.Root = UIBuilder.Rect(label, parent);
            UIBuilder.Fill(stepper.Root);
            UIBuilder.FixedHeight(stepper.Root, Height);

            RectTransform labelSlot = Slice(stepper.Root, "Label", 0f, LabelWidth);
            UIBuilder.Label(labelSlot, label, MenuEntry.RowSize, MenuEntry.Caption);

            RectTransform lessSlot = Slice(stepper.Root, "Less", LabelWidth, ChevronWidth);
            stepper.Decrease = Chevron(prefab, lessSlot, "Less", DecreaseGlyph,
                                       () => stepper.Report(-1, onChanged));

            RectTransform valueSlot = Slice(stepper.Root, "Value", LabelWidth + ChevronWidth, ValueWidth);
            stepper.ValueLabel = UIBuilder.Label(valueSlot, value.ToString(), MenuEntry.ActionSize,
                                                 MenuEntry.Idle, TextAlignmentOptions.Center,
                                                 FontStyles.Bold);

            RectTransform moreSlot = Slice(stepper.Root, "More",
                                           LabelWidth + ChevronWidth + ValueWidth, ChevronWidth);
            stepper.Increase = Chevron(prefab, moreSlot, "More", IncreaseGlyph,
                                       () => stepper.Report(1, onChanged));

            return stepper;
        }

        /// <summary>Redraws the row. The caller's answer to a chevron, and how a poll repaints it.</summary>
        public void SetValue(int newValue)
        {
            value = newValue;
            if (ValueLabel != null) ValueLabel.text = newValue.ToString();
        }

        /// <summary>Widens or narrows what the chevrons will report, for when the other axis moves.</summary>
        public void SetLimits(int newMin, int newMax)
        {
            min = newMin;
            max = newMax;
        }

        /// <summary>
        /// Locks the chevrons without hiding the row — a joiner reads the host's numbers, they just
        /// cannot press them.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (Decrease != null) Decrease.interactable = interactable;
            if (Increase != null) Increase.interactable = interactable;
        }

        private void Report(int direction, Action<int> onChanged)
        {
            int wanted = Mathf.Clamp(value + direction, min, max);
            onChanged?.Invoke(wanted);
        }

        /// <summary>
        /// A chevron, drawn in <see cref="MenuEntry.Caption"/> rather than the menu's navy.
        ///
        /// These are the parts you actually aim at, and the resting navy over dark terrain is close
        /// to invisible at this size — the same reasoning the minigame screen's stepper buttons
        /// carried.
        /// </summary>
        private static Button Chevron(GameObject prefab, RectTransform slot, string name, string glyph,
            Action onClick)
        {
            Button button = MenuEntry.Create(prefab, slot, name, glyph, MenuEntry.ActionSize, Height,
                                             () => onClick(), out TextMeshProUGUI label);

            label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        /// <summary>A fixed-width column inside the row, measured from its left edge.</summary>
        private static RectTransform Slice(RectTransform parent, string name, float fromLeft, float width)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(fromLeft, 0f);
            rect.offsetMax = new Vector2(fromLeft + width, 0f);
            return rect;
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass from "Before you start". Expected: no errors.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 7 `MenuStepperTests` pass.

- [ ] **Step 6: Commit**

Stage `Assets/Game/Scripts/Presentation/UI/Widgets/MenuStepper.cs` and `Assets/Game/Editor/Tests/MenuStepperTests.cs`, then commit with the message `feat: shared menu stepper widget`.

---

## Task 6: MenuChoiceUI, and the menu's new routes

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Pages/MenuChoiceUI.cs`
- Delete: `Assets/Game/Scripts/Presentation/UI/Pages/MultiplayerChoiceUI.cs` and its `.meta`
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs`
- Test: `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs`

`LobbyUI.Open` gains a route parameter in Task 11. Until then the two versus routes below call today's single-argument `LobbyUI.Open(this)`; Task 11 replaces exactly those two call sites.

- [ ] **Step 1: Update the wiring test**

In `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs`, replace the `MainMenuUI_KeepsItsSceneBoundMethods` cases and the whole `MainMenuUI_KeepsTheMultiplayerRoutes` method with:

```csharp
        [TestCase("StartStory")]
        [TestCase("StartVersus")]
        [TestCase("StartMultiPlayer")]
        [TestCase("StartSinglePlayer")]
        [TestCase("StartMinigame")]
        [TestCase("QuitGame")]
        public void MainMenuUI_KeepsItsSceneBoundMethods(string methodName)
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetMethod(methodName, Public),
                $"MainMenu.unity binds a menu entry to MainMenuUI.{methodName} by name. " +
                "Removing or renaming it makes that entry silently do nothing.");
        }

        /// <summary>
        /// Both second-level pages are chains where only the first link is by name:
        /// MainMenu.unity → StartStory/StartVersus (string) → MenuChoiceUI (compiled) → these
        /// (compiled). Pinning the endpoints is what keeps a rename from turning a front-menu entry
        /// into a button that opens the wrong screen.
        /// </summary>
        [TestCase("HostMultiplayer")]
        [TestCase("JoinMultiplayer")]
        [TestCase("HostVersus")]
        [TestCase("JoinVersus")]
        [TestCase("EnterVersusLobby")]
        public void MainMenuUI_KeepsTheRoutesItsChoicePagesCallBackInto(string methodName)
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetMethod(methodName, Public),
                $"A choice page calls MainMenuUI.{methodName}.");
        }

        /// <summary>
        /// The screen that replaced MultiplayerChoiceUI. Three near-identical bespoke pages is the
        /// duplication that deletion removed; a test naming the survivor is what stops a fourth.
        /// </summary>
        [Test]
        public void TheChoicePageIsShared()
        {
            Assert.IsNotNull(typeof(MenuChoiceUI).GetMethod("Open", BindingFlags.Public | BindingFlags.Static),
                "MenuChoiceUI.Open is what every second-level menu page is built from.");
        }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0246: The type or namespace name 'MenuChoiceUI' could not be found`.

- [ ] **Step 3: Write MenuChoiceUI**

`Assets/Game/Scripts/Presentation/UI/Pages/MenuChoiceUI.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A menu page that asks one question and offers two or three answers, plus Back.
    ///
    /// <para>
    /// This is the shape the front menu now needs three times over — Story asks single or multi,
    /// Multiplayer asks host or join, VS asks host or join — and it used to be a bespoke screen per
    /// question. The first of those, <c>MultiplayerChoiceUI</c>, is what this replaces: three copies
    /// of "build a column, put two entries and a Back in it, close before routing" is precisely the
    /// duplication that has to be extracted rather than repeated.
    /// </para>
    ///
    /// <para>
    /// Why host and join are asked at all, rather than the menu going straight somewhere: the two
    /// answers need different things and one "Multiplayer" button had to serve both. It opened the
    /// world list, so a player who only wanted to join a friend had to invent a world first — and
    /// having done so, arrived in the lobby carrying a staged save that the host's world would then
    /// load over. Asking first is what makes joining possible at all.
    /// </para>
    ///
    /// <para>
    /// Built at runtime rather than authored into MainMenu.unity, and drawn from
    /// <see cref="MenuEntry"/> rather than in white. This screen sits between the main menu and
    /// whatever it opens, all of which clone the menu's own button prefab and draw in its navy; a
    /// screen in the middle with its own colours reads as a seam in a flow meant to be one.
    /// </para>
    /// </summary>
    public class MenuChoiceUI : MenuScreen
    {
        /// <summary>One answer: what it says, and where it goes.</summary>
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly Action Go;

            public Choice(string label, Action go)
            {
                Label = label;
                Go = go;
            }
        }

        private const float ActionHeight = 78f;

        /// <summary>Between two answers. The menu's own entries sit about this far apart.</summary>
        private const float ChoiceGap = 30f;

        /// <summary>Between the answers and Back — enough that Back is plainly not a third answer.</summary>
        private const float BackGap = 44f;

        private MainMenuUI menu;
        private string title;
        private Choice[] choices;

        public static MenuChoiceUI Open(MainMenuUI owner, string title, params Choice[] choices)
        {
            var existing = FindFirstObjectByType<MenuChoiceUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(MenuChoiceUI)).AddComponent<MenuChoiceUI>();
            ui.menu = owner;
            ui.title = title;
            ui.choices = choices;
            ui.Present();
            return ui;
        }

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        /// <summary>
        /// Closes this screen, then routes.
        ///
        /// Closing first is not optional. The menu's canvases go back on inside
        /// <see cref="MenuScreen.Close"/>, and whatever this opens switches off whatever it finds
        /// enabled — so a screen left alive with the menu switched off would sit underneath the next
        /// one forever, holding canvases off that only it remembers having hidden.
        ///
        /// The action is read into a local because Close destroys this object, and reaching for a
        /// field afterwards is reading a field on a corpse.
        /// </summary>
        private void Route(Action go)
        {
            if (go == null)
            {
                Debug.LogError("[MenuChoiceUI] A choice was built with nowhere to go.");
                return;
            }

            Action destination = go;
            Close();
            destination();
        }

        protected override void Build()
        {
            RectTransform titleRect = PinnedRow(Surface, MenuEntry.TitleTop, MenuEntry.TitleHeight);
            UIBuilder.Label(titleRect, title, MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform column = UIBuilder.Rect("Column", Surface);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(MenuEntry.ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(MenuEntry.ColumnWidth, 0f);

            UIBuilder.Column(column, 6f);
            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (choices != null)
            {
                for (int i = 0; i < choices.Length; i++)
                {
                    if (i > 0) UIBuilder.Spacer(column, ChoiceGap);

                    Choice choice = choices[i];
                    Entry(column, $"Choice{i}", choice.Label, () => Route(choice.Go));
                }
            }

            UIBuilder.Spacer(column, BackGap);
            Entry(column, "BackButton", "Back", Close);
        }

        private void Entry(RectTransform column, string name, string label,
            UnityEngine.Events.UnityAction onClick) =>
            MenuEntry.Create(EntryPrefab, column, name, label, MenuEntry.ActionSize, ActionHeight,
                             onClick, out _);

        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float height)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MenuEntry.ColumnX, fromTop);
            rect.sizeDelta = new Vector2(MenuEntry.ColumnWidth, height);
            return rect;
        }
    }
}
```

- [ ] **Step 4: Delete MultiplayerChoiceUI**

```bash
git rm Assets/Game/Scripts/Presentation/UI/Pages/MultiplayerChoiceUI.cs Assets/Game/Scripts/Presentation/UI/Pages/MultiplayerChoiceUI.cs.meta
```

- [ ] **Step 5: Add the new routes to MainMenuUI**

In `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs`, add `using SpaceGame.Gameplay;` to the usings (for `VersusSession`), then replace the single line

```csharp
    public void StartMultiPlayer() => MultiplayerChoiceUI.Open(this);
```

with:

```csharp
    /// <summary>
    /// The front menu's first question, and the only thing it asks before anything is committed to.
    ///
    /// Story is the game as it was: a world of your own, alone or with friends. Bound by name from
    /// MainMenu.unity; do not rename.
    /// </summary>
    public void StartStory() => MenuChoiceUI.Open(this, "STORY",
        new MenuChoiceUI.Choice("Singleplayer", StartSinglePlayer),
        new MenuChoiceUI.Choice("Multiplayer", StartMultiPlayer));

    /// <summary>
    /// Versus: multiplayer only, and no world to choose — what is being set up is a match, not a
    /// place. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartVersus() => MenuChoiceUI.Open(this, "VERSUS",
        new MenuChoiceUI.Choice("Host a game", HostVersus),
        new MenuChoiceUI.Choice("Join a game", JoinVersus));

    /// <summary>
    /// Asks host or join before anything else.
    ///
    /// This used to open the world list directly, which made picking a world a toll on every route
    /// into multiplayer — including joining, where the world is the host's and the one the joiner
    /// picked is at best ignored. Reached from the Story page now rather than from the menu itself,
    /// but still bound by name in MainMenu.unity's older wiring; do not rename.
    /// </summary>
    public void StartMultiPlayer() => MenuChoiceUI.Open(this, "MULTIPLAYER",
        new MenuChoiceUI.Choice("Host a game", HostMultiplayer),
        new MenuChoiceUI.Choice("Join a game", JoinMultiplayer));

    /// <summary>VS host: the rules first, then the lobby. Called back from the versus choice page.</summary>
    public void HostVersus() => VersusRulesUI.Open(this);

    /// <summary>
    /// VS join: straight to the lobby, with no world and no rules of our own.
    ///
    /// Both are cleared for the reason JoinMultiplayer clears the staged world: SaveManager.Awake
    /// consumes whatever is staged on every peer, client included, and the rules belong to the
    /// host's match rather than to whatever this player last hosted.
    /// </summary>
    public void JoinVersus()
    {
        WorldSession.Clear();
        VersusSession.Clear();
        LobbyUI.Open(this);
    }

    /// <summary>
    /// Opens the VS lobby once the host has settled the rules. Called back from VersusRulesUI.
    ///
    /// The world is cleared rather than staged: a VS match is transient, and a host arriving with a
    /// save staged would load their own world into a match nobody is going to save.
    /// </summary>
    public void EnterVersusLobby()
    {
        WorldSession.Clear();
        LobbyUI.Open(this);
    }
```

- [ ] **Step 6: Type-check**

Run the Roslyn pass. Expected: one error, `CS0246: The type or namespace name 'VersusRulesUI' could not be found` — that type arrives in Task 8. Every other error is yours to fix now.

If you are executing tasks strictly in order, go straight to Task 8 and run the tests at the end of it. Do not leave a stub behind.

- [ ] **Step 7: Commit**

Stage `Assets/Game/Scripts/Presentation/UI/Pages/` (including the deletion) and `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs`, then commit with the message `feat: shared choice page, story and versus routes`.

---

## Task 7: Relabel and rebind the front menu

**Files:**
- Create: `Assets/Game/Editor/Menus/FrontMenuSetup.cs`
- Modify: `Assets/Game/Scenes/Core/MainMenu.unity` (by running the tool, never by hand)

The three ButtonRow entries are **prefab instances** of `Menu Button.prefab`, so their label and their `onClick` are property modifications on the instance. Editing that YAML by hand is how a scene ends up with an override the prefab has no target for; the tool below goes through `PrefabUtility`, which is what records an override correctly.

- [ ] **Step 1: Write the setup tool**

`Assets/Game/Editor/Menus/FrontMenuSetup.cs`:

```csharp
// Relabels MainMenu.unity's ButtonRow for the two-mode front menu.
//
// The menu's entries are prefab instances whose label and onClick are property modifications on the
// instance, so this cannot be a runtime job and must not be a hand edit of the YAML: an override
// written by hand is not recorded against the prefab, and the next prefab change silently drops it.
// PrefabUtility.RecordPrefabInstancePropertyModifications is what makes the change stick.
//
// Idempotent and safe to re-run: each entry is found by the method it is currently bound to, and a
// second run finds it already bound to the new one and changes nothing.
//
// Run from: Tools ▸ SpaceGame ▸ Menus ▸ Setup Front Menu
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceGame.EditorTools
{
    public static class FrontMenuSetup
    {
        private const string ScenePath = "Assets/Game/Scenes/Core/MainMenu.unity";
        private const string ButtonRowName = "ButtonRow";

        /// <summary>
        /// What each entry becomes, keyed by the MainMenuUI method it is bound to today.
        ///
        /// Keyed on the binding rather than on the GameObject's name because the binding is what
        /// actually decides where an entry goes — an entry named "Multiplayer" that calls
        /// StartSinglePlayer is exactly the failure this is meant to make impossible.
        /// </summary>
        private static readonly (string FromMethod, string Label, string ToMethod)[] Entries =
        {
            ("StartSinglePlayer", "Story", "StartStory"),
            ("StartMultiPlayer", "VS", "StartVersus"),
            ("QuitGame", "Quit", "QuitGame")
        };

        [MenuItem("Tools/SpaceGame/Menus/Setup Front Menu")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[FrontMenuSetup] Exit Play mode first — a scene edited during play " +
                               "mode is discarded when play mode ends.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var menu = Object.FindFirstObjectByType<MainMenuUI>();
            if (menu == null)
            {
                Debug.LogError($"[FrontMenuSetup] No MainMenuUI in {ScenePath}. Nothing to wire.");
                return;
            }

            Transform row = FindRow(scene);
            if (row == null)
            {
                Debug.LogError($"[FrontMenuSetup] No '{ButtonRowName}' in {ScenePath}.");
                return;
            }

            int rewired = Rewire(row, menu);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[FrontMenuSetup] Rewired {rewired} of {Entries.Length} entries. Story and VS " +
                      "are the front menu; Singleplayer and Multiplayer moved under Story.");
        }

        private static Transform FindRow(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindByName(root.transform, ButtonRowName);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindByName(Transform node, string name)
        {
            if (node.name == name) return node;

            foreach (Transform child in node)
            {
                Transform found = FindByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        private static int Rewire(Transform row, MainMenuUI menu)
        {
            int rewired = 0;

            foreach ((string fromMethod, string label, string toMethod) in Entries)
            {
                Button button = FindEntryBoundTo(row, fromMethod, toMethod);

                if (button == null)
                {
                    Debug.LogWarning($"[FrontMenuSetup] No entry bound to MainMenuUI.{fromMethod} " +
                                     $"or {toMethod}. Leaving the row alone.");
                    continue;
                }

                var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text != null) text.text = label;

                button.gameObject.name = label;

                // Cleared and rebuilt rather than edited in place: a persistent call whose method
                // name is rewritten keeps the old argument shape, and a UnityEvent that cannot
                // resolve its target does nothing at all and says nothing about it.
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(button.onClick, i);

                UnityEventTools.AddVoidPersistentListener(button.onClick, menu, toMethod);

                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                if (text != null) PrefabUtility.RecordPrefabInstancePropertyModifications(text);

                rewired++;
            }

            return rewired;
        }

        /// <summary>
        /// The entry currently bound to <paramref name="fromMethod"/>, or the one already bound to
        /// <paramref name="toMethod"/> so a second run finds it and changes nothing.
        /// </summary>
        private static Button FindEntryBoundTo(Transform row, string fromMethod, string toMethod)
        {
            foreach (Button button in row.GetComponentsInChildren<Button>(true))
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    string method = button.onClick.GetPersistentMethodName(i);
                    if (method == fromMethod || method == toMethod) return button;
                }
            }

            return null;
        }
    }
}
```

- [ ] **Step 2: Type-check**

Run the Roslyn pass, then the `Assembly-CSharp-Editor.rsp` pass described in "Before you start".
Expected: no errors.

- [ ] **Step 3: Run the tool**

In the Editor, click `Tools ▸ SpaceGame ▸ Menus ▸ Setup Front Menu`.
Expected in the console: `[FrontMenuSetup] Rewired 3 of 3 entries.`

- [ ] **Step 4: Verify the scene actually changed**

```bash
grep -c "value: StartStory" Assets/Game/Scenes/Core/MainMenu.unity
grep -c "value: StartVersus" Assets/Game/Scenes/Core/MainMenu.unity
grep -c "value: StartMultiPlayer" Assets/Game/Scenes/Core/MainMenu.unity
```

Expected: `1`, `1`, `0`. A `0` on either of the first two means the tool did not save — check for a Unity console error before re-running.

- [ ] **Step 5: Commit**

Stage `Assets/Game/Editor/Menus/FrontMenuSetup.cs` and `Assets/Game/Scenes/Core/MainMenu.unity`, then commit with the message `feat: story and VS on the front menu`.

---

## Task 8: The VS rules page

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs`
- Test: `Assets/Game/Editor/Tests/VersusRulesUITests.cs`

`VersusRulesUI` also owns the staged rules the lobby is created from — two statics beside its own screen, the way `MinigameConfigUI` owns `MatchSettings.ResetToDefaults()`. Keeping them here rather than in `VersusRules` is deliberate: `VersusRules` is the arithmetic and has no business holding mutable state.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/VersusRulesUITests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rules staged for the lobby that is about to be created.
    ///
    /// Statics outlive returning to the menu, so the interesting behaviour is the reseeding: every
    /// visit to this screen has to start from the defaults rather than from whatever the last match
    /// left behind. MatchSettings carries the same note for the same reason.
    /// </summary>
    public class VersusRulesUITests
    {
        [TearDown]
        public void Reset() => VersusRulesUI.ResetToDefaults();

        [Test]
        public void ResetPutsTheDefaultsBack()
        {
            VersusRulesUI.StagedTeams = 7;
            VersusRulesUI.StagedTeamSize = 3;

            VersusRulesUI.ResetToDefaults();

            Assert.AreEqual(VersusRules.DefaultTeams, VersusRulesUI.StagedTeams);
            Assert.AreEqual(VersusRules.DefaultTeamSize, VersusRulesUI.StagedTeamSize);
        }

        [Test]
        public void TheStagedRulesAlwaysFitTheCeiling()
        {
            VersusRulesUI.StageTeams(VersusRules.MaxTeams);
            VersusRulesUI.StageTeamSize(VersusRules.MaxTeamSize);

            Assert.LessOrEqual(VersusRules.Seats(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize),
                               VersusRules.MaxSeats);
        }

        [Test]
        public void StagingClampsRatherThanRefusing()
        {
            VersusRulesUI.StageTeams(99);
            Assert.LessOrEqual(VersusRulesUI.StagedTeams, VersusRules.MaxTeams);

            VersusRulesUI.StageTeams(0);
            Assert.GreaterOrEqual(VersusRulesUI.StagedTeams, VersusRules.MinTeams);
        }

        /// <summary>
        /// Raising one axis has to be allowed to pull the other one down, or the ceiling is
        /// enforced by refusing the host's press with no explanation on screen.
        /// </summary>
        [Test]
        public void RaisingTeamsMayShrinkTeamSize()
        {
            VersusRulesUI.StageTeamSize(VersusRules.MaxTeamSize);
            VersusRulesUI.StageTeams(VersusRules.MaxTeams);

            Assert.AreEqual(VersusRules.MaxTeams, VersusRulesUI.StagedTeams);
            Assert.LessOrEqual(VersusRules.Seats(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize),
                               VersusRules.MaxSeats);
        }

        [Test]
        public void TheSeatCaptionSaysBothNumbers()
        {
            string caption = VersusRulesUI.DescribeSeats(3, 4);

            StringAssert.Contains("12", caption);
            StringAssert.Contains(VersusRules.MaxSeats.ToString(), caption);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0246: The type or namespace name 'VersusRulesUI' could not be found`.

- [ ] **Step 3: Write VersusRulesUI**

`Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The screen between "Host a game" and the VS lobby: how many teams, and how big.
    ///
    /// <para>
    /// It is the only extra step VS adds, and it replaces one rather than adding to it — a Story
    /// host picks a world here, and a VS host has no world to pick. Both routes are three screens
    /// deep, which is what keeps the new mode from feeling like the long way round.
    /// </para>
    ///
    /// <para>
    /// The two numbers are not independent, and the screen says so with the seat caption rather
    /// than by refusing presses: teams × size is capped at <see cref="VersusRules.MaxSeats"/>,
    /// because that is what the host allocates on Relay and Relay's allocation cannot grow
    /// afterwards. Raising one axis therefore pulls the other down instead of the chevron doing
    /// nothing — a control that silently declines is the one thing worse than a smaller number.
    /// </para>
    ///
    /// <para>
    /// The staged values live here as statics, the way <c>MatchSettings</c> lives beside the
    /// minigame's config screen. <see cref="VersusRules"/> is the arithmetic and holds no state; a
    /// clamp table with mutable fields in it is a rulebook that can be wrong.
    /// </para>
    /// </summary>
    public class VersusRulesUI : MenuScreen
    {
        private const float ActionHeight = 78f;

        /// <summary>Between the last stepper and the seat caption.</summary>
        private const float CaptionGap = 26f;

        /// <summary>Between the caption and the actions, so Start is not read as a third row.</summary>
        private const float ActionGap = 30f;

        /// <summary>What the host is about to create, read once by the lobby.</summary>
        public static int StagedTeams { get; set; } = VersusRules.DefaultTeams;

        public static int StagedTeamSize { get; set; } = VersusRules.DefaultTeamSize;

        private MainMenuUI menu;

        private MenuStepper teamStepper;
        private MenuStepper sizeStepper;
        private TextMeshProUGUI seatCaption;

        public static VersusRulesUI Open(MainMenuUI owner)
        {
            var existing = FindFirstObjectByType<VersusRulesUI>();
            if (existing != null) return existing;

            // Before the screen is built, so what it draws is the defaults rather than the last
            // match's numbers. Statics survive a return to the menu; this is the only thing that
            // stops them surviving into a match that was never configured.
            ResetToDefaults();

            var ui = new GameObject(nameof(VersusRulesUI)).AddComponent<VersusRulesUI>();
            ui.menu = owner;
            ui.Present();
            return ui;
        }

        public static void ResetToDefaults()
        {
            StagedTeams = VersusRules.DefaultTeams;
            StagedTeamSize = VersusRules.DefaultTeamSize;
        }

        /// <summary>
        /// Takes a team count, then pulls team size down under the ceiling if it has to.
        ///
        /// The axis the host just moved wins. Clamping the pressed axis instead would make the
        /// chevron look broken at exactly the moment the host is trying to use it.
        /// </summary>
        public static void StageTeams(int teams)
        {
            StagedTeams = VersusRules.ClampTeams(teams, VersusRules.MinTeamSize);
            StagedTeamSize = VersusRules.ClampTeamSize(StagedTeamSize, StagedTeams);
        }

        public static void StageTeamSize(int teamSize)
        {
            StagedTeamSize = VersusRules.ClampTeamSize(teamSize, VersusRules.MinTeams);
            StagedTeams = VersusRules.ClampTeams(StagedTeams, StagedTeamSize);
        }

        /// <summary>"12 of 24 seats" — the only place the ceiling is explained.</summary>
        public static string DescribeSeats(int teams, int teamSize) =>
            $"{VersusRules.Seats(teams, teamSize)} of {VersusRules.MaxSeats} seats";

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        protected override void Build()
        {
            RectTransform titleRect = PinnedRow(Surface, MenuEntry.TitleTop, MenuEntry.TitleHeight);
            UIBuilder.Label(titleRect, "VERSUS", MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform column = UIBuilder.Rect("Column", Surface);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(MenuEntry.ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(MenuEntry.ColumnWidth, 0f);

            UIBuilder.Column(column, 6f);
            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            teamStepper = MenuStepper.Create(EntryPrefab, column, "Teams", StagedTeams,
                                             VersusRules.MinTeams, VersusRules.MaxTeams,
                                             teams => { StageTeams(teams); Refresh(); });

            sizeStepper = MenuStepper.Create(EntryPrefab, column, "Team size", StagedTeamSize,
                                             VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                                             size => { StageTeamSize(size); Refresh(); });

            UIBuilder.Spacer(column, CaptionGap);

            RectTransform captionRow = UIBuilder.Rect("Seats", column);
            UIBuilder.FixedHeight(captionRow, 40f);
            seatCaption = UIBuilder.Label(captionRow, string.Empty, MenuEntry.CaptionSize,
                                          MenuEntry.Caption);

            UIBuilder.Spacer(column, ActionGap);

            Entry(column, "StartButton", "Start lobby", StartLobby);
            Entry(column, "BackButton", "Back", Close);

            Refresh();
        }

        /// <summary>Redraws both rows and the caption from the staged values.</summary>
        private void Refresh()
        {
            teamStepper.SetValue(StagedTeams);
            sizeStepper.SetValue(StagedTeamSize);

            seatCaption.text = DescribeSeats(StagedTeams, StagedTeamSize);
        }

        /// <summary>
        /// Hands off to the lobby, which creates the session sized to these rules.
        ///
        /// Closes first, for the reason every screen in this flow does: the menu's canvases go back
        /// on inside Close, and the lobby switches off whatever it finds enabled. A screen left
        /// alive underneath would hold canvases off that only it remembers having hidden.
        /// </summary>
        private void StartLobby()
        {
            if (menu == null)
            {
                Debug.LogError("[VersusRulesUI] No MainMenuUI to route through.");
                return;
            }

            MainMenuUI owner = menu;
            Close();
            owner.EnterVersusLobby();
        }

        private void Entry(RectTransform column, string name, string label,
            UnityEngine.Events.UnityAction onClick) =>
            MenuEntry.Create(EntryPrefab, column, name, label, MenuEntry.ActionSize, ActionHeight,
                             onClick, out _);

        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float height)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MenuEntry.ColumnX, fromTop);
            rect.sizeDelta = new Vector2(MenuEntry.ColumnWidth, height);
            return rect;
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass. Expected: no errors — this is also what clears the `CS0246` Task 6 left behind.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 5 `VersusRulesUITests` pass, and every `LobbyMenuWiringTests` case from Task 6 now passes too.

- [ ] **Step 6: Commit**

Stage `Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs` and `Assets/Game/Editor/Tests/VersusRulesUITests.cs`, then commit with the message `feat: versus rules page`.

---

## Task 9: Versus keys, encoding, and the roster snapshot

**Files:**
- Create: `Assets/Game/Scripts/Core/Multiplayer/VersusSetup.cs`
- Create: `Assets/Game/Scripts/Core/Multiplayer/RosterSnapshot.cs`
- Modify: `Assets/Game/Scripts/Core/Multiplayer/LobbySessionOptions.cs`
- Test: `Assets/Game/Editor/Tests/VersusLobbyDataTests.cs`

This is the pure half of the lobby work: the keys, the encode/decode, and the readers that turn a `Lobby` into something a view can draw. No service calls — those are Task 10.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/VersusLobbyDataTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Services.Lobbies.Models;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How a versus lobby's team state is written down and read back.
    ///
    /// Every reader here is guarded on both the dictionary and the value, the way PlayerNames and
    /// SuitColors already are: a player object written by an older build, or one still mid-join,
    /// may not carry a key at all — and an unguarded indexer throws KeyNotFoundException on every
    /// poll, which kills the roster rather than degrading it.
    /// </summary>
    public class VersusLobbyDataTests
    {
        private const int Swatches = 14;

        private static Player PlayerWith(string id, int team, string teamColor)
        {
            var data = new Dictionary<string, PlayerDataObject>
            {
                { LobbySession.KeyPlayerName, Member("Pilot") },
                { LobbySession.KeyTeam, Member(team.ToString()) }
            };

            if (teamColor != null) data[LobbySession.KeyTeamColor] = Member(teamColor);

            return new Player(id: id, data: data);
        }

        private static PlayerDataObject Member(string value) =>
            new(PlayerDataObject.VisibilityOptions.Member, value);

        private static Lobby VersusLobby(int teamCount, int teamSize, List<Player> players) =>
            new(id: "L", name: "Match", players: players, data: new Dictionary<string, DataObject>
            {
                { LobbySession.KeyMode, new DataObject(DataObject.VisibilityOptions.Public, LobbySession.ModeVersus) },
                { LobbySession.KeyTeamCount, new DataObject(DataObject.VisibilityOptions.Member, teamCount.ToString()) },
                { LobbySession.KeyTeamSize, new DataObject(DataObject.VisibilityOptions.Member, teamSize.ToString()) }
            });

        // ────────────────────────────────────────────────────────────── the mode

        [Test]
        public void ALobbyWithNoModeKeyIsAStoryLobby()
        {
            var lobby = new Lobby(id: "L", name: "World", players: new List<Player>());

            Assert.IsFalse(LobbySession.IsVersus(lobby),
                "a lobby from a build before VS existed must not read as a match");
        }

        [Test]
        public void AVersusLobbyReadsAsOne()
        {
            Assert.IsTrue(LobbySession.IsVersus(VersusLobby(2, 2, new List<Player>())));
        }

        // ───────────────────────────────────────────────────────────── the rules

        [Test]
        public void TheRulesComeBackAsTheyWereWritten()
        {
            Lobby lobby = VersusLobby(3, 4, new List<Player>());

            Assert.AreEqual(3, LobbySession.TeamCountOf(lobby));
            Assert.AreEqual(4, LobbySession.TeamSizeOf(lobby));
        }

        [Test]
        public void UnreadableRulesFallBackToTheDefaults()
        {
            var lobby = new Lobby(id: "L", name: "Match", players: new List<Player>(),
                data: new Dictionary<string, DataObject>
                {
                    { LobbySession.KeyMode, new DataObject(DataObject.VisibilityOptions.Public, LobbySession.ModeVersus) },
                    { LobbySession.KeyTeamCount, new DataObject(DataObject.VisibilityOptions.Member, "not a number") }
                });

            Assert.AreEqual(VersusRules.DefaultTeams, LobbySession.TeamCountOf(lobby));
            Assert.AreEqual(VersusRules.DefaultTeamSize, LobbySession.TeamSizeOf(lobby));
        }

        // ──────────────────────────────────────────────────────────── the teams

        [Test]
        public void EveryPlayersTeamIsReadInLobbyOrder()
        {
            Lobby lobby = VersusLobby(2, 2, new List<Player>
            {
                PlayerWith("a", 0, null),
                PlayerWith("b", 1, null)
            });

            int[] teams = LobbySession.Teams(lobby);

            Assert.AreEqual(new[] { 0, 1 }, teams);
        }

        [Test]
        public void APlayerWithNoTeamKeyIsOnTeamZero()
        {
            var lobby = VersusLobby(2, 2, new List<Player>
            {
                new(id: "a", data: new Dictionary<string, PlayerDataObject>())
            });

            Assert.AreEqual(new[] { 0 }, LobbySession.Teams(lobby));
        }

        [Test]
        public void ATeamOutsideTheRulesIsFoldedBackIn()
        {
            Lobby lobby = VersusLobby(2, 2, new List<Player> { PlayerWith("a", 7, null) });

            int[] teams = LobbySession.Teams(lobby);

            Assert.GreaterOrEqual(teams[0], 0);
            Assert.Less(teams[0], 2, "a team index from a bigger build must land inside this one");
        }

        [Test]
        public void OccupancyCountsHeadsPerTeam()
        {
            Lobby lobby = VersusLobby(3, 2, new List<Player>
            {
                PlayerWith("a", 0, null),
                PlayerWith("b", 0, null),
                PlayerWith("c", 2, null)
            });

            Assert.AreEqual(new[] { 2, 0, 1 }, LobbySession.Occupancy(lobby));
        }

        // ─────────────────────────────────────────────────────── the team colour

        [Test]
        public void ATeamColourRoundTrips()
        {
            string encoded = LobbySession.EncodeTeamColor(9, stampMs: 1000);

            Assert.IsTrue(LobbySession.TryDecodeTeamColor(encoded, out int swatch, out long stamp));
            Assert.AreEqual(9, swatch);
            Assert.AreEqual(1000, stamp);
        }

        [Test]
        public void GarbageDecodesAsAFailureRatherThanThrowing()
        {
            Assert.IsFalse(LobbySession.TryDecodeTeamColor("nonsense", out _, out _));
            Assert.IsFalse(LobbySession.TryDecodeTeamColor(null, out _, out _));
        }

        /// <summary>
        /// The stamp is the whole point: any member may recolour their team, so the team's colour
        /// has to be the most recently written one rather than whichever member happens to sit
        /// first in the roster.
        /// </summary>
        [Test]
        public void TheLatestWriterDecidesTheTeamsColour()
        {
            Lobby lobby = VersusLobby(2, 2, new List<Player>
            {
                PlayerWith("a", 0, LobbySession.EncodeTeamColor(3, 100)),
                PlayerWith("b", 0, LobbySession.EncodeTeamColor(8, 900))
            });

            int[] colors = LobbySession.TeamColorsOf(lobby, Swatches);

            Assert.AreEqual(8, colors[0]);
        }

        [Test]
        public void ATeamNobodyHasRecolouredWearsItsDefault()
        {
            Lobby lobby = VersusLobby(2, 2, new List<Player> { PlayerWith("a", 0, null) });

            int[] colors = LobbySession.TeamColorsOf(lobby, Swatches);
            int[] defaults = TeamColorRules.DefaultColors(2, Swatches);

            Assert.AreEqual(defaults[1], colors[1]);
        }

        [Test]
        public void ThereIsAColourForEveryTeamEvenWithNobodyInTheLobby()
        {
            Lobby lobby = VersusLobby(5, 2, new List<Player>());

            Assert.AreEqual(5, LobbySession.TeamColorsOf(lobby, Swatches).Length);
        }

        // ────────────────────────────────────────────────────────── the snapshot

        [Test]
        public void TheSnapshotCarriesEverythingAViewNeeds()
        {
            Lobby lobby = VersusLobby(2, 2, new List<Player>
            {
                PlayerWith("a", 0, LobbySession.EncodeTeamColor(3, 100)),
                PlayerWith("b", 1, null)
            });

            RosterSnapshot snapshot = LobbySession.Snapshot(lobby, localSlot: 1, hostSlot: 0,
                                                            swatchCount: Swatches);

            Assert.IsTrue(snapshot.IsVersus);
            Assert.AreEqual(2, snapshot.Names.Length);
            Assert.AreEqual(2, snapshot.TeamCount);
            Assert.AreEqual(2, snapshot.TeamSize);
            Assert.AreEqual(1, snapshot.LocalSlot);
            Assert.AreEqual(0, snapshot.HostSlot);
            Assert.AreEqual(1, snapshot.LocalTeam);
            Assert.AreEqual(3, snapshot.TeamColors[0]);
        }

        [Test]
        public void AStoryLobbySnapshotStillHasSuitColours()
        {
            var lobby = new Lobby(id: "L", name: "World", players: new List<Player>
            {
                new(id: "a", data: new Dictionary<string, PlayerDataObject>
                {
                    { LobbySession.KeyPlayerName, Member("Pilot") },
                    { LobbySession.KeySuitColor, Member("5") }
                })
            });

            RosterSnapshot snapshot = LobbySession.Snapshot(lobby, 0, 0, Swatches);

            Assert.IsFalse(snapshot.IsVersus);
            Assert.AreEqual(5, snapshot.SuitColors[0]);
        }

        [Test]
        public void AnEmptySnapshotIsSafeToDraw()
        {
            RosterSnapshot snapshot = LobbySession.Snapshot(null, -1, -1, Swatches);

            Assert.IsNotNull(snapshot.Names);
            Assert.IsNotNull(snapshot.Teams);
            Assert.IsNotNull(snapshot.TeamColors);
            Assert.AreEqual(0, snapshot.Names.Length);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0117: 'LobbySession' does not contain a definition for 'KeyMode'`.

- [ ] **Step 3: Write VersusSetup**

`Assets/Game/Scripts/Core/Multiplayer/VersusSetup.cs`:

```csharp
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    /// <summary>
    /// The rules a lobby is created with, or <see cref="None"/> for a story lobby.
    ///
    /// A value rather than two loose ints so a call site cannot pass a team count and forget the
    /// size, and so "this is not a versus lobby" is one thing to check rather than a sentinel on
    /// each number.
    /// </summary>
    public readonly struct VersusSetup
    {
        /// <summary>A story lobby: no teams, and the story player cap.</summary>
        public static readonly VersusSetup None = default;

        public readonly bool IsVersus;
        public readonly int TeamCount;
        public readonly int TeamSize;

        public VersusSetup(int teamCount, int teamSize)
        {
            IsVersus = true;
            TeamCount = VersusRules.ClampTeams(teamCount, teamSize);
            TeamSize = VersusRules.ClampTeamSize(teamSize, TeamCount);
        }

        /// <summary>What the lobby advertises as its maximum. Not what Relay allocates — see LobbySession.</summary>
        public int Seats => VersusRules.Seats(TeamCount, TeamSize);
    }
}
```

- [ ] **Step 4: Write RosterSnapshot**

`Assets/Game/Scripts/Core/Multiplayer/RosterSnapshot.cs`:

```csharp
using System;

namespace SpaceGame.Core
{
    /// <summary>
    /// Everything the lobby's views draw, taken off a <c>Lobby</c> once per poll.
    ///
    /// <para>
    /// It exists because the alternative was a render call whose parameter list grew with every
    /// feature — names, then colours, then the local slot, then the host slot, then the local
    /// colour — and would now be gaining teams, team colours, occupancy and the rules. One value
    /// keeps the call readable and keeps the two views agreeing about what they were handed.
    /// </para>
    ///
    /// <para>
    /// Deliberately free of <c>Lobby</c> itself. Building it is <c>LobbySession</c>'s job, and
    /// everything downstream of that can be built, rendered and tested without a network, an
    /// authentication service, or Unity Gaming Services — which is the property the lobby views
    /// were written to have and the reason they can be exercised at all.
    /// </para>
    /// </summary>
    public readonly struct RosterSnapshot
    {
        /// <summary>Player names, in lobby order. Every other array is index-aligned with this one.</summary>
        public readonly string[] Names;

        /// <summary>Each player's own suit swatch. What a story lobby paints its rank with.</summary>
        public readonly int[] SuitColors;

        /// <summary>Each player's team, or all zeroes in a story lobby.</summary>
        public readonly int[] Teams;

        /// <summary>One swatch per team, resolved from the latest writer on each.</summary>
        public readonly int[] TeamColors;

        /// <summary>Heads per team, which is what the host's steppers are refused against.</summary>
        public readonly int[] Occupancy;

        public readonly int TeamCount;
        public readonly int TeamSize;

        /// <summary>Which row of the roster is us, or -1.</summary>
        public readonly int LocalSlot;

        /// <summary>Which row is the host, or -1. Marked in the rank with an underline.</summary>
        public readonly int HostSlot;

        public readonly bool IsVersus;

        public RosterSnapshot(string[] names, int[] suitColors, int[] teams, int[] teamColors,
            int[] occupancy, int teamCount, int teamSize, int localSlot, int hostSlot, bool isVersus)
        {
            Names = names ?? Array.Empty<string>();
            SuitColors = suitColors ?? Array.Empty<int>();
            Teams = teams ?? Array.Empty<int>();
            TeamColors = teamColors ?? Array.Empty<int>();
            Occupancy = occupancy ?? Array.Empty<int>();
            TeamCount = teamCount;
            TeamSize = teamSize;
            LocalSlot = localSlot;
            HostSlot = hostSlot;
            IsVersus = isVersus;
        }

        /// <summary>The team we stand on, or -1 when we are not in the lobby or it is not a match.</summary>
        public int LocalTeam =>
            IsVersus && LocalSlot >= 0 && LocalSlot < Teams.Length ? Teams[LocalSlot] : -1;

        /// <summary>The swatch a team wears, guarded so a team index off the end cannot throw.</summary>
        public int ColorOfTeam(int team) =>
            team >= 0 && team < TeamColors.Length ? TeamColors[team] : 0;

        /// <summary>How many stand on a team, guarded the same way.</summary>
        public int HeadsOn(int team) =>
            team >= 0 && team < Occupancy.Length ? Occupancy[team] : 0;

        /// <summary>Whether a player may still move onto this team.</summary>
        public bool HasRoomOn(int team) => HeadsOn(team) < TeamSize;
    }
}
```

- [ ] **Step 5: Add the keys, encoders and readers to LobbySessionOptions**

In `Assets/Game/Scripts/Core/Multiplayer/LobbySessionOptions.cs`, add `using SpaceGame.Gameplay;` to the usings, then add the following inside the `LobbySession` partial class, after the existing `StateInGame` constant:

```csharp
        /// <summary>
        /// Whether this lobby is a story world or a versus match.
        ///
        /// Public, unlike the rest of the team keys: the browser has to label rows the player has
        /// not joined, and a VS joiner's list must not offer them story lobbies that have no teams
        /// to stand in.
        /// </summary>
        public const string KeyMode = "Mode";

        public const string ModeStory = "story";
        public const string ModeVersus = "versus";

        /// <summary>The host's live rules. Member-visible; only meaningful once you are inside.</summary>
        public const string KeyTeamCount = "TeamCount";

        public const string KeyTeamSize = "TeamSize";

        /// <summary>Which team a player stands on. Player data, so each player writes only their own.</summary>
        public const string KeyTeam = "Team";

        /// <summary>
        /// The colour this player last set for their team, as <c>"swatch:stampMs"</c>.
        ///
        /// <para>
        /// On the PLAYER rather than on the lobby, and that is forced rather than preferred.
        /// <c>UpdateLobbyAsync</c> is host-only, so a shared table of team colours in lobby data
        /// could not be written by the member who actually pressed the arrow — the rule that any
        /// member recolours their own team would need a round trip through the host to survive at
        /// all. Player data has no such restriction.
        /// </para>
        ///
        /// <para>
        /// The cost is that a team has as many opinions about its colour as it has members, which
        /// the stamp resolves: the team wears the highest-stamped value among the players standing
        /// in it. Last writer wins, which is what a colour cycler means. The stamp is the writer's
        /// own clock, and clocks between friends disagree by seconds at worst — the only thing at
        /// stake in a disagreement is which of two swatches a team wears for one poll.
        /// </para>
        /// </summary>
        public const string KeyTeamColor = "TeamColor";

        private const char TeamColorSeparator = ':';

        // ─────────────────────────────────────────────────────── writing versus state

        /// <summary>The options that change a versus lobby's team rules. Host-only, like every UpdateLobby.</summary>
        public static UpdateLobbyOptions BuildTeamRulesOptions(int teamCount, int teamSize) => new()
        {
            MaxPlayers = VersusRules.Seats(teamCount, teamSize),
            Data = new Dictionary<string, DataObject>
            {
                { KeyTeamCount, new DataObject(DataObject.VisibilityOptions.Member, teamCount.ToString(CultureInfo.InvariantCulture)) },
                { KeyTeamSize, new DataObject(DataObject.VisibilityOptions.Member, teamSize.ToString(CultureInfo.InvariantCulture)) }
            }
        };

        /// <summary>
        /// The options that move this player to another team.
        ///
        /// Only the team is sent, for the reason <see cref="BuildSuitColorOptions"/> sends only the
        /// colour: including anything else would make every team switch also rewrite it, so a value
        /// changed on another screen could be reverted by pressing a team.
        /// </summary>
        public static UpdatePlayerOptions BuildTeamOptions(int team) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyTeam, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                                                team.ToString(CultureInfo.InvariantCulture)) }
            }
        };

        /// <summary>The options that record this player's opinion of their team's colour.</summary>
        public static UpdatePlayerOptions BuildTeamColorOptions(int swatch, long stampMs) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyTeamColor, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                                                     EncodeTeamColor(swatch, stampMs)) }
            }
        };

        public static string EncodeTeamColor(int swatch, long stampMs) =>
            swatch.ToString(CultureInfo.InvariantCulture) + TeamColorSeparator +
            stampMs.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads a stamped colour back, answering false rather than throwing on anything it does
        /// not recognise — a value written by an older build, or a half-written one caught mid-poll.
        /// </summary>
        public static bool TryDecodeTeamColor(string value, out int swatch, out long stampMs)
        {
            swatch = 0;
            stampMs = 0;

            if (string.IsNullOrEmpty(value)) return false;

            int split = value.IndexOf(TeamColorSeparator);
            if (split <= 0 || split >= value.Length - 1) return false;

            return int.TryParse(value[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out swatch)
                   && long.TryParse(value[(split + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out stampMs);
        }

        // ─────────────────────────────────────────────────────── reading versus state

        public static bool IsVersus(Lobby lobby) =>
            lobby?.Data != null
            && lobby.Data.TryGetValue(KeyMode, out DataObject mode)
            && mode.Value == ModeVersus;

        public static int TeamCountOf(Lobby lobby) =>
            VersusRules.ClampTeams(ReadInt(lobby, KeyTeamCount, VersusRules.DefaultTeams),
                                   VersusRules.MinTeamSize);

        public static int TeamSizeOf(Lobby lobby) =>
            VersusRules.ClampTeamSize(ReadInt(lobby, KeyTeamSize, VersusRules.DefaultTeamSize),
                                      VersusRules.MinTeams);

        /// <summary>
        /// Each player's team, in lobby order and index-aligned with <see cref="PlayerNames"/>.
        ///
        /// Guarded on every step, like the names and the suit colours are: a player still mid-join
        /// carries no team key, and an unguarded indexer throws on every poll rather than once.
        /// A team index this build has never heard of — from a peer whose host allowed more teams —
        /// is folded back inside the rules rather than dropping the player out of the rank.
        /// </summary>
        public static int[] Teams(Lobby lobby)
        {
            if (lobby?.Players == null) return System.Array.Empty<int>();

            int teamCount = TeamCountOf(lobby);
            var teams = new int[lobby.Players.Count];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                int team = p?.Data != null
                           && p.Data.TryGetValue(KeyTeam, out PlayerDataObject value)
                           && int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                           out int parsed)
                    ? parsed
                    : 0;

                teams[i] = team >= 0 && team < teamCount ? team : 0;
            }

            return teams;
        }

        /// <summary>Heads per team. What the host's steppers are refused against.</summary>
        public static int[] Occupancy(Lobby lobby)
        {
            int teamCount = TeamCountOf(lobby);
            var occupancy = new int[teamCount];

            foreach (int team in Teams(lobby))
                if (team >= 0 && team < teamCount)
                    occupancy[team]++;

            return occupancy;
        }

        /// <summary>
        /// One swatch per team: the highest-stamped opinion among that team's members, or the
        /// team's default when nobody has said anything.
        ///
        /// Ties go to the earlier player in lobby order, which is arbitrary and only has to be the
        /// SAME arbitrary answer on every peer — two machines disagreeing about a tie is two ranks
        /// painted differently.
        /// </summary>
        public static int[] TeamColorsOf(Lobby lobby, int swatchCount)
        {
            int teamCount = TeamCountOf(lobby);
            int[] colors = TeamColorRules.DefaultColors(teamCount, swatchCount);

            if (lobby?.Players == null) return colors;

            var best = new long[teamCount];
            var claimed = new bool[teamCount];
            int[] teams = Teams(lobby);

            for (int i = 0; i < lobby.Players.Count && i < teams.Length; i++)
            {
                Player p = lobby.Players[i];
                int team = teams[i];

                if (team < 0 || team >= teamCount) continue;
                if (p?.Data == null || !p.Data.TryGetValue(KeyTeamColor, out PlayerDataObject value)) continue;
                if (!TryDecodeTeamColor(value.Value, out int swatch, out long stamp)) continue;
                if (claimed[team] && stamp <= best[team]) continue;

                colors[team] = swatchCount > 0 ? ((swatch % swatchCount) + swatchCount) % swatchCount : 0;
                best[team] = stamp;
                claimed[team] = true;
            }

            return colors;
        }

        /// <summary>Everything a lobby view draws, taken off the lobby once per poll.</summary>
        public static RosterSnapshot Snapshot(Lobby lobby, int localSlot, int hostSlot, int swatchCount)
        {
            if (lobby == null)
                return new RosterSnapshot(null, null, null, null, null,
                                          VersusRules.DefaultTeams, VersusRules.DefaultTeamSize,
                                          localSlot, hostSlot, false);

            bool versus = IsVersus(lobby);

            return new RosterSnapshot(
                PlayerNames(lobby),
                SuitColors(lobby),
                versus ? Teams(lobby) : System.Array.Empty<int>(),
                versus ? TeamColorsOf(lobby, swatchCount) : System.Array.Empty<int>(),
                versus ? Occupancy(lobby) : System.Array.Empty<int>(),
                TeamCountOf(lobby),
                TeamSizeOf(lobby),
                localSlot,
                hostSlot,
                versus);
        }

        private static int ReadInt(Lobby lobby, string key, int fallback) =>
            lobby?.Data != null
            && lobby.Data.TryGetValue(key, out DataObject value)
            && int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
```

Then extend `BuildCreateOptions` so a versus lobby is stamped with its mode and rules at creation. Replace the existing method with:

```csharp
        /// <summary>
        /// The options a lobby is created with.
        ///
        /// The relay code goes in here rather than into a follow-up UpdateLobbyAsync: a client
        /// polling in the gap between the two saw a lobby with no join code and read straight past
        /// the missing key. The mode and the team rules ride along for the same reason — a joiner
        /// who lands between create and a follow-up update sees a match with no teams in it.
        /// </summary>
        public static CreateLobbyOptions BuildCreateOptions(bool isPrivate, string relayJoinCode,
            string playerName, int suitColor, in VersusSetup versus)
        {
            var data = new Dictionary<string, DataObject>
            {
                { KeyRelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) },

                // Public, not Member: the browser labels rows the player has not joined.
                { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateWaiting) },
                { KeyMode, new DataObject(DataObject.VisibilityOptions.Public,
                                          versus.IsVersus ? ModeVersus : ModeStory) }
            };

            if (versus.IsVersus)
            {
                data[KeyTeamCount] = new DataObject(DataObject.VisibilityOptions.Member,
                                                    versus.TeamCount.ToString(CultureInfo.InvariantCulture));
                data[KeyTeamSize] = new DataObject(DataObject.VisibilityOptions.Member,
                                                   versus.TeamSize.ToString(CultureInfo.InvariantCulture));
            }

            return new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = BuildPlayer(playerName, suitColor),
                Data = data
            };
        }
```

- [ ] **Step 6: Type-check**

Run the Roslyn pass. Expected: one error at `LobbySession.CreateAsync`, which still calls the four-argument `BuildCreateOptions` — Task 10 fixes that call site. Everything else must be clean.

- [ ] **Step 7: Commit**

Stage the two new files, `LobbySessionOptions.cs` and `Assets/Game/Editor/Tests/VersusLobbyDataTests.cs`, then commit with the message `feat: versus lobby keys, team colour stamps, roster snapshot`.

---

## Task 10: One debounced publisher, and the versus session calls

**Files:**
- Create: `Assets/Game/Scripts/Core/Multiplayer/DebouncedPublish.cs`
- Modify: `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`
- Test: `Assets/Game/Editor/Tests/DebouncedPublishTests.cs`

The suit-colour flush is about to be needed three times over — suit colour, team, team colour — so it comes out once rather than being copied twice more.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/DebouncedPublishTests.cs`:

```csharp
using System.Threading.Tasks;
using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The coalescing that keeps a burst of arrow presses from becoming a burst of service calls.
    ///
    /// Lobby rate-limits UpdatePlayer to five calls per five seconds per player, and stepping
    /// through a palette to see it is a dozen presses in about two seconds. Without this, the
    /// colour a player settles on is the one request that gets refused — leaving everyone else
    /// looking at whatever they happened to be on when the budget ran out.
    /// </summary>
    public class DebouncedPublishTests
    {
        private static Task Nothing(int value) => Task.CompletedTask;

        [Test]
        public void NothingIsSentBeforeTheDelayHasPassed()
        {
            int sent = -1;
            var publisher = new DebouncedPublish<int>(seconds: 1f);

            publisher.Request(4);
            publisher.Tick(0.5f, value => { sent = value; return Task.CompletedTask; });

            Assert.AreEqual(-1, sent);
        }

        [Test]
        public void TheValueGoesOutOnceThePressesStop()
        {
            int sent = -1;
            var publisher = new DebouncedPublish<int>(1f);

            publisher.Request(4);
            publisher.Tick(1.1f, value => { sent = value; return Task.CompletedTask; });

            Assert.AreEqual(4, sent);
        }

        [Test]
        public void OnlyTheLastOfABurstIsSent()
        {
            int sends = 0;
            int sent = -1;
            var publisher = new DebouncedPublish<int>(1f);

            publisher.Request(1);
            publisher.Tick(0.4f, Nothing);
            publisher.Request(2);
            publisher.Tick(0.4f, Nothing);
            publisher.Request(9);
            publisher.Tick(1.1f, value => { sends++; sent = value; return Task.CompletedTask; });

            Assert.AreEqual(1, sends);
            Assert.AreEqual(9, sent);
        }

        [Test]
        public void ASecondTickWithNothingPendingSendsNothing()
        {
            int sends = 0;
            var publisher = new DebouncedPublish<int>(1f);

            publisher.Request(4);
            publisher.Tick(1.1f, _ => { sends++; return Task.CompletedTask; });
            publisher.Tick(1.1f, _ => { sends++; return Task.CompletedTask; });

            Assert.AreEqual(1, sends);
        }

        /// <summary>
        /// Dropped rather than held: a value left pending would fire at whatever lobby this peer
        /// joins next, which is somebody else's session.
        /// </summary>
        [Test]
        public void CancelForgetsWhatWasWaiting()
        {
            int sends = 0;
            var publisher = new DebouncedPublish<int>(1f);

            publisher.Request(4);
            publisher.Cancel();
            publisher.Tick(1.1f, _ => { sends++; return Task.CompletedTask; });

            Assert.AreEqual(0, sends);
        }

        [Test]
        public void ThePendingValueIsReadableBeforeItIsSent()
        {
            var publisher = new DebouncedPublish<int>(1f);

            Assert.IsFalse(publisher.TryPeek(out _));

            publisher.Request(7);

            Assert.IsTrue(publisher.TryPeek(out int pending));
            Assert.AreEqual(7, pending);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0246: The type or namespace name 'DebouncedPublish' could not be found`.

- [ ] **Step 3: Write DebouncedPublish**

`Assets/Game/Scripts/Core/Multiplayer/DebouncedPublish.cs`:

```csharp
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Holds a value back until the player has stopped changing it, then sends the last one.
    ///
    /// <para>
    /// Lobby rate-limits UpdatePlayer to five calls per five seconds per player, and every control
    /// in the lobby that writes player data is a control you press repeatedly: stepping a palette
    /// to see it is a dozen presses in a couple of seconds, and picking a team is often two or
    /// three before you settle. Without coalescing, the value the player settles on is the one
    /// request the limiter refuses — which leaves everyone else looking at the choice they
    /// abandoned.
    /// </para>
    ///
    /// <para>
    /// Written once here because the lobby now needs it three times over — suit colour, team, and
    /// team colour. It was the suit cycler's private flush first; a second and third copy of a
    /// timer, a pending field and an in-flight guard is how three controls end up with three
    /// slightly different ideas of what "still typing" means.
    /// </para>
    ///
    /// <para>
    /// Nothing here paints anything. The local view is updated the instant the control is pressed
    /// and this only tells everyone else, which is the split that lets a cycler feel immediate
    /// while the service call is allowed to be slow.
    /// </para>
    /// </summary>
    public class DebouncedPublish<T>
    {
        private readonly float seconds;

        private T pending;
        private bool hasPending;
        private bool inFlight;
        private float timer;

        /// <summary>
        /// <paramref name="seconds"/> is how long the player has to stop for. Long enough to
        /// swallow a burst, short enough that the result still feels like a response.
        /// </summary>
        public DebouncedPublish(float seconds)
        {
            this.seconds = seconds;
        }

        /// <summary>Replaces whatever was waiting and restarts the clock.</summary>
        public void Request(T value)
        {
            pending = value;
            hasPending = true;
            timer = seconds;
        }

        /// <summary>Forgets what was waiting — for leaving a lobby, where it would fire at the next one.</summary>
        public void Cancel()
        {
            hasPending = false;
            pending = default;
        }

        /// <summary>What is waiting to go out, which the caller may need to render optimistically.</summary>
        public bool TryPeek(out T value)
        {
            value = pending;
            return hasPending;
        }

        /// <summary>
        /// Counts down and, when the clock runs out, sends the last value through
        /// <paramref name="send"/>.
        ///
        /// The pending value is cleared before the send starts, so a press that lands while the
        /// request is in flight is a new pending value rather than being swallowed by the one
        /// already on its way.
        /// </summary>
        public void Tick(float deltaTime, Func<T, Task> send)
        {
            if (!hasPending || inFlight || send == null) return;

            timer -= deltaTime;
            if (timer > 0f) return;

            T sending = pending;
            hasPending = false;
            inFlight = true;

            Send(sending, send);
        }

        /// <summary>
        /// Failures are logged, not raised. The local view and the stored preference are already
        /// correct, so the only casualty is that other people see the previous value until the next
        /// press — and a warning pinned over the lobby for that is worse than the problem.
        /// </summary>
        private async void Send(T value, Func<T, Task> send)
        {
            try
            {
                await send(value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DebouncedPublish] Could not publish '{value}': {e.Message}");
            }
            finally
            {
                inFlight = false;
            }
        }
    }
}
```

- [ ] **Step 4: Rewire LobbySession onto it, and add the versus calls**

In `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`:

**4a.** Add `using SpaceGame.Gameplay;` to the usings.

**4b.** Replace the four suit-colour fields —

```csharp
        /// <summary>Long enough to swallow a burst of arrow presses, short enough to feel immediate.</summary>
        private const float SuitColorDebounce = 0.75f;

        /// <summary>The colour waiting to be published, or -1 when there is nothing pending.</summary>
        private int pendingSuitColor = -1;

        private float suitColorTimer;
        private bool suitColorInFlight;
```

— with the three publishers:

```csharp
        /// <summary>Long enough to swallow a burst of presses, short enough to feel immediate.</summary>
        private const float PublishDebounce = 0.75f;

        private readonly DebouncedPublish<int> suitColorPublisher = new(PublishDebounce);
        private readonly DebouncedPublish<int> teamPublisher = new(PublishDebounce);
        private readonly DebouncedPublish<int> teamColorPublisher = new(PublishDebounce);
```

**4c.** Replace `PublishSuitColor` and the whole `FlushSuitColor` method with:

```csharp
        /// <summary>
        /// Publishes the local player's suit colour to the lobby, coalescing bursts.
        ///
        /// Nothing here paints anything — the local figure is repainted by the screen the instant
        /// the arrow is pressed, and this only tells everyone else. That split is what lets the
        /// cycler feel immediate while the service call is allowed to be slow.
        /// </summary>
        public void PublishSuitColor(int suitColor) =>
            suitColorPublisher.Request(SuitPalette.Clamp(suitColor));

        /// <summary>Moves the local player to another team, coalescing a burst of changes of mind.</summary>
        public void PublishTeam(int team) => teamPublisher.Request(team);

        /// <summary>
        /// Records this player's opinion of their own team's colour.
        ///
        /// Stamped on the way out: any member of a team may recolour it, so the team wears the most
        /// recently written value rather than whichever member sits first in the roster. See
        /// <see cref="KeyTeamColor"/> for why this cannot live in lobby data.
        /// </summary>
        public void PublishTeamColor(int swatch) =>
            teamColorPublisher.Request(SuitPalette.Clamp(swatch));

        /// <summary>
        /// Drains all three publishers. Called once per frame from <c>Update</c>.
        ///
        /// Each is dropped rather than held when there is nothing to publish to, or it would fire
        /// at whatever lobby this peer joins next.
        /// </summary>
        private void FlushPublishers()
        {
            if (Current == null || !AuthenticationService.Instance.IsSignedIn)
            {
                suitColorPublisher.Cancel();
                teamPublisher.Cancel();
                teamColorPublisher.Cancel();
                return;
            }

            float delta = Time.deltaTime;

            suitColorPublisher.Tick(delta, async swatch =>
            {
                Current = await LobbyService.Instance.UpdatePlayerAsync(
                    Current.Id, AuthenticationService.Instance.PlayerId, BuildSuitColorOptions(swatch));
                Changed?.Invoke();
            });

            teamPublisher.Tick(delta, async team =>
            {
                Current = await LobbyService.Instance.UpdatePlayerAsync(
                    Current.Id, AuthenticationService.Instance.PlayerId, BuildTeamOptions(team));
                Changed?.Invoke();
            });

            teamColorPublisher.Tick(delta, async swatch =>
            {
                Current = await LobbyService.Instance.UpdatePlayerAsync(
                    Current.Id, AuthenticationService.Instance.PlayerId,
                    BuildTeamColorOptions(swatch, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                Changed?.Invoke();
            });
        }
```

**4d.** In `Update`, replace the `FlushSuitColor();` call with `FlushPublishers();`.

**4e.** Replace `CreateAsync` so a versus lobby allocates for the ceiling and advertises its rules:

```csharp
        /// <summary>
        /// Allocates a Relay server, then advertises it as a lobby.
        ///
        /// Relay first. If it fails there is no lobby to clean up — the reverse order created the
        /// lobby, then allocated Relay, and on an allocation failure left an orphan lobby
        /// advertised to everyone with a join code that led nowhere.
        ///
        /// <para>
        /// A versus host allocates <see cref="VersusRules.MaxSeats"/> connections however small the
        /// match is, and advertises only as many seats as the rules ask for. Relay's allocation is
        /// sized once and cannot grow, so a host who allocated for their current rules and then
        /// added a team would be advertising seats nobody could connect to — and live retuning is
        /// the whole point of the lobby's host steppers.
        /// </para>
        /// </summary>
        public async Task<bool> CreateAsync(string lobbyName, bool isPrivate, VersusSetup versus)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                int relaySeats = versus.IsVersus ? VersusRules.MaxSeats : MaxPlayers;
                int advertised = versus.IsVersus ? versus.Seats : MaxPlayers;

                SessionResult host = await SessionLauncher.HostRelayAsync(relaySeats);
                if (!host.Success) { Failed?.Invoke(host.Error); return false; }

                string name = string.IsNullOrWhiteSpace(lobbyName) ? $"{PlayerName}'s game" : lobbyName;

                Current = await LobbyService.Instance.CreateLobbyAsync(name, advertised,
                    BuildCreateOptions(isPrivate, host.JoinCode, PlayerName, SuitColor, versus));

                State = LobbyState.InLobby;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Hosting '{Current.Name}' code={Current.LobbyCode} " +
                          $"relay={host.JoinCode} seats={advertised}/{relaySeats}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not create the lobby."));
                SessionLauncher.Shutdown();
                return false;
            }
            finally { busy = false; }
        }
```

**4f.** Add the host-only rules update, beside `SetPrivacyAsync`:

```csharp
        /// <summary>
        /// Retunes a live match's teams.
        ///
        /// Host-only because <c>UpdateLobbyAsync</c> is host-only, and refused outright when the
        /// change would displace somebody: a player moved out of the team they chose, by someone
        /// else, with no warning, is worse than a host being told no. The refusal comes back as a
        /// sentence fit for the lobby's status line.
        ///
        /// Not routed through <see cref="TryBegin"/>, for the reason <see cref="SetPrivacyAsync"/>
        /// is not: that guard exists to stop a double-click allocating two Relay servers, and this
        /// allocates nothing.
        /// </summary>
        public async Task<bool> SetTeamRulesAsync(int teamCount, int teamSize)
        {
            try
            {
                if (Current == null) { Failed?.Invoke("You are not in a lobby."); return false; }
                if (!IsHost) { Failed?.Invoke("Only the host can change this."); return false; }

                int[] occupancy = Occupancy(Current);

                if (!VersusRules.CanSetTeamCount(teamCount, occupancy, out string tooFewTeams))
                {
                    Failed?.Invoke(tooFewTeams);
                    return false;
                }

                if (!VersusRules.CanSetTeamSize(teamSize, occupancy, out string tooSmall))
                {
                    Failed?.Invoke(tooSmall);
                    return false;
                }

                Current = await LobbyService.Instance.UpdateLobbyAsync(Current.Id,
                    BuildTeamRulesOptions(teamCount, teamSize));

                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not change the match rules."));

                // The screen renders from Current, so a failed update has to be announced or the
                // steppers keep showing what the host asked for rather than what is in force.
                Changed?.Invoke();
                return false;
            }
        }

        /// <summary>Everything the lobby views draw, taken off the current lobby.</summary>
        public RosterSnapshot CurrentSnapshot() =>
            Snapshot(Current, LocalSlot, HostSlot(Current), SuitPalette.Count);
```

- [ ] **Step 5: Fix the remaining call site**

`LobbyUI.StartHosting` calls `session.CreateAsync(WorldSession.DisplayName, false)`. Change it to `session.CreateAsync(WorldSession.DisplayName, false, VersusSetup.None)` for now; Task 11 replaces the method around it.

- [ ] **Step 6: Type-check**

Run the Roslyn pass. Expected: no errors.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 6 `DebouncedPublishTests` and all 16 `VersusLobbyDataTests` pass, and `LobbySessionTests` is unchanged.

- [ ] **Step 8: Commit**

Stage `DebouncedPublish.cs`, `LobbySession.cs`, `LobbyUI.cs` and `Assets/Game/Editor/Tests/DebouncedPublishTests.cs`, then commit with the message `feat: one debounced publisher, versus lobby creation and retuning`.

---

## Task 11: An explicit lobby route, and a mode-filtered browser

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/LobbyUI.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs`
- Test: `Assets/Game/Editor/Tests/LobbyRouteTests.cs`

`IsHosting => WorldSession.IsActive` stops being true the moment a mode exists that stages no world. The route becomes an argument instead.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/LobbyRouteTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Which of the four ways into the lobby a screen was opened by.
    ///
    /// This used to be inferred from WorldSession.IsActive — a staged world WAS the difference
    /// between hosting and joining, because every host had picked one. A VS host stages nothing, so
    /// the inference now reports every versus host as a joiner; the route is passed in instead.
    /// </summary>
    public class LobbyRouteTests
    {
        [TestCase(LobbyRoute.StoryHost, true)]
        [TestCase(LobbyRoute.VersusHost, true)]
        [TestCase(LobbyRoute.StoryJoin, false)]
        [TestCase(LobbyRoute.VersusJoin, false)]
        public void HostingIsReadFromTheRoute(LobbyRoute route, bool hosting)
        {
            Assert.AreEqual(hosting, route.IsHosting());
        }

        [TestCase(LobbyRoute.VersusHost, true)]
        [TestCase(LobbyRoute.VersusJoin, true)]
        [TestCase(LobbyRoute.StoryHost, false)]
        [TestCase(LobbyRoute.StoryJoin, false)]
        public void VersusIsReadFromTheRoute(LobbyRoute route, bool versus)
        {
            Assert.AreEqual(versus, route.IsVersus());
        }

        /// <summary>
        /// A VS joiner offered a story lobby lands in a session with no teams to stand in, and a
        /// story joiner offered a match lands in one where their world is nobody's.
        /// </summary>
        [TestCase(LobbyRoute.VersusJoin, true, true)]
        [TestCase(LobbyRoute.VersusJoin, false, false)]
        [TestCase(LobbyRoute.StoryJoin, false, true)]
        [TestCase(LobbyRoute.StoryJoin, true, false)]
        public void TheBrowserOnlyListsSessionsOfItsOwnMode(LobbyRoute route, bool lobbyIsVersus,
            bool listed)
        {
            Assert.AreEqual(listed, route.Accepts(lobbyIsVersus));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `CS0246: The type or namespace name 'LobbyRoute' could not be found`.

- [ ] **Step 3: Add the route**

At the top of `Assets/Game/Scripts/Presentation/UI/Pages/LobbyUI.cs`, inside `namespace SpaceGame.Presentation` and above the `LobbyUI` class:

```csharp
    /// <summary>
    /// Which of the four ways into the lobby a screen was opened by.
    ///
    /// <para>
    /// This replaces reading <c>WorldSession.IsActive</c>. A staged world genuinely WAS the
    /// difference between hosting and joining for as long as every host had picked one — the doc on
    /// that field argued, correctly at the time, that a flag beside it could only ever disagree.
    /// Versus breaks the premise rather than the argument: a VS host stages no world at all, so the
    /// inference reports them as a joiner and sends them to the browser to look for their own
    /// session.
    /// </para>
    /// </summary>
    public enum LobbyRoute
    {
        /// <summary>A story host, who has already chosen the world the session is named after.</summary>
        StoryHost,

        StoryJoin,

        /// <summary>A versus host, who has already settled the rules the lobby is sized by.</summary>
        VersusHost,

        VersusJoin
    }

    public static class LobbyRouteExtensions
    {
        /// <summary>True when this screen is here to run a session rather than find one.</summary>
        public static bool IsHosting(this LobbyRoute route) =>
            route == LobbyRoute.StoryHost || route == LobbyRoute.VersusHost;

        public static bool IsVersus(this LobbyRoute route) =>
            route == LobbyRoute.VersusHost || route == LobbyRoute.VersusJoin;

        /// <summary>
        /// Whether a session found in the browser is one this route should offer.
        ///
        /// The two modes are not interchangeable destinations: a VS joiner dropped into a story
        /// lobby has no team to stand on, and a story joiner dropped into a match arrives in a world
        /// that is nobody's and that nobody will save.
        /// </summary>
        public static bool Accepts(this LobbyRoute route, bool lobbyIsVersus) =>
            route.IsVersus() == lobbyIsVersus;
    }
```

- [ ] **Step 4: Take the route as an argument**

In `LobbyUI`:

**4a.** Add the field and replace `Open` and `IsHosting`:

```csharp
        /// <summary>How this screen was opened. Replaces inferring the answer from a staged world.</summary>
        private LobbyRoute route;

        public static LobbyUI Open(MainMenuUI owner, LobbyRoute route)
        {
            var existing = FindFirstObjectByType<LobbyUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(LobbyUI)).AddComponent<LobbyUI>();
            ui.menu = owner;
            ui.route = route;
            ui.Present();
            return ui;
        }

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        /// <summary>True when this screen is here to run a session rather than find one.</summary>
        private bool IsHosting => route.IsHosting();
```

Note this changes `IsHosting` from `static` to an instance property; the compiler will point at every use.

**4b.** In `StartHosting`, name a versus session after the host rather than after a world it does not have, and size it from the staged rules:

```csharp
        private async void StartHosting()
        {
            ShowRoster();
            roster.SetBusy(true, "Creating session");

            if (!await session.EnsureReadyAsync()) { EndHosting(); return; }

            // A story session is named after the world — what the host chose one screen ago and what
            // everyone in the browser is being invited into. A versus session has no world, so it is
            // named after the host, which is the other thing a joiner recognises.
            //
            // Public to start with either way. A host who wanted it hidden can say so on the roster,
            // and creating it listed is the choice that matches pressing "Host a game".
            bool versus = route.IsVersus();

            string name = versus ? null : WorldSession.DisplayName;
            VersusSetup setup = versus
                ? new VersusSetup(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize)
                : VersusSetup.None;

            await session.CreateAsync(name, false, setup);

            EndHosting();
        }
```

`CreateAsync` already falls back to `"{PlayerName}'s game"` on a null or blank name, which is exactly the versus naming rule — no branch needed there.

**4c.** In `Leave`, a versus host clears the versus staging as well:

```csharp
            // A host's staged world must not follow them back to the menu, or the next thing they
            // do — joining someone else — starts with a save of their own waiting to be restored.
            // The same goes for the staged rules: the rules page reseeds on entry, but a host who
            // leaves and joins somebody else must not carry a match of their own into it.
            WorldSession.Clear();
            VersusRulesUI.ResetToDefaults();
            Close();
```

**4d.** In `ApplyLobbies`, drop sessions of the other mode before any row is built or reconciled. Immediately after the method's existing null guard on the incoming list, add:

```csharp
            // Filtered here rather than in the query: Lobby's filters cannot express "this custom
            // key equals this value" on a Public DataObject reliably across SDK versions, and a
            // wrong filter silently returns nothing at all — which is indistinguishable from
            // "nobody is hosting" on a screen whose whole job is to say otherwise.
            lobbies = lobbies.FindAll(lobby => route.Accepts(LobbySession.IsVersus(lobby)));
```

**4e.** In `ShowRoster`, hand the roster the route so it knows whether to draw team controls:

```csharp
        private void ShowRoster()
        {
            RectTransform root = NewPage(Page.Roster, null);

            roster = new LobbyRosterView(root, EntryPrefab,
                new LobbyRosterView.Actions(StartGame, Leave, CopyCode, SetPrivacy, StepTeamColor,
                                            JoinTeam, SetTeamRules));

            roster.Render(session.CurrentSnapshot(), session.IsHost,
                          IsHosting && !route.IsVersus() ? WorldSession.DisplayName : null);
        }
```

**4f.** Replace `StepSuitColor` with the versus-aware version, and add the two new actions:

```csharp
        /// <summary>
        /// Steps a colour by one swatch — the local player's suit in a story lobby, their whole
        /// team's in a match.
        ///
        /// Three things happen, in this order and for three different reasons. The choice is stored
        /// first, because in a story lobby it is the player's outfit and has to survive them backing
        /// out without starting anything. What is on screen is repainted second, synchronously,
        /// because a cycler that waits on a service call before showing anything feels broken.
        /// Everyone else is told last, through a debounced publish, because Lobby rate-limits player
        /// updates and browsing the whole palette is a dozen presses in a couple of seconds.
        /// </summary>
        private void StepTeamColor(int direction)
        {
            RosterSnapshot snapshot = session.CurrentSnapshot();

            if (!snapshot.IsVersus)
            {
                int nextSuit = SuitPalette.Step(GameSettings.SuitColorIndex, direction);

                GameSettings.SuitColorIndex = nextSuit;
                GameSettings.Save();

                roster?.SetLocalColor(nextSuit);
                session.PublishSuitColor(nextSuit);
                return;
            }

            int team = snapshot.LocalTeam;
            if (team < 0) return;

            // Every other team's swatch, so the step lands somewhere this rank can still be read.
            var taken = new List<int>();
            for (int other = 0; other < snapshot.TeamCount; other++)
                if (other != team)
                    taken.Add(snapshot.ColorOfTeam(other));

            int next = TeamColorRules.Step(snapshot.ColorOfTeam(team), direction,
                                           SuitPalette.Count, taken.ToArray());

            roster?.SetLocalColor(next);
            session.PublishTeamColor(next);
        }

        /// <summary>
        /// Moves the local player onto a team, or says why not.
        ///
        /// Refused rather than queued when the team is full: the press has to be answered, and
        /// answering it with a publish that the next poll silently undoes is worse than a sentence.
        /// </summary>
        private void JoinTeam(int team)
        {
            RosterSnapshot snapshot = session.CurrentSnapshot();

            if (!snapshot.IsVersus || team < 0 || team >= snapshot.TeamCount) return;
            if (team == snapshot.LocalTeam) return;

            if (!snapshot.HasRoomOn(team))
            {
                roster?.SetWarning($"{VersusRules.TeamName(team)} is full.");
                return;
            }

            roster?.SetStatus(string.Empty);
            session.PublishTeam(team);
        }

        /// <summary>The host's steppers. The refusal, when there is one, comes back through Failed.</summary>
        private async void SetTeamRules(int teamCount, int teamSize) =>
            await session.SetTeamRulesAsync(teamCount, teamSize);
```

Add `using SpaceGame.Gameplay;` and `using SpaceGame.Characters;` to `LobbyUI.cs` if they are not already there (`SuitPalette` is in `SpaceGame.Characters`; `TeamColorRules` and `VersusRules` are in `SpaceGame.Gameplay`).

**4g.** In `Render`, hand the roster a snapshot instead of five arguments:

```csharp
            roster.Render(session.CurrentSnapshot(), session.IsHost,
                          IsHosting && !route.IsVersus() ? WorldSession.DisplayName : null);
```

- [ ] **Step 5: Update the two call sites in MainMenuUI**

```csharp
    public void JoinMultiplayer()
    {
        WorldSession.Clear();
        LobbyUI.Open(this, LobbyRoute.StoryJoin);
    }

    public void EnterLobby() => LobbyUI.Open(this, LobbyRoute.StoryHost);

    public void JoinVersus()
    {
        WorldSession.Clear();
        VersusSession.Clear();
        LobbyUI.Open(this, LobbyRoute.VersusJoin);
    }

    public void EnterVersusLobby()
    {
        WorldSession.Clear();
        LobbyUI.Open(this, LobbyRoute.VersusHost);
    }
```

- [ ] **Step 6: Type-check**

Run the Roslyn pass. Expected: errors only inside `LobbyRosterView`, whose `Render` and `Actions` change in Task 12. Fix anything else now.

- [ ] **Step 7: Commit**

Stage `LobbyUI.cs`, `MainMenuUI.cs` and `Assets/Game/Editor/Tests/LobbyRouteTests.cs`, then commit with the message `feat: explicit lobby route, mode-filtered browser`.

---

## Task 12: The roster view renders from a snapshot, and grows a host strip

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyRosterView.cs`
- Test: `Assets/Game/Editor/Tests/LobbyRosterViewTests.cs`

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/LobbyRosterViewTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What the in-lobby page shows, and to whom.
    ///
    /// Rendered for real from a RosterSnapshot, which is exactly the property the snapshot was
    /// introduced for: no network, no authentication service, no Unity Gaming Services.
    /// </summary>
    public class LobbyRosterViewTests
    {
        private RectTransform page;
        private LobbyRosterView view;

        private static LobbyRosterView.Actions NoActions() =>
            new(() => { }, () => { }, () => { }, _ => { }, _ => { }, _ => { }, (_, _) => { });

        private static RosterSnapshot Versus(int teams, int size, int localSlot, params int[] playerTeams)
        {
            var names = new string[playerTeams.Length];
            for (int i = 0; i < names.Length; i++) names[i] = $"P{i}";

            var occupancy = new int[teams];
            foreach (int team in playerTeams)
                if (team >= 0 && team < teams)
                    occupancy[team]++;

            return new RosterSnapshot(names, new int[names.Length], playerTeams,
                                      TeamColorRules.DefaultColors(teams, 14), occupancy,
                                      teams, size, localSlot, 0, isVersus: true);
        }

        [SetUp]
        public void Build()
        {
            var host = new GameObject("Page", typeof(RectTransform));
            page = (RectTransform)host.transform;
            view = new LobbyRosterView(page, null, NoActions());
        }

        [TearDown]
        public void Clean()
        {
            view?.Dispose();
            if (page != null) Object.DestroyImmediate(page.gameObject);
        }

        [Test]
        public void AStoryLobbyHasNoTeamControls()
        {
            var snapshot = new RosterSnapshot(new[] { "Pilot" }, new[] { 3 }, null, null, null,
                                              2, 2, 0, 0, isVersus: false);

            view.Render(snapshot, isHost: true, hostTitle: "DUNE");

            Assert.IsFalse(view.TeamRulesShown, "story lobbies have no teams to tune");
        }

        [Test]
        public void AVersusHostSeesTheTeamSteppers()
        {
            view.Render(Versus(2, 2, 0, 0, 1), isHost: true, hostTitle: null);

            Assert.IsTrue(view.TeamRulesShown);
            Assert.IsTrue(view.TeamsStepper.Increase.interactable);
        }

        /// <summary>
        /// A joiner reads the host's numbers and cannot press them. Shown rather than hidden: the
        /// rules are what the match is, and a client who cannot see them cannot tell whether their
        /// team is full.
        /// </summary>
        [Test]
        public void AVersusJoinerSeesTheNumbersButCannotPressThem()
        {
            view.Render(Versus(3, 2, 1, 0, 1), isHost: false, hostTitle: null);

            Assert.IsTrue(view.TeamRulesShown);
            Assert.IsFalse(view.TeamsStepper.Increase.interactable);
            Assert.IsFalse(view.TeamSizeStepper.Decrease.interactable);
        }

        [Test]
        public void TheSteppersShowTheRulesInForce()
        {
            view.Render(Versus(3, 4, 0, 0), isHost: true, hostTitle: null);

            Assert.AreEqual("3", view.TeamsStepper.ValueLabel.text);
            Assert.AreEqual("4", view.TeamSizeStepper.ValueLabel.text);
        }

        [Test]
        public void OnlyTheHostIsOfferedStart()
        {
            view.Render(Versus(2, 2, 1, 0, 1), isHost: false, hostTitle: null);
            Assert.IsFalse(view.StartShown);

            view.Render(Versus(2, 2, 0, 0, 1), isHost: true, hostTitle: null);
            Assert.IsTrue(view.StartShown);
        }

        [Test]
        public void AWarningSurvivesTheNextPoll()
        {
            view.Render(Versus(2, 1, 0, 0, 1), isHost: true, hostTitle: null);

            view.SetWarning("TEAM TWO is full.");
            view.Render(Versus(2, 1, 0, 0, 1), isHost: true, hostTitle: null);

            Assert.AreEqual("TEAM TWO is full.", view.StatusText);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure — the seven-argument `Actions` constructor and `Render(RosterSnapshot, …)` do not exist yet.

- [ ] **Step 3: Widen Actions**

In `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyRosterView.cs`, replace the `Actions` struct with:

```csharp
        /// <summary>What can be done from this page. Supplied by the screen that owns it.</summary>
        public readonly struct Actions
        {
            public readonly System.Action Start;
            public readonly System.Action Leave;
            public readonly System.Action CopyCode;

            /// <summary>Called with the privacy the host just asked for.</summary>
            public readonly System.Action<bool> SetPrivacy;

            /// <summary>Called with -1 or +1 when a colour chevron is pressed.</summary>
            public readonly System.Action<int> StepColor;

            /// <summary>Called with the team a player asked to stand on.</summary>
            public readonly System.Action<int> JoinTeam;

            /// <summary>Called with the team count and team size the host asked for.</summary>
            public readonly System.Action<int, int> SetTeamRules;

            public Actions(System.Action start, System.Action leave, System.Action copyCode,
                System.Action<bool> setPrivacy, System.Action<int> stepColor,
                System.Action<int> joinTeam, System.Action<int, int> setTeamRules)
            {
                Start = start;
                Leave = leave;
                CopyCode = copyCode;
                SetPrivacy = setPrivacy;
                StepColor = stepColor;
                JoinTeam = joinTeam;
                SetTeamRules = setTeamRules;
            }
        }
```

- [ ] **Step 4: Add the host strip and the state the tests read**

Add these constants beside the existing top-bar slots, after `PrivacyStateWidth`:

```csharp
        // The team rules, laid out to the right of the privacy toggle in the same strip. Only a
        // versus lobby shows them, and only a host can press them — but everyone reads them, which
        // is why they are drawn rather than hidden from a client.
        private const float TeamRulesX = 660f;

        /// <summary>Room for two steppers side by side, at the strip's smaller type.</summary>
        private const float TeamRulesWidth = 1180f;

        /// <summary>The gap between the two steppers inside that slot.</summary>
        private const float TeamRulesGap = 40f;
```

Add the fields beside `privacyState`:

```csharp
        private GameObject teamRulesRow;

        /// <summary>The team steppers, or null in a story lobby. Public for the layout tests.</summary>
        public MenuStepper TeamsStepper { get; private set; }

        public MenuStepper TeamSizeStepper { get; private set; }

        /// <summary>Whether the team rules are on screen at all.</summary>
        public bool TeamRulesShown => teamRulesRow != null && teamRulesRow.activeSelf;

        /// <summary>Whether Start is offered. Only ever to the host.</summary>
        public bool StartShown => startAction != null && startAction.activeSelf;

        /// <summary>What the status line currently says.</summary>
        public string StatusText => status != null ? status.text : null;

        /// <summary>The rules the steppers last reported, so each reports against the other's value.</summary>
        private int shownTeamCount = VersusRules.DefaultTeams;

        private int shownTeamSize = VersusRules.DefaultTeamSize;
```

Add `using SpaceGame.Gameplay;` to the file's usings, then build the strip at the end of `BuildTopBar`:

```csharp
            RectTransform rulesSlot = Slice(bar, "TeamRules", TeamRulesX, TeamRulesWidth);
            teamRulesRow = rulesSlot.gameObject;

            var rulesLayout = rulesSlot.gameObject.AddComponent<HorizontalLayoutGroup>();
            rulesLayout.spacing = TeamRulesGap;
            rulesLayout.childControlWidth = true;
            rulesLayout.childControlHeight = true;
            rulesLayout.childForceExpandWidth = false;
            rulesLayout.childForceExpandHeight = true;
            rulesLayout.childAlignment = TextAnchor.MiddleLeft;

            TeamsStepper = MenuStepper.Create(entryPrefab, rulesSlot, "Teams",
                                              VersusRules.DefaultTeams,
                                              VersusRules.MinTeams, VersusRules.MaxTeams,
                                              teams => actions.SetTeamRules?.Invoke(teams, shownTeamSize));

            TeamSizeStepper = MenuStepper.Create(entryPrefab, rulesSlot, "Team size",
                                                 VersusRules.DefaultTeamSize,
                                                 VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                                                 size => actions.SetTeamRules?.Invoke(shownTeamCount, size));

            teamRulesRow.SetActive(false);
```

- [ ] **Step 5: Render from the snapshot**

Replace the whole `Render` method with:

```csharp
        /// <summary>
        /// Redraws from the snapshot the session hands over. Called on every change, so it has to be
        /// cheap enough to run twice a second — and it is the only thing that writes the privacy
        /// label and the team steppers, so what they read is always what is actually in force rather
        /// than the last thing anyone clicked.
        /// </summary>
        /// <param name="hostTitle">
        /// The world's name for a story host, null otherwise. A versus lobby has no world, and a
        /// joiner's title is the session's own name.
        /// </param>
        public void Render(RosterSnapshot snapshot, bool isHost, string hostTitle)
        {
            title.text = (!string.IsNullOrEmpty(hostTitle) ? hostTitle : lobbyName ?? "Session")
                .ToUpperInvariant();

            SetCode(string.IsNullOrEmpty(lobbyCode) ? "—" : lobbyCode);
            if (copyAction != null) copyAction.SetActive(!string.IsNullOrEmpty(lobbyCode));

            // Only the host can start, and only the host can change privacy or the rules. A client
            // shown any of them gets a control whose whole behaviour is to refuse.
            if (startAction != null) startAction.SetActive(isHost);
            if (privacyRow != null) privacyRow.SetActive(isHost);

            RenderTeamRules(snapshot, isHost);

            if (privacyState != null)
            {
                privacyState.text = isPrivate ? "on" : "off";
                privacyState.color = isPrivate ? PrivacyOn : PrivacyOff;
            }

            // Only what has to be read. A host is told nothing; a joiner is told the one thing they
            // cannot see for themselves, which is whether they are waiting or already being pulled
            // into a running world.
            SetPolledStatus(isHost ? string.Empty
                                   : isPlaying ? "The host is already playing. Joining the world…"
                                               : "Waiting for the host to start.");

            if (rank != null) rank.Render(snapshot);
        }

        /// <summary>
        /// Draws the rules for a versus lobby and hides them for a story one.
        ///
        /// Shown to everyone, live only for the host. A joiner who cannot read the rules cannot tell
        /// whether the team they are trying to join is full, which turns a refusal into a mystery.
        /// </summary>
        private void RenderTeamRules(RosterSnapshot snapshot, bool isHost)
        {
            if (teamRulesRow == null) return;

            teamRulesRow.SetActive(snapshot.IsVersus);
            if (!snapshot.IsVersus) return;

            shownTeamCount = snapshot.TeamCount;
            shownTeamSize = snapshot.TeamSize;

            TeamsStepper.SetValue(snapshot.TeamCount);
            TeamsStepper.SetLimits(VersusRules.MinTeams,
                                   VersusRules.ClampTeams(VersusRules.MaxTeams, snapshot.TeamSize));
            TeamsStepper.SetInteractable(isHost);

            TeamSizeStepper.SetValue(snapshot.TeamSize);
            TeamSizeStepper.SetLimits(VersusRules.MinTeamSize,
                                      VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, snapshot.TeamCount));
            TeamSizeStepper.SetInteractable(isHost);
        }
```

The old `Render` read the lobby's own name, code and playing state off the `Lobby`. Those three are not view state, so add them beside the existing `isPrivate` mirror and have `LobbyUI` set them before rendering:

```csharp
        /// <summary>The lobby's own name, code and state, mirrored so this view never sees a Lobby.</summary>
        private string lobbyName;

        private string lobbyCode;
        private bool isPlaying;

        /// <summary>Set by the screen from the lobby, immediately before <see cref="Render"/>.</summary>
        public void SetSession(string name, string code, bool playing, bool priv)
        {
            lobbyName = name;
            lobbyCode = code;
            isPlaying = playing;
            isPrivate = priv;
        }
```

In `LobbyUI.Render` and `LobbyUI.ShowRoster`, call it first:

```csharp
            Lobby lobby = session.Current;

            roster.SetSession(lobby?.Name, lobby?.LobbyCode,
                              lobby != null && LobbySession.IsPlaying(lobby),
                              lobby != null && lobby.IsPrivate);

            roster.Render(session.CurrentSnapshot(), session.IsHost,
                          IsHosting && !route.IsVersus() ? WorldSession.DisplayName : null);
```

- [ ] **Step 6: Point the rank at the snapshot**

In `Build`, the rank is created as before. `LobbyRosterView.SetLocalColor` keeps its signature and still forwards to the rank. The rank's own `Render` becomes `Render(RosterSnapshot)` in Task 13; until then this step will not compile, which is expected.

- [ ] **Step 7: Type-check and run the tests**

Complete Task 13 first — the two files change together — then run the Roslyn pass and:

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 6 `LobbyRosterViewTests` pass.

- [ ] **Step 8: Commit**

Stage `LobbyRosterView.cs`, `LobbyUI.cs` and `Assets/Game/Editor/Tests/LobbyRosterViewTests.cs`, then commit with the message `feat: roster renders from a snapshot, host team steppers`.

---

## Task 13: Team clusters in the rank

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyPreviewRank.cs`
- Test: `Assets/Game/Editor/Tests/LobbyRankLayoutTests.cs`

The rank holds four fixed slots today. It becomes as many as the rules ask for, grouped, with a plate over each team that is also the way onto it.

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/LobbyRankLayoutTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rank's own arithmetic — which figure stands in which seat, and whether a team can be
    /// stood on at all.
    ///
    /// The figures themselves need a Resources prefab and a camera, so what is pinned here is the
    /// mapping the rank does before it touches either.
    /// </summary>
    public class LobbyRankLayoutTests
    {
        [Test]
        public void PlayersFillTheirOwnTeamsSeatsInLobbyOrder()
        {
            int[] teams = { 0, 1, 0 };

            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(0, teams));
            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(1, teams), "team two's first player is its seat 0");
            Assert.AreEqual(1, LobbyPreviewRank.SeatOf(2, teams));
        }

        [Test]
        public void AStoryLobbyPutsEveryoneOnOneTeam()
        {
            int[] teams = System.Array.Empty<int>();

            Assert.AreEqual(0, LobbyPreviewRank.SeatOf(0, teams));
            Assert.AreEqual(2, LobbyPreviewRank.SeatOf(2, teams));
        }

        [Test]
        public void ASeatBeyondTheTeamsSizeIsStillPlaced()
        {
            int[] teams = { 0, 0, 0 };

            Assert.AreEqual(2, LobbyPreviewRank.SeatOf(2, teams),
                "a player over the size cap has to stand somewhere, not vanish");
        }

        [Test]
        public void TheTeamYouAreOnIsNotOfferedAsSomewhereToGo()
        {
            Assert.IsFalse(LobbyPreviewRank.CanJoin(team: 1, localTeam: 1, headsOn: 0, teamSize: 2));
        }

        [Test]
        public void AFullTeamIsNotOfferedEither()
        {
            Assert.IsFalse(LobbyPreviewRank.CanJoin(team: 0, localTeam: 1, headsOn: 2, teamSize: 2));
        }

        [Test]
        public void ATeamWithRoomIsOffered()
        {
            Assert.IsTrue(LobbyPreviewRank.CanJoin(team: 0, localTeam: 1, headsOn: 1, teamSize: 2));
        }

        /// <summary>
        /// A spectator — someone in the lobby with no team yet — must be able to join any team with
        /// room, or they have nowhere at all to stand.
        /// </summary>
        [Test]
        public void SomeoneWithNoTeamCanJoinAnyTeamWithRoom()
        {
            Assert.IsTrue(LobbyPreviewRank.CanJoin(team: 0, localTeam: -1, headsOn: 0, teamSize: 2));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `'LobbyPreviewRank' does not contain a definition for 'SeatOf'`.

- [ ] **Step 3: Rewrite the rank's state as lists**

In `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyPreviewRank.cs`:

**3a.** Add `using System.Collections.Generic;` and `using SpaceGame.Gameplay;`.

**3b.** Replace the six fixed-size arrays and `Slots` with growable lists. The comment on `Slots` — "Slots are fixed, so nobody slides sideways when somebody joins" — still holds and now lives on the seat arithmetic in `RankLayout`, which addresses a seat whether or not anyone stands in it:

```csharp
        // One entry per PLAYER in the lobby, not per seat. Seats are addressed positionally by
        // RankLayout, which is what keeps a figure from sliding sideways when someone else joins;
        // these lists only have to grow with the number of people actually here.
        private readonly List<GameObject> figures = new();
        private readonly List<Transform> heads = new();
        private readonly List<SuitRecolor> recolors = new();
        private readonly List<RectTransform> labelRows = new();
        private readonly List<TextMeshProUGUI> labels = new();
        private readonly List<TextMeshProUGUI> labelShadows = new();
        private readonly List<RectTransform> underlines = new();
        private readonly List<bool> occupied = new();

        // One entry per TEAM. Rebuilt whenever the host changes the rules, because a plate is
        // positioned from the team count and there is nothing to move it to otherwise.
        private readonly List<RectTransform> teamPlates = new();
        private readonly List<TextMeshProUGUI> teamLabels = new();
        private readonly List<TextMeshProUGUI> teamLabelShadows = new();
        private readonly List<Button> teamButtons = new();

        /// <summary>The rules the plates were last built for, so they are rebuilt only when they move.</summary>
        private int builtTeamCount = -1;

        private int builtTeamSize = -1;

        /// <summary>The last snapshot drawn, which LateUpdate needs to keep the overlays placed.</summary>
        private RosterSnapshot current;

        /// <summary>Called with the team a plate was pressed for.</summary>
        private Action<int> onJoinTeam;
```

**3c.** Add the pure helpers the tests reach for, near the top of the class:

```csharp
        /// <summary>
        /// Which seat of their own team a player stands in: their position among the players on
        /// that team, in lobby order.
        ///
        /// Lobby order rather than anything cleverer because it is the one ordering every peer
        /// agrees on — two machines seating the same rank differently is two different pictures of
        /// the same lobby.
        ///
        /// A story lobby passes an empty <paramref name="teams"/> and everyone is on team zero,
        /// which is the four-in-a-line the rank has always drawn.
        /// </summary>
        public static int SeatOf(int slot, int[] teams)
        {
            if (teams == null || teams.Length == 0) return slot;
            if (slot < 0 || slot >= teams.Length) return 0;

            int team = teams[slot];
            int seat = 0;

            for (int i = 0; i < slot; i++)
                if (teams[i] == team)
                    seat++;

            return seat;
        }

        /// <summary>
        /// Whether a team is somewhere this player could actually go.
        ///
        /// The team you already stand on is not, and neither is a full one — a plate that accepts a
        /// press it cannot honour is worse than one that plainly will not take it. Someone with no
        /// team yet may go anywhere with room, or they have nowhere to stand at all.
        /// </summary>
        public static bool CanJoin(int team, int localTeam, int headsOn, int teamSize) =>
            team != localTeam && headsOn < teamSize;
```

**3d.** Replace `Create` so it takes the join callback:

```csharp
        public static LobbyPreviewRank Create(RectTransform page, GameObject entryPrefab,
            Action<int> onStep, Action<int> onJoinTeam)
        {
            var host = new GameObject(nameof(LobbyPreviewRank));
            var rank = host.AddComponent<LobbyPreviewRank>();

            rank.entryPrefab = entryPrefab;
            rank.onStep = onStep;
            rank.onJoinTeam = onJoinTeam;
            rank.labelLayer = UIBuilder.Fill(UIBuilder.Rect("PreviewLabels", page));

            // Before the anchor is resolved, because the anchor's own fallback is computed from
            // where the camera is looking — and by then it should be looking at the lobby's shot.
            rank.AdoptCameraView();
            rank.ResolveAnchor();
            rank.BuildCycler();

            return rank;
        }
```

In `LobbyRosterView.Build`, pass the new argument:

```csharp
            rank = LobbyPreviewRank.Create(page, entryPrefab, Step, JoinTeam);
```

and add beside `Step`:

```csharp
        private void JoinTeam(int team) => actions.JoinTeam?.Invoke(team);
```

**3e.** Replace `Render` with the snapshot version:

```csharp
        /// <summary>
        /// Fills the rank from the snapshot.
        ///
        /// A story lobby draws one group of everybody, which is the line the rank has always been.
        /// A versus lobby draws one group per team, spaced by <see cref="RankLayout.TeamGap"/>, with
        /// a plate over each that is also the way onto it.
        /// </summary>
        public void Render(RosterSnapshot snapshot)
        {
            current = snapshot;

            int teams = snapshot.IsVersus ? snapshot.TeamCount : 1;
            int teamSize = snapshot.IsVersus ? snapshot.TeamSize : Mathf.Max(1, snapshot.Names.Length);

            EnsureTeamPlates(teams, teamSize, snapshot.IsVersus);

            for (int slot = 0; slot < snapshot.Names.Length; slot++)
            {
                int team = snapshot.IsVersus && slot < snapshot.Teams.Length ? snapshot.Teams[slot] : 0;
                int seat = SeatOf(slot, snapshot.IsVersus ? snapshot.Teams : System.Array.Empty<int>());

                EnsureFigure(slot);
                if (slot >= figures.Count || figures[slot] == null) continue;

                figures[slot].SetActive(true);
                figures[slot].transform.localPosition =
                    RankLayout.SeatPosition(team, seat, teams, teamSize);
                FaceCamera(figures[slot].transform);
                occupied[slot] = true;

                // In a match everyone on a side wears the side's colour; outside one, their own.
                int color = snapshot.IsVersus
                    ? snapshot.ColorOfTeam(team)
                    : slot < snapshot.SuitColors.Length ? snapshot.SuitColors[slot] : 0;

                if (recolors[slot] != null) recolors[slot].Apply(color);

                EnsureLabel(slot);
                labels[slot].text = snapshot.Names[slot];
                labelShadows[slot].text = snapshot.Names[slot];

                // An underline instead of the word "host": there is no room for a caption per
                // figure, and a rank only ever needs to mark one of them.
                if (underlines[slot] != null)
                    underlines[slot].gameObject.SetActive(slot == snapshot.HostSlot);
            }

            // Anyone who has left. Switched off rather than destroyed, because the next poll very
            // often puts the same number of people back.
            for (int slot = snapshot.Names.Length; slot < figures.Count; slot++)
            {
                occupied[slot] = false;
                if (figures[slot] != null) figures[slot].SetActive(false);
            }

            RenderTeamPlates(snapshot, teams, teamSize);

            cyclerWanted = snapshot.LocalSlot >= 0 && snapshot.LocalSlot < figures.Count
                           && occupied[snapshot.LocalSlot];

            if (cyclerWanted)
                SetCyclerColor(snapshot.IsVersus
                    ? snapshot.ColorOfTeam(snapshot.LocalTeam)
                    : snapshot.LocalSlot < snapshot.SuitColors.Length
                        ? snapshot.SuitColors[snapshot.LocalSlot]
                        : 0);

            FitCamera(RankLayout.TotalWidth(teams, teamSize));
            PositionOverlays();
        }
```

**3f.** Add the plates:

```csharp
        /// <summary>How far above the anchor line a team's plate floats, in metres.</summary>
        private const float PlateLift = 2.35f;

        private const int TeamNameSize = 46;

        /// <summary>Rebuilds the plates when the rules move them, and only then.</summary>
        private void EnsureTeamPlates(int teams, int teamSize, bool versus)
        {
            if (teams == builtTeamCount && teamSize == builtTeamSize) return;

            foreach (RectTransform plate in teamPlates)
                if (plate != null)
                    Destroy(plate.gameObject);

            teamPlates.Clear();
            teamLabels.Clear();
            teamLabelShadows.Clear();
            teamButtons.Clear();

            builtTeamCount = teams;
            builtTeamSize = teamSize;

            if (!versus || labelLayer == null) return;

            for (int team = 0; team < teams; team++)
            {
                RectTransform row = UIBuilder.Rect($"Team{team}", labelLayer);
                row.sizeDelta = new Vector2(420f, 64f);

                RectTransform shadow = UIBuilder.Fill(UIBuilder.Rect("Shadow", row));
                shadow.anchoredPosition = new Vector2(2f, -2f);
                teamLabelShadows.Add(UIBuilder.Label(shadow, VersusRules.TeamName(team), TeamNameSize,
                                                     MenuEntry.Idle, TextAlignmentOptions.Center,
                                                     FontStyles.Bold));

                RectTransform front = UIBuilder.Fill(UIBuilder.Rect("Front", row));
                TextMeshProUGUI label = UIBuilder.Label(front, VersusRules.TeamName(team), TeamNameSize,
                                                        MenuEntry.Title, TextAlignmentOptions.Center,
                                                        FontStyles.Bold);

                int captured = team;
                Button button = UIBuilder.Clickable(row, UIBuilder.HitArea(row), Color.white,
                                                    Color.white);
                button.onClick.AddListener(() => onJoinTeam?.Invoke(captured));

                teamPlates.Add(row);
                teamLabels.Add(label);
                teamButtons.Add(button);
            }
        }

        /// <summary>
        /// Paints each plate in its team's colour and says whether it can be stood on.
        ///
        /// A plate you cannot use goes translucent and stops taking clicks rather than disappearing:
        /// the team is still part of the match, and a team that vanishes because it filled up is a
        /// match that appears to have shrunk.
        /// </summary>
        private void RenderTeamPlates(RosterSnapshot snapshot, int teams, int teamSize)
        {
            for (int team = 0; team < teamPlates.Count && team < teams; team++)
            {
                bool joinable = CanJoin(team, snapshot.LocalTeam, snapshot.HeadsOn(team), teamSize);

                Color color = SuitPalette.ColorOf(snapshot.ColorOfTeam(team));
                teamLabels[team].color = joinable ? color : new Color(color.r, color.g, color.b, 0.45f);

                teamButtons[team].interactable = joinable;
            }
        }
```

**3g.** Replace `EnsureFigure`'s fixed-slot body so it grows the lists, and drop the `localPosition` line — `Render` places every figure now:

```csharp
        private void EnsureFigure(int slot)
        {
            while (figures.Count <= slot)
            {
                figures.Add(null);
                heads.Add(null);
                recolors.Add(null);
                occupied.Add(false);
                labelRows.Add(null);
                labels.Add(null);
                labelShadows.Add(null);
                underlines.Add(null);
            }

            if (figures[slot] != null) return;

            if (figurePrefab == null)
            {
                figurePrefab = Resources.Load<GameObject>(PrefabResource);

                if (figurePrefab == null)
                {
                    Debug.LogError($"[LobbyPreviewRank] No '{PrefabResource}' in a Resources folder. " +
                                   "Run Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview to build it. " +
                                   "The lobby still works; it just has nobody standing in it.");
                    return;
                }
            }

            if (anchor == null) return;

            GameObject figure = Instantiate(figurePrefab, anchor);
            figure.name = $"PreviewAstronaut{slot}";

            figures[slot] = figure;
            heads[slot] = FindHead(figure.transform) ?? figure.transform;
            recolors[slot] = figure.GetComponentInChildren<SuitRecolor>(true);

            SetupAnimator(figure, slot);
        }
```

**3h.** Add the camera fit, beside `AdoptCameraView`:

```csharp
        /// <summary>How much of the frame is left as air around the rank. A fifth.</summary>
        private const float FitMargin = 1.2f;

        /// <summary>
        /// Backs the camera off along its own forward until the whole rank is in frame.
        ///
        /// The authored <see cref="CameraViewName"/> composes a shot of four figures. Six teams of
        /// four is twenty-four, spread across some thirty metres, and no amount of composing fixes
        /// that from a fixed distance — so the view supplies the ANGLE and this supplies the
        /// distance.
        ///
        /// Never closer than the authored pose: a two-by-two match should look like the shot that
        /// was composed, not like a close-up nobody asked for.
        /// </summary>
        private void FitCamera(float rankWidth)
        {
            if (borrowedCamera == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            // Horizontal, not vertical: the rank is a horizontal problem, and a camera fitted on
            // Unity's vertical FOV frames four figures and clips twenty-four.
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                                  * camera.aspect) * Mathf.Rad2Deg;

            float wanted = RankLayout.CameraDistance(rankWidth, horizontalFov, FitMargin);
            float authored = Vector3.Distance(returnPosition, anchor != null ? anchor.position : Vector3.zero);

            float distance = Mathf.Max(wanted, authored);

            // Measured from the ANCHOR along the authored view's own backward direction, so the
            // shot keeps its angle and only its length changes.
            Vector3 centre = anchor != null ? anchor.position : borrowedCamera.position;
            borrowedCamera.SetPositionAndRotation(centre - returnRotation * Vector3.forward * distance
                                                  + Vector3.up * (returnPosition.y - centre.y),
                                                  returnRotation);
        }
```

**3i.** Place the plates every frame, in `PositionOverlays`, after the name labels:

```csharp
            for (int team = 0; team < teamPlates.Count; team++)
            {
                if (teamPlates[team] == null) continue;

                Vector3 centre = RankLayout.TeamCenter(team, builtTeamCount, builtTeamSize);
                Vector3 world = anchor != null
                    ? anchor.TransformPoint(centre + Vector3.up * PlateLift)
                    : centre + Vector3.up * PlateLift;

                teamPlates[team].gameObject.SetActive(Place(camera, teamPlates[team], world));
            }
```

**3j.** In `Dispose`, destroy the plates with everything else:

```csharp
            foreach (RectTransform plate in teamPlates)
                if (plate != null) Destroy(plate.gameObject);

            teamPlates.Clear();
```

- [ ] **Step 4: Type-check**

Run the Roslyn pass. Expected: no errors — this is also what clears the errors Task 12 left in `LobbyRosterView`.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 7 `LobbyRankLayoutTests` and all 6 `LobbyRosterViewTests` pass.

- [ ] **Step 6: Commit**

Stage `LobbyPreviewRank.cs`, `LobbyRosterView.cs` and `Assets/Game/Editor/Tests/LobbyRankLayoutTests.cs`, then commit with the message `feat: team clusters and joinable team plates in the lobby rank`.

---

## Task 14: Carry the teams into the world

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/LobbyUI.cs`
- Modify: `Assets/Game/Scripts/Core/Multiplayer/PlayerIdentity.cs`
- Test: `Assets/Game/Editor/Tests/VersusHandoffTests.cs`

- [ ] **Step 1: Write the failing test**

`Assets/Game/Editor/Tests/VersusHandoffTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What a peer wears when it arrives in the world.
    ///
    /// The rule that matters is the one about the stored preference: a match must not spend it. The
    /// suit colour in GameSettings is a property of the install, and a player who joins one versus
    /// match should not find their own colour permanently replaced by the side they happened to be
    /// put on.
    /// </summary>
    public class VersusHandoffTests
    {
        [TearDown]
        public void Reset() => VersusSession.Clear();

        [Test]
        public void OutsideAMatchAPeerWearsItsOwnSwatch()
        {
            VersusSession.Clear();

            Assert.AreEqual(7, PlayerIdentity.ResolveSuitColor(storedSwatch: 7));
        }

        [Test]
        public void InsideAMatchAPeerWearsItsTeamsSwatch()
        {
            VersusSession.Begin(teamCount: 2, teamSize: 2, localTeam: 1, teamColors: new[] { 4, 9 });

            Assert.AreEqual(9, PlayerIdentity.ResolveSuitColor(storedSwatch: 7));
        }

        /// <summary>
        /// A match with no team assigned yet must not paint everyone team zero's colour before the
        /// assignment lands — the stored swatch is the honest answer until then.
        /// </summary>
        [Test]
        public void AMatchWithNoTeamYetFallsBackToTheStoredSwatch()
        {
            VersusSession.Begin(2, 2, localTeam: -1, teamColors: new[] { 4, 9 });

            Assert.AreEqual(7, PlayerIdentity.ResolveSuitColor(storedSwatch: 7));
        }

        /// <summary>
        /// The load-bearing assertion of this whole file: resolving a colour must not spend the
        /// player's own. A match lasts minutes; the preference is meant to outlive the install's
        /// every session.
        /// </summary>
        [Test]
        public void ResolvingAColourDoesNotWriteTheStoredPreference()
        {
            int before = GameSettings.SuitColorIndex;

            try
            {
                GameSettings.SuitColorIndex = 7;
                VersusSession.Begin(2, 2, 1, new[] { 4, 9 });

                PlayerIdentity.ResolveSuitColor(GameSettings.SuitColorIndex);

                Assert.AreEqual(7, GameSettings.SuitColorIndex,
                    "ResolveSuitColor is a read — it has no business writing the preference");
            }
            finally
            {
                GameSettings.SuitColorIndex = before;
            }
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`.
Expected: compile failure, `'PlayerIdentity' does not contain a definition for 'ResolveSuitColor'`.

- [ ] **Step 3: Publish the team from PlayerIdentity**

In `Assets/Game/Scripts/Core/Multiplayer/PlayerIdentity.cs`, add `using SpaceGame.Gameplay;`, then:

**3a.** Add a replicated team beside the existing `suitColor` NetworkVariable, matching its write permission and its `-1` sentinel:

```csharp
        /// <summary>
        /// Which side this player is on, or -1 outside a match.
        ///
        /// Replicated rather than derived because only this peer knows it: the lobby that assigned
        /// it is gone by the time anyone is in the world, and a client cannot read another client's
        /// VersusSession.
        /// </summary>
        private readonly NetworkVariable<int> team =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>The team this player stands on, or -1. Read by anything that needs sides.</summary>
        public int Team => team.Value;
```

**3b.** Add the pure resolver:

```csharp
        /// <summary>
        /// The swatch this peer should be painted in: its team's inside a match, its own outside
        /// one.
        ///
        /// <para>
        /// A pure read, and deliberately not a write. The stored preference in
        /// <c>GameSettings.SuitColorIndex</c> is a property of the install — it survives quitting,
        /// and it is what a player picked for themselves. Overwriting it with the colour of a side
        /// they were put on for one match would spend a preference to carry a temporary fact, and
        /// they would find it gone the next time they played alone.
        /// </para>
        ///
        /// <para>
        /// A match with no team assigned yet falls back to the stored swatch rather than to team
        /// zero's, so nobody is briefly painted onto a side they are not on.
        /// </para>
        /// </summary>
        public static int ResolveSuitColor(int storedSwatch)
        {
            if (!VersusSession.IsActive || VersusSession.LocalTeam < 0)
                return SuitPalette.Clamp(storedSwatch);

            return SuitPalette.Clamp(VersusSession.ColorOf(VersusSession.LocalTeam));
        }
```

**3c.** In `PublishLocalProfile`, publish through it and send the team:

```csharp
            int wantedColor = ResolveSuitColor(GameSettings.SuitColorIndex);
            if (wantedColor != suitColor.Value) suitColor.Value = wantedColor;

            int wantedTeam = VersusSession.IsActive ? VersusSession.LocalTeam : -1;
            if (wantedTeam != team.Value) team.Value = wantedTeam;
```

- [ ] **Step 4: Stage the match on the way into the world**

In `LobbyUI.StartGame`, before the scene load, record what the world needs. A joiner reaches the world through `EnterIfPlaying` instead, so both routes get the same two lines — factor them into one method:

```csharp
        /// <summary>
        /// Hands the match to the world.
        ///
        /// Every route into a versus world goes through here: the host's Start, and a joiner's
        /// EnterIfPlaying. The lobby is about to be destroyed by the load, so this is the last
        /// moment either of them can read it.
        /// </summary>
        private void StageVersusSession()
        {
            if (!route.IsVersus())
            {
                VersusSession.Clear();
                return;
            }

            RosterSnapshot snapshot = session.CurrentSnapshot();

            VersusSession.Begin(snapshot.TeamCount, snapshot.TeamSize, snapshot.LocalTeam,
                                snapshot.TeamColors);
        }
```

Call `StageVersusSession();` as the first statement of both `StartGame` and `EnterIfPlaying`.

- [ ] **Step 5: Type-check**

Run the Roslyn pass. Expected: no errors.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`.
Expected: all 4 `VersusHandoffTests` pass, and `SessionProfileTests` is unchanged.

- [ ] **Step 7: Commit**

Stage `LobbyUI.cs`, `PlayerIdentity.cs` and `Assets/Game/Editor/Tests/VersusHandoffTests.cs`, then commit with the message `feat: carry teams and team colours into the world`.

---

## Task 15: Prove it on a real client

**Files:** none — this is verification, and it is the task CLAUDE.md's first non-negotiable actually asks for. A feature seen working only on the host is not finished.

- [ ] **Step 1: Run the whole suite**

```bash
rm -f Temp/headless_tests.txt
grep -rho '\[Test\]' Assets/Game | wc -l
```

Note that number. Click `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, poll for `DONE`, then read the counts. A run that comes back in seconds was truncated by a domain reload — re-run it. Expected: the ten standing failures listed in "Before you start", and nothing else.

- [ ] **Step 2: Build a second peer**

Click `Tools ▸ Tests ▸ Build Multiplayer Test Player`. Build from the Unity UI, not over the MCP bridge — a full player build exceeds the bridge's ceiling and comes back as "Build was canceled".

- [ ] **Step 3: Walk the story routes, which must not have changed**

In the Editor, from the front menu:

1. **Story ▸ Singleplayer** → the world list opens, a world loads.
2. **Story ▸ Multiplayer ▸ Host** → the world list opens, then the lobby, with four astronauts in one line and the colour cycler under your own.
3. **Story ▸ Multiplayer ▸ Join** → the browser lists story sessions only.

Expected: identical to the behaviour before this branch. If the rank is grouped or a team plate is visible in a story lobby, `RosterSnapshot.IsVersus` is being set wrongly.

- [ ] **Step 4: Host a match and check the rules page**

**VS ▸ Host a game** → the rules page. Step Teams up to 8 and Team size up to 12 in turn.

Expected: the seat caption never exceeds "24 of 24 seats", and raising one axis pulls the other down rather than a chevron doing nothing.

Set 3 teams of 2 and press **Start lobby**.

Expected: the lobby opens with three team plates, spaced apart, the session named after you, and a code in the top strip.

- [ ] **Step 5: Join from the built player — the half that host-only testing never proves**

Launch the built player, go **VS ▸ Join a game**, and join by code.

Check, on the **client**:

1. The browser listed the match and did **not** list any story session.
2. Three team plates are drawn, in the same three colours as on the host.
3. The client's own astronaut stands in a team cluster, not in a line.
4. The host's figure carries the underline.

Check, on the **host**, that the client appeared in the right cluster.

- [ ] **Step 6: Switch teams and recolour, from the client**

On the client, click another team's plate.

Expected: within a poll (about two seconds), the client's astronaut moves to that cluster **on both machines**. Click the team you are already on — nothing happens, and no error appears.

Fill a team to its size, then click its plate from the other machine.

Expected: the plate is translucent and does not take the click; the status line says "TEAM N is full."

Step the colour cycler on the client.

Expected: the client's own team recolours immediately on the client and within a poll on the host — **the whole team, not one figure** — and stepping never lands on a colour another team is wearing.

- [ ] **Step 7: Retune from the host while the client watches**

On the host, press **Teams +**.

Expected: a fourth plate appears on both machines and the rank re-spaces; the camera pulls back.

Press **Team size −** until it would cut into an occupied team.

Expected: the number does not move and the status line names the team that is in the way.

On the client, confirm the steppers are readable and not pressable.

- [ ] **Step 8: Start the match**

Press **Start game** on the host.

Expected: both peers load the world; every player wears their team's colour, not their personal swatch; and the two sides are visibly different colours.

- [ ] **Step 9: Confirm the preference survived**

Quit both, relaunch the Editor peer, and open **Story ▸ Multiplayer ▸ Host**.

Expected: your own astronaut wears the swatch you had before the match. If it wears the team's colour, something wrote `GameSettings.SuitColorIndex` — `ResolveSuitColor` is a read and nothing else.

- [ ] **Step 10: Say what is not persisted, out loud**

Per CLAUDE.md's second non-negotiable, this is the explicit answer rather than a skipped question: **a VS match holds no state worth persisting.** The rules live in the lobby for the life of the session; the rules page reseeds its statics on entry; `VersusSession` is cleared on every route that is not a versus start; and the match itself is transient by design — no world is staged, so nothing is loaded and nothing is saved. Confirm by checking that no save file appears for a VS session:

```bash
ls -la ~/Library/Application\ Support/DefaultCompany/SpaceGame/ 2>/dev/null | tail -5
```

- [ ] **Step 11: Commit the verification notes**

Add a short `docs/architecture/Versus.md` recording the flow, the lobby keys, and the two constraints future work will trip over — Relay's fixed allocation, and `UpdateLobbyAsync` being host-only — then commit it with the message `docs: versus mode`.

---

## Self-review

Checked against the spec, section by section:

| Spec section | Task |
| --- | --- |
| Front menu: Story / VS / Quit | 6, 7 |
| Story keeps today's behaviour | 6 (routes), 15 step 3 (proof) |
| VS host → rules → lobby; VS join → lobby | 6, 8, 11 |
| Rules page tunes team count and size | 5, 8 |
| Seats = teams × size, ceiling 24 | 1, 8, 10 |
| Relay allocated for the ceiling, lobby advertises the rules | 10 |
| Lobby data model (mode, rules, team, stamped colour) | 9 |
| Explicit lobby route replaces `WorldSession.IsActive` | 11 |
| Mode-filtered browser | 11 |
| Team clusters with a gap | 3, 13 |
| Team plates are the click target; full teams refuse visibly | 13 |
| Any member recolours their team; no two teams share a swatch | 2, 11, 13 |
| Host retunes live; occupied teams refuse to shrink | 1, 10, 12 |
| Camera fits the rank; teams over four wrap | 3, 13 |
| Teams reach the world; personal preference untouched | 14 |
| Multiplayer verified on a real client | 15 |
| Persistence question answered explicitly | 15 step 10 |

Deliberate ordering note: Tasks 12 and 13 change `LobbyRosterView` and `LobbyPreviewRank` together and do not type-check independently — Task 12 step 7 says so and defers its test run to the end of Task 13. Task 6 likewise leaves one `CS0246` that Task 8 clears. Both are called out at the step where they bite rather than left to be discovered.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-25-versus-mode-ui.md`. Two execution options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, reviewed between tasks, fast iteration.

**2. Inline Execution** — tasks executed in this session with checkpoints for review.

Which approach?
