using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The app's menus, which carry upstream's keyboard shortcuts as the
    /// platforms' apps carry theirs: upstream's app menu (about, the
    /// shortcuts, updates, settings, the SSH key helper, the data folder),
    /// then File (tabs, cloning, opening), View (the pages, the search, the
    /// tabs, reloading) and Repository (syncing, branches, the history).
    /// Built again as the page shown and its state change, so what doesn't
    /// apply is off and its key does nothing. The same list makes
    /// <see cref="Hotkeys"/>. Titles are the platforms' short ones in
    /// English where upstream has only its reference's descriptions
    /// ("Create new tab"), which the reference shows, translated.
    /// </summary>
    public static class AppMenu
    {
        /// <summary>A command: its title, its key (null: none on this
        /// platform), what it does, and when it applies.</summary>
        public sealed record Command(string Title, Shortcut? Key, Action Run, Func<bool> Enabled = null, Func<bool> Checked = null)
        {
            /// <summary>What it does, for the shortcuts reference: upstream's
            /// words where its own title is a name.</summary>
            public string Description { get; init; }

            /// <summary>An item every app has, which macOS puts in its own
            /// place.</summary>
            public MenuRole Role { get; init; }
        }

        /// <summary>A menu's commands; null is a separator.</summary>
        public sealed record Section(string Title, List<Command> Commands);

        private static ViewModels.Launcher s_launcher;
        private static INotifyPropertyChanged[] s_watched = [];

        public static void Attach(ViewModels.Launcher launcher)
        {
            s_launcher = launcher;
            launcher.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(launcher.ActivePage))
                    Watch();
            };
            Watch();
        }

        /// <summary>Rebuilds the menus, and follows what they read: the page
        /// shown, its repository and history.</summary>
        private static void Watch()
        {
            foreach (var source in s_watched)
                source.PropertyChanged -= Changed;
            var repo = Repo();
            s_watched = repo == null ? [] : repo.Histories != null ? [repo, repo.Histories] : [repo];
            foreach (var source in s_watched)
                source.PropertyChanged += Changed;
            Set();
        }

        private static readonly HashSet<string> s_read =
        [
            nameof(ViewModels.Repository.SelectedViewIndex),
            nameof(ViewModels.Histories.IsSearchingCommits),
            nameof(ViewModels.Histories.SelectedCommits),
        ];

        private static void Changed(object sender, PropertyChangedEventArgs e)
        {
            if (s_read.Contains(e.PropertyName))
                Set();
        }

        private static void Set() =>
            MenuBar.Set(Sections().Select(s => new Submenu(s.Title, s.Commands.Select(Entry).ToList())).ToList());

        private static MenuEntry Entry(Command command) =>
            command == null
                ? MenuSeparator.Instance
                : new MenuItem(command.Title, command.Run)
                {
                    Shortcut = command.Key,
                    Enabled = command.Enabled?.Invoke() ?? true,
                    Checked = command.Checked?.Invoke(),
                    Role = command.Role,
                };

        private static ViewModels.Repository Repo() => s_launcher?.ActivePage?.Data as ViewModels.Repository;

        private static bool Mac => OperatingSystem.IsMacOS();

        private static Shortcut Primary(char c) => Shortcut.Of(c).WithPrimary();

        private static Shortcut Primary(Key key) => Shortcut.Of(key).WithPrimary();

        /// <summary>Every menu and command, in order: the first, titled with
        /// the app's name, is its app menu on macOS.</summary>
        public static List<Section> Sections()
        {
            bool Repository() => Repo() != null;
            bool Worktree() => Repo() is { IsBare: false };
            bool History() => Repo() is { SelectedViewIndex: 0, Histories: not null };
            bool OneCommit() => History() && Repo().Histories.SelectedCommits is { Count: 1 };

            var dataDir = OperatingSystem.IsLinux()
                ? new Command(App.Text("OpenAppDataDir.Cache"), null, () => Native.OS.OpenInFileManager(Native.OS.BasicDirectories.CacheDir))
                : new Command(App.Text("OpenAppDataDir"), null, () => Native.OS.OpenInFileManager(Native.OS.BasicDirectories.CacheDir));
            var app = new List<Command>
            {
                new(App.Text("About.Menu"), null, About.Show) { Role = MenuRole.About },
                new("Keyboard Shortcuts", Shortcut.Function(1), Hotkeys.Show) { Description = App.Text("Hotkeys") },
                new("Check for Updates…", null, Updates.Check),
                null,
                new(App.Text("Preferences"), Primary(','), Preferences.Show)
                {
                    Description = App.Text("Hotkeys.Global.OpenPreferences"),
                    Role = MenuRole.Settings,
                },
                new(App.Text("SSHKeyHelper"), null, SSHKeyHelper.Show),
                dataDir,
            };
            if (OperatingSystem.IsLinux())
                app.Add(new(App.Text("OpenAppDataDir.Config"), null, () => Native.OS.OpenInFileManager(Native.OS.BasicDirectories.ConfigDir)));

            var file = new List<Command>
            {
                new("New Tab", Primary('t'), () => s_launcher.AddNewTab()) { Description = App.Text("Hotkeys.Global.NewTab") },
                new("Close Tab", Primary('w'), () => s_launcher.CloseTab(null)) { Description = App.Text("Hotkeys.Global.CloseTab") },
                null,
                new("Clone Repository…", Primary('r'), () => OnWelcome(ViewModels.Welcome.Instance.Clone)) { Description = App.Text("Hotkeys.Global.Clone") },
                new("Open Repository…", Primary('l'), () => OnWelcome(ViewModels.Welcome.Instance.OpenLocalRepository)) { Description = App.Text("Hotkeys.Global.OpenLocalRepository") },
                null,
                // Upstream's ⌃`: macOS has no Control apart from ⌘ here, and
                // ⌘` cycles the app's windows.
                new(App.Text("Welcome.OpenTerminal"), Mac ? null : Primary('`'), () =>
                {
                    if (Repo() is { } repo)
                        Native.OS.OpenTerminal(repo.FullPath);
                    else
                        ViewModels.Welcome.Instance.OpenTerminal();
                }),
                new(App.Text("Repository.Explore"), Primary('e'), () => Native.OS.OpenInFileManager(Repo().FullPath), Repository),
            };

            var view = new List<Command>
            {
                Page(0, "Dashboard", '1', Repository),
                Page(1, "WorkingCopy", '2', Worktree),
                Page(2, "Stashes", '3', Worktree),
                null,
                new("Search Commits", Primary('f').WithShift(), () =>
                {
                    var histories = Repo().Histories;
                    histories.IsSearchingCommits = !histories.IsSearchingCommits;
                }, () => Repo()?.Histories != null, () => Repo()?.Histories?.IsSearchingCommits ?? false)
                {
                    Description = App.Text("Hotkeys.Repo.ToggleSearchCommits"),
                },
                null,
                new("Go to Tab…", Primary('p'), () => s_launcher.CommandPalette = new ViewModels.LauncherPagesCommandPalette(s_launcher))
                {
                    Description = App.Text("Hotkeys.Global.SwitchTab"),
                },
                // Upstream's: ⌘⌥← and ⌘⌥→ on macOS, Ctrl+Shift+Tab and
                // Ctrl+Tab elsewhere.
                new("Show Previous Tab", Mac ? Primary(Key.Left).WithAlt() : Primary(Key.Tab).WithShift(), () => s_launcher.GotoPrevTab())
                {
                    Description = App.Text("Hotkeys.Global.GotoPrevTab"),
                },
                new("Show Next Tab", Mac ? Primary(Key.Right).WithAlt() : Primary(Key.Tab), () => s_launcher.GotoNextTab())
                {
                    Description = App.Text("Hotkeys.Global.GotoNextTab"),
                },
                null,
                new("Reload", Shortcut.Function(5), () =>
                {
                    if (Repo() is { } repo)
                        repo.RefreshAll();
                    else if (s_launcher.ActivePage?.Data is ViewModels.Welcome welcome)
                        _ = welcome.UpdateStatusAsync(true, null);
                }) { Description = App.Text("Hotkeys.Repo.Refresh") },
            };

            var repository = new List<Command>
            {
                new("Commands…", Primary('p').WithShift(), () => s_launcher.CommandPalette = new ViewModels.RepositoryCommandPalette(Repo()), Repository)
                {
                    Description = App.Text("Hotkeys.Repo.OpenCommandPalette"),
                },
                null,
                new(App.Text("Fetch"), Primary(Key.Down), () => _ = Repo().FetchAsync(true), Repository) { Description = App.Text("Hotkeys.Repo.Fetch") },
                new(App.Text("Pull"), Primary(Key.Down).WithShift(), () => _ = Repo().PullAsync(true), Worktree) { Description = App.Text("Hotkeys.Repo.Pull") },
                new(App.Text("Push"), Primary(Key.Up).WithShift(), () => _ = Repo().PushAsync(true), Repository) { Description = App.Text("Hotkeys.Repo.Push") },
                null,
                new("New Branch…", Primary('b'), () => Repo().CreateNewBranch(), Repository) { Description = App.Text("Hotkeys.Repo.CreateBranch") },
                new("New Branch from Commit…", Primary('b').WithShift(), () =>
                {
                    if (Repo() is { } repo && repo.CanCreatePopup() && repo.GetSelectedCommitInHistory() is { } commit)
                        repo.ShowPopup(new ViewModels.CreateBranch(repo, commit));
                }, OneCommit),
                new("New Tag from Commit…", Primary('t').WithShift(), () =>
                {
                    if (Repo() is { } repo && repo.CanCreatePopup() && repo.GetSelectedCommitInHistory() is { } commit)
                        repo.ShowPopup(new ViewModels.CreateTag(repo, commit));
                }, OneCommit),
                null,
                new("Go to Parent", Shortcut.Of(Key.Down).WithAlt(), () => _ = GoToParent(Repo()), OneCommit) { Description = App.Text("Hotkeys.Repo.GoToParent") },
                new("Go to Child", Shortcut.Of(Key.Up).WithAlt(), () => _ = GoToChild(Repo()), OneCommit) { Description = App.Text("Hotkeys.Repo.GoToChild") },
            };

            return
            [
                new("SourceGit", app),
                // macOS puts the app's File menu, known by its title, before Edit.
                new("File", file),
                new("View", view),
                new(Popups.ButtonText("Hotkeys.Repo"), repository),
            ];

            static Command Page(int index, string key, char digit, Func<bool> enabled) =>
                new(App.Text(key), Primary(digit), () => Repo().SelectedViewIndex = index, enabled, () => Repo()?.SelectedViewIndex == index)
                {
                    Description = App.Text($"Hotkeys.Repo.View{(index == 0 ? "Dashboard" : index == 1 ? "Changes" : "Stashes")}"),
                };
        }

        /// <summary>Upstream opens cloning and opening on the welcome page,
        /// in a new tab if another page shows.</summary>
        private static void OnWelcome(Action open)
        {
            if (s_launcher.ActivePage?.Data is not ViewModels.Welcome)
                s_launcher.AddNewTab();
            open();
        }

        /// <summary>Upstream's <c>GotoParent</c>: the selected commit's
        /// parent, or the one picked of several.</summary>
        private static async Task GoToParent(ViewModels.Repository repo)
        {
            if (repo?.Histories is not { SelectedCommits: [{ Parents.Count: > 0 } commit] } histories)
                return;
            var parents = new List<Models.Commit>();
            foreach (var sha in commit.Parents)
            {
                if (await histories.GetCommitAsync(sha) is { } parent)
                    parents.Add(parent);
            }
            if (await Pick(parents, "Go to Parent") is { } picked)
                histories.NavigateTo(picked.SHA);
        }

        /// <summary>Upstream's <c>GotoChild</c>: the commits shown above it
        /// whose parent it is.</summary>
        private static async Task GoToChild(ViewModels.Repository repo)
        {
            if (repo?.Histories is not { SelectedCommits: [var commit] } histories)
                return;
            var children = new List<Models.Commit>();
            foreach (var c in histories.Commits)
            {
                if (c.Parents.Any(p => commit.SHA.StartsWith(p, StringComparison.Ordinal)))
                    children.Add(c);
                if (commit.SHA.Equals(c.SHA, StringComparison.Ordinal))
                    break;
            }
            if (await Pick(children, "Go to Child") is { } picked)
                histories.NavigateTo(picked.SHA);
        }

        /// <summary>Upstream's <c>GotoRevisionSelector</c>: the one commit,
        /// or a sheet to pick one of several (Return or a double-click).</summary>
        private static Task<Models.Commit> Pick(List<Models.Commit> commits, string title)
        {
            if (commits.Count <= 1)
                return Task.FromResult(commits.FirstOrDefault());
            var done = new TaskCompletionSource<Models.Commit>();
            var sheet = new Window(App.MainWindow, title, 520);
            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            var list = new ListView<Models.Commit>(c => c.SHA, c =>
            {
                var row = new Row { Gap = 8, AlignItems = Align.Center, Padding = new Thickness(0, 4) };
                row.Height = 28;
                row.Add(new Text(c.SHA[..10]) { Style = TextStyle.Monospace, Color = Color.SecondaryLabel });
                var subject = new Text(c.Subject) { MaxLines = 1 };
                subject.Shrink = 1;
                subject.MinWidth = 0;
                row.Add(subject);
                return row;
            }, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 28,
                Items = commits,
            };
            list.Height = Math.Min(commits.Count, 8) * 28 + 12;
            list.SelectedItems = [commits[0]];
            column.Add(list);
            var buttons = new Row { Justify = Justify.End, Gap = 8 };
            var cancel = new Button(Popups.ButtonText("Cancel"));
            var ok = new Button(Popups.ButtonText("Sure")) { Role = ButtonRole.Default };
            buttons.Add(cancel);
            buttons.Add(ok);
            column.Add(buttons);
            sheet.Content = column;
            void Close(Models.Commit picked)
            {
                if (done.TrySetResult(picked))
                    sheet.Destroy();
            }
            ok.Click += () => Close(list.SelectedItem);
            list.Activated += Close;
            cancel.Click += () => Close(null);
            sheet.Closed += () => done.TrySetResult(null);
            return done.Task;
        }
    }
}
