using Deventry.Core.Protocol;
using SkiaSharp;

namespace Deventry.App.Services;

/// <summary>
/// The Media window's thumbnails (DESIGN.md §18, "Media"): an image scaled down to a small PNG off the UI thread, so a
/// gallery of large screenshots costs little to show again as the window follows another tab. An animated image's is its
/// first frame.
/// </summary>
public static class MediaThumbnails
{
    /// <summary>A tile's image is 72 pixels high and up to 96 wide; its thumbnail is twice that, for high-DPI screens.</summary>
    private const int MaxHeight = 144;

    private const int MaxWidth = 192;

    /// <summary>A thumbnail of the image <paramref name="data"/> holds; null when it can't be decoded.</summary>
    public static byte[]? FromBytes(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        return Make(codec);
    }

    /// <summary>
    /// A thumbnail of the image file at <paramref name="path"/>: one the image window shows (<see cref="ViewerImages"/>), no
    /// larger than it shows. Null when it isn't one, isn't there or can't be decoded.
    /// </summary>
    public static byte[]? FromFile(string path) => ViewerImages.ReadFile(path) is { } image ? FromBytes(image.Data) : null;

    private static byte[]? Make(SKCodec? codec)
    {
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        var scale = Math.Min(1f, Math.Min((float)MaxWidth / width, (float)MaxHeight / height));
        // A JPEG or WebP decodes smaller itself; the rest decode whole and are scaled after.
        var decoded = codec.GetScaledDimensions(scale);
        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }
        var target = new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scaled = target.Width == decoded.Width && target.Height == decoded.Height
            ? null
            : bitmap.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(scaled ?? bitmap);
        using var png = image?.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }
}
