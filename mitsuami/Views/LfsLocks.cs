using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's LFS locks (<c>Views/LFSLocks.axaml</c>), a 600 × 400 sheet
    /// as upstream's dialog: a remote's locked files with their owners, each
    /// with buttons that unlock it and force it; only the user's own, and
    /// unlocking all of them once confirmed; upstream's empty icon, or a
    /// spinner while they load.
    /// </summary>
    public static class LfsLocks
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo, string remote)
        {
            var vm = new ViewModels.LFSLocks(repo, remote);
            var title = App.Text("GitLFS.Locks.Title");
            var sheet = Shown = new Window(App.MainWindow, title, 600, 400);
            sheet.Closed += () => Shown = null;

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Grow = 1;
            column.Add(new Text(title) { Style = TextStyle.Headline });

            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center, Gap = Spacing.Lg };
            area.Grow = 1;
            area.MinHeight = 0;
            var list = new ListView<Models.LFSLock>(l => l.ID, l => Row(vm, l), itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 28,
            };
            list.Grow = 1;
            list.MinHeight = 0;
            area.Add(list);
            var empty = new Icon(Icons.Get("Empty")) { Size = 48, Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(empty);
            var none = new Text(App.Text("GitLFS.Locks.Empty")) { Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(none);
            var spinner = new Spinner { AlignSelf = Align.Center };
            area.Add(spinner);
            column.Add(area);
            void Refresh()
            {
                var locks = vm.VisibleLocks ?? [];
                list.Items = locks;
                spinner.IsVisible = vm.IsLoading;
                list.IsVisible = !vm.IsLoading && locks.Count > 0;
                empty.IsVisible = none.IsVisible = !vm.IsLoading && locks.Count == 0;
            }
            Bind.OneWay(vm, nameof(vm.VisibleLocks), () => vm.VisibleLocks, _ => Refresh(), owner: area);
            Bind.OneWay(vm, nameof(vm.IsLoading), () => vm.IsLoading, _ => Refresh(), owner: area);

            var buttons = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            var mine = new Checkbox(App.Text("GitLFS.Locks.OnlyMine"));
            Bind.Checked(mine, vm, nameof(vm.ShowOnlyMyLocks), () => vm.ShowOnlyMyLocks, v => vm.ShowOnlyMyLocks = v);
            buttons.Add(mine);
            var unlockAll = new Button(App.Text("GitLFS.Locks.UnlockAllMyLocks"));
            unlockAll.Click += async () =>
            {
                if (await App.AskConfirmAsync(App.Text("GitLFS.Locks.UnlockAllMyLocks.Confirm")))
                    await vm.UnlockAllMyLocksAsync();
            };
            buttons.Add(unlockAll);
            void Enable() => mine.IsEnabled = unlockAll.IsEnabled = vm.HasValidUserName && !vm.IsLoading;
            Bind.OneWay(vm, nameof(vm.IsLoading), () => vm.IsLoading, _ => Enable(), owner: buttons);
            Bind.OneWay(vm, nameof(vm.HasValidUserName), () => vm.HasValidUserName, _ => Enable(), owner: buttons);
            var gap = new Row();
            gap.Grow = 1;
            buttons.Add(gap);
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += sheet.Destroy;
            buttons.Add(close);
            column.Add(buttons);
            sheet.Content = column;
        }

        /// <summary>Upstream's row: the file, its owner, and the unlock and
        /// force-unlock buttons (the latter in the error colour).</summary>
        private static Node Row(ViewModels.LFSLocks vm, Models.LFSLock lfsLock)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Spacing.Sm, Padding = new Thickness(Spacing.None, Spacing.Xs) };
            row.Add(new Icon(Icons.Get("File")) { Size = 14 });
            var path = new Text(lfsLock.Path) { MaxLines = 1 };
            path.Grow = 1;
            path.Shrink = 1;
            path.MinWidth = 0;
            row.Add(path);
            row.Add(new Text(lfsLock.Owner?.Name ?? "") { Color = Color.SecondaryLabel, MaxLines = 1 });
            var unlock = new Button(App.Text("GitLFS.Locks.Unlock"))
            {
                Icon = Icons.Get("Unlock", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("GitLFS.Locks.Unlock"),
            };
            unlock.Click += () => _ = vm.UnlockAsync(lfsLock, false);
            var force = new Button(App.Text("GitLFS.Locks.UnlockForce"))
            {
                Icon = Icons.Get("Unlock", 14),
                IconOnly = true,
                IconColor = Color.Error,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("GitLFS.Locks.UnlockForce"),
            };
            force.Click += () => _ = vm.UnlockAsync(lfsLock, true);
            var unlocks = new Row { AlignItems = Align.Center, Gap = Icons.ButtonGap(Spacing.Sm) };
            unlocks.Add(unlock);
            unlocks.Add(force);
            row.Add(unlocks);
            return row;
        }
    }
}
