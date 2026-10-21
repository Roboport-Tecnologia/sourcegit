using System;
using System.IO;
using System.Runtime.InteropServices;

using Avalonia.Platform;
using StbImageSharp;

namespace Avalonia.Media.Imaging
{
    /// <summary>
    /// Decoded pixels and their format. Files are decoded to straight RGBA8
    /// with StbImageSharp (PNG, JPEG, BMP, GIF, TGA, PSD), which is what
    /// mitsuami's Image shows; raw buffers keep their format for the view to
    /// convert. Avalonia decodes with Skia, so WebP and ICO don't decode
    /// here: they throw, as a broken file does in Avalonia.
    /// </summary>
    public class Bitmap : IDisposable
    {
        public Bitmap(Stream stream) : this(Decode(stream)) { }

        public Bitmap(string fileName) : this(File.OpenRead(fileName)) { }

        public Bitmap(PixelFormat format, AlphaFormat alphaFormat, IntPtr data, PixelSize size, Vector dpi, int stride)
        {
            Format = format;
            AlphaFormat = alphaFormat;
            PixelSize = size;
            Dpi = dpi;
            Stride = stride;
            Pixels = new byte[stride * size.Height];
            Marshal.Copy(data, Pixels, 0, Pixels.Length);
        }

        private protected Bitmap(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
        {
            Format = format;
            AlphaFormat = alphaFormat;
            PixelSize = size;
            Dpi = dpi;
            Stride = size.Width * format.BytesPerPixel;
            Pixels = new byte[Stride * size.Height];
        }

        private Bitmap(ImageResult image)
        {
            Format = PixelFormats.Rgba8888;
            AlphaFormat = AlphaFormat.Unpremul;
            PixelSize = new PixelSize(image.Width, image.Height);
            Dpi = new Vector(96, 96);
            Stride = image.Width * 4;
            Pixels = image.Data;
        }

        public PixelFormat Format { get; }
        public AlphaFormat AlphaFormat { get; }
        public PixelSize PixelSize { get; }
        public Vector Dpi { get; }
        public int Stride { get; }
        public byte[] Pixels { get; }
        public Size Size => new(PixelSize.Width * 96 / Dpi.X, PixelSize.Height * 96 / Dpi.Y);

        /// <summary>Avatars ask for 128 pixels wide; the view scales, so the
        /// full image is kept.</summary>
        public static Bitmap DecodeToWidth(Stream stream, int width) => new(stream);

        public void Dispose() { }

        private static ImageResult Decode(Stream stream)
        {
            // StbImageSharp reads the whole stream; seekable or not.
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return ImageResult.FromStream(copy, ColorComponents.RedGreenBlueAlpha);
        }
    }

    public sealed class WriteableBitmap(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
        : Bitmap(size, dpi, format, alphaFormat)
    {
        /// <summary>The pixels, pinned until the framebuffer is disposed.</summary>
        public ILockedFramebuffer Lock() => new LockedFramebuffer(this);

        private sealed class LockedFramebuffer : ILockedFramebuffer
        {
            private GCHandle _pin;

            public LockedFramebuffer(WriteableBitmap bitmap)
            {
                _pin = GCHandle.Alloc(bitmap.Pixels, GCHandleType.Pinned);
                Address = _pin.AddrOfPinnedObject();
                Size = bitmap.PixelSize;
                RowBytes = bitmap.Stride;
                Dpi = bitmap.Dpi;
                Format = bitmap.Format;
            }

            public IntPtr Address { get; }
            public PixelSize Size { get; }
            public int RowBytes { get; }
            public Vector Dpi { get; }
            public PixelFormat Format { get; }

            public void Dispose()
            {
                if (_pin.IsAllocated)
                    _pin.Free();
            }
        }
    }
}
