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
