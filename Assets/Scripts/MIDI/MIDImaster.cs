// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MIDImaster : MonoBehaviour {

  public static MIDImaster instance;
  public List<MIDIdevice> inputDevices = new List<MIDIdevice>();
  public List<MIDIdevice> outputDevices = new List<MIDIdevice>();
  List<MIDIdevice> connectedInputDevices = new List<MIDIdevice>();
  List<MIDIdevice> connectedOutputDevices = new List<MIDIdevice>();

  void Awake() {
    instance = this;
    EnsureSender();
  }

  public int GetIDbyName(bool input, string n) {
    // (the MIDI library is Windows-only: on the Quest this throws, and used to break
    // loading any patch that had a MIDI connection in it)
    try { return GetIDbyNameUnsafe(input, n); }
    catch (System.Exception) { return -1; }
  }

  int GetIDbyNameUnsafe(bool input, string n) {
    return System.Array.IndexOf(WinMidiCaps.Names(input), n);
  }

  public void RefreshDevices(bool input) {
    // the MIDI library is Windows-only: on the Quest there are simply no local devices
    // (other players' devices are offered by Net/NetMidi.cs)
    try { RefreshDevicesUnsafe(input); }
    catch (System.Exception) { if (input) inputDevices.Clear(); else outputDevices.Clear(); }
  }

  void RefreshDevicesUnsafe(bool input) {
    string[] names = WinMidiCaps.Names(input);
    List<MIDIdevice> list = input ? inputDevices : outputDevices;
    list.Clear();
    for (int i = 0; i < names.Length; i++) list.Add(new MIDIdevice(names[i], input, i));
  }

  // an already open device (one device can feed or be fed by several SoundStage devices)
  MIDIdevice getConnectedDevice(bool input, string name) {
    foreach (MIDIdevice m in input ? connectedInputDevices : connectedOutputDevices) {
      if (m.name == name) return m;
    }
    return null;
  }

  public void Disconnect(MIDIdevice m, bool input, midiComponentInterface _interface = null) {
    if (input) {
      if (!connectedInputDevices.Contains(m)) return;

      if (_interface != null) {
        m.removeInterface(_interface);
      } else {
        WinMidiIn.Close(m);
        connectedInputDevices.Remove(m);
      }
    } else {
      if (!connectedOutputDevices.Contains(m)) return;

      if (_interface == null) {
        lock (sendLock) {
          WinMidiOut.Close(m);
          connectedOutputDevices.Remove(m);
        }
      }
    }
  }

  // Quitting (2026-11): the PC build hung on quit with MIDI devices open (the
  // Windows MIDI driver can block closing a device). The devices are still closed
  // properly, each step logged, but a standalone build that hasn't exited 3 seconds
  // later is ended, so it never hangs. (Not in the editor.)
  void OnApplicationQuit() {
    Debug.Log("[MIDI] quitting: closing " + connectedInputDevices.Count + " input(s), " + connectedOutputDevices.Count + " output(s)");
    if (!Application.isEditor) {
      var watchdog = new System.Threading.Thread(() => {
        System.Threading.Thread.Sleep(3000);
        System.Diagnostics.Process.GetCurrentProcess().Kill();
      });
      watchdog.IsBackground = true;
      watchdog.Start();
    }
    StopSender();
    // Inputs and outputs are opened straight through Windows now (WinMidiIn/Out), which
    // close at once: they're closed here, before quitting, not left to a background
    // thread. (Closing in the background let the 3-second safety exit kill the app with
    // the input half-closed, which seemed to leave the USB MIDI interface's input dead
    // until it was unplugged or the PC rebooted.)
    for (int i = connectedInputDevices.Count - 1; i >= 0; i--) {
      try { Disconnect(connectedInputDevices[i], true); }
      catch (System.Exception e) { Debug.LogWarning("[MIDI] closing input: " + e.Message); }
    }
    for (int i = connectedOutputDevices.Count - 1; i >= 0; i--) {
      try { Disconnect(connectedOutputDevices[i], false); }
      catch (System.Exception e) { Debug.LogWarning("[MIDI] closing output: " + e.Message); }
    }
    Debug.Log("[MIDI] closed");
  }

  public MIDIdevice Connect(midiComponentInterface _interface, int ID, bool input) {
    string[] names = WinMidiCaps.Names(input);
    if (ID < 0 || ID >= names.Length) return null;
    string devName = names[ID];

    MIDIdevice midOut = getConnectedDevice(input, devName);

    bool success = false;
    if (midOut == null) {
      try {
        if (input) {
          midOut = new MIDIdevice(devName, true, ID);
          int err = WinMidiIn.Open(midOut, ID);
          if (err != 0) throw new System.Exception("midiInOpen error " + err);
          connectedInputDevices.Add(midOut);
          success = true;
        } else {
          midOut = new MIDIdevice(devName, false, ID);
          int err = WinMidiOut.Open(midOut, ID);
          if (err != 0) throw new System.Exception("midiOutOpen error " + err + " (" + WinMidiOut.ErrorText(err) + ")");
          lock (sendLock) connectedOutputDevices.Add(midOut);
          success = true;
        }
      } catch (System.Exception e) {
        Debug.LogWarning("[MIDI] couldn't open " + devName + (input ? " (input)" : " (output)") + ": " + e.Message);
      }
    } else success = true;

    if (success) {
      if (input) midOut.addInterface(_interface);
    }

    return midOut;
  }

  // MIDI out (2026-11): notes come from the main thread and from the audio thread
  // (sequencers, arpeggiators and timelines play keyboards there). Sending from both
  // at once made Windows fail ("DeviceException: Error in the application") after the
  // first note, so everything goes through one queue. It's sent by a thread of its own
  // the moment it arrives - not once a frame, which made arpeggios jitter by up to a
  // frame - and only that thread talks to the output devices (closing takes the same
  // lock). An error is logged once per device instead of every note.
  struct OutMsg { public MIDIdevice dev; public bool cc, on; public int id, channel, val; }
  System.Collections.Concurrent.BlockingCollection<OutMsg> outQueue;
  System.Threading.Thread sender;
  readonly object sendLock = new object();

  void EnsureSender() {
    if (sender != null) return;
    outQueue = new System.Collections.Concurrent.BlockingCollection<OutMsg>();
    var q = outQueue;
    sender = new System.Threading.Thread(() => {
      try { foreach (OutMsg m in q.GetConsumingEnumerable()) Send(m); }
      catch (System.Exception) { } // stopped
    });
    sender.IsBackground = true; // never keeps the app from quitting
    sender.Name = "MIDI out";
    sender.Start();
  }

  void StopSender() {
    if (sender == null) return;
    outQueue.CompleteAdding();
    sender.Join(250);
    sender = null;
  }

  void OnDestroy() { StopSender(); }

  void Send(OutMsg m) {
    lock (sendLock) {
      if (m.dev == null || !connectedOutputDevices.Contains(m.dev)) return;
      // MIDI channels 1-16 in the UI; 0 ("all") sends on channel 1
      int ch = System.Math.Max(0, System.Math.Min(15, m.channel > 0 ? m.channel - 1 : 0));
      int err;
      if (m.cc) {
        if (m.id < 0 || m.id > 127) return;
        err = WinMidiOut.Short(m.dev, 0xB0 | ch, m.id, System.Math.Max(0, System.Math.Min(127, m.val)));
      } else {
        if (m.id < 0 || m.id > 127) return;
        err = m.on ? WinMidiOut.Short(m.dev, 0x90 | ch, m.id, 127) : WinMidiOut.Short(m.dev, 0x80 | ch, m.id, 0);
      }
      if (err != 0) {
        m.dev.outErrors++;
        if (m.dev.outErrors == 1 || m.dev.outErrors % 100 == 0)
          failMessages.Enqueue("[MIDI] sending to " + m.dev.name + " failed (" + (m.cc ? "CC " + m.id + "=" + m.val : "note " + m.id + (m.on ? " on" : " off")) + ", channel " + (ch + 1) + "): midiOutShortMsg error " + err + " (" + WinMidiOut.ErrorText(err) + "), " + m.dev.outErrors + " failure(s) so far, " + m.dev.outSent + " sent");
      } else m.dev.outSent++;
    }
  }

  // (Debug.Log from the sender thread is passed to Update)
  readonly System.Collections.Concurrent.ConcurrentQueue<string> failMessages = new System.Collections.Concurrent.ConcurrentQueue<string>();
  float activityTimer;
  void Update() {
    while (failMessages.TryDequeue(out string msg)) Debug.LogWarning(msg);
    activityTimer += Time.unscaledDeltaTime;
    if (activityTimer > 2f) { activityTimer = 0; WinMidiIn.LogActivity(); }
    // play what came in, for every device listening (their own Update stops while
    // their MIDI panel is closed)
    for (int i = 0; i < connectedInputDevices.Count; i++) {
      foreach (midiComponentInterface c in connectedInputDevices[i].targets) {
        if (c != null) c.PlayQueuedMidi();
      }
    }
  }

  void Enqueue(OutMsg m) {
    var q = outQueue;
    if (q == null) return;
    try { q.Add(m); } catch (System.InvalidOperationException) { } // quitting
  }

  public void outputNote(MIDIdevice _dev, bool on, int ID, int channel) {
    if (_dev.input) {
      Debug.Log("problem");
      return;
    }
    Enqueue(new OutMsg { dev = _dev, on = on, id = ID, channel = channel });
  }

  public void outputCC(MIDIdevice _dev, int val, int ID, int channel) {
    if (_dev.input) {
      Debug.Log("problem");
      return;
    }
    Enqueue(new OutMsg { dev = _dev, cc = true, id = ID, channel = channel, val = val });
  }
}


public class MIDIdevice {
  public string name;
  public int id; // Windows MIDI device number when it was listed
  public bool input;
  public List<midiComponentInterface> _interfaceList = new List<midiComponentInterface>();
  public System.IntPtr outHandle; // output opened by WinMidiOut
  public int outErrors, outSent;

  public MIDIdevice(string _name, bool _input, int _id) {
    name = _name;
    input = _input;
    id = _id;
  }

  // (the MIDI-in callback thread reads 'targets', a copy swapped in whole on each change)
  public volatile midiComponentInterface[] targets = new midiComponentInterface[0];
  public System.IntPtr inHandle; // input opened by WinMidiIn
  public long inKey;

  public void addInterface(midiComponentInterface m) {
    if (!_interfaceList.Contains(m)) _interfaceList.Add(m);
    targets = _interfaceList.ToArray();
  }

  public void removeInterface(midiComponentInterface m) {
    if (_interfaceList.Contains(m)) _interfaceList.Remove(m);
    targets = _interfaceList.ToArray();
  }

};


// MIDI out straight through the Windows multimedia API (2026-11). The MIDI library's
// output sent one note and then failed with "Error in the application" on every
// message after; these few calls replace it (input still uses the library).
public static class WinMidiOut {
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiOutOpen(out System.IntPtr handle, uint deviceId, System.IntPtr callback, System.IntPtr instance, uint flags);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiOutShortMsg(System.IntPtr handle, uint msg);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiOutReset(System.IntPtr handle);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiOutClose(System.IntPtr handle);
  [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  static extern int midiOutGetErrorText(int err, System.Text.StringBuilder text, int size);

  public static int Open(MIDIdevice d, int deviceId) {
    int err = midiOutOpen(out System.IntPtr h, (uint)deviceId, System.IntPtr.Zero, System.IntPtr.Zero, 0);
    if (err == 0) d.outHandle = h;
    return err;
  }

  public static int Short(MIDIdevice d, int status, int data1, int data2) {
    if (d.outHandle == System.IntPtr.Zero) return -1;
    return midiOutShortMsg(d.outHandle, (uint)(status & 0xFF) | ((uint)(data1 & 0x7F) << 8) | ((uint)(data2 & 0x7F) << 16));
  }

  public static void Close(MIDIdevice d) {
    if (d.outHandle == System.IntPtr.Zero) return;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { midiOutReset(d.outHandle); midiOutClose(d.outHandle); } catch (System.Exception) { }
    d.outHandle = System.IntPtr.Zero;
    // (a PC test run froze for ~9 s right after a device using the USB MIDI interface was
    // deleted: say so if closing is what takes the time)
    if (sw.ElapsedMilliseconds > 100) Debug.LogWarning("[MIDI] closing output " + d.name + " took " + sw.ElapsedMilliseconds + " ms");
  }

  public static string ErrorText(int err) {
    if (err == -1) return "not open";
    try {
      var sb = new System.Text.StringBuilder(256);
      return midiOutGetErrorText(err, sb, sb.Capacity) == 0 ? sb.ToString() : "?";
    } catch (System.Exception) { return "?"; }
  }
}

// MIDI in straight through the Windows multimedia API (2026-11). The MIDI library is a
// 32-bit-era DLL; opening an input through it on the 64-bit PC build corrupted memory
// (the app later crashed inside the garbage collector, right after connecting an input
// a Quest player had picked). Like WinMidiOut, this does it with correct 64-bit types;
// the library is still used to list the devices' names.
public static class WinMidiIn {
  delegate void MidiInProc(System.IntPtr handle, uint msg, System.IntPtr instance, System.IntPtr param1, System.IntPtr param2);

  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiInOpen(out System.IntPtr handle, uint deviceId, MidiInProc callback, System.IntPtr instance, uint flags);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiInStart(System.IntPtr handle);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiInStop(System.IntPtr handle);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiInReset(System.IntPtr handle);
  [System.Runtime.InteropServices.DllImport("winmm.dll")]
  static extern int midiInClose(System.IntPtr handle);

  const uint CALLBACK_FUNCTION = 0x30000, MIM_DATA = 0x3C3;

  // kept in a static field so the garbage collector never frees it while Windows calls it
  static readonly MidiInProc callback = OnMidi;
  static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MIDIdevice> open =
    new System.Collections.Concurrent.ConcurrentDictionary<long, MIDIdevice>();
  static long nextKey = 1;

  public static int Open(MIDIdevice d, int deviceId) {
    long key = System.Threading.Interlocked.Increment(ref nextKey);
    open[key] = d;
    int err = midiInOpen(out System.IntPtr h, (uint)deviceId, callback, new System.IntPtr(key), CALLBACK_FUNCTION);
    if (err != 0) { open.TryRemove(key, out _); Debug.LogWarning("[MIDI] midiInOpen(" + deviceId + ") failed: " + err); return err; }
    d.inHandle = h;
    d.inKey = key;
    err = midiInStart(h);
    if (err != 0) { Debug.LogWarning("[MIDI] midiInStart failed: " + err); Close(d); return err; }
    Debug.Log("[MIDI] input " + deviceId + " (" + d.name + ") open and started, handle 0x" + h.ToInt64().ToString("X"));
    return 0;
  }

  public static void Close(MIDIdevice d) {
    if (d.inHandle == System.IntPtr.Zero) return;
    System.IntPtr h = d.inHandle;
    d.inHandle = System.IntPtr.Zero;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { midiInStop(h); midiInReset(h); midiInClose(h); } catch (System.Exception) { }
    open.TryRemove(d.inKey, out _);
    if (sw.ElapsedMilliseconds > 100) Debug.LogWarning("[MIDI] closing input " + d.name + " took " + sw.ElapsedMilliseconds + " ms");
  }

  // Windows' MIDI thread: hand the message to the devices listening (they only queue it)
  [AOT.MonoPInvokeCallback(typeof(MidiInProc))]
  static void OnMidi(System.IntPtr handle, uint msg, System.IntPtr instance, System.IntPtr param1, System.IntPtr param2) {
    System.Threading.Interlocked.Increment(ref calls);
    lastMsg = msg;
    if (msg != MIM_DATA) return;
    try {
      long p = param1.ToInt64();
      lastData = p;
      System.Threading.Interlocked.Increment(ref dataCalls);
      if (!open.TryGetValue(instance.ToInt64(), out MIDIdevice d)) { System.Threading.Interlocked.Increment(ref unknown); return; }
      int status = (int)(p & 0xFF), d1 = (int)((p >> 8) & 0x7F), d2 = (int)((p >> 16) & 0x7F);
      midiComponentInterface[] t = d.targets;
      if (t.Length == 0) System.Threading.Interlocked.Increment(ref noTargets);
      foreach (midiComponentInterface c in t) c.RawMidiIn(status, d1, d2);
    } catch (System.Exception e) { lastError = e.GetType().Name + ": " + e.Message; } // nothing may escape into Windows
  }

  // Diagnostics (2026-11): what Windows has delivered, logged from the main thread when
  // it changes (MIDImaster.Update). MIM_OPEN (0x3C1) arrives once per open input, so a
  // working callback shows at least 1 call.
  static int calls, dataCalls, unknown, noTargets, loggedCalls = -1;
  static uint lastMsg;
  static long lastData;
  static string lastError;
  public static void LogActivity() {
    if (calls == loggedCalls) return;
    loggedCalls = calls;
    Debug.Log("[MIDI] input callback: " + calls + " call(s), " + dataCalls + " MIDI message(s)" +
      (unknown > 0 ? ", " + unknown + " for an input no longer open" : "") +
      (noTargets > 0 ? ", " + noTargets + " with no device listening" : "") +
      ", last 0x" + lastMsg.ToString("X") + (dataCalls > 0 ? " data 0x" + lastData.ToString("X6") : "") +
      (lastError != null ? ", error " + lastError : ""));
  }
}

// The names of the MIDI devices, straight from Windows (2026-11; replaces the MIDI
// library's device lists - see WinMidiIn).
public static class WinMidiCaps {
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  struct MIDIINCAPS {
    public ushort wMid, wPid;
    public uint vDriverVersion;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
    public uint dwSupport;
  }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  struct MIDIOUTCAPS {
    public ushort wMid, wPid;
    public uint vDriverVersion;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
    public ushort wTechnology, wVoices, wNotes, wChannelMask;
    public uint dwSupport;
  }

  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
  [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsW")]
  static extern int midiInGetDevCaps(System.UIntPtr id, out MIDIINCAPS caps, uint size);
  [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW")]
  static extern int midiOutGetDevCaps(System.UIntPtr id, out MIDIOUTCAPS caps, uint size);

  // (throws off Windows: callers catch it - there are no local devices on the Quest)
  public static string[] Names(bool input) {
    if (!DesktopMode.IsWindows) return new string[0];
    uint n = input ? midiInGetNumDevs() : midiOutGetNumDevs();
    string[] r = new string[n];
    for (uint i = 0; i < n; i++) {
      if (input) {
        int err = midiInGetDevCaps(new System.UIntPtr(i), out MIDIINCAPS c, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MIDIINCAPS)));
        r[i] = err == 0 ? c.szPname : "MIDI in " + i;
      } else {
        int err = midiOutGetDevCaps(new System.UIntPtr(i), out MIDIOUTCAPS c, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MIDIOUTCAPS)));
        r[i] = err == 0 ? c.szPname : "MIDI out " + i;
      }
    }
    return r;
  }
}
