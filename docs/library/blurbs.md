# Library blurbs

Plain-language descriptions for the showcase site. One `##` section per entry, keyed by the `id`
in `library.json`. `tools/build_library_site.py` joins these to the renders that
`Tools/SpaceGame/Export Library Site Data` produces.

The exporter never touches this file. Adding a creature does not put its description at risk; it
only means a new id turns up in `library.json` with nothing written for it here, which the build
script reports.

Write for someone who has never played. Say what the thing is, what it does for you, and anything
that would surprise you the first time. No component names, no code.

---

## DragonBazooka

Fires a firework rocket that corkscrews away from you and bursts into a litter of small whelps.
The whelps are the point — the rocket is just how they arrive. It is also the ruler the rest of
the armoury is measured against: every other item's size in the hand was set relative to this one.

## GravelBlaster

A pipe shotgun that sprays gravel. Roughly one shot in ten backfires in your face, and you have
no way of knowing which one it will be until it does. Loud, close-range, and slightly a bad idea.

## RepulsorGauntlet

Worn on the forearm rather than held. Fires an instant cone of force that sends everything caught
in it tumbling. No projectile to lead and no travel time — whatever is in the cone goes flying.

## SuckerPuncher

A steam-driven ram. Whatever you actually connect with takes heavy damage and gets launched;
everything standing nearby is shoved back by the shockwave. A crowd-clearer disguised as a punch.

## LaserStaff

Hold to fire a beam that burns for three seconds, then spends ten seconds recharging. Once it
starts it finishes on its own — letting go early does not cut it short.

## LightningSpell

Points at a spot and drops a bolt on it. No projectile, no arc, no travel time: you look, you
press, it strikes.

## BallLightningWeapon

The one weapon that charges. The first press spawns a crackling orb and starts building it up;
the second press launches it. In flight it drifts and wanders on its own, lighting the ground as
it goes.

## basicgun

The plain sidearm. Fires a bullet in a straight line. Nothing clever, which is the point — it is
the weapon everything else is compared against.

## RocketTurret

Plants a rocket-launcher turret on the ground in front of you. Once it is down it picks its own
targets and fires without you. Useful for holding a spot you need to leave.

## GrapplingHook

Fires a dart on a rope, pulls taut, and swings or winches you in. The rope is a real line you
hang from, so a badly aimed shot leaves you dangling somewhere awkward.

## Lasso

Hold to twirl the loop overhead — the twirl is the charge, so a longer wind-up throws further —
then release to fling it. Whatever you catch struggles against the rope.

## NetGun

Fires a folded net that opens out along its flight and drapes over whatever it lands on. The
charge comes back on its own after a while, so it is never permanently spent.

## Leash

A rope you can tie between any two things at all — a creature to a post, a crate to a moving
vehicle, yourself to something much stronger than you. Below its length it does nothing; at full
stretch, the stronger end drags the weaker one. It can haul you off your feet, but it can never
fling you: there is no way to reel yourself in along it. To get free, hold your movement squarely
away from the anchor for a couple of seconds and it tears loose.

## ItemScanner

A screen strapped to the forearm that finds loose salvage within about 100 metres and points you
at it. Purely informational — it changes nothing in the world, it only tells you what is out
there.

## RuinScanner

Casts a wide cone of light downward. Anything hidden that the light touches reveals itself. Made
for sweeping ruins where the thing you want is buried in the geometry.

## PortalGun

A spray can, not a gun. The paint you spray *is* the portal — hold the trigger and keep painting
until the opening is as big as you want it. Two patches connect to each other.

## JumpingRod

A pogo stick. Plant it and bounce. It was built as a rideable vehicle first and deliberately
turned back into a hand item, because riding a pogo stick read wrong.

## AntiGravityPotion

Drink it and float for about five seconds. Long enough to cross something you could not jump, and
short enough that you have to mean it.

## Jetpack

Worn on the back. Thrust follows the nozzles rather than the keys — they swing round to where you
asked to go, and the push arrives as they get there, so it steers like a machine warming up rather
than a switch. Holding it down heats it, and only fully cutting the throttle cools it back down.

## Wingsuit

A worn suit that trades height for distance. No engine — you are gliding, and the ground is
always winning slowly.

## WingPack

Worn folded on your back until you deploy it, at which point it unfurls into an actual ornithopter
you are flying. The only item in the game that turns into a vehicle. Its size is set by your
wingspan, not by taste.

## FlashlightGauntlet

The torch, and it is worn rather than built in: a lamp clamped to the bracer on your forearm.
Because it is on your arm, it lights where your arm points, not where you are looking — switching
it on raises the arm into a carrying pose, which is what puts the beam ahead of you. Put something
in your hands and that wins. Lose the gauntlet and you are in the dark.

## Lantern

A lamp you can carry or set down. Once it is on the ground it stays there, lit, and is still there
when you come back.

## Saddle

Fit it to an animal that can carry one and you can ride it. Without a saddle the animal is just an
animal.

## FoamGun

Sprays expanding foam that sets where it lands. What you are really doing is building — the blobs
stay put and become something you can climb or block a gap with.

## Battery

Portable charge. Slots into things that have run flat.

## OxygenTank

A bottle of breathable air that plugs into the generator's hatch at a quarter turn. How much is
left in it belongs to the bottle itself, so a half-empty tank stays half empty wherever it ends
up — swapping bottles is genuinely swapping bottles.

## ReactorCore

The lander's power plant. One of the hull modules you carry by hand and fit into its socket in the
ship; the candidate sockets light up as you approach holding it.

## NuclearMotor

The heavy engine option for the lander. Carried and socketed like every other hull module.

## SmallMotor

The light engine. Less thrust than the nuclear motor and a great deal less to carry.

## LongTurbine

A hull-mounted turbine. One of the parts you salvage and refit to get the ship flying again.

## AirIntake

Feeds the engines. A ship part you carry to its socket and press into place.

## AntiGravity

The lander's lift module. Fitted into the hull like the rest of the ship parts.

## Gun

A ship-mounted gun module, fitted to the hull rather than held in your hands.

## Ostrich

A two-legged bird you can saddle and ride, and the most carefully built creature in the game. It
plays no animation at all — every footstep is worked out as it happens, which is why it picks its
way over uneven dunes properly instead of skating. Watch the head: the neck spends its whole time
cancelling the body's bounce, so the head hangs still in the air while the body bobs underneath.
Its gaze snaps between things rather than sweeping.

## Sandloper

Desert wildlife. Peaceful until something hurts it — it has no opinion about you at all until you
give it one, and then it fights back for a while before calming down again.

## DuneRat

Small, quick and low to the ground. Despite four legs' worth of skeleton it actually moves as a
biped.

## Vrescal

A six-legged desert animal — four legs forward, two behind. Big enough to be a problem.

## CrabWalker6

A crab that travels sideways across its own nose. The wave of legs runs along the body rather than
front-to-back, with the front and back halves stepping in opposite time. The shell stays low and
hugs the ground instead of bobbing.

## Golem

A heavy, slow thing made of rock. Hits hard and takes a lot of stopping.

## Appa

A large friendly beast. More transport than threat.

## HumanoidRobot

A walking machine on two legs, built on the same footstep system as the ostrich with every
movement scaled down. The arms swing off the legs' own rhythm, so they can never fall out of time
with the walk.

## Nomad

The people of the desert. A nomad has somewhere to be — a task names a kind of place and how long
to linger there, and the specific spot is worked out when they get near it. Get close and they
will say what they are doing. You can talk to them, but not while they are fighting you; a
bystander of the same species will still chat.

## PatrolRobot

Walks a fixed route through ruins and outposts and objects to company. If it sees you it can wake
its neighbours onto you as well, so a group comes at you together rather than one at a time.

## DeathmatchBot

The opponent in the arena minigame. Exists to be shot at and to shoot back.

## DuneOrnithopter

A flapping-wing aircraft, and the trickiest thing in the game to fly. There is no throttle: speed
is bought with height or with flapping, and flapping costs stamina — about six seconds of hard
climbing empties you, about four and a half seconds of gliding fills you back up. The controls go
soft as you slow down, and in a stall you keep barely a third of your authority. Pulling the wings
in sheds most of the wing area, which is how you dive fast. Landing is the hard part.

## DuneFoil

A sand sailer. Nobody rides it — you walk its deck and work it from stations: a helm, four rigging
posts, a boarding ramp. The wheel turns the foil rather than steering the hull directly. Left
alone it moors itself and holds still.

## RigWalker

A piloted six-legged walker with a cockpit and a deck you can walk around on. You board it at the
controls, not by touching a leg.

## DesertCrawler

A six-legged habitat on the move, driven by its own AI rather than by you. You ride along on the
deck and work the dig, claw and collector rig mounted on it. Its six legs make it statically
stable — at least three feet are down at all times, so it cannot be tipped over. The deck follows
only about 60% of the slope beneath it, and that is on purpose: a deck that matched the ground
exactly would throw you off it.

## PlayerShip

The lander, and where the game starts — you arrive in it, crashed. Four chairs: the front-left one
flies it, the other three are for passengers. One switch opens the entire side of the hull, four
telescoping leaves and a boarding stair together. Inside there is a gear wall you lay your kit
onto, a map projector, salvage sockets for the hull modules you find, and a repair bench that is
now furniture — it used to eat scrap, scrap left the game, and the bench stayed because the deck
is laid out around it. However far you travel, you respawn inside your own ship.

## CowBotRocket

A small rocket that carries a cow-shaped robot somewhere it did not ask to go.

## Rover

An autonomous six-wheeled explorer whose suspension works out each wheel's height against the
ground independently. It drives itself; you cannot ride it, and it currently only exists in a test
scene.

## ExpeditionRig

The backpack, and it is not a grid of icons. It is a physical rig you wear on your spine,
unshoulder onto the sand and lay real objects onto. Press B and it comes off your back with a toss
and unfolds while the world carries on around you — you are kneeling over your kit, not paused in
a menu. Seven flat faces make up the usable surface, all measured in one 13.5 cm cell read off the
rig's own webbing. What limits you is squares covered, not item count or weight. It refuses
bluntly: there is no error message, the red cells *are* the refusal, and clicking on red turns the
item a quarter turn — the complaint and its likely fix are the same click.

## ExpeditionBackpack

The pack itself, worn shut. While it is on your back only the rack is within reach; the fold-out
leaf, the lash line and both wings ride the hinge and are simply not there until you set it down.
Its contents belong to the pack rather than to you, so putting it down and walking away leaves
your gear lying there for anyone to find.

## ForearmBracer

Worn permanently on both forearms, and never taken off. On its own it does nothing — it is the
mounting deck that gauntlets clamp onto, so the flashlight and the scanner have somewhere to live.

## InventoryWall

The gear wall inside the lander: the same laying-out-your-kit idea as the backpack, mounted on the
ship's ribs and answering the same gestures. Storage that stays with the ship instead of on your
spine.

## Flamethrower

Hold the trigger and it throws a jet of burning fuel. The throttle ramps rather than switching, so
the flame swells and dies back instead of snapping on, and it draws from a tank you can run dry.
