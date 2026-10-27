using Mitsuamia;

namespace SourceGit
{
    /// <summary>
    /// The platform's icons for the files and folders the port shows. A
    /// repository's files may be gone (deleted, or only in an old
    /// revision), so they go by name: the icon of the file's type, as the
    /// file manager shows it, the same in the working copy and in
    /// history, with nothing of the repository read to draw it.
    /// </summary>
    public static class FileIcons
    {
        /// <summary>The icon of a file at <paramref name="path"/>'s type.
        /// A path ending in a slash is a folder (a submodule's, in a diff).</summary>
        public static FileIcon File(string path) =>
            path.EndsWith('/') ? Folder(path) : new FileIcon(path, FileLookup.FileName);

        /// <summary>A plain folder's icon.</summary>
        public static FileIcon Folder(string path = "folder") => new(path, FileLookup.Folder);
    }
}
