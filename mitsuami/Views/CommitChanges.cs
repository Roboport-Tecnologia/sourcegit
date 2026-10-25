using System;
using System.Collections.Generic;
using System.IO;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's Changes tab of a commit (<c>Views/CommitChanges.axaml</c>):
    /// beside the diff, a search over the changes' paths with the view mode,
    /// the changes as a list, grid or tree, and their count under them. The
    /// diff waits for the code view (bemtvi); the menus, opening files,
    /// checking them out and saving patches work.
    /// </summary>
    public static class CommitChanges
    {
        public static Node Create(ViewModels.CommitDetail vm) =>
            Pane(vm, vm.GetAbsPath,
                nameof(vm.SearchChangeFilter), () => vm.SearchChangeFilter, v => vm.SearchChangeFilter = v,
                nameof(vm.VisibleChanges), () => vm.VisibleChanges,
                nameof(vm.ChangeSelection), () => vm.ChangeSelection, s => vm.ChangeSelection = s,
                nameof(vm.Changes), () => vm.Changes?.Count ?? 0,
                nameof(vm.DiffContext), () => vm.DiffContext,
                s => Menu(vm, s));

        /// <summary>
        /// Upstream's changes beside their diff, which a commit, two commits
        /// compared and two branches compared share: the search over their
        /// paths with the view mode, the changes as a list, grid or tree
        /// (with a spinner while they load), their count, and upstream's
        /// diff (<see cref="DiffView"/>).
        /// </summary>
        public static Node Pane(System.ComponentModel.INotifyPropertyChanged vm, Func<string, string> absPath,
            string filterProperty, Func<string> filter, Action<string> setFilter,
            string changesProperty, Func<List<Models.Change>> changes,
            string selectionProperty, Func<ViewModels.ChangeSelection> selection, Action<ViewModels.ChangeSelection> setSelection,
            string countProperty, Func<int> count,
            string diffProperty, Func<object> diff,
            Func<ViewModels.ChangeSelection, IReadOnlyList<MenuEntry>> menu,
            string loadingProperty = null, Func<bool> loading = null)
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            var split = new Split { Position = layout.CommitDetailChangesLeftWidth.IsAbsolute ? layout.CommitDetailChangesLeftWidth.Value : 256 };
            split.Grow = 1;
            split.MinHeight = 0;
            split.Changed += width => layout.CommitDetailChangesLeftWidth = new(width);

            // Room on both sides of the divider, so neither pane runs into it.
            var left = new Column { AlignItems = Align.Stretch, Gap = 4, Padding = new Thickness(4, 6, 4, 4) };
            left.MinWidth = 200;
            var top = new Row { AlignItems = Align.Center, Gap = 4 };
            var search = new SearchInput { Placeholder = App.Text("CommitDetail.Changes.Search") };
            search.Grow = 1;
            Bind.Search(search, vm, filterProperty, filter, setFilter, owner: top);
            top.Add(search);
            top.Add(ChangeCollection.ModeButton());
            left.Add(top);

            var prefs = ViewModels.Preferences.Instance;
            var list = new ChangeCollection(false);
            var view = list.View;
            view.Style = ListStyle.Framed;
            view.Grow = 1;
            view.MinHeight = 0;
            Bind.OneWay(prefs, nameof(prefs.ChangeViewMode), () => prefs.ChangeViewMode, m => list.Mode = m, owner: view);
            Bind.OneWay(prefs, nameof(prefs.EnableCompactFoldersInChangesTree), () => prefs.EnableCompactFoldersInChangesTree,
                v => list.CompactFolders = v, owner: view);
            Bind.OneWay(vm, changesProperty, changes, c => list.Changes = c, owner: view);
            Bind.OneWay(vm, selectionProperty, selection, s => list.Selection = s, owner: view);
            list.SelectionChanged += setSelection;
            list.Menu = menu;
            view.OnKey(Shortcut.Of('c').WithPrimary(), () => ChangeMenus.CopyPaths(absPath, selection(), false));
            view.OnKey(Shortcut.Of('c').WithPrimary().WithShift(), () => ChangeMenus.CopyPaths(absPath, selection(), true));
            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            area.Grow = 1;
            area.MinHeight = 0;
            area.Add(view);
            if (loadingProperty != null)
            {
                var spinner = new Spinner { AlignSelf = Align.Center };
                area.Add(spinner);
                Bind.OneWay(vm, loadingProperty, loading, busy =>
                {
                    spinner.IsVisible = busy;
                    view.IsVisible = !busy;
                }, owner: spinner);
            }
            left.Add(area);
            var counted = new Text { Color = Color.SecondaryLabel };
            Bind.OneWay(vm, countProperty, count, c => counted.Value = $"{c} {App.Text("CommitDetail.Changes.Count")}", owner: counted);
            left.Add(counted);
            split.Add(left);

            var right = new Column { AlignItems = Align.Stretch, Padding = new Thickness(0, 0, 0, 6) };
            right.MinWidth = 100;
            right.Add(DiffView.Create(vm, diffProperty, diff));
            split.Add(right);
            return split;
        }

        /// <summary>Upstream's menus: for a folder, a change, several
        /// changes, or a selection with folders in it.</summary>
        public static IReadOnlyList<MenuEntry> Menu(ViewModels.CommitDetail vm, ViewModels.ChangeSelection selection)
        {
            if (selection.Count == 0 || vm is not { Repository: { } repo, Commit: { } commit })
                return [];
            var changes = selection.Changes;
            var menu = new List<MenuEntry>();
            if (selection.IsSingleFolder)
            {
                var folder = selection.SingleFolderPath;
                var full = Native.OS.GetAbsPath(repo.FullPath, folder);
                menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(full)) { Enabled = Directory.Exists(full) });
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("DirHistories"), () => HistoryWindows.ShowFolder(repo, folder, commit.SHA)));
                menu.Add(SaveAsPatch(vm, changes));
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(folder)));
                menu.Add(new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(full)));
                return menu;
            }
            if (changes.Count > 1)
            {
                menu.Add(SaveAsPatch(vm, changes));
                menu.Add(MenuSeparator.Instance);
                if (!repo.IsBare)
                {
                    menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutThisRevision"), () => _ = vm.ResetMultipleToThisRevisionAsync(changes)));
                    menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutFirstParentRevision"), () => _ = vm.ResetMultipleToParentRevisionAsync(changes))
                    { Enabled = commit.Parents.Count > 0 });
                    menu.Add(MenuSeparator.Instance);
                }
                menu.Add(new MenuItem(App.Text("CopyPath"), () => ChangeMenus.CopyPaths(repo, selection, false)));
                menu.Add(new MenuItem(App.Text("CopyFullPath"), () => ChangeMenus.CopyPaths(repo, selection, true)));
                return menu;
            }
            var change = changes[0];
            var path = Native.OS.GetAbsPath(repo.FullPath, change.Path);
            if (selection.HasFolder)
            {
                menu.Add(SaveAsPatch(vm, [change]));
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(change.Path)));
                menu.Add(new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(path)));
                return menu;
            }
            menu.Add(Open(vm, change));
            menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.OpenChangeInMergeTool(change)));
            menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(path)) { Enabled = File.Exists(path) });
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("FileHistory"), () => HistoryWindows.ShowFile(repo, change.Path, commit.SHA)));
            menu.Add(CommitMenu.NotPorted(App.Text("Blame"), "Blame"));
            menu.Add(SaveAsPatch(vm, [change]));
            menu.Add(MenuSeparator.Instance);
            if (!repo.IsBare)
            {
                menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutThisRevision"), () => _ = vm.ResetToThisRevisionAsync(change)));
                menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutFirstParentRevision"), () => _ = vm.ResetToParentRevisionAsync(change))
                { Enabled = commit.Parents.Count > 0 });
                menu.Add(MenuSeparator.Instance);
                if (repo.Remotes.Count > 0 && File.Exists(path) && repo.IsLFSEnabled())
                {
                    menu.Add(new Submenu(App.Text("GitLFS"), [.. ChangeMenus.LfsLocks(repo, change.Path)]));
                    menu.Add(MenuSeparator.Instance);
                }
            }
            var actions = repo.GetCustomActions(Models.CustomActionScope.File);
            if (actions.Count > 0)
            {
                var target = new Models.CustomActionTargetFile(change.Path, commit);
                var custom = new List<MenuEntry>();
                foreach (var (action, label) in actions)
                    custom.Add(new MenuItem(label.Name, () => _ = repo.ExecCustomActionAsync(action, target)));
                menu.Add(new Submenu(App.Text("FileCM.CustomAction"), custom));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(change.Path)));
            menu.Add(new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(path)));
            return menu;
        }

        /// <summary>Upstream's Open: the file as it is in the commit, with
        /// the system's default editor or an external tool.</summary>
        private static MenuEntry Open(ViewModels.CommitDetail vm, Models.Change change)
        {
            if (change.Index == Models.ChangeState.Deleted)
                return new MenuItem(App.Text("Open"), () => { }) { Enabled = false };
            var open = new List<MenuEntry> { new MenuItem(App.Text("Open.SystemDefaultEditor"), () => _ = vm.OpenRevisionFileAsync(change.Path, null)) };
            var tools = Native.OS.ExternalTools;
            if (tools.Count > 0)
            {
                open.Add(MenuSeparator.Instance);
                foreach (var tool in tools)
                    open.Add(new MenuItem(tool.Name, () => _ = vm.OpenRevisionFileAsync(change.Path, tool)));
            }
            return new Submenu(App.Text("Open"), open);
        }

        public static MenuItem SaveAsPatch(ViewModels.CommitDetail vm, List<Models.Change> changes) =>
            new(App.Text("FileCM.SaveAsPatch"), async () =>
            {
                if (await Dialogs.SaveFile(App.MainWindow, App.Text("FileCM.SaveAsPatch"), "changes.patch") is not { } file)
                    return;
                try
                {
                    await vm.SaveChangesAsPatchAsync(changes, file);
                }
                catch (Exception exception)
                {
                    vm.Repository.SendNotification($"Failed to save as patch: {exception.Message}", true);
                }
            });
    }
}
