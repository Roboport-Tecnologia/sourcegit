using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>ChangeCollectionView</c>: local changes with several
    /// rows selectable, as a list of paths, a grid (the file's name, then
    /// its folder in the secondary colour) or the platform's tree of folders
    /// that open and close. Each change shows its status mark, the
    /// worktree's state in the unstaged list and the index's in the staged
    /// one. All three are a tree: as a list or a grid, no row opens.
    /// </summary>
    public sealed class ChangeCollection
    {
        /// <summary>A row: a change, or a folder of the tree.</summary>
        private sealed record Item(string Key, Models.Change Change, ViewModels.ChangeTreeNode Node);

        private readonly bool _unstaged;
        private readonly Dictionary<string, Item> _byKey = [];
        private readonly Dictionary<ViewModels.ChangeTreeNode, Item> _byNode = [];
        private List<Item> _roots = [];
        private Models.ChangeViewMode _mode = Models.ChangeViewMode.List;
        private bool _compactFolders;
        private List<Models.Change> _changes = [];
        private List<ViewModels.ChangeTreeNode> _tree = [];
        private ViewModels.ChangeSelection _selection = new(null);

        public ChangeCollection(bool unstaged)
        {
            _unstaged = unstaged;
            _list = new TreeView<Item>(item => item.Key, Children, Row, SelectionMode.Multiple, itemAnimations: false) { EstimatedRowHeight = 24 };
            // The user opened or closed folders with the platform's
            // disclosure or keys.
            _list.ExpandedChanged += open =>
            {
                var now = open.ToHashSet();
                foreach (var item in _byNode.Values)
                {
                    if (item.Node.IsFolder)
                        item.Node.IsExpanded = now.Contains(item);
                }
            };
            _list.SelectionChanged += _ =>
            {
                var selection = new ViewModels.ChangeSelection(Selected(_list.SelectedItems));
                if (!selection.IsChanged(_selection))
                    return;
                _selection = selection;
                SelectionChanged?.Invoke(selection);
            };
            // Upstream's double-click, and Return: a folder opens or closes,
            // a change is staged or unstaged.
            _list.Activated += item =>
            {
                if (item.Node is { IsFolder: true } folder)
                    Toggle(folder.FullPath);
                else
                    Activated?.Invoke();
            };
            // Upstream's tree keys, where the platform's tree leaves them
            // (GTK's take Shift): ← closes a folder or goes to its parent, →
            // opens one or goes to its first child.
            _list.OnKey(Shortcut.Of(Key.Left), () => Step(back: true));
            _list.OnKey(Shortcut.Of(Key.Right), () => Step(back: false));
        }

        private readonly TreeView<Item> _list;

        public ListView View => _list;

        /// <summary>The user changed the selection: upstream's binding of
        /// <c>Selection</c> back to the view model.</summary>
        public event Action<ViewModels.ChangeSelection> SelectionChanged;

        /// <summary>A change double-clicked, or Return on one.</summary>
        public event Action Activated;

        /// <summary>The menu for the changes a right-click is about.</summary>
        public Func<ViewModels.ChangeSelection, IReadOnlyList<MenuEntry>> Menu { get; set; }

        public Models.ChangeViewMode Mode
        {
            set
            {
                if (_mode == value)
                    return;
                _mode = value;
                Refresh();
            }
        }

        public bool CompactFolders
        {
            set
            {
                if (_compactFolders == value)
                    return;
                _compactFolders = value;
                if (_mode == Models.ChangeViewMode.Tree)
                    Refresh();
            }
        }

        public List<Models.Change> Changes
        {
            set
            {
                _changes = value ?? [];
                Refresh();
            }
        }

        /// <summary>The view model's selection, shown.</summary>
        public ViewModels.ChangeSelection Selection
        {
            set
            {
                // The list's own selection coming back: shown already. Shown
                // again, a folder picked would give way to its changes.
                if (ReferenceEquals(value, _selection))
                    return;
                _selection = value ?? new(null);
                ShowSelection();
            }
        }

        /// <summary>Upstream's <c>GetNextChangeWithoutSelection</c>: the
        /// change after the selected ones, which keeps the selection's place
        /// once they've left the list.</summary>
        public Models.Change NextWithoutSelection()
        {
            var selected = _selection.Changes;
            if (selected.Count == 0)
                return _changes.Count > 0 ? _changes[0] : null;
            if (selected.Count == _changes.Count)
                return null;

            var set = new HashSet<string>();
            foreach (var c in selected)
            {
                if (!c.IsConflicted)
                    set.Add(c.Path);
            }

            // In the order the rows show them.
            var shown = new List<Models.Change>();
            foreach (var item in Shown())
            {
                if (item.Change != null)
                    shown.Add(item.Change);
            }
            var lastUnselected = -1;
            for (var i = shown.Count - 1; i >= 0; i--)
            {
                if (set.Contains(shown[i].Path))
                {
                    if (lastUnselected == -1)
                        continue;
                    break;
                }
                lastUnselected = i;
            }
            return lastUnselected != -1 ? shown[lastUnselected] : null;
        }

        /// <summary>Upstream's <c>ChangeViewModeSwitcher</c>: a menu button
        /// with the mode's icon, picking list, grid or tree for every list
        /// of changes.</summary>
        public static MenuButton ModeButton()
        {
            var prefs = ViewModels.Preferences.Instance;
            var mode = new MenuButton(App.Text("ChangeDisplayMode")) { IconOnly = true, Style = ButtonStyle.Borderless, ToolTip = App.Text("ChangeDisplayMode") };
            Bind.OneWay(prefs, nameof(prefs.ChangeViewMode), () => prefs.ChangeViewMode, m =>
            {
                mode.Icon = Icons.Get(m.ToString(), 14);
                mode.Menu =
                [
                    new MenuItem(App.Text("ChangeDisplayMode.List"), () => prefs.ChangeViewMode = Models.ChangeViewMode.List)
                    { Checked = m == Models.ChangeViewMode.List, Radio = true },
                    new MenuItem(App.Text("ChangeDisplayMode.Grid"), () => prefs.ChangeViewMode = Models.ChangeViewMode.Grid)
                    { Checked = m == Models.ChangeViewMode.Grid, Radio = true },
                    new MenuItem(App.Text("ChangeDisplayMode.Tree"), () => prefs.ChangeViewMode = Models.ChangeViewMode.Tree)
                    { Checked = m == Models.ChangeViewMode.Tree, Radio = true },
                ];
            }, owner: mode);
            return mode;
        }

        private void Refresh()
        {
            _byKey.Clear();
            _byNode.Clear();
            _roots = [];
            if (_mode == Models.ChangeViewMode.Tree)
            {
                // Folders the user closed stay closed.
                var folded = new HashSet<string>();
                foreach (var node in Folders(_tree))
                {
                    if (!node.IsExpanded)
                        folded.Add(node.FullPath);
                }
                _tree = ViewModels.ChangeTreeNode.Build(_changes, folded, _compactFolders);
                _roots = MakeItems(_tree);
            }
            else
            {
                _tree = [];
                foreach (var c in _changes)
                {
                    var item = new Item(ChangeKey(c), c, null);
                    _byKey[item.Key] = item;
                    _roots.Add(item);
                }
            }
            _list.Roots = _roots;
            ShowOpen();
            ShowSelection();
        }

        /// <summary>The tree's nodes as rows' items, all the way down.</summary>
        private List<Item> MakeItems(List<ViewModels.ChangeTreeNode> nodes)
        {
            var items = new List<Item>();
            foreach (var node in nodes)
            {
                var item = new Item(node.IsFolder ? FolderKey(node) : ChangeKey(node.Change), node.Change, node);
                _byKey[item.Key] = item;
                _byNode[node] = item;
                items.Add(item);
                if (node.IsFolder)
                    MakeItems(node.Children);
            }
            return items;
        }

        private IReadOnlyList<Item> Children(Item item)
        {
            if (item.Node is not { IsFolder: true } folder)
                return null;
            var children = new List<Item>();
            foreach (var node in folder.Children)
            {
                if (_byNode.TryGetValue(node, out var child))
                    children.Add(child);
            }
            return children;
        }

        private static IEnumerable<ViewModels.ChangeTreeNode> Folders(List<ViewModels.ChangeTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;
                yield return node;
                foreach (var folder in Folders(node.Children))
                    yield return folder;
            }
        }

        /// <summary>The tree shows the folders open that are.</summary>
        private void ShowOpen()
        {
            var open = new List<Item>();
            foreach (var node in Folders(_tree))
            {
                if (node.IsExpanded && _byNode.TryGetValue(node, out var item))
                    open.Add(item);
            }
            _list.ExpandedItems = open;
        }

        /// <summary>The rows that show, in order: the top ones, and the
        /// children of open folders.</summary>
        private List<Item> Shown()
        {
            var shown = new List<Item>();
            void Walk(IEnumerable<Item> items)
            {
                foreach (var item in items)
                {
                    shown.Add(item);
                    if (item.Node is { IsFolder: true, IsExpanded: true } && Children(item) is { } children)
                        Walk(children);
                }
            }
            Walk(_roots);
            return shown;
        }

        /// <summary>A row stays while its key does: the key has the view
        /// mode, so the rows change with it, and the state, so a change that
        /// turns from modified to deleted gets its new mark.</summary>
        private string ChangeKey(Models.Change c) =>
            $"{_mode}\n{c.Path}\n{(_unstaged ? c.WorkTree : c.Index)}\n{c.ConflictReason}";

        private static string FolderKey(ViewModels.ChangeTreeNode node) => $"folder\n{node.FullPath}";

        private void ShowSelection()
        {
            var keys = new HashSet<string>();
            foreach (var c in _selection.Changes)
                keys.Add(ChangeKey(c));
            var selected = new List<Item>();
            foreach (var item in _byKey.Values)
            {
                if (item.Change != null && keys.Contains(item.Key))
                    selected.Add(item);
            }
            _list.SelectedItems = selected;
        }

        private static List<object> Selected(IEnumerable<Item> items)
        {
            var selected = new List<object>();
            foreach (var item in items)
                selected.Add(item.Node != null ? item.Node : item.Change);
            return selected;
        }

        /// <summary>Upstream's <c>ToggleNodeIsExpanded</c>: the folder's rows
        /// come or go.</summary>
        private void Toggle(string folder)
        {
            foreach (var node in Folders(_tree))
            {
                if (node.FullPath != folder)
                    continue;
                node.IsExpanded = !node.IsExpanded;
                ShowOpen();
                return;
            }
        }

        private void Step(bool back)
        {
            if (_list.SelectedItems is not [{ Node: { } node }])
                return;
            if (back)
            {
                if (node.IsFolder && node.IsExpanded)
                {
                    Toggle(node.FullPath);
                }
                else
                {
                    // The nearest row above that's less deep.
                    var items = Shown();
                    for (var i = IndexOf(items, node) - 1; i >= 0; i--)
                    {
                        if (items[i].Node is { } parent && parent.Depth < node.Depth)
                        {
                            Pick(items[i]);
                            break;
                        }
                    }
                }
            }
            else if (node.IsFolder)
            {
                if (!node.IsExpanded)
                    Toggle(node.FullPath);
                else if (Shown() is var items && IndexOf(items, node) + 1 is var next && next < items.Count && node.Children.Count > 0)
                    Pick(items[next]);
            }
        }

        private static int IndexOf(List<Item> items, ViewModels.ChangeTreeNode node)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Node == node)
                    return i;
            }
            return -1;
        }

        private void Pick(Item item)
        {
            _list.SelectedItems = [item];
            _list.ScrollTo(item);
            _selection = new ViewModels.ChangeSelection(Selected([item]));
            SelectionChanged?.Invoke(_selection);
        }

        /// <summary>The menu for the row right-clicked, or for the selection
        /// when the row is in it.</summary>
        private IReadOnlyList<MenuEntry> MenuFor(string key)
        {
            if (Menu == null || !_byKey.TryGetValue(key, out var clicked))
                return [];
            var selected = new List<Item>();
            foreach (var item in _list.SelectedItems)
            {
                if (_byKey.TryGetValue(item.Key, out var now))
                    selected.Add(now);
            }
            if (!selected.Contains(clicked))
                selected = [clicked];
            var selection = new ViewModels.ChangeSelection(Selected(selected));
            return selection.Count > 0 ? Menu(selection) : [];
        }

        /// <summary>Upstream's rows, 24 points high, after the platform's
        /// disclosure and indent in the tree. A change: its status mark, a
        /// conflict's marker in bold dark orange, and its path (the list), its
        /// name then its folder (the grid) or its name (the tree). A folder:
        /// its icon in goldenrod, open or closed as it is.</summary>
        private Node Row(Item item)
        {
            var row = new Mitsuamia.Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 0, 0, 4) };
            row.Height = 24;
            var key = item.Key;
            row.ContextMenuOnOpen = () => MenuFor(key);

            if (item.Change is { } change)
            {
                row.Add(new StatusMark(_unstaged ? change.WorkTree : change.Index).View);
                var tip = $"{change.Path} • {(_unstaged ? change.WorkTreeDesc : change.IndexDesc)}";
                if (change.IsConflicted)
                {
                    tip += $" • {change.ConflictDesc}";
                    row.Add(new Text(change.ConflictMarker) { Weight = FontWeight.Bold, Color = s_conflict });
                }
                row.ToolTip = tip;

                if (item.Node != null)
                {
                    row.Add(new Text(item.Node.DisplayName) { MaxLines = 1 });
                }
                else if (_mode == Models.ChangeViewMode.Grid)
                {
                    row.Add(new Text(Path.GetFileName(change.Path)) { MaxLines = 1 });
                    var folder = Path.GetDirectoryName(change.Path);
                    if (!string.IsNullOrEmpty(folder))
                        row.Add(new Text(folder) { MaxLines = 1, Color = Color.SecondaryLabel });
                }
                else
                {
                    row.Add(new Text(change.Path) { MaxLines = 1 });
                }
            }
            else if (item.Node is { } folder)
            {
                var icon = new Icon(Icons.Get("Folder")) { Size = 14, Color = s_folder };
                Bind.OneWay(folder, nameof(folder.IsExpanded), () => folder.IsExpanded,
                    open => icon.Vector = Icons.Get(open ? "Folder.Open" : "Folder"), owner: row);
                row.Add(icon);
                row.Add(new Text(folder.DisplayName) { MaxLines = 1 });
                row.ToolTip = folder.FullPath;
            }
            return row;
        }

        private static readonly Color s_conflict = Color.Rgba(255, 140, 0);
        private static readonly Color s_folder = Color.Rgba(218, 165, 32);
    }
}
