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
        public static Node Create(ViewModels.CommitDetail vm)
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            var split = new Split { Position = layout.CommitDetailChangesLeftWidth.IsAbsolute ? layout.CommitDetailChangesLeftWidth.Value : 256 };
            split.Grow = 1;
            split.MinHeight = 0;
            split.Changed += width => layout.CommitDetailChangesLeftWidth = new(width);

            var left = new Column { AlignItems = Align.Stretch, Gap = 4, Padding = new Thickness(4, 0, 4, 4) };
            left.MinWidth = 200;
            var top = new Row { AlignItems = Align.Center, Gap = 4 };
            var search = new SearchInput { Placeholder = App.Text("CommitDetail.Changes.Search") };
            search.Grow = 1;
            Bind.Search(search, vm, nameof(vm.SearchChangeFilter), () => vm.SearchChangeFilter, v => vm.SearchChangeFilter = v, owner: top);
            top.Add(search);
            top.Add(ChangeCollection.ModeButton());
            left.Add(top);

            var list = List(vm);
            list.Grow = 1;
            list.MinHeight = 0;
            left.Add(list);
            var count = new Text { Color = Color.SecondaryLabel };
            Bind.OneWay(vm, nameof(vm.Changes), () => vm.Changes?.Count ?? 0,
                c => count.Value = $"{c} {App.Text("CommitDetail.Changes.Count")}", owner: count);
            left.Add(count);
            split.Add(left);
            split.Add(Detail(vm));
            return split;
        }

        private static ListView List(ViewModels.CommitDetail vm)
        {
            var prefs = ViewModels.Preferences.Instance;
            var repo = vm.Repository;
            var list = new ChangeCollection(false);
            var owner = list.View;
            owner.Style = ListStyle.Framed;
            Bind.OneWay(prefs, nameof(prefs.ChangeViewMode), () => prefs.ChangeViewMode, m => list.Mode = m, owner: owner);
            Bind.OneWay(prefs, nameof(prefs.EnableCompactFoldersInChangesTree), () => prefs.EnableCompactFoldersInChangesTree,
                v => list.CompactFolders = v, owner: owner);
            Bind.OneWay(vm, nameof(vm.VisibleChanges), () => vm.VisibleChanges, c => list.Changes = c, owner: owner);
            Bind.OneWay(vm, nameof(vm.ChangeSelection), () => vm.ChangeSelection, s => list.Selection = s, owner: owner);
            list.SelectionChanged += s => vm.ChangeSelection = s;
            list.Menu = s => Menu(vm, s);
            owner.OnKey(Shortcut.Of('c').WithPrimary(), () => ChangeMenus.CopyPaths(repo, vm.ChangeSelection, false));
            owner.OnKey(Shortcut.Of('c').WithPrimary().WithShift(), () => ChangeMenus.CopyPaths(repo, vm.ChangeSelection, true));
            return owner;
        }

        /// <summary>Upstream's welcome until a change is picked; its diff
        /// waits for bemtvi.</summary>
        private static Node Detail(ViewModels.CommitDetail vm)
        {
            var detail = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = 16 };
            detail.MinWidth = 100;
            detail.Add(new Icon(Icons.Get("Diff")) { Size = 64, Color = Color.SecondaryLabel });
            detail.Add(new Text(App.Text("Diff.Welcome")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            var note = new Text("Diffs aren't ported to mitsuami yet.") { Style = TextStyle.Caption, Color = Color.SecondaryLabel };
            Bind.OneWay(vm, nameof(vm.DiffContext), () => vm.DiffContext, d => note.IsVisible = d != null, owner: note);
            detail.Add(note);
            return detail;
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
                menu.Add(CommitMenu.NotPorted(App.Text("DirHistories"), "A folder's history"));
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
            menu.Add(CommitMenu.NotPorted(App.Text("FileHistory"), "A file's history"));
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
