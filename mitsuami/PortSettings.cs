using System;
using System.IO;
using System.Text.Json;

namespace SourceGit
{
    /// <summary>
    /// The port's own settings, which upstream's preferences have no place
    /// for: <c>mitsuami.json</c> beside the port's <c>preference.json</c>.
    /// Saved as the preferences are, never in captures.
    /// </summary>
    public sealed class PortSettings
    {
        /// <summary>bemtvi's colorscheme for the code views (diffs, blame,
        /// conflicts): any it has, its own or Neovim's.</summary>
        public string EditorColorscheme { get; set; } = "bemtvi";

        /// <summary>bemtvi's own, then Neovim's bundled ones; a colorscheme
        /// from a plugin is kept if it's set.</summary>
        public static readonly string[] Colorschemes =
        [
            "bemtvi", "neutral",
            "blue", "catppuccin", "darkblue", "default", "delek", "desert", "elflord", "evening", "habamax",
            "industry", "koehler", "lunaperche", "morning", "murphy", "pablo", "peachpuff", "quiet", "retrobox",
            "ron", "shine", "slate", "sorbet", "torte", "unokai", "vim", "wildcharm", "zaibatsu", "zellner",
        ];

        public static PortSettings Instance { get; } = Load();

        /// <summary>Set once the app starts saving, as upstream's
        /// <c>SetCanModify</c>.</summary>
        public static bool CanSave { get; set; }

        private static string FilePath => Path.Combine(Native.OS.BasicDirectories.ConfigDir, "mitsuami.json");

        private static PortSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<PortSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }
            return new();
        }

        public void Save()
        {
            if (!CanSave)
                return;
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }
        }
    }
}
