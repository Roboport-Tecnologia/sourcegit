using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's comparison of two branches or tags (<c>Views/Compare.axaml</c>),
    /// a window of its own, 1280 × 720 as upstream's least size: each side's
    /// head (author, name, SHA to go to, time, subject), swapping them, and
    /// tabs for upstream's switch: the changes between them beside their
    /// diff, or the commits only one side has (picking one goes to it in the
    /// history; with HEAD on the other side they can be cherry-picked).
    /// </summary>
    public static class Compare
    {
        /// <summary>The window last opened, while it's open: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo, object based, object to) =>
            Show(repo, new ViewModels.Compare(repo, based, to));

        /// <summary>A comparison already made: a palette's pick.</summary>
        public static void Show(ViewModels.Repository repo, ViewModels.Compare vm)
        {
            var window = Shown = new Window(App.Text("Compare"), 1280, 720);
            window.Closed += () =>
            {
                if (Shown == window)
                    Shown = null;
            };

            var column = new Column { Padding = Metrics.Panel, Gap = Metrics.PanelGap, AlignItems = Align.Stretch };
            column.Grow = 1;
            var header = new Row { AlignItems = Align.Center, Gap = Spacing.Md };
            header.Add(Side(vm, nameof(vm.BaseHead), () => vm.BaseHead, () => vm.BaseName));
            var swap = new Button(App.Text("Diff.SwapCommits"))
            {
                Icon = Icons.Get("Compare", 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Diff.SwapCommits"),
            };
            swap.Click += vm.Swap;
            Bind.OneWay(vm, nameof(vm.IsLoadingChanges), () => !vm.IsLoadingChanges && !vm.IsLoadingPickableCommits,
                ready => swap.IsEnabled = ready, owner: swap);
            Bind.OneWay(vm, nameof(vm.IsLoadingPickableCommits), () => !vm.IsLoadingChanges && !vm.IsLoadingPickableCommits,
                ready => swap.IsEnabled = ready, owner: swap);
            header.Add(swap);
            header.Add(Side(vm, nameof(vm.ToHead), () => vm.ToHead, () => vm.ToName));
            column.Add(header);

            var tabs = new Tabs();
            tabs.Grow = 1;
            tabs.MinHeight = 0;
            var changes = new Column { AlignItems = Align.Stretch, Padding = Spacing.Sm };
            changes.Add(CommitChanges.Pane(vm, vm.GetAbsPath,
                nameof(vm.SearchFilter), () => vm.SearchFilter, v => vm.SearchFilter = v,
                nameof(vm.VisibleChanges), () => vm.VisibleChanges,
                nameof(vm.ChangeSelection), () => vm.ChangeSelection, s => vm.ChangeSelection = s,
                nameof(vm.TotalChanges), () => vm.TotalChanges,
                nameof(vm.DiffContext), () => vm.DiffContext,
                s => Menu(vm, s),
                nameof(vm.IsLoadingChanges), () => vm.IsLoadingChanges));
            tabs.Add(Popups.ButtonText("Compare.Changes"), changes);
            tabs.Add(Popups.ButtonText("Compare.Commits"), Commits(vm));
            Bind.OneWay(vm, nameof(vm.IsViewChanges), () => vm.IsViewChanges, on => tabs.SelectedIndex = on ? 0 : 1, owner: tabs);
            tabs.Changed += index => vm.IsViewChanges = index == 0;
            column.Add(tabs);
            window.Content = column;
        }

        /// <summary>A side's head: its author's avatar and name, the branch
        /// or tag's name, its SHA (a click goes to it), time and subject.</summary>
        private static Node Side(ViewModels.Compare vm, string property, Func<Models.Commit> head, Func<string> name)
        {
            var holder = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            holder.Grow = 1;
            holder.MinWidth = 0;
            Bind.OneWay(vm, property, head, commit =>
            {
                foreach (var child in holder.Children.ToArray())
                    child.Destroy();
                if (commit == null)
                    return;
                var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
                top.Add(new Avatar(16, menu: false) { User = commit.Author }.View);
                var author = new Text(commit.Author.Name) { MaxLines = 1 };
                author.Shrink = 1;
                author.MinWidth = 0;
                top.Add(author);
                top.Add(new Text(name()) { Weight = FontWeight.Bold, Color = Color.Accent, MaxLines = 1 });
                var sha = new Row();
                sha.Add(new Text(commit.SHA[..10]) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
                sha.Clicked += () => vm.NavigateTo(commit.SHA);
                sha.ToolTip = commit.SHA;
                top.Add(sha);
                top.Add(new Text(Models.DateTimeFormat.Format(commit.CommitterTime)) { Color = Color.SecondaryLabel });
                holder.Add(top);
                holder.Add(new Text(commit.Subject) { MaxLines = 1 });
            }, owner: holder);
            return holder;
        }

        /// <summary>Upstream's commits page: the commits only the left side
        /// has, then the right side's, and the tip about cherry-picking.</summary>
        private static Node Commits(ViewModels.Compare vm)
        {
            var page = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm, Padding = Spacing.Sm };
            page.Grow = 1;
            Add("Compare.Commits.LeftOnly", nameof(vm.LeftOnlyCommits), () => vm.LeftOnlyCommits, () => vm.BaseName, () => vm.BaseHead);
            Add("Compare.Commits.RightOnly", nameof(vm.RightOnlyCommits), () => vm.RightOnlyCommits, () => vm.ToName, () => vm.ToHead);
            page.Add(new Text(App.Text("Compare.Commits.Tips")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            return page;

            void Add(string title, string property, Func<List<Models.Commit>> commits, Func<string> name, Func<Models.Commit> side)
            {
                var heading = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
                heading.Add(new Text(Popups.ButtonText(title)) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                var named = new Text { Weight = FontWeight.Bold };
                Bind.OneWay(vm, nameof(vm.BaseName), name, n => named.Value = side()?.IsCurrentHead == true ? $"✓ {n}" : n, owner: named);
                heading.Add(named);
                page.Add(heading);
                var list = new ListView<Models.Commit>(c => c.SHA, Row, SelectionMode.Multiple, itemAnimations: false)
                {
                    Style = ListStyle.Framed,
                    EstimatedRowHeight = 28,
                };
                list.Grow = 1;
                list.MinHeight = 0;
                Bind.OneWay(vm, property, commits, c => list.Items = c ?? [], owner: list);
                list.SelectionChanged += selected =>
                {
                    if (selected is [var commit])
                        vm.NavigateTo(commit.SHA);
                };
                list.ContextMenuOnOpen = () => CommitMenu(vm, list.SelectedItems.ToList(), side());
                list.OnKey(Shortcut.Of('c').WithPrimary(), () => Clipboard.SetText(Lines(list.SelectedItems.ToList())));
                var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
                area.Grow = 1;
                area.MinHeight = 0;
                area.Add(list);
                var spinner = new Spinner { AlignSelf = Align.Center };
                area.Add(spinner);
                Bind.OneWay(vm, nameof(vm.IsLoadingPickableCommits), () => vm.IsLoadingPickableCommits, busy =>
                {
                    spinner.IsVisible = busy;
                    list.IsVisible = !busy;
                }, owner: spinner);
                page.Add(area);
            }
        }

        private static Node Row(Models.Commit commit)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Metrics.RowGap, Padding = Metrics.ListRow(Spacing.Xs, Spacing.Xs) };
            row.Add(new Avatar(16, menu: false) { User = commit.Author }.View);
            var subject = new Text(commit.Subject) { MaxLines = 1 };
            subject.Grow = 1;
            subject.Shrink = 1;
            subject.MinWidth = 0;
            row.Add(subject);
            row.Add(new Text(Models.DateTimeFormat.Format(commit.CommitterTime)) { Color = Color.SecondaryLabel });
            return row;
        }

        /// <summary>Upstream's commit menu: cherry-picking them onto HEAD
        /// when the other side is HEAD, and copying them.</summary>
        private static IReadOnlyList<MenuEntry> CommitMenu(ViewModels.Compare vm, List<Models.Commit> commits, Models.Commit side)
        {
            if (commits.Count == 0)
                return [];
            commits.Sort((l, r) => l.CommitterTime.CompareTo(r.CommitterTime));
            var menu = new List<MenuEntry>();
            var hasCurrentHead = vm.BaseHead.IsCurrentHead || vm.ToHead.IsCurrentHead;
            if (hasCurrentHead && side is { IsCurrentHead: false })
            {
                menu.Add(new MenuItem(App.Text("CommitCM.CherryPickMultiple"), () => vm.CherryPick(commits)));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("Copy"), () => Clipboard.SetText(Lines(commits))));
            return menu;
        }

        private static string Lines(List<Models.Commit> commits)
        {
            var builder = new StringBuilder();
            foreach (var c in commits)
                builder.Append(c.SHA.AsSpan(0, 10)).Append(" - ").AppendLine(c.Subject);
            return builder.ToString();
        }

        /// <summary>Upstream's change menus: saving as a patch, the diff tool
        /// and revealing one, putting files back as either side has them, and
        /// copying paths.</summary>
        private static IReadOnlyList<MenuEntry> Menu(ViewModels.Compare vm, ViewModels.ChangeSelection selection)
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
                    menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.OpenInExternalDiffTool(change)));
                }
                if (!selection.HasFolder || selection.IsSingleFolder)
                {
                    var path = folder ?? vm.GetAbsPath(changes[0].Path);
                    menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(path))
                    { Enabled = folder != null ? Directory.Exists(path) : File.Exists(path) });
                }
                menu.Add(MenuSeparator.Instance);
            }
            if (vm.CanResetFiles)
            {
                if (changes.Count == 1 && !selection.HasFolder)
                {
                    var change = changes[0];
                    menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.BaseName), () => _ = vm.ResetToLeftAsync(change)));
                    menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.ToName), () => _ = vm.ResetToRightAsync(change)));
                }
                else
                {
                    menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.BaseName), () => _ = vm.ResetMultipleToLeftAsync(changes)));
                    menu.Add(new MenuItem(App.Text("ChangeCM.ResetFileTo", vm.ToName), () => _ = vm.ResetMultipleToRightAsync(changes)));
                }
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("CopyPath"), () => ChangeMenus.CopyPaths(vm.GetAbsPath, selection, false)));
            menu.Add(new MenuItem(App.Text("CopyFullPath"), () => ChangeMenus.CopyPaths(vm.GetAbsPath, selection, true)));
            return menu;
        }
    }
}
