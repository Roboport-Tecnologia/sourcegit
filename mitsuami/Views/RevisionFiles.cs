using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's Files tab of a commit (<c>Views/RevisionFiles.axaml</c>,
    /// <c>RevisionFileTreeView</c>, <c>RevisionFileContentViewer</c>): the
    /// commit's files as a tree whose folders load as they open, a search
    /// with the field's suggestions that shows the file found in its folders,
    /// and beside them the picked file: an image, a binary's size, an LFS
    /// object, a submodule's commit. Text waits for the code view (bemtvi).
    /// </summary>
    public sealed class RevisionFiles
    {
        private readonly ViewModels.CommitDetail _vm;
        private readonly ListView<ViewModels.RevisionFileTreeNode> _list;
        private readonly List<ViewModels.RevisionFileTreeNode> _tree = [];
        private List<ViewModels.RevisionFileTreeNode> _search = [];
        private bool _loaded;

        private RevisionFiles(ViewModels.CommitDetail vm)
        {
            _vm = vm;
            _list = new ListView<ViewModels.RevisionFileTreeNode>(n => n.Backend.Path, Row, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 24,
            };
            _list.SelectionChanged += selected =>
                _ = _vm.ViewRevisionFileAsync(selected is [{ IsFolder: false } node] ? node.Backend : null);
            _list.Activated += node =>
            {
                if (node.IsFolder)
                    _ = Toggle(node);
            };
            // Upstream's tree keys: → opens a folder, ← closes it.
            _list.OnKey(Shortcut.Of(Key.Right), () =>
            {
                if (_list.SelectedItem is { IsFolder: true, IsExpanded: false } node)
                    _ = Toggle(node);
            });
            _list.OnKey(Shortcut.Of(Key.Left), () =>
            {
                if (_list.SelectedItem is { IsFolder: true, IsExpanded: true } node)
                    _ = Toggle(node);
            });
            _list.ContextMenuOnOpen = () => _list.SelectedItem is { } node ? Menu(node.Backend) : [];
        }

        public static Node Create(ViewModels.CommitDetail vm)
        {
            var files = new RevisionFiles(vm);
            var layout = ViewModels.Preferences.Instance.Layout;
            var split = new Split { Position = layout.CommitDetailFilesLeftWidth.IsAbsolute ? layout.CommitDetailFilesLeftWidth.Value : 256 };
            split.Grow = 1;
            split.MinHeight = 0;
            split.Changed += width => layout.CommitDetailFilesLeftWidth = new(width);

            var left = new Column { AlignItems = Align.Stretch, Gap = 4, Padding = new Thickness(4, 0, 4, 4) };
            left.MinWidth = 200;
            left.Add(files.SearchField());
            files._list.Grow = 1;
            files._list.MinHeight = 0;
            left.Add(files._list);
            split.Add(left);
            split.Add(Content(vm));
            // Upstream loads the tree once the tab is shown.
            Bind.OneWay(vm, nameof(vm.ActiveTabIndex), () => vm.ActiveTabIndex, tab =>
            {
                if (tab == 2 && !files._loaded)
                    _ = files.Load();
            }, owner: split);
            return split;
        }

        /// <summary>Upstream's search: suggestions under the field as it's
        /// typed, the platform's way; Return or a pick shows the file.</summary>
        private Node SearchField()
        {
            var field = new SearchInput { Placeholder = App.Text("CommitDetail.Files.Search"), SearchOnReturn = true };
            Bind.OneWay(_vm, nameof(_vm.RevisionFileSearchFilter), () => _vm.RevisionFileSearchFilter, v => field.Value = v ?? "", owner: field);
            field.Changed += text => _vm.RevisionFileSearchFilter = text;
            field.Search += text =>
            {
                _vm.RevisionFileSearchFilter = text;
                _vm.CancelRevisionFileSuggestions();
                _ = ShowFound(text);
            };
            Bind.OneWay(_vm, nameof(_vm.RevisionFileSearchSuggestion), () => _vm.RevisionFileSearchSuggestion, suggestions =>
                field.Suggestions = (suggestions ?? []).Select(p => (Path.GetFileName(p), (string)Path.GetDirectoryName(p))).ToList(), owner: field);
            field.SuggestionChosen += index =>
            {
                if (_vm.RevisionFileSearchSuggestion is not { } suggestions || index >= suggestions.Count)
                    return;
                var path = suggestions[index];
                _vm.RevisionFileSearchFilter = path;
                _vm.CancelRevisionFileSuggestions();
                _ = ShowFound(path);
            };
            return field;
        }

        private async Task Load()
        {
            _loaded = true;
            _tree.Clear();
            if (_vm.Commit != null && await _vm.GetRevisionFilesUnderFolderAsync(null) is { Count: > 0 } objects)
            {
                foreach (var obj in objects)
                    _tree.Add(new ViewModels.RevisionFileTreeNode { Backend = obj });
                Sort(_tree);
            }
            ShowRows(_tree);
        }

        /// <summary>Upstream's search result: the file with its folders
        /// open above it, picked; or the whole tree again.</summary>
        private async Task ShowFound(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                _search = [];
                ShowRows(_tree);
                return;
            }
            if (await _vm.GetRevisionFilesUnderFolderAsync(file) is not [var found])
                return;
            _search = [];
            var last = _search;
            var prefix = string.Empty;
            var routes = file.Split('/');
            for (var i = 0; i < routes.Length - 1; i++)
            {
                var folder = new ViewModels.RevisionFileTreeNode
                {
                    Backend = new Models.Object { Type = Models.ObjectType.Tree, Path = prefix + routes[i] },
                    IsExpanded = true,
                };
                last.Add(folder);
                last = folder.Children;
                prefix = folder.Backend.Path + "/";
            }
            var node = new ViewModels.RevisionFileTreeNode { Backend = found };
            last.Add(node);
            ShowRows(_search);
            _list.SelectedItems = [node];
            _list.ScrollTo(node);
            await _vm.ViewRevisionFileAsync(found);
        }

        private void ShowRows(List<ViewModels.RevisionFileTreeNode> nodes)
        {
            var rows = new List<ViewModels.RevisionFileTreeNode>();
            MakeRows(rows, nodes, 0);
            _list.Items = rows;
        }

        private static void MakeRows(List<ViewModels.RevisionFileTreeNode> rows, List<ViewModels.RevisionFileTreeNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                node.Depth = depth;
                rows.Add(node);
                if (node.IsFolder && node.IsExpanded)
                    MakeRows(rows, node.Children, depth + 1);
            }
        }

        private static void Sort(List<ViewModels.RevisionFileTreeNode> nodes) =>
            nodes.Sort((l, r) => l.IsFolder == r.IsFolder ? Models.NumericSort.Compare(l.Name, r.Name) : l.IsFolder ? -1 : 1);

        /// <summary>Opens or closes a folder, loading what's in it the first
        /// time.</summary>
        private async Task Toggle(ViewModels.RevisionFileTreeNode node)
        {
            node.IsExpanded = !node.IsExpanded;
            if (node.IsExpanded && node.Children.Count == 0
                && await _vm.GetRevisionFilesUnderFolderAsync(node.Backend.Path + "/") is { Count: > 0 } objects)
            {
                foreach (var obj in objects)
                    node.Children.Add(new ViewModels.RevisionFileTreeNode { Backend = obj });
                Sort(node.Children);
            }
            ShowRows(_search.Count > 0 ? _search : _tree);
        }

        /// <summary>Upstream's row: indented by depth, a folder's chevron,
        /// its icon (a folder in goldenrod, a submodule, a file) and name.</summary>
        private Node Row(ViewModels.RevisionFileTreeNode node)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 0, 0, 4) };
            row.Height = 24;
            row.ToolTip = node.Backend.Path;
            for (var level = 0; level < node.Depth; level++)
                row.Add(new Column { Width = Spacing.Indent });
            var expander = new Row { Justify = Justify.Center, AlignItems = Align.Center };
            expander.Width = 16;
            expander.Shrink = 0;
            if (node.IsFolder)
            {
                var button = new Button(node.Name)
                {
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    Icon = node.IsExpanded ? Welcome.s_open : Welcome.s_closed,
                };
                button.Click += () => _ = Toggle(node);
                expander.Add(button);
            }
            row.Add(expander);
            var icon = node.Backend.Type switch
            {
                Models.ObjectType.Blob => new Icon(Icons.Get("File")) { Size = 14 },
                Models.ObjectType.Commit => new Icon(Icons.Get("Submodule")) { Size = 14 },
                _ => new Icon(Icons.Get(node.IsExpanded ? "Folder.Open" : "Folder")) { Size = 14, Color = s_folder },
            };
            row.Add(icon);
            var name = new Text(node.Name) { MaxLines = 1 };
            name.Shrink = 1;
            name.MinWidth = 0;
            row.Add(name);
            return row;
        }

        /// <summary>Upstream's menus: a folder's, and a file's.</summary>
        private IReadOnlyList<MenuEntry> Menu(Models.Object obj)
        {
            if (_vm is not { Repository: { } repo, Commit: { } commit })
                return [];
            var full = Native.OS.GetAbsPath(repo.FullPath, obj.Path);
            if (obj.Type == Models.ObjectType.Tree)
            {
                return
                [
                    new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(full)) { Enabled = Directory.Exists(full) },
                    new MenuItem(App.Text("DirHistories"), () => HistoryWindows.ShowFolder(repo, obj.Path, commit.SHA)),
                    MenuSeparator.Instance,
                    new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(obj.Path)),
                    new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(full)),
                ];
            }

            var blob = obj.Type == Models.ObjectType.Blob;
            var menu = new List<MenuEntry>();
            if (blob)
            {
                var open = new List<MenuEntry> { new MenuItem(App.Text("Open.SystemDefaultEditor"), () => _ = _vm.OpenRevisionFileAsync(obj.Path, null)) };
                if (Native.OS.ExternalTools.Count > 0)
                {
                    open.Add(MenuSeparator.Instance);
                    foreach (var tool in Native.OS.ExternalTools)
                        open.Add(new MenuItem(tool.Name, () => _ = _vm.OpenRevisionFileAsync(obj.Path, tool)));
                }
                menu.Add(new Submenu(App.Text("Open"), open));
            }
            else
            {
                menu.Add(new MenuItem(App.Text("Open"), () => { }) { Enabled = false });
            }
            menu.Add(new MenuItem(App.Text("SaveAs"), async () =>
            {
                if (await Dialogs.OpenFolder(App.MainWindow) is { } folder)
                    await _vm.SaveRevisionFileAsync(obj, Path.Combine(folder, Path.GetFileName(obj.Path)));
            }) { Enabled = blob });
            menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(full)) { Enabled = File.Exists(full) || Directory.Exists(full) });
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("FileHistory"), () => HistoryWindows.ShowFile(repo, obj.Path, commit.SHA)));
            menu.Add(CommitMenu.NotPorted(App.Text("Blame"), "Blame"));
            menu.Add(MenuSeparator.Instance);
            if (!repo.IsBare)
            {
                menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutThisRevision"), () => _ = _vm.ResetToThisRevisionAsync(obj.Path)));
                menu.Add(MenuSeparator.Instance);
                if (repo.Remotes.Count > 0 && File.Exists(full) && repo.IsLFSEnabled())
                {
                    menu.Add(new Submenu(App.Text("GitLFS"), [.. ChangeMenus.LfsLocks(repo, obj.Path)]));
                    menu.Add(MenuSeparator.Instance);
                }
            }
            var actions = repo.GetCustomActions(Models.CustomActionScope.File);
            if (actions.Count > 0)
            {
                var target = new Models.CustomActionTargetFile(obj.Path, commit);
                menu.Add(new Submenu(App.Text("FileCM.CustomAction"),
                    actions.Select(a => (MenuEntry)new MenuItem(a.Item2.Name, () => _ = repo.ExecCustomActionAsync(a.Item1, target))).ToList()));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(obj.Path)));
            menu.Add(new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(full)));
            return menu;
        }

        /// <summary>Upstream's content viewer: the file's path with its open
        /// button over what it is.</summary>
        private static Node Content(ViewModels.CommitDetail vm)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = 4 };
            column.MinWidth = 100;
            var header = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 4) };
            var path = new Text { Style = TextStyle.Caption, MaxLines = 1 };
            path.Grow = 1;
            path.Shrink = 1;
            path.MinWidth = 0;
            header.Add(path);
            var open = new Button(App.Text("Open"))
            {
                Icon = Icons.Get("OpenWith", 12),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Open"),
            };
            open.Click += () =>
            {
                if (vm.CanOpenRevisionFileWithDefaultEditor)
                    _ = vm.OpenRevisionFileAsync(vm.ViewRevisionFilePath, null);
            };
            header.Add(open);
            column.Add(header);
            Bind.OneWay(vm, nameof(vm.ViewRevisionFilePath), () => vm.ViewRevisionFilePath, p =>
            {
                path.Value = p ?? "";
                header.IsVisible = !string.IsNullOrEmpty(p);
            }, owner: header);
            Bind.OneWay(vm, nameof(vm.CanOpenRevisionFileWithDefaultEditor), () => vm.CanOpenRevisionFileWithDefaultEditor, can => open.IsVisible = can, owner: open);
            header.OnKey(Shortcut.Of('o').WithPrimary(), () =>
            {
                if (vm.CanOpenRevisionFileWithDefaultEditor)
                    _ = vm.OpenRevisionFileAsync(vm.ViewRevisionFilePath, null);
            });

            var body = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = 12 };
            body.Grow = 1;
            body.MinHeight = 0;
            column.Add(body);
            Node shown = null;
            Bind.OneWay(vm, nameof(vm.ViewRevisionFileContent), () => vm.ViewRevisionFileContent, content =>
            {
                shown?.Destroy();
                shown = Shown(content);
                body.Add(shown);
            }, owner: body);
            return column;
        }

        internal static Node Shown(object content)
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = 8 };
            column.Grow = 1;
            column.MinHeight = 0;
            column.AlignSelf = Align.Stretch;
            switch (content)
            {
                case Models.RevisionImageFile { Image: { } bitmap } image:
                    column.Add(Picture(bitmap));
                    var info = new Row { Gap = 8, AlignItems = Align.Center };
                    info.Add(new Text(image.ImageType) { Style = TextStyle.Caption, Weight = FontWeight.Bold });
                    info.Add(new Text(image.ImageSize) { Style = TextStyle.Caption });
                    info.Add(new Text(Size(image.FileSize)) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                    column.Add(info);
                    break;
                case Models.RevisionBinaryFile binary:
                    column.Add(new Icon(Icons.Get("Binary")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("File.Binary")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    column.Add(new Text(Size(binary.Size)) { Color = Color.SecondaryLabel });
                    break;
                case Models.RevisionLFSObject lfs:
                    column.Add(new Icon(Icons.Get("LFS")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("CommitDetail.Files.LFS")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    column.Add(new Text(lfs.Object.Oid) { Selectable = true, Color = Color.SecondaryLabel });
                    column.Add(new Text(Size(lfs.Object.Size)) { Color = Color.SecondaryLabel });
                    break;
                case Models.RevisionSubmodule submodule:
                    column.Add(new Icon(Icons.Get("Submodule")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("CommitDetail.Files.Submodule")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    if (submodule.Commit is { } commit)
                    {
                        column.Add(new Text($"{commit.SHA[..10]}  {commit.Subject}") { Selectable = true, MaxLines = 2 });
                        column.Add(new Text($"{commit.Author.Name} • {Models.DateTimeFormat.Format(commit.CommitterTime)}") { Color = Color.SecondaryLabel });
                    }
                    break;
                case Models.RevisionTextFile:
                case ViewModels.RevisionLFSImage:
                    column.Add(new Icon(Icons.Get("File")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text("Viewing a file's text isn't ported to mitsuami yet: it needs the code view.") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                    break;
                default:
                    column.Add(new Icon(Icons.Get("Folder.Open")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("CommitDetail.Files.Tips")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    break;
            }
            return column;
        }

        /// <summary>A picture as large as its pixels on a Retina screen,
        /// shrunk to fit where it is.</summary>
        internal static Image Picture(Avalonia.Media.Imaging.Bitmap bitmap)
        {
            var (w, h) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            var view = new Image();
            view.SetPixels(w, h, Rgba(bitmap), 2);
            view.MaxWidth = w / 2f;
            view.MaxHeight = h / 2f;
            view.Grow = 1;
            view.MinHeight = 0;
            view.MinWidth = 0;
            return view;
        }

        /// <summary>The bitmap's pixels as straight RGBA, as decoded files
        /// are; others are swapped from BGRA.</summary>
        private static byte[] Rgba(Avalonia.Media.Imaging.Bitmap bitmap)
        {
            var pixels = (byte[])bitmap.Pixels.Clone();
            if (bitmap.Format != Avalonia.Platform.PixelFormats.Rgba8888)
            {
                for (var i = 0; i + 3 < pixels.Length; i += 4)
                    (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
            return pixels;
        }

        private static string Size(long bytes) =>
            bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F2} KB",
                < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F2} MB",
                _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
            };

        private static readonly Color s_folder = Color.Rgba(218, 165, 32);
    }
}
