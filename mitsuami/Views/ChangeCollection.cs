using System;
using System.Collections.Generic;
using System.IO;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>ChangeCollectionView</c>: local changes in a list with
    /// several rows selectable, as a list of paths, a grid (the file's name,
    /// then its folder in the secondary colour) or a tree of folders that
    /// open and close. Each change shows its status mark, the worktree's
    /// state in the unstaged list and the index's in the staged one.
    /// </summary>
    public sealed class ChangeCollection
    {
        /// <summary>A row: a change, or a folder of the tree.</summary>
        private sealed record Item(string Key, Models.Change Change, ViewModels.ChangeTreeNode Node);

        private readonly bool _unstaged;
        private readonly Dictionary<string, Item> _byKey = [];
        private Models.ChangeViewMode _mode = Models.ChangeViewMode.List;
        private bool _compactFolders;
        private List<Models.Change> _changes = [];
        private List<ViewModels.ChangeTreeNode> _tree = [];
        private ViewModels.ChangeSelection _selection = new(null);

        public ChangeCollection(bool unstaged)
        {
            _unstaged = unstaged;
            _list = new ListView<Item>(item => item.Key, Row, SelectionMode.Multiple, itemAnimations: false) { EstimatedRowHeight = 24 };
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
            // Upstream's tree keys: ← closes a folder or goes to its parent,
            // → opens one or goes to its first child.
            _list.OnKey(Shortcut.Of(Key.Left), () => Step(back: true));
            _list.OnKey(Shortcut.Of(Key.Right), () => Step(back: false));
        }

        private readonly ListView<Item> _list;

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
            foreach (var item in _list.Items)
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

        private void Refresh()
        {
            var items = new List<Item>();
            if (_mode == Models.ChangeViewMode.Tree)
            {
                // Folders the user closed stay closed.
                var folded = new HashSet<string>();
                foreach (var item in _byKey.Values)
                {
                    if (item.Node is { IsFolder: true, IsExpanded: false } node)
                        folded.Add(node.FullPath);
                }
                _tree = ViewModels.ChangeTreeNode.Build(_changes, folded, _compactFolders);
                MakeRows(items, _tree);
            }
            else
            {
                foreach (var c in _changes)
                    items.Add(new Item(ChangeKey(c), c, null));
            }

            _byKey.Clear();
            foreach (var item in items)
                _byKey[item.Key] = item;
            _list.Items = items;
            ShowSelection();
        }

        private void MakeRows(List<Item> rows, List<ViewModels.ChangeTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(new Item(node.IsFolder ? FolderKey(node) : ChangeKey(node.Change), node.Change, node));
                if (node.IsFolder && node.IsExpanded)
                    MakeRows(rows, node.Children);
            }
        }

        /// <summary>A row stays while its key does: the key has the view
        /// mode, so the rows change with it, and the state, so a change that
        /// turns from modified to deleted gets its new mark.</summary>
        private string ChangeKey(Models.Change c) =>
            $"{_mode}\n{c.Path}\n{(_unstaged ? c.WorkTree : c.Index)}\n{c.ConflictReason}";

        /// <summary>A folder's row shows whether it's open.</summary>
        private static string FolderKey(ViewModels.ChangeTreeNode node) => $"{(node.IsExpanded ? "open" : "closed")}\n{node.FullPath}";

        private void ShowSelection()
        {
            var keys = new HashSet<string>();
            foreach (var c in _selection.Changes)
                keys.Add(ChangeKey(c));
            var selected = new List<Item>();
            foreach (var item in _list.Items)
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
            Item toggled = null;
            foreach (var item in _byKey.Values)
            {
                if (item.Node is { IsFolder: true } n && n.FullPath == folder)
                    toggled = item;
            }
            if (toggled == null)
                return;
            // The rows selected stay so, the folder too under its new key.
            var selected = new HashSet<string>();
            foreach (var item in _list.SelectedItems)
                selected.Add(item.Node is { IsFolder: true } n ? n.FullPath : item.Key);
            toggled.Node.IsExpanded = !toggled.Node.IsExpanded;

            var items = new List<Item>();
            MakeRows(items, _tree);
            _byKey.Clear();
            var reselected = new List<Item>();
            foreach (var item in items)
            {
                _byKey[item.Key] = item;
                if (selected.Contains(item.Node is { IsFolder: true } n ? n.FullPath : item.Key))
                    reselected.Add(item);
            }
            _list.Items = items;
            _list.SelectedItems = reselected;
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
                    var items = _list.Items;
                    for (var i = IndexOf(node) - 1; i >= 0; i--)
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
                else if (IndexOf(node) + 1 is var next && next < _list.Items.Count && node.Children.Count > 0)
                    Pick(_list.Items[next]);
            }
        }

        private int IndexOf(ViewModels.ChangeTreeNode node)
        {
            var items = _list.Items;
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

        /// <summary>Upstream's rows, 24 points high. A change: its status
        /// mark, a conflict's marker in bold dark orange, and its path (the
        /// list), its name then its folder (the grid) or its name (the tree,
        /// indented a level per folder). A folder: an expander and the
        /// folder's icon in goldenrod.</summary>
        private Node Row(Item item)
        {
            var row = new Mitsuamia.Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 0, 0, 4) };
            row.Height = 24;
            var key = item.Key;
            row.ContextMenuOnOpen = () => MenuFor(key);

            if (item.Node is { } node)
            {
                for (var level = 0; level < node.Depth; level++)
                    row.Add(new Column { Width = Spacing.Indent });
                var expander = Slot(16);
                if (node.IsFolder)
                {
                    var button = new Button(node.DisplayName)
                    {
                        IconOnly = true,
                        Style = ButtonStyle.Borderless,
                        Icon = node.IsExpanded ? Welcome.s_open : Welcome.s_closed,
                    };
                    var path = node.FullPath;
                    button.Click += () => Toggle(path);
                    expander.Add(button);
                }
                row.Add(expander);
            }

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
                row.Add(new Icon(Icons.Get(folder.IsExpanded ? "Folder.Open" : "Folder")) { Size = 14, Color = s_folder });
                row.Add(new Text(folder.DisplayName) { MaxLines = 1 });
                row.ToolTip = folder.FullPath;
            }
            return row;

            static Mitsuamia.Row Slot(float width)
            {
                var slot = new Mitsuamia.Row { AlignItems = Align.Center, Justify = Justify.Center };
                slot.Width = width;
                return slot;
            }
        }

        private static readonly Color s_conflict = Color.Rgba(255, 140, 0);
        private static readonly Color s_folder = Color.Rgba(218, 165, 32);
    }
}
