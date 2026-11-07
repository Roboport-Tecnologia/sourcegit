using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        /// <summary>How the code views edit: what people who don't use vim
        /// expect, unless they say otherwise.</summary>
        public string EditorMode { get; set; } = "modeless";

        public static readonly string[] EditorModes = ["modeless", "vim", "helix"];

        /// <summary>Relative line numbers in the code views, for counts.</summary>
        public bool EditorRelativeNumbers { get; set; }

        /// <summary>Whether a language with no grammar gets one, which downloads
        /// and builds code: off until asked for.</summary>
        public bool EditorInstallGrammars { get; set; }

        /// <summary>Lines of context around a diff's changes; upstream's
        /// starting number, which it goes no lower than.</summary>
        public int EditorDiffContext { get; set; } = MinDiffContext;

        public const int MinDiffContext = 4;

        /// <summary>Every line of a diff, not only those around its changes.</summary>
        public bool EditorDiffAllLines { get; set; }

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
                    return JsonSerializer.Deserialize(File.ReadAllText(FilePath), PortSettingsJson.Default.PortSettings) ?? new();
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
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, PortSettingsJson.Default.PortSettings));
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }
        }
    }

    /// <summary>The settings' JSON, generated at build time: an AOT app
    /// has no reflection to serialize with.</summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(PortSettings))]
    internal partial class PortSettingsJson : JsonSerializerContext
    {
    }
}
