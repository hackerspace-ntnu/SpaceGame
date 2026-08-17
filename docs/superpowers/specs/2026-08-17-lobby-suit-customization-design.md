# Lobby suit customization

Players pick a suit colour in the multiplayer lobby, see each other's astronauts standing in
the menu scene with names above their heads, and carry that colour into the world. The main
menu stops showing the Nomad preview harness and goes back to its standard view.

## What a player does

Opens Multiplayer, lands in the lobby, and sees a rank of astronauts standing on the sand —
one per player in the session, each in their own colour, each with their name above their
head. Under their own figure is `◀ Ember ▶`. Pressing an arrow changes their astronaut
immediately; everyone else sees it within a poll. Starting the game carries the colour onto
their body in the world, where every peer sees it.

## Decisions

| Decision | Choice |
|---|---|
| Granularity | One colour for the whole suit. No zones, no per-part choice. |
| What the colour does | The four other hued parts follow it, keeping today's relationships. |
| Colour source | Fixed list of 14 vivid swatches. No picker. |
| Default | A random vivid swatch, picked once on first launch and kept. |
| Reach | Lobby and in game. Changeable in the lobby only. |
| Preview | Live astronauts standing in the real menu scene, not a RenderTexture. |
| Empty slots | Absent. Not ghosted. |
| Framing | The lobby borrows the camera for a shot of its own, on open dune. |

## 1. The colour model

One value per player: an index into a fixed list of swatches.

`SuitPalette` is a **static class in code**, not a ScriptableObject. This table is the decode
key for a synced index — two peers holding different palette assets would make index 3 mean
different colours on different machines, and a ScriptableObject adds an Inspector reference
that can be left unassigned. A static table can neither desync nor be misconfigured. The cost
is that retuning a colour is a code edit; it is one small file, and this project already
generates its art from scripts.

Swatches, in order. Ember is index 0 so it stays the colour the astronaut has always been:

| # | Name | Hex | # | Name | Hex |
|---|---|---|---|---|---|
| 0 | Ember | `#E7771C` | 7 | Aqua | `#00D6A5` |
| 1 | Signal Red | `#FF3B30` | 8 | Green | `#2ED84B` |
| 2 | Magenta | `#FF2FB0` | 9 | Lime | `#B5E838` |
| 3 | Violet | `#B94BFF` | 10 | Sunburst | `#FFD400` |
| 4 | Ultramarine | `#4A5BFF` | 11 | Tangerine | `#FF8A00` |
| 5 | Cobalt | `#0057E7` | 12 | Bone | `#F2F0EA` |
| 6 | Cyan | `#00C8FF` | 13 | Graphite | `#2A2E35` |

Bone and Graphite are near-neutral, so they are deliberate choices only and are never handed
out as a random default.

Slot 5 started as an azure `#00A2FF` and was moved to a deeper cobalt: at `#00A2FF` it sat 0.149
from Cyan, which `TheSwatchesAreTellableApart` rejects, and which in the lobby would have meant
two arrow presses that appeared to do nothing.

### Derivation

The astronaut carries seven hued materials today, in five distinct colours. Applying a swatch
rebuilds all of them from the chosen colour, each keeping the relationship it has to the
harness. Offsets measured in sRGB HSV against the harness (`#E7771C`, H 26.9° / S 0.879 /
V 0.906):

| Materials | Part | Δhue | sat × | val × |
|---|---|---|---|---|
| `Material.002` `Material.043` `Material.047` | harness, vest, kneepads, straps, buckle | 0° | 1.00 | 1.00 |
| `Material.049` | pack panel | +7.4° | 0.76 | 1.00 |
| `Material.048` | scarf, cubes | −11.2° | 0.90 | 0.63 |
| `Material.045` | scarf trim | +20.6° | 1.14 | 1.10 |
| `Material.046` | pack detail | +198.7° | 0.62 | 0.79 |

Saturation and value are clamped to 1. The `+198.7°` on the pack detail is what keeps the
model reading as two-tone: whatever you pick, that detail lands on the opposite side of the
wheel, so cyan gives a tan detail and magenta gives a mint one.

Untouched, always: `Material.044` (suit body), `Material.050` (helmet), `Material.051`
(gloves, boots), `Material.052` (hardware), `Material.054` (tubing).

## 2. Applying it

`SuitRecolor` — a MonoBehaviour on the astronaut model root, with no netcode in it, so the
lobby preview and the networked player use the same component.

`Apply(int swatch)` walks every child renderer's material slots, matches
`sharedMaterials[i].name` against the table, and writes the derived colour with a
**`MaterialPropertyBlock` on that slot**, setting `_BaseColor` and `_Color`.

Property blocks rather than material instances, because the FBX's materials are read-only
embedded sub-assets, and a block has no lifetime to manage — nothing leaks on despawn and the
editor preview and runtime take the same path. The cost is that these renderers leave the SRP
batcher. With at most four astronauts that is acceptable; the fallback, if it ever shows up in
a profile, is extracting the seven materials to assets and swapping instances.

Matching is **by material name**, which means a Blender re-export that renames materials would
silently stop recolouring. Guarded twice: `Apply` logs an error when it matches fewer slots
than the table expects, and an EditMode test asserts every name in the table exists on
`astronaut_tobb.fbx`. Both names and the `mixamorig:Head` bone are verified present in the
shipped FBX today.

## 3. One value, three homes

**Persisted** — `GameSettings.SuitColorIndex`, in PlayerPrefs beside `PlayerName`. Seeded on
first read with a random pick from the twelve vivid swatches and written immediately, the same
trick `PlayerName` uses to mint `Pilot-1234` once and keep it. Added to `ResetToDefaults`.

**In the lobby** — a `KeySuitColor` player-data entry written by `BuildPlayer`, updated by
`LobbySession.SetSuitColorAsync`. Read back by `LobbySession.SuitColors(lobby)`, a sibling of
`PlayerNames(lobby)` and defensive in the same way: that method's own comment records an
unguarded indexer killing the roster on every poll.

**In game** — a second owner-write `NetworkVariable<int>` on `PlayerIdentity`, published by the
path that already runs on `GameSettings.Changed`. An `int`, not a string, so the `FixedString`
null trap does not apply. `PlayerIdentity` calls into `SuitRecolor` on spawn and on change.

Pressing an arrow changes the local value and the local figure immediately. The lobby write is
debounced ~0.75 s and coalesced, because `UpdatePlayerAsync` is rate-limited to five calls per
five seconds and ten fast presses would trip it.

## 4. The lobby preview

`LobbyPreviewRank`, a plain MonoBehaviour owned by the roster page. It spawns up to
`LobbySession.MaxPlayers` instances of `LobbyPreviewAstronaut.prefab` — model, Animator, and
`SuitRecolor`, and nothing else: no colliders, no gameplay components, no NetworkObject.

The Animator needs `IsGrounded = true` and both speeds at zero, or the controller plays
*jump/fall*. `IdleIndex` is varied per slot so four figures do not idle in lockstep.

Figures are parented to a `LobbyPreviewAnchor` placed in `MainMenu.unity` and spaced 1.45 m along
its local X, so they stand on real sand under the menu's own sun with real shadows. When the
anchor is missing, the rank computes a spot ahead of `Camera.main` and raycasts down — degrading
rather than vanishing, the way `MenuEntry` builds a plain button when its prefab is absent.

The rank exists only while the roster page is up. It is created when that page is built and
destroyed when the page is swapped or the screen goes away, so the main menu itself never has
astronauts standing in it.

### The lobby's own shot

The lobby borrows the menu camera and puts it back. The menu's framing is composed around the
ruin and three decorative `AstronautArmature` figures on the right, which leaves the rank nowhere
to stand: the left of the frame is the control column and the middle is where a mannequin has its
arm out — measured from a capture, where that arm crossed the fourth astronaut's chest.

`LobbyCameraView`, a second authored empty, holds the pose the camera takes while the roster is
up: same position, swung 38° left onto open dune, and pitched to 2° — near level, against the
menu's own ~11.6° upward tilt. Levelling is the load-bearing part: it puts the horizon near the
middle of the frame, which lifts the astronauts' heads into sky and leaves the bottom band free
for the footer. The pose is saved on the way in and restored on the way out, the same shape as
`MenuScreen` switching the menu's canvases off and on again.

The rank stands 4.7 m in front of that pose, 0.3 m right of centre because the left of the frame
is the control column. Measured, not guessed: at 4 m the helmets were cropped by the top of the
frame with nowhere to put a nameplate, and at 5 m with 1.15 m spacing the four figures read as a
huddle with each shoulder occluding the next one's arm. All four framing numbers live as constants
in `LobbyPreviewSetup` next to the reasoning.

### Nameplates

Name labels are UI, positioned each `LateUpdate` by projecting each figure's `mixamorig:Head`
through the camera. Each is **two offset copies** — white over navy, three pixels apart. That is
not decoration: the menu's rule is that white reads on sky and navy reads on sand, and a
nameplate cannot pick one, because whether a given head is against sky or against a dune depends
on the framing. Two copies read against both, cost no per-label material instance the way a TMP
outline would, and put no box on a screen whose language has none. The host's carries a thin navy
underline rather than the word "host".

The `< Ember >` cycler sits 0.85 m under the local figure, projected the same way, built from
`MenuEntry` buttons so it carries the menu's hover animation and FMOD sounds — with the colour
name and its swatch chip as **separate objects**, because the button animator owns its own label's
colour and anything written there lasts one frame.

The chevrons are ASCII `<` and `>`, not `◀` and `▶`. The project's TMP default font is
LiberationSans SDF, which has neither U+25C0 nor U+25B6 and no fallback that does: TMP substitutes
U+25A1 and both arrows render as empty **boxes**. Caught from a warning during a capture, where the
control read as "□ Ember □".

Empty slots are absent, not ghosted: ghosting a 23-renderer skinned model needs a transparent
material swap for something that only means "room for one more". Slots stay fixed, so nobody
slides sideways when a player joins.

## 5. `LobbyRosterView` shrinks

The text roster list and its scroll view come out — the rank is the roster. The title drops to
~44pt. The status line renders only when it has something to say: a warning, or a joiner waiting on
the host. The `statusIsSticky` mechanism stays exactly as it is, since it exists precisely because
this page redraws twice a second.

Code, `Copy` and `Private off` sit in one small strip along the **top** of the page, at 24/34pt.
They were a stack down the left, which put controls in front of the astronauts. Three consequences
of moving them above the horizon:

- **They are white.** Navy is unreadable against sky, and a colour cannot simply be assigned: the
  button prefab's animator rewrites its own label's colour on every state change, so an assignment
  lasts one frame. `MenuEntry.MakeLight` switches the animator off and hands the colour to the
  Button's own tint block. That costs the hover scale-up and nothing else — `UIButton` plays both
  FMOD sounds from its own pointer handlers, not from animation events — and the tint block puts
  hover feedback back as a colour shift.
- **The two plain labels carry a drop shadow**, the same white-over-navy pair as the nameplates, so
  a head passing behind them cannot swallow the text.
- **Every slot is sized to its longest content, measured rather than guessed.** UIBuilder labels are
  built with word wrap off and Ellipsis overflow, so a slot narrower than its text silently
  TRUNCATES: `CODE` needs 74px, shipped in a 70px slot, and read as "CO…". `MenuField.Trailing`
  right-aligns the on/off state against the slot's right edge, so the only way to bring it nearer
  the word "Private" is to narrow the slot itself.

## 6. The main menu

`Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview`, following `WorldSelectSetup`: build the preview
prefab into `Resources`, add `SuitRecolor` to `PlayerCharacter`, destroy every root named
`__NomadPreview*`, create `LobbyCameraView` and then `LobbyPreviewAnchor` in front of it, save.

Neither placement object is moved once it exists, so a re-run cannot undo framing somebody has
composed by hand. Changing the authored framing therefore means deleting them and re-running.

The `MinigameButton` was also removed from the menu's ButtonRow. Only the entry: `StartMinigame`,
`LaunchMinigame`, `MinigameConfigUI` and the scene reference are all untouched, so the mode is
reachable again by putting one button back.

The four `__NomadPreview*` objects are removed **surgically** rather than by reverting the
scene, because the same uncommitted diff also contains menu button anchor moves that must
survive.

## 7. Verification

EditMode tests, in `Assets/Game/Editor/Tests/` beside `LobbySessionTests` and
`GameSettingsTests` — that folder, not `Assets/Game/Tests/EditMode/`, because the latter has an
asmdef and an asmdef cannot reference Assembly-CSharp:

- every material name in the derivation table exists on `astronaut_tobb.fbx`
- every swatch × relationship stays in gamut, with no clamping surprises
- `Apply` touches exactly the expected slot count and leaves the neutral slots alone
- `BuildPlayer` carries the suit key
- `SuitColors` survives a missing key, a garbage value, and an out-of-range index
- the default seed only ever lands on a vivid swatch
- `mixamorig:Head` exists on the preview prefab

Then the part tests cannot answer: framing. Verified by rendering the lobby's camera pose with four
recoloured figures staged in it and looking at the result — which is how the crowding, the cropped
helmets, the mannequin's arm across the fourth astronaut, the unreadable white-on-sand nameplate and
the boxed chevrons were all found. Staging for a capture must be saved to a **throwaway scene**
(`SaveScene(scene, tempPath)`), never to `MainMenu.unity`: saving probes into the real scene is what
put four astronauts in the main menu.

`MenuEntry.Horizon`'s claim that the menu camera sits at zero pitch was wrong — it is pitched about
11.6° — and that comment has been corrected in place, because the whole nameplate treatment depends
on not believing it.

What still needs a live session with more than one machine: that a colour picked by one peer arrives
on another's rank through the lobby poll, and that it survives the load into the world.

## Out of scope

Zones and per-part colours. A colour picker. A settings-page control. Ghosted empty slots. The
menu button anchor changes already sitting in the scene diff.
