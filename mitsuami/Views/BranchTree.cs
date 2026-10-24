using System.Collections.Generic;
using System.Collections.ObjectModel;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>BranchTree</c> (<c>Views/BranchTree.axaml</c>): a
    /// repository's local or remote branches as a tree in a list, folders
    /// opening and closing, each row indented a level per folder it's in.
    /// Upstream's view flattens the view model's nodes into rows itself, and
    /// so does this.
    /// </summary>
    public sealed class BranchTree
    {
        private readonly ViewModels.Repository _repo;
        private List<ViewModels.BranchTreeNode> _nodes = [];
        // A row stays while its key does, and a refresh brings new nodes:
        // its menu and keys act on the node now at its path.
        private readonly Dictionary<string, ViewModels.BranchTreeNode> _byPath = [];

        public BranchTree(ViewModels.Repository repo)
        {
            _repo = repo;
            // Several at once, as upstream's: merging or deleting them.
            View = new ListView<ViewModels.BranchTreeNode>(node => node.Path, Row, SelectionMode.Multiple, itemAnimations: false)
            {
                EstimatedRowHeight = 24,
                Items = Rows,
            };
            // A branch picked goes to its head in the history, as upstream's.
            View.SelectionChanged += selected =>
            {
                if (selected is [{ Backend: Models.Branch branch }])
                    _repo.NavigateToCommit(branch.Head);
            };
            View.Activated += node =>
            {
                if (!node.IsBranch)
                    Toggle(node);
            };
            // Upstream's keys: Delete (and Backspace, the Mac's) deletes the
            // branches or the remote, F2 renames a local branch.
            View.OnKey(Shortcut.Of(Key.Delete), Delete);
            View.OnKey(Shortcut.Of(Key.Backspace), Delete);
            View.OnKey(Shortcut.Function(2), () =>
            {
                if (Current(View.SelectedItems) is [{ Backend: Models.Branch { IsLocal: true } branch }] && _repo.CanCreatePopup())
                    _repo.ShowPopup(new ViewModels.RenameBranch(_repo, branch));
            });
        }

        public ListView<ViewModels.BranchTreeNode> View { get; }

        public ObservableCollection<ViewModels.BranchTreeNode> Rows { get; } = [];

        /// <summary>The tree's top nodes, from the view model.</summary>
        public List<ViewModels.BranchTreeNode> Nodes
        {
            set
            {
                _nodes = value ?? [];
                Refresh();
            }
        }

        /// <summary>Upstream's <c>ToggleNodeIsExpanded</c>: the folder's rows
        /// come or go, and the repository remembers it.</summary>
        public void Toggle(ViewModels.BranchTreeNode node)
        {
            node.IsExpanded = !node.IsExpanded;
            _repo.UpdateBranchNodeIsExpanded(node);
            Refresh();
        }

        private void Refresh()
        {
            var rows = new List<ViewModels.BranchTreeNode>();
            MakeRows(rows, _nodes, 0);
            Rows.Clear();
            _byPath.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
                _byPath[row.Path] = row;
            }
        }

        private List<ViewModels.BranchTreeNode> Current(IEnumerable<ViewModels.BranchTreeNode> nodes)
        {
            var current = new List<ViewModels.BranchTreeNode>();
            foreach (var node in nodes)
            {
                if (_byPath.TryGetValue(node.Path, out var now))
                    current.Add(now);
            }
            return current;
        }

        /// <summary>The menu for the row right-clicked, or for the selection
        /// when the row is in it.</summary>
        private IReadOnlyList<MenuEntry> Menu(string path)
        {
            var selected = Current(View.SelectedItems);
            if (selected.Find(n => n.Path == path) == null)
                selected = _byPath.TryGetValue(path, out var clicked) ? [clicked] : [];
            return SidebarMenus.Branches(_repo, selected);
        }

        /// <summary>Upstream's <c>OnTreeKeyDown</c>: a remote's deletion, or
        /// the branches', but never the current one's.</summary>
        private void Delete()
        {
            var selected = Current(View.SelectedItems);
            if (_repo.Settings == null || selected.Count == 0)
                return;
            if (selected is [{ Backend: Models.Remote remote }])
            {
                _repo.DeleteRemote(remote);
                return;
            }
            var branches = new List<Models.Branch>();
            foreach (var node in selected)
                SidebarMenus.Collect(branches, node);
            if (branches.Count == 0 || branches.Find(x => x.IsCurrent) != null)
                return;
            if (branches.Count == 1)
                _repo.DeleteBranch(branches[0]);
            else
                _repo.DeleteMultipleBranches(branches, branches[0].IsLocal);
        }

        private static void MakeRows(List<ViewModels.BranchTreeNode> rows, List<ViewModels.BranchTreeNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                node.Depth = depth;
                rows.Add(node);
                if (node.IsExpanded && !node.IsBranch)
                    MakeRows(rows, node.Children, depth + 1);
            }
        }

        /// <summary>Upstream's row: 24 points high, indented by the
        /// platform's tree indentation per level; an expander for folders in
        /// a 16-point slot; the node's icon in an 18-point one (a remote, a
        /// branch, the current branch's check in green, a worktree's branch
        /// in cyan, a folder open or closed); the name, bold for the current
        /// branch, and a folder's count in the secondary colour.</summary>
        private Node Row(ViewModels.BranchTreeNode node)
        {
            var row = new Row { AlignItems = Align.Center };
            row.Height = 24;
            var path = node.Path;
            row.ContextMenuOnOpen = () => Menu(path);
            for (var level = 0; level < node.Depth; level++)
                row.Add(new Column { Width = Spacing.Indent });

            var expander = Slot(16);
            if (!node.IsBranch)
            {
                var button = new Button(node.Name) { IconOnly = true, Style = ButtonStyle.Borderless };
                Bind.OneWay(node, nameof(node.IsExpanded), () => node.IsExpanded,
                    open => button.Icon = open ? Welcome.s_open : Welcome.s_closed, owner: row);
                button.Click += () => Toggle(node);
                expander.Add(button);
            }
            row.Add(expander);

            var mark = Slot(18);
            switch (node.Backend)
            {
                case Models.Remote:
                    mark.Add(new Icon(Icons.Get("Remote")) { Size = 12 });
                    break;
                case Models.Branch { IsCurrent: true }:
                    mark.Add(new Icon(Icons.Get("CheckCircled")) { Size = 12, Color = Color.Rgba(0, 128, 0) });
                    break;
                case Models.Branch { IsLocal: true } branch when !string.IsNullOrEmpty(branch.WorktreePath):
                    mark.Add(new Icon(Icons.Get("Branch")) { Size = 12, Color = Color.Rgba(0, 139, 139) });
                    break;
                case Models.Branch:
                    mark.Add(new Icon(Icons.Get("Branch")) { Size = 12 });
                    break;
                default:
                    // Open or closed as the folder is: a row stays as it opens.
                    var open = new Icon(Icons.Get("Folder.Open")) { Size = 12 };
                    var closed = new Icon(Icons.Get("Folder")) { Size = 12 };
                    Bind.OneWay(node, nameof(node.IsExpanded), () => node.IsExpanded, o =>
                    {
                        open.IsVisible = o;
                        closed.IsVisible = !o;
                    }, owner: row);
                    mark.Add(open);
                    mark.Add(closed);
                    break;
            }
            row.Add(mark);

            var name = new Text(node.Name);
            if (node.IsCurrent)
                name.Weight = FontWeight.Bold;
            row.Add(name);
            if (node.Counter > 0)
            {
                var count = new Text(node.BranchesCount) { Color = Color.SecondaryLabel };
                count.Margin = new Thickness(0, 0, 0, 4);
                row.Add(count);
            }
            return row;

            static Mitsuamia.Row Slot(float width)
            {
                var slot = new Mitsuamia.Row { AlignItems = Align.Center, Justify = Justify.Center };
                slot.Width = width;
                return slot;
            }
        }
    }
}
