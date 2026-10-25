using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using Mitsuamia;

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

            // Upstream marks a name it highlights, "${0}$", and its menus and
            // NameHighlightedTextBlock draw it so. Native text has no such
            // run (a menu showed "Reset $main$ to …"): the name goes in plain.
            return string.Format(s_highlighted.Replace(fmt, "{$1}"), args);
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

        /// <summary>Upstream's <c>ConfirmEmptyCommit</c> window as an alert:
        /// Cancel first, upstream's primary button, so it's the default;
        /// then committing empty, and staging all or the selection first
        /// when there's something to stage.</summary>
        public static async Task<Models.ConfirmEmptyCommitResult> AskConfirmEmptyCommitAsync(bool hasLocalChanges, bool hasSelectedUnstaged)
        {
            var results = new List<Models.ConfirmEmptyCommitResult> { Models.ConfirmEmptyCommitResult.Cancel, Models.ConfirmEmptyCommitResult.CreateEmptyCommit };
            var buttons = new List<string> { Views.Popups.ButtonText("Cancel"), Views.Popups.ButtonText("ConfirmEmptyCommit.Continue") };
            if (hasLocalChanges)
            {
                results.Add(Models.ConfirmEmptyCommitResult.StageAllAndCommit);
                buttons.Add(Views.Popups.ButtonText("ConfirmEmptyCommit.StageAllThenCommit"));
            }
            if (hasSelectedUnstaged)
            {
                results.Add(Models.ConfirmEmptyCommitResult.StageSelectedAndCommit);
                buttons.Add(Views.Popups.ButtonText("ConfirmEmptyCommit.StageSelectedThenCommit"));
            }
            var message = Text(hasLocalChanges ? "ConfirmEmptyCommit.WithLocalChanges" : "ConfirmEmptyCommit.NoLocalChanges");
            var chosen = await Mitsuamia.Dialogs.Alert(s_mainWindow, message, "", buttons, Mitsuamia.AlertStyle.Warning);
            return chosen >= 0 && chosen < results.Count ? results[chosen] : Models.ConfirmEmptyCommitResult.Cancel;
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
        /// opens a repository by its path first. <c>SOURCEGIT_CAPTURE_COMMIT</c>
        /// selects commits of the history (indexes, comma-separated), and
        /// <c>SOURCEGIT_CAPTURE_MENU</c> then picks the first item of their
        /// menu whose title starts with that text, as a click on it does.</summary>
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
            // SOURCEGIT_CAPTURE_COMMIT selects the history's commits at those
            // indexes, as clicks do, and waits for their details.
            if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_COMMIT") is { Length: > 0 } commits
                && s_launcher.ActivePage?.Data is ViewModels.Repository { Histories: { } histories } opened)
            {
                var picked = commits.Split(',').Select(int.Parse).Where(i => i < histories.Commits.Count).Select(i => histories.Commits[i]).ToList();
                histories.SelectedCommits = picked;
                await Task.Delay(TimeSpan.FromSeconds(2));
                // SOURCEGIT_CAPTURE_MENU picks an item of their menu by its title.
                if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_MENU") is { Length: > 0 } title && picked.Count > 0)
                {
                    var item = Find(Views.CommitMenu.For(opened, histories, picked[0]), title);
                    Console.WriteLine(item != null ? $"Picked {item.Title}" : $"No menu item has {title}");
                    item?.Action();
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            // SOURCEGIT_CAPTURE_PAGES picks the rail's pages in turn, as
            // clicks do: "1,0" goes to the local changes and back.
            if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_PAGES") is { Length: > 0 } pages
                && s_launcher.ActivePage?.Data is ViewModels.Repository repository)
            {
                var shown = 0;
                foreach (var page in pages.Split(','))
                {
                    repository.SelectedViewIndex = int.Parse(page);
                    // SOURCEGIT_CAPTURE_EACH also captures right after each
                    // pick, before anything settles, to catch what flashes.
                    if (Environment.GetEnvironmentVariable("SOURCEGIT_CAPTURE_EACH") == "1")
                        await s_mainWindow.CapturePng(path.Replace(".png", $"-{shown++}.png"));
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            var ok = await (Views.Notifications.Shown ?? Views.Popups.Shown ?? Views.InteractiveRebase.Editing ?? Views.InteractiveRebase.Shown ?? s_mainWindow).CapturePng(path);
            Console.WriteLine(ok ? $"Captured to {path}" : "Capture failed");
        }

        private static MenuItem Find(IReadOnlyList<MenuEntry> entries, string title)
        {
            foreach (var entry in entries)
            {
                var found = entry switch
                {
                    // As a click: a disabled item can't be picked.
                    MenuItem item when item.Enabled && item.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase) => item,
                    Submenu submenu => Find(submenu.Entries, title),
                    _ => null,
                };
                if (found != null)
                    return found;
            }
            return null;
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

        private static readonly System.Text.RegularExpressions.Regex s_highlighted = new(@"\$\{(\d+)\}\$");

        private static readonly Dictionary<string, string> s_fallbackLocale = LoadLocale("en_US");
        private static Dictionary<string, string> s_activeLocale = s_fallbackLocale;
        private static ViewModels.Launcher s_launcher = null;
        /// <summary>The launcher's window, which dialogs go on.</summary>
        public static Mitsuamia.Window MainWindow => s_mainWindow;

        private static Mitsuamia.Window s_mainWindow = null;
    }
}
