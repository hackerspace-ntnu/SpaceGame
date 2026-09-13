# Game Development Constitution - routing index

143 principles across 24 domains. Public edition v1.1.0 (corpus generated 2026-07-20).

Use this table to pick the **1-5 principles that actually bear on the decision**, then read those files in full from [`principles/`](principles/). The titles below are lossy: the load-bearing parts of a principle are its `Applies when`, `Does not apply / Exceptions` and `Disagreement` sections. Retrieval procedure: [`AI_START_HERE.md`](AI_START_HERE.md). Citation and usage rules: [`AI-USAGE.md`](AI-USAGE.md).

`type` is `objective` (well-evidenced), `contextual` (depends on the game) or `stylistic` (taste). `conf` is evidence strength 1-5, not certainty.

## Domains

| Domain | Covers | Count |
| --- | --- | --- |
| `DESIGN` | Core design | 8 |
| `FEEL` | Game feel | 8 |
| `LEVEL` | Level design | 8 |
| `SYS` | Systems design | 8 |
| `UX` | UX & UI | 7 |
| `ARCH` | Software architecture | 6 |
| `AUDIO` | Audio | 6 |
| `BAL` | Balance | 6 |
| `ECON` | Economy | 6 |
| `NARR` | Narrative | 6 |
| `PLAYTEST` | Playtesting | 6 |
| `PROD` | Production | 6 |
| `PROG` | Programming | 6 |
| `PROTO` | Prototyping | 6 |
| `ANIM` | Animation | 5 |
| `CONTENT` | Content & procedural generation | 5 |
| `MON` | Monetisation | 5 |
| `MP` | Multiplayer | 5 |
| `PERF` | Performance | 5 |
| `QA` | QA | 5 |
| `SHIP` | Shipping | 5 |
| `TEAM` | Team | 5 |
| `TECH` | Tech direction | 5 |
| `VISION` | Vision | 5 |

## ANIM - Animation

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-ANIM-0001](principles/GDC-L1-ANIM-0001.md) | Apply the principles of animation — games are animation too | principles-of-animation | contextual | 4 |
| [GDC-L1-ANIM-0002](principles/GDC-L1-ANIM-0002.md) | Responsiveness beats fidelity — never let animation block input | responsiveness-vs-fidelity | contextual | 4 |
| [GDC-L1-ANIM-0003](principles/GDC-L1-ANIM-0003.md) | Animation is feedback — it communicates state and sells action | gameplay-animation | contextual | 4 |
| [GDC-L1-ANIM-0004](principles/GDC-L1-ANIM-0004.md) | Choose your point on the root-motion vs. code-driven axis deliberately | root-motion-vs-in-place | contextual | 4 |
| [GDC-L1-ANIM-0005](principles/GDC-L1-ANIM-0005.md) | Life is in the secondary motion — animate more than the primary action | procedural | contextual | 3 |

## ARCH - Software architecture

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-ARCH-0001](principles/GDC-L1-ARCH-0001.md) | Make the game data-driven — push behavior and tuning out of code into editable data | data-driven-design | contextual | 4 |
| [GDC-L1-ARCH-0002](principles/GDC-L1-ARCH-0002.md) | Favor composition over inheritance — build entities from components | patterns | contextual | 4 |
| [GDC-L1-ARCH-0003](principles/GDC-L1-ARCH-0003.md) | Decouple systems through events, not direct references | decoupling | contextual | 4 |
| [GDC-L1-ARCH-0004](principles/GDC-L1-ARCH-0004.md) | Keep game logic decoupled from engine and platform specifics | gameplay-architecture | contextual | 3 |
| [GDC-L1-ARCH-0005](principles/GDC-L1-ARCH-0005.md) | Architect for iteration speed — treat the change-to-feedback loop as first-class | code-that-designers-can-touch | objective | 4 |
| [GDC-L1-ARCH-0006](principles/GDC-L1-ARCH-0006.md) | Decide your authoritative state and how it serializes — early | save-systems | contextual | 3 |

## AUDIO - Audio

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-AUDIO-0001](principles/GDC-L1-AUDIO-0001.md) | Sound is feedback first — communicate action, state, and event | sfx-as-feedback | objective | 4 |
| [GDC-L1-AUDIO-0002](principles/GDC-L1-AUDIO-0002.md) | Sound is the cheapest, highest-impact feel win — sell impact with audio | sfx-as-feedback | contextual | 4 |
| [GDC-L1-AUDIO-0003](principles/GDC-L1-AUDIO-0003.md) | Make music adaptive — respond to game state | adaptive-music | contextual | 4 |
| [GDC-L1-AUDIO-0004](principles/GDC-L1-AUDIO-0004.md) | Mix for clarity — the player must always hear what matters | mixing | objective | 4 |
| [GDC-L1-AUDIO-0005](principles/GDC-L1-AUDIO-0005.md) | Use silence and dynamic range — contrast gives sound its power | silence | contextual | 4 |
| [GDC-L1-AUDIO-0006](principles/GDC-L1-AUDIO-0006.md) | Place sound in space — spatialization conveys position and information | spatialization | contextual | 4 |

## BAL - Balance

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-BAL-0001](principles/GDC-L1-BAL-0001.md) | Decide what balance is for before you tune | balance-philosophy | contextual | 4 |
| [GDC-L1-BAL-0002](principles/GDC-L1-BAL-0002.md) | Hunt down dominant strategies; protect viable diversity | dominant-strategies | contextual | 4 |
| [GDC-L1-BAL-0003](principles/GDC-L1-BAL-0003.md) | Symmetry is safe; asymmetry is rich but must be earned through testing | symmetry-vs-asymmetry | contextual | 4 |
| [GDC-L1-BAL-0004](principles/GDC-L1-BAL-0004.md) | Balance through counterplay and situational strength, not raw parity | numbers-and-curves | contextual | 4 |
| [GDC-L1-BAL-0005](principles/GDC-L1-BAL-0005.md) | Tune with data and math; decide with play | tuning-methods | contextual | 4 |
| [GDC-L1-BAL-0006](principles/GDC-L1-BAL-0006.md) | Balance for fun and perceived fairness, not just mathematical parity | balance-philosophy | stylistic | 3 |

## CONTENT - Content & procedural generation

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-CONTENT-0001](principles/GDC-L1-CONTENT-0001.md) | Invest in tools — the pipeline is a force multiplier | toolchain | contextual | 4 |
| [GDC-L1-CONTENT-0002](principles/GDC-L1-CONTENT-0002.md) | Empower creators to build and iterate without programmers | empowering-content-creators | contextual | 4 |
| [GDC-L1-CONTENT-0003](principles/GDC-L1-CONTENT-0003.md) | Enforce naming and organization conventions — consistency scales, chaos compounds | naming-and-organization | contextual | 4 |
| [GDC-L1-CONTENT-0004](principles/GDC-L1-CONTENT-0004.md) | Validate content early — catch bad data on ingest, not at runtime | asset-pipeline | contextual | 4 |
| [GDC-L1-CONTENT-0005](principles/GDC-L1-CONTENT-0005.md) | Automate the repetitive and keep builds reproducible | build-tooling | contextual | 4 |

## DESIGN - Core design

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-DESIGN-0001](principles/GDC-L1-DESIGN-0001.md) | Design for the player's experience, not the designer's intent | player-centric | objective | 5 |
| [GDC-L1-DESIGN-0002](principles/GDC-L1-DESIGN-0002.md) | Make the player's choices interesting — real tradeoffs, no dominant option | decisions-and-agency | objective | 4 |
| [GDC-L1-DESIGN-0003](principles/GDC-L1-DESIGN-0003.md) | Fun is learning — feed the player a steady supply of masterable patterns | fun-and-motivation | contextual | 3 |
| [GDC-L1-DESIGN-0004](principles/GDC-L1-DESIGN-0004.md) | Keep the player in flow by matching challenge to rising skill | fun-and-motivation | contextual | 4 |
| [GDC-L1-DESIGN-0005](principles/GDC-L1-DESIGN-0005.md) | Easy to learn, hard to master — pursue depth from a simple surface | elegance-and-depth | contextual | 4 |
| [GDC-L1-DESIGN-0006](principles/GDC-L1-DESIGN-0006.md) | Give the player real agency — choices must produce legible consequences | decisions-and-agency | contextual | 4 |
| [GDC-L1-DESIGN-0007](principles/GDC-L1-DESIGN-0007.md) | Prize elegance — maximize meaningful gameplay per rule, and cut what doesn't earn its complexity | elegance-and-depth | stylistic | 3 |
| [GDC-L1-DESIGN-0008](principles/GDC-L1-DESIGN-0008.md) | Know your audience — design for a specific player, not "everyone" | player-centric | contextual | 4 |

## ECON - Economy

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-ECON-0001](principles/GDC-L1-ECON-0001.md) | Balance faucets against drains — the flow, not the total, defines the economy | faucets-and-drains | contextual | 4 |
| [GDC-L1-ECON-0002](principles/GDC-L1-ECON-0002.md) | Scarcity creates value and decisions | scarcity | contextual | 4 |
| [GDC-L1-ECON-0003](principles/GDC-L1-ECON-0003.md) | Design sinks deliberately — the missing half of most economies | sources-and-sinks | contextual | 4 |
| [GDC-L1-ECON-0004](principles/GDC-L1-ECON-0004.md) | Guard against inflation and runaway accumulation | inflation | contextual | 4 |
| [GDC-L1-ECON-0005](principles/GDC-L1-ECON-0005.md) | Give each currency a distinct purpose | currencies | contextual | 3 |
| [GDC-L1-ECON-0006](principles/GDC-L1-ECON-0006.md) | Player-driven economies are emergent systems — design the rules, not the transactions | trade | contextual | 3 |

## FEEL - Game feel

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-FEEL-0001](principles/GDC-L1-FEEL-0001.md) | Game feel is real-time control of a virtual body in a simulated space, made vivid by polish | responsiveness | objective | 5 |
| [GDC-L1-FEEL-0002](principles/GDC-L1-FEEL-0002.md) | Acknowledge input immediately; keep control latency perceptually tight | responsiveness | objective | 5 |
| [GDC-L1-FEEL-0003](principles/GDC-L1-FEEL-0003.md) | Interpret the player's intent, not just their literal input | input | contextual | 4 |
| [GDC-L1-FEEL-0004](principles/GDC-L1-FEEL-0004.md) | Amplify every meaningful action with layered, redundant, multi-sensory feedback | feedback-and-juice | objective | 4 |
| [GDC-L1-FEEL-0005](principles/GDC-L1-FEEL-0005.md) | Sell impact by briefly interrupting time (hitstop / hit pause) | feedback-and-juice | contextual | 4 |
| [GDC-L1-FEEL-0006](principles/GDC-L1-FEEL-0006.md) | Use camera motion and screenshake to convey force — but dose it and let players control it | camera | contextual | 4 |
| [GDC-L1-FEEL-0007](principles/GDC-L1-FEEL-0007.md) | Tune for the sensation, not physical accuracy | physicality | contextual | 4 |
| [GDC-L1-FEEL-0008](principles/GDC-L1-FEEL-0008.md) | Place the game deliberately on the responsiveness–commitment axis | responsiveness | contextual | 4 |

## LEVEL - Level design

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-LEVEL-0001](principles/GDC-L1-LEVEL-0001.md) | Guide the player's eye with the environment, not with hand-holding | guidance-and-legibility | contextual | 4 |
| [GDC-L1-LEVEL-0002](principles/GDC-L1-LEVEL-0002.md) | Make space legible — support the player's mental map | guidance-and-legibility | contextual | 4 |
| [GDC-L1-LEVEL-0003](principles/GDC-L1-LEVEL-0003.md) | Pace intensity — shape a rhythm of tension and release | pacing | contextual | 4 |
| [GDC-L1-LEVEL-0004](principles/GDC-L1-LEVEL-0004.md) | Teach through space — the level is a tutorial | encounter-design | contextual | 4 |
| [GDC-L1-LEVEL-0005](principles/GDC-L1-LEVEL-0005.md) | Balance guidance with exploration — a clear spine and rewarded detours | open-vs-linear | contextual | 4 |
| [GDC-L1-LEVEL-0006](principles/GDC-L1-LEVEL-0006.md) | Show before you go — use vistas and foreshadowing to plant goals | composition | contextual | 3 |
| [GDC-L1-LEVEL-0007](principles/GDC-L1-LEVEL-0007.md) | Tell story through the environment — let players pull the narrative | environmental-storytelling | contextual | 4 |
| [GDC-L1-LEVEL-0008](principles/GDC-L1-LEVEL-0008.md) | Shape encounters through space — the arena is a combat design tool | encounter-design | contextual | 4 |

## MON - Monetisation

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-MON-0001](principles/GDC-L1-MON-0001.md) | Choose a monetization model that fits the game and is honest about the deal | models | contextual | 4 |
| [GDC-L1-MON-0002](principles/GDC-L1-MON-0002.md) | Trade fair value — monetization should add value, not manufacture problems to sell relief | value-exchange | contextual | 4 |
| [GDC-L1-MON-0003](principles/GDC-L1-MON-0003.md) | Refuse dark patterns — don't exploit cognitive biases to extract spending | dark-patterns-to-avoid | contextual | 4 |
| [GDC-L1-MON-0004](principles/GDC-L1-MON-0004.md) | Keep pay out of "win" — sell expression and convenience, not competitive power | models | stylistic | 3 |
| [GDC-L1-MON-0005](principles/GDC-L1-MON-0005.md) | Be transparent — disclose odds and true costs; enable informed consent | ethics | contextual | 4 |

## MP - Multiplayer

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-MP-0001](principles/GDC-L1-MP-0001.md) | Design the social experience, not just the netcode | social-dynamics | contextual | 4 |
| [GDC-L1-MP-0002](principles/GDC-L1-MP-0002.md) | Behavior is designed — shape the community with systems, don't just moderate it | fairness-and-anti-toxicity | contextual | 4 |
| [GDC-L1-MP-0003](principles/GDC-L1-MP-0003.md) | Fair matchmaking is core design — match by skill and connection | matchmaking-design | contextual | 4 |
| [GDC-L1-MP-0004](principles/GDC-L1-MP-0004.md) | Trust the server, not the client — assume the network is hostile | competitive | contextual | 4 |
| [GDC-L1-MP-0005](principles/GDC-L1-MP-0005.md) | Design for the players who actually show up | community | contextual | 3 |

## NARR - Narrative

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-NARR-0001](principles/GDC-L1-NARR-0001.md) | Seek ludonarrative harmony — align what the game says with what it makes you do | ludonarrative | contextual | 4 |
| [GDC-L1-NARR-0002](principles/GDC-L1-NARR-0002.md) | The player is a co-author, not an audience | player-authored-story | contextual | 4 |
| [GDC-L1-NARR-0003](principles/GDC-L1-NARR-0003.md) | Prefer embedded, experienced narrative over delivered exposition | environmental-narrative | contextual | 4 |
| [GDC-L1-NARR-0004](principles/GDC-L1-NARR-0004.md) | Branching is expensive — buy the feeling of agency efficiently | branching-vs-linear | contextual | 4 |
| [GDC-L1-NARR-0005](principles/GDC-L1-NARR-0005.md) | Pace story around player-controlled time | pacing | contextual | 3 |
| [GDC-L1-NARR-0006](principles/GDC-L1-NARR-0006.md) | Build worlds by implication — and let players miss things | worldbuilding | contextual | 4 |

## PERF - Performance

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-PERF-0001](principles/GDC-L1-PERF-0001.md) | Measure, don't guess — profile before you optimize | profiling-first | objective | 5 |
| [GDC-L1-PERF-0002](principles/GDC-L1-PERF-0002.md) | Don't optimize prematurely — implement simply first | optimization-timing | contextual | 4 |
| [GDC-L1-PERF-0003](principles/GDC-L1-PERF-0003.md) | Optimize the bottleneck — the critical few, not the trivial many | optimization-timing | objective | 4 |
| [GDC-L1-PERF-0004](principles/GDC-L1-PERF-0004.md) | Budget the frame — decide what each system may spend | frame-budget | contextual | 4 |
| [GDC-L1-PERF-0005](principles/GDC-L1-PERF-0005.md) | Respect data locality — memory layout often beats instruction cleverness | memory | contextual | 4 |

## PLAYTEST - Playtesting

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-PLAYTEST-0001](principles/GDC-L1-PLAYTEST-0001.md) | Watch what players do, not just what they say | observing-vs-asking | objective | 5 |
| [GDC-L1-PLAYTEST-0002](principles/GDC-L1-PLAYTEST-0002.md) | Test early, often, and rough | playtest-methods | objective | 4 |
| [GDC-L1-PLAYTEST-0003](principles/GDC-L1-PLAYTEST-0003.md) | Don't help, don't explain | observing-vs-asking | objective | 5 |
| [GDC-L1-PLAYTEST-0004](principles/GDC-L1-PLAYTEST-0004.md) | Listen to the problem, distrust the proposed solution | interpreting-feedback | objective | 5 |
| [GDC-L1-PLAYTEST-0005](principles/GDC-L1-PLAYTEST-0005.md) | Combine telemetry with observation — the "what" needs the "why" | telemetry-and-analytics | contextual | 4 |
| [GDC-L1-PLAYTEST-0006](principles/GDC-L1-PLAYTEST-0006.md) | Mind your sample and your biases | sample-and-bias | contextual | 4 |

## PROD - Production

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-PROD-0001](principles/GDC-L1-PROD-0001.md) | Scope is the primary risk — cut scope to protect quality and shipping | scoping-and-cutting | contextual | 4 |
| [GDC-L1-PROD-0002](principles/GDC-L1-PROD-0002.md) | Fight scope creep — default to no; every feature has a hidden tail | scope-creep | contextual | 4 |
| [GDC-L1-PROD-0003](principles/GDC-L1-PROD-0003.md) | Build a vertical slice — prove one polished slice at target quality | pre-production-vs-production | contextual | 4 |
| [GDC-L1-PROD-0004](principles/GDC-L1-PROD-0004.md) | Plan for iteration and the unknown — schedules must budget discovery | scheduling | contextual | 4 |
| [GDC-L1-PROD-0005](principles/GDC-L1-PROD-0005.md) | Avoid crunch — sustained overwork is a planning failure, not a virtue | crunch-avoidance | contextual | 4 |
| [GDC-L1-PROD-0006](principles/GDC-L1-PROD-0006.md) | Finish — shipping is its own skill | scoping-and-cutting | contextual | 4 |

## PROG - Programming

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-PROG-0001](principles/GDC-L1-PROG-0001.md) | Design the power curve deliberately — pace growth against challenge | pacing-of-power | contextual | 4 |
| [GDC-L1-PROG-0002](principles/GDC-L1-PROG-0002.md) | Decide the mix of player skill and character power | skill-vs-power | contextual | 4 |
| [GDC-L1-PROG-0003](principles/GDC-L1-PROG-0003.md) | Reward the behavior you want — legibly and proportionately | reward-schedules | contextual | 4 |
| [GDC-L1-PROG-0004](principles/GDC-L1-PROG-0004.md) | Favor intrinsic progression over extrinsic treadmills | reward-schedules | stylistic | 3 |
| [GDC-L1-PROG-0005](principles/GDC-L1-PROG-0005.md) | Introduce complexity in four beats — introduce, develop, twist, conclude | mastery-curve | contextual | 4 |
| [GDC-L1-PROG-0006](principles/GDC-L1-PROG-0006.md) | Design the mastery ceiling and endgame explicitly | mastery-curve | contextual | 3 |

## PROTO - Prototyping

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-PROTO-0001](principles/GDC-L1-PROTO-0001.md) | Find the fun first — prove the core before building around it | prototyping | objective | 4 |
| [GDC-L1-PROTO-0002](principles/GDC-L1-PROTO-0002.md) | Prototype the riskiest assumption first | fail-fast | objective | 4 |
| [GDC-L1-PROTO-0003](principles/GDC-L1-PROTO-0003.md) | Greybox before you make it pretty — validate the design with placeholders | greyboxing | contextual | 4 |
| [GDC-L1-PROTO-0004](principles/GDC-L1-PROTO-0004.md) | Keep prototypes focused and disposable — one question, throwaway code | prototyping | contextual | 3 |
| [GDC-L1-PROTO-0005](principles/GDC-L1-PROTO-0005.md) | Kill your darlings — cut what doesn't serve the game, however attached you are | kill-your-darlings | contextual | 4 |
| [GDC-L1-PROTO-0006](principles/GDC-L1-PROTO-0006.md) | The iteration loop is the master tool — the more you test and refine, the better the game | iteration-loops | objective | 5 |

## QA - QA

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-QA-0001](principles/GDC-L1-QA-0001.md) | Match test rigor to risk | test-strategy | contextual | 4 |
| [GDC-L1-QA-0002](principles/GDC-L1-QA-0002.md) | Automate regression; human-test feel and emergence | automated-testing-of-games | contextual | 4 |
| [GDC-L1-QA-0003](principles/GDC-L1-QA-0003.md) | A bug you can't reproduce, you can't fix | repro-and-bug-tracking | contextual | 4 |
| [GDC-L1-QA-0004](principles/GDC-L1-QA-0004.md) | Test on target hardware and under real conditions | soak-and-stress | contextual | 4 |
| [GDC-L1-QA-0005](principles/GDC-L1-QA-0005.md) | Build quality in — don't test it in at the end | test-strategy | contextual | 4 |

## SHIP - Shipping

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-SHIP-0001](principles/GDC-L1-SHIP-0001.md) | You get one first impression — the launch state shapes the game's reception | launch-readiness | contextual | 4 |
| [GDC-L1-SHIP-0002](principles/GDC-L1-SHIP-0002.md) | For live games, launch is a beginning — plan post-launch from the start | post-launch | contextual | 4 |
| [GDC-L1-SHIP-0003](principles/GDC-L1-SHIP-0003.md) | Run live ops as a service — sustain content, balance, and communication | live-ops | contextual | 3 |
| [GDC-L1-SHIP-0004](principles/GDC-L1-SHIP-0004.md) | Close the loop after launch — telemetry and community feedback into responsive patching | post-launch | contextual | 4 |
| [GDC-L1-SHIP-0005](principles/GDC-L1-SHIP-0005.md) | Sunset with respect — plan the end of life | sunsetting | stylistic | 3 |

## SYS - Systems design

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-SYS-0001](principles/GDC-L1-SYS-0001.md) | Build around a core loop and make it satisfying in isolation | core-loop | objective | 4 |
| [GDC-L1-SYS-0002](principles/GDC-L1-SYS-0002.md) | Design second-order — author the rules, not the outcomes | systems-thinking | objective | 5 |
| [GDC-L1-SYS-0003](principles/GDC-L1-SYS-0003.md) | Seek depth through emergence — few rules interacting, not enumerated content | emergence | contextual | 4 |
| [GDC-L1-SYS-0004](principles/GDC-L1-SYS-0004.md) | Know your feedback loops — positive loops amplify, negative loops stabilize | feedback-loops | objective | 4 |
| [GDC-L1-SYS-0005](principles/GDC-L1-SYS-0005.md) | Make systems orthogonal — each earns its place by doing something no other does | complexity-management | contextual | 3 |
| [GDC-L1-SYS-0006](principles/GDC-L1-SYS-0006.md) | Make systems legible — expose enough state for players to form and test hypotheses | systems-thinking | contextual | 4 |
| [GDC-L1-SYS-0007](principles/GDC-L1-SYS-0007.md) | Players optimize the fun out — protect the experience from degenerate strategies | systems-thinking | contextual | 4 |
| [GDC-L1-SYS-0008](principles/GDC-L1-SYS-0008.md) | Model resources as an internal economy — mind the sources, sinks, and flows | interlocking-systems | objective | 4 |

## TEAM - Team

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-TEAM-0001](principles/GDC-L1-TEAM-0001.md) | Psychological safety is the foundation of good teamwork | psychological-safety | contextual | 4 |
| [GDC-L1-TEAM-0002](principles/GDC-L1-TEAM-0002.md) | Critique the work, not the person | feedback-culture | contextual | 4 |
| [GDC-L1-TEAM-0003](principles/GDC-L1-TEAM-0003.md) | Run blameless postmortems — treat failure as a system to fix | conflict | contextual | 4 |
| [GDC-L1-TEAM-0004](principles/GDC-L1-TEAM-0004.md) | Make decisions shared, visible, and durable | communication | contextual | 4 |
| [GDC-L1-TEAM-0005](principles/GDC-L1-TEAM-0005.md) | Prefer small, empowered, cross-disciplinary teams | collaboration | stylistic | 3 |

## TECH - Tech direction

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-TECH-0001](principles/GDC-L1-TECH-0001.md) | Technical art is one discipline — bridge art intent and engine reality | art-tech-pipeline | contextual | 4 |
| [GDC-L1-TECH-0002](principles/GDC-L1-TECH-0002.md) | Budget the frame's visuals — art must fit the rendering cost | rendering-budget | contextual | 4 |
| [GDC-L1-TECH-0003](principles/GDC-L1-TECH-0003.md) | Lighting is the highest-leverage visual tool | lighting | contextual | 4 |
| [GDC-L1-TECH-0004](principles/GDC-L1-TECH-0004.md) | Coherent art direction beats raw fidelity | optimization-of-art | stylistic | 3 |
| [GDC-L1-TECH-0005](principles/GDC-L1-TECH-0005.md) | Build materials and shaders as a data-driven system, not one-offs | shaders-and-materials | contextual | 4 |

## UX - UX & UI

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-UX-0001](principles/GDC-L1-UX-0001.md) | Teach by doing, just in time — not with front-loaded walls of text | onboarding-and-tutorials | contextual | 4 |
| [GDC-L1-UX-0002](principles/GDC-L1-UX-0002.md) | Manage cognitive load — reveal complexity progressively | cognitive-load | contextual | 4 |
| [GDC-L1-UX-0003](principles/GDC-L1-UX-0003.md) | Make the interface communicate — readability, hierarchy, and feedback | ui-design | objective | 4 |
| [GDC-L1-UX-0004](principles/GDC-L1-UX-0004.md) | Use affordances, signifiers, and conventions — make the right action obvious | ui-design | contextual | 4 |
| [GDC-L1-UX-0005](principles/GDC-L1-UX-0005.md) | Design controls for the hand — ergonomics, mapping, and button economy | control-schemes | contextual | 4 |
| [GDC-L1-UX-0006](principles/GDC-L1-UX-0006.md) | Treat accessibility as design — and build it in early | accessibility | contextual | 4 |
| [GDC-L1-UX-0007](principles/GDC-L1-UX-0007.md) | Minimize friction between the player and the fun | ui-design | contextual | 4 |

## VISION - Vision

| ID | Title | Subdomain | Type | Conf |
| --- | --- | --- | --- | --- |
| [GDC-L1-VISION-0001](principles/GDC-L1-VISION-0001.md) | Hold a clear creative vision — a north star that resolves decisions | vision-holding | contextual | 4 |
| [GDC-L1-VISION-0002](principles/GDC-L1-VISION-0002.md) | Define pillars — a few explicit principles every decision is checked against | pillars | contextual | 4 |
| [GDC-L1-VISION-0003](principles/GDC-L1-VISION-0003.md) | Say no to protect coherence — a game is defined by what it excludes | saying-no | contextual | 4 |
| [GDC-L1-VISION-0004](principles/GDC-L1-VISION-0004.md) | Give the vision an owner — coherence needs decision authority | decision-making-authority | stylistic | 3 |
| [GDC-L1-VISION-0005](principles/GDC-L1-VISION-0005.md) | Make the vision communicable — if you can't say it simply, it isn't clear | the-hook | contextual | 3 |

