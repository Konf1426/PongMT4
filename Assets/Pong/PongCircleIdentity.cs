using UnityEngine;

public struct PongCircleIdentity
{
    public string Name;
    public int ColorIndex;
    public Color Color;

    public bool HasColor {
      get { return ColorIndex >= 0 && ColorIndex < PongCircleIdentityStore.Palette.Length; }
    }

    public string ColorHex {
      get { return HasColor ? ColorUtility.ToHtmlStringRGB(Color) : ""; }
    }
}

public static class PongCircleIdentityStore
{
    const string NameKey = "pong_name";
    const string ColorIndexKey = "pong_color_index";

    public static readonly Color[] Palette = {
        new Color(0.345f, 0.902f, 0.784f),
        new Color(0.961f, 0.353f, 0.408f),
        new Color(0.984f, 0.686f, 0.243f),
        new Color(0.969f, 0.878f, 0.318f),
        new Color(0.486f, 0.812f, 0.380f),
        new Color(0.388f, 0.616f, 0.961f),
        new Color(0.706f, 0.514f, 0.961f),
        new Color(0.961f, 0.510f, 0.776f),
        new Color(0.380f, 0.835f, 0.961f),
        new Color(0.741f, 0.910f, 0.376f),
    };

    public static PongCircleIdentity Load()
    {
      PongCircleIdentity identity = new PongCircleIdentity {
        Name = PlayerPrefs.GetString(NameKey, ""),
        ColorIndex = PlayerPrefs.GetInt(ColorIndexKey, -1)
      };

      if (identity.ColorIndex >= 0 && identity.ColorIndex < Palette.Length) {
        identity.Color = Palette[identity.ColorIndex];
      } else {
        identity.ColorIndex = -1;
      }

      return identity;
    }

    public static void Save(PongCircleIdentity identity)
    {
      PlayerPrefs.SetString(NameKey, identity.Name ?? "");
      PlayerPrefs.SetInt(ColorIndexKey, identity.ColorIndex);
      PlayerPrefs.Save();
    }

    public static PongCircleIdentity WithName(PongCircleIdentity identity, string name)
    {
      identity.Name = name ?? "";
      return identity;
    }

    public static PongCircleIdentity WithColor(PongCircleIdentity identity, int colorIndex)
    {
      identity.ColorIndex = colorIndex;
      identity.Color = Palette[colorIndex];
      return identity;
    }
}
