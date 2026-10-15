---
system: Bounties
layer: world
summary: "Bounty hunts: one per crew, owned by one settlement, a cast at a site; ends Claimed or Expired"
paths:
  - Assets/Game/Scripts/World/Bounties/
  - Assets/Game/Scripts/World/Bounties/BountyHunt.cs
  - Assets/Game/Scripts/World/Bounties/SettlementBounties.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntSite.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntSpot.cs
  - Assets/Game/Scripts/World/Bounties/BountyClosure.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntRun.cs
  - Assets/Game/Scripts/World/Bounties/BountyHuntRunState.cs
  - Assets/Game/Scripts/World/Bounties/BountyNoticeBoard.cs
  - Assets/Game/Prefabs/Environment/Decorations/Civic/Deco_NoticeBoard.prefab
  - Assets/Game/Scripts/Core/Persistence/Adapters/BountySaveable.cs
  - Assets/Game/Scripts/Presentation/UI/Pages/IPosterOffer.cs
  - Assets/Game/Editor/Posters/BountyHuntRecorder.cs
  - Assets/Game/Editor/Tests/BountyTests.cs
  - Assets/Game/Resources/Bounties/
symptoms:
  - "reading a wanted poster offers no bounty, or Y does nothing"
  - "the poster says Not yet"
  - "the town's notice board shows no wanted poster"
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
  - "the hunt ended while I was at my ship"
  - "every poster of a hunt is stamped EXPIRED"
  - "a hunt claimed before the update is offered again"
  - "[Bounties] ... spawned without a save identity"
  - "[Bounties] ... owns hunts but has no settlement id"
reads_with: [WantedPosters, Residents, ResidentReputation, Objectives, Multiplayer, Persistence, Combat, LeashSystem, AgentSystem]
updated: 2026-10-14
---

# Bounties

A bounty **hunt** is offered by the wanted posters of the ONE settlement that owns it. The crew take one at a time (Y
while reading a poster). While it is open, that settlement's residents answer "where is he?" with a tip; the first tip
puts a search area on the visor. At the hunt's **site** its **cast** waits: the target, and whoever guards it, each
keeping to a spot. Beaten, the target surrenders and follows the nearest player; killed, its body stays until handed in.
Brought into the owner's bounds, a guard or elder thanks whoever brought it, the deed spreads as favor, and the reward
drops at the claimant's feet: **Claimed**. A hunt left open 60 minutes, or with every player outside its bounds for two
minutes, is **Expired**: its people are taken away. Either way it is over for good, and its posters are stamped.

## Model

| Idea | Mechanism |
|---|---|
| A hunt is one asset | `BountyHunt` in `Resources/Bounties/`: `poster` (target prefab, name, wanted state), `tips`, `rewards`, `otherStateShare`, `cast`, `boundsRadius` (default 1500 m). `id` made once in `OnValidate`. The site (`hasSite`, `sitePosition`, `siteScene`) and `ownerSettlementId` are **written by the recorder**, never typed |
| Every machine agrees on the list | `BountyCatalog`: `Resources.LoadAll`, sorted by id. The wire names a hunt by place, saves by id. No cap |
| A poster finds its hunt by what it prints | `BountyCatalog.For(poster)`: same `Agent` and `DisplayName` |
| One owner, never the nearest | `SettlementBounties` on a `Settlement`: `entries {hunt, offeredWhen Always/AfterClaimed/Goodwill, afterClaimed, minGoodwill (a GoodwillBand)}`, `townFaction` for Goodwill. `Owner(hunt)` searches loaded instances; `Offers(hunt, board, reader)` answers the same on every machine (bands come from the client's goodwill mirror). Town = owner's position, radius = `GeneratedExtent` or `handInRadius` |
| The cast | `CastEntry {who Target/Prefab/RosterRole, prefab, roster, role, minCount..maxCount, spotTag, faction, spawnWhen OnTaken/OnFirstTip}`. Exactly one Target (the poster's agent). Every entry defaults to OutlawFaction (`OnValidate`), so a fight with them is never a fight with the town |
| Where they stand | `BountyHuntSite` (chunk scenery) and its child `BountyHuntSpot`s: `spotTag`, `use` (a `SpotUse`), `mode Post/Patrol/Wander`, `wanderRadius`. Entries take the spots of their tag in hierarchy order; extras scatter round the last one |
| Shared state | `BountyProgress` (NetworkVariable): `Open` (place + 1), `Phase` (Asking → Located → Caught), `Town`, `TownRadius`, `Site`, `Target`, `TargetDead`, `OutsideFor` (whole seconds) |
| How hunts ended | `ClosedHunt {place, closure Claimed/Expired}` list on the board, replicated as a `NetworkList`. `IsClosed(place, out closure)`; a closed hunt is never offered again |
| What only the server knows | `BountyHuntRunState`: `spawned` (OnTaken wave stood), `tipCastSpawned` (OnFirstTip wave stood), `pieces [{body SaveRef, target, spot}]`, `elapsed`, `outsideFor`, `pendingDespawn`. `spot` is `BountyRules.SpotKey` = `tag#place`, place = index among the site's spots of that tag in hierarchy order; empty = scattered. Run by `BountyHuntRun`, a plain class the board owns |
| Tips are resident speech | `Topic.Whereabouts` rows wrap `{tip}`; `BountyRules.TipIndex(id, resident.index)` fixes each resident's tip; `SpeechTokens` fills `{tip}` then `{target}` on every machine |
| Hand-in and pay | Unchanged: claimant (`claimantArchetypes`, else nearest resident) walks over, `Say(Remark, BroughtIn*)`, `Gossip.Witness`, rewards via `ItemDropService`; `BountyRules.Payout` pays in full as wanted, else `floor(count × otherStateShare)` |

## Key types

| Type | Role |
|---|---|
| `BountyBoard` | Take, tips, hand-in, replication hooks, `Expire`, `Restore`. Every machine: `RequestTake`, `IsClosed`, `TipFor`, `TryGetTargetPosition`. Tunables `maxOpenSeconds` 3600, `outOfBoundsSeconds` 120, `Stamp` |
| `BountyHuntRun` | Server: spawns the cast, sends pieces to their spots, the clock and the bounds, cleanup on close |
| `SettlementBounties` | The owner and its offer conditions |
| `BountyHuntSite` / `BountyHuntSpot` | Scenery markers. The site's `Update` asks `WantsCastAt` → `SpawnCast` (server). The spot holds its `use` cue on whoever idles on it, on every machine |
| `BountyNetwork` | Beside `ObjectiveNetwork`: `NetworkVariable<BountyProgress>`, `NetworkList<ClosedHunt>`, `TakeRpc` |
| `BountySaveable` | Global saver, key `bounties` |
| `BountyHuntRecorder` (editor) | `EditorSceneManager.sceneSaved` → `RecordSite` per site, `RecordOwner(SettlementId)` per owned hunt |
| `BountyNoticeBoard` | On `Deco_NoticeBoard` (in `NomadMarketPlaza`): every `refreshSeconds` hangs its `Settlement`'s posters in `slots` — the open hunt, then offers newest first (later in `entries`), then ended ones most recent first. Every machine, local, nothing saved |
| `BountyPosters` | Poster prompt, checked in order: ended → nothing; this hunt open → taken; another open → finish it; owner does not offer it (or is not loaded) → `NOT YET`; else `Y: TAKE THIS BOUNTY`. World stamp object `BountyStamp`, re-texted/re-coloured CLAIMED / EXPIRED on every `RestampAll` |
| `IPosterOffer` / `PosterViewerUI` | The offer read by the full-screen viewer: `Prompt`, `Stamp`, `StampColour`, `CanTake`, `Take` |
| `BountyObjective` | Visor: search area round `progress.Site`, then the target; while `OutsideFor > 0` the status is "Out of bounds: return within N s" (N = ceil(`OutOfBoundsSeconds − OutsideFor`)) and the waypoint is the town |
| `BountyTarget` | Added at runtime to the target; holds off `Remains`, surrenders to `captive.faction`, follows. `Release(Vector3? ledTo, …)` leads it only when given a place |
| `BountyRules` | Pure: tip index, search centre, payout, `CanTake` (none open), `CastSeed`/`CastCount`, `SpotKey`/`TryReadSpotKey`, `FlatWithin`, `AllOutside`, `HasExpired` |

## Flows

1. **Take.** Poster → `RequestTake` → host `Take`, client `TakeRpc`. Server checks: none open, not closed, the owner
   is loaded and `Offers` it to the taker, a poster within `takeRange`. Sets town, radius and `progress.Site`.
2. **Ask.** A resident of the owner gives a Whereabouts line → `TipHeard` → Located.
3. **Stand the cast.** Two waves: the OnTaken entries once the site's chunk is loaded on the server, the OnFirstTip
   entries once Phase ≥ Located and the site is loaded. Each wave's flag (`spawned` / `tipCastSpawned`) is set
   **first**, then per entry a count from `CastCount(CastSeed(WorldId, hunt.Id), entry index)` (the Target is always
   1), a prefab (`FactionRoster.Deal`, one deck per roster+role across both waves in entry order), spots by tag,
   `World.Spawn`, `SetKeepChunksLoaded(false)`, the entry's faction, a `HuntPiece`. The target gets `BountyTarget`.
4. **Posts.** Server writes each piece's `AgentGoal` with reason "bounty post" (hold + face the spot's forward),
   "bounty patrol" (the tag's spots in order) or "bounty wander" (within `wanderRadius`), rewriting only when the goal
   is not its own. Skipped while the piece is dead, held or down, or has a combat target, so it walks back after a
   fight. A scattered piece gets one hold goal at spawn and is not re-driven. Pieces are re-found by `SaveRef` as their
   chunks stream in.
5. **Clock and bounds.** `elapsed` grows while open; `outsideFor` grows while **all** `SessionPlayers` are beyond
   `boundsRadius` of the town (flat), else resets — including when no player is connected. Either limit → `Expire`.
   `progress.OutsideFor` is published floored to whole seconds, and a change to it alone is not "live".
6. **Catch, hand in.** As before; hand-in appends `Claimed`.
7. **Close.** On hand-in the target is led to the town's `WalkableHeart`; on expiry it is let go where it stands.
   Loaded non-target pieces → `Remains.BeginUnlessCounting(takenAwaySeconds)` (a corpse keeps its own countdown);
   unloaded ones → `pendingDespawn`, despawned through `GameServices.World.Despawn` as they resolve (dropped once the
   world record no longer has the id; processed every think, hunt open or not). Append the `ClosedHunt`, fresh run
   keeping `pendingDespawn`, posters restamp. A restored run whose hunt left the catalog goes to `pendingDespawn` too.

## Multiplayer

| What | Carrier | Authority |
|---|---|---|
| Progress | `BountyNetwork` NetworkVariable | Server |
| Closed list | `BountyNetwork` NetworkList, read on spawn by late joiners | Server |
| Take | `TakeRpc` | Client asks; server checks owner, offer, poster distance |
| Tips, thanks, gossip | `ResidentSaid` (line id) | Server picks, every machine resolves tokens |
| Cast, rewards | Registered network prefabs via `World.Spawn` / `ItemDropService` | Server |
| Spot cues | none: each machine holds the cue on the agent it sees idle on the spot | Local |
| Visor, poster prompt, stamps | none | Local, derived |

## Persistence

`BountySaveable` (key `bounties`, on `Objectives` in persistentScene), written only while a hunt is open, a hunt has
ended, or `pendingDespawn` is non-empty:

```json
{"open":"<id>","phase":"Located","town":{"x":0,"y":0,"z":0},"townRadius":60.0,"hasSite":true,"site":{"x":0,"y":0,"z":0},
 "run":{"spawned":true,"tipCastSpawned":false,"pieces":[{"body":{"kind":"entity","id":"…"},"target":true,"spot":"hideout#0"}],
        "elapsed":12.5,"outsideFor":0.0,"pendingDespawn":[{"kind":"entity","id":"…"}]},
 "closed":[{"id":"<huntId>","closure":"Claimed"}]}
```

`run` is the whole `BountyHuntRunState`. `site` has a `hasSite` flag because the serializer has no nullable-Vector3
converter. Each piece is its own saved entity, found again by `SaveRef`. **Old saves** (`claimed` ids, `target`,
`targetSpawned`, `settlementId`) load as Claimed closures, a run with `spawned = targetSpawned` and one Target piece;
the site falls back to the asset's; `settlementId` is ignored. A closed id no longer in the catalog is dropped with a
warning; an unknown closure name reads as Expired, with a warning. Round trips, old format included, in `BountyTests`.

## Gotchas

- **The owner, never the nearest settlement.** An Outpost is a `Settlement` too, so "nearest" handed hunts to the
  wrong town. `IsTown` matches `ownerSettlementId` only. An empty id (Stamp Settlement Identity never run) means the
  hunt can never be taken: the recorder logs an error and records nothing.
- **The owner and site are known only after their scenes are saved** (`BountyHuntRecorder`). A hunt listed by two
  settlements in different scenes is only warned about; the last saved wins.
- **The site must lie inside `boundsRadius` of the owner**, or the crew cannot reach it without the hunt expiring.
  The test reads the owner's position from the baked site catalogs (`towns`): re-bake after moving a settlement.
- **Cue holding is local on every machine** (no netcode): a spot holds its cue on one idle, standing, non-`Resident`
  agent within `holdRadius`, picked by sibling index so every machine plays the same loop. **A combat target is known
  only on the server**: on a client a guard shooting from its post without moving may keep its loop.
- **Cast prefabs must have no goal writer of their own** (an `NpcTaskModule`, a resident routine): it fights the run's
  posts. The run only rewrites goals that are not its own.
- **Spot keys are positional** (`tag#place`): reordering or inserting spots under a site while a hunt is open moves
  its pieces to other spots after a load.
- **Never hand-place a hunt's poster for the owning town**: its `BountyNoticeBoard` hangs it when offered. The
  board's town is the `Settlement` ABOVE it, so a board outside a settlement's hierarchy stays empty.
- **`BountyTarget.Release` disables before `Destroy(this)`**: the same frame's `Update` would otherwise `Stop()` the
  `Remains` countdown it just started.
- **`pendingDespawn` outlives the hunt.** A new run is built as `new BountyHuntRunState { pendingDespawn = … }`;
  dropping it brings an expired hunt's people back the next time their chunk loads.
- **`spawned` / `tipCastSpawned` are set before each wave's first spawn**, so a throw part-way never stands it twice.
  On load they are what stop a second cast.
- **Never a `RosterRole.Rider` entry**: a Rider member is the MOUNT carrying an `NpcPassenger`, not a person.
- **Every entry, the Target included, takes its faction (Outlaw by default).** Surrender IS switching to
  `captive.faction` (Drifters); a Raxy target is a Drifter by birth, so in its own faction it was Caught on arrival.
- **`BountyTarget` is never on a prefab**: `HuntPiece.target` re-adds it after a load. Spawn with `World.Spawn`, never
  `NpcSpawn` (disowned from the save).
- **The wire names a hunt by catalog place; saves by id.** Adding a hunt can shift places.
- **`{tip}` is replaced before the token loop**, so a tip may contain `{target}`. Thanks, gossip and rumour rows must
  not use `{target}`: the hunt is closed before clients resolve them. `Favor.IsHarm` is an explicit list: handing in is kind.
- **The corpse drag is the rope's job (LeashSystem).** The bounty only keeps the body (`Remains` held off), saved.
- **Triggers and actions (Phase 2) must be stateless**: anything they change must live in the run state or the
  entities themselves, or it is lost on reload.
- **Verified 2026-10-14:** 19/19 tests; offline host Play: the notice board hangs the poster, take, post, surrender,
  hand-in + pay, both expiries, stamps, `bounties` in a save file. **Not run:** a client, reloading that save, `pendingDespawn` (site chunk stayed loaded).

## Extending

1. **A hunt:** bake its poster ([WantedPosters.md](WantedPosters.md)), then *Create ▸ SpaceGame ▸ Bounty Hunt* in
   `Resources/Bounties/`: poster, 2+ tips, rewards, a cast with one Target entry. Guards: Prefab or RosterRole entries
   (registered network prefabs with a `SaveableEntity`).
2. **Its site:** in a chunk scene, a `BountyHuntSite` pointed at the hunt, with `BountyHuntSpot` children tagged as
   the cast's `spotTag`s. **Its owner:** a `SettlementBounties` on the settlement listing it. Save both scenes. Its
   poster hangs itself on the town's `BountyNoticeBoard`. Re-bake the site catalog if the settlement is new. Run `BountyTests`.
3. **More voices:** `Whereabouts` rows or `Remark × BroughtIn*` rows in the culture's lines.
4. **Tuning:** the `BountyBoard` on `persistentScene/Objectives` (time limit, bounds grace, ranges, claimants, stamps);
   `boundsRadius` per hunt.
