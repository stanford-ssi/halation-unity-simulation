using UnityEngine;
using UnityEngine.InputSystem;

public class GlacierSession : MonoBehaviour
{
    public RoverMovement movement;
    public RoverPhysicsDrive drive;
    public RoverRosClient ros;
    public Terrain terrain;
    public Vector3 spawn;
    public Quaternion heading = Quaternion.identity;
    public float patchSize = 204.8f;
    void Start()
    {
        SyncControlMode();
    }
    void Update()
    {
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.rKey.wasPressedThisFrame)
                ResetRover();
            if (k.tabKey.wasPressedThisFrame)
                ToggleControl();
        }
        SyncControlMode();
        Vector3 p = drive.transform.position - terrain.transform.position;
        if (p.x < 1 || p.z < 1 || p.x > patchSize - 1 || p.z > patchSize - 1 || p.y < -10)
        {
            ResetRover();
        }
    }
    public void ToggleControl()
    {
        movement.Stop();
        movement.controlMode = movement.controlMode == RoverMovement.ControlMode.Keyboard ? RoverMovement.ControlMode.Ros : RoverMovement.ControlMode.Keyboard;
        SyncControlMode();
    }

    void SyncControlMode()
    {
        bool useRos = movement.controlMode == RoverMovement.ControlMode.Ros;
        if (ros.enabled != useRos)
        {
            movement.Stop();
            ros.enabled = useRos;
        }
    }
    public void ResetRover()
    {
        movement.Stop();
        drive.Brake();
        drive.Body.position = spawn;
        drive.Body.rotation = heading;
        drive.Body.linearVelocity = Vector3.zero;
        drive.Body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
}
