# SoundStage VR

SoundStage VR is a virtual reality music sandbox. Build your own instruments and studio
out of oscillators, drum machines, sequencers, samplers, loopers, filters and mixers,
patch them together with cables, and play them with your hands — alone, or together
with other people on the same network.

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

**Multiplayer on your local network** — no accounts, no servers:
1. Put every headset (and the PC, if you like) on the **same Wi-Fi network**, running the
   **same SoundStage version**.
2. Turn on **MULTIPLAYER** in the settings panel (gear menu). The first one becomes the
   host, the others find it and join; the button shows *HOSTING (n)* or *JOINED (n)* and
   the ping.
3. Everyone sees and hears the same patch: devices, cables, knobs, notes, tapes, tempo
   and sequencers stay in step. If the host leaves, the next player takes over.
4. In the same physical room, use **ALIGN ROOM**: everyone touches the same two spots
   (a metre or more apart) so the virtual room lines up with the real one.
   **MOVE ROOM** then moves the room for everyone with the stick.

Each headset makes its own sound from the shared patch, so only a few KB/s cross the
network. Built-in samples work everywhere; your own recordings and custom samples are
still only on the headset that made them (sharing them is planned), as is the
microphone. Internet play is next on the list.

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
* **[Runtime AudioClip Loader](http://u3d.as/hEP)** (Unity Asset Store) is required and
  isn't included: import it into `Assets/third_party/RuntimeAudioClipLoader`. It loads the
  samples (WAV, OGG, MP3).
* The audio engine's native code is in `Assets/SoundStageNative` (Visual Studio project
  for the Windows DLL, Android libraries prebuilt in `Assets/Plugins/Android`).
* Quest: Android platform, Oculus XR plug-in on. PC: Windows platform; the Oculus XR
  plug-in on gives VR through the Meta runtime, and the build falls back to desktop mode
  when there's no headset.
* Multiplayer: every machine in a session needs a build of the same code — builds with
  different network messages don't see each other.
* **Test harness**: *TEST HARNESS* in the settings panel (or `-harness` on the PC command
  line) makes the app play with itself — spawning, wiring, playing and deleting devices at
  random, also in a multiplayer session — until it sees real input. It logs every action,
  status, per-device state and every error to `harness_<date>.log` next to the Player log.

## Included dependencies
* [Mirror](https://github.com/MirrorNetworking/Mirror) — networking (multiplayer)
* [Sonic Ether Natural Bloom & Dirty Lens](https://github.com/sonicether/SE-Natural-Bloom-Dirty-Lens) — the glow and bloom
* [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — compression (saves, sample packs)
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

## License
[Apache 2.0](LICENSE)
