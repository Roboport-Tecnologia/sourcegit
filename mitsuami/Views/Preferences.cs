using System;
using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

using static SourceGit.Views.Settings;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's preferences (<c>Views/Preferences.axaml</c>): a window of
    /// its own, opened from the app menu's Settings item (⌘, on macOS), with
    /// upstream's tabs. Each field sets its preference as it changes; git's
    /// global settings are read on opening and written on closing, the
    /// changed ones only, and the preferences saved, as upstream does.
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
            var git = new GitConfig(pref.IsGitConfigured() ? new Commands.Config(null).ReadAll() : new());
            window.Closed += () =>
            {
                s_shown = null;
                Save(git);
            };
            var tabs = Tabs(
                ("Preferences.General", General(pref)),
                ("Preferences.Appearance", Appearance(pref)),
                ("Preferences.Git", Git(pref, git)),
                ("Preferences.GPG", Gpg(git)),
                ("Preferences.Integration", Integration(pref)),
                ("Configure.CustomAction", ListDetail(pref.CustomActions, a => a.Name, nameof(Models.CustomAction.Name), "Action",
                    a => CustomAction(() => s_shown, a), new ListActions<Models.CustomAction>
                    {
                        Add = () =>
                        {
                            var action = new Models.CustomAction() { Name = "Unnamed Action (Global)" };
                            pref.CustomActions.Add(action);
                            return action;
                        },
                        Remove = a => pref.CustomActions.Remove(a),
                        Move = pref.CustomActions.Move,
                    })),
                ("Preferences.AI", ListDetail(pref.OpenAIServices, s => s.Name, nameof(AI.Service.Name), "AIAssist", Service,
                    new ListActions<AI.Service>
                    {
                        Add = () =>
                        {
                            var service = new AI.Service() { Name = "Unnamed Service" };
                            pref.OpenAIServices.Add(service);
                            return service;
                        },
                        Remove = s => pref.OpenAIServices.Remove(s),
                    })));
            var column = new Column { Padding = 16, AlignItems = Align.Stretch };
            column.Add(tabs);
            window.Content = column;
        }

        /// <summary>Git's global settings as the window shows them: read on
        /// opening, as upstream's window reads them.</summary>
        private sealed class GitConfig(Dictionary<string, string> read)
        {
            public Dictionary<string, string> Read { get; } = read;
            public string User = Get(read, "user.name");
            public string Email = Get(read, "user.email");
            public string SigningKey = Get(read, "user.signingkey");
            public string CRLF = read.GetValueOrDefault("core.autocrlf");
            public bool PruneOnFetch = Get(read, "fetch.prune") == "true";
            public bool CommitSigning = Get(read, "commit.gpgsign") == "true";
            public bool TagSigning = Get(read, "tag.gpgsign") == "true";
            public bool SSLVerify = Get(read, "http.sslverify") != "false";
            public Models.GPGFormat Format = Models.GPGFormat.Supported.Find(f => f.Value == Get(read, "gpg.format")) ?? Models.GPGFormat.Supported[0];
            public string Program;

            public string ProgramOf(Models.GPGFormat format) =>
                format.Value == "openpgp" ? Get(Read, "gpg.program") : Get(Read, $"gpg.{format.Value}.program");

            public static string Get(Dictionary<string, string> read, string key) =>
                read.TryGetValue(key, out var value) ? value : "";
        }

        /// <summary>Upstream's closing: git's global settings that changed,
        /// then the preferences.</summary>
        private static async void Save(GitConfig git)
        {
            var pref = ViewModels.Preferences.Instance;
            if (pref.IsGitConfigured())
            {
                var config = await new Commands.Config(null).ReadAllAsync();
                await SetIfChanged(config, "user.name", git.User, "");
                await SetIfChanged(config, "user.email", git.Email, "");
                await SetIfChanged(config, "user.signingkey", git.SigningKey, "");
                await SetIfChanged(config, "core.autocrlf", git.CRLF, null);
                await SetIfChanged(config, "fetch.prune", git.PruneOnFetch ? "true" : "false", "false");
                await SetIfChanged(config, "commit.gpgsign", git.CommitSigning ? "true" : "false", "false");
                await SetIfChanged(config, "tag.gpgsign", git.TagSigning ? "true" : "false", "false");
                await SetIfChanged(config, "http.sslverify", git.SSLVerify ? "" : "false", "");
                await SetIfChanged(config, "gpg.format", git.Format.Value, "openpgp");
                if (!git.Format.Value.Equals("ssh", StringComparison.Ordinal) && git.Program != null)
                {
                    var old = git.Format.Value == "openpgp" ? GitConfig.Get(config, "gpg.program") : GitConfig.Get(config, $"gpg.{git.Format.Value}.program");
                    if (old != git.Program)
                        await new Commands.Config(null).SetAsync($"gpg.{git.Format.Value}.program", git.Program);
                }
            }
            pref.UpdateAvailableAIModels();
            pref.Save();
        }

        // Upstream's.
        private static async System.Threading.Tasks.Task SetIfChanged(Dictionary<string, string> cached, string key, string value, string defValue)
        {
            bool changed = false;
            if (cached.TryGetValue(key, out var old))
                changed = old != value;
            else if (!string.IsNullOrEmpty(value) && value != defValue)
                changed = true;

            if (changed)
                await new Commands.Config(null).SetAsync(key, value);
        }

        private static Window Over() => s_shown;

        private static Node General(ViewModels.Preferences pref)
        {
            var page = Page();
            var locales = Models.Locale.Supported;
            page.Add(Field(App.Text("Preferences.General.Locale"),
                Choice(App.Text("Preferences.General.Locale"), locales.Select(l => l.Name),
                    locales.FindIndex(l => l.Key == pref.Locale), i => pref.Locale = locales[i].Key)));
            var date = new Row { Gap = 8, AlignItems = Align.Center };
            var format = Choice(App.Text("Preferences.General.DateFormat"),
                Models.DateTimeFormat.Supported.Select(f => $"{f.Example}  ({f.DateFormat})"), pref.DateTimeFormat, i => pref.DateTimeFormat = i);
            format.Grow = 1;
            date.Add(format);
            date.Add(Check(App.Text("Preferences.General.Use24Hours"), pref.Use24Hours, v => pref.Use24Hours = v));
            page.Add(Field(App.Text("Preferences.General.DateFormat"), date));
            page.Add(Field(App.Text("Preferences.Git.DefaultCloneDir"),
                Path(Over, pref.GitDefaultCloneDir, v => pref.GitDefaultCloneDir = v, pick: Pick.Folder)));
            // Upstream's subject guide doesn't snap to its ticks; the
            // commit count snaps to thousands of five.
            page.Add(Field(App.Text("Preferences.General.SubjectGuideLength"),
                Slider(50, 200, 0, pref.SubjectGuideLength, v => pref.SubjectGuideLength = (int)v)));
            page.Add(Field(App.Text("Preferences.General.MaxHistoryCommits"),
                Slider(5000, 100000, 5000, pref.MaxHistoryCommits, v => pref.MaxHistoryCommits = (int)v)));
            foreach (var (key, value, set) in new (string, bool, Action<bool>)[]
            {
                ("Preferences.General.ShowChangesPageByDefault", pref.ShowLocalChangesByDefault, v => pref.ShowLocalChangesByDefault = v),
                ("Preferences.General.ShowChangesTabInCommitDetailByDefault", pref.ShowChangesInCommitDetailByDefault, v => pref.ShowChangesInCommitDetailByDefault = v),
                ("Preferences.General.ShowTagsInGraph", pref.ShowTagsInGraph, v => pref.ShowTagsInGraph = v),
                ("Preferences.General.ShowRelativeTimeInGraph", pref.DisplayTimeAsPeriodInHistories, v => pref.DisplayTimeAsPeriodInHistories = v),
                ("Preferences.General.UseCompactBranchNames", pref.UseCompactBranchNamesInGraph, v => pref.UseCompactBranchNamesInGraph = v),
                ("Preferences.General.EnableCompactFolders", pref.EnableCompactFoldersInChangesTree, v => pref.EnableCompactFoldersInChangesTree = v),
                ("Preferences.General.UseGitHubStyleAvatar", pref.UseGitHubStyleAvatar, v => pref.UseGitHubStyleAvatar = v),
                ("Preferences.General.Check4UpdatesOnStartup", pref.Check4UpdatesOnStartup, v => pref.Check4UpdatesOnStartup = v),
            })
                page.Add(Field("", Check(App.Text(key), value, set)));
            return page;
        }

        /// <summary>Upstream's sliders show their value beside them.</summary>
        private static Row Slider(double min, double max, double step, double value, Action<double> set)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            var slider = new Mitsuamia.Slider();
            slider.SetRange(min, max, step);
            slider.Value = value;
            slider.Grow = 1;
            row.Add(slider);
            var shown = new Row { Justify = Justify.End };
            shown.Width = 56;
            shown.Shrink = 0;
            var text = new Text(value.ToString());
            shown.Add(text);
            row.Add(shown);
            slider.Changed += v =>
            {
                v = Math.Round(v);
                text.Value = v.ToString();
                set(v);
            };
            return row;
        }

        private static Node Appearance(ViewModels.Preferences pref)
        {
            var page = Page();
            string[] themes = ["Default", "Dark", "Light"];
            page.Add(Field(App.Text("Preferences.Appearance.Theme"),
                Choice(App.Text("Preferences.Appearance.Theme"), themes, Array.IndexOf(themes, pref.Theme), i => pref.Theme = themes[i])));
            page.Add(Field(App.Text("Preferences.Appearance.DefaultFont"), TextValue(pref.DefaultFontFamily, v => pref.DefaultFontFamily = v)));
            page.Add(Field(App.Text("Preferences.Appearance.MonospaceFont"), TextValue(pref.MonospaceFontFamily, v => pref.MonospaceFontFamily = v)));
            // Upstream's sizes go in halves, which a platform's number field
            // doesn't: a drop-down of them, as font size pickers have.
            var sizes = Enumerable.Range(0, 17).Select(i => 10 + i * 0.5).ToList();
            var fontSizes = new Row { Gap = 8, AlignItems = Align.Center };
            foreach (var (key, size, set) in new (string, double, Action<double>)[]
            {
                ("Preferences.Appearance.FontSize.Default", pref.DefaultFontSize, v => pref.DefaultFontSize = v),
                ("Preferences.Appearance.FontSize.Editor", pref.EditorFontSize, v => pref.EditorFontSize = v),
            })
            {
                fontSizes.Add(new Text(App.Text(key)) { Color = Color.SecondaryLabel });
                var select = Choice(App.Text(key), sizes.Select(s => s.ToString()), sizes.IndexOf(size), i => set(sizes[i]));
                select.Grow = 1;
                fontSizes.Add(select);
            }
            page.Add(Field(App.Text("Preferences.Appearance.FontSize"), fontSizes));
            var tabWidth = new NumberInput(App.Text("Preferences.Appearance.EditorTabWidth"));
            tabWidth.SetRange(1, 16);
            tabWidth.Value = pref.EditorTabWidth;
            tabWidth.Changed += v => pref.EditorTabWidth = v;
            page.Add(Field(App.Text("Preferences.Appearance.EditorTabWidth"), Leading(tabWidth)));
            var overrides = Path(Over, pref.ThemeOverrides, v => pref.ThemeOverrides = v, title: "Theme Overrides File");
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
            page.Add(Field("", Check(App.Text("Preferences.Appearance.UseFixedTabWidth"), pref.UseFixedTabWidth, v => pref.UseFixedTabWidth = v)));
            page.Add(Field("", Check(App.Text("Preferences.Appearance.UseAutoHideScrollBars"), pref.UseAutoHideScrollBars, v => pref.UseAutoHideScrollBars = v)));
            if (OperatingSystem.IsLinux())
                page.Add(Field("", Check(App.Text("Preferences.Appearance.UseNativeWindowFrame"), pref.UseSystemWindowFrame, v => pref.UseSystemWindowFrame = v)));
            return page;
        }

        /// <summary>A control at its own size, at the field's start.</summary>
        private static Row Leading(Node control)
        {
            var row = new Row { AlignItems = Align.Center };
            row.Add(control);
            return row;
        }

        private static Node Git(ViewModels.Preferences pref, GitConfig git)
        {
            var page = Page();
            var version = new Row { Gap = 8, AlignItems = Align.Center };
            void ShowVersion()
            {
                Empty(version);
                version.Add(new Text(Native.OS.GitVersionString ?? "") { Selectable = true });
                if (!string.IsNullOrEmpty(Native.OS.GitVersionString) && Native.OS.GitVersion < Models.GitVersions.MINIMAL)
                    version.Add(new Icon(Icons.Get("Error")) { Size = 14, Color = Color.Error, ToolTip = App.Text("Preferences.Git.Invalid") });
            }
            ShowVersion();
            // Setting the path reads the version of the git there.
            page.Add(Field(App.Text("Preferences.Git.Path"), Path(Over, pref.GitInstallPath, v =>
            {
                pref.GitInstallPath = v;
                ShowVersion();
            }, title: "Git Executable")));
            page.Add(Field(App.Text("Preferences.Git.Version"), version));
            page.Add(Field(App.Text("Preferences.Git.User"), TextValue(git.User, v => git.User = v, App.Text("Preferences.Git.User.Placeholder"))));
            page.Add(Field(App.Text("Preferences.Git.Email"), TextValue(git.Email, v => git.Email = v, App.Text("Preferences.Git.Email.Placeholder"))));
            var crlf = Models.CRLFMode.Supported;
            page.Add(Field(App.Text("Preferences.Git.CRLF"), Choice(App.Text("Preferences.Git.CRLF"),
                crlf.Select(m => $"{m.Name}  ({m.Desc})"), crlf.FindIndex(m => m.Value == git.CRLF), i => git.CRLF = crlf[i].Value)));
            page.Add(Field("", Check(App.Text("Preferences.Git.EnablePruneOnFetch"), git.PruneOnFetch, v => git.PruneOnFetch = v, "fetch.prune=true")));
            page.Add(Field("", Check(App.Text("Preferences.Git.IgnoreCRAtEOLInDiff"), pref.IgnoreCRAtEOLInDiff, v => pref.IgnoreCRAtEOLInDiff = v, "--ignore-cr-at-eol")));
            page.Add(Field("", Check(App.Text("Preferences.Git.SSLVerify"), git.SSLVerify, v => git.SSLVerify = v, "http.sslverify=true")));
            if (OperatingSystem.IsLinux())
                page.Add(Field("", Check(App.Text("Preferences.Git.UseLibsecret"), pref.UseLibsecretInsteadOfGCM, v => pref.UseLibsecretInsteadOfGCM = v)));
            page.Add(Field("", Check(App.Text("Preferences.Git.UseStashAndReapplyByDefault"), pref.UseStashAndReapplyByDefault, v => pref.UseStashAndReapplyByDefault = v)));
            var fetch = new Row { Gap = 8, AlignItems = Align.Center };
            var interval = new NumberInput(App.Text("Configure.Git.AutoFetch")) { IsEnabled = pref.EnableAutoFetch };
            fetch.Add(Check(App.Text("Configure.Git.AutoFetch"), pref.EnableAutoFetch, v =>
            {
                pref.EnableAutoFetch = v;
                interval.IsEnabled = v;
            }));
            interval.SetRange(1, 60);
            interval.Value = pref.AutoFetchInterval;
            interval.Changed += v => pref.AutoFetchInterval = v;
            fetch.Add(interval);
            fetch.Add(new Text(App.Text("Configure.Git.AutoFetchIntervalSuffix")));
            page.Add(Field("", fetch));
            return page;
        }

        /// <summary>The format, then its program where it needs one (shown
        /// again when the format changes, read from git's settings as
        /// upstream reads it), the key and what's signed.</summary>
        private static Node Gpg(GitConfig git)
        {
            var page = Page();
            var formats = Models.GPGFormat.Supported;
            var programField = new Column { AlignItems = Align.Stretch };
            void ShowProgram()
            {
                Empty(programField);
                git.Program = git.ProgramOf(git.Format);
                if (git.Format.NeedFindProgram)
                    programField.Add(Field(App.Text("Preferences.GPG.Path"), Path(Over, git.Program, v => git.Program = v,
                        App.Text("Preferences.GPG.Path.Placeholder"), title: "GPG Program")));
            }
            page.Add(Field(App.Text("Preferences.GPG.Format"), Choice(App.Text("Preferences.GPG.Format"),
                formats.Select(f => $"{f.Name}  ({f.Desc})"), formats.IndexOf(git.Format), i =>
                {
                    git.Format = formats[i];
                    ShowProgram();
                })));
            ShowProgram();
            page.Add(programField);
            page.Add(Field(App.Text("Preferences.GPG.UserKey"), TextValue(git.SigningKey, v => git.SigningKey = v, App.Text("Preferences.GPG.UserKey.Placeholder"))));
            page.Add(Field("", Check(App.Text("Preferences.GPG.CommitEnabled"), git.CommitSigning, v => git.CommitSigning = v, "commit.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.GPG.TagEnabled"), git.TagSigning, v => git.TagSigning = v, "tag.gpgsign=true")));
            return page;
        }

        private static Node Integration(ViewModels.Preferences pref)
        {
            var page = Page();
            page.Gap = 8;
            page.Add(Section("Terminal", App.Text("Preferences.Shell")));
            var shells = Models.ShellOrTerminal.Supported;
            var shellFields = new Column { Gap = 8, AlignItems = Align.Stretch };
            // macOS opens its terminals by name: upstream asks no path there.
            void ShowShell()
            {
                Empty(shellFields);
                if (OperatingSystem.IsMacOS())
                    return;
                shellFields.Add(Field(App.Text("Preferences.Shell.Path"), Path(Over, pref.ShellOrTerminalPath, v => pref.ShellOrTerminalPath = v)));
                shellFields.Add(Field(App.Text("Preferences.Shell.Args"),
                    WithTip(TextValue(pref.ShellOrTerminalArgs, v => pref.ShellOrTerminalArgs = v), App.Text("Preferences.Shell.Args.Tip"))));
            }
            // Choosing one fills in its path and arguments, as upstream's does.
            page.Add(Field(App.Text("Preferences.Shell.Type"), Choice(App.Text("Preferences.Shell.Type"), shells.Select(s => s.Name),
                pref.ShellOrTerminalType, i =>
                {
                    pref.ShellOrTerminalType = i;
                    ShowShell();
                })));
            ShowShell();
            page.Add(shellFields);
            var gap = new Row();
            gap.Height = 16;
            page.Add(gap);
            page.Add(Section("Diff", App.Text("Preferences.DiffMerge")));
            var mergers = Models.ExternalMerger.Supported;
            var mergerFields = new Column { Gap = 8, AlignItems = Align.Stretch };
            void ShowMerger()
            {
                Empty(mergerFields);
                if (pref.ExternalMergeToolType <= 0)
                    return;
                mergerFields.Add(Field(App.Text("Preferences.DiffMerge.Path"), Path(Over, pref.ExternalMergeToolPath,
                    v => pref.ExternalMergeToolPath = v, App.Text("Preferences.DiffMerge.Path.Placeholder"))));
                mergerFields.Add(Field(App.Text("Preferences.DiffMerge.DiffArgs"),
                    WithTip(TextValue(pref.ExternalMergeToolDiffArgs, v => pref.ExternalMergeToolDiffArgs = v), App.Text("Preferences.DiffMerge.DiffArgs.Tip"))));
                mergerFields.Add(Field(App.Text("Preferences.DiffMerge.MergeArgs"),
                    WithTip(TextValue(pref.ExternalMergeToolMergeArgs, v => pref.ExternalMergeToolMergeArgs = v), App.Text("Preferences.DiffMerge.MergeArgs.Tip"))));
            }
            page.Add(Field(App.Text("Preferences.DiffMerge.Type"), Choice(App.Text("Preferences.DiffMerge.Type"), mergers.Select(m => m.Name),
                pref.ExternalMergeToolType, i =>
                {
                    pref.ExternalMergeToolType = i;
                    ShowMerger();
                })));
            ShowMerger();
            page.Add(mergerFields);
            return page;
        }

        /// <summary>Upstream's AI service form. The key is a password field,
        /// or the variable it's read from.</summary>
        private static Node Service(AI.Service service)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Preferences.AI.Name"), TextValue(service.Name, v => service.Name = v)));
            column.Add(Stacked(App.Text("Preferences.AI.Server"), TextValue(service.Server, v => service.Server = v)));
            var keyColumn = new Column { Gap = 4, AlignItems = Align.Stretch };
            keyColumn.Add(new Text(App.Text("Preferences.AI.ApiKey").Trim()));
            var keyField = new Column { AlignItems = Align.Stretch };
            keyColumn.Add(keyField);
            void ShowKey()
            {
                Empty(keyField);
                if (service.ReadApiKeyFromEnv)
                {
                    keyField.Add(TextValue(service.ApiKey, v => service.ApiKey = v));
                }
                else
                {
                    var password = new PasswordInput { Value = service.ApiKey ?? "" };
                    password.Changed += v => service.ApiKey = v;
                    keyField.Add(password);
                }
            }
            ShowKey();
            keyColumn.Add(Check(App.Text("Preferences.AI.ReadApiKeyFromEnv"), service.ReadApiKeyFromEnv, v =>
            {
                service.ReadApiKeyFromEnv = v;
                ShowKey();
            }));
            column.Add(keyColumn);
            var modelColumn = Stacked(App.Text("Preferences.AI.Model"), TextValue(service.Model, v => service.Model = v));
            modelColumn.Add(Check(App.Text("Preferences.AI.Model.AutoFetchAvailableModels"), service.AutoFetchAvailableModels, v => service.AutoFetchAvailableModels = v));
            column.Add(modelColumn);
            var levels = AI.Options.ReasoningEffortLevels;
            column.Add(Stacked(App.Text("Preferences.AI.ReasoningEffortLevel"),
                Choice(App.Text("Preferences.AI.ReasoningEffortLevel"), levels, levels.IndexOf(service.ReasoningEffortLevel), i => service.ReasoningEffortLevel = levels[i])));
            column.Add(Stacked(App.Text("Preferences.AI.AdditionalPrompt"), TextBlock(service.AdditionalPrompt, 6, v => service.AdditionalPrompt = v)));
            column.Add(Stacked(App.Text("Preferences.AI.ExtraHeaders"), TextBlock(service.ExtraHeaders, 3, v => service.ExtraHeaders = v)));
            return column;
        }
    }
}
