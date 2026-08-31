// The id catalog half of the NetMessaging channel — see NetMessaging.cs for the why.
namespace SpaceGame.Core
{
    /// <summary>
    /// Every message id in the game, in one place so they are greppable and provably unique
    /// (NetMessagingTests asserts it). Ids are only ever appended — a reused number would route a
    /// message to the wrong handler, and the ids travel over the wire between builds.
    /// </summary>
    public static class NetMsg
    {
        // ── Items ──
        // (3 was Equip, retired: the hotbar selection already replicates, so every machine
        //  reaches the same equip on its own. Not reused — ids travel between builds.)
        public const ushort UseItem   = 1;  // owner → server: use what I have equipped
        public const ushort ItemUsed  = 2;  // server → peers: play the presentation for it

        // Held use, for items that keep doing something for as long as the button is down. Sent
        // repeatedly while held and once more on release, with B carrying 1 for "still going" and
        // 0 for "stop". There is deliberately no separate Start id: the first tick of the stream
        // is the start, which means one code path can never begin a beam it has no way to end.
        //
        // P and R carry the owner's aim RAY — its origin and its rotation — not the point it
        // landed on. Every machine then traces that same ray for itself, so the server stays the
        // one deciding what was hit while peers still draw the beam ending exactly where the
        // damage is being dealt.
        public const ushort UseItemHold = 4;  // owner → server: I am holding use, and here is my aim
        public const ushort ItemUseHeld = 5;  // server → peers: sustain the presentation

        // ── Combat ──
        public const ushort Damage    = 10; // → server, on the TARGET's relay. A = amount, Target = source

        // server → peers, on the VICTIM's relay. A = amount, Target = the attacking PLAYER.
        //
        // Damage above travels towards the server; this is the answer coming back, and it exists
        // because a client cannot see its own hits land. Weapon.Use() runs on the authority alone,
        // so a client that pulls the trigger runs only the cosmetic Present() — the amount is
        // decided on a machine that is not theirs. Anything drawn from the local call would work
        // for whoever is hosting and silently show nothing to everyone else.
        //
        // Broadcast rather than addressed at the shooter, because this layer has no unicast. That
        // is the same shape NetTo.Others already uses: every machine receives it and each decides
        // whether it is theirs, here by resolving Target and asking whether that player is owned
        // locally. Only sent when a player dealt the hit, so NPC-on-NPC fighting — which is most
        // of the damage in a populated world — puts nothing on the wire at all.
        public const ushort Damaged   = 11; // server → peers, on the VICTIM's relay

        // ── Riding ──
        // A = which mount on the vehicle, as MountNetworkSync.MountIndex. A vehicle may carry
        // several — every non-helm chair on the PlayerShip is its own MountModule, see the retired
        // 92/93 below — and they share the entity's one channel, so without the number one press
        // seats the same player in all of them.
        public const ushort Mount     = 20; // → server, on the MOUNT's relay. Target = rider
        public const ushort Mounted   = 21; // server → peers
        public const ushort Dismount  = 22; // → server, on the MOUNT's relay
        public const ushort Dismounted = 23;

        // (30 was LaunchCraft, retired: a wing-pack launch is just a server-authoritative item use
        //  like any other. Not reused — ids travel between builds.)

        // ── Trading ──
        // A = index of the accepted offer on the trader. Target = the player taking it.
        // Sent to the TRADER's relay: the trader owns its own stock, and the server is the only
        // machine allowed to decide that two players did not both take the last water cell.
        public const ushort Trade     = 50;

        // ── Life cycle ──
        // No matching "Respawned" id: the server's answer to this is a heal (which the health
        // NetworkVariable already publishes) plus a placement (which NetworkedTeleport routes to
        // the owner). Neither needs a message of its own.
        public const ushort Respawn   = 40; // owner → server, on the PLAYER's relay

        // ── Articulated parts (doors, ramps, hatches) ──
        // A = the switch's index among the ArticulatedPartInteractions on the entity, since one
        // entity can carry several independent groups (ShipRV has a cockpit door and a garage
        // door). B is the verb:
        //
        //   -1  what is this group's state?   (client → server)
        //    0  closed, animate               1  open, animate
        //    2  closed, instantly             3  open, instantly   (server's answer to -1)
        //
        // "Instantly" exists for late joiners: a door that was opened before you arrived should
        // already be open, not swing open in your face the moment you spawn.
        public const ushort PartToggle = 60; // owner → server: set this group, or ask for it
        public const ushort PartState  = 61; // server → everyone: this is the group's state

        // ── Ropes ──
        // Sent to the PLAYER's channel, carrying a velocity delta in P.
        //
        // RETIRED. A rope used to be simulated entirely by the server, which cannot push a player's
        // body — theirs is owner-authoritative, so a server-side push is overwritten by their next
        // state update. So the server banked what the rope owed each player and shipped it here at
        // 10 Hz for that player's own machine to apply.
        //
        // Leash now splits the rope by end instead: a player's end is resolved on their own machine
        // and everything else on the server, so there is nothing left to send. The number stays
        // burnt rather than reused — a peer on an older build must never decode a different message
        // as this one.
        public const ushort RopeTug   = 62; // retired 2026-08, see LeashedBody

        // ── Latches (doors, levers, and anything else with a held open/closed state) ──
        // The same shape as PartToggle/PartState above, and deliberately so: a door and a lever
        // are both "a switch with an index and a state", and one pair of ids serving both is what
        // lets NetLatch be a single shared helper rather than a class per fixture.
        //
        // A = the latch's index among the latches on the entity, since one entity can carry
        // several (a corridor with two doors). B is the verb, matching PartToggle's vocabulary:
        //
        //   -1  what is this latch's state?   (client → server)
        //    0  off/closed, animate           1  on/open, animate
        //    2  off/closed, instantly         3  on/open, instantly   (server's answer to -1)
        //
        // "Instantly" is the late-joiner case: a door opened before you arrived should already be
        // open when you walk up to it, not swing open in your face.
        public const ushort LatchSet   = 63; // owner → server: set this latch, or ask for it
        public const ushort LatchState = 64; // server → everyone: this is the latch's state

        // ── Vehicle stations (helm, sheet, mooring — anything a player mans) ──
        // A = the station's index on the vehicle. B is 1 to claim it and 0 to stand down.
        // Target = the player doing it, so the server can refuse a claim on a manned station and
        // can reject a stand-down from anyone but the person actually at the wheel.
        //
        // Addressed to the VEHICLE's channel rather than the player's: the vehicle owns the fact
        // that exactly one person is steering it, which is precisely the state two players racing
        // for the same wheel would otherwise both believe they had won.
        public const ushort StationClaim = 65; // player → server: I am taking / leaving this station
        public const ushort StationState = 66; // server → everyone: this station is manned by Target

        // ── Backpacks ──
        // Sent to the PACK OWNER's channel. A carries the deploy state for PackState
        // (0 shouldered, 1 deploying, 2 open, 3 stowing).
        //
        // PackTake names what is being taken POSITIONALLY, like its three neighbours below:
        //
        //   Target  the player reaching in. Their hotbar is the destination.
        //   A       the surface the point is on.
        //   P       the point, in that surface's uv: X and Z, Y unused.
        //   B       which hotbar slot to put it in, or -1 for "wherever it fits", which is what a
        //           right-click sends and what every caller that does not name a slot gets. A
        //           named slot is a DRAG let go over that slot, and it swaps: whatever was in the
        //           box goes back onto the pack, into the space the taken item is vacating.
        //
        // A pack is a container two people can reach into at once, so the server has to be the one
        // deciding which of them got the last water cell — the same rule that puts Trade on the
        // trader's channel rather than the buyer's.
        public const ushort PackState = 67; // server → everyone: the pack is in this state
        public const ushort PackTake  = 68; // player → server: give me what is in this slot

        // ── Agent actions ──
        // Server → peers, on the AGENT's relay. A is what it did (see AgentAction), P and R carry
        // the muzzle or strike ORIGIN and the direction it was aimed — the same ray convention
        // UseItemHold uses, and for the same reason: a peer that re-derives the shot from its own
        // copy of the world would draw it leaving from wherever its own divergent brain was
        // pointing.
        //
        // There is deliberately no request direction. An NPC's decisions are the authority's alone,
        // so unlike every player-driven message above this one only ever travels outward.
        //
        // It exists because gating the AI to the authority took the swings and muzzle flashes off
        // every other machine with it. Before the gate peers DID show them — from their own
        // divergent target and timing, which is the bug — so this is not a new feature but the
        // honest version of something the desync was providing by accident.
        public const ushort AgentActed = 69; // server → peers: this agent attacked, here and thus

        // ── Scene transitions ──
        // Sent to the INITIATOR's channel, because a transition's effects belong to exactly one
        // pair of eyes: the fade, the audio muffle and the walk-through cutscene are things that
        // happen to the person going through the door, not to everyone who can see the door.
        //
        // Two ids rather than one because the handshake genuinely runs both ways.
        // SceneEffects travels outward with A carrying the phase (see SceneEffectPhase). Broadcast
        // and filtered by ownership on arrival, the same shape RopeTug and Damaged use, because
        // this layer has no unicast.
        //
        // SceneEffectsDone is the answer, and it exists because EffectHandle.AwaitOutPhase is
        // allowed to BLOCK the load — a walk-through cutscene is supposed to finish before the
        // teleport. The server cannot see a client's cutscene finish, so the client has to say so.
        // The server must time that wait out rather than trust it: a client that drops mid-fade
        // would otherwise wedge the transition forever, and SceneTransition already carries a
        // self-healing busy flag for precisely this class of failure.
        public const ushort SceneEffects     = 70; // server → the initiator's owner: run this phase
        public const ushort SceneEffectsDone = 71; // owner → server: my out-phase has finished

        // ── Lasso ──
        // Sent to the THROWER's channel, not the lasso's. Every equipped artifact in this project
        // is plain-Instantiated onto a hand bone, and several of their prefabs — the lasso among
        // them — carry a NetworkObject of their own because dropping the item routes through
        // World.Spawn. That NetworkObject is never spawned while the item is held, so a message
        // sent from the item would resolve to the ITEM's dormant relay and fall through to a local
        // dispatch on every machine. The player above it is the entity with a live wire.
        //
        // The rope's two ends are decided on two different machines and this pair is what joins
        // them. The THROW itself needs nothing here — it rides UseItem/ItemUsed like every other
        // artifact — but the CATCH cannot: the arc finds its target mid-flight, and two machines
        // integrating the same arc at different frame rates can pick different creatures out of a
        // crowd. So the thrower's machine decides what was caught and says so, and everyone else
        // ropes what they are told rather than what they found.
        //
        //   LassoRope   owner → server. B = LassoVerb, Target = the roped subject (Caught only).
        //   LassoRoped  server → everyone. Same payload, relayed untouched.
        //
        // Broadcast to All rather than Others because the reel is a level, not an edge: a machine
        // that missed a message is corrected by the next one, and both handlers act only when the
        // new state differs — the same idempotence rule NetLatch.Apply documents.
        public const ushort LassoRope  = 72; // owner → server, on the THROWER's relay
        public const ushort LassoRoped = 73; // server → everyone

        // ── Ornithopter ──
        // Both on the CRAFT's channel, and they travel in opposite directions because the two
        // halves of a flight are decided on different machines.
        //
        // The craft is spawned by the server (only the server may spawn) but handed to the PILOT,
        // and everything about it from that moment on is the pilot's machine's business: it owns
        // the transform, it reads the stick, it runs the flight model. So the launch has to be
        // carried outward to that machine rather than performed where it was decided — a launch
        // applied on the server alone lands on a copy NetAuthority has already switched to
        // following the wire, which is a craft nobody is flying.
        //
        //   CraftLaunch  server → everyone.  R = heading, A = launch airspeed in cm/s.
        //   CraftDown    owner → server.     P = where to stand the pilot, A = closing speed in
        //                                    cm/s, B = 1 when the craft flew INTO something.
        //
        // CraftDown is the answer coming back. The pilot's machine is the only one integrating the
        // flight, so it is the only one that can see the landing — but what a landing COSTS, and
        // the despawn and dismount that follow it, are the server's to decide, which is what this
        // hands over. Speeds travel as centimetres per second because NetArg has no float field.
        public const ushort CraftLaunch = 74; // server → everyone, on the CRAFT's relay
        public const ushort CraftDown   = 75; // owner → server, on the CRAFT's relay

        // ── Backpacks, continued: free placement ──
        // Sent to the PACK OWNER's channel, like PackTake, and for the same reason: two people can
        // be reaching into one pack and only one machine may decide which of them got the space.
        //
        // There is no answer coming back and none is needed. A move that the server allows changes
        // the pack's layout, and BackpackNetwork's NetworkList publishes that to everyone on its
        // own; a move it refuses changes nothing, and the requester's display is already showing
        // the truth because nothing was applied optimistically.
        //
        // The item is named POSITIONALLY — "whatever is under this point" — rather than by id, the
        // same trick PackTake uses. An InventoryItem.ID is a string and NetArg has no string
        // field, and an index would name different items on two machines mid-reconcile.
        //
        //   A  the source surface in the low byte, the destination surface in the next one up.
        //   B  yaw in whole degrees, 0..359.
        //   P  the point that was grabbed, in the source surface's uv: X and Z, Y unused.
        //   R  where it is being put, in the destination surface's uv: X and Z, Y and W unused.
        //      Abusing a Quaternion as two floats is ugly and deliberate — a placement needs two
        //      uvs, NetArg carries one Vector3, and inventing a second message to hold the other
        //      half would make a single placement a two-packet handshake that can half-arrive.
        public const ushort PackMove = 76; // player → server: slide this item to there

        // 77 was PackDrop, retired 2026-08-25: an item dragged clean off the mat left the pack and
        // landed on the ground. The click interaction has no such verb — gear lives in the hotbar
        // or on the pack and there is no third place for it to be — so there is nothing left to
        // send. The number is not reused.

        // The way IN from the hotbar, and the only one: an item taken into the player's hand and
        // put down on the pack. PackTake is its mirror and this is the half that was missing —
        // before it, an item that reached the hotbar could only ever leave it by being dropped on
        // the ground.
        //
        // On the PACK OWNER's channel with the other three, and the server does BOTH halves of the
        // transfer: PlayerInventoryNetwork replicates the hotbar losing a slot and BackpackNetwork
        // replicates the pack gaining a placement, so nothing here has to travel back.
        //
        // Unlike its three neighbours the item is named by INDEX rather than positionally, and the
        // difference is real rather than an inconsistency: a hotbar slot is a fixed numbered box
        // the player pressed a numbered key for, where a pack placement is a point on a mat whose
        // contents two people are rearranging.
        //
        //   Target  the player reaching in, as for PackTake. Their hotbar is the source.
        //   A       the hotbar slot index in the low byte, the surface being placed on in the next
        //           one up — BackpackController.EncodeStowTarget, the same byte packing PackMove
        //           uses for its two surfaces.
        //   B       yaw in whole degrees 0-359, PackMove's own convention: yaw has to travel now,
        //           because an item is turned in the player's hand before it is put down, and a
        //           server that placed everything at zero would land it on cells the player never
        //           saw highlighted. There is no "the cursor was nowhere" sentinel any more — a
        //           stow is only ever sent for a spot the player pointed at and watched go green,
        //           and a spot the server finds taken is refused rather than first-fitted.
        //   P       where on that surface, in its uv: X and Z, Y unused.
        public const ushort PackStow = 78; // player → server: put my hotbar slot on the pack

        // Server → everyone, on the VICTIM player's relay: this player has been flung and their
        // owning machine must apply the velocity in P (m/s, world space).
        //
        // Broadcast because this layer has no unicast — every machine receives it and only the
        // machine that owns the victim acts (see FlungBody). The server cannot apply it itself:
        // the player body is owner-authoritative, so a server-side push is overwritten within a
        // tick. Successor to the retired RopeTug (62) shape.
        public const ushort Flung = 79; // server → everyone, on the VICTIM's relay

        // Something went through this player's portal pair and closeOnTraversal shut it — on the
        // machine that moved the traveller. Traversal is detected independently per machine from
        // local physics, and a peer watching an interpolated remote body can miss the plane
        // crossing entirely (the exit need not even be behind the entry's plane), which left the
        // pair standing on that machine for the rest of its lifetime while it was gone everywhere
        // else — and walkable, since that peer owns their own body.
        //
        // Sent by the machine that OWNS the traveller: every machine may detect a crossing
        // cosmetically, but only the owner's detection actually moved the body. No payload — the
        // pair only ever holds two apertures and a traversal shuts both.
        public const ushort PortalsUsed = 80; // traveller's owner → server, on the SHOOTER's relay
        public const ushort PortalsShut = 81; // server → everyone else, on the SHOOTER's relay

        // Server → everyone, on the VICTIM's relay: this body has been knocked down. Every machine
        // presents it going limp, with P as the impulse handed to the hips (m/s, world space).
        //
        // Broadcast for the same reason Flung (79) is: bone transforms are not replicated, so a
        // ragdoll is not something one machine can do on another's behalf — every machine has to
        // run its own. Position still converges, through the authority split the entity already
        // has: an agent's root follows the server's NetworkTransform, a player's follows its owner.
        // Only the limb poses differ between machines, and nothing reads a corpse's elbow.
        //
        // A message of its own rather than a flag on Flung, because Flung is shared three ways and
        // one of them is self-inflicted: GravelBlasterArtifact flings the HOLDER as self-propulsion
        // (GravelBlasterArtifact.Backfire). A ragdoll hung off Flung would knock players down every
        // time they fired their own gravel blaster.
        public const ushort Knockdown = 82; // server → everyone, on the VICTIM's relay

        // Server → everyone, on the MOUNT's relay: this agent has been thrown and the machine that
        // owns it must run the leap.
        //
        // The agent counterpart of Flung (79), and it exists for the same reason. A mount is owned
        // by its RIDER while ridden — MountNetworkSync hands ownership over so the motion
        // replicates outward from them — so a leap applied on the server writes a transform the
        // rider's next state update overwrites, silently. Broadcast because this layer has no
        // unicast; every machine hears it and only the owner acts (see NavMeshAgentMotor).
        //
        //   P  the leap, as direction × horizontal distance in metres. One field rather than two
        //      because that is exactly what the caller already computed, and a magnitude carries
        //      the distance for free.
        //   A  peak height, in centimetres — NetArg has no float field, the same reason
        //      CraftLaunch sends speeds in centimetres per second.
        //   B  duration, in milliseconds.
        public const ushort Leap = 83; // server → everyone, on the MOUNT's relay

        // What the swinger's rope is doing, when the item's own use messages cannot say it.
        //
        // On the SWINGER's channel, like the lasso's rope verbs and for the same reason: the
        // artifact prefab carries a NetworkObject — it has to, because dropping the item routes
        // through World.Spawn — and that NetworkObject is never spawned while the item is in a
        // hand, so a send from the item itself would resolve to a dormant relay and quietly run on
        // the local machine only.
        //
        // Two things need saying that a press cannot. A rope that let GO by itself — the swinger
        // reached the anchor, or the winch stalled — fires inside one physics step and boosts the
        // body straight back out of the arrival sphere, so a peer watching an interpolated
        // transform may have no sample inside it at all and would draw that rope forever. And a
        // rope that is still OUT has to be re-stated for somebody who has just joined, who was not
        // listening when the press went round and would otherwise watch a player arc through the
        // air on nothing.
        //
        //   A       the verb — see GrappleVerb.
        //   P       the anchor point, and R the surface normal, for On. Unused for Off.
        //   Target  what the rope is set into, for On, when that has a networked identity.
        public const ushort GrappleRope = 84; // owner/server → everyone, on the SWINGER's channel

        // Server → everyone: this rope has been pulled apart.
        //
        // On the channel of one of the rope's own ANCHORS, because a Leash is a bare GameObject
        // with no NetworkObject and therefore no relay of its own. Which anchor is in Target, and
        // P names the rope in that anchor's local space — the same addressing an untie uses, and
        // for the same reason: a bare world point stops naming a rope the moment the thing it is
        // tied to moves.
        //
        // The verdict is the server's alone. Every machine can compute the stretch, but they
        // compute it from interpolated endpoints and can land on opposite sides of the threshold —
        // and a rope that broke on one machine and not another is permanent, because the machine
        // that kept it goes on constraining a creature nobody else can see a rope on.
        public const ushort LeashSnap = 85; // server → everyone, on one of the rope's ANCHORS

        // One aperture's time ran out. On the SHOOTER's channel, with A naming which barrel.
        //
        // A pair of its own rather than reusing PortalsUsed/PortalsShut (80/81), which are
        // deliberately payload-free because a traversal shuts BOTH ends: routing an expiry through
        // them would destroy an aperture's partner everywhere, and PeekBarrel's "an EMPTY barrel
        // always wins" rule exists precisely for the state where one has expired and the other has
        // not. Losing both when one times out would be a feel regression, not a fix.
        //
        // It exists because the lifetime is counted from each machine's own Present moment, so the
        // same aperture reaches zero up to a round trip apart on different machines. A peer that
        // still holds one the shooter has dropped is a portal only that player can walk through —
        // and since they own their own body, walking through it genuinely moves them, on a machine
        // where nobody else sees a portal at all.
        //
        //   A  the barrel, PortalPair.Primary or PortalPair.Secondary.
        public const ushort PortalExpired = 86; // owner → server, on the SHOOTER's channel
        public const ushort PortalGone    = 87; // server → everyone else, on the SHOOTER's channel

        // ── Net gun ──
        // Sent to the SHOOTER's channel, not the net's. The net carries no NetworkObject at all —
        // it is drawn from a shared seed rather than replicated — so it has no relay of its own to
        // send from, and the player holding the gun is the entity with a live wire.
        //
        // The SHOT needs nothing here: it rides UseItem/ItemUsed like every other artifact, and
        // NetGunFlight is closed-form, so every machine draws the identical arc from the muzzle,
        // aim and seed the press already carried.
        //
        // The CATCH cannot ride that. Two machines integrating one arc at different frame rates can
        // pick different creatures out of a crowd — the same reason LassoRoped exists — so the
        // server decides what was caught and says so, and everyone else nets what they are told.
        // NetArg has no list field, so a net that sweeps up three creatures sends three messages
        // sharing one net id in A.
        //
        //   Snared      server → everyone. Target = the captive, A = net id.
        //   SnareFreed  server → everyone. A = net id; Target = 0 for the whole net.
        //
        // Broadcast to All rather than Others, and both handlers act only when the state differs,
        // so a machine that missed one is corrected by the next — the idempotence rule
        // NetLatch.Apply documents.
        public const ushort Snared     = 88; // server → everyone, on the SHOOTER's relay
        public const ushort SnareFreed = 89; // server → everyone, on the SHOOTER's relay

        // ── Arrival ──
        // Sent on the SHIP's channel, not the player's. The ship is the thing that has seats, it is
        // a spawned NetworkObject with a relay of its own, and it outlives any one player's seating.
        //
        // Two channels here for the reason MountNetworkSync sets out at length. This pair is the
        // EVENT, acted on immediately by everybody present. SeatedRider's own NetworkVariable is the
        // STATE, and it has to exist because NetworkVariable change events never replay: a client
        // that connects while the ship is already falling was not here for the event, and with only
        // this pair it would spawn standing on the ground watching its crew drop out of the sky.
        //
        // Broadcast to All rather than Others, and both handlers act only when the state differs, so
        // a machine that missed one is corrected by the next — the idempotence rule NetLatch.Apply
        // documents.
        //
        //   Target  the player being seated or released.
        //   A       which seat, as an index into the ship's ordered ShipSeat list.
        public const ushort TakeSeat  = 90; // server → everyone, on the SHIP's relay
        public const ushort LeaveSeat = 91; // server → everyone, on the SHIP's relay

        // (92 and 93 were SeatRequest/SeatRelease, retired: passenger chairs are ordinary mounts —
        //  PlayerShipBuilder gives every non-helm chair its own MountModule — so a second, bespoke
        //  way to sit down was two mechanisms for one job. Not reused; ids travel between builds.)

        // "Let me out of my arrival seat." Client → server, on the SHIP's relay.
        //
        // Its own message rather than a client-side call to SeatedRider.Release, because releasing
        // is a server decision and the seat's occupancy is server-written state. The server
        // releases the SENDER's own body and nobody else's — see SeatedRider.OnLeaveSeatRequested,
        // which checks ownership rather than trusting the reference on the wire.
        //
        //   Target  the player asking to get up.
        public const ushort LeaveSeatRequest = 94; // client → server, on the SHIP's relay
    }
}
