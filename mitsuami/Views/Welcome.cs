using System;
using System.Collections.Generic;
using System.Linq;

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
            middle.MinWidth = 600;
            body.Add(Side());
            body.Add(middle);
            body.Add(Side());
            page.Add(body);

            // Folders dropped elsewhere in the list go at its top.
            middle.FileDrop = Drops;
            middle.FilesDropped += paths => _ = AddDropped(vm, paths, null);

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

        /// <summary>
        /// Upstream's row template: 30 points high, indented 16 a level; an
        /// expander for groups in a 16-point slot; a group's folder or a
        /// repository's bookmark in an 18-point one; the name, a repository's
        /// path in the secondary colour (an orange error icon if it's gone);
        /// then its status at the end.
        /// </summary>
        private static Node Row(ViewModels.Welcome vm, ViewModels.RepositoryNode node)
        {
            var row = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 8, 0, node.Depth * 16) };
            row.Height = 30;
            row.ContextMenu = Menu(node);
            // Folders dropped on a group go in it; on a repository, in its group.
            row.FileDrop = Drops;
            row.FilesDropped += paths => _ = AddDropped(vm, paths, node.IsRepository ? vm.FindParentGroup(node) : node);

            var expander = Slot(16);
            if (!node.IsRepository)
            {
                var button = new Button(node.Name) { IconOnly = true, Style = ButtonStyle.Borderless };
                Bind.OneWay(node, nameof(node.IsExpanded), () => node.IsExpanded,
                    open => button.Icon = open ? s_open : s_closed, owner: row);
                button.Click += () => vm.ToggleNodeIsExpanded(node);
                expander.Add(button);
            }
            row.Add(expander);

            var mark = Slot(18);
            if (node.IsRepository)
            {
                var bookmark = new Icon(Icons.Get("Bookmark")) { Size = 12 };
                Bind.OneWay(node, nameof(node.Bookmark), () => node.Bookmark,
                    b => bookmark.Color = BookmarkColor(b), owner: row);
                mark.Add(bookmark);
            }
            else
            {
                var folder = new Icon(Icons.Get("Folder")) { Size = 14 };
                mark.Add(folder);
            }
            row.Add(mark);

            var name = new Text();
            Bind.OneWay(node, nameof(node.Name), () => node.Name, v => name.Value = v, owner: row);
            row.Add(name);

            if (node.IsRepository && node.IsInvalid)
            {
                var gone = new Icon(Icons.Get("Error")) { Size = 12, Color = Color.Rgba(255, 165, 0) };
                gone.Margin = new Thickness(0, 0, 0, 2);
                row.Add(gone);
            }
            else if (node.IsRepository)
            {
                var path = new Text($"({node.Id})") { Style = TextStyle.Caption, Color = Color.SecondaryLabel };
                path.Margin = new Thickness(0, 0, 0, 4);
                row.Add(path);
            }

            var space = new Column();
            space.Grow = 1;
            row.Add(space);

            if (node.IsRepository)
            {
                var status = new Row { Gap = 6, AlignItems = Align.Center };
                Bind.OneWay(node, nameof(node.Status), () => node.Status, s => ShowStatus(status, s), owner: row);
                row.Add(status);
            }
            return row;

            static Mitsuamia.Row Slot(float width)
            {
                var slot = new Mitsuamia.Row { AlignItems = Align.Center, Justify = Justify.Center };
                slot.Width = width;
                return slot;
            }
        }

        /// <summary>Upstream's menu for a row: a group's, or a repository's
        /// while its folder is there; else only Delete.</summary>
        private static MenuEntry[] Menu(ViewModels.RepositoryNode node)
        {
            var edit = new MenuItem(App.Text("Welcome.Edit"), node.Edit);
            var move = new MenuItem(App.Text("Welcome.Move"), node.Move);
            var delete = new MenuItem(App.Text("Welcome.Delete"), node.Delete);
            if (!node.IsRepository)
            {
                var entries = new List<MenuEntry>();
                if (node.SubNodes.Count > 0)
                    entries.AddRange([new MenuItem(App.Text("Welcome.OpenAllInNode"), node.Open), MenuSeparator.Instance]);
                entries.AddRange([new MenuItem(App.Text("Welcome.AddSubFolder"), node.AddSubFolder), edit, move, MenuSeparator.Instance, delete]);
                return [.. entries];
            }
            if (!System.IO.Directory.Exists(node.Id))
                return [delete];
            return
            [
                new MenuItem(App.Text("Welcome.OpenOrInit"), node.Open),
                MenuSeparator.Instance,
                new MenuItem(App.Text("Repository.Explore"), node.OpenInFileManager),
                new MenuItem(App.Text("Repository.Terminal"), node.OpenTerminal),
                MenuSeparator.Instance,
                edit,
                move,
                MenuSeparator.Instance,
                delete,
            ];
        }

        /// <summary>Upstream takes dropped folders, and files' folders.</summary>
        private static readonly FileDrop Drops = new(Folders: true);

        /// <summary>
        /// Upstream's drop: each folder's repository, added under
        /// <paramref name="parent"/> (moved there if it's listed); a folder
        /// git won't trust asks first, one at a time, as upstream does.
        /// </summary>
        private static async System.Threading.Tasks.Task AddDropped(ViewModels.Welcome vm, IReadOnlyList<string> paths, ViewModels.RepositoryNode parent)
        {
            var refresh = false;
            foreach (var path in paths)
            {
                var root = path;
                if (!System.IO.Directory.Exists(root))
                {
                    if (System.IO.File.Exists(root))
                        root = System.IO.Path.GetDirectoryName(root);
                    else
                        continue;
                }

                var rs = await vm.GetRepositoryRootAsync(root);
                if (rs.IsSuccess)
                {
                    await vm.AddRepositoryAsync(rs.StdOut.Trim(), parent, true, false);
                    refresh = true;
                }
                else if (Models.SafeDirectories.IsUntrustedRepository(rs.StdErr) &&
                         Models.SafeDirectories.TryGetSafeDirectoryValue(root, rs.StdErr, out var safeDirectory) &&
                         App.GetLauncher().ActivePage is { } page)
                {
                    page.Popup = new ViewModels.TrustRepository(page.Node.Id, root, rs.StdErr, safeDirectory, parent, false, false, 0);
                    break;
                }
            }

            if (refresh)
                vm.Refresh();
        }

        /// <summary>Upstream's badges: local changes, ahead and behind, and
        /// the branch beside its icon.</summary>
        private static void ShowStatus(Container status, Models.RepositoryStatus s)
        {
            foreach (var child in status.Children.ToArray())
                child.Destroy();
            if (s == null)
                return;

            if (s.LocalChanges > 0)
                status.Add(new Text(s.LocalChanges.ToString()) { Style = TextStyle.Caption });
            if (s.IsTrackingStatusVisible)
                status.Add(new Text(s.TrackingDescription) { Style = TextStyle.Monospace });
            var branch = new Row { Gap = 2, AlignItems = Align.Center };
            branch.Add(new Icon(Icons.Get("Branch")) { Size = 12 });
            branch.Add(new Text(s.CurrentBranch) { Style = TextStyle.Caption });
            status.Add(branch);
        }

        /// <summary>Upstream's bookmark colours, or the text's for none.</summary>
        public static Color BookmarkColor(int bookmark) =>
            Models.Bookmarks.Get(bookmark) is Avalonia.Media.ISolidColorBrush { Color: var c } ? Color.Rgba(c.R, c.G, c.B, c.A) : Color.Label;

        // Upstream's tree expander: a 9-point triangle, turned down when open,
        // in a button's 16-point icon.
        private static readonly VectorIcon s_closed = new("M4 0L8 4L4 8Z", new ViewBox(-3.11f, -3.11f, 14.22f, 14.22f));
        private static readonly VectorIcon s_open = new("M8 4L4 8L0 4Z", new ViewBox(-3.11f, -3.11f, 14.22f, 14.22f));

    }
}
