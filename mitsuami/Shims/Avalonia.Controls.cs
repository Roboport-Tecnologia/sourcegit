using System.Collections.Generic;

using Avalonia.Platform;

namespace Avalonia.Controls
{
    public enum WindowState
    {
        Normal,
        Minimized,
        Maximized,
        FullScreen,
    }

    public enum GridUnitType
    {
        Auto,
        Pixel,
        Star,
    }

    /// <summary>A grid track's size, which layouts remember (splitter
    /// positions).</summary>
    public readonly record struct GridLength(double Value, GridUnitType GridUnitType = GridUnitType.Pixel)
    {
        public static GridLength Auto { get; } = new(1, GridUnitType.Auto);
        public static GridLength Star { get; } = new(1, GridUnitType.Star);

        public bool IsAbsolute => GridUnitType == GridUnitType.Pixel;
        public bool IsAuto => GridUnitType == GridUnitType.Auto;
        public bool IsStar => GridUnitType == GridUnitType.Star;
    }

    /// <summary>
    /// What Native's <c>SetupWindow</c> sets on Avalonia's windows (chrome,
    /// borders) and reads (the platform handle). mitsuami's windows are the
    /// platform's own, with its own chrome, so nothing reads these, and
    /// there's no handle: Native's frame fixes skip themselves.
    /// </summary>
    public class Window
    {
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
        public WindowState WindowState { get; set; }
        public ExtendClientAreaChromeHints ExtendClientAreaChromeHints { get; set; }
        public bool ExtendClientAreaToDecorationsHint { get; set; }
        public List<string> Classes { get; } = [];

        public IPlatformHandle TryGetPlatformHandle() => null;
    }
}
