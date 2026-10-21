using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RoverRosClient))]
public class RoverRosClientEditor : Editor
{
    [MenuItem("Halation/Inspect rover")]
    public static void InspectRover()
    {
        RoverRosClient client = UnityEngine.Object.FindAnyObjectByType<RoverRosClient>();
        if (client != null)
            Selection.activeGameObject = client.gameObject;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        RoverRosClient client = (RoverRosClient)target;
        if (!Application.isPlaying)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(client.Status, client.IsConnected ? MessageType.Info : MessageType.Warning);
        EditorGUILayout.LabelField("Commands received", client.ReceivedCommands.ToString());
        RoverMovement movement = client.GetComponent<RoverMovement>();
        EditorGUILayout.LabelField("Steering / forward", movement.RosInput.ToString("F2"));
        EditorGUILayout.LabelField("Command fresh", movement.HasFreshCommand ? "Yes" : "No - stopped");
        Repaint();
    }
}
