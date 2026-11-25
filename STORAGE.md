# Where SoundStage keeps its files

Unity's `persistentDataPath` is the app's own folder. SoundStage also uses the folder
**above** it (shown below as *app folder*) for the files it unpacks from the build.

| | Windows | Quest |
|---|---|---|
| app folder | `%USERPROFILE%\AppData\LocalLow\Hard Light Labs LLC\` | `/sdcard/Android/data/com.HardLightLabsLLC.SoundStage/` |
| persistentDataPath | *app folder*`SoundStage\` | *app folder*`files/` |
| save folder (`masterControl.SaveDir`) | persistentDataPath`\SoundStage\` | persistentDataPath`/SoundStage/` |

The Windows build and the Unity Editor share the same folders on one PC.

## App folder — unpacked from the build

| Path | What | Written by | Safe to delete? |
|---|---|---|---|
| `Examples\` | The example scenes (`*.xml`), incl. `startExample.xml`. `.build` records which build unpacked them; a new build unpacks them again. | `exampleManager` from `StreamingAssets/Examples.tgz` (Quest and Windows build; **not** the Editor) | Yes, comes back on next launch of a build |
| `Samples\` | The built-in sample library (the tape library's built-in categories). `.installed` lists what's been unpacked, so files you delete stay deleted. | `BuiltinSamples` from `StreamingAssets/Samples.tgz` (not the Editor) | Yes, but delete `.installed` too to get them all back |

## persistentDataPath — caches and logs

| Path | What | Written by | Safe to delete? |
|---|---|---|---|
| `NetSamples\` | Samples received from other players (`<hash>.wav`, plus `<hash>.src` for the original file when it wasn't a WAV). Size-limited; oldest go first. | `SampleCache` / `NetSamples` | Yes (they're fetched again when needed) |
| `sampleindex.txt` | Fingerprints (SHA-256) of library samples, so each file is only read once. | `SampleIndex` | Yes, rebuilt in the background |
| `builtin_index.txt` | Fingerprints of the samples inside `Samples.tgz`, once per build. | `BuiltinSamples` | Yes, rebuilt in the background |
| `Player.log`, `Player-prev.log` | This run's and the previous run's log (Windows build). Quest: `adb logcat -s Unity`. | Unity | Yes |
| `harness_<date>.log` | Only when started with `-harness` (automatic random-action test). | `TestHarness` | Yes |
| `Unity\` | Unity's own cache (analytics etc.). | Unity engine | Yes |

## Save folder — your work

| Path | What | Written by | Safe to delete? |
|---|---|---|---|
| `Saves\` | Saved scenes (`*.xml`). | Save menu (`pauseMenu`, `uiPanelComponentInterface`) | **No — your scenes** |
| `Samples\Custom\` | Your own samples (`.wav`, `.ogg`, `.mp3`). | You (copy files in, or IMPORT SOUND SAMPLES on the PC) | **No** |
| `Samples\Recordings\` | Looper and recorder saves (`rec_…` = recorder, others = looper). | `bufferToWav` | **No** |
| `Samples\Sessions\` | Samples from multiplayer sessions that a saved scene uses (kept out of the cache so the scene keeps working). | `SessionSamples.KeepForScene` | Only if no saved scene needs them |
| `MySamples\` | Old, unused (the original app made it empty). Removed at start-up when empty; if you put files there, move them to `Samples\Custom\`. | — | Yes |

## Settings (not files)

Settings are in Unity's PlayerPrefs, not in these folders:
glow quality/brightness, audio quality, environment, MIDI out, multiplayer on/off and
mode, recent room codes, the multiplayer terms, controller tool positions.

- Windows build: registry `HKEY_CURRENT_USER\Software\Hard Light Labs LLC\SoundStage`
- Editor: `HKEY_CURRENT_USER\Software\Unity\UnityEditor\Hard Light Labs LLC\SoundStage`
- Quest: `/data/data/com.HardLightLabsLLC.SoundStage/shared_prefs/` (not reachable without root; clearing the app's data resets them)

## Backing up / moving to another machine

Copy the **save folder** (`Saves`, `Samples\Custom`, `Samples\Recordings`,
`Samples\Sessions`). Everything else is rebuilt or re-unpacked by the app.
