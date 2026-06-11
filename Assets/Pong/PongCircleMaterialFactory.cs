using UnityEngine;

public static class PongCircleMaterialFactory
{
    public static Material Create(Color color)
    {
      return Create(color, 0f);
    }

    public static Material Create(Color color, float emission)
    {
      Material material = new Material(ResolveShader("Universal Render Pipeline/Lit", "Standard"));
      material.color = color;
      if (material.HasProperty("_BaseColor")) {
        material.SetColor("_BaseColor", color);
      }

      if (emission > 0f) {
        material.EnableKeyword("_EMISSION");
        Color hdr = new Color(color.r, color.g, color.b, 1f).linear * emission;
        if (material.HasProperty("_EmissionColor")) {
          material.SetColor("_EmissionColor", hdr);
        }
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
      }

      if (color.a < 1f) {
        if (material.HasProperty("_Surface")) { material.SetFloat("_Surface", 1); }
        if (material.HasProperty("_Mode")) { material.SetFloat("_Mode", 3); }
        if (material.HasProperty("_SrcBlend")) { material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); }
        if (material.HasProperty("_DstBlend")) { material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
        if (material.HasProperty("_ZWrite")) { material.SetInt("_ZWrite", 0); }
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = 3000;
      }

      return material;
    }

    public static Material CreateUnlitHdr(Color color, float intensity)
    {
      Material material = new Material(ResolveShader("Universal Render Pipeline/Unlit", "Unlit/Color"));
      Color hdr = color.linear * intensity;
      if (material.HasProperty("_BaseColor")) {
        material.SetColor("_BaseColor", hdr);
      }
      material.color = hdr;
      return material;
    }

    static Shader ResolveShader(params string[] names)
    {
      foreach (string name in names) {
        Shader shader = Shader.Find(name);
        if (shader != null) {
          return shader;
        }
      }

      return Shader.Find("Hidden/InternalErrorShader");
    }
}
