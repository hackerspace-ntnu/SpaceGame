# Third-party notices

Assets in this repository that were not made here, with the licence each one carries. Keep
this file current: a licence with an attribution clause is a promise the shipped game has to
keep, and the credits line below is part of that promise.

## Red Planet Rampage — the Clanker robot

**What:** the robot body `YiiHaw.fbx` (the "Y11-H4W" character) and its nine locomotion clips
(`rig.001_{idle,Walk,back,SideStepLeft,SideStepRight,CrouchForward,CrouchIdle,CrouchRight,Leap}.anim`),
under [`Assets/ThirdParty/RedPlanetRampage/`](Assets/ThirdParty/RedPlanetRampage/). Used as
the body of the Clanker faction. Materials, shaders, textures, controllers and code from that
project are **not** included; the body is dressed from this project's own palette.

**From:** <https://github.com/hackerspace-ntnu/Red-Planet-Rampage>, commit
`25835ac03389d0be948abeda8c31f68965d682b9` (2025-11-09), with the developers' permission.

**Licence:** BSD 4-Clause, Copyright (c) 2022, Hackerspace NTNU. Full text in
[`Assets/ThirdParty/RedPlanetRampage/LICENSE.md`](Assets/ThirdParty/RedPlanetRampage/LICENSE.md).

**What the licence asks of us:**

1. Keep the copyright notice and the licence text with the assets (done: the file above).
2. Reproduce the notice in the documentation or materials shipped with a build (this file
   ships with the game).
3. Any advertising material that mentions the Clankers must carry this line, verbatim:

   > This product includes software developed by Prosjekt: Spill, of Hackerspace NTNU.

   There is no credits screen in the game yet (2026-09-07); when one exists, this line goes on it.
4. Do not use Hackerspace NTNU's name or its contributors' names to endorse this game without
   written permission.

**Lore carried over, with thanks:** the robots were sent to terraform Mars and received
western films in place of their instructions. In SpaceGame they have since moved on to this
planet — see `docs/superpowers/specs/2026-09-07-faction-system-design.md` §3.7.

## Kenney — Sci-Fi Sounds

**What:** `lowFrequency_explosion_001.ogg`, shipped as
[`Assets/StreamingAssets/Audio/storm_ward_pulse.ogg`](Assets/StreamingAssets/Audio/storm_ward_pulse.ogg),
the storm ward's pulse.

**From:** Kenney, "Sci-Fi Sounds" 1.0 (2020-10-11), <https://kenney.nl/assets/sci-fi-sounds>,
downloaded 2026-09-17.

**Licence:** Creative Commons Zero (CC0 1.0), <http://creativecommons.org/publicdomain/zero/1.0/>.
Nothing is owed; crediting Kenney (www.kenney.nl) is invited, not required.

## Concentus — the Opus voice codec

**What:** the portable C# implementation of the Opus audio codec, vendored as source under
[`Assets/ThirdParty/Concentus/`](Assets/ThirdParty/Concentus/). Used by the proximity voice
chat to encode and decode speech, and its Speex resampler port converts microphones that do
not record at 48 kHz. Compiled as its own assembly (`Concentus.asmdef`).

**From:** <https://github.com/lostromb/concentus>, version 2.2.2, commit
`3885c4e46513ef0fc81fca100189e54f1714c6ca` (2025-09-27).

**What was left out:** `AssemblyInfo.cs` (`InternalsVisibleTo` entries for the upstream test
projects) and `Concentus.csproj`. Everything else is upstream, unmodified — including
`Native/`, which is never called: the voice code constructs `OpusEncoder`/`OpusDecoder`
directly rather than going through `OpusCodecFactory`, so no native libopus is ever probed
for or P/Invoked. Keeping the tree unpatched is what makes it updatable.

**Licence:** BSD 3-Clause, © Xiph.Org Foundation, Skype Limited, CSIRO, Microsoft Corp.,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry and Logan Stromberg.
Full text in [`Assets/ThirdParty/Concentus/LICENSE.md`](Assets/ThirdParty/Concentus/LICENSE.md).

**What the licence asks of us:**

1. Keep the copyright notice and licence text with the source (done: the file above).
2. Reproduce the notice in the documentation or materials shipped with a build (this file
   ships with the game).
3. Do not use the names of Xiph.Org, the IETF, the Internet Society or the contributors to
   endorse this game without written permission.
