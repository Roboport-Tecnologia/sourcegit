using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SourceGit
{
    /// <summary>
    /// Upstream's strings, icons and images, embedded in the app as
    /// Avalonia embeds them, so a published app is its executable and not a
    /// folder of files. Each is named by the path it had beside the app, as
    /// <c>Locales/en_US.axaml</c>.
    /// </summary>
    public static class EmbeddedFiles
    {
        /// <summary>The file at <paramref name="path"/>, or null.</summary>
        public static Stream Open(string path)
        {
            return s_names.TryGetValue(Normalize(path), out var name)
                ? typeof(EmbeddedFiles).Assembly.GetManifestResourceStream(name)
                : null;
        }

        public static bool Exists(string path) => s_names.ContainsKey(Normalize(path));

        private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

        // MSBuild's %(RecursiveDir) has the build machine's separator, so
        // names are looked up with forward slashes.
        private static Dictionary<string, string> Load()
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in typeof(EmbeddedFiles).Assembly.GetManifestResourceNames())
                names[Normalize(name)] = name;
            return names;
        }

        private static readonly Dictionary<string, string> s_names = Load();
    }
}
