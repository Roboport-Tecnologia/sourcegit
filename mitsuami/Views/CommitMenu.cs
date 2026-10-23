using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's context menus of the history (<c>Histories.axaml.cs</c>'s
    /// <c>CreateContextMenuForSingleCommit</c> and
    /// <c>CreateContextMenuForMultipleCommits</c>), as the platform's menus:
    /// built as one opens, for the commit right-clicked, or for the
    /// selection when that commit is in it, as Finder's and Explorer's
    /// menus act. Popups not ported yet say so on their sheet; dialogs and
    /// windows (interactive rebase, compare) in an alert. Upstream's menu
    /// icons and the shortcuts it shows aren't ported.
    /// </summary>
    public static class CommitMenu
    {
        public static IReadOnlyList<MenuEntry> For(ViewModels.Repository repo, ViewModels.Histories vm, Models.Commit clicked)
        {
            var selected = vm.SelectedCommits ?? [];
            if (selected.Count > 1 && selected.Contains(clicked))
            {
                // Upstream's order: oldest first, as the table lists them
                // newest first.
                var commits = new List<Models.Commit>(selected);
                commits.Sort((a, b) => vm.Commits.IndexOf(b).CompareTo(vm.Commits.IndexOf(a)));
                return Multiple(repo, vm, commits);
            }
            return Single(repo, vm, clicked);
        }

        private static IReadOnlyList<MenuEntry> Multiple(ViewModels.Repository repo, ViewModels.Histories vm, List<Models.Commit> selected)
        {
            var canCherryPick = true;
            var canMerge = true;
            foreach (var c in selected)
            {
                if (c.IsMerged)
                    canMerge = false;
                if (c.Parents.Count > 1)
                    canCherryPick = false;
            }

            var menu = new List<MenuEntry>();
            if (!repo.IsBare)
            {
                if (canCherryPick)
                    menu.Add(Popup(repo, App.Text("CommitCM.CherryPickMultiple"), () => new ViewModels.CherryPick(repo, selected)));
                if (canMerge)
                    menu.Add(Popup(repo, App.Text("CommitCM.MergeMultiple"), () => new ViewModels.MergeMultiple(repo, selected)));
                if (canCherryPick || canMerge)
                    menu.Add(MenuSeparator.Instance);
            }

            menu.Add(new MenuItem(App.Text("CommitCM.SaveAsPatch"), async () =>
            {
                if (await Dialogs.OpenFolder(null) is not { } folder)
                    return;
                try
                {
                    var succ = false;
                    for (var i = 0; i < selected.Count; i++)
                    {
                        succ = await repo.SaveCommitAsPatchAsync(selected[i], folder, i);
                        if (!succ)
                            break;
                    }
                    if (succ)
                        repo.SendNotification(App.Text("SaveAsPatchSuccess"));
                }
                catch (Exception exception)
                {
                    repo.SendNotification($"Failed to save as patch: {exception.Message}", true);
                }
            }));
            menu.Add(MenuSeparator.Instance);

            menu.Add(new Submenu(App.Text("Copy"),
            [
                new MenuItem(App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject"), () =>
                {
                    var builder = new StringBuilder();
                    foreach (var c in selected)
                        builder.Append(c.SHA.AsSpan(0, 10)).Append(" - ").AppendLine(c.Subject);
                    Clipboard.SetText(builder.ToString());
                }),
                MenuSeparator.Instance,
                new MenuItem(App.Text("CommitCM.CopySHA"), () => Clipboard.SetText(Lines(selected, c => c.SHA))),
                new MenuItem(App.Text("CommitCM.CopySubject"), () => Clipboard.SetText(Lines(selected, c => c.Subject))),
                new MenuItem(App.Text("CommitCM.CopyCommitMessage"), async () =>
                {
                    var messages = new List<string>();
                    foreach (var c in selected)
                        messages.Add(await vm.GetCommitFullMessageAsync(c));
                    Clipboard.SetText(string.Join("\n-----\n", messages));
                }),
            ]));
            return menu;
        }

        private static IReadOnlyList<MenuEntry> Single(ViewModels.Repository repo, ViewModels.Histories vm, Models.Commit commit)
        {
            var current = repo.CurrentBranch;
            if (current == null)
                return [];

            var menu = new List<MenuEntry>();
            var tags = new List<Models.Tag>();
            var isHead = commit.IsCurrentHead;

            if (commit.HasDecorators)
            {
                foreach (var d in commit.Decorators)
                {
                    switch (d.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                            menu.Add(CurrentBranch(repo, current));
                            break;
                        case Models.DecoratorType.LocalBranchHead:
                            var lb = repo.Branches.Find(x => x.IsLocal && d.Name.Equals(x.Name, StringComparison.Ordinal));
                            if (lb != null)
                                menu.Add(OtherLocalBranch(repo, lb, current, commit.IsMerged));
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            var rb = repo.Branches.Find(x => !x.IsLocal && d.Name.Equals(x.FriendlyName, StringComparison.Ordinal));
                            if (rb != null)
                                menu.Add(RemoteBranch(repo, rb, current, commit.IsMerged));
                            break;
                        case Models.DecoratorType.Tag:
                            var t = repo.Tags.Find(x => d.Name.Equals(x.Name, StringComparison.Ordinal));
                            if (t != null)
                                tags.Add(t);
                            break;
                    }
                }

                if (menu.Count > 0)
                    menu.Add(MenuSeparator.Instance);
            }

            if (tags.Count > 0)
            {
                foreach (var tag in tags)
                    menu.Add(Tag(repo, tag, current));
                menu.Add(MenuSeparator.Instance);
            }

            menu.Add(Popup(repo, App.Text("CreateBranch"), () => new ViewModels.CreateBranch(repo, commit)));
            menu.Add(Popup(repo, App.Text("CreateTag"), () => new ViewModels.CreateTag(repo, commit)));
            menu.Add(MenuSeparator.Instance);

            if (!repo.IsBare)
            {
                var target = commit.GetFriendlyName();
                if (target.Length > 40)
                    target = commit.SHA.Substring(0, 10);

                if (!isHead)
                    menu.Add(Popup(repo, App.Text("CommitCM.Reset", current.Name, target), () => new ViewModels.Reset(repo, current, commit)));

                if (!commit.IsMerged)
                {
                    menu.Add(Popup(repo, App.Text("CommitCM.Rebase", current.Name, target), () => new ViewModels.Rebase(repo, current, commit)));
                    menu.Add(new MenuItem(App.Text("BranchCM.Merge", target, current.Name), () =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(MergeOf(repo, commit, current));
                    }));
                }

                menu.Add(new MenuItem(App.Text("CommitCM.CherryPick"), async () => await vm.CherryPickAsync(commit)));
                menu.Add(Popup(repo, App.Text("CommitCM.Revert"), () => new ViewModels.Revert(repo, commit)));

                if (!isHead)
                    menu.Add(Popup(repo, App.Text("CommitCM.Checkout"), () => new ViewModels.CheckoutDetached(repo, commit)));

                menu.Add(MenuSeparator.Instance);
                var manually = App.Text("CommitCM.InteractiveRebase.Manually", current.Name, target);
                if (commit.IsMerged && commit.Parents.Count > 0)
                {
                    var rebase = new List<MenuEntry>();
                    if (!isHead)
                        rebase.AddRange([NotPorted(manually, "Interactive rebase"), MenuSeparator.Instance]);
                    rebase.AddRange(
                    [
                        NotPorted(App.Text("CommitCM.InteractiveRebase.Reword"), "Interactive rebase"),
                        NotPorted(App.Text("CommitCM.InteractiveRebase.Edit"), "Interactive rebase"),
                        NotPorted(App.Text("CommitCM.InteractiveRebase.Squash"), "Interactive rebase"),
                        NotPorted(App.Text("CommitCM.InteractiveRebase.Fixup"), "Interactive rebase"),
                        NotPorted(App.Text("CommitCM.InteractiveRebase.Drop"), "Interactive rebase"),
                    ]);
                    menu.Add(new Submenu(App.Text("CommitCM.InteractiveRebase"), rebase));
                }
                else
                {
                    menu.Add(NotPorted(manually, "Interactive rebase"));
                }
                menu.Add(MenuSeparator.Instance);
            }

            if (!isHead)
            {
                if (current.Ahead.Contains(commit.SHA))
                {
                    var upstream = repo.Branches.Find(x => x.FullName.Equals(current.Upstream, StringComparison.Ordinal));
                    if (upstream != null)
                    {
                        menu.Add(Popup(repo, App.Text("CommitCM.PushRevision", commit.SHA.Substring(0, 10), upstream.FriendlyName),
                            () => new ViewModels.PushRevision(repo, commit, upstream)));
                        menu.Add(MenuSeparator.Instance);
                    }
                }

                // Upstream adds the head to the selection: the details then
                // compare the two.
                menu.Add(new MenuItem(App.Text("CommitCM.CompareWithHead"), async () =>
                {
                    var head = await vm.CompareWithHeadAsync(commit);
                    if (head != null)
                        vm.SelectedCommits = [commit, head];
                }));
                if (repo.LocalChangesCount > 0)
                    menu.Add(new MenuItem(App.Text("CommitCM.CompareWithWorktree"), () => vm.CompareWithWorktree(commit)));
                menu.Add(MenuSeparator.Instance);
            }

            menu.Add(new MenuItem(App.Text("CommitCM.SaveAsPatch"), async () =>
            {
                if (await Dialogs.OpenFolder(null) is not { } folder)
                    return;
                try
                {
                    if (await repo.SaveCommitAsPatchAsync(commit, folder))
                        repo.SendNotification(App.Text("SaveAsPatchSuccess"));
                }
                catch (Exception exception)
                {
                    repo.SendNotification($"Failed to save as patch: {exception.Message}", true);
                }
            }));
            menu.Add(Popup(repo, App.Text("Archive"), () => new ViewModels.Archive(repo, commit)));
            menu.Add(MenuSeparator.Instance);

            var actions = repo.GetCustomActions(Models.CustomActionScope.Commit);
            if (actions.Count > 0)
            {
                var custom = new List<MenuEntry>();
                foreach (var action in actions)
                {
                    // Upstream's green GLOBAL badge, in the title: a
                    // native item holds text.
                    var (dup, label) = action;
                    var title = label.IsGlobal ? $"{label.Name} (GLOBAL)" : label.Name;
                    custom.Add(new MenuItem(title, async () => await repo.ExecCustomActionAsync(dup, commit)));
                }
                menu.Add(new Submenu(App.Text("CommitCM.CustomAction"), custom));
                menu.Add(MenuSeparator.Instance);
            }

            menu.Add(new Submenu(App.Text("Copy"),
            [
                new MenuItem(App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject"),
                    () => Clipboard.SetText($"{commit.SHA.AsSpan(0, 10)} - {commit.Subject}")),
                MenuSeparator.Instance,
                new MenuItem(App.Text("CommitCM.CopySHA"), () => Clipboard.SetText(commit.SHA)),
                new MenuItem(App.Text("CommitCM.CopySubject"), () => Clipboard.SetText(commit.Subject)),
                new MenuItem(App.Text("CommitCM.CopyCommitMessage"), async () => Clipboard.SetText(await vm.GetCommitFullMessageAsync(commit))),
                new MenuItem(App.Text("CommitCM.CopyAuthor"), () => Clipboard.SetText(commit.Author.ToString())),
                new MenuItem(App.Text("CommitCM.CopyCommitter"), () => Clipboard.SetText(commit.Committer.ToString())),
                new MenuItem(App.Text("CommitCM.CopyAuthorTime"), () => Clipboard.SetText(Models.DateTimeFormat.Format(commit.AuthorTime))),
                new MenuItem(App.Text("CommitCM.CopyCommitterTime"), () => Clipboard.SetText(Models.DateTimeFormat.Format(commit.CommitterTime))),
            ]));
            return menu;
        }

        /// <summary>Upstream merges the commit's first branch or tag, by
        /// name, else the commit itself.</summary>
        private static ViewModels.Popup MergeOf(ViewModels.Repository repo, Models.Commit commit, Models.Branch current)
        {
            foreach (var d in commit.Decorators)
            {
                if (d.Type == Models.DecoratorType.LocalBranchHead
                    && repo.Branches.Find(x => x.IsLocal && x.Name.Equals(d.Name, StringComparison.Ordinal)) is { } b)
                    return new ViewModels.Merge(repo, b, current.Name, false);
                if (d.Type == Models.DecoratorType.RemoteBranchHead
                    && repo.Branches.Find(x => !x.IsLocal && x.FriendlyName.Equals(d.Name, StringComparison.Ordinal)) is { } rb)
                    return new ViewModels.Merge(repo, rb, current.Name, false);
                if (d.Type == Models.DecoratorType.Tag
                    && repo.Tags.Find(x => x.Name.Equals(d.Name, StringComparison.Ordinal)) is { } t)
                    return new ViewModels.Merge(repo, t, current.Name);
            }
            return new ViewModels.Merge(repo, commit, current.Name);
        }

        private static MenuEntry CurrentBranch(ViewModels.Repository repo, Models.Branch current)
        {
            var menu = new List<MenuEntry> { Visibility(repo, current), MenuSeparator.Instance };
            if (!string.IsNullOrEmpty(current.Upstream))
            {
                var upstream = current.Upstream.Substring(13);
                menu.Add(new MenuItem(App.Text("BranchCM.FastForward", upstream), async () =>
                {
                    var b = repo.Branches.Find(x => x.FriendlyName == upstream);
                    if (b != null && repo.CanCreatePopup())
                        await repo.ShowAndStartPopupAsync(new ViewModels.Merge(repo, b, current.Name, true));
                })
                { Enabled = current.Ahead.Count == 0 && current.Behind.Count > 0 });
                menu.Add(Popup(repo, App.Text("BranchCM.Pull", upstream), () => new ViewModels.Pull(repo, null)));
            }
            menu.Add(Popup(repo, App.Text("BranchCM.Push", current.Name), () => new ViewModels.Push(repo, current), repo.Remotes.Count > 0));
            menu.Add(Popup(repo, App.Text("BranchCM.Rename", current.Name), () => new ViewModels.RenameBranch(repo, current)));
            menu.Add(MenuSeparator.Instance);
            GitFlowFinish(menu, repo, current);
            menu.Add(new MenuItem(App.Text("BranchCM.CopyName"), () => Clipboard.SetText(current.Name)));
            return new Submenu(current.Name, menu);
        }

        private static MenuEntry OtherLocalBranch(ViewModels.Repository repo, Models.Branch branch, Models.Branch current, bool merged)
        {
            var menu = new List<MenuEntry> { Visibility(repo, branch), MenuSeparator.Instance };
            if (!repo.IsBare)
            {
                if (string.IsNullOrEmpty(branch.WorktreePath))
                {
                    menu.Add(new MenuItem(App.Text("BranchCM.Checkout", branch.Name), async () => await repo.CheckoutBranchAsync(branch)));
                    menu.Add(Popup(repo, App.Text("BranchCM.CheckoutAsWorktree", branch.Name), () => new ViewModels.CheckoutAsWorktree(repo, branch)));
                }
                else
                {
                    menu.Add(new MenuItem(App.Text("BranchCM.SwitchToWorktree", branch.Name), async () => await repo.CheckoutBranchAsync(branch)));
                }
                menu.Add(Popup(repo, App.Text("BranchCM.Merge", branch.Name, current.Name), () => new ViewModels.Merge(repo, branch, current.Name, false), !merged));
            }
            else
            {
                menu.Add(new MenuItem(App.Text("BranchCM.SwitchToWorktree", branch.Name), async () => await repo.CheckoutBranchAsync(branch)));
            }
            menu.Add(Popup(repo, App.Text("BranchCM.Push", branch.Name), () => new ViewModels.Push(repo, branch), repo.Remotes.Count > 0));
            menu.Add(Popup(repo, App.Text("BranchCM.Rename", branch.Name), () => new ViewModels.RenameBranch(repo, branch)));
            menu.Add(Popup(repo, App.Text("BranchCM.Delete", branch.Name), () => new ViewModels.DeleteBranch(repo, branch)));
            menu.Add(MenuSeparator.Instance);
            if (!repo.IsBare)
                GitFlowFinish(menu, repo, branch);
            menu.Add(NotPorted(App.Text("BranchCM.CompareWithSpecial", current.Name), "Comparing branches"));
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("BranchCM.CopyName"), () => Clipboard.SetText(branch.Name)));
            return new Submenu(branch.Name, menu);
        }

        private static MenuEntry RemoteBranch(ViewModels.Repository repo, Models.Branch branch, Models.Branch current, bool merged)
        {
            var name = branch.FriendlyName;
            return new Submenu(name,
            [
                Visibility(repo, branch),
                MenuSeparator.Instance,
                new MenuItem(App.Text("BranchCM.Checkout", name), async () => await repo.CheckoutBranchAsync(branch)) { Enabled = !repo.IsBare },
                Popup(repo, App.Text("BranchCM.CheckoutAsWorktree", name), () => new ViewModels.CheckoutRemoteBranchAsWorktree(repo, branch)),
                Popup(repo, App.Text("BranchCM.Merge", name, current.Name), () => new ViewModels.Merge(repo, branch, current.Name, false), !merged),
                Popup(repo, App.Text("BranchCM.Delete", name), () => new ViewModels.DeleteBranch(repo, branch)),
                MenuSeparator.Instance,
                NotPorted(App.Text("BranchCM.CompareWithSpecial", current.Name), "Comparing branches"),
                MenuSeparator.Instance,
                new MenuItem(App.Text("BranchCM.CopyName"), () => Clipboard.SetText(name)),
            ]);
        }

        private static MenuEntry Tag(ViewModels.Repository repo, Models.Tag tag, Models.Branch current) =>
            new Submenu(tag.Name,
            [
                Visibility(repo, tag),
                MenuSeparator.Instance,
                Popup(repo, App.Text("TagCM.Push", tag.Name), () => new ViewModels.PushTag(repo, tag), repo.Remotes.Count > 0),
                Popup(repo, App.Text("TagCM.Delete", tag.Name), () => new ViewModels.DeleteTag(repo, tag)),
                MenuSeparator.Instance,
                NotPorted(App.Text("BranchCM.CompareWithSpecial", current.Name), "Comparing branches"),
                MenuSeparator.Instance,
                new MenuItem(App.Text("TagCM.CopyName"), () => Clipboard.SetText(tag.Name)),
            ]);

        private static void GitFlowFinish(List<MenuEntry> menu, ViewModels.Repository repo, Models.Branch branch)
        {
            var type = repo.GetGitFlowType(branch);
            if (type == Models.GitFlowBranchType.None)
                return;
            menu.Add(Popup(repo, App.Text("BranchCM.Finish", branch.Name), () => new ViewModels.GitFlowFinish(repo, branch, type)));
            menu.Add(MenuSeparator.Instance);
        }

        /// <summary>
        /// Upstream's "Visibility in Graph" row of two toggles (filter in,
        /// hide) inside the menu. A native menu holds items, not buttons:
        /// it's a submenu of the three states, as radio items.
        /// </summary>
        private static MenuEntry Visibility(ViewModels.Repository repo, object target)
        {
            var mode = new ViewModels.FilterModeInGraph(repo, target);
            return new Submenu(App.Text("Repository.FilterCommits"),
            [
                new MenuItem(App.Text("Repository.FilterCommits.Default"), () =>
                {
                    mode.IsFiltered = false;
                    mode.IsExcluded = false;
                })
                { Checked = !mode.IsFiltered && !mode.IsExcluded, Radio = true },
                new MenuItem(App.Text("Repository.FilterCommits.Include"), () => mode.IsFiltered = true) { Checked = mode.IsFiltered, Radio = true },
                new MenuItem(App.Text("Repository.FilterCommits.Exclude"), () => mode.IsExcluded = true) { Checked = mode.IsExcluded, Radio = true },
            ]);
        }

        /// <summary>An item showing upstream's popup, when the page can
        /// show one.</summary>
        private static MenuItem Popup(ViewModels.Repository repo, string title, Func<ViewModels.Popup> popup, bool enabled = true) =>
            new(title, () =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(popup());
            })
            { Enabled = enabled };

        /// <summary>An item for one of upstream's dialogs or windows, which
        /// aren't ported yet: it says so.</summary>
        private static MenuItem NotPorted(string title, string what) =>
            new(title, () => _ = Dialogs.Alert(null, $"{what} isn't ported to mitsuami yet.", "", [App.Text("Sure")]));

        private static string Lines(List<Models.Commit> commits, Func<Models.Commit, string> line)
        {
            var builder = new StringBuilder();
            foreach (var c in commits)
                builder.AppendLine(line(c));
            return builder.ToString();
        }
    }
}
