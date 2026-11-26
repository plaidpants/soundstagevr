# SoundStage VR

SoundStage VR is a virtual reality music sandbox. Build your own instruments and studio
out of oscillators, drum machines, sequencers, samplers, loopers, filters and mixers,
patch them together with cables, and play them with your hands — alone, or together
with other people in the same room or over the internet.

SoundStage was created by Logan Olson and released as open source by Google in 2017; the
original project is archived and no longer maintained. **This fork keeps it alive**:
it's ported to Unity 2022.3 and runs on Meta Quest headsets and on Windows PCs, with
new devices, multiplayer, hand tracking, passthrough and a desktop version.

This is not an officially supported Google product.

## Download

Get the latest build from the [Releases](https://github.com/plaidpants/soundstagevr/releases) page.

* **Meta Quest 2 / 3 / 3S / Pro** — `soundstage.apk`, also on
  [SideQuest](https://sidequestvr.com/), which picks up each new release. Or sideload it
  yourself: drag the APK onto SideQuest, or `adb install -r soundstage.apk`. If an older SoundStage is installed and
  the install fails with a signature error, uninstall the old one first (that removes
  its saved patches).
* **Windows PC (64-bit)** — `SoundStage-<version>-windows.zip`. Unzip anywhere and run
  `SoundStage.exe`. With a Quest connected through Quest Link / Air Link (or a Rift)
  and the Meta Quest PC app running, it starts in VR; without a headset it starts in
  **desktop mode** (mouse and keyboard). Windows may warn that the app is unrecognised:
  *More info → Run anyway*.

## What's in it

**Devices** — oscillator, noise, keyboard, xylophone roll (with arpeggiator), drum
machine, scale sequencer (the UNTZtrument step sequencer), CV sequencer, timeline
sequencer, sampler, tape library, looper, recorder, drums, maracas, air horn, touch pad,
control cube, ADSR, gain, glide, filter, reverb, gate, splitter, mixer, speakers,
microphone, camera, MIDI in / out, and more. Hold a dial to see its value (the
oscillator shows Hz and the nearest note).

**Playing** — controllers or hand tracking (poke with your fingertips, drum with your
hands), snap turn and stick movement, passthrough mixed reality with the glow over your
real room, and 3D (spatial) audio: *3D AUDIO* in the settings panel switches it between
off, speakers only and everything.

**Multiplayer** — set up in the settings panel (gear menu):
* **MULTIPLAYER: LOCAL / INTERNET** picks the kind, **MULTIPLAYER: ON / OFF** starts and
  stops it, and the line under them shows *OFFLINE*, *SEARCHING...*, *HOSTING (n) 90MS* or
  *JOINED (n) 89MS* (players and ping).
* Everyone needs the **same SoundStage version**. Everyone sees and hears the same patch:
  devices, cables, knobs, notes, tapes, tempo and sequencers stay in step (also
  sequencers started from a button through a splitter). If the host leaves, the next
  player takes over and the music keeps playing.
* Each machine makes its own sound from the shared patch, so only a few KB/s cross the
  network. The microphone and camera stay local.

*Local* — no accounts, no servers: put every headset (and the PC, if you like) on the
same Wi-Fi and turn MULTIPLAYER ON. The first one hosts, the others find it and join.

*Internet* — private rooms with a code:
1. Set MULTIPLAYER: INTERNET and press MULTIPLAYER ON. The first time, read and agree to
   the terms (what's shared, the limits, what Unity's services receive).
2. Press **NEW** for a room code (6 letters and digits), or type one a friend gave you
   (keypad in VR, or the keyboard on the PC); **JOIN**. Your last few codes are kept.
3. Whoever enters a code first hosts that room; everyone else with the code joins. Only
   people with the code can find or join it, so share it only with people you trust.
* Sessions end after 10 minutes with nobody playing, or after 3 hours (press ON again for
  a new one), up to 4 players. Internet multiplayer is always off when SoundStage starts.
* It runs through Unity Gaming Services (Relay and Lobby); see the
  [privacy policy](https://plaidpants.github.io/soundstagevr/SoundStagePrivacyPolicy.html).
  *FORGET MY INTERNET ID* on the terms page deletes your anonymous ID.

*Talking while you play* — SoundStage has no voice chat of its own; use Meta's:
* **Quest players:** start a **call** with the others from the Quest's people / chats
  menu before or while you play. A Meta call or party keeps going inside apps, so you can
  talk while SoundStage runs. Everyone needs to follow each other to call.
* **PC players:** join the same call from the **Meta Horizon** phone app, or use
  Discord (also on Quest) if your group already does.
* **Same room?** Don't call each other: the call picks up the other headset's speakers
  and echoes. Only people in other places need to be on the call.
* Headphones help for everyone on a call: the music from the headset speakers otherwise
  goes into your mic and to the others twice (once from SoundStage, once from the call).

*Same physical room* — use **ALIGN ROOM**: everyone touches the same two spots (a metre or
more apart) so the virtual room lines up with the real one. **MOVE ROOM** then moves the
room for everyone with the stick.

**Samples in multiplayer** — samples are matched by their content, so a renamed or moved
file still counts as the same sample. A sample someone else's patch uses that your machine
doesn't have is sent to you (the tape shows *LOADING*), checked, and kept in a hidden cache;
the library's **Sessions** category lists them. A built-in sample a machine
lacks (deleted, or from an older install) is restored from the app itself, without the
network. **Saving a scene** copies the received
samples it uses into `Samples/Sessions`, so it still plays later. Over the internet,
samples over 5 MB aren't sent (the tape shows *TOO BIG*); on the local network up to 50 MB.
*SHARED SAMPLES* in the settings panel shows the cache size (press twice to clear it).
Loopers: the player who presses SAVE shares that recording, and everyone gets the same loop.

**The PC as a player** — in multiplayer the PC joins as a full player (in VR or desktop
mode). It's also the place for **MIDI hardware**: plug a USB MIDI keyboard or synth into
the PC and every player can pick it on their devices' MIDI IN / MIDI OUT panels (listed
with the PC's name). And it's the **broadcast camera**: its window shows the session for
streaming or recording software.

**Desktop mode controls**
| | |
|---|---|
| Right mouse + move | look around |
| W A S D / Q E | move / down, up (Shift faster, Ctrl slower) |
| Mouse wheel | move forward / back (while holding something: nearer / further) |
| Left click | grab, press, plug cables — on keys it plays them (hold to sustain, slide for runs) |
| Alt + click | lock a xylophone key |
| Tab | menu |
| Delete | the B button (delete / back) |
| Esc twice | quit |

Start `SoundStage.exe -novr` to run in desktop mode even when a headset is connected.

**Your own samples** — on the PC use *IMPORT SOUND SAMPLES* in the desktop window's menu.
On the Quest, copy `.wav`, `.ogg` or `.mp3` files into
`Android/data/com.HardLightLabsLLC.SoundStage/files/SoundStage/Samples/Custom` (USB cable
or SideQuest) and restart SoundStage; they appear in the library's *Custom* category.

**Saving** — patches are saved on the device: on the Quest under
`Android/data/com.HardLightLabsLLC.SoundStage/files/SoundStage`, on the PC under
`%USERPROFILE%\AppData\LocalLow\Hard Light Labs LLC\SoundStage\SoundStage` (the desktop
companion window can open the saves and recordings folders). Patches from older versions
load and play as before.

## Reporting problems

Please open an [issue](https://github.com/plaidpants/soundstagevr/issues) and attach the
log if you can:
* Quest: `adb logcat -s Unity` while it happens, or `adb logcat -d > log.txt` right after
* PC: `%USERPROFILE%\AppData\LocalLow\Hard Light Labs LLC\SoundStage\Player.log`

For multiplayer problems, logs from every machine in the session help most.

## Building from source

* **Unity 2022.3.62f3** with Android Build Support (for the Quest) and Windows Build
  Support (IL2CPP or Mono) for the PC. Open the project and the `main` scene.
* Samples are decoded by the app's own WAV reader and two MIT-licensed decoders included as
  source in `Assets/Plugins`: [NVorbis](https://github.com/NVorbis/NVorbis) (OGG) and
  [NLayer](https://github.com/naudio/NLayer) (MP3). Nothing from the Asset Store is needed
  (earlier versions used the Runtime AudioClip Loader package).
* The audio engine's native code is in `Assets/SoundStageNative` (Visual Studio project
  for the Windows DLL, Android libraries prebuilt in `Assets/Plugins/Android`).
* Quest: Android platform, Oculus XR plug-in on. PC: Windows platform; the Oculus XR
  plug-in on gives VR through the Meta runtime, and the build falls back to desktop mode
  when there's no headset.
* Multiplayer: every machine in a session needs a build of the same code — builds with
  different network messages don't see each other.
* Internet multiplayer needs the project linked to a Unity Cloud project (Project Settings
  > Services) with **Relay** and **Lobby** turned on in the Unity dashboard. A fork has to
  link its own project; builds from different projects don't see each other.
* **Test harness**: *TEST HARNESS* in the settings panel (or `-harness` on the PC command
  line) makes the app play with itself — spawning, wiring, playing and deleting devices at
  random, also in a multiplayer session — until it sees real input. It logs every action,
  status, per-device state and every error to `harness_<date>.log` next to the Player log.

## Included dependencies
* [Mirror](https://github.com/MirrorNetworking/Mirror) — networking (multiplayer)
* [Sonic Ether Natural Bloom & Dirty Lens](https://github.com/sonicether/SE-Natural-Bloom-Dirty-Lens) — the glow and bloom
* [Unity Gaming Services](https://unity.com/products/gaming-services) (Multiplayer Services: Authentication, Lobby, Relay) and Unity Transport — internet multiplayer
* [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — compression (saves, sample packs)
* [NVorbis](https://github.com/NVorbis/NVorbis) (OGG) and [NLayer](https://github.com/naudio/NLayer) (MP3) — sample decoding, MIT licence, included as source in `Assets/Plugins`
* [MIDI-dot-NET](https://github.com/jstnryan/midi-dot-net) — MIDI note names (MIDI input and output now use the Windows MIDI API directly)
* [Meta XR / Oculus Integration](https://developer.oculus.com/downloads/package/unity-integration/) and the Unity Oculus XR plug-in — VR, hand tracking, passthrough, spatial audio

## Credits

###### ORIGINAL SOUNDSTAGE
Created by **Logan Olson** · Sound design (samples) **Reek Havok** · Programming consultant
**Giray Ozil** · Music consultant **Ron Fish**

###### THIS FORK
Unity 2022 / Quest port, multiplayer, desktop version and new devices by **James**
([plaidpants](https://github.com/plaidpants)). Gain, Glide and the CV sequencer come from
[ludzeller's fork](https://github.com/ludzeller/soundstagevr); the scale sequencer is a
port of James's [UNTZtrument step sequencer](https://github.com/plaidpants/Adafruit_UNTZtrument).

## Privacy

SoundStage has no accounts, ads or tracking. Internet multiplayer uses Unity Gaming
Services, which receive your IP address and an anonymous ID; see the
[privacy policy](https://plaidpants.github.io/soundstagevr/SoundStagePrivacyPolicy.html).
Contact: soundstagevr@gmail.com

## License
[Apache 2.0](LICENSE)
