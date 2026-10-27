using System.Collections.Generic;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>BranchTree</c> (<c>Views/BranchTree.axaml</c>): a
    /// repository's local or remote branches as a tree, folders opening and
    /// closing. The platform's tree draws the disclosures and indents the
    /// levels, where upstream's view flattens its nodes into rows and draws
    /// its own; which folders are open is the view model's, as upstream's.
    /// </summary>
    public sealed class BranchTree
    {
        private readonly ViewModels.Repository _repo;
        // A row stays while its key does, and a refresh brings new nodes:
        // its menu and keys act on the node now at its path.
        private readonly Dictionary<string, ViewModels.BranchTreeNode> _byPath = [];

        public BranchTree(ViewModels.Repository repo)
        {
            _repo = repo;
            // Several at once, as upstream's: merging or deleting them.
            View = new TreeView<ViewModels.BranchTreeNode>(node => node.Path, node => node.IsBranch ? null : node.Children,
                Row, SelectionMode.Multiple, itemAnimations: false)
            {
                EstimatedRowHeight = 24,
            };
            // The user opened or closed folders: the repository remembers
            // them, as upstream's toggle does.
            View.ExpandedChanged += open =>
            {
                var now = new HashSet<string>();
                foreach (var node in open)
                    now.Add(node.Path);
                // Taken first: remembering one can refresh the nodes.
                var changed = new List<ViewModels.BranchTreeNode>();
                foreach (var node in _byPath.Values)
                {
                    if (!node.IsBranch && node.IsExpanded != now.Contains(node.Path))
                        changed.Add(node);
                }
                foreach (var node in changed)
                {
                    node.IsExpanded = !node.IsExpanded;
                    _repo.UpdateBranchNodeIsExpanded(node);
                }
            };
            // A branch picked goes to its head in the history, as upstream's.
            View.SelectionChanged += selected =>
            {
                if (selected is [{ Backend: Models.Branch branch }])
                    _repo.NavigateToCommit(branch.Head);
            };
            // Upstream's double-click: a branch is checked out (unless it's
            // the current one), a folder opens or closes.
            View.Activated += async node =>
            {
                if (node.Backend is Models.Branch branch)
                {
                    if (!branch.IsCurrent && _repo.Settings != null)
                        await _repo.CheckoutBranchAsync(branch);
                }
                else
                {
                    Toggle(node);
                }
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

        public TreeView<ViewModels.BranchTreeNode> View { get; }

        /// <summary>The tree's top nodes, from the view model, and the
        /// folders it has open.</summary>
        public List<ViewModels.BranchTreeNode> Nodes
        {
            set
            {
                var nodes = value ?? [];
                _byPath.Clear();
                var open = new List<ViewModels.BranchTreeNode>();
                Collect(nodes, open);
                View.Roots = nodes;
                View.ExpandedItems = open;
            }
        }

        /// <summary>Upstream's <c>ToggleNodeIsExpanded</c>: the folder opens
        /// or closes, and the repository remembers it.</summary>
        public void Toggle(ViewModels.BranchTreeNode node)
        {
            node.IsExpanded = !node.IsExpanded;
            _repo.UpdateBranchNodeIsExpanded(node);
            var open = new List<ViewModels.BranchTreeNode>();
            foreach (var n in _byPath.Values)
            {
                if (!n.IsBranch && n.IsExpanded)
                    open.Add(n);
            }
            View.ExpandedItems = open;
        }

        /// <summary>Every node by its path, and the open folders.</summary>
        private void Collect(List<ViewModels.BranchTreeNode> nodes, List<ViewModels.BranchTreeNode> open)
        {
            foreach (var node in nodes)
            {
                _byPath[node.Path] = node;
                if (node.IsBranch)
                    continue;
                if (node.IsExpanded)
                    open.Add(node);
                Collect(node.Children, open);
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

        /// <summary>Upstream's row, after the platform's disclosure and
        /// indent, as high as its content; the node's icon in an 18-point slot (a
        /// remote, a branch, the current branch's check in green, a
        /// worktree's branch in cyan, a folder open or closed); the name,
        /// bold for the current branch, and a folder's count in the secondary
        /// colour; then a branch's gone upstream and tracking status at the
        /// end.</summary>
        private Node Row(ViewModels.BranchTreeNode node)
        {
            var row = new Row { AlignItems = Align.Center, Padding = Metrics.ListRow() };
            var path = node.Path;
            row.ContextMenuOnOpen = () => Menu(path);

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

            var name = new Text(node.Name) { MaxLines = 1 };
            name.Shrink = 1;
            name.MinWidth = 0;
            if (node.IsCurrent)
                name.Weight = FontWeight.Bold;
            row.Add(name);
            if (node.Counter > 0)
            {
                var count = new Text(node.BranchesCount) { Color = Color.SecondaryLabel };
                count.Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Xs);
                count.Shrink = 0;
                row.Add(count);
            }
            var gap = new Row();
            gap.Grow = 1;
            row.Add(gap);

            // Upstream's mark for a branch whose upstream is gone, and how
            // far ahead and behind it the branch is, on a pill.
            if (node.ShowUpstreamGoneTip)
            {
                var gone = new Icon(Icons.Get("Error")) { Size = 12, Color = Color.Warning };
                gone.Margin = new Thickness(Spacing.None, Spacing.Sm);
                row.Add(gone);
            }
            if (node.Backend is Models.Branch { TrackStatusDescription: { Length: > 0 } track })
            {
                var status = new Row { AlignItems = Align.Center, Padding = new Thickness(Spacing.None, Spacing.Md) };
                status.Height = 18;
                status.Shrink = 0;
                status.Margin = new Thickness(Spacing.None, Spacing.Xs, Spacing.None, Spacing.Sm);
                status.Add(new TrackPill { Inset = new Thickness(0) });
                status.Add(new Text(track) { Style = TextStyle.Caption, MaxLines = 1 });
                row.Add(status);
            }
            row.Add(FilterModeSwitch.Create(row, node, () => node.FilterMode, mode => _repo.SetBranchFilterMode(node, mode, false, true)));
            return row;

            static Mitsuamia.Row Slot(float width)
            {
                var slot = new Mitsuamia.Row { AlignItems = Align.Center, Justify = Justify.Center };
                slot.Width = width;
                slot.Shrink = 0;
                return slot;
            }
        }

        /// <summary>Behind the tracking status: a pill in a grey that
        /// reads in either appearance, as the stashes' branch chips.</summary>
        private sealed class TrackPill() : DrawnWidget("TrackPill", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.Rgba(128, 128, 128, 56), canvas.Height / 2);
        }
    }
}
