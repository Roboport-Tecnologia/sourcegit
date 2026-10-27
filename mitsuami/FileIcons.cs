using System;
using System.Collections.Generic;
using System.IO;

using Mitsuamia;

namespace SourceGit
{
    /// <summary>
    /// The platform's icons for the files and folders in the port's trees.
    /// A file icon reads a path, and a tree's files may be gone (deleted,
    /// or only in an old revision), so each shows an empty stand-in with
    /// its extension (its name, without one), made once in the temporary
    /// folder: the icon of its type, as the file manager shows it, the
    /// same in the working copy and in history, and nothing of the
    /// repository read to draw it.
    /// </summary>
    public static class FileIcons
    {
        private static readonly string s_folder = Path.Combine(Path.GetTempPath(), "sourcegit-mitsuami-icons");
        private static readonly HashSet<string> s_made = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The icon of a file at <paramref name="path"/>'s type.</summary>
        public static FileIcon File(string path)
        {
            var name = Path.GetFileName(path);
            var extension = Path.GetExtension(name);
            return new FileIcon(StandIn(string.IsNullOrEmpty(extension) ? name : "file" + extension, false));
        }

        /// <summary>A plain folder's icon.</summary>
        public static FileIcon Folder() => new(StandIn("folder", true));

        private static string StandIn(string name, bool folder)
        {
            var path = Path.Combine(s_folder, folder ? name : Path.Combine("files", name));
            if (s_made.Add(path))
            {
                // Without its stand-in, the platform shows what it shows for
                // a path that's missing: a generic document, or nothing.
                try
                {
                    if (folder)
                        Directory.CreateDirectory(path);
                    else if (!System.IO.File.Exists(path))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        System.IO.File.Create(path).Dispose();
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
            return path;
        }
    }
}
