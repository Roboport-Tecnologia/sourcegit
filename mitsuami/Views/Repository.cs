using System;
using System.Collections.Generic;
using System.Linq;

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
            // the user drags it; it's at least 200, and the page 100. On
            // Windows the default opens 320 wide: WinUI's buttons are wider
            // than upstream's, and at 250 the headers' titles were cut down
            // to make room for their buttons. A width the user dragged to
            // is kept.
            var layout = ViewModels.Preferences.Instance.Layout;
            var sidebarWidth = OperatingSystem.IsWindows() && layout.RepositorySidebarWidth == 250 ? 320 : layout.RepositorySidebarWidth;
            var split = new Split { Position = sidebarWidth };
            split.Grow = 1;
            split.Changed += width => layout.RepositorySidebarWidth = width;
            // While searching commits, upstream's search panel takes the
            // sidebar's place.
            var left = new Column { AlignItems = Align.Stretch };
            left.MinWidth = 200;
            var sidebar = Sidebar(vm);
            sidebar.Grow = 1;
            sidebar.MinHeight = 0;
            left.Add(sidebar);
            var search = CommitSearch.Create(vm.Histories.SearchCommitContext);
            search.Grow = 1;
            search.MinHeight = 0;
            left.Add(search);
            // Upstream's sidebar shows beside the history and the local
            // changes (unless the user collapsed it there), not the
            // stashes; the split collapses it.
            void ShowLeft()
            {
                var searching = vm.Histories.IsSearchingCommits;
                var page = vm.SelectedViewIndex;
                sidebar.IsVisible = !searching;
                search.IsVisible = searching;
                left.IsVisible = page == 0 || (page == 1 && !layout.IsSidebarCollapsedInLocalChanges);
            }
            Bind.OneWay(vm.Histories, nameof(vm.Histories.IsSearchingCommits), () => 0, _ => ShowLeft(), owner: left);
            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => 0, _ => ShowLeft(), owner: left);
            Bind.OneWay(layout, nameof(layout.IsSidebarCollapsedInLocalChanges), () => 0, _ => ShowLeft(), owner: left);
            split.Add(left);
            var right = Page(vm);
            right.MinWidth = 100;
            split.Add(right);
            // Upstream's in-progress bar spans the sidebar and the page.
            var main = new Column { AlignItems = Align.Stretch };
            main.Grow = 1;
            main.Add(InProgressBar(vm));
            main.Add(split);
            body.Add(main);
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
            // Every button and line in it is the platform's small spacing
            // from the next, and the band as far from the window's edges.
            // AppKit's borderless buttons have no padding of their own, so
            // there its buttons get the large spacing, or their icons touch.
            var buttons = OperatingSystem.IsMacOS() ? Spacing.Lg : Spacing.Sm;
            var band = new Row { Justify = Justify.SpaceBetween, AlignItems = Align.Center, Gap = Spacing.Sm, Padding = new Thickness(Spacing.None, Spacing.Sm), IsHeader = true, HeaderEdge = HeaderEdge.Hard };
            band.MinHeight = 36;
            // Its height stays when the page below is taller than the
            // window (a long list in the sidebar) and shrinks to fit.
            band.Shrink = 0;

            var start = new Row { AlignItems = Align.Center, Gap = buttons };
            start.Grow = 1;
            AddMenu(start, "OpenWith", "Repository.OpenWithExternalTools", () => ToolbarMenus.OpenWith(vm));
            Add(start, "Logs", 14, "Repository.ViewLogs", () => Logs.Show(vm));
            Add(start, "Statistics", 13, "Repository.Statistics", () => Statistics.Show(vm));
            band.Add(start);

            var middle = new Row { AlignItems = Align.Center, Gap = buttons };
            var sync = new Row { AlignItems = Align.Center, Gap = buttons };
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
            // Upstream's line between syncing and the rest, 16 high.
            var line = new Separator(Orientation.Vertical);
            line.Height = 16;
            middle.Add(line);
            var tools = new Row { AlignItems = Align.Center, Gap = buttons };
            Add(tools, "Branch.Add", 14, "CreateBranch", vm.CreateNewBranch);
            if (!vm.IsBare)
            {
                AddMenu(tools, "GitFlow", "GitFlow", () => ToolbarMenus.GitFlow(vm));
                AddMenu(tools, "LFS", "GitLFS", () => ToolbarMenus.LFS(vm));
                Add(tools, "Bisect", 14, "Bisect", () => _ = StartBisect(vm));
            }
            AddMenu(tools, "Action", "Repository.CustomActions", () => ToolbarMenus.CustomActions(vm));
            Add(tools, "Clean", 14, "Repository.Clean", () => _ = vm.CleanupAsync());
            middle.Add(tools);
            band.Add(middle);

            var end = new Row { AlignItems = Align.Center, Justify = Justify.End, Gap = Spacing.Sm };
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
            // The history's own buttons, while it's the page showing, as
            // upstream's sit over the history.
            var history = new Row { AlignItems = Align.Center, Gap = buttons };
            var divider = new Separator(Orientation.Vertical);
            divider.Height = 16;
            history.Add(divider);
            HistoryOptions.AddTo(history, vm.Histories);
            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => vm.SelectedViewIndex, page => history.IsVisible = page == 0, owner: history);
            end.Add(history);
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
                if (command != null)
                    button.Click += command;
                group.Add(button);
            }

            // Upstream's menus, built as they open as upstream's are.
            static void AddMenu(Container group, string icon, string text, Func<IReadOnlyList<MenuEntry>> menu)
            {
                var button = new MenuButton(App.Text(text))
                {
                    Icon = Icons.Get(icon, 14),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(text),
                    MenuOnOpen = menu,
                };
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

        /// <summary>Upstream's badge: a count (999+ at most) on an accent
        /// pill, white, 10 points; drawn, with the platform's text on it,
        /// as the ref labels are. A click on it picks its page, as one
        /// beside it does.</summary>
        private static Node Badge(ViewModels.Repository vm, string property, Func<int> count, Action pick)
        {
            var badge = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 5), Inset = new Thickness(4, 4, Length.Auto, Length.Auto) };
            badge.Height = 14;
            badge.Add(new Pill { Inset = new Thickness(0) });
            var text = new Text("") { Style = TextStyle.Caption, Color = Color.Rgba(255, 255, 255) };
            badge.Add(text);
            badge.Clicked += pick;
            Bind.OneWay(vm, property, count, n =>
            {
                badge.IsVisible = n > 0;
                text.Value = n > 999 ? "999+" : n.ToString();
            }, owner: badge);
            return badge;
        }

        private sealed class Pill() : DrawnWidget("RailBadge", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.Accent, canvas.Height / 2);
        }

        /// <summary>
        /// Upstream's rail, 49 points wide: the history (its dashboard),
        /// the local changes and the stashes, one of them chosen. Here
        /// they're the platform's toggle buttons, the chosen one pressed.
        /// </summary>
        private static Node Rail(ViewModels.Repository vm)
        {
            // Upstream's items: 41 by 42 points, 4 from everything (AppKit
            // draws a button that tall as its flexible push button); their
            // icons are the 16 points a button's icon has, upstream's 18.
            // The window's navigation, which upstream sets apart from the
            // branches beside it: on Windows, on the chosen tab's layer, as
            // the toolbar band is.
            var rail = new Column { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(4, 0), IsSidebarPane = true };
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
                button.MinWidth = 41;
                button.MinHeight = 42;
                // Pressing the chosen one again keeps it chosen.
                button.Changed += _ => vm.SelectedViewIndex = index;
                buttons.Add(button);
                var item = new Container();
                item.Add(button);
                // Upstream's counts in the corner: the local changes (and
                // the orange mark while something's in progress), the stashes.
                if (index == 1)
                {
                    item.Add(Badge(vm, nameof(vm.LocalChangesCount), () => vm.LocalChangesCount, () => vm.SelectedViewIndex = index));
                    var busy = new Icon(Icons.Get("Info")) { Size = 12, Color = Color.Warning, Inset = new Thickness(Length.Auto, 4, 4, Length.Auto) };
                    Bind.OneWay(vm, nameof(vm.InProgressContext), () => vm.InProgressContext, c => busy.IsVisible = c != null, owner: busy);
                    item.Add(busy);
                }
                else if (index == 2)
                {
                    item.Add(Badge(vm, nameof(vm.StashesCount), () => vm.StashesCount, () => vm.SelectedViewIndex = index));
                }
                rail.Add(item);
            }
            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => vm.SelectedViewIndex, chosen =>
            {
                for (var i = 0; i < buttons.Count; i++)
                    buttons[i].IsChecked = i == chosen;
            }, owner: rail);
            // Upstream's settings button at the rail's foot.
            var gap = new Column();
            gap.Grow = 1;
            rail.Add(gap);
            var settings = new Button(App.Text("Repository.Configure"))
            {
                Icon = Icons.Get("Settings", 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Repository.Configure"),
                Margin = new Thickness(0, 0, 4, 0),
            };
            settings.Click += () => RepositorySettings.Show(vm);
            rail.Add(settings);
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
                local.View, [SortButton(vm, nameof(vm.IsSortingLocalBranchByName), () => vm.IsSortingLocalBranchByName,
                    v => vm.IsSortingLocalBranchByName = v, "Repository.BranchSort",
                    "Repository.BranchSort.ByName", "Repository.BranchSort.ByCommitterDate")], branches: true);

            var remotes = new BranchTree(vm);
            Bind.OneWay(vm, nameof(vm.RemoteBranchTrees), () => vm.RemoteBranchTrees, trees => remotes.Nodes = trees, owner: sidebar);
            Group(sidebar, vm, "Remotes", "Repository.Remotes", nameof(vm.Remotes), () => $"({vm.Remotes?.Count ?? 0})",
                nameof(vm.IsRemoteGroupExpanded), () => vm.IsRemoteGroupExpanded, v => vm.IsRemoteGroupExpanded = v,
                remotes.View, [SortButton(vm, nameof(vm.IsSortingRemoteBranchByName), () => vm.IsSortingRemoteBranchByName,
                    v => vm.IsSortingRemoteBranchByName = v, "Repository.BranchSort",
                    "Repository.BranchSort.ByName", "Repository.BranchSort.ByCommitterDate"),
                    HeaderButton("Remote.Add", "Repository.Remotes.Add", vm.AddRemote)], branches: true);

            // Several at once, as upstream's: deleting them. Upstream's
            // visible tags (the filter applied), as a list or a tree whose
            // folders open and close.
            ListView<object> tags = null;
            tags = new ListView<object>(TagKey, item => TagRow(vm, item, () => TagsMenu(vm, tags, item)),
                SelectionMode.Multiple, itemAnimations: false) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.VisibleTags), () => vm.VisibleTags, visible => tags.Items = visible switch
            {
                ViewModels.TagCollectionAsList list => list.TagItems.Cast<object>().ToList(),
                ViewModels.TagCollectionAsTree tree => new Rows<ViewModels.TagTreeNode>(tree.Rows),
                _ => [],
            }, owner: sidebar);
            tags.SelectionChanged += selected =>
            {
                if (selected is [var item] && TagOf(item) is { } tag)
                    vm.NavigateToCommit(tag.SHA);
            };
            tags.Activated += item =>
            {
                if (item is ViewModels.TagTreeNode { IsFolder: true } folder && vm.VisibleTags is ViewModels.TagCollectionAsTree tree)
                    tree.ToggleExpand(folder);
            };
            Group(sidebar, vm, "Tags", "Repository.Tags", nameof(vm.Tags), () => $"({vm.Tags?.Count ?? 0})",
                nameof(vm.IsTagGroupExpanded), () => vm.IsTagGroupExpanded, v => vm.IsTagGroupExpanded = v,
                tags, [TreeToggle(vm, nameof(vm.ShowTagsAsTree), () => vm.ShowTagsAsTree, v => vm.ShowTagsAsTree = v, "Repository.ShowTagsAsTree"),
                    SortButton(vm, nameof(vm.IsSortingTagsByName), () => vm.IsSortingTagsByName,
                    v => vm.IsSortingTagsByName = v, "Repository.Tags.Sort",
                    "Repository.Tags.OrderByName", "Repository.Tags.OrderByCreatorDate"),
                    HeaderButton("Tag.Add", "Repository.Tags.Add", vm.CreateNewTag)]);

            // Upstream's visible submodules, as a list or a tree.
            var submodules = new ListView<object>(SubmoduleKey, item => SubmoduleRow(vm, item), itemAnimations: false) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.VisibleSubmodules), () => vm.VisibleSubmodules, visible => submodules.Items = visible switch
            {
                ViewModels.SubmoduleCollectionAsList list => list.Submodules.Cast<object>().ToList(),
                ViewModels.SubmoduleCollectionAsTree tree => new Rows<ViewModels.SubmoduleTreeNode>(tree.Rows),
                _ => [],
            }, owner: sidebar);
            submodules.Activated += item =>
            {
                if (item is ViewModels.SubmoduleTreeNode { IsFolder: true } folder && vm.VisibleSubmodules is ViewModels.SubmoduleCollectionAsTree tree)
                    tree.ToggleExpand(folder);
            };
            Group(sidebar, vm, "Submodules", "Repository.Submodules", nameof(vm.Submodules), () => $"({vm.Submodules?.Count ?? 0})",
                nameof(vm.IsSubmoduleGroupExpanded), () => vm.IsSubmoduleGroupExpanded, v => vm.IsSubmoduleGroupExpanded = v,
                submodules, [TreeToggle(vm, nameof(vm.ShowSubmodulesAsTree), () => vm.ShowSubmodulesAsTree, v => vm.ShowSubmodulesAsTree = v, "Repository.ShowSubmodulesAsTree"),
                    HeaderButton("Submodule.Add", "Repository.Submodules.Add", vm.AddSubmodule)],
                [HeaderButton("Loading", "Repository.Submodules.Update", vm.UpdateSubmodules)]);

            var worktrees = new ListView<ViewModels.Worktree>(w => w.FullPath, w => WorktreeRow(vm, w), itemAnimations: false) { EstimatedRowHeight = 24 };
            Bind.OneWay(vm, nameof(vm.Worktrees), () => (IReadOnlyList<ViewModels.Worktree>)vm.Worktrees ?? [],
                visible => worktrees.Items = visible, owner: sidebar);
            Group(sidebar, vm, "Worktree", "Repository.Worktrees", nameof(vm.Worktrees), () => $"({vm.Worktrees?.Count ?? 0})",
                nameof(vm.IsWorktreeGroupExpanded), () => vm.IsWorktreeGroupExpanded, v => vm.IsWorktreeGroupExpanded = v,
                worktrees, [HeaderButton("Worktree.Add", "Repository.Worktrees.Add", vm.AddWorktree)],
                [HeaderButton("Loading", "Repository.Worktrees.Prune", () => _ = vm.PruneWorktreesAsync())]);

            // One selection in the sidebar: picking in a list unselects the
            // others, as upstream's lists do.
            var clears = new List<Action>
            {
                () => local.View.SelectedItems = [],
                () => remotes.View.SelectedItems = [],
                () => tags.SelectedItems = [],
                () => submodules.SelectedItems = [],
                () => worktrees.SelectedItems = [],
            };
            void Only(int index)
            {
                for (var i = 0; i < clears.Count; i++)
                {
                    if (i != index)
                        clears[i]();
                }
            }
            local.View.SelectionChanged += s => { if (s.Count > 0) Only(0); };
            remotes.View.SelectionChanged += s => { if (s.Count > 0) Only(1); };
            tags.SelectionChanged += s => { if (s.Count > 0) Only(2); };
            submodules.SelectionChanged += s => { if (s.Count > 0) Only(3); };
            worktrees.SelectionChanged += s => { if (s.Count > 0) Only(4); };
            return sidebar;
        }

        /// <summary>
        /// One of the sidebar's groups: upstream's <c>group_expander</c>, 28
        /// points high, opening and closing at a click anywhere on it but its
        /// buttons: an expander, the group's icon (11 points, in the
        /// secondary colour), its name and count in bold, also secondary,
        /// and its buttons at the end (<paramref name="withRows"/>' only
        /// while it has rows, before the others); then its list, while it's
        /// open.
        /// </summary>
        private static void Group(Container sidebar, ViewModels.Repository vm, string icon, string title,
            string countProperty, Func<string> count, string expandedProperty, Func<bool> expanded, Action<bool> setExpanded,
            ListView list, Node[] buttons, Node[] withRows = null, bool branches = false)
        {
            var header = new Row { AlignItems = Align.Center, Gap = 4 };
            header.MinHeight = 28;
            header.Shrink = 0;
            var expander = new Button(App.Text(title)) { IconOnly = true, Style = ButtonStyle.Borderless };
            expander.Click += () => setExpanded(!expanded());
            // The whole header opens and closes it, as upstream's toggle
            // button does; the expander is the keyboard's way.
            header.Clicked += () => setExpanded(!expanded());
            header.Add(expander);
            header.Add(new Icon(Icons.Get(icon)) { Size = 11, Color = Color.SecondaryLabel });
            // One line, as upstream's: a narrow sidebar cuts it off, and
            // the buttons after it keep their room.
            header.Add(new Text(App.Text(title)) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold, MaxLines = 1 });
            var counter = new Text { Color = Color.SecondaryLabel, Weight = FontWeight.Bold };
            Bind.OneWay(vm, countProperty, count, v => counter.Value = v, owner: header);
            header.Add(counter);
            var space = new Column();
            space.Grow = 1;
            header.Add(space);
            withRows ??= [];
            foreach (var button in withRows)
                header.Add(button);
            foreach (var button in buttons)
                header.Add(button);
            sidebar.Add(header);

            // As tall as its rows, as upstream's. When the open groups
            // don't all fit, upstream's tags, submodules and worktrees give
            // up their room first, down to 120 points each, and the branches
            // share what's left; each list then scrolls.
            // An open group without rows shows nothing under its header.
            void Fit()
            {
                var rows = list.ItemCount * 24;
                list.Height = rows;
                list.MinHeight = branches ? 0 : Math.Min(120, rows);
                list.Shrink = branches ? 1 : 1000;
                list.IsVisible = expanded() && list.ItemCount > 0;
                foreach (var button in withRows)
                    button.IsVisible = list.ItemCount > 0;
            }
            Fit();
            list.ItemsChanged += Fit;
            Bind.OneWay(vm, expandedProperty, expanded, open =>
            {
                expander.Icon = open ? Welcome.s_open : Welcome.s_closed;
                Fit();
            }, owner: header);
            sidebar.Add(list);
        }

        /// <summary>A group header's button. As the toolbar's: upstream's
        /// 12-point icons in 14-point buttons came out too small beside the
        /// platform's text.</summary>
        private static Button HeaderButton(string icon, string text, Action command)
        {
            var button = new Button(App.Text(text))
            {
                Icon = Icons.Get(icon, 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text(text),
            };
            button.Click += command;
            return button;
        }

        /// <summary>Upstream's sort button: by name or by date, its icon
        /// the order chosen, a menu of the two with the chosen one checked.</summary>
        private static MenuButton SortButton(ViewModels.Repository vm, string property, Func<bool> byName, Action<bool> setByName,
            string text, string nameText, string dateText)
        {
            var button = new MenuButton(App.Text(text))
            {
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text(text),
                MenuOnOpen = () =>
                [
                    new MenuItem(App.Text(nameText), () => setByName(true)) { Checked = byName(), Radio = true },
                    new MenuItem(App.Text(dateText), () => setByName(false)) { Checked = !byName(), Radio = true },
                ],
            };
            Bind.OneWay(vm, property, byName, name => button.Icon = Icons.Get(name ? "OrderByName" : "OrderByTime", 14), owner: button);
            return button;
        }

        /// <summary>A refresh brings new tags, submodules and worktrees,
        /// and a row stays while its key does: menus act on what's at its
        /// key now.</summary>
        /// <summary>The tags selected, the row right-clicked among them (or
        /// alone if it isn't), as upstream's menus take them.</summary>
        private static IReadOnlyList<MenuEntry> TagsMenu(ViewModels.Repository vm, ListView<object> list, object item)
        {
            if (TagOf(item) is not { } clicked)
                return [];
            var all = vm.Tags ?? [];
            var selected = new List<Models.Tag>();
            foreach (var tag in list.SelectedItems.Select(TagOf).Where(t => t != null))
            {
                if (all.Find(t => t.Name == tag.Name) is { } now)
                    selected.Add(now);
            }
            if (selected.Find(t => t.Name == clicked.Name) == null)
                selected = all.FindAll(t => t.Name == clicked.Name);
            return SidebarMenus.Tags(vm, selected);
        }

        private static Models.Tag TagOf(object item) => item switch
        {
            ViewModels.TagListItem i => i.Tag,
            ViewModels.TagTreeNode n => n.Tag,
            Models.Tag t => t,
            _ => null,
        };

        private static string TagKey(object item) => item switch
        {
            ViewModels.TagTreeNode { IsFolder: true } n => $"folder {n.FullPath}",
            _ when TagOf(item) is { } tag => $"tag {tag.Name}",
            _ => item.GetHashCode().ToString(),
        };

        private static string SubmoduleKey(object item) => item switch
        {
            ViewModels.SubmoduleTreeNode n => $"{(n.IsFolder ? "folder" : "module")} {n.FullPath}",
            Models.Submodule m => $"module {m.Path}",
            _ => item.GetHashCode().ToString(),
        };

        /// <summary>Upstream's toggle in a group's header: its items as a
        /// tree of their folders, or a list.</summary>
        private static Node TreeToggle(ViewModels.Repository vm, string property, Func<bool> get, Action<bool> set, string tip)
        {
            var toggle = new ToggleButton(App.Text(tip))
            {
                Icon = Icons.Get("Tree", 12),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text(tip),
            };
            Bind.OneWay(vm, property, get, on => toggle.IsChecked = on, owner: toggle);
            toggle.Changed += set;
            return toggle;
        }

        /// <summary>A tree's rows as a list's items: upstream's rows, which
        /// grow and shrink as folders open and close.</summary>
        private sealed class Rows<T>(Avalonia.Collections.AvaloniaList<T> rows) : IReadOnlyList<object>, System.Collections.Specialized.INotifyCollectionChanged
        {
            public object this[int index] => rows[index];
            public int Count => rows.Count;
            public IEnumerator<object> GetEnumerator() => rows.Cast<object>().GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public event System.Collections.Specialized.NotifyCollectionChangedEventHandler CollectionChanged
            {
                add => rows.CollectionChanged += value;
                remove => rows.CollectionChanged -= value;
            }
        }

        /// <summary>A tag's row, or a folder of tags: indented by its depth
        /// in the tree, a folder's chevron and its count.</summary>
        private static Node TagRow(ViewModels.Repository vm, object item, Func<IReadOnlyList<MenuEntry>> menu)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 4, 0, 16) };
            row.Height = 24;
            row.ContextMenuOnOpen = menu;
            if (item is ViewModels.TagTreeNode node)
            {
                for (var level = 0; level < node.Depth; level++)
                    row.Add(new Column { Width = Spacing.Indent });
                if (node.IsFolder)
                {
                    Folder(row, node, () => node.IsExpanded, node.FullPath.Split('/')[^1], node.TagsCount,
                        () => (vm.VisibleTags as ViewModels.TagCollectionAsTree)?.ToggleExpand(node));
                    return row;
                }
                // Under the folders' chevrons, as the branch tree's rows.
                row.Add(new Column { Width = 16 });
            }
            var tag = TagOf(item);
            row.Add(new Icon(Icons.Get("Tag")) { Size = 12 });
            var name = new Text(item is ViewModels.TagTreeNode n ? n.FullPath.Split('/')[^1] : tag.Name) { MaxLines = 1 };
            name.Grow = 1;
            name.Shrink = 1;
            name.MinWidth = 0;
            row.Add(name);
            row.ToolTip = tag.Message ?? tag.Name;
            Func<Models.FilterMode> mode = item switch
            {
                ViewModels.TagListItem i => () => i.FilterMode,
                ViewModels.TagTreeNode t => () => t.FilterMode,
                _ => () => Models.FilterMode.None,
            };
            row.Add(FilterModeSwitch.Create(row, item as System.ComponentModel.INotifyPropertyChanged, mode, m => vm.SetTagFilterMode(tag, m)));
            return row;
        }

        /// <summary>A folder of a sidebar tree: its chevron, which opens or
        /// closes it, its icon, name and count.</summary>
        private static void Folder(Container row, System.ComponentModel.INotifyPropertyChanged node, Func<bool> open, string name, string count, Action toggle)
        {
            var button = new Button(name) { IconOnly = true, Style = ButtonStyle.Borderless };
            button.Click += toggle;
            row.Add(button);
            var icon = new Icon(Icons.Get("Folder")) { Size = 12 };
            row.Add(icon);
            // The row stays as the folder opens and closes.
            Bind.OneWay(node, "IsExpanded", open, now =>
            {
                button.Icon = now ? Welcome.s_open : Welcome.s_closed;
                icon.Vector = Icons.Get(now ? "Folder.Open" : "Folder");
            }, owner: row);
            var text = new Text(name) { MaxLines = 1 };
            text.Shrink = 1;
            text.MinWidth = 0;
            row.Add(text);
            if (!string.IsNullOrEmpty(count))
                row.Add(new Text(count) { Color = Color.SecondaryLabel });
        }

        /// <summary>A submodule's row, or a folder of them.</summary>
        private static Node SubmoduleRow(ViewModels.Repository vm, object item)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 0, 0, 16) };
            row.Height = 24;
            if (item is ViewModels.SubmoduleTreeNode node)
            {
                for (var level = 0; level < node.Depth; level++)
                    row.Add(new Column { Width = Spacing.Indent });
                if (node.IsFolder)
                {
                    Folder(row, node, () => node.IsExpanded, node.FullPath.Split('/')[^1], node.ChildCounter,
                        () => (vm.VisibleSubmodules as ViewModels.SubmoduleCollectionAsTree)?.ToggleExpand(node));
                    return row;
                }
                row.Add(new Column { Width = 16 });
            }
            var submodule = item switch
            {
                ViewModels.SubmoduleTreeNode t => t.Module,
                Models.Submodule m => m,
                _ => null,
            };
            if (submodule == null)
                return row;
            row.Add(new Icon(Icons.Get("Submodule")) { Size = 12 });
            row.Add(new Text(item is ViewModels.SubmoduleTreeNode ? submodule.Path.Split('/')[^1] : submodule.Path) { MaxLines = 1 });
            row.ToolTip = submodule.Path;
            var path = submodule.Path;
            row.ContextMenuOnOpen = () => vm.Submodules?.Find(s => s.Path == path) is { } now ? SidebarMenus.Submodule(vm, now) : [];
            return row;
        }

        private static Node WorktreeRow(ViewModels.Repository vm, ViewModels.Worktree worktree)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 0, 0, 16) };
            row.Height = 24;
            row.Add(new Icon(Icons.Get("Worktree")) { Size = 12 });
            row.Add(new Text(worktree.Name));
            row.ToolTip = worktree.FullPath;
            var path = worktree.FullPath;
            row.ContextMenuOnOpen = () => vm.Worktrees?.Find(w => w.FullPath == path) is { } now ? SidebarMenus.Worktree(vm, now) : [];
            return row;
        }

        /// <summary>
        /// Upstream's bar over the history while bisecting, 28 points high:
        /// what to do next, in bold, a spinner while a command runs, and
        /// Bad, Good, Skip (while waiting for a mark) and Abort. Upstream's
        /// colour is a pale yellow in its light theme and a dark amber in
        /// its dark one: a translucent amber over the window gives each.
        /// </summary>
        private static Node BisectBar(ViewModels.Repository vm)
        {
            var bar = new Row { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(0, 8) };
            bar.MinHeight = 28;
            bar.Shrink = 0;
            bar.Add(new Tint { Inset = new Thickness(0) });
            var text = new Text { Weight = FontWeight.Bold, MaxLines = 1 };
            text.Grow = 1;
            text.Width = 0;
            bar.Add(text);
            var spinner = new Spinner();
            bar.Add(spinner);
            Button Command(string title, string subcmd)
            {
                var button = new Button(App.Text(title));
                button.Click += async () =>
                {
                    if (!vm.IsBisectCommandRunning && vm.CanCreatePopup())
                        await vm.ExecBisectCommandAsync(subcmd);
                };
                bar.Add(button);
                return button;
            }
            var bad = Command("Bisect.Bad", "bad");
            var good = Command("Bisect.Good", "good");
            var skip = Command("Bisect.Skip", "skip");
            var abort = Command("Bisect.Abort", "reset");

            void Show()
            {
                var state = vm.BisectState;
                var running = vm.IsBisectCommandRunning;
                bar.IsVisible = state != Models.BisectState.None;
                var message = state switch
                {
                    Models.BisectState.WaitingForFirstBad => App.Text("Bisect.WaitingForFirstBad"),
                    Models.BisectState.WaitingForCheckoutAnother => App.Text("Bisect.WaitingForCheckoutAnother"),
                    Models.BisectState.WaitingForFirstGood => App.Text("Bisect.WaitingForFirstGood"),
                    Models.BisectState.WaitingForMark => App.Text("Bisect.WaitingForMark"),
                    _ => "",
                };
                text.Value = message;
                text.ToolTip = message;
                spinner.IsVisible = running;
                bad.IsEnabled = !running && state != Models.BisectState.WaitingForCheckoutAnother;
                good.IsEnabled = !running && state is not (Models.BisectState.WaitingForFirstBad or Models.BisectState.WaitingForCheckoutAnother);
                skip.IsVisible = state == Models.BisectState.WaitingForMark;
                skip.IsEnabled = !running;
                abort.IsEnabled = !running;
            }
            Bind.OneWay(vm, nameof(vm.BisectState), () => vm.BisectState, _ => Show(), owner: bar);
            Bind.OneWay(vm, nameof(vm.IsBisectCommandRunning), () => vm.IsBisectCommandRunning, _ => Show(), owner: bar);
            return bar;
        }

        /// <summary>
        /// Upstream's bar while a cherry-pick, rebase, revert or merge is in
        /// progress, over the sidebar and the page: what's going on and the
        /// commit it's at (its short SHA in upstream's orange, the commit as
        /// its tooltip), a spinner while skipping or aborting, then Skip
        /// (for a cherry-pick, a revert, or a rebase stopped at a commit),
        /// Resolve (which shows the local changes, unless they show) and
        /// Abort. The bisect bar's colour, as upstream's.
        /// </summary>
        private static Node InProgressBar(ViewModels.Repository vm)
        {
            var bar = new Row { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(0, 8) };
            bar.Height = 28;
            bar.Shrink = 0;
            bar.Add(new Tint { Inset = new Thickness(0) });
            var what = new Row { AlignItems = Align.Center, Gap = 4 };
            what.Grow = 1;
            what.Width = 0;
            var text = new Text { Weight = FontWeight.Bold, MaxLines = 1 };
            text.Shrink = 1;
            text.MinWidth = 0;
            what.Add(text);
            var head = new Text { Weight = FontWeight.Bold, MaxLines = 1, Color = Color.Rgba(255, 140, 0) };
            head.Shrink = 0;
            what.Add(head);
            // A merge's: the source, an arrow, the current branch.
            var arrow = new Text("→") { Weight = FontWeight.Bold };
            what.Add(arrow);
            var current = new Text { Weight = FontWeight.Bold, MaxLines = 1, Color = Color.Rgba(255, 140, 0) };
            what.Add(current);
            bar.Add(what);
            var spinner = new Spinner();
            bar.Add(spinner);
            var skip = new Button(Popups.ButtonText("Repository.Skip"));
            skip.Click += () => _ = vm.SkipMergeAsync();
            bar.Add(skip);
            var resolve = new Button(Popups.ButtonText("Repository.Resolve"));
            resolve.Click += () => vm.SelectedViewIndex = 1;
            bar.Add(resolve);
            var abort = new Button(Popups.ButtonText("Repository.Abort"));
            abort.Click += () => _ = vm.AbortMergeAsync();
            bar.Add(abort);

            static string Tip(Models.Commit commit) =>
                string.IsNullOrEmpty(commit.Subject) ? commit.SHA : $"{commit.Author.Name}  ·  {Models.DateTimeFormat.Format(commit.CommitterTime)}\n\n{commit.Subject}";
            void Show()
            {
                var context = vm.InProgressContext;
                bar.IsVisible = context != null;
                var (message, commit, name) = context switch
                {
                    ViewModels.CherryPickInProgress c => ($"{App.Text("InProgress.CherryPick")} {App.Text("InProgress.CherryPick.Head")}", c.Head, null),
                    ViewModels.RebaseInProgress r when r.StoppedAt != null => ($"{App.Text("InProgress.Rebase")} {App.Text("InProgress.Rebase.StoppedAt")}", r.StoppedAt, null),
                    ViewModels.RebaseInProgress => (App.Text("InProgress.Rebase"), null, null),
                    ViewModels.RevertInProgress r => ($"{App.Text("InProgress.Revert")} {App.Text("InProgress.Revert.Head")}", r.Head, null),
                    ViewModels.MergeInProgress m => ($"{App.Text("InProgress.Merge")} {App.Text("InProgress.Merge.Operating")}", m.Source, m.SourceName),
                    _ => ("", (Models.Commit)null, (string)null),
                };
                text.Value = message;
                head.IsVisible = commit != null;
                if (commit != null)
                {
                    head.Value = name ?? (commit.SHA.Length > 10 ? commit.SHA[..10] : commit.SHA);
                    head.Style = name == null ? TextStyle.Monospace : TextStyle.Body;
                    head.ToolTip = Tip(commit);
                }
                arrow.IsVisible = current.IsVisible = context is ViewModels.MergeInProgress;
                if (context is ViewModels.MergeInProgress merge)
                    current.Value = merge.Current;
                skip.IsVisible = context is ViewModels.CherryPickInProgress or ViewModels.RevertInProgress
                    || context is ViewModels.RebaseInProgress { StoppedAt: not null };
                resolve.IsVisible = !vm.IsWorkingCopyVisible;
                var busy = vm.IsSkippingOrAbortingMerge;
                spinner.IsVisible = busy;
                skip.IsEnabled = !busy;
                abort.IsEnabled = !busy;
            }
            foreach (var property in new[] { nameof(vm.InProgressContext), nameof(vm.IsWorkingCopyVisible), nameof(vm.IsSkippingOrAbortingMerge) })
                Bind.OneWay(vm, property, () => 0, _ => Show(), owner: bar);
            return bar;
        }

        /// <summary>Behind the bisect and in-progress bars, upstream's
        /// conflict colour (a pale yellow in its light theme, a dark amber
        /// in its dark one): amber at a quarter over the window.</summary>
        private sealed class Tint() : DrawnWidget("BisectBar", 0, 0)
        {
            protected override void Draw(Canvas canvas) => canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.Rgba(255, 196, 0, 64));
        }

        /// <summary>The page the rail picks: the history, the local changes
        /// or the stashes.</summary>
        private static Container Page(ViewModels.Repository vm)
        {
            var page = new Column { AlignItems = Align.Stretch };
            // Upstream's dashboard: the bisect and filter bars over the
            // history.
            var history = new Column { AlignItems = Align.Stretch };
            history.Grow = 1;
            history.Add(BisectBar(vm));
            history.Add(HistoryFilters.Create(vm));
            history.Add(Histories.Create(vm, vm.Histories));
            page.Add(history);

            var changes = WorkingCopy.Create(vm.WorkingCopy);
            page.Add(changes);

            var stashes = Stashes.Create(vm);
            page.Add(stashes);

            Bind.OneWay(vm, nameof(vm.SelectedViewIndex), () => vm.SelectedViewIndex, i =>
            {
                history.IsVisible = i == 0;
                changes.IsVisible = i == 1;
                stashes.IsVisible = i == 2;
            }, owner: page);
            return page;
        }
    }
}
