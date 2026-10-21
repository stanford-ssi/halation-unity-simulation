using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(RoverPhysicsDrive))]
public class RoverMovement : MonoBehaviour
{
    public enum ControlMode
    {
        Keyboard, Ros
    }

    public ControlMode controlMode = ControlMode.Keyboard;
    [Min(0.1f)] public float commandTimeout = 1f;

    private RoverPhysicsDrive physicsDrive;
    private Vector2 rosInput;
    private float lastCommandTime = float.NegativeInfinity;
    private ControlMode previousMode;

    public Vector2 RosInput => rosInput;
    public bool HasFreshCommand => Time.unscaledTime - lastCommandTime < commandTimeout;

    private void OnEnable()
    {
        physicsDrive = GetComponent<RoverPhysicsDrive>();
        previousMode = controlMode;
        Stop();
    }

    public void SetRosCommand(float turn, float forward)
    {
        if (float.IsNaN(turn) || float.IsInfinity(turn) ||
            float.IsNaN(forward) || float.IsInfinity(forward))
        {
            Stop();
            return;
        }

        rosInput = new Vector2(Mathf.Clamp(turn, -1f, 1f), Mathf.Clamp(forward, -1f, 1f));
        lastCommandTime = Time.unscaledTime;
    }

    public void Stop()
    {
        rosInput = Vector2.zero;
        lastCommandTime = float.NegativeInfinity;
        if (physicsDrive != null)
            physicsDrive.Brake();
    }

    private void Update()
    {
        if (controlMode != previousMode)
        {
            Stop();
            previousMode = controlMode;
        }
        if (controlMode == ControlMode.Ros && !HasFreshCommand)
            Stop();

        Vector2 input = controlMode == ControlMode.Keyboard ? ReadKeyboard() : rosInput;
        physicsDrive.SetInput(input);
    }

    private Vector2 ReadKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || keyboard.spaceKey.isPressed)
            return Vector2.zero;

        float forward = 0f;
        float turn = 0f;

        if (keyboard.wKey.isPressed)
            forward += 1f;
        if (keyboard.sKey.isPressed)
            forward -= 1f;
        if (keyboard.aKey.isPressed)
            turn -= 1f;
        if (keyboard.dKey.isPressed)
            turn += 1f;

        return new Vector2(turn, forward);
    }

    private void OnDisable()
    {
        Stop();
    }
}
