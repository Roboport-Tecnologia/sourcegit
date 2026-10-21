using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGit
{
    /// <summary>
    /// SourceGit's app on mitsuami, in place of upstream's Avalonia
    /// <c>App</c>: the members upstream's view models call.
    /// </summary>
    public partial class App
    {
        /// <summary>
        /// A message from upstream's locale dictionaries
        /// (<c>Resources/Locales/*.axaml</c>, copied next to the app), as
        /// upstream's <c>App.Text</c>: the active locale over en_US, and the
        /// key itself when neither has it.
        /// </summary>
        public static string Text(string key, params object[] args)
        {
            var fullKey = $"Text.{key}";
            if (!s_activeLocale.TryGetValue(fullKey, out var fmt) && !s_fallbackLocale.TryGetValue(fullKey, out fmt))
                return fullKey;
            if (string.IsNullOrWhiteSpace(fmt))
                return fullKey;

            if (args == null || args.Length == 0)
                return fmt;

            return string.Format(fmt, args);
        }

        public static void SetLocale(string localeKey)
        {
            var locale = Models.Locale.Supported.Find(x => x.Key.Equals(localeKey, StringComparison.OrdinalIgnoreCase));
            s_activeLocale = LoadLocale(locale?.Key ?? "en_US");
        }

        /// <summary>mitsuami's controls follow the platform's light or dark
        /// appearance, so the preference is kept but not applied yet.</summary>
        public static void SetTheme(string theme, string themeOverridesFile) { }

        /// <summary>Native controls use the platform's fonts; the monospace
        /// one is for the code views (bemtvi), which come later.</summary>
        public static void SetFonts(string defaultFont, string monospaceFont) { }

        public static ViewModels.Launcher GetLauncher() => s_launcher;

        public static Task<bool> AskConfirmAsync(string message, Models.ConfirmButtonType buttonType = Models.ConfirmButtonType.OkCancel)
        {
            throw new NotImplementedException("mitsuami: confirmations come with the views");
        }

        public static Task<Models.ConfirmEmptyCommitResult> AskConfirmEmptyCommitAsync(bool hasLocalChanges, bool hasSelectedUnstaged)
        {
            throw new NotImplementedException("mitsuami: confirmations come with the views");
        }

        /// <summary>mitsuami's run loop ends with its last window; until the
        /// C ABI can end it sooner, the process exits.</summary>
        public static void Quit(int exitCode) => Environment.Exit(exitCode);

        private static Dictionary<string, string> LoadLocale(string key)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var file = Path.Combine(AppContext.BaseDirectory, "Locales", $"{key}.axaml");
            if (!File.Exists(file))
                return strings;

            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var element in XDocument.Load(file).Root!.Elements(x + "String"))
            {
                if (element.Attribute(x + "Key")?.Value is { } name)
                    strings[name] = element.Value;
            }
            return strings;
        }

        private static readonly Dictionary<string, string> s_fallbackLocale = LoadLocale("en_US");
        private static Dictionary<string, string> s_activeLocale = s_fallbackLocale;
        private static ViewModels.Launcher s_launcher = null;
    }
}
