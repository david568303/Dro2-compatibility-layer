using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager.Api;

/// <summary>
/// Reading and rewriting the pixels of a game-owned Texture2D. Pixels are exchanged as tightly packed RGBA bytes,
/// row 0 first, so the processing in between can be plain managed code on any thread.
/// Only textures with Read/Write enabled are supported; anything else is reported through <c>problem</c>.
/// </summary>
public static class TextureTools
{
    /// <summary>
    /// Makes a readable copy of any texture, including ones with Read/Write disabled (the usual case for textures the
    /// game loads itself), by drawing it into a temporary render target on the GPU and reading that back. The copy is
    /// a new Texture2D owned by the caller (destroy it when done). Call on the main thread.
    /// </summary>
    public static bool TryCopyToReadable(Texture2D source, out Texture2D? copy, out string? problem)
    {
        copy = null;
        problem = null;
        RenderTexture? previous = RenderTexture.active;
        RenderTexture? target = null;
        try
        {
            int width = source.width, height = source.height;
            target = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            var result = new Texture2D(width, height);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            result.Apply();
            copy = result;
            return true;
        }
        catch (Exception exception)
        {
            problem = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        finally
        {
            RenderTexture.active = previous!;
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>Copies the texture's pixels out. Call on the main thread.</summary>
    public static bool TryRead(Texture2D texture, out byte[] rgba, out int width, out int height, out string? problem)
    {
        rgba = Array.Empty<byte>();
        width = height = 0;
        problem = null;
        try
        {
            if (!texture.isReadable)
            {
                problem = "the texture is not readable (Read/Write is disabled on it)";
                return false;
            }
            width = texture.width;
            height = texture.height;
            Il2CppStructArray<Color32> pixels = texture.GetPixels32();
            rgba = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                rgba[i * 4] = pixel.r;
                rgba[i * 4 + 1] = pixel.g;
                rgba[i * 4 + 2] = pixel.b;
                rgba[i * 4 + 3] = pixel.a;
            }
            return true;
        }
        catch (Exception exception)
        {
            problem = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Overwrites the texture's pixels (same size as when read) and uploads them to the GPU, so everything already
    /// showing this texture updates. Call on the main thread.
    /// </summary>
    public static bool TryWriteInPlace(Texture2D texture, byte[] rgba, int width, int height, out string? problem)
    {
        problem = null;
        try
        {
            if (texture.width != width || texture.height != height)
            {
                problem = $"the texture is now {texture.width}x{texture.height}, not {width}x{height}";
                return false;
            }
            var pixels = new Il2CppStructArray<Color32>(width * height);
            for (int i = 0; i < width * height; i++)
                pixels[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            texture.SetPixels32(pixels);
            texture.Apply();
            return true;
        }
        catch (Exception exception)
        {
            problem = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
