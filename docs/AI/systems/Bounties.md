---
system: Bounties
layer: world
summary: "Bounty hunts: one per crew, one owner, a cast at a site; failing starts it over, only Claimed ends it"
paths:
  - Assets/Game/Scripts/World/Bounties/
  - Assets/Game/Scripts/World/Bounties/BountyHunt.cs
  - Assets/Game/Scripts/World/Bounties/SettlementBounties.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntSite.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntSpot.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntRun.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntRunState.cs
  - Assets/Game/Scripts/World/Bounties/BountyNoticeBoard.cs
  - Assets/Game/Prefabs/Environment/Decorations/Civic/Deco_NoticeBoard.prefab
  - Assets/Game/Editor/Posters/WallPosterSpots.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/BountySaveable.cs
  - Assets/Game/Scripts/Presentation/UI/Pages/IPosterOffer.cs
  - Assets/Game/Editor/Posters/BountyHuntRecorder.cs
  - Assets/Game/Editor/Tests/BountyTests.cs
  - Assets/Game/Resources/Bounties/
symptoms:
  - "reading a wanted poster offers no bounty, or Y does nothing"
  - "the poster says Not yet"
  - "the town's notice board shows no wanted poster"
  - "a building has no wanted poster on its walls"
  - "Stamp Settlement Identity or Bake Site Catalog skips the Spillexo settlement"
  - "a hunt can be taken or handed in at the wrong settlement"
  - "residents never say anything about the bounty when I talk to them"
  - "every resident gives the same tip"
  - "the visor shows no search area after a tip"
  - "the bounty target or its guards never appear at the site"
  - "the hunt's people stand around instead of keeping to their posts"
  - "a guard on a post never plays its spot's pose on a client"
  - "hunt guards turned the town against me"
  - "the target keeps fighting after I knocked it down"
  - "the visor says bring the target back right after taking the hunt"
  - "the target's body disappeared before I could drag it home"
  - "I brought the target into town and nobody came"
  - "a second target appeared after loading the world"
  - "an abandoned hunt's people came back"
  - "the hunt started over while I was at my ship"
  - "the hunt started over when the whole crew died"
  - "after a failure the guards stand twice at the site"
  - "I killed the target and it came back to life at its post"
  - "the target fell through the ground and the hunt could never be finished"
  - "the hunt started over while the captive was in a cave or my ship"
  - "a hunt claimed before the update is offered again"
  - "a hunt that expired in an old save is offered again"
  - "[Bounties] ... spawned without a save identity"
  - "[Bounties] ... owns hunts but has no settlement id"
reads_with: [WantedPosters, Residents, ResidentReputation, Objectives, Multiplayer, Persistence, Combat, LeashSystem, AgentSystem, Expeditions]
updated: 2026-10-20
---

# Bounties

A bounty **hunt** is offered by the wanted posters of the ONE settlement that owns it. The crew take one at a time (Y
while reading a poster). Its residents answer "where is he?" with a tip; the first puts a search area on the visor. At
the hunt's **site** its **cast** waits: the target and its guards, each keeping to a spot. Beaten, the target surrenders
and follows the nearest player. Brought into the owner's bounds, a guard or elder thanks the bringer and drops the
reward: **Claimed**, over for good, its posters stamped. Doing it wrong **fails** the hunt (the target killed when wanted
alive or fallen beneath the ground, the cast standing 60 min, every player outside its bounds 2 min) and it **starts over in place**: still open,
the tip kept, its whole cast despawned and stood afresh at their spots, the visor saying why. The whole crew going down
is **no** failure: the hunt carries on as it stands.

## Model

| Idea | Mechanism |
|---|---|
| A hunt is one asset | `BountyHunt` in `Resources/Bounties/`: `poster` (target prefab, name, wanted state), `tips`, `rewards`, `otherStateShare`, `cast`, `boundsRadius` (default 1500 m). `id` made once in `OnValidate`. The site (`hasSite`, `sitePosition`, `siteScene`) and `ownerSettlementId` are **written by the recorder**, never typed |
| Every machine agrees on the list | `BountyCatalog`: `Resources.LoadAll`, sorted by id. The wire names a hunt by place, saves by id. No cap |
| A poster finds its hunt by what it prints | `BountyCatalog.For(poster)`: same `Agent` and `DisplayName` |
| One owner, never the nearest | `SettlementBounties` on a `Settlement`: `entries {hunt, offeredWhen Always/AfterClaimed/Goodwill, afterClaimed, minGoodwill (a GoodwillBand)}`, `townFaction` for Goodwill. `Owner(hunt)` searches loaded instances; `Offers(hunt, board, reader)` answers the same on every machine (bands come from the client's goodwill mirror). Town = owner's position, radius = `GeneratedExtent` or `handInRadius` |
| The cast | `CastEntry {who Target/Prefab/RosterRole, prefab, roster, role, minCount..maxCount, spotTag, faction, spawnWhen OnTaken/OnFirstTip}`. Exactly one Target (the poster's agent). Every entry defaults to OutlawFaction (`OnValidate`), so a fight with them is never a fight with the town |
| Where they stand | `BountyHuntSite` (chunk scenery) and its child `BountyHuntSpot`s: `spotTag`, `use` (a `SpotUse`), `mode Post/Patrol/Wander`, `wanderRadius`. Entries take the spots of their tag in hierarchy order; extras scatter round the last one |
| Shared state | `BountyProgress` (NetworkVariable): `Open` (place + 1), `Phase` (Asking → Located → Caught), `Town`, `TownRadius`, `Site`, `Target`, `TargetDead`, `OutsideFor` (whole seconds), `Resets` (wrapping byte), `Failure` (`BountyFailure` None/OutOfTime/OutOfBounds/TargetKilled/TargetLost: why it last started over) |
| The only end | Claimed catalog places on the board (`Claimed`, `IsClaimed`), a `NetworkList<int>`; never offered again. No other closure exists |
| What only the server knows | `BountyHuntRunState`: `spawned` / `tipCastSpawned` (each wave stood), `pieces [{body SaveRef, target, spot}]`, `elapsed` and `outsideFor` (count only while the cast stands), `pendingDespawn`. `spot` = `BountyRules.SpotKey` `tag#place` (index among the site's spots of that tag, hierarchy order); empty = scattered. Run by `BountyHuntRun`, a plain class the board owns |
| Tips are resident speech | `Topic.Whereabouts` rows wrap `{tip}`; `BountyRules.TipIndex(id, resident.index)` fixes each resident's tip; `SpeechTokens` fills `{tip}` then `{target}` on every machine |
| Hand-in and pay | Claimant (`claimantArchetypes`, else nearest resident) walks over, `Say(Remark, BroughtIn*)`, `Gossip.Witness`, rewards via `ItemDropService`; `BountyRules.Payout` pays in full as wanted, else `floor(count × otherStateShare)` (only a wanted-dead target brought alive: killing a wanted-alive one fails) |

## Key types

| Type | Role |
|---|---|
| `BountyBoard` | Take, tips, `CurrentFailure` → `StartOver`, hand-in, replication hooks, `Restore`. Every machine: `RequestTake`, `IsClaimed`, `TipFor`, `TryGetTargetPosition`, the "Bounty failed" message. Tunables `maxOpenSeconds` 3600 (of cast standing), `outOfBoundsSeconds` 120, `lostBelowGround` 2 m, `lostBelowY` −500, `builtFloorReach` 1.7 m, `Stamp` |
| `BountyHuntRun` | Server: spawns the cast, sends pieces to their spots, the clock and the bounds (`HasStood` gates both), cleanup on close |
| `SettlementBounties` | The owner and its offer conditions |
| `BountyHuntSite` / `BountyHuntSpot` | Scenery markers. The site's `Update` asks `WantsCastAt` → `SpawnCast` (server). The spot holds its `use` cue on whoever idles on it, on every machine |
| `BountyNetwork` | Beside `ObjectiveNetwork`: `NetworkVariable<BountyProgress>`, `NetworkList<int>` (claimed), `TakeRpc` |
| `BountySaveable` / `BountyHuntRecorder` (editor) | Global saver, key `bounties` / `sceneSaved` → `RecordSite` per site, `RecordOwner(SettlementId)` per owned hunt |
| `BountyNoticeBoard` | On `Deco_NoticeBoard` (in `NomadMarketPlaza`, three in `NomadShrinePlaza`) and on a building's `WallPosters` child (slots on its outer walls): every `refreshSeconds` hangs its `Settlement`'s posters in `slots` — the open hunt, then offers newest first (later in `entries`), then claimed ones most recent first. One hunt = one poster per board, in slot 1. Local, nothing saved. Wall slots come from `WallPosterSpots` (editor, *Tools ▸ SpaceGame ▸ Posters ▸ Add Wall Poster Spots (Selected Buildings)*): up to 3 per prefab on its `Model` renderers — upright, flat over a whole sheet, one non-cloth material, a stride clear in front, ≥ 3 m from every `SettlementEntrance`, ≥ 5 m apart, centred 2.0–2.6 m up — nearest the first entrance first. Adds only |
| `BountyPosters` | Prompt, in order: claimed → nothing; this hunt open → taken; another open → finish it; owner does not offer it (or is not loaded) → `NOT YET`; else `Y: TAKE THIS BOUNTY`. Stamp object `BountyStamp` (CLAIMED), shown or hidden on `RestampAll` |
| `IPosterOffer` / `PosterViewerUI` | The offer read by the full-screen viewer: `Prompt`, `Stamp`, `StampColour`, `CanTake`, `Take` |
| `BountyObjective` | Visor: search area round `progress.Site`, then the target; while `OutsideFor > 0` "Out of bounds: return within N s" and the town as waypoint. `FailedLine(failure)`: "Bounty failed: … The hunt starts over" |
| `BountyTarget` | Added at runtime to the target; holds off `Remains`, surrenders to `captive.faction`, follows. `Release(ledTo, …)` on hand-in leads it into town |
| `BountyRules` | Pure: tip index, search centre, payout, `CanTake`, `CastSeed`/`CastCount`, `SpotKey`/`TryReadSpotKey`, `FlatWithin`, `AllOutside`, `KilledWhenWantedAlive`, `FailureOf` (precedence TargetLost, TargetKilled, OutOfTime, OutOfBounds) |

## Flows

1. **Take.** Poster → `RequestTake` → host `Take`, client `TakeRpc`. Server checks: none open, not claimed, the owner
   is loaded and `Offers` it to the taker, a poster within `takeRange`. Sets town, radius and `progress.Site`.
2. **Ask.** A resident of the owner gives a Whereabouts line → `TipHeard` → Located.
3. **Stand the cast.** Two waves: OnTaken once the site's chunk is loaded on the server, OnFirstTip once Phase ≥
   Located too. Each wave's flag is set **first**, then per entry a count from `CastCount(CastSeed(WorldId, hunt.Id),
   entry index)` (Target always 1), a prefab (`FactionRoster.Deal`, one deck per roster+role across both waves),
   spots by tag, `World.Spawn`, `SetKeepChunksLoaded(false)`, the entry's faction, a `HuntPiece`; the target gets `BountyTarget`.
4. **Posts.** Server writes each piece's `AgentGoal` with reason "bounty post" (hold + face the spot's forward),
   "bounty patrol" or "bounty wander", rewriting only when the goal is not its own; skipped while dead, held, down or
   fighting, so it walks back after a fight. A scattered piece gets one hold goal. Pieces re-found by `SaveRef`.
5. **Clock, bounds, failure.** Once the cast stands: `elapsed` grows; `outsideFor` grows while **all** `SessionPlayers`
   are beyond `boundsRadius` of the town (flat), else resets (also with nobody connected; published floored, not
   "live"). `CurrentFailure` each think: the target `BeneathTheWorld` (`UnderTerrainRule` past `lostBelowGround`
   under `TerrainProbe`'s height, or below `lostBelowY` with none; never while parented, `CarriedBody.IsHeld` or
   `SpawnClearance.StandsOnStructure`), the target dead while wanted alive, or either limit (a crew wipe is not one). Any → `StartOver`: `WithdrawPieces` (all, dead and caught
   too, → `pendingDespawn`), `FreshRun`, `DespawnPending` at once, claim walk dropped; Caught → Located, `Target` 0,
   `OutsideFor` 0, `Resets++`, `Failure` set, live. The site restands both due waves — the same people `CastSeed`
   deals. Every machine seeing `Resets` change live posts `FailedLine` (id `bounty.failed`) and re-announces the objective.
6. **Hand in, close.** As before; the place joins `Claimed`. The target is led to the town's `WalkableHeart`; loaded
   others → `Remains.BeginUnlessCounting(takenAwaySeconds)`; unloaded → `pendingDespawn`, despawned via
   `GameServices.World.Despawn` as they resolve (dropped once the world record lacks the id; every think, hunt open or
   not). A restored run whose hunt left the catalog goes to `pendingDespawn` too.

## Multiplayer

| What | Carrier | Authority |
|---|---|---|
| Progress (failure and resets included) | `BountyNetwork` NetworkVariable | Server |
| Claimed list | `BountyNetwork` `NetworkList<int>`, read on spawn by late joiners | Server |
| Take | `TakeRpc` | Client asks; server checks owner, offer, poster distance |
| Tips, thanks, gossip | `ResidentSaid` (line id) | Server picks, every machine resolves tokens |
| Cast, rewards | Registered network prefabs via `World.Spawn` / `ItemDropService` | Server |
| Spot cues | none: each machine holds the cue on the agent it sees idle on the spot | Local |
| Visor, failure message, poster prompt, stamps | none | Local, derived |

## Persistence

`BountySaveable` (key `bounties`, on `Objectives` in persistentScene), written only while a hunt is open, one has been
claimed, or `pendingDespawn` is non-empty:

```json
{"open":"<id>","phase":"Located","town":{"x":0,"y":0,"z":0},"townRadius":60.0,"hasSite":true,"site":{"x":0,"y":0,"z":0},
 "run":{"spawned":true,"tipCastSpawned":false,"pieces":[{"body":{"kind":"entity","id":"…"},"target":true,"spot":"hideout#0"}],
        "elapsed":12.5,"outsideFor":0.0,"pendingDespawn":[{"kind":"entity","id":"…"}]},
 "claimed":["<huntId>"]}
```

`run` is the whole `BountyHuntRunState`; `hasSite` because the serializer has no nullable-Vector3 converter.
`Resets`/`Failure` are not saved. **Old saves:** the first format (`claimed` — the same field — plus `target`,
`targetSpawned`, `settlementId`) loads one Target piece with `spawned = targetSpawned`, the asset's site. The
2026-10-14 format (`closed: [{id, closure}]`) keeps only `"Claimed"`: an Expired hunt is offered again. An id no longer
in the catalog is dropped with a warning. Round trips, old formats included, in `BountyTests`.

## Gotchas

- **The owner, never the nearest settlement.** An Outpost is a `Settlement` too. `IsTown` matches `ownerSettlementId`
  only; an empty id (Stamp Settlement Identity never run) means it can never be taken (the recorder logs an error).
- **Owner and site are known only after their scenes are saved** (`BountyHuntRecorder`); two owners: last saved wins.
- **The site must lie inside `boundsRadius` of the owner**, or reaching it fails the hunt. The test reads the owner
  from the baked site catalogs (`towns`): re-bake after moving a settlement.
- **Cue holding is local on every machine**: one idle, standing, non-`Resident` agent within `holdRadius`, by sibling
  index. A combat target is server-only, so on a client a guard shooting without moving may keep its loop.
- **Cast prefabs must have no goal writer of their own** (`NpcTaskModule`, a resident routine): it fights the posts.
- **Spot keys are positional**: reordering spots under a site while a hunt is open moves pieces after a load.
- **Never hand-place a hunt's poster for the owning town**: its `BountyNoticeBoard`s (notice boards and every building's `WallPosters`, under the `Settlement`) hang it. Listing the hunt in `SettlementBounties.entries` is the whole step. **Wall spots live on the building PREFABS**, so every town built from them pastes its own hunts on its walls, Story towns included (none without `SettlementBounties`). 15 nomad buildings carry 1–3 (2026-10-20); open yards, tents and plazas found none; a prefab with a board is skipped and a re-run never moves a spot (delete `WallPosters` to search again). Search renderers, never colliders: `Collision` is hulls and ramps standing proud of the painted wall.
- **A single-scene world (Spillexo) reaches the editor tools through its `GameWorld`**: `WorldChunkScenes.ScenePaths` returns a chunk-less config's one scene, so *Stamp Settlement Identity*, *Bake Site Catalog* and `EveryHuntsSite_IsInsideItsOwnersBounds` see `NoiseTerrain`. Before 2026-10-20 they read no scene: the settlement had no id and its hunt could never be taken.
- **`BountyTarget.Release` disables before `Destroy(this)`**, or that frame's `Update` stops the `Remains` countdown.
- **`pendingDespawn` outlives the hunt** (`FreshRun` carries it): dropping it brings a restarted or claimed hunt's
  people back when their chunk loads.
- **`StartOver` despawns before the site restands** (`DespawnPending` in the same call), or the old cast stands beside
  the new. Unloaded old pieces may overlap the new cast for one think when their chunk loads, far from any player.
- **The clock only runs once the cast stands.** Otherwise an untouched hunt fails every hour, one whose site loads late
  fails as its cast appears, and a crew at their ship fail again every 2 min. After a restart nothing counts until the
  site loads again.
- **"Fallen beneath the ground" reuses the under-terrain guard's rule and exclusions**: cave floors and cargo decks lie
  under the heightmap on purpose. Only the target is checked. A target with an `UnderTerrainGuard` is lifted at 0.5 m,
  before the 2 m failure; with no terrain loaded under it, only below −500 counts.
- **A crew wipe is deliberately not a failure** (user, 2026-10-15): the hunt carries on as it stands. Never add it back as
  a per-think level check: the site stays loaded round the bodies and would restart the hunt every half second.
- **Each wave's flag is set before its first spawn**, so a throw part-way never stands it twice; on load they stop a second cast.
- **Never a `RosterRole.Rider` entry**: a Rider member is the MOUNT carrying an `NpcPassenger`, not a person.
- **Every entry, the Target included, takes its faction (Outlaw).** Surrender IS switching to `captive.faction`
  (Drifters); a Raxy target in its own faction was Caught on arrival.
- **`BountyTarget` is never on a prefab**: `HuntPiece.target` re-adds it after a load. `World.Spawn`, never `NpcSpawn`.
- **The wire names a hunt by catalog place; saves by id.** Adding a hunt can shift places.
- **`{tip}` is replaced before the token loop**, so a tip may contain `{target}`; thanks, gossip and rumour rows must
  not (the hunt is closed before clients resolve them). `Favor.IsHarm` is an explicit list: handing in is kind.
- **The corpse drag is the rope's job (LeashSystem).** The bounty only keeps the body (`Remains` held off), saved.
- **Triggers and actions (Phase 2) must be stateless**: state lives in the run state or the entities, or a reload loses it.
- **Verified:** 2026-10-14 offline host Play (board hangs the poster, take, post, surrender, hand-in + pay, stamps, save file); 2026-10-15 23/23 `BountyTests`; 2026-10-20 offline host Play in `NoiseTerrain` (all 19 boards — market, 3 shrine, 15 walls — hang one `Bounty_NimQuickhands` poster; taken from a shrine board; the target stands at the site as Outlaw) and 23/23 `BountyTests`. **Not run:** a failure → start over in Play, a client, a reload, `pendingDespawn`, Nim's tips and OnFirstTip guards.

## Extending

1. **A hunt:** bake its poster ([WantedPosters.md](WantedPosters.md)), then *Create ▸ SpaceGame ▸ Bounty Hunt* in
   `Resources/Bounties/`: poster, 2+ tips, rewards, a cast with one Target entry; guards are registered network prefabs
   with a `SaveableEntity`.
2. **Its site and owner:** a `BountyHuntSite` in a chunk scene (or the single world scene) with `BountyHuntSpot` children tagged as the cast's `spotTag`s; an entry in the owner's `SettlementBounties`. Save both scenes; re-bake the site catalog for a new settlement. The town's boards and walls hang the posters. Example: `Bounty_NimQuickhands`, owned by `NoiseTerrain/settlement`, site at (1906, 53, 1038).
3. **Wall spots for a new building:** select its prefab, *Add Wall Poster Spots (Selected Buildings)*; move an odd slot by hand. A board baked into a model is stripped and replaced by `Deco_NoticeBoard`s, as the shrine plaza's three were ([ArtPipeline.md](ArtPipeline.md)).
4. **More voices:** `Whereabouts` rows or `Remark × BroughtIn*` rows in the culture's lines.
5. **Tuning:** the `BountyBoard` on `persistentScene/Objectives`; `boundsRadius` per hunt.
6. **A new way to fail:** append a `BountyFailure` (on the wire), a flag in `FailureOf`'s precedence, its check in
   `CurrentFailure` (never re-firing on every think), its `FailedLine`.
