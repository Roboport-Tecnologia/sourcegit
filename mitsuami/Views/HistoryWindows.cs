using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's history windows, each a window of its own, 1280 × 720 as
    /// upstream's least size: a folder's (<c>Views/DirHistories.axaml</c>),
    /// its commits beside the picked one's details; and a file's
    /// (<c>Views/FileHistories.axaml</c>), its revisions beside the picked
    /// one's change or content, or two compared. Diffs and text wait for
    /// the code view (bemtvi); images, binaries and LFS objects show.
    /// </summary>
    public static class HistoryWindows
    {
        /// <summary>The window last opened, while it's open: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void ShowFolder(ViewModels.Repository repo, string folder, string revision = null)
        {
            var vm = new ViewModels.DirHistories(repo, folder, revision);
            var window = Open(App.Text("DirHistories"));
            var column = Page(window, "Folder.Open", vm, nameof(vm.Title), () => vm.Title);

            var split = new Split { Position = 300 };
            split.Grow = 1;
            split.MinHeight = 0;
            var list = new ListView<Models.Commit>(c => c.SHA,
                c => Row(c.Author, c.SHA, c.AuthorTime, c.Subject, () => vm.NavigateToCommit(c), () => vm.GetCommitFullMessage(c)),
                itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 48,
            };
            Bind.OneWay(vm, nameof(vm.Commits), () => vm.Commits, c => list.Items = c ?? [], owner: list);
            Bind.OneWay(vm, nameof(vm.SelectedCommit), () => vm.SelectedCommit, c =>
            {
                if (list.SelectedItem != c)
                    list.SelectedItems = c != null ? [c] : [];
            }, owner: list);
            list.SelectionChanged += selected => vm.SelectedCommit = selected.Count > 0 ? selected[0] : null;
            split.Add(Loading(list, vm, nameof(vm.IsLoading), () => vm.IsLoading));
            split.Add(CommitDetail.Create(vm, nameof(vm.Detail), () => vm.Detail, () => repo.Remotes.Count == 1, tips: false));
            column.Add(split);
        }

        public static void ShowFile(ViewModels.Repository repo, string file, string commit = null) =>
            ShowFile(new ViewModels.FileHistories(repo.FullPath, file, commit));

        /// <summary>A file's history already asked for: a palette's pick.</summary>
        public static void ShowFile(ViewModels.FileHistories vm)
        {
            var window = Open(App.Text("FileHistory"));
            var column = Page(window, "File", vm, nameof(vm.Title), () => vm.Title);

            var split = new Split { Position = 300 };
            split.Grow = 1;
            split.MinHeight = 0;
            var list = new ListView<Models.FileVersion>(v => v.SHA,
                v => Row(v.Author, v.SHA, v.AuthorTime, v.Subject, () => vm.NavigateToCommit(v), () => vm.GetCommitFullMessage(v)),
                SelectionMode.Multiple, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 48,
            };
            Bind.OneWay(vm, nameof(vm.Revisions), () => vm.Revisions, r => list.Items = r ?? [], owner: list);
            list.SelectionChanged += selected => vm.SelectedRevisions = [.. selected];
            split.Add(Loading(list, vm, nameof(vm.IsLoading), () => vm.IsLoading));

            var detail = new Column { AlignItems = Align.Stretch, Padding = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Sm) };
            detail.Grow = 1;
            detail.MinWidth = 0;
            Node shown = null;
            Bind.OneWay(vm, nameof(vm.ViewContent), () => vm.ViewContent, content =>
            {
                shown?.Destroy();
                shown = content switch
                {
                    ViewModels.FileHistoriesSingleRevision single => Single(single),
                    ViewModels.FileHistoriesCompareRevisions compare => Compared(compare),
                    _ => Empty(),
                };
                detail.Add(shown);
            }, owner: detail);
            split.Add(detail);
            column.Add(split);
        }

        private static Window Open(string title)
        {
            var window = Shown = new Window(title, 1280, 720);
            window.Closed += () =>
            {
                if (Shown == window)
                    Shown = null;
            };
            return window;
        }

        /// <summary>Upstream's page: the folder's or file's icon and path
        /// over a line, then what's under it.</summary>
        private static Column Page(Window window, string icon, System.ComponentModel.INotifyPropertyChanged vm, string property, Func<string> title)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm, Padding = Spacing.Md };
            column.Grow = 1;
            var heading = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            heading.Add(new Icon(Icons.Get(icon)) { Size = 14 });
            var text = new Text { Weight = FontWeight.Bold, Selectable = true };
            Bind.OneWay(vm, property, title, t => text.Value = t ?? "", owner: text);
            heading.Add(text);
            column.Add(heading);
            column.Add(new Separator());
            window.Content = column;
            return column;
        }

        /// <summary>A list, with upstream's spinner in its place while it
        /// loads.</summary>
        private static Node Loading(ListView list, System.ComponentModel.INotifyPropertyChanged vm, string property, Func<bool> loading)
        {
            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            area.MinWidth = 300;
            list.Grow = 1;
            list.MinHeight = 0;
            area.Add(list);
            var spinner = new Spinner { AlignSelf = Align.Center };
            area.Add(spinner);
            Bind.OneWay(vm, property, loading, busy =>
            {
                spinner.IsVisible = busy;
                list.IsVisible = !busy;
            }, owner: area);
            return area;
        }

        /// <summary>Upstream's row: the author's avatar and name, the SHA
        /// (a click goes to it in the history), the time, then the subject,
        /// its full message as the tooltip.</summary>
        private static Node Row(Models.User author, string sha, ulong time, string subject, Action navigate, Func<string> message)
        {
            var column = new Column { Gap = Spacing.Xs, Padding = new Thickness(Spacing.Xs, Spacing.None) };
            column.Height = 48;
            var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            top.Add(new Avatar(16, menu: false) { User = author }.View);
            var name = new Text(author.Name) { MaxLines = 1 };
            name.Grow = 1;
            name.Shrink = 1;
            name.MinWidth = 0;
            top.Add(name);
            var link = new Row();
            link.Add(new Text(sha[..10]) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
            link.Clicked += navigate;
            link.ToolTip = sha;
            link.Shrink = 0;
            top.Add(link);
            // The author's name gives way; the time keeps its one line.
            var when = new Text(Models.DateTimeFormat.Format(time)) { Color = Color.SecondaryLabel, MaxLines = 1 };
            when.Shrink = 0;
            top.Add(when);
            column.Add(top);
            var line = new Text(subject) { MaxLines = 1 };
            column.Add(line);
            // The full message is git's to tell; asked for once the row shows.
            var gone = false;
            column.Forgotten += () => gone = true;
            _ = Task.Run(message).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    Application.Post(() =>
                    {
                        if (!gone)
                            line.ToolTip = t.Result;
                    });
            });
            return column;
        }

        /// <summary>Upstream's single revision: tabs for its change and its
        /// content (upstream's switch), and checking the file out as it was.</summary>
        private static Node Single(ViewModels.FileHistoriesSingleRevision vm)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm };
            column.Grow = 1;
            var tabs = new Tabs();
            tabs.Grow = 1;
            tabs.MinHeight = 0;
            var change = new Column { AlignItems = Align.Stretch };
            var content = new Column { AlignItems = Align.Stretch };
            tabs.Add(Popups.ButtonText("FileHistory.FileChange"), change);
            tabs.Add(Popups.ButtonText("FileHistory.FileContent"), content);
            tabs.SelectedIndex = vm.IsDiffMode ? 0 : 1;
            tabs.Changed += index => vm.IsDiffMode = index == 0;
            Node shown = null;
            Bind.OneWay(vm, nameof(vm.ViewContent), () => vm.ViewContent, value =>
            {
                shown?.Destroy();
                shown = value switch
                {
                    ViewModels.FileHistoriesRevisionFile file => File(vm, file),
                    ViewModels.DiffContext => DiffView.Create(vm, nameof(vm.ViewContent), () => vm.ViewContent),
                    _ => Empty(),
                };
                (vm.IsDiffMode ? change : content).Add(shown);
            }, owner: tabs);
            column.Add(tabs);
            var buttons = new Row { Justify = Justify.End };
            var reset = new Button(App.Text("ChangeCM.CheckoutThisRevision"));
            // Upstream's alert once it's done.
            reset.Click += async () =>
            {
                await vm.ResetToSelectedRevisionAsync();
                await Dialogs.Alert(Shown, "Reset to selected revision successfully.", "", [Popups.ButtonText("Sure")]);
            };
            buttons.Add(reset);
            column.Add(buttons);
            return column;
        }

        /// <summary>The file at that revision: its path with the open button
        /// over it, as the Files tab shows one.</summary>
        private static Node File(ViewModels.FileHistoriesSingleRevision vm, ViewModels.FileHistoriesRevisionFile file)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            column.Grow = 1;
            var header = new Row { AlignItems = Align.Center, Gap = Spacing.Xs, Padding = new Thickness(Spacing.Xs, Spacing.Xs) };
            var path = new Text(file.Path) { Style = TextStyle.Caption, MaxLines = 1 };
            path.Grow = 1;
            path.Shrink = 1;
            path.MinWidth = 0;
            header.Add(path);
            if (file.CanOpenWithDefaultEditor)
            {
                var open = new Button(App.Text("Open"))
                {
                    Icon = Icons.Get("OpenWith", 12),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text("Open"),
                };
                open.Click += () => _ = vm.OpenWithDefaultEditorAsync();
                header.Add(open);
            }
            column.Add(header);
            column.Add(RevisionFiles.Shown(file.Content));
            return column;
        }

        /// <summary>Upstream's two revisions compared: each side, swapping
        /// them, saving the difference as a patch, and the diff.</summary>
        private static Node Compared(ViewModels.FileHistoriesCompareRevisions vm)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm };
            column.Grow = 1;
            var header = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            header.Add(Side(vm, nameof(vm.StartPoint), () => vm.StartPoint));
            var swap = new Button(App.Text("Diff.SwapCommits"))
            {
                Icon = Icons.Get("Compare", 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Diff.SwapCommits"),
            };
            swap.Click += vm.Swap;
            header.Add(swap);
            header.Add(Side(vm, nameof(vm.EndPoint), () => vm.EndPoint));
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
                    await vm.SaveAsPatch(file);
            };
            header.Add(patch);
            column.Add(header);
            column.Add(DiffView.Create(vm, nameof(vm.ViewContent), () => vm.ViewContent));
            return column;
        }

        private static Node Side(ViewModels.FileHistoriesCompareRevisions vm, string property, Func<Models.FileVersion> point)
        {
            var holder = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            holder.Grow = 1;
            holder.MinWidth = 0;
            Bind.OneWay(vm, property, point, version =>
            {
                foreach (var child in holder.Children.ToArray())
                    child.Destroy();
                if (version == null)
                    return;
                var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
                top.Add(new Avatar(16, menu: false) { User = version.Author }.View);
                top.Add(new Text(version.Author.Name) { MaxLines = 1 });
                top.Add(new Text(version.SHA[..10]) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
                top.Add(new Text(Models.DateTimeFormat.Format(version.AuthorTime)) { Color = Color.SecondaryLabel });
                holder.Add(top);
                holder.Add(new Text(version.Subject) { MaxLines = 1 });
            }, owner: holder);
            return holder;
        }

        /// <summary>Upstream's empty side: its history icon.</summary>
        private static Node Empty()
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center };
            column.Grow = 1;
            column.Add(new Icon(Icons.Get("Histories")) { Size = 64, Color = Color.SecondaryLabel });
            return column;
        }
    }
}
