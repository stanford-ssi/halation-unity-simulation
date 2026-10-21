using UnityEngine;

// Four wheel skid steering; commands act through tire forces, never pose changes.
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public class RoverPhysicsDrive : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        public WheelCollider collider;
        public Transform visual;
        public bool left;
    }
    public enum Grip
    {
        FirmGround, Ice
    }
    public Wheel[] wheels = new Wheel[0];
    [Tooltip("Target wheel speed in metres per second; terrain and traction affect actual speed.")]
    [Min(0.1f)] public float maximumSpeed = 1.5f;
    [Min(1f)] public float motorTorque = 45f;
    [Min(1f)] public float brakeTorque = 120f;
    [Min(0.1f)] public float speedGain = 35f;
    public Grip grip = Grip.FirmGround;
    public Vector3 centerOfMass = new Vector3(0, -0.45f, 0);
    private Grip previousGrip;
    private Vector2 command;
    private Rigidbody body;
    public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

    void Awake()
    {
        previousGrip = grip;
        Body.centerOfMass = centerOfMass;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.solverIterations = 12;
        Body.solverVelocityIterations = 6;
        foreach (var wheel in wheels)
            wheel.collider.ConfigureVehicleSubsteps(2, 8, 12);
    }
    public void SetInput(Vector2 input)
    {
        if (!float.IsFinite(input.x) || !float.IsFinite(input.y))
        {
            Brake();
            return;
        }
        command = new Vector2(Mathf.Clamp(input.x, -1, 1), Mathf.Clamp(input.y, -1, 1));
    }
    public void Brake()
    {
        command = Vector2.zero;
        foreach (var wheel in wheels)
            if (wheel.collider != null)
            {
                wheel.collider.motorTorque = 0;
                wheel.collider.brakeTorque = brakeTorque;
            }
    }
    void FixedUpdate()
    {
        if (grip != previousGrip)
        {
            // Material changes must take effect even when the parked rigid body is asleep.
            Body.WakeUp();
            previousGrip = grip;
        }
        // Differential wheel speeds allow turning in place as well as on an arc.
        float left = command.y + command.x;
        float right = command.y - command.x;
        float divisor = Mathf.Max(1, Mathf.Abs(left), Mathf.Abs(right));
        foreach (var wheel in wheels)
        {
            var c = wheel.collider;
            float desired = (wheel.left ? left : right) / divisor * maximumSpeed;
            float measured = c.rpm * (2 * Mathf.PI * c.radius / 60f);
            bool stop = Mathf.Abs(desired) < 0.025f;
            c.brakeTorque = stop ? brakeTorque : 0;
            c.motorTorque = stop ? 0 : Mathf.Clamp((desired - measured) * speedGain, -motorTorque, motorTorque);
            SetFriction(c, grip == Grip.Ice ? 0.18f : 1.0f);
        }
    }
    static void SetFriction(WheelCollider c, float grip)
    {
        WheelFrictionCurve forward = c.forwardFriction;
        forward.extremumSlip = 0.25f;
        forward.extremumValue = 1;
        forward.asymptoteSlip = 0.8f;
        forward.asymptoteValue = 0.7f;
        forward.stiffness = grip;
        c.forwardFriction = forward;
        WheelFrictionCurve sideways = forward;
        sideways.extremumSlip = 0.15f;
        sideways.asymptoteSlip = 0.5f;
        // Lateral scrub is needed for a four-wheel skid-steer vehicle to turn.
        sideways.stiffness = grip * 0.65f;
        c.sidewaysFriction = sideways;
    }
    void LateUpdate()
    {
        foreach (var wheel in wheels)
            if (wheel.visual != null)
            {
                wheel.collider.GetWorldPose(out var pos, out var rot);
                wheel.visual.SetPositionAndRotation(pos, rot);
            }
    }
    void OnDisable()
    {
        Brake();
    }
}
