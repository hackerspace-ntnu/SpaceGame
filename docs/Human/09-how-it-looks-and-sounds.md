# How it looks and sounds

Everything the player sees that is not the world itself — the menus, the helmet HUD, the black bars
of a cutscene, the wall of sand rolling in from the horizon — and everything they hear. This page is
for whoever is going to design a screen, stage a moment, or add a sound, and wants to know the shape
of what is already there before adding to it. The short version: the interface is drawn in code and
follows two strict colour languages, cutscenes are small local performances that never leave the
machine they play on, weather is a shared clock that every machine reads the same way, and sound is
asked for by meaning rather than by file.

---

## Two design languages, and no art files

There is no UI art in this project. No PNGs, no authored panels, no exported button states. Every
rounded rectangle, disc, chevron and panel edge is drawn into a texture at runtime and stretched to
fit. That sounds like a constraint and mostly it is a relief: nothing goes stale, nothing needs
re-exporting at a new resolution, and a widget is described in one place rather than split between a
script and a sprite atlas.

Two visual languages sit on top of that.

**Menu navy over the live 3D set.** The main menu is not a picture — it is a real scene with a real
camera looking at a real set, and the words sit on top of it. Menu type is dark navy, which reads
only against ground, never against sky. That gives the menu its one hard layout rule: everything
clickable lives below a horizon line at 540 in the 1920×1080 layout. Above that line, you are on your
own for contrast — white type, or the shadow trick the lobby nameplates use.

**Near-black panel with a blue accent.** This is the language for anything that opens *over*
gameplay: pause, chat, trade, the dev item browser, the loading screen. It is a panel, it is opaque
enough to read against a bright desert, and it does not pretend to be part of the world.

The in-world HUD is a third, smaller thing: it borrows the warm palette straight from the model
library's shared colour palette, so the hotbar and the world it sits over agree.

### The rules that surprise people

These are not preferences. Each one exists because the obvious version of it is broken here.

- **Never tint a label to show that something is selected.** The shared menu button animation drives
  the label colour and the root scale on every state change, so your tint survives about one frame.
  Say the state on a *different* object instead. The suit cycler puts the swatch name in its own
  object; the hotbar lifts and rings the selected slot rather than brightening it.
- **Never grey out a menu row by just switching the button off.** The shared button animation has an
  empty "disabled" clip, so the row freezes in whatever colour and scale it happened to be in — and
  with its raycasts off it never receives the pointer-exit, so it can freeze mid-hover. There is one
  sanctioned lock that fades the whole control group and disables interaction together.
- **No glyph spinners.** The project font has no arrows, no braille, no box-drawing characters. They
  render as nothing at all. In-flight feedback is a sweeping rule or animated trailing dots.
- **A stepper reports; it does not decide.** The `− 3 +` rows show only what someone told them to
  show after the fact, so a host can refuse a change and the number simply does not move.
- **A warning sticks.** The status line at the bottom of a menu page refuses a routine polled update
  while a warning is standing, because a twice-a-second redraw would otherwise erase a failure
  message before anyone could read it.

---

## What the screens actually are

**The front of the game** is a stack of menu pages over that one 3D set: the main menu, a
two-or-three-answer question page (story or versus, host or join), world select — the only place a
world is ever chosen, for single player and for multiplayer alike — versus team rules, and the
pre-match gamemode configuration for the arena minigame.

**The lobby** is the most elaborate screen in the game and worth looking at before designing
anything else. It swaps between a join page and a roster page, and the player list is not a list of
rows: it is a rank of actual astronauts standing in the real menu scene, one per slot, with
nameplates and clickable team headers projected onto them from a screen-space layer. The browser
list of open sessions refreshes on about a one-second budget; the roster polls twice a second.

**The HUD** appears for the local player only, and is a small cast of independent readouts: a health
bar, a crosshair, a four-slot hotbar, an interaction prompt that answers "what am I looking at and
what will the buttons do", a timed prompt after the crash landing telling you how to leave your
seat, and a death overlay.

**The helmet layer** is the visor HUD: corner marks that snap around whatever you are looking at,
a name and a prompt that unfold beside them, plus a danger vignette of two curved arcs that grow
with each hit from a direction and decay afterwards. It toggles on its own key, independently of
the rest of the HUD.

**World-anchored labels** are their own layer that survives the world streaming underneath it:
floating damage numbers for your own hits, names over other players (distance-faded and
occlusion-tested), and a world-space progress gauge on the repair workstation. NPC dialogue is a
scene-authored speech popup with a typewriter reveal, a hold, and an optional yes/no.

**The map** is a floating 3D hologram beside the player — one low-poly mesh per chunk you have
revealed. There is no 2D map screen.

**Overlays over gameplay** are pause (on **M**, not Escape), chat (**T**), the dev artifact browser
(**I**, in dev mode), the trader window, the loading screen, and the match result and scoreboard
screens.

### One owner of the cursor, the input, and time

All of the above shares a single arbiter. When any screen opens over gameplay it claims that
arbiter; when the last one closes, the claim is released. The first claimant frees the cursor, turns
off player input, look and movement (the camera keeps rendering), and optionally hides the HUD. The
last one out puts it all back and re-locks the cursor — unless the player is dead, in which case the
cursor stays free.

Two consequences worth knowing:

- **Time only freezes in a solo session**, and only if some open screen actually asked for it. Chat
  never asks. In a session with other people, pausing pauses nothing for anyone — including you. All
  the open/close animations run on unscaled time so they still animate on a frozen frame.
- **A gameplay overlay refuses to open when there is no local player.** That is how the pause menu
  knows not to appear over the main menu: there is no world to pause.

There is a related trap in the other direction. The key that *opens* a menu cannot live on the
player's own input bindings, because the arbiter switches those off while a menu is up — a screen
that opens on a key owns a private little set of bindings with only its UI actions live.

### The layering ladder

Screens are stacked by explicit sorting order and it is worth knowing the shape: the world-anchored
overlay sits *below* the HUD at −1, the HUD is 0, world interaction prompts 50, menu pages 900,
match result 1000, the scoreboard 1100, chat 1500, pause 2000, trade 2050, the dev browser 2100, and
the loading screen sits alone at 5000 above everything.

### What survives a quit

Only settings: player name, suit colour, five volume buses, mouse sensitivity, camera shake
intensity, invert look, invert hotbar scroll, dev mode, field of view, quality, fullscreen,
resolution and frame cap. Everything else in the interface is session-only — open pages, the HUD
toggles, the set of map chunks you have revealed. The chat log is a deliberate near-exception: it is
cleared when the networking for it goes away, but *not* by a scene change, so walking into an
interior does not empty your conversation.

---

## Cutscenes

A cutscene here is a component you drop on an object, not an asset and not a timeline. It is a
coroutine: you write the beats in order, with every duration, target and offset exposed so a designer
can retune them without touching code. There is no Timeline and no Cinemachine in this path.

Four things are kept deliberately separate, and none of them knows about the others: the **trigger**
(a click, walking into a volume, or code), the **cutscene** (what plays), the **director** (which
locks the player, shows the bars, and steps the coroutine), and the **aftermath** (an event that
fires when it ends). That separation is why a door, a pressure pad and a scripted story beat can all
reuse the same camera move.

The envelope around every cutscene is the same: player input, look, movement and damage feedback are
saved and switched off, black bars ease in over 0.4 s, the beats play, then the bars leave and every
saved flag is restored — or, if the player died mid-scene, the death freeze is re-asserted instead.
Only one cutscene runs at a time; a second request is refused rather than queued.

The worked examples are a camera pan to a target and back, a camera shake, a first-person glide
through a doorway, a third-person version that dollies a temporary camera while easing the body
through, and the crash landing that opens the game — black, a wake-up fade, a descent with a shake
curve, an impact hold, a blackout, and a fade back in.

### Things that bite

- **Cutscenes are local. Nothing about them goes on the wire.** A cutscene plays on the machine that
  triggered it, for that machine's player. Never move networked state inside one — the change lands
  on exactly one machine.
- **The arrival is the pattern to copy when a moment must be right for everyone.** It is split: the
  server flies the hull, every machine seats its *own* players, the cabin alert runs off replicated
  occupancy rather than the cutscene (so it lights up for someone merely watching a crewmate), and
  the local camera performance hangs off a purely local "this machine's player just sat down" event.
- **Camera writes are offsets, never assignments.** The player camera's authored resting pose is the
  head, roughly 1.45 m up and slightly forward — not the origin. Writing a shake straight into the
  local position drops the view to chest height, and "restoring" it by zeroing leaves it there for
  the rest of the session. Capture the pose when you take over, restore it when you let go.
- **Reading look input during a cutscene gets you a frozen camera**, because the input component
  zeroes its look axis on the way out. A cutscene that wants free look reads the raw look action
  directly. Leaving the normal player input enabled instead is worse: jump and dash arrive as events
  whose handlers fire whether or not movement is switched off.
- **One component writes the camera transform per frame.** Look and shake from two racing components
  lose one contribution on frames Unity orders differently.
- **Play-once is a saved fact.** A cutscene marked play-once records that it fired; it writes nothing
  at all until it has played, and a missing record on load explicitly means "not yet".
- **Camera shake is currently dead in most of the game.** The vendored shake system needs a shaker
  component alive in the scene, and the project's only one sits on a camera prefab that nothing
  references. Damage feedback, several artifacts and the flung-body effect therefore shake nothing
  today. Separately, the accessibility shake-intensity setting is only honoured by our own shake
  maths, not by the vendor path — so even when the vendor path is revived it will ignore the slider
  until someone wires it.
- **There is no audio ducking during cutscenes.** Worth knowing before designing a quiet moment.
- **Impact effects should emit from one particle system, not spawn thirty objects.** The established
  approach moves a single always-simulating system to each hit point and emits there. Four conditions
  have to hold at once for that to work, and all four are non-obvious — read the effect that already
  does it rather than re-deriving them.

---

## Weather, fog, and the look of the sky

The whole environment layer rests on a single idea: **everything is a pure function of a clock and an
anchor.** Where a storm is, how hard it is blowing, how the fog drifts and churns, where the sun sits
— all recomputed from a shared clock reading rather than sent frame by frame. There is no per-frame
weather traffic at all.

### Sandstorms

A storm is born on the server as a record of about 30 bytes — an id, which kind of storm it is, a
seed, an origin, a bearing, a start time and a duration — written once and never touched again. Every
machine mirrors that record and derives the rest: the centre drifts from the origin along the
heading with a wander, the intensity follows a curve over its life with gusts on top. A player who
joins late gets the list plus the anchor and lands in *identical* weather.

The important design property is that **one shape function drives both the damage and the pixels.**
The maths that decides how dense the sand is at a point is written once in code and mirrored in the
shader, so what hurts you is what you can see. Damage is ticked on the server only, from an exposure
figure that multiplies storm density by how sheltered you are and by whatever protective gear you
are wearing — a shelter is a box you stand in, not a trigger volume, and gear contributes a single
number.

There are three visual layers: a fullscreen interior effect once you are inside the storm, a closed
shell drawn around each storm so you can see the wall from outside, and near-camera grit driven by
one float. The renderer skips the whole thing entirely when the camera is not in any storm.

Sandstorms are also the one place with a deliberate audio exception, described below.

Weather time and sky time are two separate anchors on purpose, so restoring a saved storm cannot
swing the sun.

### Volumetric fog and clouds

Fog is authored, not simulated: you drop a volume of air into a scene, pick its shape — ellipsoid,
box, cylinder or ground layer — and tune its look, detail and motion. Fog volumes and cloud layers
hold **zero** runtime state, which is why they need no netcode and no save support whatsoever.

The renderer takes the eight nearest volumes and eight nearest lights and marches all of them in one
pass, so overlapping volumes mix correctly instead of drawing over one another. That march happens
at reduced resolution and is then composited back up with a depth-aware 3×3 upsample. A few
constraints fall out of that and they are the ones that trip people:

- Clouds draw after the skybox; fog and sandstorm draw before transparents. Fog drawn *after*
  transparents would paint over every particle and pane of glass.
- The eight-volume budget is a hard number shared between the code and the shader; raising it means
  raising both and paying linear cost.
- There is a gallery scene you can walk into to check a fog look, and you should — every claim about
  a volumetric effect is really a claim about a viewing angle.

The desert skybox carries painted dust bands that stand down as the volumetric clouds fade in, so the
two never fight. The helmet visor has its own lens warp and chromatic aberration effect, play-mode
only.

One honest note on platform: the PC renderer runs clouds, sandstorm, fog and ambient occlusion. The
mobile renderer has only fog and clouds — no sandstorm, no visor distortion — and no quality level
actually references the mobile settings, so it is effectively an orphan today.

---

## Sound

Sound in this game is **asked for by meaning and played locally by every machine.** Both halves of
that sentence carry weight.

### Asked for by meaning

Code never names an audio file. It says "play the player-jump sound at this position", and a single
catalog asset decides which event that maps to, how often it may retrigger, how far away it can still
be heard, and how loud it is. That indirection is the whole design: the vocabulary of sounds the game
knows about is a fixed list of **71 named sounds**, grouped in hundreds — player, weapons, impacts,
creatures and NPCs, interaction, wings, ship and vehicle, ambience, UI, portals.

FMOD is the actual playback engine underneath, and it is used rather than replaced. But it is worth
being blunt about the current state: **those 71 named sounds resolve to only 18 distinct FMOD
events** (19 counting the one music track), so a lot of the game is currently sharing. One impact
event does the work of nine slots; an electric hum does another nine; a UI "no" covers eight. Every
entry carries a note, and stand-ins are marked as stand-ins there.

The reason it is like that is the one thing everyone needs to know before planning audio work: **the
FMOD Studio project that built these banks is not in the repository.** What ships is the compiled
banks. Until a project file exists again, no new event can be authored — new sounds can only be new
names pointing at existing events. There is a manifest listing exactly what the banks contain; read
it before assuming a sound exists.

Volumes are five buses — master, music, SFX, UI, ambience — pushed onto FMOD from the settings menu
and stored with the other preferences. No audio state is in world saves.

### Played locally by every machine

**Sound is never replicated.** There is no audio message anywhere in the networking layer. Each
machine plays its own sounds off state and events it already has, which means the only real rule is:
*put the play call on a code path that runs on every machine.* Server-only code makes no sound for
anybody else.

Three routes exist, in rough order of preference:

- **Off something already replicated** — a health event, a repair feedback call. The sound fires
  everywhere, and only on genuine success.
- **In the presentation half of a usable item.** Items are split into a "do the thing" half that runs
  on the authority and a "show the thing" half that runs everywhere; the sound belongs in the second.
- **Immediately and locally**, accepting a small lie. Picking an item up clicks *before* the request
  goes to the server, because a round trip would make the feedback feel late. A refused pickup still
  clicks. That is a deliberate trade.

A projectile impact deliberately makes noise on every peer even though only one of them bills the
damage: everyone holds a copy of the shot, everyone should hear it land.

### Things that bite

- **Nothing in the audio layer throws.** A missing catalog, a missing bank, an uninitialised sound
  engine — all of it warns once and then goes quiet. Silence is the normal failure mode.
- **It warns once per sound, forever.** Fix a mapping in the middle of a session and it stays silent
  until the caches are reset (which happens automatically when you re-enter play mode).
- **A one-shot is fire-and-forget; anything sustained must be owned.** A loop belongs to an emitter
  that can stop it, and it must be stopped in *both* the disable and the destroy path — scene unload
  and despawn are different exits, and a loop that only handles one of them keeps playing after its
  object is gone.
- **Rate limiting is per source, not per sound.** One chatty creature throttles itself without muting
  every other creature of the same kind. The 2D path (UI, HUD, dialogue) is the exception: all 2D
  sounds of the same name share one bucket, which is right for UI and wrong if you wanted per-widget
  limiting.
- **An inspector override picks the asset, not the tuning.** Roughly 37 places in prefabs and scenes
  still point at a specific event directly; the cooldown, cull distance and volume for that slot
  still come from the catalog.
- **Distance culling needs a listener.** With no listener in the scene the cull is skipped, not
  forced — everything plays.
- **The sandstorm's own noise is a plain Unity audio source with a low-pass filter, on purpose.** It
  is a 2D continuous loop driven by one number, and doing it properly would need the missing FMOD
  project. It is the documented exception, not a pattern to copy.
- Event references bind by unique id, so recreating an event in a future FMOD project would silently
  break both the catalog and those ~37 inspector assignments. If that day comes, switch the binding
  to path-based *first*.

---

## Where this lives

The dense, implementation-level versions of everything above, for when you need the actual types and
call sites:

- `docs/AI/systems/UI.md` — every screen, widget, the sorting ladder, the input/cursor/time arbiter,
  and the full list of menu rules.
- `docs/AI/systems/Cutscenes.md` — the cutscene base and director, the shared presentation helpers
  (letterbox, shake maths, cloth wind, placement tint), and the arrival split.
- `docs/AI/systems/CutsceneExamples.md` — the example prefabs to drag into a scene (note: some names
  there are stale).
- `docs/AI/systems/audio.md` — the sound vocabulary, the catalog, the FMOD situation, and the
  multiplayer routes.
- `docs/AI/systems/Environment.md` — sandstorms, volumetric fog and clouds, sky, and the URP render
  features that draw them.
- `docs/AI/systems/audio-prefab-inventory.md` — a generated per-prefab sweep of what makes noise.
