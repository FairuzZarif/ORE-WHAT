using UnityEngine;

/// <summary>
/// Keeps the map signs' text material (Resources/SignText, shader "Ore What/Sign Text") pointing at the built-in
/// font's texture. The font is dynamic: its glyph texture is filled (and can be rebuilt) at runtime, so the texture
/// is assigned when the game starts and again whenever Unity rebuilds it. No scene objects needed.
/// </summary>
public static class SignTextFont
{
    public const string MaterialName = "SignText";
    public const string FontName = "LegacyRuntime.ttf";

    private static Material material;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Font.textureRebuilt -= OnTextureRebuilt; // (domain reload can be off: never subscribe twice)
        Font.textureRebuilt += OnTextureRebuilt;
        Apply();
    }

    private static void OnTextureRebuilt(Font font)
    {
        if (font != null && font.name == System.IO.Path.GetFileNameWithoutExtension(FontName)) Apply();
    }

    /// <summary>Assigns the font's current texture to the sign text material.</summary>
    public static void Apply()
    {
        if (material == null) material = Resources.Load<Material>(MaterialName);
        Font font = Resources.GetBuiltinResource<Font>(FontName);
        if (material != null && font != null && font.material != null && material.mainTexture != font.material.mainTexture)
            material.mainTexture = font.material.mainTexture;
    }
}
