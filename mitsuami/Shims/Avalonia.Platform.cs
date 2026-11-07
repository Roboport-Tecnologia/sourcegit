using System;
using System.IO;

namespace Avalonia.Platform
{
    public readonly record struct PixelFormat(string Name, int BitsPerPixel)
    {
        public int BytesPerPixel => (BitsPerPixel + 7) / 8;
        public override string ToString() => Name;
    }

    public static class PixelFormats
    {
        public static PixelFormat Rgba8888 { get; } = new("Rgba8888", 32);
        public static PixelFormat Bgra8888 { get; } = new("Bgra8888", 32);
        public static PixelFormat Bgr24 { get; } = new("Bgr24", 24);
        public static PixelFormat Bgr555 { get; } = new("Bgr555", 16);
        public static PixelFormat Bgr565 { get; } = new("Bgr565", 16);
        public static PixelFormat Gray8 { get; } = new("Gray8", 8);
        public static PixelFormat Gray16 { get; } = new("Gray16", 16);
        public static PixelFormat Gray32Float { get; } = new("Gray32Float", 32);
    }

    public enum AlphaFormat
    {
        Premul,
        Unpremul,
        Opaque,
    }

    public interface ILockedFramebuffer : IDisposable
    {
        IntPtr Address { get; }
        PixelSize Size { get; }
        int RowBytes { get; }
        Vector Dpi { get; }
        PixelFormat Format { get; }
    }

    public interface IPlatformHandle
    {
        IntPtr Handle { get; }
        string HandleDescriptor { get; }
    }

    public enum ExtendClientAreaChromeHints
    {
        NoChrome,
        Default,
        SystemChrome,
        PreferSystemChrome,
        OSXThickTitleBar,
    }

    /// <summary>
    /// Opens <c>avares://SourceGit/…</c> resources from the files the
    /// project embeds (<c>Resources/Images</c>), as Avalonia does. A missing
    /// one throws, as Avalonia's does.
    /// </summary>
    public static class AssetLoader
    {
        public static Stream Open(Uri uri, Uri baseUri = null)
        {
            var path = PathOf(uri);
            return SourceGit.EmbeddedFiles.Open(path) ?? throw new FileNotFoundException($"No embedded {path}", path);
        }

        public static bool Exists(Uri uri, Uri baseUri = null) => SourceGit.EmbeddedFiles.Exists(PathOf(uri));

        private static string PathOf(Uri uri)
        {
            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
            return Uri.UnescapeDataString(path);
        }
    }
}
