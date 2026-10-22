using System;
using System.Collections.Generic;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// A repository's page, upstream's <c>Views/Repository.axaml</c> and
    /// <c>RepositoryToolbar.axaml</c>: the toolbar band, then the rail that
    /// picks the page (history, local changes, stashes), the sidebar of
    /// branches, remotes, tags, submodules and worktrees, and the page,
    /// split by the platform's divider.
    /// </summary>
    public static class Repository
    {
        public static Node Create(ViewModels.Repository vm)
        {
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;
            page.Add(Toolbar(vm));

            var body = new Row { AlignItems = Align.Stretch };
            body.Grow = 1;
            body.Add(Rail(vm));

            // Upstream's sidebar width is a preference, 250 points until
            // the user drags it; it's at least 200, and the page 100.
            var layout = ViewModels.Preferences.Instance.Layout;
            var split = new Split { Position = layout.RepositorySidebarWidth };
            split.Grow = 1;
            split.Changed += width => layout.RepositorySidebarWidth = width;
            var sidebar = Sidebar(vm);
            sidebar.MinWidth = 200;
            split.Add(sidebar);
            var right = Page(vm);
            right.MinWidth = 100;
            split.Add(right);
            body.Add(split);
            page.Add(body);
            return page;
        }

        /// <summary>
        /// Upstream's toolbar band, 36 points high and the window's header,
        /// as the welcome page's: the external tools, logs and statistics on
        /// the left; fetch, pull, push, stash and patch, then a new branch,
        /// Git-Flow, LFS, bisect, custom actions and cleanup in the middle;
        /// the current branch and a way back to it on the right.
        /// </summary>
        private static Node Toolbar(ViewModels.Repository vm)
        {
            var band = new Row { Justify = Justify.SpaceBetween, AlignItems = Align.Center, Padding = new Thickness(0, 4), IsHeader = true };
            band.Height = 36;

            var start = new Row { AlignItems = Align.Center };
            start.Grow = 1;
            Add(start, "OpenWith", 14, "Repository.OpenWithExternalTools", null);
            Add(start, "Logs", 14, "Repository.ViewLogs", null);
            Add(start, "Statistics", 13, "Repository.Statistics", null);
            band.Add(start);

            var middle = new Row { AlignItems = Align.Center, Gap = 16 };
            var sync = new Row { AlignItems = Align.Center, Gap = 16 };
            Add(sync, "Fetch", 14, "Fetch", () => _ = vm.FetchAsync(false));
            if (!vm.IsBare)
                Add(sync, "Pull", 14, "Pull", () => _ = vm.PullAsync(false));
            Add(sync, "Push", 14, "Push", () => _ = vm.PushAsync(false));
            if (!vm.IsBare)
            {
                Add(sync, "Stashes.Add", 14, "Stash", () => _ = vm.StashAllAsync(false));
                Add(sync, "ApplyPatch", 14, "Apply.Title", vm.ApplyPatch);
            }
            middle.Add(sync);
            var tools = new Row { AlignItems = Align.Center, Gap = 12 };
            Add(tools, "Branch.Add", 14, "CreateBranch", vm.CreateNewBranch);
            if (!vm.IsBare)
            {
                // Menus, which the C ABI can't show from a button yet.
                Add(tools, "GitFlow", 14, "GitFlow", null);
                Add(tools, "LFS", 14, "GitLFS", null);
                Add(tools, "Bisect", 14, "Bisect", () => _ = StartBisect(vm));
            }
            Add(tools, "Action", 14, "Repository.CustomActions", null);
            Add(tools, "Clean", 14, "Repository.Clean", () => _ = vm.CleanupAsync());
            middle.Add(tools);
            band.Add(middle);

            var end = new Row { AlignItems = Align.Center, Justify = Justify.End, Gap = 6 };
            end.Grow = 1;
            end.Add(new Icon(Icons.Get("Branch")) { Size = 13 });
            var branch = new Text();
            Bind.OneWay(vm, nameof(vm.CurrentBranch), () => vm.CurrentBranch?.Name ?? "--", name =>
            {
                branch.Value = name;
                branch.ToolTip = name;
            }, owner: band);
            end.Add(branch);
            Add(end, "Target", 13, "Repository.NavigateToCurrentHead", () =>
            {
                if (vm.CurrentBranch is { } head)
                    vm.NavigateToCommit(head.Head);
            });
            band.Add(end);
            return band;

            static void Add(Container group, string icon, float size, string text, Action command)
            {
                var button = new Button(App.Text(text))
                {
                    Icon = Icons.Get(icon, size),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(text),
                    IsEnabled = command != null,
                };
                button.Width = 32;
                if (command != null)
                    button.Click += command;
                group.Add(button);
            }
        }

        /// <summary>Upstream's <c>StartBisect</c>: refused with local
        /// changes or a bisect already running.</summary>
        private static async System.Threading.Tasks.Task StartBisect(ViewModels.Repository vm)
        {
            if (vm.IsBisectCommandRunning || vm.InProgressContext != null || !vm.CanCreatePopup())
                return;
            if (vm.LocalChangesCount > 0)
                vm.SendNotification("You have un-committed local changes. Please discard or stash them first.", true);
            else if (vm.BisectState != Models.BisectState.None)
                vm.SendNotification("Bisect is running! Please abort it before starting a new one.", true);
            else
                await vm.ExecBisectCommandAsync("start");
        }

        /// <summary>
        /// Upstream's rail, 49 points wide: the history (its dashboard),
        /// the local changes and the stashes, one of them chosen. Here
        /// they're the platform's toggle buttons, the chosen one pressed.
        /// </summary>
        private static Node Rail(ViewModels.Repository vm)
        {
            var rail = new Column { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(6, 0) };
            rail.Width = 49;
            var pages = new List<(string Icon, string Text)> { ("Dashboard", "Dashboard") };
            if (!vm.IsBare)
            {
                pages.Add(("Changes", "WorkingCopy"));
                pages.Add(("Stashes", "Stashes"));
            }
            var buttons = new List<ToggleButton>();
            for (var i = 0; i < pages.Count; i++)
            {
                var index = i;
                var button = new ToggleButton(App.Text(pages[i].Text))
                {
                    Icon = Icons.Get(pages[i].Icon, 16),
                    IconOnly = true,
                    ToolTip = App.Text(pages[i].Text),
                };
                // Pressing the chosen one again keeps it chosen.
                button.Changed += _ => vm.SelectedViewIndex = index;
                buttons.Add(button);
                rail.Add(button);
            }
            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => vm.SelectedViewIndex, chosen =>
            {
                for (var i = 0; i < buttons.Count; i++)
                    buttons[i].IsChecked = i == chosen;
            }, owner: rail);
            return rail;
        }

        /// <summary>
        /// Upstream's sidebar: a field filtering branches, tags and
        /// submodules, then a group each for local branches, remotes, tags,
        /// submodules and worktrees, a header that opens and closes it over
        /// its list. Open lists are as tall as their rows, sharing the height
        /// when they don't all fit.
        /// </summary>
        private static Container Sidebar(ViewModels.Repository vm)
        {
            var sidebar = new Column { AlignItems = Align.Stretch, Padding = new Thickness(6, 4, 0, 6), Gap = 4 };

            var filter = new SearchInput { Placeholder = App.Text("Repository.Filter") };
            Bind.Search(filter, vm, nameof(vm.Filter), () => vm.Filter, v => vm.Filter = v, owner: sidebar);
            sidebar.Add(filter);

            var local = new BranchTree(vm);
            Bind.OneWay(vm, nameof(vm.LocalBranchTrees), () => vm.LocalBranchTrees, trees => local.Nodes = trees, owner: sidebar);
            Group(sidebar, vm, "Local", "Repository.LocalBranches", nameof(vm.LocalBranchesCount), () => $"({vm.LocalBranchesCount})",
                nameof(vm.IsLocalBranchGroupExpanded), () => vm.IsLocalBranchGroupExpanded, v => vm.IsLocalBranchGroupExpanded = v,
                local.View, []);

            var remotes = new BranchTree(vm);
            Bind.OneWay(vm, nameof(vm.RemoteBranchTrees), () => vm.RemoteBranchTrees, trees => remotes.Nodes = trees, owner: sidebar);
            Group(sidebar, vm, "Remotes", "Repository.Remotes", nameof(vm.Remotes), () => $"({vm.Remotes?.Count ?? 0})",
                nameof(vm.IsRemoteGroupExpanded), () => vm.IsRemoteGroupExpanded, v => vm.IsRemoteGroupExpanded = v,
                remotes.View, [("Remote.Add", "Repository.Remotes.Add", vm.AddRemote)]);

            var tags = new ListView<Models.Tag>(tag => tag.Name, TagRow) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.VisibleTags), () => vm.VisibleTags as IReadOnlyList<Models.Tag> ?? vm.Tags ?? [],
                visible => tags.Items = visible, owner: sidebar);
            tags.SelectionChanged += selected =>
            {
                if (selected is [var tag])
                    vm.NavigateToCommit(tag.SHA);
            };
            Group(sidebar, vm, "Tags", "Repository.Tags", nameof(vm.Tags), () => $"({vm.Tags?.Count ?? 0})",
                nameof(vm.IsTagGroupExpanded), () => vm.IsTagGroupExpanded, v => vm.IsTagGroupExpanded = v,
                tags, [("Tag.Add", "Repository.Tags.Add", vm.CreateNewTag)]);

            var submodules = new ListView<Models.Submodule>(s => s.Path, SubmoduleRow) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.Submodules), () => (IReadOnlyList<Models.Submodule>)vm.Submodules ?? [],
                visible => submodules.Items = visible, owner: sidebar);
            Group(sidebar, vm, "Submodules", "Repository.Submodules", nameof(vm.Submodules), () => $"({vm.Submodules?.Count ?? 0})",
                nameof(vm.IsSubmoduleGroupExpanded), () => vm.IsSubmoduleGroupExpanded, v => vm.IsSubmoduleGroupExpanded = v,
                submodules, []);

            var worktrees = new ListView<ViewModels.Worktree>(w => w.FullPath, WorktreeRow) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.Worktrees), () => (IReadOnlyList<ViewModels.Worktree>)vm.Worktrees ?? [],
                visible => worktrees.Items = visible, owner: sidebar);
            Group(sidebar, vm, "Worktree", "Repository.Worktrees", nameof(vm.Worktrees), () => $"({vm.Worktrees?.Count ?? 0})",
                nameof(vm.IsWorktreeGroupExpanded), () => vm.IsWorktreeGroupExpanded, v => vm.IsWorktreeGroupExpanded = v,
                worktrees, []);
            return sidebar;
        }

        /// <summary>
        /// One of the sidebar's groups: upstream's <c>group_expander</c>, 28
        /// points high, an expander, the group's icon (11 points, in the
        /// secondary colour), its name and count in bold, also secondary,
        /// and its buttons at the end; then its list, while it's open.
        /// </summary>
        private static void Group(Container sidebar, ViewModels.Repository vm, string icon, string title,
            string countProperty, Func<string> count, string expandedProperty, Func<bool> expanded, Action<bool> setExpanded,
            ListView list, (string Icon, string Text, Action Command)[] buttons)
        {
            var header = new Row { AlignItems = Align.Center, Gap = 4 };
            header.Height = 28;
            var expander = new Button(App.Text(title)) { IconOnly = true, Style = ButtonStyle.Borderless };
            expander.Width = 16;
            expander.Click += () => setExpanded(!expanded());
            header.Add(expander);
            header.Add(new Icon(Icons.Get(icon)) { Size = 11, Color = Color.SecondaryLabel });
            header.Add(new Text(App.Text(title)) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            var counter = new Text { Color = Color.SecondaryLabel, Weight = FontWeight.Bold };
            Bind.OneWay(vm, countProperty, count, v => counter.Value = v, owner: header);
            header.Add(counter);
            var space = new Column();
            space.Grow = 1;
            header.Add(space);
            foreach (var (buttonIcon, text, command) in buttons)
            {
                var button = new Button(App.Text(text))
                {
                    Icon = Icons.Get(buttonIcon, 12),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(text),
                };
                button.Width = 22;
                button.Click += command;
                header.Add(button);
            }
            sidebar.Add(header);

            // As tall as its rows, as upstream's; open groups share what
            // room there is when they don't all fit.
            void Fit() => list.Height = list.ItemCount * 24;
            Fit();
            list.ItemsChanged += Fit;
            Bind.OneWay(vm, expandedProperty, expanded, open =>
            {
                expander.Icon = open ? Welcome.s_open : Welcome.s_closed;
                list.IsVisible = open;
            }, owner: header);
            sidebar.Add(list);
        }

        private static Node TagRow(Models.Tag tag)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 0, 0, 16) };
            row.Height = 24;
            row.Add(new Icon(Icons.Get("Tag")) { Size = 12 });
            row.Add(new Text(tag.Name));
            row.ToolTip = tag.Message ?? tag.Name;
            return row;
        }

        private static Node SubmoduleRow(Models.Submodule submodule)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 0, 0, 16) };
            row.Height = 24;
            row.Add(new Icon(Icons.Get("Submodule")) { Size = 12 });
            row.Add(new Text(submodule.Path));
            return row;
        }

        private static Node WorktreeRow(ViewModels.Worktree worktree)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 0, 0, 16) };
            row.Height = 24;
            row.Add(new Icon(Icons.Get("Worktree")) { Size = 12 });
            row.Add(new Text(worktree.Name));
            row.ToolTip = worktree.FullPath;
            return row;
        }

        /// <summary>The page the rail picks. Not ported yet: the history,
        /// local changes and stashes come next.</summary>
        private static Container Page(ViewModels.Repository vm)
        {
            var page = new Column { Padding = 20, Gap = 8 };
            var title = new Text { Style = TextStyle.Headline };
            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => vm.SelectedViewIndex, i => title.Value = App.Text(i switch
            {
                1 => "WorkingCopy",
                2 => "Stashes",
                _ => "Dashboard",
            }), owner: page);
            page.Add(title);
            page.Add(new Text("This page isn't ported to mitsuami yet.") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            return page;
        }
    }
}
