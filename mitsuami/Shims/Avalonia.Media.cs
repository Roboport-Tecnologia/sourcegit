using System;
using System.Collections.Generic;
using System.Globalization;

namespace Avalonia.Media
{
    /// <summary>An ARGB colour, as Avalonia's.</summary>
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
        public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

        public static Color FromUInt32(uint argb) =>
            new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        public uint ToUInt32() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

        /// <summary><c>#rgb</c>, <c>#rrggbb</c>, <c>#aarrggbb</c> or a
        /// known name, as theme overrides and preferences store them.</summary>
        public static Color Parse(string s)
        {
            if (TryParse(s, out var color))
                return color;
            throw new FormatException($"Invalid color string: '{s}'");
        }

        public static bool TryParse(string s, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            if (s[0] != '#')
                return Colors.TryGetNamed(s, out color);

            var hex = s[1..];
            if (hex.Length == 3)
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length == 6)
                hex = "ff" + hex;
            if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                return false;

            color = FromUInt32(argb);
            return true;
        }

        public override string ToString() => $"#{ToUInt32():x8}";
    }

    /// <summary>The named colours upstream's code uses, Avalonia's values.</summary>
    public static class Colors
    {
        public static Color Transparent => Color.FromUInt32(0x00ffffff);
        public static Color Black => Color.FromUInt32(0xff000000);
        public static Color White => Color.FromUInt32(0xffffffff);
        public static Color Red => Color.FromUInt32(0xffff0000);
        public static Color Green => Color.FromUInt32(0xff008000);
        public static Color Orange => Color.FromUInt32(0xffffa500);
        public static Color DarkOrange => Color.FromUInt32(0xffff8c00);
        public static Color Purple => Color.FromUInt32(0xff800080);
        public static Color ForestGreen => Color.FromUInt32(0xff228b22);
        public static Color Turquoise => Color.FromUInt32(0xff40e0d0);
        public static Color Olive => Color.FromUInt32(0xff808000);
        public static Color Magenta => Color.FromUInt32(0xffff00ff);
        public static Color Khaki => Color.FromUInt32(0xfff0e68c);
        public static Color Lime => Color.FromUInt32(0xff00ff00);
        public static Color RoyalBlue => Color.FromUInt32(0xff4169e1);
        public static Color Teal => Color.FromUInt32(0xff008080);
        public static Color DarkCyan => Color.FromUInt32(0xff008b8b);
        public static Color DeepSkyBlue => Color.FromUInt32(0xff00bfff);
        public static Color Gold => Color.FromUInt32(0xffffd700);

        private static readonly Dictionary<string, Color> s_named = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Transparent"] = Transparent, ["Black"] = Black, ["White"] = White, ["Red"] = Red,
            ["Green"] = Green, ["Orange"] = Orange, ["DarkOrange"] = DarkOrange, ["Purple"] = Purple,
            ["ForestGreen"] = ForestGreen, ["Turquoise"] = Turquoise, ["Olive"] = Olive, ["Magenta"] = Magenta,
            ["Khaki"] = Khaki, ["Lime"] = Lime, ["RoyalBlue"] = RoyalBlue, ["Teal"] = Teal,
            ["DarkCyan"] = DarkCyan, ["DeepSkyBlue"] = DeepSkyBlue, ["Gold"] = Gold,
        };

        internal static bool TryGetNamed(string name, out Color color) => s_named.TryGetValue(name, out color);
    }

    public interface IBrush
    {
        double Opacity { get; }
    }

    public interface ISolidColorBrush : IBrush
    {
        Color Color { get; }
    }

    public sealed class SolidColorBrush : ISolidColorBrush
    {
        public SolidColorBrush(Color color) => Color = color;
        public SolidColorBrush(uint argb) : this(Color.FromUInt32(argb)) { }

        public Color Color { get; }
        public double Opacity => 1;
    }

    public static class Brushes
    {
        public static ISolidColorBrush Transparent { get; } = new SolidColorBrush(Colors.Transparent);
        public static ISolidColorBrush Red { get; } = new SolidColorBrush(Colors.Red);
        public static ISolidColorBrush Green { get; } = new SolidColorBrush(Colors.Green);
        public static ISolidColorBrush Orange { get; } = new SolidColorBrush(Colors.Orange);
        public static ISolidColorBrush DarkOrange { get; } = new SolidColorBrush(Colors.DarkOrange);
        public static ISolidColorBrush Purple { get; } = new SolidColorBrush(Colors.Purple);
        public static ISolidColorBrush ForestGreen { get; } = new SolidColorBrush(Colors.ForestGreen);
        public static ISolidColorBrush DarkCyan { get; } = new SolidColorBrush(Colors.DarkCyan);
        public static ISolidColorBrush DeepSkyBlue { get; } = new SolidColorBrush(Colors.DeepSkyBlue);
        public static ISolidColorBrush Gold { get; } = new SolidColorBrush(Colors.Gold);
    }

    /// <summary>A line colour and width: the commit graph's, for its drawn
    /// view.</summary>
    public sealed class Pen(uint color, double thickness)
    {
        public Color Color { get; } = Color.FromUInt32(color);
        public double Thickness { get; } = thickness;
    }

    /// <summary>Only <c>FormatFontNames</c> uses it, to drop fonts that
    /// aren't installed. mitsuami passes font names to the platform, which
    /// falls back on its own, so every name is kept.</summary>
    public sealed class FontFamily
    {
        private FontFamily(string name) => Name = name;

        public string Name { get; }
        public IReadOnlyList<string> FamilyTypefaces => [Name];

        public static FontFamily Parse(string s) => new(s);
    }
}
