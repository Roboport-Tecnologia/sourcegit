using System;
using System.Linq;

using Mitsuamia;

using static SourceGit.Views.Settings;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's preferences (<c>Views/Preferences.axaml</c>): a window of
    /// its own, opened from the app menu's Settings item (⌘, on macOS), with
    /// upstream's tabs. Git's global settings are read as upstream reads
    /// them. Read-only for now (<see cref="Settings.Editable"/>): our app
    /// never saves the preferences, and upstream saves on closing.
    /// </summary>
    public static class Preferences
    {
        private static Window s_shown;

        /// <summary>The window, if it's open: for captures.</summary>
        public static Window Shown => s_shown;

        public static void Show()
        {
            if (s_shown != null)
                return;
            var pref = ViewModels.Preferences.Instance;
            var window = s_shown = new Window(App.Text("Preferences"), 720);
            window.Closed += () => s_shown = null;
            var tabs = Tabs(
                ("Preferences.General", General(pref)),
                ("Preferences.Appearance", Appearance(pref)),
                ("Preferences.Git", Git(pref)),
                ("Preferences.GPG", Gpg(pref)),
                ("Preferences.Integration", Integration(pref)),
                ("Configure.CustomAction", ListDetail(pref.CustomActions, a => a.Name, "Action", CustomAction, moves: true)),
                ("Preferences.AI", ListDetail(pref.OpenAIServices, s => s.Name, "AIAssist", Service)));
            var column = new Column { Padding = 16, AlignItems = Align.Stretch };
            column.Add(tabs);
            window.Content = column;
        }

        private static Node General(ViewModels.Preferences pref)
        {
            var page = Page();
            page.Add(Field(App.Text("Preferences.General.Locale"),
                Choice(App.Text("Preferences.General.Locale"), Models.Locale.Supported.Select(l => l.Name),
                    Models.Locale.Supported.FindIndex(l => l.Key == pref.Locale))));
            var date = new Row { Gap = 8, AlignItems = Align.Center };
            var format = Choice(App.Text("Preferences.General.DateFormat"),
                Models.DateTimeFormat.Supported.Select(f => $"{f.Example}  ({f.DateFormat})"), pref.DateTimeFormat);
            format.Grow = 1;
            date.Add(format);
            date.Add(Check(App.Text("Preferences.General.Use24Hours"), pref.Use24Hours));
            page.Add(Field(App.Text("Preferences.General.DateFormat"), date));
            page.Add(Field(App.Text("Preferences.Git.DefaultCloneDir"), Path(pref.GitDefaultCloneDir)));
            // Upstream's subject guide doesn't snap to its ticks; the
            // commit count snaps to thousands of five.
            page.Add(Field(App.Text("Preferences.General.SubjectGuideLength"), Slider(50, 200, 0, pref.SubjectGuideLength)));
            page.Add(Field(App.Text("Preferences.General.MaxHistoryCommits"), Slider(5000, 100000, 5000, pref.MaxHistoryCommits)));
            foreach (var (key, value) in new[]
            {
                ("Preferences.General.ShowChangesPageByDefault", pref.ShowLocalChangesByDefault),
                ("Preferences.General.ShowChangesTabInCommitDetailByDefault", pref.ShowChangesInCommitDetailByDefault),
                ("Preferences.General.ShowTagsInGraph", pref.ShowTagsInGraph),
                ("Preferences.General.ShowRelativeTimeInGraph", pref.DisplayTimeAsPeriodInHistories),
                ("Preferences.General.UseCompactBranchNames", pref.UseCompactBranchNamesInGraph),
                ("Preferences.General.EnableCompactFolders", pref.EnableCompactFoldersInChangesTree),
                ("Preferences.General.UseGitHubStyleAvatar", pref.UseGitHubStyleAvatar),
                ("Preferences.General.Check4UpdatesOnStartup", pref.Check4UpdatesOnStartup),
            })
                page.Add(Field("", Check(App.Text(key), value)));
            return page;
        }

        /// <summary>Upstream's sliders show their value beside them.</summary>
        private static Row Slider(double min, double max, double step, double value)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            var slider = new Mitsuamia.Slider { IsEnabled = Editable };
            slider.SetRange(min, max, step);
            slider.Value = value;
            slider.Grow = 1;
            row.Add(slider);
            var shown = new Row { Justify = Justify.End };
            shown.Width = 56;
            shown.Shrink = 0;
            shown.Add(new Text(value.ToString()));
            row.Add(shown);
            return row;
        }

        private static Node Appearance(ViewModels.Preferences pref)
        {
            var page = Page();
            string[] themes = ["Default", "Dark", "Light"];
            page.Add(Field(App.Text("Preferences.Appearance.Theme"),
                Choice(App.Text("Preferences.Appearance.Theme"), themes, Array.IndexOf(themes, pref.Theme))));
            page.Add(Field(App.Text("Preferences.Appearance.DefaultFont"), TextValue(pref.DefaultFontFamily)));
            page.Add(Field(App.Text("Preferences.Appearance.MonospaceFont"), TextValue(pref.MonospaceFontFamily)));
            // Upstream's sizes go in halves, which a platform's number field
            // doesn't: a drop-down of them, as font size pickers have.
            var sizes = Enumerable.Range(0, 17).Select(i => 10 + i * 0.5).ToList();
            var fontSizes = new Row { Gap = 8, AlignItems = Align.Center };
            foreach (var (key, size) in new[] { ("Preferences.Appearance.FontSize.Default", pref.DefaultFontSize), ("Preferences.Appearance.FontSize.Editor", pref.EditorFontSize) })
            {
                fontSizes.Add(new Text(App.Text(key)) { Color = Color.SecondaryLabel });
                var select = Choice(App.Text(key), sizes.Select(s => s.ToString()), sizes.IndexOf(size));
                select.Grow = 1;
                fontSizes.Add(select);
            }
            page.Add(Field(App.Text("Preferences.Appearance.FontSize"), fontSizes));
            var tabWidth = new NumberInput(App.Text("Preferences.Appearance.EditorTabWidth")) { IsEnabled = Editable };
            tabWidth.SetRange(1, 16);
            tabWidth.Value = pref.EditorTabWidth;
            page.Add(Field(App.Text("Preferences.Appearance.EditorTabWidth"), Leading(tabWidth)));
            var overrides = Path(pref.ThemeOverrides);
            var themesSite = new Button(App.Text("Preferences.Appearance.ThemeOverrides"))
            {
                Icon = Icons.Get("Remotes", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = "https://github.com/sourcegit-scm/sourcegit-theme",
            };
            themesSite.Click += () => Native.OS.OpenBrowser("https://github.com/sourcegit-scm/sourcegit-theme");
            overrides.Add(themesSite);
            page.Add(Field(App.Text("Preferences.Appearance.ThemeOverrides"), overrides));
            page.Add(Field("", Check(App.Text("Preferences.Appearance.UseFixedTabWidth"), pref.UseFixedTabWidth)));
            page.Add(Field("", Check(App.Text("Preferences.Appearance.UseAutoHideScrollBars"), pref.UseAutoHideScrollBars)));
            if (OperatingSystem.IsLinux())
                page.Add(Field("", Check(App.Text("Preferences.Appearance.UseNativeWindowFrame"), pref.UseSystemWindowFrame)));
            return page;
        }

        /// <summary>A control at its own size, at the field's start.</summary>
        private static Row Leading(Node control)
        {
            var row = new Row { AlignItems = Align.Center };
            row.Add(control);
            return row;
        }

        private static Node Git(ViewModels.Preferences pref)
        {
            var page = Page();
            var config = pref.IsGitConfigured() ? new Commands.Config(null).ReadAll() : new();
            string Read(string key) => config.TryGetValue(key, out var value) ? value : "";

            page.Add(Field(App.Text("Preferences.Git.Path"), Path(pref.GitInstallPath)));
            var version = new Row { Gap = 8, AlignItems = Align.Center };
            version.Add(new Text(Native.OS.GitVersionString ?? "") { Selectable = true });
            if (!string.IsNullOrEmpty(Native.OS.GitVersionString) && Native.OS.GitVersion < Models.GitVersions.MINIMAL)
                version.Add(new Icon(Icons.Get("Error")) { Size = 14, Color = Color.Error, ToolTip = App.Text("Preferences.Git.Invalid") });
            page.Add(Field(App.Text("Preferences.Git.Version"), version));
            page.Add(Field(App.Text("Preferences.Git.User"), TextValue(Read("user.name"), App.Text("Preferences.Git.User.Placeholder"))));
            page.Add(Field(App.Text("Preferences.Git.Email"), TextValue(Read("user.email"), App.Text("Preferences.Git.Email.Placeholder"))));
            var crlf = Models.CRLFMode.Supported;
            page.Add(Field(App.Text("Preferences.Git.CRLF"), Choice(App.Text("Preferences.Git.CRLF"),
                crlf.Select(m => $"{m.Name}  ({m.Desc})"), crlf.FindIndex(m => m.Value == Read("core.autocrlf")))));
            page.Add(Field("", Check(App.Text("Preferences.Git.EnablePruneOnFetch"), Read("fetch.prune") == "true", "fetch.prune=true")));
            page.Add(Field("", Check(App.Text("Preferences.Git.IgnoreCRAtEOLInDiff"), pref.IgnoreCRAtEOLInDiff, "--ignore-cr-at-eol")));
            page.Add(Field("", Check(App.Text("Preferences.Git.SSLVerify"), Read("http.sslverify") != "false", "http.sslverify=true")));
            if (OperatingSystem.IsLinux())
                page.Add(Field("", Check(App.Text("Preferences.Git.UseLibsecret"), pref.UseLibsecretInsteadOfGCM)));
            page.Add(Field("", Check(App.Text("Preferences.Git.UseStashAndReapplyByDefault"), pref.UseStashAndReapplyByDefault)));
            var fetch = new Row { Gap = 8, AlignItems = Align.Center };
            fetch.Add(Check(App.Text("Configure.Git.AutoFetch"), pref.EnableAutoFetch));
            var interval = new NumberInput(App.Text("Configure.Git.AutoFetch")) { IsEnabled = Editable && pref.EnableAutoFetch };
            interval.SetRange(1, 60);
            interval.Value = pref.AutoFetchInterval;
            fetch.Add(interval);
            fetch.Add(new Text(App.Text("Configure.Git.AutoFetchIntervalSuffix")));
            page.Add(Field("", fetch));
            return page;
        }

        private static Node Gpg(ViewModels.Preferences pref)
        {
            var page = Page();
            var config = pref.IsGitConfigured() ? new Commands.Config(null).ReadAll() : new();
            string Read(string key) => config.TryGetValue(key, out var value) ? value : "";
            var formats = Models.GPGFormat.Supported;
            var format = formats.Find(f => f.Value == Read("gpg.format")) ?? formats[0];
            page.Add(Field(App.Text("Preferences.GPG.Format"), Choice(App.Text("Preferences.GPG.Format"),
                formats.Select(f => $"{f.Name}  ({f.Desc})"), formats.IndexOf(format))));
            if (format.NeedFindProgram)
            {
                var program = format.Value == "openpgp" ? Read("gpg.program") : Read($"gpg.{format.Value}.program");
                page.Add(Field(App.Text("Preferences.GPG.Path"), Path(program, App.Text("Preferences.GPG.Path.Placeholder"))));
            }
            page.Add(Field(App.Text("Preferences.GPG.UserKey"), TextValue(Read("user.signingkey"), App.Text("Preferences.GPG.UserKey.Placeholder"))));
            page.Add(Field("", Check(App.Text("Preferences.GPG.CommitEnabled"), Read("commit.gpgsign") == "true", "commit.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.GPG.TagEnabled"), Read("tag.gpgsign") == "true", "tag.gpgsign=true")));
            return page;
        }

        private static Node Integration(ViewModels.Preferences pref)
        {
            var page = Page();
            page.Gap = 8;
            page.Add(Section("Terminal", App.Text("Preferences.Shell")));
            var shells = Models.ShellOrTerminal.Supported;
            page.Add(Field(App.Text("Preferences.Shell.Type"), Choice(App.Text("Preferences.Shell.Type"), shells.Select(s => s.Name), pref.ShellOrTerminalType)));
            // macOS opens its terminals by name: upstream asks no path there.
            if (!OperatingSystem.IsMacOS())
            {
                page.Add(Field(App.Text("Preferences.Shell.Path"), Path(pref.ShellOrTerminalPath)));
                page.Add(Field(App.Text("Preferences.Shell.Args"), WithTip(TextValue(pref.ShellOrTerminalArgs), App.Text("Preferences.Shell.Args.Tip"))));
            }
            var gap = new Row();
            gap.Height = 16;
            page.Add(gap);
            page.Add(Section("Diff", App.Text("Preferences.DiffMerge")));
            var mergers = Models.ExternalMerger.Supported;
            page.Add(Field(App.Text("Preferences.DiffMerge.Type"), Choice(App.Text("Preferences.DiffMerge.Type"), mergers.Select(m => m.Name), pref.ExternalMergeToolType)));
            if (pref.ExternalMergeToolType > 0)
            {
                page.Add(Field(App.Text("Preferences.DiffMerge.Path"), Path(pref.ExternalMergeToolPath, App.Text("Preferences.DiffMerge.Path.Placeholder"))));
                page.Add(Field(App.Text("Preferences.DiffMerge.DiffArgs"), WithTip(TextValue(pref.ExternalMergeToolDiffArgs), App.Text("Preferences.DiffMerge.DiffArgs.Tip"))));
                page.Add(Field(App.Text("Preferences.DiffMerge.MergeArgs"), WithTip(TextValue(pref.ExternalMergeToolMergeArgs), App.Text("Preferences.DiffMerge.MergeArgs.Tip"))));
            }
            return page;
        }

        /// <summary>Upstream's AI service form. The key shows as a password
        /// field's bullets, or as the variable it's read from.</summary>
        private static Node Service(AI.Service service)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Preferences.AI.Name"), TextValue(service.Name)));
            column.Add(Stacked(App.Text("Preferences.AI.Server"), TextValue(service.Server)));
            Node key = service.ReadApiKeyFromEnv
                ? TextValue(service.ApiKey)
                : new PasswordInput { Value = service.ApiKey ?? "", IsEnabled = Editable };
            var keyColumn = Stacked(App.Text("Preferences.AI.ApiKey"), key);
            keyColumn.Add(Check(App.Text("Preferences.AI.ReadApiKeyFromEnv"), service.ReadApiKeyFromEnv));
            column.Add(keyColumn);
            var model = TextValue(service.Model);
            var modelColumn = Stacked(App.Text("Preferences.AI.Model"), model);
            modelColumn.Add(Check(App.Text("Preferences.AI.Model.AutoFetchAvailableModels"), service.AutoFetchAvailableModels));
            column.Add(modelColumn);
            var levels = AI.Options.ReasoningEffortLevels;
            column.Add(Stacked(App.Text("Preferences.AI.ReasoningEffortLevel"),
                Choice(App.Text("Preferences.AI.ReasoningEffortLevel"), levels, levels.IndexOf(service.ReasoningEffortLevel))));
            column.Add(Stacked(App.Text("Preferences.AI.AdditionalPrompt"), TextBlock(service.AdditionalPrompt, 6)));
            column.Add(Stacked(App.Text("Preferences.AI.ExtraHeaders"), TextBlock(service.ExtraHeaders, 3)));
            return column;
        }
    }
}
