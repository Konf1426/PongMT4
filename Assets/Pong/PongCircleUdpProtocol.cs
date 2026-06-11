using System;
using UnityEngine;

[Serializable]
public class PongCircleUdpPayload
{
    public string type;
    public string deviceId;
    public string deviceName;
    public float direction;
}

[Serializable]
public class PongCircleUdpEnvelope
{
    public int seq;
    public string deviceId;
    public string deviceName;
    public string displayName;
    public string color;
    public PongCircleUdpPayload payload;
}

public static class PongCircleUdpProtocol
{
    public static string BuildMessage(
        int sequence,
        string deviceId,
        string deviceName,
        string displayName,
        string color,
        PongCircleUdpPayload payload)
    {
      PongCircleUdpEnvelope envelope = new PongCircleUdpEnvelope {
        seq = sequence,
        deviceId = deviceId ?? "",
        deviceName = deviceName ?? "",
        displayName = displayName ?? "",
        color = (color ?? "").Replace("#", ""),
        payload = payload
      };

      return JsonUtility.ToJson(envelope);
    }

    public static PongCircleUdpPayload Hello(string deviceId, string deviceName)
    {
      return new PongCircleUdpPayload {
        type = "hello",
        deviceId = deviceId ?? "",
        deviceName = deviceName ?? ""
      };
    }

    public static PongCircleUdpPayload Simple(string type)
    {
      return new PongCircleUdpPayload {
        type = type
      };
    }

    public static PongCircleUdpPayload Input(float direction)
    {
      return new PongCircleUdpPayload {
        type = "input",
        direction = Mathf.Clamp(direction, -1f, 1f)
      };
    }
}
