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

public class timelinePlayer : MonoBehaviour, INetBeatSync {
  public xHandle playheadHandle;
  timelineComponentInterface _deviceInterface;
  float lastBeatTime = 0;

  float curGridPosition;

  public bool looping = true;

  Dictionary<int, timelineEvent> activeEvents = new Dictionary<int, timelineEvent>();

  void Awake() {
    _deviceInterface = GetComponent<timelineComponentInterface>();
  }

  void Start() {
    masterControl.instance.beatUpdateEvent += beatUpdateEvent;
    masterControl.instance.beatResetEvent += beatResetEvent;
  }

  void OnDestroy() {
    masterControl.instance.beatUpdateEvent -= beatUpdateEvent;
    masterControl.instance.beatResetEvent -= beatResetEvent;
  }

  void beatResetEvent() {
    lastBeatTime = 0;
    Back();
  }

  public void setRecord(bool on) {
    List<int> keys = new List<int>(activeEvents.Keys);
    foreach (int n in keys) {
      activeEvents[n].setRecord(false);
      activeEvents.Remove(n);
    }
  }

  void updatePlayhead() {
    if (_deviceInterface._gridParams.isOnGrid(curGridPosition)) {
      playheadHandle.gameObject.SetActive(true);
      Vector3 pos = playheadHandle.transform.localPosition;
      pos.x = _deviceInterface._gridParams.UnittoX(curGridPosition);
      playheadHandle.transform.localPosition = pos;
    } else playheadHandle.gameObject.SetActive(false);
  }

  List<int> loopKeys = new List<int>();
  void loopActiveEvents() {
    loopKeys = new List<int>(activeEvents.Keys);
    foreach (int n in loopKeys) {
      activeEvents[n].setOut(_deviceInterface._gridParams.head_tail.y);
      activeEvents[n].setRecord(false);
      activeEvents.Remove(n);
    }
  }

  public void Back() {
    curGridPosition = _deviceInterface._gridParams.head_tail.x;
  }

  bool playheadScrubbing = false;

  void Update() { long _pp = PerfProbe.Start(); try { UpdateProbed(); } finally { PerfProbe.Add("timelinePlayer.Update", _pp); } } // (PerfProbe: temporary)
  void UpdateProbed() {
    playheadScrubbing = playheadHandle.curState == manipObject.manipState.grabbed;

    if (!playheadScrubbing) updatePlayhead();
    else {
      playheadHandle.xBounds.y = _deviceInterface._gridParams.UnittoX(_deviceInterface._gridParams.head_tail.x);
      playheadHandle.xBounds.x = _deviceInterface._gridParams.UnittoX(_deviceInterface._gridParams.head_tail.y);
      curGridPosition = _deviceInterface._gridParams.XtoUnit(playheadHandle.transform.localPosition.x);
      timelineEventUpdate();
    }

    lock (_recordLock) {

      if (toRecord.Keys.Count > 0) {
        foreach (KeyValuePair<int, float> entry in toRecord) {
          activeEvents[entry.Key] = _deviceInterface.SpawnTimelineEvent(entry.Key, new Vector2(entry.Value, entry.Value + .01f));
          activeEvents[entry.Key].setRecord(true);
          activeEvents[entry.Key].setOut(entry.Value + .01f);
          activeEvents[entry.Key].overlapCheck();
        }

        toRecord.Clear();
      }
    }
  }

  public void clearActiveEvents() {
    activeEvents.Clear();
  }

  private object _recordLock = new object();
  Dictionary<int, float> toRecord = new Dictionary<int, float>();

  public void RecordEvent(int n, bool on) {
    if (on) {
      if (activeEvents.ContainsKey(n)) {
        activeEvents[n].setRecord(false);
        activeEvents.Remove(n);
      }

      float b = curGridPosition;
      if (_deviceInterface.snapping) {
        b = _deviceInterface._gridParams.UnittoSnap(b, true);
      }
      lock (_recordLock) toRecord[n] = b;
    } else {
      if (activeEvents.ContainsKey(n)) {
        activeEvents[n].setRecord(false);
        activeEvents.Remove(n);
      } else {
        lock (_recordLock) {
          if (toRecord.ContainsKey(n)) toRecord.Remove(n);
        }

      }
    }
  }

  // Runs on the audio thread. Notes can be removed meanwhile on the main thread (another
  // player's change, NetTimelines, takes eventsLock; deleting or overlapping notes here
  // doesn't), and an index running past the end threw out of the whole audio callback -
  // a click. Now a note that went away is skipped.
  void timelineEventUpdate() {
    long _pp = PerfProbe.Start();
    lock (_deviceInterface.eventsLock) {
      try { timelineEventUpdateLocked(); }
      catch (System.ArgumentOutOfRangeException) { } // a note was removed under us: next callback
      catch (System.NullReferenceException) { }
    }
    PerfProbe.AddAudio(_pp);
  }

  // Overwrite recording (overdub off) erases notes by trimming their start up to the
  // playhead. The notes are checked every 64 samples now, so the playhead jumps a little
  // between checks: a note's last bit, or a short note, could fall between two checks and
  // stay as a sliver. Anything the playhead will pass before the next check now goes.
  float lastEvalPos = -1;

  void EraseNote(int i) {
    timelineEvent e = _deviceInterface._tlEvents[i];
    if (e.playing) _deviceInterface.tlEventTrigger(i, false);
    e.pleaseDestroy = true;
    _deviceInterface._tlEvents.RemoveAt(i);
  }

  void timelineEventUpdateLocked() {
    float prev = lastEvalPos;
    lastEvalPos = curGridPosition;
    bool overwriting = _deviceInterface.recording && !_deviceInterface.overdub;
    float jump = curGridPosition - prev;
    bool stepped = prev >= 0 && jump > 0 && jump < 1f; // moved forward a little since the last check (not a loop or a scrub)
    for (int i = _deviceInterface._tlEvents.Count - 1; i >= 0; i--) {
      if (overwriting && stepped && i < _deviceInterface._tlEvents.Count) {
        timelineEvent n = _deviceInterface._tlEvents[i];
        if ((object)n != null && !n.recording && !n.grabbed && !n.playing && !n.pleaseDestroy &&
            n.in_out.x >= prev - 1e-4f && n.in_out.y <= curGridPosition + jump * 1.5f) { EraseNote(i); continue; }
      }
      if (i < _deviceInterface._tlEvents.Count) {
        if (_deviceInterface._tlEvents[i] != null) {
          if (_deviceInterface._tlEvents[i].recording) {
            _deviceInterface._tlEvents[i].setOut(curGridPosition);
            _deviceInterface._tlEvents[i].overlapCheck();
          } else if (_deviceInterface._tlEvents[i].grabbed) {
            // nothing?
          } else if (!_deviceInterface._tlEvents[i].inRange(curGridPosition)) {
            if (_deviceInterface._tlEvents[i].playing) _deviceInterface.tlEventTrigger(i, false);
          } else {
            bool shouldplay = true;

            if (_deviceInterface.recording) {
              if (!_deviceInterface.overdub) // not overdubbing means overwrite
              {
                if (!_deviceInterface._tlEvents[i].playing) {
                  _deviceInterface._tlEvents[i].setIn(curGridPosition, false);
                  shouldplay = false;
                }
              }
            }

            if (i < _deviceInterface._tlEvents.Count) {
              if (_deviceInterface._tlEvents[i] != null) {
                if (!_deviceInterface._tlEvents[i].playing && shouldplay) {
                  _deviceInterface.tlEventTrigger(i, true);
                }
              }
            }
          }
        }
      }

    }
  }

  bool playing = false;
  bool playDesired = false;
  int playBeat = 0;

  public void setPlay(bool on, bool immediate) {
    playDesired = on;
    if (immediate) playing = playDesired;
    playBeat = Mathf.CeilToInt(curBeatTime * 8) % 8;
  }

  float curBeatTime = 0;
  public bool timeSynch = true;

  // ---- multiplayer (Net/NetBeatSync.cs): keep the playhead lined up with the host's ----
  volatile bool netShiftPending;
  float netShift;
  float LoopLength { get { return _deviceInterface._gridParams.head_tail.y - _deviceInterface._gridParams.head_tail.x; } }
  public bool NetSyncable { get { return playing && looping && !playheadScrubbing && LoopLength > 0 && _deviceInterface._gridParams.unitDuration > 0; } }
  public int NetConfig { get { return Mathf.RoundToInt(LoopLength * 16) * 1000 + Mathf.RoundToInt(_deviceInterface._gridParams.unitDuration * 1024); } }
  public double NetAnchor() {
    double a = (curGridPosition - _deviceInterface._gridParams.head_tail.x) - NetBeat.Measures / _deviceInterface._gridParams.unitDuration;
    double len = LoopLength;
    a %= len;
    return a < 0 ? a + len : a;
  }
  public double NetPeriod { get { return LoopLength; } }
  public double NetTolerance { get { return (1.0 / 64) / _deviceInterface._gridParams.unitDuration; } } // a 64th of a measure
  public void NetShift(double d) { netShift = (float)d; netShiftPending = true; }
  public void beatUpdateEvent(float t) {
    if (playDesired != playing) {
      if (playDesired) {
        if (playBeat == Mathf.FloorToInt(curBeatTime * 8) % 8) {
          playing = playDesired;
        }
      } else playing = playDesired;
    }

    curBeatTime = t;

    if (netShiftPending) { // moved on the audio thread, where the playhead runs
      netShiftPending = false;
      if (playing && !playheadScrubbing) {
        curGridPosition += netShift;
        Vector2 ht = _deviceInterface._gridParams.head_tail;
        float len = ht.y - ht.x;
        if (len > 0) {
          while (curGridPosition > ht.y) curGridPosition -= len;
          while (curGridPosition < ht.x) curGridPosition += len;
        }
      }
    }

    if (!playing || playheadScrubbing) {
      lastBeatTime = t;
      return;
    }

    if (lastBeatTime > curBeatTime) {
      curGridPosition = curGridPosition + (1 - lastBeatTime + curBeatTime) / _deviceInterface._gridParams.unitDuration;
    } else curGridPosition = curGridPosition + (curBeatTime - lastBeatTime) / _deviceInterface._gridParams.unitDuration;

    bool shouldUpdate = true;
    if (curGridPosition > _deviceInterface._gridParams.head_tail.y) {
      if (looping) {
        float dif = curGridPosition - _deviceInterface._gridParams.head_tail.y;
        curGridPosition = _deviceInterface._gridParams.head_tail.x + dif;
        loopActiveEvents();
        evalCountdown = 0; // check the notes right at the loop point
      } else {
        curGridPosition = _deviceInterface._gridParams.head_tail.x;
        _deviceInterface.setPlay(false);
        shouldUpdate = false;
      }
    }

    if (curGridPosition < _deviceInterface._gridParams.head_tail.x) curGridPosition = _deviceInterface._gridParams.head_tail.x;
    // masterControl calls this for every audio SAMPLE (48,000 times a second), and checking
    // every note each time cost ~40 us per sample with 85 notes - more than real time, so the
    // audio fell behind: the beat dragged and crackled more as notes were added (2026-11-24;
    // dragging the playhead, checked once a frame, didn't). The playhead still moves every
    // sample; the notes are checked every 64 samples (1.3 ms - far below what can be heard).
    if (shouldUpdate && --evalCountdown <= 0) {
      evalCountdown = evalEvery;
      timelineEventUpdate();
    }
    lastBeatTime = curBeatTime;
  }

  const int evalEvery = 64;
  int evalCountdown;
}
