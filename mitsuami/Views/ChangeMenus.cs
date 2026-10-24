using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's context menus of the local changes
    /// (<c>WorkingCopy.axaml.cs</c>'s <c>CreateContextMenuForUnstagedChanges</c>
    /// and <c>CreateContextMenuForStagedChanges</c>), built as one opens for
    /// the changes it's about. Windows not ported yet (a file's history,
    /// blame, the merge editor, the AI assistant) say so in an alert.
    /// Upstream's menu icons and the shortcuts it shows aren't ported.
    /// </summary>
    public static class ChangeMenus
    {
        public static IReadOnlyList<MenuEntry> Unstaged(ViewModels.WorkingCopy vm, ViewModels.ChangeSelection selection, Func<Models.Change> next)
        {
            var repo = vm.Repository;
            var changes = selection.Changes;
            var menu = new List<MenuEntry>();

            if (changes.Count == 1)
            {
                var change = changes[0];
                var path = Native.OS.GetAbsPath(repo.FullPath, change.Path);

                if (!change.IsConflicted && !selection.HasFolder)
                {
                    menu.Add(Open(path));
                    menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.UseExternalDiffTool(change, true)));
                }
                if (!selection.HasFolder || selection.IsSingleFolder)
                    menu.Add(Reveal(selection.IsSingleFolder ? Native.OS.GetAbsPath(repo.FullPath, selection.SingleFolderPath) : path));
                if (menu.Count > 0)
                    menu.Add(MenuSeparator.Instance);

                if (change.IsConflicted)
                {
                    menu.AddRange(Resolve(vm, changes));
                    if (change.ConflictReason is Models.ConflictReason.BothAdded or Models.ConflictReason.BothModified && !Directory.Exists(path))
                    {
                        menu.Add(CommitMenu.NotPorted(App.Text("ChangeCM.Merge"), "The merge editor"));
                        menu.Add(new MenuItem(App.Text("ChangeCM.MergeExternal"), () => _ = vm.UseExternalMergeToolAsync(change)));
                    }
                    menu.Add(MenuSeparator.Instance);
                }
                else
                {
                    menu.Add(new MenuItem(App.Text("FileCM.Stage"), () => _ = vm.StageChangesAsync(changes, next())));
                    menu.Add(new MenuItem(App.Text("FileCM.Discard"), () => vm.Discard(changes, next())));
                    menu.Add(CommitMenu.Popup(repo, App.Text("FileCM.Stash"), () => new ViewModels.StashChanges(repo, changes)));
                    menu.Add(SaveAsPatch(vm, changes, true));
                    if (change.WorkTree != Models.ChangeState.Untracked)
                    {
                        menu.Add(new MenuItem(App.Text("FileCM.AssumeUnchanged"), async () =>
                        {
                            var log = repo.CreateLog("Assume File Unchanged");
                            await new Commands.AssumeUnchanged(repo.FullPath, change.Path, true).Use(log).ExecAsync();
                            log.Complete();
                        }));
                    }
                    menu.Add(MenuSeparator.Instance);

                    var extension = Path.GetExtension(change.Path);
                    var hasExtra = false;
                    if (selection.IsSingleFolder)
                    {
                        menu.Add(new Submenu(App.Text("WorkingCopy.AddToGitIgnore"),
                        [
                            Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.InFolder"), $"{selection.SingleFolderPath}/"),
                        ]));
                        hasExtra = true;
                    }
                    else if (!selection.HasFolder && change.WorkTree == Models.ChangeState.Untracked)
                    {
                        var isRooted = change.Path.IndexOf('/') <= 0;
                        var dir = isRooted ? "" : Path.GetDirectoryName(change.Path)!.Replace('\\', '/').TrimEnd('/');
                        var ignore = new List<MenuEntry> { Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.SingleFile"), change.Path) };
                        if (!string.IsNullOrEmpty(extension))
                        {
                            ignore.Add(Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.Extension", extension), $"*{extension}"));
                            if (!isRooted)
                                ignore.Add(Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.ExtensionInSameFolder", extension), $"{dir}/*{extension}"));
                        }
                        if (!isRooted)
                            ignore.Add(Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.UntrackedInSameFolder"), $"{dir}/"));
                        menu.Add(new Submenu(App.Text("WorkingCopy.AddToGitIgnore"), ignore));
                        hasExtra = true;
                    }

                    if (!selection.HasFolder && File.Exists(path) && repo.IsLFSEnabled())
                    {
                        var lfs = new List<MenuEntry>();
                        if (!new Commands.IsLFSFiltered(repo.FullPath, change.Path).GetResult())
                        {
                            var filename = Path.GetFileName(change.Path);
                            lfs.Add(new MenuItem(App.Text("GitLFS.Track", filename), () => _ = repo.TrackLFSFileAsync(filename, true)));
                            if (!string.IsNullOrEmpty(extension))
                                lfs.Add(new MenuItem(App.Text("GitLFS.TrackByExtension", extension), () => _ = repo.TrackLFSFileAsync($"*{extension}", false)));
                            lfs.Add(MenuSeparator.Instance);
                        }
                        lfs.AddRange(LfsLocks(repo, change.Path));
                        menu.Add(new Submenu(App.Text("GitLFS"), lfs));
                        hasExtra = true;
                    }

                    if (hasExtra)
                        menu.Add(MenuSeparator.Instance);
                }

                if (selection.IsSingleFolder)
                {
                    menu.Add(CommitMenu.NotPorted(App.Text("DirHistories"), "A folder's history"));
                    menu.Add(MenuSeparator.Instance);
                }
                else if (change.WorkTree is not (Models.ChangeState.Untracked or Models.ChangeState.Added))
                {
                    menu.Add(CommitMenu.NotPorted(App.Text("FileHistory"), "A file's history"));
                    menu.Add(CommitMenu.NotPorted(App.Text("Blame") + " (HEAD-only)", "Blame"));
                    menu.Add(MenuSeparator.Instance);
                }

                SidebarMenus.CustomActions(menu, repo, Models.CustomActionScope.File, "FileCM.CustomAction", new Models.CustomActionTargetFile(change.Path, null));
                menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(selection.IsSingleFolder ? selection.SingleFolderPath : change.Path)));
                menu.Add(new MenuItem(App.Text("CopyFullPath"), () =>
                    Clipboard.SetText(selection.IsSingleFolder ? Native.OS.GetAbsPath(repo.FullPath, selection.SingleFolderPath) : path)));
                return menu;
            }

            var hasConflicts = false;
            var hasNonConflicts = false;
            foreach (var change in changes)
            {
                if (change.IsConflicted)
                    hasConflicts = true;
                else
                    hasNonConflicts = true;
            }
            if (hasConflicts)
            {
                if (hasNonConflicts)
                {
                    repo.SendNotification("Selection contains both conflict and non-conflict changes!", true);
                    return [];
                }
                menu.AddRange(Resolve(vm, changes));
                return menu;
            }

            if (selection.IsSingleFolder)
            {
                menu.Add(Reveal(Path.Combine(repo.FullPath, selection.SingleFolderPath)));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("FileCM.StageMulti", changes.Count), () => _ = vm.StageChangesAsync(changes, next())));
            menu.Add(new MenuItem(App.Text("FileCM.DiscardMulti", changes.Count), () => vm.Discard(changes, next())));
            menu.Add(CommitMenu.Popup(repo, App.Text("FileCM.StashMulti", changes.Count), () => new ViewModels.StashChanges(repo, changes)));
            menu.Add(SaveAsPatch(vm, changes, true));
            if (selection.IsSingleFolder)
            {
                menu.Add(MenuSeparator.Instance);
                menu.Add(new Submenu(App.Text("WorkingCopy.AddToGitIgnore"),
                [
                    Ignore(repo, App.Text("WorkingCopy.AddToGitIgnore.InFolder"), $"{selection.SingleFolderPath}/"),
                ]));
                menu.Add(MenuSeparator.Instance);
                menu.Add(CommitMenu.NotPorted(App.Text("DirHistories"), "A folder's history"));
                menu.Add(MenuSeparator.Instance);
                menu.AddRange(CopyFolder(repo, selection.SingleFolderPath));
            }
            return menu;
        }

        public static IReadOnlyList<MenuEntry> Staged(ViewModels.WorkingCopy vm, ViewModels.ChangeSelection selection, Func<Models.Change> next)
        {
            var repo = vm.Repository;
            var changes = selection.Changes;
            var menu = new List<MenuEntry>();
            var ai = repo.GetPreferredOpenAIServices().Count > 0
                ? CommitMenu.NotPorted(App.Text("ChangeCM.GenerateCommitMessage"), "The AI assistant")
                : null;

            if (changes.Count == 1)
            {
                var change = changes[0];
                var path = Native.OS.GetAbsPath(repo.FullPath, change.Path);

                if (!selection.HasFolder)
                {
                    menu.Add(Open(path));
                    menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.UseExternalDiffTool(change, false)));
                }
                if (!selection.HasFolder || selection.IsSingleFolder)
                    menu.Add(Reveal(selection.IsSingleFolder ? Native.OS.GetAbsPath(repo.FullPath, selection.SingleFolderPath) : path));
                if (menu.Count > 0)
                    menu.Add(MenuSeparator.Instance);

                menu.Add(new MenuItem(App.Text("FileCM.Unstage"), () => _ = vm.UnstageChangesAsync(changes, next())));
                menu.Add(CommitMenu.Popup(repo, App.Text("FileCM.Stash"), () => new ViewModels.StashChanges(repo, changes), enabled: !vm.UseAmend));
                menu.Add(SaveAsPatch(vm, changes, false));
                menu.Add(MenuSeparator.Instance);

                if (!selection.HasFolder && File.Exists(path) && repo.IsLFSEnabled())
                {
                    menu.Add(new Submenu(App.Text("GitLFS"), [.. LfsLocks(repo, change.Path)]));
                    menu.Add(MenuSeparator.Instance);
                }
                if (ai != null)
                {
                    menu.Add(ai);
                    menu.Add(MenuSeparator.Instance);
                }

                if (selection.IsSingleFolder)
                {
                    menu.Add(CommitMenu.NotPorted(App.Text("DirHistories"), "A folder's history"));
                    menu.Add(MenuSeparator.Instance);
                }
                else if (change.Index is not (Models.ChangeState.Added or Models.ChangeState.Renamed))
                {
                    menu.Add(CommitMenu.NotPorted(App.Text("FileHistory"), "A file's history"));
                    menu.Add(CommitMenu.NotPorted(App.Text("Blame") + " (HEAD-only)", "Blame"));
                    menu.Add(MenuSeparator.Instance);
                }

                SidebarMenus.CustomActions(menu, repo, Models.CustomActionScope.File, "FileCM.CustomAction", new Models.CustomActionTargetFile(change.Path, null));
                menu.Add(new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(selection.IsSingleFolder ? selection.SingleFolderPath : change.Path)));
                menu.Add(new MenuItem(App.Text("CopyFullPath"), () =>
                    Clipboard.SetText(selection.IsSingleFolder ? Native.OS.GetAbsPath(repo.FullPath, selection.SingleFolderPath) : path)));
                return menu;
            }

            if (selection.IsSingleFolder)
            {
                menu.Add(Reveal(Native.OS.GetAbsPath(repo.FullPath, selection.SingleFolderPath)));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("FileCM.UnstageMulti", changes.Count), () => _ = vm.UnstageChangesAsync(changes, next())));
            menu.Add(CommitMenu.Popup(repo, App.Text("FileCM.StashMulti", changes.Count), () => new ViewModels.StashChanges(repo, changes), enabled: !vm.UseAmend));
            menu.Add(SaveAsPatch(vm, changes, false));
            if (ai != null)
            {
                menu.Add(MenuSeparator.Instance);
                menu.Add(ai);
            }
            if (selection.IsSingleFolder)
            {
                menu.Add(MenuSeparator.Instance);
                menu.Add(CommitMenu.NotPorted(App.Text("DirHistories"), "A folder's history"));
                menu.Add(MenuSeparator.Instance);
                menu.AddRange(CopyFolder(repo, selection.SingleFolderPath));
            }
            return menu;
        }

        /// <summary>Upstream's ⌘C and ⌘⇧C on a list: the selected paths,
        /// one a line, or a folder's.</summary>
        public static void CopyPaths(ViewModels.Repository repo, ViewModels.ChangeSelection selection, bool full)
        {
            string Shown(string path) => full ? Native.OS.GetAbsPath(repo.FullPath, path) : path;
            var changes = selection.Changes;
            var builder = new StringBuilder();
            if (selection.IsSingleFolder)
                builder.Append(Shown(selection.SingleFolderPath));
            else if (changes.Count == 1)
                builder.Append(Shown(changes[0].Path));
            else
            {
                foreach (var c in changes)
                    builder.AppendLine(Shown(c.Path));
            }
            if (builder.Length > 0)
                Clipboard.SetText(builder.ToString());
        }

        /// <summary>Upstream's ⌘O: the file in the system's default
        /// editor.</summary>
        public static void OpenWithDefaultEditor(ViewModels.Repository repo, ViewModels.ChangeSelection selection)
        {
            if (selection.Changes is not [var change])
                return;
            var path = Native.OS.GetAbsPath(repo.FullPath, change.Path);
            if (File.Exists(path))
                Native.OS.OpenWithDefaultEditor(path);
        }

        /// <summary>Upstream's "Use theirs" and "Use mine", named after what
        /// each side is in the operation in progress.</summary>
        private static IEnumerable<MenuEntry> Resolve(ViewModels.WorkingCopy vm, List<Models.Change> changes)
        {
            var repo = vm.Repository;
            var (theirs, mine) = vm.InProgressContext switch
            {
                ViewModels.CherryPickInProgress cherryPick => (App.Text("FileCM.ResolveUsing", cherryPick.HeadName), App.Text("FileCM.ResolveUsing", repo.CurrentBranch.Name)),
                ViewModels.RebaseInProgress rebase => (App.Text("FileCM.ResolveUsing", rebase.HeadName), App.Text("FileCM.ResolveUsing", rebase.BaseName)),
                ViewModels.RevertInProgress revert => (App.Text("FileCM.ResolveUsing", $"{revert.Head.SHA.AsSpan(0, 10)} (revert)"), App.Text("FileCM.ResolveUsing", repo.CurrentBranch.Name)),
                ViewModels.MergeInProgress merge => (App.Text("FileCM.ResolveUsing", merge.SourceName), App.Text("FileCM.ResolveUsing", repo.CurrentBranch.Name)),
                _ => (App.Text("FileCM.UseTheirs"), App.Text("FileCM.UseMine")),
            };
            yield return new MenuItem(theirs, () => _ = vm.UseTheirsAsync(changes));
            yield return new MenuItem(mine, () => _ = vm.UseMineAsync(changes));
        }

        /// <summary>Upstream's "Open": the system's default editor and the
        /// external tools it found, for a file that's there.</summary>
        private static MenuEntry Open(string path)
        {
            if (!File.Exists(path))
                return new MenuItem(App.Text("Open"), () => { }) { Enabled = false };
            var open = new List<MenuEntry> { new MenuItem(App.Text("Open.SystemDefaultEditor"), () => Native.OS.OpenWithDefaultEditor(path)) };
            var tools = Native.OS.ExternalTools;
            if (tools.Count > 0)
            {
                open.Add(MenuSeparator.Instance);
                foreach (var tool in tools)
                    open.Add(new MenuItem(tool.Name, () => tool.Launch(path.Quoted())));
            }
            return new Submenu(App.Text("Open"), open);
        }

        private static MenuItem Reveal(string path) =>
            new(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(path)) { Enabled = Path.Exists(path) };

        private static MenuItem Ignore(ViewModels.Repository repo, string title, string pattern) =>
            CommitMenu.Popup(repo, title, () => new ViewModels.AddToIgnore(repo, pattern));

        private static MenuItem SaveAsPatch(ViewModels.WorkingCopy vm, List<Models.Change> changes, bool unstaged) =>
            new(App.Text("FileCM.SaveAsPatch"), async () =>
            {
                if (await Dialogs.SaveFile(App.MainWindow, App.Text("FileCM.SaveAsPatch"), "changes.patch") is not { } file)
                    return;
                try
                {
                    await vm.SaveChangesToPatchAsync(changes, unstaged, file);
                }
                catch (Exception exception)
                {
                    vm.Repository.SendNotification($"Failed to save as patch: {exception.Message}", true);
                }
            });

        /// <summary>Upstream's LFS lock and unlock: straight away with one
        /// remote, a submenu of them with several.</summary>
        private static IEnumerable<MenuEntry> LfsLocks(ViewModels.Repository repo, string path)
        {
            MenuEntry Each(string title, Func<string, System.Threading.Tasks.Task> act)
            {
                if (repo.Remotes.Count == 0)
                    return new MenuItem(title, () => { }) { Enabled = false };
                if (repo.Remotes.Count == 1)
                    return new MenuItem(title, () => _ = act(repo.Remotes[0].Name));
                var remotes = new List<MenuEntry>();
                foreach (var remote in repo.Remotes)
                {
                    var name = remote.Name;
                    remotes.Add(new MenuItem(name, () => _ = act(name)));
                }
                return new Submenu(title, remotes);
            }
            yield return Each(App.Text("GitLFS.Locks.Lock"), remote => repo.LockLFSFileAsync(remote, path));
            yield return Each(App.Text("GitLFS.Locks.Unlock"), remote => repo.UnlockLFSFileAsync(remote, path, false, true));
        }

        private static IEnumerable<MenuEntry> CopyFolder(ViewModels.Repository repo, string folder)
        {
            yield return new MenuItem(App.Text("CopyPath"), () => Clipboard.SetText(folder));
            yield return new MenuItem(App.Text("CopyFullPath"), () => Clipboard.SetText(Native.OS.GetAbsPath(repo.FullPath, folder)));
        }
    }
}
