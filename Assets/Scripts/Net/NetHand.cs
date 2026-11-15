// Another player's hand or controller (2026-11, multiplayer).
//
// Shows what that player's own hand shows: the glowing tracked hand when they use hand
// tracking, or SoundStage's controller model when they hold controllers. No fingertip
// spheres. Both visuals are built here, on this headset, from the same sources the
// local player uses:
//  * hand: Resources/Hands/HandLeft|HandRight with OVRHand removed; this component feeds
//    OVRSkeleton / OVRMesh / OVRMeshRenderer the bone rotations received over the
//    network instead of local tracking.
//  * controller: a copy of our own manipulator's controller model (controllerRep), with
//    every script, collider and light stripped, so it's only meshes.
// NetPlayer moves this object to the remote hand / controller pose.

using UnityEngine;

public class NetHand : MonoBehaviour {

  public bool isLeft;
  bool handVisible;

  GameObject handObj, controllerObj;
  OVRPlugin.Quatf[] bones;
  bool bonesValid;

  public static NetHand Create(Transform parent, bool left) {
    GameObject g = new GameObject(left ? "NetHandLeft" : "NetHandRight");
    g.transform.SetParent(parent, false);
    NetHand h = g.AddComponent<NetHand>();
    h.isLeft = left;
    return h;
  }

  // ---- what the remote player is using ----

  public void Show(bool handTracking, bool visible) {
    if (handTracking && handObj == null) BuildHand();
    if (!handTracking && controllerObj == null) BuildController();
    if (handObj != null) handObj.SetActive(visible && handTracking);
    if (controllerObj != null) controllerObj.SetActive(visible && !handTracking);
    handVisible = visible && handTracking;
  }

  public void SetBones(uint[] packed) {
    if (packed == null || packed.Length == 0) { bonesValid = false; return; }
    if (bones == null || bones.Length != packed.Length) bones = new OVRPlugin.Quatf[packed.Length];
    for (int i = 0; i < packed.Length; i++) {
      Quaternion q = Mirror.Compression.DecompressQuaternion(packed[i]);
      bones[i] = new OVRPlugin.Quatf { x = q.x, y = q.y, z = q.z, w = q.w };
    }
    bonesValid = true;
  }

  // ---- building the visuals (under an inactive holder so no Awake runs early) ----

  void BuildHand() {
    GameObject prefab = Resources.Load<GameObject>(isLeft ? "Hands/HandLeft" : "Hands/HandRight");
    if (prefab == null) return;
    GameObject holder = new GameObject("hold");
    holder.SetActive(false);
    handObj = Instantiate(prefab, holder.transform, false);
    OVRHand ovrHand = handObj.GetComponent<OVRHand>();
    if (ovrHand != null) DestroyImmediate(ovrHand); // our data instead of local tracking
    NetHandData data = handObj.AddComponent<NetHandData>();
    data.owner = this;
    HandFingertips.ApplyGlowMaterial(handObj.GetComponent<SkinnedMeshRenderer>());
    handObj.transform.SetParent(transform, false);
    handObj.transform.localPosition = Vector3.zero;
    handObj.transform.localRotation = Quaternion.identity;
    Destroy(holder);
  }

  void BuildController() {
    GameObject src = null;
    foreach (manipulator m in FindObjectsOfType<manipulator>(true)) {
      if (m.controllerRep != null && m.ControllerIndex == (isLeft ? 0 : 1)) { src = m.controllerRep; break; }
    }
    if (src == null) return;
    GameObject holder = new GameObject("hold");
    holder.SetActive(false);
    controllerObj = Instantiate(src, holder.transform, false);
    StripToMeshes(controllerObj);
    controllerObj.transform.SetParent(transform, false);
    controllerObj.transform.localPosition = Vector3.zero;
    controllerObj.transform.localRotation = Quaternion.identity;
    controllerObj.transform.localScale = src.transform.lossyScale;
    Destroy(holder);
  }

  static void StripToMeshes(GameObject g) {
    // scripts first (some require colliders etc.), then physics, lights and sound
    foreach (MonoBehaviour b in g.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
    foreach (Collider c in g.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
    foreach (Rigidbody r in g.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
    foreach (Light l in g.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
    foreach (AudioSource a in g.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
    foreach (TextMesh t in g.GetComponentsInChildren<TextMesh>(true)) t.gameObject.SetActive(false); // tooltips
  }

  // ---- data for the OVR hand components ----

  public OVRSkeleton.SkeletonType GetSkeletonType() {
    return isLeft ? OVRSkeleton.SkeletonType.HandLeft : OVRSkeleton.SkeletonType.HandRight;
  }

  public OVRSkeleton.SkeletonPoseData GetSkeletonPoseData() {
    return new OVRSkeleton.SkeletonPoseData {
      RootPose = OVRPlugin.Posef.identity,
      RootScale = 1f,
      BoneRotations = bones,
      IsDataValid = bonesValid && bones != null,
      IsDataHighConfidence = bonesValid,
      SkeletonChangedCount = 1
    };
  }

  public OVRMesh.MeshType GetMeshType() {
    return isLeft ? OVRMesh.MeshType.HandLeft : OVRMesh.MeshType.HandRight;
  }

  public OVRMeshRenderer.MeshRendererData GetMeshRendererData() {
    return new OVRMeshRenderer.MeshRendererData {
      IsDataValid = handVisible && bonesValid,
      IsDataHighConfidence = handVisible && bonesValid,
      ShouldUseSystemGestureMaterial = false
    };
  }
}

// Sits on the hand object itself, where OVRSkeleton / OVRMesh / OVRMeshRenderer look for
// their data provider, and forwards to the NetHand above.
public class NetHandData : MonoBehaviour,
    OVRSkeleton.IOVRSkeletonDataProvider, OVRMesh.IOVRMeshDataProvider, OVRMeshRenderer.IOVRMeshRendererDataProvider {
  public NetHand owner;
  public OVRSkeleton.SkeletonType GetSkeletonType() { return owner.GetSkeletonType(); }
  public OVRSkeleton.SkeletonPoseData GetSkeletonPoseData() { return owner.GetSkeletonPoseData(); }
  public OVRMesh.MeshType GetMeshType() { return owner.GetMeshType(); }
  public OVRMeshRenderer.MeshRendererData GetMeshRendererData() { return owner.GetMeshRendererData(); }
}
