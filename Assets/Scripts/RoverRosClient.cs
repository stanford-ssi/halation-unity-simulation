using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RoverMovement))]
public class RoverRosClient : MonoBehaviour {
    public string bridgeUrl = "ws://127.0.0.1:9095";
    [Min(0.1f)] public float reconnectDelay = 2f;
    [Min(1f)] public float connectTimeout = 5f;

    private ClientWebSocket socket;
    private CancellationTokenSource cancellationSource;
    private RoverMovement movement;
    private float lastInvalidWarning = float.NegativeInfinity;

    public bool IsConnected { get; private set; }
    public string Status { get; private set; } = "Not connected";
    public int ReceivedCommands { get; private set; }

    private void Awake() {
        movement = GetComponent<RoverMovement>();
    }

    private async void OnEnable() {
        // Keep receiving commands while the browser controller has focus.
        Application.runInBackground = true;
        CancellationTokenSource lifetime = new CancellationTokenSource();
        cancellationSource = lifetime;

        try {
            await RunConnection(lifetime.Token);
        } catch (OperationCanceledException) {
            // Stopping Play mode or disabling this component ends the connection.
        } catch (Exception exception) {
            if (!lifetime.IsCancellationRequested) Debug.LogException(exception, this);
        } finally {
            if (cancellationSource == lifetime) cancellationSource = null;
            lifetime.Dispose();
        }
    }

    private async Task RunConnection(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            using (ClientWebSocket connection = new ClientWebSocket()) {
                socket = connection;
                try {
                    Status = "Connecting to " + bridgeUrl;
                    using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        attempt.CancelAfter(TimeSpan.FromSeconds(connectTimeout));
                        await connection.ConnectAsync(new Uri(bridgeUrl), attempt.Token);
                    }

                    await Subscribe(connection, "/motor_vector", token);
                    await Subscribe(connection, "/motor_estop", token);
                    token.ThrowIfCancellationRequested();
                    IsConnected = true;
                    Status = "Connected - listening for motor commands";
                    Debug.Log("Connected to rosbridge! Subscribed to /motor_vector and /motor_estop.", this);
                    await ReceiveMessages(connection, token);
                } catch (Exception exception) when (!token.IsCancellationRequested) {
                    Status = "Disconnected - retrying";
                    Debug.LogWarning("ROS connection interrupted: " + exception.Message + ". Retrying.", this);
                } finally {
                    if (socket == connection) {
                        socket = null;
                        IsConnected = false;
                        movement.Stop();
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), token);
        }
    }

    private async Task Subscribe(ClientWebSocket connection, string topic, CancellationToken token) {
        JObject request = new JObject {
            ["op"] = "subscribe",
            ["topic"] = topic,
            ["type"] = "std_msgs/msg/String",
            ["queue_length"] = 1,
            // Accept live commands from ordinary volatile ROS publishers.
            // Never request retained commands when reconnecting.
            ["qos"] = new JObject {
                ["history"] = "keep_last", ["depth"] = 1,
                ["reliability"] = "reliable", ["durability"] = "volatile"
            }
        };
        byte[] bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
        await connection.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private async Task ReceiveMessages(ClientWebSocket connection, CancellationToken token) {
        byte[] buffer = new byte[4096];
        UTF8Encoding utf8 = new UTF8Encoding(false, true);
        while (!token.IsCancellationRequested) {
            using (MemoryStream message = new MemoryStream()) {
                WebSocketReceiveResult part;
                do {
                    part = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (part.MessageType == WebSocketMessageType.Close) {
                        throw new IOException("The bridge closed the connection");
                    }
                    if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 65536) {
                        throw new InvalidDataException("Expected a text command smaller than 64 KiB");
                    }
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);

                token.ThrowIfCancellationRequested();
                HandleMessage(utf8.GetString(message.ToArray()));
            }
        }
    }

    private void HandleMessage(string text) {
        try {
            JObject envelope = JObject.Parse(text);
            if ((string)envelope["op"] == "status" && (string)envelope["level"] == "error") {
                throw new InvalidDataException("rosbridge: " + (string)envelope["msg"]);
            }
            if ((string)envelope["op"] != "publish") return;

            string topic = (string)envelope["topic"];
            if (topic != "/motor_vector" && topic != "/motor_estop") return;
            JToken data = envelope["msg"]?["data"];
            if (data == null || data.Type != JTokenType.String) {
                RejectCommand();
                return;
            }

            if (topic == "/motor_estop") {
                movement.Stop();
                ReceivedCommands++;
            } else if (TryDecodeVector((string)data, out Vector2 input)) {
                // Await resumes on Unity's main thread. Update applies the movement.
                movement.SetRosCommand(input.x, input.y);
                ReceivedCommands++;
            } else {
                RejectCommand();
            }
        } catch (Exception exception) when (exception is JsonException || exception is ArgumentException ||
                                            exception is InvalidOperationException || exception is FormatException ||
                                            exception is OverflowException) {
            RejectCommand();
        }
    }

    public static bool TryDecodeVector(string data, out Vector2 input) {
        input = Vector2.zero;
        try {
            JObject payload = JObject.Parse(data);
            // Halation's web publisher wraps {x,y} inside a second "data" string.
            // Also accept direct {x,y} JSON for native ROS command-line tools.
            if (payload["data"] != null) {
                if (payload["data"].Type != JTokenType.String) return false;
                payload = JObject.Parse((string)payload["data"]);
            }

            JToken x = payload["x"];
            JToken y = payload["y"];
            if (!IsNumber(x) || !IsNumber(y)) return false;
            float turn = x.Value<float>();
            float forward = y.Value<float>();
            if (float.IsNaN(turn) || float.IsInfinity(turn) ||
                float.IsNaN(forward) || float.IsInfinity(forward)) return false;

            input = new Vector2(Mathf.Clamp(turn, -1f, 1f), Mathf.Clamp(forward, -1f, 1f));
            return true;
        } catch (Exception exception) when (exception is JsonException || exception is ArgumentException ||
                                            exception is FormatException || exception is OverflowException) {
            return false;
        }
    }

    private static bool IsNumber(JToken value) {
        return value != null && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer);
    }

    private void RejectCommand() {
        movement.Stop();
        if (Time.unscaledTime - lastInvalidWarning < 2f) return;
        lastInvalidWarning = Time.unscaledTime;
        Debug.LogWarning("Ignored an invalid ROS motor command; rover stopped.", this);
    }

    private void OnDisable() {
        cancellationSource?.Cancel();
        socket?.Abort();
        IsConnected = false;
        Status = "Disabled";
        movement.Stop();
    }
}
