# In-Game Chat

**Date:** 2026-08-17
**Status:** implemented

Minecraft-style chat: a log down the bottom left that fades on its own, and a box you type into
when you press **T**. Multiplayer from the start. Not persisted — the log lives for one session.

## Decisions

| Question | Answer |
| --- | --- |
| Message kinds | Player messages, system messages (join/leave), slash commands |
| Commands at launch | `/tp <player>`, any player may run it. `/help` comes with the registry. |
| While typing | Input and mouse look stop, cursor frees — **the clock keeps running** |
| Log lifetime | One session, 100 lines, survives scene loads. Cleared when a session starts. |
| Where | In-game only. Not the main menu, not the lobby. |
| Key | **T**. Enter sends and closes, Escape closes without sending. |

## Why chat does not use `NetMessaging`

Every other gameplay system in this project networks itself in three lines through
`NetRelay`/`NetChannel`. Chat cannot, for two structural reasons:

1. **`NetArg` has no string field.** It is one fixed struct of ints, a `Vector3` and a
   `Quaternion`, shared by every message in the game. Widening it for chat would put a 128-byte
   field on every damage, mount and item-use message to serve one feature.
2. **`NetTo` has no unicast.** Its three directions are `Server`, `All` and `Others`. A command's
   answer — "no player called Bob is in this session" — is worked out on the server and belongs to
   the person who asked, nobody else.

So chat gets its own three RPCs. Everything else about it is ordinary code with no netcode in it.

## Where it lives

`ChatNetwork` is a component on the **`NetworkGameManager` prefab**. That object already carries a
`NetworkObject`, is placed in `persistentScene` — which is loaded beneath every gameplay scene,
including an additively loaded minigame arena — and spawns before the first player does.

The alternative, a component on the player prefab, was rejected: it would exist once per human,
lose messages that arrive before your own body spawns, and have nowhere to put a system message on
a machine with no local player.

## The pieces

```
ChatText          pure    sanitise: control chars, length, byte length, TMP markup
ChatMessage       pure    one line: kind, sender, text, arrival time
ChatLog           static  100-line ring buffer + Added/Cleared events. No Unity types but Time.
ChatCommands      static  registry + parser. Knows nothing about players or teleports.
ChatBuiltinCommands       /tp and /help, and the player-name resolver they share.
ChatNetwork       NetBeh  three RPCs, the throttle, join/leave announcements.
ChatUI            Mono    the canvas. Bootstrapped from a static, built lazily.
```

Each half can be understood without the other. The log and the parser are covered by edit-mode
tests (`Assets/Game/Tests/Editor/ChatTests.cs`) with no session and no scene.

### Data flow

```
type → ChatUI.OnSubmit → ChatNetwork.Send
                             ├─ offline / not spawned → handled here, exactly as single-player
                             ├─ we are the server     → Handle(localClientId, text)   [no round trip]
                             └─ otherwise             → SubmitRpc  ──► server: Handle(sender, text)

Handle(sender, text)
  ├─ throttled?     → NoticeRpc  → that client only
  ├─ starts with /  → ChatCommands.Execute → NoticeRpc → that client only
  └─ otherwise      → BroadcastRpc(nameOf(sender), text) → everyone → ChatLog.AddPlayer

ChatLog.Added → ChatUI adds a row
```

### What the server decides, and what it does not trust

- **The name is looked up from the sender's replicated `PlayerIdentity`**, never taken from the
  message. No client can put words in another player's mouth.
- **The text is sanitised twice.** The sender already did it, on a machine this server does not
  get to trust.
- **A token bucket per client**: four messages in hand, then one every 1.2 s. Exceeding it is
  answered once per spree, not once per message — a throttle that replies to a flood with a flood
  is not a throttle.
- **A command that throws is reported and logged**, not propagated. A command is player input, and
  player input must not be able to kill the server's message pump.

## Markup containment

The view draws every name and every message body inside `<noparse>…</noparse>`, so markup a player
types shows up as the characters they typed. That has exactly one hole: typing the closing tag
yourself ends the block early and hands the rest of the line to TMP as live rich text —
`</noparse><size=400%>` is a message that covers everyone else's screen. `ChatText.Sanitize`
breaks any closing tag, case-insensitively, because TMP matches tags case-insensitively. **The two
halves only work together**; neither is sufficient alone.

## Control handover

`GameplayMenuScope` gained a `freezeTime` parameter and a second owner set. Freezing is now decided
by whether *any* current owner wants it, rather than by whoever entered first:

- The pause menu enters with `freezeTime: true` — unchanged behaviour, the world waits.
- Chat enters with `freezeTime: false` — input and cursor are taken, the world runs on.

`Enter` still returns false where there is no local player, which is what makes chat in-game-only
without a second test for it.

## `/tp`

Resolution is exact case-insensitive match first, then a unique prefix — so `/tp fer` works but
`/tp p` in a session of Pia and Per is refused rather than teleporting you to whichever of them
spawned first.

The move goes through **`NetworkedTeleport.Move`**, not the transform. The player's
NetworkTransform is owner-authoritative: a server that writes a remote player's position has it
overwritten by that player's next state update, within a tick and silently. You land 1.6 m behind
the target, facing the way they face, on a flattened forward vector so a target on a slope does not
drop you through the ground.

## Life cycle

The log is cleared when `ChatNetwork` **spawns**, not when a session ends. A spawn is an event
every peer observes — a fresh host, or this client joining one. A hard disconnect raises nothing at
all, so clearing on disconnect would leave the previous session's chat in the buffer.

A join is announced only once the joiner's chosen name has replicated (bounded to 6 s). The
connection callback fires long before that client's player object exists, so announcing on the
callback names "Player 3" somebody who is about to become somebody. A leave uses a cached name,
because by then the player object is gone.

## Files

| File | |
| --- | --- |
| `Assets/Game/Scripts/Core/Multiplayer/Chat/` | the six new chat scripts |
| `Assets/Game/Scripts/Presentation/UI/Pages/ChatUI.cs` | the canvas |
| `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab` | `ChatNetwork` added |
| `Assets/Game/Scripts/Core/Multiplayer/PlayerIdentity.cs` | `HasPublishedName` added |
| `Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs` | `freezeTime` |
| `Assets/Game/Settings/Input/InputSystem_Actions.inputactions` | `UI/Chat` → `<Keyboard>/t` |
| `Assets/Game/Settings/Input/InputControls.cs` | the generated wrapper, edited to match |
| `Assets/Game/Tests/Editor/ChatTests.cs` | sanitiser, ring buffer, command parser |

`InputControls.cs` embeds its own copy of the action JSON and **that** is what binds at runtime, so
both files were edited in step. Editing only the `.inputactions` would have changed nothing.

## Deliberately not built

- Persistence. The log dies with the session, by decision.
- Lobby chat. Would need chat off the `NetworkGameManager` object; `ChatNetwork` is the easiest
  thing to lift onto its own bootstrapped object if that changes.
- Whispers and team channels. `ChatKind.Notice` already proves the unicast path works, so a
  `/w <player>` command is a handler and nothing else.
- Rebinding T in the settings menu. The action exists; the settings page has no rebinder yet.
