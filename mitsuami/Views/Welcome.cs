using System;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The welcome page, upstream's <c>Views/Welcome.axaml</c> and
    /// <c>WelcomeToolbar.axaml</c>: the toolbar's commands, a search field and
    /// the repositories, groups indented as upstream's tree rows are (its
    /// view model flattens the tree into rows with a depth).
    /// </summary>
    public static class Welcome
    {
        public static Node Create(ViewModels.Welcome vm)
        {
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;
            page.Add(Toolbar(vm));

            // Upstream's repositories: a column half the page wide, at least
            // 600 points, with a quarter on each side.
            var body = new Row { Padding = 8, AlignItems = Align.Stretch };
            body.Grow = 1;
            var middle = new Column { Gap = 8, AlignItems = Align.Stretch };
            middle.Grow = 2;
            body.Add(Side());
            body.Add(middle);
            body.Add(Side());
            page.Add(body);

            var search = new SearchInput { Placeholder = App.Text("Welcome.Search") };
            Bind.Search(search, vm, nameof(vm.SearchFilter), () => vm.SearchFilter, v => vm.SearchFilter = v, owner: page);
            middle.Add(search);

            var list = new ListView<ViewModels.RepositoryNode>(node => node.Id, node => Row(vm, node))
            {
                EstimatedRowHeight = 30,
                Items = vm.Rows,
            };
            list.Grow = 1;
            // A repository opens in a tab, a group opens or closes, as
            // upstream's double-click does.
            list.Activated += node =>
            {
                if (node.IsRepository)
                    node.Open();
                else
                    vm.ToggleNodeIsExpanded(node);
            };
            middle.Add(list);

            return page;

            static Node Side()
            {
                var side = new Column();
                side.Grow = 1;
                return side;
            }
        }

        /// <summary>
        /// Upstream's toolbar band (<c>WelcomeToolbar.axaml</c>), 36 points
        /// high: clone, open and terminal on the left, a new group and a
        /// rescan on the right, each a borderless icon button 32 points wide,
        /// its icon sized as upstream's. A window's toolbar would be native,
        /// but the repository page's band has far more buttons than a
        /// toolbar holds, and both pages share the band.
        /// </summary>
        private static Node Toolbar(ViewModels.Welcome vm)
        {
            // The window's header: on macOS in the title bar, under the tabs,
            // with no line between them, as Safari's bars are.
            var band = new Row { Justify = Justify.SpaceBetween, AlignItems = Align.Center, Padding = new Thickness(0, 4), IsHeader = true };
            band.Height = 36;
            var start = new Row { AlignItems = Align.Center };
            var end = new Row { AlignItems = Align.Center };
            band.Add(start);
            band.Add(end);

            Add(start, Icons.Get("Clone", 16, 2), "Welcome.Clone", vm.Clone);
            Add(start, Icons.Get("Folder.Open", 14, 1), "Welcome.OpenOrInit", vm.OpenLocalRepository);
            Add(start, Icons.Get("Terminal", 13), "Welcome.OpenTerminal", vm.OpenTerminal);
            Add(end, Icons.Get("Folder.Add", 14, 1), "Welcome.AddRootFolder", vm.AddRootNode);
            Add(end, Icons.Get("Scan", 14), "Welcome.ScanDefaultCloneDir", vm.ScanDefaultCloneDir);
            return band;

            static void Add(Container group, VectorIcon icon, string text, Action command)
            {
                var button = new Button(App.Text(text))
                {
                    Icon = icon,
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(text),
                };
                button.Width = 32;
                button.Click += command;
                group.Add(button);
            }
        }

        /// <summary>Upstream's row template: an expander for groups, the
        /// name, a repository's path, and its status once it's known.</summary>
        private static Node Row(ViewModels.Welcome vm, ViewModels.RepositoryNode node)
        {
            var row = new Row { Gap = 6, AlignItems = Align.Center, Padding = new Thickness(0, 4, 0, 4 + node.Depth * 16) };
            row.Height = 30;

            if (!node.IsRepository)
            {
                var expander = new Button { Style = ButtonStyle.Borderless };
                Bind.OneWay(node, nameof(node.IsExpanded), () => node.IsExpanded,
                    expanded => expander.Label = expanded ? "▾" : "▸", owner: row);
                expander.Click += () => vm.ToggleNodeIsExpanded(node);
                row.Add(expander);
            }

            var name = new Text();
            Bind.OneWay(node, nameof(node.Name), () => node.Name, v => name.Value = v, owner: row);
            row.Add(name);

            if (node.IsRepository)
            {
                // Upstream marks a missing repository with an orange error icon.
                var path = new Text(node.IsInvalid ? $"({node.Id}) ⚠" : $"({node.Id})") { Style = TextStyle.Caption };
                path.Grow = 1;
                row.Add(path);

                var status = new Text { Style = TextStyle.Caption };
                Bind.OneWay(node, nameof(node.Status), () => node.Status, s => status.Value = Describe(s), owner: row);
                row.Add(status);
            }

            return row;
        }

        /// <summary>What upstream shows in badges: local changes, ahead and
        /// behind, and the branch.</summary>
        private static string Describe(Models.RepositoryStatus status)
        {
            if (status == null)
                return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (status.LocalChanges > 0)
                parts.Add($"{status.LocalChanges} ±");
            if (status.IsTrackingStatusVisible)
                parts.Add(status.TrackingDescription);
            parts.Add(status.CurrentBranch);
            return string.Join("  ", parts);
        }
    }
}
