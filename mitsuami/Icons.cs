using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

using Mitsuamia;

namespace SourceGit
{
    /// <summary>
    /// Upstream's own icons (<c>Resources/Icons.axaml</c>, copied next to the
    /// app): path data that mitsuami draws as each platform draws its
    /// symbolic icons. Upstream's paths fill their 16-point boxes (a
    /// <c>Path</c> stretched uniformly), so they're fitted to their bounds.
    /// </summary>
    public static class Icons
    {
        /// <summary>An icon by upstream's key, with or without its
        /// <c>Icons.</c> prefix: <c>Icons.Get("Clone")</c>.</summary>
        public static VectorIcon Get(string key)
        {
            var name = key.StartsWith("Icons.", StringComparison.Ordinal) ? key : $"Icons.{key}";
            if (s_icons.TryGetValue(name, out var icon))
                return icon;
            throw new KeyNotFoundException($"mitsuami: no icon {name} in Icons.axaml");
        }

        /// <summary>
        /// An icon as upstream sizes it in a button: a <c>Path</c> of
        /// <paramref name="size"/> points in the button's 16-point icon,
        /// moved down <paramref name="down"/> points (half the top margin
        /// upstream gives it). Upstream balances its icons this way, so a
        /// view box padded around the path's bounds does the same. No more
        /// than the 16 points the button has: a larger one is cut off.
        /// </summary>
        public static VectorIcon Get(string key, float size, float down = 0)
        {
            size = Math.Min(size, ButtonIconSide);
            var icon = Get(key);
            if (VectorIcon.Bounds(icon.Data) is not { } bounds)
                return icon;
            var side = Math.Max(bounds.Width, bounds.Height) * ButtonIconSide / size;
            var unit = side / ButtonIconSide;
            var x = bounds.X + bounds.Width / 2 - side / 2;
            var y = bounds.Y + bounds.Height / 2 - side / 2 - down * unit;
            return icon with { ViewBox = new ViewBox(x, y, side, side) };
        }

        /// <summary>The side mitsuami gives a button's icon.</summary>
        private const float ButtonIconSide = 16;

        /// <summary>The gap between icon-only borderless buttons side by
        /// side. AppKit's have no padding of their own, so there it's the
        /// medium spacing, or their icons nearly touch; elsewhere it's
        /// <paramref name="other"/>, the row's own.</summary>
        public static Length ButtonGap(Length other) => OperatingSystem.IsMacOS() ? Spacing.Md : other;

        /// <summary>As <see cref="ButtonGap"/>, for the toolbar bands at
        /// the top of the windows: the large spacing on macOS.</summary>
        public static Length ToolbarGap(Length other) => OperatingSystem.IsMacOS() ? Spacing.Lg : other;

        private static Dictionary<string, VectorIcon> Load()
        {
            var icons = new Dictionary<string, VectorIcon>(StringComparer.Ordinal);
            var file = Path.Combine(AppContext.BaseDirectory, "Icons.axaml");
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var element in XDocument.Load(file).Root!.Elements())
            {
                if (element.Name.LocalName == "StreamGeometry" && element.Attribute(x + "Key")?.Value is { } name)
                    icons[name] = new VectorIcon(element.Value.Trim());
            }
            return icons;
        }

        private static readonly Dictionary<string, VectorIcon> s_icons = Load();
    }
}
