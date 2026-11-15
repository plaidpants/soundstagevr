// Makes the multiplayer player prefab (2026-11, milestone 0):
// SoundStage > Multiplayer > Create NetPlayer prefab
// -> Assets/Resources/Net/NetPlayer.prefab (+ NetAvatar.mat). Run it again to rebuild.
// The head is SoundStage's own headset model (the "hmd" mesh and material from
// Prefabs/player/HMDmain, the one the Camera device's broadcast shows), tinted with
// the player's colour. LeftHand / RightHand are empty: NetHand builds the glowing hand
// or the controller model there at runtime, matching what that player uses. No
// colliders, so the avatar never gets in the way of grabbing or poking.

using Mirror;
using UnityEditor;
using UnityEngine;

public static class NetworkPrefabTools {
  const string Folder = "Assets/Resources/Net";

  [MenuItem("SoundStage/Multiplayer/Create NetPlayer prefab")]
  static void Create() {
    if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
    if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Net");

    string matPath = Folder + "/NetAvatar.mat";
    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if (mat == null) {
      mat = new Material(Shader.Find("Unlit/Color")) { color = Color.white, enableInstancing = true };
      AssetDatabase.CreateAsset(mat, matPath);
    }

    GameObject root = new GameObject("NetPlayer");
    root.AddComponent<NetworkIdentity>();
    NetPlayer np = root.AddComponent<NetPlayer>();

    Transform head = new GameObject("Head").transform;
    head.SetParent(root.transform, false);
    Renderer[] headParts = HeadsetModel(head, mat);

    Transform left = new GameObject("LeftHand").transform;
    left.SetParent(root.transform, false);
    Transform right = new GameObject("RightHand").transform;
    right.SetParent(root.transform, false);

    np.head = head; np.leftHand = left; np.rightHand = right;
    np.parts = headParts;

    // head only; hands are sent by NetPlayer itself (with finger poses)
    NetworkTransformUnreliable nt = root.AddComponent<NetworkTransformUnreliable>();
    nt.target = head;
    nt.syncDirection = SyncDirection.ClientToServer;
    nt.syncPosition = true;
    nt.syncRotation = true;
    nt.syncScale = false;

    string path = Folder + "/NetPlayer.prefab";
    PrefabUtility.SaveAsPrefabAsset(root, path);
    Object.DestroyImmediate(root);
    AssetDatabase.SaveAssets();
    Debug.Log("[NetworkPrefabTools] wrote " + path);
  }

  // Copies the renderers under HMDmain's "hmd" object (no light, no scripts) onto the
  // head, on the Default layer (HMDmain keeps it on "norender" so you don't see your
  // own headset). Falls back to a sphere if the prefab can't be found.
  static Renderer[] HeadsetModel(Transform head, Material fallback) {
    GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player/HMDmain.prefab");
    var list = new System.Collections.Generic.List<Renderer>();
    if (src != null) {
      foreach (MeshRenderer mr in src.GetComponentsInChildren<MeshRenderer>(true)) {
        MeshFilter mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) continue;
        GameObject g = new GameObject(mr.gameObject.name);
        g.transform.SetParent(head, false);
        // same pose relative to HMDmain's root as in the original
        g.transform.localPosition = src.transform.InverseTransformPoint(mr.transform.position);
        g.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * mr.transform.rotation;
        g.transform.localScale = mr.transform.lossyScale;
        g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        MeshRenderer nr = g.AddComponent<MeshRenderer>();
        nr.sharedMaterials = mr.sharedMaterials;
        nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        list.Add(nr);
      }
    }
    if (list.Count == 0) {
      Debug.LogWarning("[NetworkPrefabTools] HMDmain headset model not found; using a sphere");
      list.Add(Part(head, PrimitiveType.Sphere, Vector3.zero, new Vector3(.18f, .2f, .2f), fallback));
    }
    return list.ToArray();
  }

  static Renderer Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat) {
    GameObject g = GameObject.CreatePrimitive(type);
    Object.DestroyImmediate(g.GetComponent<Collider>());
    g.transform.SetParent(parent, false);
    g.transform.localPosition = pos;
    g.transform.localScale = scale;
    Renderer rend = g.GetComponent<Renderer>();
    rend.sharedMaterial = mat;
    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    return rend;
  }
}
