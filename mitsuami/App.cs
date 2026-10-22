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

        /// <summary>Upstream's startup makes the launcher once the app runs:
        /// it opens the workspace's repositories, and <paramref name="startupRepo"/>
        /// (from the command line) if given.</summary>
        public static ViewModels.Launcher CreateLauncher(string startupRepo)
        {
            s_launcher = new ViewModels.Launcher(startupRepo);
            s_mainWindow = Views.Launcher.Create(s_launcher);
            CaptureIfAsked();
            return s_launcher;
        }

        /// <summary>Upstream's confirmation, as the platform's alert on the
        /// main window.</summary>
        public static async Task<bool> AskConfirmAsync(string message, Models.ConfirmButtonType buttonType = Models.ConfirmButtonType.OkCancel)
        {
            var (yes, no) = buttonType == Models.ConfirmButtonType.YesNo ? ("Yes", "No") : ("Sure", "Cancel");
            var buttons = new[] { Views.Popups.ButtonText(yes), Views.Popups.ButtonText(no) };
            return await Mitsuamia.Dialogs.Alert(s_mainWindow, message, "", buttons, Mitsuamia.AlertStyle.Warning) == 0;
        }

        public static Task<Models.ConfirmEmptyCommitResult> AskConfirmEmptyCommitAsync(bool hasLocalChanges, bool hasSelectedUnstaged)
        {
            throw new NotImplementedException("mitsuami: confirmations come with the views");
        }

        /// <summary>mitsuami's run loop ends with its last window; until the
        /// C ABI can end it sooner, the process exits.</summary>
        public static void Quit(int exitCode) => Environment.Exit(exitCode);

        /// <summary>For development: with <c>SOURCEGIT_CAPTURE</c> set to a
        /// path, captures the main window there once it has settled, so its
        /// look can be checked without a screenshot permission.
        /// <c>SOURCEGIT_CAPTURE_TAB</c> picks the tab first: an index, or
        /// <c>new</c> for a welcome page. <c>SOURCEGIT_CAPTURE_POPUP</c>
        /// opens a popup and captures its sheet: <c>clone</c>, or <c>move</c>
        /// (the first repository or group). <c>SOURCEGIT_CAPTURE_REPO</c>
        /// opens a repository by its path first.</summary>
        private static async void CaptureIfAsked()
        {
            if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE") is not { Length: > 0 } path)
                return;
            // SOURCEGIT_CAPTURE_TAB picks a tab by its index, or "new" opens
            // a welcome page (a repository the workspace reopens takes the
            // first one's place).
            var tab = Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_TAB");
            if (tab == "new")
                s_launcher.AddNewTab();
            else if (int.TryParse(tab, out var index) && index < s_launcher.Pages.Count)
                s_launcher.ActivePage = s_launcher.Pages[index];
            // SOURCEGIT_CAPTURE_REPO opens a repository by its path, as a
            // double-click on the welcome page does; one the page doesn't
            // have is added for this run only (nothing is saved).
            if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_REPO") is { Length: > 0 } repo)
                ViewModels.Preferences.Instance.FindOrAddNodeByRepositoryPath(repo, null, false, false).Open();
            switch (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_POPUP"))
            {
                case "clone":
                    ViewModels.Welcome.Instance.Clone();
                    break;
                case "move" when ViewModels.Preferences.Instance.RepositoryNodes is [var first, ..]:
                    first.Move();
                    break;
            }
            await Task.Delay(TimeSpan.FromSeconds(3));
            var ok = await (Views.Popups.Shown ?? s_mainWindow).CapturePng(path);
            Console.WriteLine(ok ? $"Captured to {path}" : "Capture failed");
        }

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
        private static Mitsuamia.Window s_mainWindow = null;
    }
}
