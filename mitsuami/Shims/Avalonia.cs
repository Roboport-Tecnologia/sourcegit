// Stand-ins for the Avalonia types upstream's models, view models and
// Native code use, under Avalonia's own names, so those files build
// unchanged. They're plain data: mitsuami's views read them.

using System;

namespace Avalonia
{
    public readonly record struct Point(double X, double Y);

    public readonly record struct Vector(double X, double Y)
    {
        public static Vector Zero { get; } = new(0, 0);
    }

    public readonly record struct Size(double Width, double Height);

    public readonly record struct PixelSize(int Width, int Height);

    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
        public Thickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }
    }

    public readonly record struct CornerRadius(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
    {
        public CornerRadius(double uniform) : this(uniform, uniform, uniform, uniform) { }
        public CornerRadius(double top, double bottom) : this(top, top, bottom, bottom) { }
    }

    /// <summary>Native's <c>SetupApp</c> takes one; mitsuami has no
    /// platform options to give, so they're dropped.</summary>
    public sealed class AppBuilder
    {
        public AppBuilder With<T>(T options) => this;
    }

    public sealed class MacOSPlatformOptions
    {
        public bool DisableDefaultApplicationMenuItems { get; set; }
    }

    public sealed class X11PlatformOptions
    {
        public bool EnableIme { get; set; }
    }
}
