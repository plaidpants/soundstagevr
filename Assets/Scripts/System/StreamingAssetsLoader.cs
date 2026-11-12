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

// Synchronous read of a file from StreamingAssets (added 2026-11).
// On Android StreamingAssets lives inside the APK, so it has to go through
// UnityWebRequest. Replaces the obsolete WWW + busy-wait the unpack code used.

using UnityEngine;
using UnityEngine.Networking;

public static class StreamingAssetsLoader {
  public static byte[] ReadAllBytes(string path) {
    using (UnityWebRequest req = UnityWebRequest.Get(path)) {
      UnityWebRequestAsyncOperation op = req.SendWebRequest();
      while (!op.isDone) System.Threading.Thread.Sleep(1);
      if (req.result != UnityWebRequest.Result.Success) {
        Debug.LogError("[StreamingAssetsLoader] failed to read " + path + ": " + req.error);
        return null;
      }
      return req.downloadHandler.data;
    }
  }
}
