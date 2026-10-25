using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's update check (<c>App.Check4Update</c>, its
    /// <c>SelfUpdate</c> window), asked for from the app menu: the latest
    /// release from upstream's site, and the answer as the platform's
    /// alert: a new version with its date and notes and a way to download
    /// it, none, or why the check failed.
    /// </summary>
    public static class Updates
    {
        public static void Check() => _ = CheckAsync();

        private static async Task CheckAsync()
        {
            object result;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var data = await client.GetStringAsync("https://sourcegit-scm.github.io/data/version.json");
                var version = JsonSerializer.Deserialize(data, JsonCodeGen.Default.Version);
                result = version is { IsNewVersion: true } ? version : new Models.AlreadyUpToDate();
            }
            catch (Exception e)
            {
                result = new Models.SelfUpdateFailed(e);
            }
            Application.Post(() => _ = Show(result));
        }

        private static async Task Show(object result)
        {
            var title = App.Text("SelfUpdate.Title");
            switch (result)
            {
                case Models.Version version:
                    var message = $"{App.Text("SelfUpdate.Available")}{version.Name}\n"
                        + $"{App.Text("SelfUpdate.CurrentVersion")}{version.CurrentVersionStr}\n"
                        + $"{App.Text("SelfUpdate.ReleaseDate")}{version.ReleaseDateStr}\n\n{version.Body}";
                    var buttons = new[] { App.Text("SelfUpdate.GotoDownload"), App.Text("SelfUpdate.IgnoreThisVersion"), Popups.ButtonText("Cancel") };
                    var chosen = await Dialogs.Alert(App.MainWindow, title, message, buttons);
                    if (chosen == 0)
                        Native.OS.OpenBrowser("https://github.com/sourcegit-scm/sourcegit/releases/latest");
                    else if (chosen == 1)
                        ViewModels.Preferences.Instance.IgnoreUpdateTag = version.TagName;
                    break;
                case Models.SelfUpdateFailed failed:
                    await Dialogs.Alert(App.MainWindow, App.Text("SelfUpdate.Error"), failed.Reason, [Popups.ButtonText("Sure")], AlertStyle.Warning);
                    break;
                default:
                    await Dialogs.Alert(App.MainWindow, title, App.Text("SelfUpdate.UpToDate"), [Popups.ButtonText("Sure")]);
                    break;
            }
        }
    }
}
