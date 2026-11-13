using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArtificialVRMovement : MonoBehaviour
{
    public float maxSpeed = 1.0f;
    public string horizontalAxisName = "Horizontal";
    public string verticalAxisName = "Vertical";
    public bool threeDMovement = false;

    [Header("Required Scene/Child References")]
    public Transform offhandMovementHand;

    [Header("Debug settings")]
    public bool logErrorsAfterInitialFailure = false;

    private bool hasThrownError;

    // Quest (2026-11): read the left thumbstick through OVRInput with a dead zone. The legacy
    // Input Manager axes ("Horizontal"/"Vertical") could keep reporting a value after the
    // stick was released, so the player drifted away for a while. Also stays still while the
    // left hand holds/points at something (devices use the stick) and during calibration.
    public float deadZone = 0.2f;
    manipulator leftHand;

    // Update is called once per frame
    void Update()
    {
        if (!offhandMovementHand)
        {
            LogError();
            return;
        }

        Vector3 verticalMovementVector = offhandMovementHand.forward;

        if (!threeDMovement)
        {
            // Project offhandMovementHand forward axis onto floor plane so that the player doesn't fly
            // Also, normalize (so that lifting controller up and down does not affect magnitude)
            verticalMovementVector = Vector3.ProjectOnPlane(verticalMovementVector, Vector3.up).normalized;
        }

        Vector3 horizontalMovementVector = Vector3.Cross(verticalMovementVector,-Vector3.up);

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        if (stick.magnitude < deadZone) return;
        if (platformSetup.calibrating) return;
        if (leftHand == null)
        {
            foreach (manipulator m in FindObjectsOfType<manipulator>()) if (m.IsLeftHand) leftHand = m;
        }
        if (leftHand != null && leftHand.IsBusy) return;
        // rescale so motion starts at zero just outside the dead zone
        stick = stick.normalized * Mathf.InverseLerp(deadZone, 1f, stick.magnitude);

        transform.position += verticalMovementVector * stick.y * maxSpeed * Time.deltaTime;
        transform.position += horizontalMovementVector * stick.x * maxSpeed * Time.deltaTime;
    }

    void LogError()
    {
        if(logErrorsAfterInitialFailure || !hasThrownError)
        {
            Debug.LogError("offhandMovementHand reference not set!", gameObject);
            hasThrownError = true;
        }
        return;
    }
}
