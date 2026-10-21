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
            var page = new Column { Padding = 8, Gap = 8, AlignItems = Align.Stretch };
            page.Grow = 1;

            page.Add(Toolbar(vm));

            var search = new SearchInput { Placeholder = App.Text("Welcome.Search") };
            Bind.Search(search, vm, nameof(vm.SearchFilter), () => vm.SearchFilter, v => vm.SearchFilter = v, owner: page);
            page.Add(search);

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
            page.Add(list);

            return page;
        }

        private static Node Toolbar(ViewModels.Welcome vm)
        {
            // Upstream's toolbar: borderless icon buttons, named by their
            // tooltips, which say what each does.
            var bar = new Row { Gap = 2 };
            Add("Clone", "Welcome.Clone", vm.Clone);
            Add("Folder.Open", "Welcome.OpenOrInit", vm.OpenLocalRepository);
            Add("Terminal", "Welcome.OpenTerminal", vm.OpenTerminal);
            Add("Folder.Add", "Welcome.AddRootFolder", vm.AddRootNode);
            Add("Scan", "Welcome.ScanDefaultCloneDir", vm.ScanDefaultCloneDir);
            return bar;

            void Add(string icon, string text, Action command)
            {
                var button = new Button(App.Text(text))
                {
                    Icon = Icons.Get(icon),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(text),
                };
                button.Click += command;
                bar.Add(button);
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
