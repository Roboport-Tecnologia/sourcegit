using System.Collections.Generic;
using System.IO;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's toolbar menus (<c>RepositoryToolbar.axaml.cs</c>): opening
    /// the repository elsewhere, Git-Flow, Git LFS and the repository's
    /// custom actions, built as they open, as upstream's are: what they
    /// offer depends on the tools, branches, remotes and git's hooks then.
    /// </summary>
    public static class ToolbarMenus
    {
        /// <summary>Upstream's <c>OpenWithExternalTools</c>: the file
        /// manager and the terminal, the external tools found (a submenu of
        /// a tool's own choices, e.g. a workspace, when it has several), and
        /// each remote's web page.</summary>
        public static IReadOnlyList<MenuEntry> OpenWith(ViewModels.Repository repo)
        {
            var path = repo.FullPath;
            if (!Directory.Exists(path))
                return [];
            var menu = new List<MenuEntry>
            {
                new MenuItem(App.Text("Repository.Explore"), () => Native.OS.OpenInFileManager(path)),
                new MenuItem(App.Text("Repository.Terminal"), () => Native.OS.OpenTerminal(path)),
            };

            var tools = Native.OS.ExternalTools;
            if (tools.Count > 0)
            {
                menu.Add(MenuSeparator.Instance);
                foreach (var tool in tools)
                {
                    var options = tool.MakeLaunchOptions(path);
                    var count = (tool.SupportOpenFolder ? 1 : 0) + (options?.Count ?? 0);
                    var title = App.Text("Repository.OpenIn", tool.Name);
                    if (count == 0)
                        continue;
                    if (count == 1)
                    {
                        var args = options is { Count: 1 } ? options[0].Args : path.Quoted();
                        menu.Add(new MenuItem(title, () => tool.Launch(args)));
                        continue;
                    }
                    var each = new List<MenuEntry>();
                    foreach (var option in options)
                        each.Add(new MenuItem(option.Title, () => tool.Launch(option.Args)));
                    if (tool.SupportOpenFolder)
                    {
                        each.Add(MenuSeparator.Instance);
                        each.Add(new MenuItem(App.Text("Repository.OpenAsFolder"), () => tool.Launch(path.Quoted())));
                    }
                    menu.Add(new Submenu(title, each));
                }
            }

            var visits = new List<MenuEntry>();
            foreach (var remote in repo.Remotes)
            {
                if (remote.TryGetVisitURL(out var url))
                    visits.Add(new MenuItem(App.Text("Repository.Visit", remote.Name), () => Native.OS.OpenBrowser(url)));
            }
            if (visits.Count > 0)
            {
                menu.Add(MenuSeparator.Instance);
                menu.AddRange(visits);
            }
            return menu;
        }

        /// <summary>Upstream's <c>OpenGitFlowMenu</c>: starting a feature,
        /// release or hotfix, and finishing the current branch if it's one;
        /// else initializing Git-Flow.</summary>
        public static IReadOnlyList<MenuEntry> GitFlow(ViewModels.Repository repo)
        {
            if (!repo.IsGitFlowEnabled())
            {
                return
                [
                    new MenuItem(App.Text("GitFlow.Init"), () =>
                    {
                        if (repo.CurrentBranch == null)
                            repo.SendNotification("Git flow init failed: No branch found!!!", true);
                        else if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.InitGitFlow(repo));
                    }),
                ];
            }

            var menu = new List<MenuEntry>
            {
                Start(repo, "GitFlow.StartFeature", Models.GitFlowBranchType.Feature),
                Start(repo, "GitFlow.StartRelease", Models.GitFlowBranchType.Release),
                Start(repo, "GitFlow.StartHotfix", Models.GitFlowBranchType.Hotfix),
            };
            var current = repo.CurrentBranch;
            var type = current != null ? repo.GetGitFlowType(current) : Models.GitFlowBranchType.None;
            if (type != Models.GitFlowBranchType.None)
            {
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("GitFlow.Finish", current.Name), () =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.GitFlowFinish(repo, current, type));
                }));
            }
            return menu;
        }

        private static MenuItem Start(ViewModels.Repository repo, string title, Models.GitFlowBranchType type) =>
            new(App.Text(title), () =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.GitFlowStart(repo, type));
            });

        /// <summary>Upstream's <c>OpenGitLFSMenu</c>: tracking a pattern;
        /// fetching, pulling and pushing objects (at once with one remote);
        /// pruning; the locks. Without LFS's hooks, installing them.</summary>
        public static IReadOnlyList<MenuEntry> LFS(ViewModels.Repository repo)
        {
            if (!repo.IsLFSEnabled())
                return [new MenuItem(App.Text("GitLFS.Install"), async () => await repo.InstallLFSAsync())];

            var remotes = repo.Remotes.Count > 0;
            var menu = new List<MenuEntry>
            {
                new MenuItem(App.Text("GitLFS.AddTrackPattern"), () =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.LFSTrackCustomPattern(repo));
                }),
                MenuSeparator.Instance,
                Sync(repo, "GitLFS.Fetch", remotes, () => new ViewModels.LFSFetch(repo)),
                Sync(repo, "GitLFS.Pull", remotes, () => new ViewModels.LFSPull(repo)),
                Sync(repo, "GitLFS.Push", remotes, () => new ViewModels.LFSPush(repo)),
                MenuSeparator.Instance,
                new MenuItem(App.Text("GitLFS.Prune"), async () =>
                {
                    if (repo.CanCreatePopup())
                        await repo.ShowAndStartPopupAsync(new ViewModels.LFSPrune(repo));
                }),
                MenuSeparator.Instance,
            };
            // Upstream's locks window, for the one remote or each of them.
            var locks = App.Text("GitLFS.Locks");
            if (repo.Remotes.Count == 1)
            {
                menu.Add(CommitMenu.NotPorted(locks, "The LFS locks window"));
            }
            else if (remotes)
            {
                var each = new List<MenuEntry>();
                foreach (var remote in repo.Remotes)
                    each.Add(CommitMenu.NotPorted(remote.Name, "The LFS locks window"));
                menu.Add(new Submenu(locks, each));
            }
            else
            {
                menu.Add(new MenuItem(locks, () => { }) { Enabled = false });
            }
            return menu;
        }

        // Started at once with one remote, else its popup picks one.
        private static MenuItem Sync(ViewModels.Repository repo, string title, bool remotes, System.Func<ViewModels.Popup> popup) =>
            new(App.Text(title), async () =>
            {
                if (!repo.CanCreatePopup())
                    return;
                if (repo.Remotes.Count == 1)
                    await repo.ShowAndStartPopupAsync(popup());
                else
                    repo.ShowPopup(popup());
            })
            { Enabled = remotes };

        /// <summary>Upstream's <c>OpenCustomActionMenu</c>: the actions for
        /// the repository, the global ones marked as the history's are; a
        /// disabled line when there are none.</summary>
        public static IReadOnlyList<MenuEntry> CustomActions(ViewModels.Repository repo)
        {
            var actions = repo.GetCustomActions(Models.CustomActionScope.Repository);
            if (actions.Count == 0)
                return [new MenuItem(App.Text("Repository.CustomActions.Empty"), () => { }) { Enabled = false }];
            var menu = new List<MenuEntry>();
            foreach (var (action, label) in actions)
            {
                var name = label.IsGlobal ? $"{label.Name} (GLOBAL)" : label.Name;
                menu.Add(new MenuItem(name, async () => await repo.ExecCustomActionAsync(action, null)));
            }
            return menu;
        }
    }
}
