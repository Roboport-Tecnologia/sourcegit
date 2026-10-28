using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's comparison of two commits (<c>Views/RevisionCompare.axaml</c>),
    /// in place of the details when two are selected: each side (a commit's
    /// author, HEAD mark, SHA to go to, time and subject, or the worktree),
    /// the button that swaps them, saving the difference as a patch, and the
    /// changes between them beside their diff. Each side is a card, red for
    /// the start and green for the end as upstream's are, and the two share
    /// the row evenly, so swapping or a long subject doesn't move the button.
    /// </summary>
    public static class RevisionCompare
    {
        public static Node Create(ViewModels.RevisionCompare vm)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm };
            column.Grow = 1;
            column.MinHeight = 0;

            var header = new Row { AlignItems = Align.Stretch, Gap = Spacing.Sm, Padding = new Thickness(Spacing.None, Spacing.Xs) };
            var start = Side(vm, nameof(vm.StartPoint), () => vm.StartPoint, Color.Error);
            header.Add(start);
            var swap = new Button(App.Text("Diff.SwapCommits"))
            {
                Icon = Icons.Get("Compare", 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Diff.SwapCommits"),
            };
            swap.AlignSelf = Align.Center;
            swap.Click += vm.Swap;
            header.Add(swap);
            header.Add(Side(vm, nameof(vm.EndPoint), () => vm.EndPoint, Color.Success));
            var patch = new Button(App.Text("Diff.SaveAsPatch"))
            {
                Icon = Icons.Get("Save", 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Diff.SaveAsPatch"),
            };
            patch.Click += async () =>
            {
                if (await Dialogs.SaveFile(App.MainWindow, App.Text("FileCM.SaveAsPatch"), "changes.patch") is { } file)
                    await vm.SaveChangesAsPatchAsync(null, file);
            };
            patch.AlignSelf = Align.Center;
            Bind.OneWay(vm, nameof(vm.CanSaveAsPatch), () => vm.CanSaveAsPatch, can => patch.IsVisible = can, owner: patch);
            header.Add(patch);
            column.Add(header);

            column.Add(CommitChanges.Pane(vm, vm.GetAbsPath,
                nameof(vm.SearchFilter), () => vm.SearchFilter, v => vm.SearchFilter = v,
                nameof(vm.VisibleChanges), () => vm.VisibleChanges,
                nameof(vm.ChangeSelection), () => vm.ChangeSelection, s => vm.ChangeSelection = s,
                nameof(vm.TotalChanges), () => vm.TotalChanges,
                nameof(vm.DiffContext), () => vm.DiffContext,
                s => Menu(vm, s),
                nameof(vm.IsLoading), () => vm.IsLoading));
            return column;
        }

        /// <summary>A compared side's card, outlined in
        /// <paramref name="color"/>, around <paramref name="content"/>. It
        /// starts from no width, so both sides get half the room whatever
        /// they hold. Put the sides in a row that stretches its items, so
        /// they're one height too.</summary>
        public static Node Card(Color color, Node content)
        {
            var card = new Column { AlignItems = Align.Stretch, Justify = Justify.Center, Padding = new Thickness(Spacing.Xs, Spacing.Sm) };
            card.Grow = 1;
            card.Basis = 0;
            card.MinWidth = 0;
            card.Add(new Outline(color) { Inset = new Thickness(0) });
            content.MinWidth = 0;
            card.Add(content);
            return card;
        }

        /// <summary>One side: a commit, or upstream's worktree chip.</summary>
        private static Node Side(ViewModels.RevisionCompare vm, string property, System.Func<object> point, Color color)
        {
            var holder = new Column { AlignItems = Align.Stretch };
            Bind.OneWay(vm, property, point, value =>
            {
                foreach (var child in holder.Children.ToArray())
                    child.Destroy();
                holder.Add(value is Models.Commit commit ? Commit(vm, commit) : Worktree());
            }, owner: holder);
            return Card(color, holder);
        }

        private static Node Worktree()
        {
            var row = new Row { AlignItems = Align.Center, Justify = Justify.Center };
            row.Add(new Text(App.Text("Worktree")) { Weight = FontWeight.Bold });
            return row;
        }

        private static Node Commit(ViewModels.RevisionCompare vm, Models.Commit commit)
        {
            var column = new Column { Gap = Spacing.Xs };
            column.MinWidth = 0;
            var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            var avatar = new Avatar(16, menu: false) { User = commit.Author };
            top.Add(avatar.View);
            var author = new Text(commit.Author.Name) { MaxLines = 1 };
            author.Shrink = 1;
            author.MinWidth = 0;
            top.Add(author);
            if (commit.IsCurrentHead)
                top.Add(new Text("HEAD") { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.Accent });
            var sha = new Row();
            sha.Add(new Text(commit.SHA[..10]) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
            sha.Clicked += () => vm.NavigateTo(commit.SHA);
            sha.ToolTip = commit.SHA;
            top.Add(sha);
            top.Add(new Text(Models.DateTimeFormat.Format(commit.CommitterTime)) { Color = Color.SecondaryLabel });
            column.Add(top);
            column.Add(new Text(commit.Subject) { MaxLines = 1 });
            return column;
        }

        /// <summary>A card's fill and its side's outline, upstream's 4pt
        /// corners.</summary>
        private sealed class Outline(Color color) : DrawnWidget("CompareSide", 0, 0)
        {
            protected override void Draw(Canvas canvas)
            {
                canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.ControlBackground, 4);
                canvas.StrokeRect(0.5f, 0.5f, canvas.Width - 1, canvas.Height - 1, color, 1, 4);
            }
        }

        /// <summary>Upstream's menus: saving as a patch; for one change or
        /// folder the diff tool and revealing it; putting files back as
        /// either side has them; copying paths.</summary>
        private static IReadOnlyList<MenuEntry> Menu(ViewModels.RevisionCompare vm, ViewModels.ChangeSelection selection)
        {
            if (selection.Count == 0)
                return [];
            var changes = selection.Changes;
            var menu = new List<MenuEntry>
            {
                new MenuItem(App.Text("FileCM.SaveAsPatch"), async () =>
                {
                    if (await Dialogs.SaveFile(App.MainWindow, App.Text("FileCM.SaveAsPatch"), "changes.patch") is { } file)
                        await vm.SaveChangesAsPatchAsync(changes, file);
                }),
                MenuSeparator.Instance,
            };
            if (selection.Count == 1)
            {
                var folder = selection.IsSingleFolder ? vm.GetAbsPath(selection.SingleFolderPath) : null;
                if (!selection.HasFolder)
                {
                    var change = changes[0];
                    menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.OpenChangeWithExternalDiffTool(change)));
                }
                if (!selection.HasFolder || selection.IsSingleFolder)
                {
                    var path = folder ?? vm.GetAbsPath(changes[0].Path);
                    menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(path))
                    { Enabled = folder != null ? Directory.Exists(path) : File.Exists(path) });
                }
                menu.Add(MenuSeparator.Instance);
            }
            if (changes.Count == 1 && !selection.HasFolder)
            {
                var change = changes[0];
                menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.LeftSideDesc), () => _ = vm.ResetToLeftAsync(change)) { Enabled = vm.CanResetToLeft });
                menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.RightSideDesc), () => _ = vm.ResetToRightAsync(change)) { Enabled = vm.CanResetToRight });
            }
            else
            {
                menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.LeftSideDesc), () => _ = vm.ResetMultipleToLeftAsync(changes)) { Enabled = vm.CanResetToLeft });
                menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.RightSideDesc), () => _ = vm.ResetMultipleToRightAsync(changes)) { Enabled = vm.CanResetToRight });
            }
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("CopyPath"), () => ChangeMenus.CopyPaths(vm.GetAbsPath, selection, false)));
            menu.Add(new MenuItem(App.Text("CopyFullPath"), () => ChangeMenus.CopyPaths(vm.GetAbsPath, selection, true)));
            return menu;
        }
    }
}
