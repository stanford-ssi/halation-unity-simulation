using UnityEngine;
using UnityEngine.InputSystem;

public class GlacierCamera : MonoBehaviour
{
    public Transform target;
    public Terrain terrain;
    public bool overview;
    public float distance = 5.5f;
    private bool initialized;
    void LateUpdate()
    {
        if (target == null)
            return;
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            overview = !overview;
            initialized = false;
        }
        Vector3 look = target.position + Vector3.up * 0.25f;
        Quaternion yaw = Quaternion.Euler(0, target.eulerAngles.y, 0);
        Vector3 desired = overview ? target.position + new Vector3(20, 28, -25) : look + yaw * new Vector3(2.5f, 2.5f, -distance);
        if (terrain != null)
            desired.y = Mathf.Max(desired.y, terrain.SampleHeight(desired) + terrain.transform.position.y + 1.2f);
        // Keep the camera on the near side of terrain or obstacles.
        Vector3 ray = desired - look;
        if (Physics.Linecast(look, desired, out RaycastHit hit) && !hit.collider.transform.IsChildOf(target))
            desired = hit.point - ray.normalized * 0.3f;
        transform.position = initialized ? Vector3.Lerp(transform.position, desired, 1 - Mathf.Exp(-6 * Time.deltaTime)) : desired;
        transform.LookAt(look, Vector3.up);
        initialized = true;
    }
}
