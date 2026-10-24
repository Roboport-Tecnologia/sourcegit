using System;
using System.Collections.Generic;

using Mitsuamia;

using static SourceGit.Views.CommitMenu;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's context menus of the sidebar's rows: branches and remotes
    /// (<c>BranchTree.axaml.cs</c>), tags (<c>TagsView.axaml.cs</c>),
    /// submodules (<c>SubmodulesView.axaml.cs</c>) and worktrees
    /// (<c>Repository.axaml.cs</c>), built as one opens, for the row
    /// right-clicked, or for the selection when that row is in it, as the
    /// history's are. Comparing isn't ported: it says so.
    /// </summary>
    public static class SidebarMenus
    {
        /// <summary>Upstream's <c>OnTreeContextRequested</c>: a remote's
        /// menu, a branch's, or, for several branches (a folder's too), the
        /// menu for all of them.</summary>
        public static IReadOnlyList<MenuEntry> Branches(ViewModels.Repository repo, IReadOnlyList<ViewModels.BranchTreeNode> nodes)
        {
            if (repo.Settings == null || nodes.Count == 0)
                return [];
            if (nodes is [{ Backend: Models.Remote remote }])
                return Tidy(Remote(repo, remote));

            var branches = new List<Models.Branch>();
            foreach (var node in nodes)
                Collect(branches, node);
            if (branches.Count == 1)
                return Tidy(branches[0].IsLocal ? LocalBranch(repo, branches[0]) : RemoteBranch(repo, branches[0]));

            var menu = new List<MenuEntry>();
            if (branches.Count == 2)
                menu.Add(NotPorted(App.Text("BranchCM.CompareTwo"), "Comparing branches"));
            if (branches.Find(x => x.IsCurrent) == null)
            {
                menu.Add(new MenuItem(App.Text("BranchCM.MergeMultiBranches", branches.Count), () => repo.MergeMultipleBranches(branches)));
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("BranchCM.DeleteMultiBranches", branches.Count),
                    () => repo.DeleteMultipleBranches(branches, branches[0].IsLocal)));
            }
            return Tidy(menu);
        }

        /// <summary>The branches in a node: itself, or every branch under
        /// a folder.</summary>
        public static void Collect(List<Models.Branch> branches, ViewModels.BranchTreeNode node)
        {
            if (node.Backend is Models.Branch branch)
            {
                if (!branches.Contains(branch))
                    branches.Add(branch);
                return;
            }
            foreach (var child in node.Children)
                Collect(branches, child);
        }

        private static List<MenuEntry> LocalBranch(ViewModels.Repository repo, Models.Branch branch)
        {
            var current = repo.CurrentBranch;
            var menu = new List<MenuEntry>();
            var upstream = !string.IsNullOrEmpty(branch.Upstream) ? repo.Branches.Find(x => x.FullName.Equals(branch.Upstream, StringComparison.Ordinal)) : null;
            var push = Popup(repo, App.Text("BranchCM.Push", branch.Name), () => new ViewModels.Push(repo, branch), repo.Remotes.Count > 0);

            if (branch.IsCurrent)
            {
                if (!repo.IsBare && upstream != null)
                {
                    menu.Add(Started(repo, App.Text("BranchCM.FastForward", upstream.FriendlyName),
                        () => new ViewModels.Merge(repo, upstream, branch.Name, true), branch.Ahead.Count == 0 && branch.Behind.Count > 0));
                    menu.Add(MenuSeparator.Instance);
                    menu.Add(Popup(repo, App.Text("BranchCM.Pull", upstream.FriendlyName), () => new ViewModels.Pull(repo, null)));
                }
                menu.Add(push);
                menu.Add(MenuSeparator.Instance);

                var type = repo.GetGitFlowType(branch);
                if (type != Models.GitFlowBranchType.None)
                {
                    menu.Add(Popup(repo, App.Text("BranchCM.Finish", branch.Name), () => new ViewModels.GitFlowFinish(repo, branch, type), !repo.IsBare));
                    menu.Add(MenuSeparator.Instance);
                }

                if (upstream != null)
                    menu.Add(NotPorted(App.Text("BranchCM.CompareWithSpecial", upstream.FriendlyName), "Comparing branches"));
                menu.Add(NotPorted(App.Text("BranchCM.CompareWith"), "Comparing branches"));
            }
            else
            {
                var hasNoWorktree = string.IsNullOrEmpty(branch.WorktreePath);
                menu.Add(new MenuItem(App.Text(hasNoWorktree ? "BranchCM.Checkout" : "BranchCM.SwitchToWorktree", branch.Name),
                    async () => await repo.CheckoutBranchAsync(branch))
                { Enabled = !repo.IsBare || !hasNoWorktree });
                if (hasNoWorktree)
                    menu.Add(Popup(repo, App.Text("BranchCM.CheckoutAsWorktree", branch.Name), () => new ViewModels.CheckoutAsWorktree(repo, branch)));
                menu.Add(MenuSeparator.Instance);

                if (upstream != null && hasNoWorktree)
                {
                    menu.Add(Started(repo, App.Text("BranchCM.FastForward", upstream.FriendlyName),
                        () => new ViewModels.ResetWithoutCheckout(repo, branch, upstream), branch.Ahead.Count == 0 && branch.Behind.Count > 0));
                    menu.Add(MenuSeparator.Instance);
                    menu.Add(Started(repo, App.Text("BranchCM.FetchInto", upstream.FriendlyName, branch.Name),
                        () => new ViewModels.FetchInto(repo, branch, upstream), branch.Ahead.Count == 0));
                }
                menu.Add(push);

                if (!repo.IsBare)
                {
                    menu.Add(Popup(repo, App.Text("BranchCM.Merge", branch.Name, current.Name), () => new ViewModels.Merge(repo, branch, current.Name, false)));
                    menu.Add(Popup(repo, App.Text("BranchCM.Rebase", current.Name, branch.Name), () => new ViewModels.Rebase(repo, current, branch)));
                    menu.Add(MenuSeparator.Instance);
                    menu.Add(Rebase(repo, current, branch, branch.Name));

                    var type = repo.GetGitFlowType(branch);
                    if (type != Models.GitFlowBranchType.None)
                    {
                        menu.Add(MenuSeparator.Instance);
                        menu.Add(Popup(repo, App.Text("BranchCM.Finish", branch.Name), () => new ViewModels.GitFlowFinish(repo, branch, type), !repo.IsBare || !hasNoWorktree));
                    }
                }

                if (hasNoWorktree && repo.GetSelectedCommitInHistory() is { } selected && !selected.SHA.Equals(branch.Head, StringComparison.Ordinal))
                {
                    menu.Add(MenuSeparator.Instance);
                    menu.Add(Popup(repo, App.Text("BranchCM.ResetToSelectedCommit", branch.Name, selected.SHA.Substring(0, 10)),
                        () => new ViewModels.ResetWithoutCheckout(repo, branch, selected)));
                }

                menu.Add(MenuSeparator.Instance);
                menu.Add(NotPorted(App.Text("BranchCM.CompareWithHead"), "Comparing branches"));
                if (upstream != null)
                    menu.Add(NotPorted(App.Text("BranchCM.CompareWithSpecial", upstream.FriendlyName), "Comparing branches"));
                menu.Add(NotPorted(App.Text("BranchCM.CompareWith"), "Comparing branches"));
            }

            if (!branch.IsDetachedHead)
            {
                menu.Add(MenuSeparator.Instance);
                menu.Add(Description(repo, branch));
                menu.Add(Popup(repo, App.Text("BranchCM.Rename", branch.Name), () => new ViewModels.RenameBranch(repo, branch)));
                menu.Add(Popup(repo, App.Text("BranchCM.Delete", branch.Name), () => new ViewModels.DeleteBranch(repo, branch), !branch.IsCurrent));
            }

            menu.Add(MenuSeparator.Instance);
            menu.Add(Popup(repo, App.Text("CreateBranch"), () => new ViewModels.CreateBranch(repo, branch)));
            menu.Add(Popup(repo, App.Text("CreateTag"), () => new ViewModels.CreateTag(repo, branch)));
            if (upstream != null
                && repo.Remotes.Find(x => x.Name.Equals(upstream.Remote, StringComparison.Ordinal)) is { } remote
                && remote.TryGetCreatePullRequestURL(out var url, upstream.Name))
                menu.Add(new MenuItem(App.Text("BranchCM.CreatePRForUpstream", upstream.FriendlyName), () => Native.OS.OpenBrowser(url)));
            menu.Add(MenuSeparator.Instance);
            CustomActions(menu, repo, Models.CustomActionScope.Branch, "BranchCM.CustomAction", branch);

            if (!repo.IsBare)
            {
                var remoteBranches = repo.Branches.FindAll(b => !b.IsLocal);
                if (remoteBranches.Count > 0)
                    menu.Add(Popup(repo, App.Text("BranchCM.Tracking"), () => new ViewModels.SetUpstream(repo, branch, remoteBranches)));
            }
            menu.Add(Popup(repo, App.Text("Archive"), () => new ViewModels.Archive(repo, branch)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("BranchCM.CopyName"), () => Clipboard.SetText(branch.Name)));
            return menu;
        }

        private static List<MenuEntry> RemoteBranch(ViewModels.Repository repo, Models.Branch branch)
        {
            var name = branch.FriendlyName;
            var menu = new List<MenuEntry>
            {
                new MenuItem(App.Text("BranchCM.Checkout", name), async () => await repo.CheckoutBranchAsync(branch)) { Enabled = !repo.IsBare },
                Popup(repo, App.Text("BranchCM.CheckoutAsWorktree", name), () => new ViewModels.CheckoutRemoteBranchAsWorktree(repo, branch)),
                MenuSeparator.Instance,
            };

            if (repo.CurrentBranch is { } current)
            {
                menu.Add(Popup(repo, App.Text("BranchCM.PullInto", name, current.Name), () => new ViewModels.Pull(repo, branch)));
                menu.Add(Popup(repo, App.Text("BranchCM.Merge", name, current.Name), () => new ViewModels.Merge(repo, branch, current.Name, false)));
                menu.Add(Popup(repo, App.Text("BranchCM.Rebase", current.Name, name), () => new ViewModels.Rebase(repo, current, branch)));
                menu.Add(MenuSeparator.Instance);
                menu.Add(Rebase(repo, current, branch, name));
                menu.Add(MenuSeparator.Instance);
                menu.Add(NotPorted(App.Text("BranchCM.CompareWithHead"), "Comparing branches"));
                menu.Add(NotPorted(App.Text("BranchCM.CompareWith"), "Comparing branches"));
            }

            menu.Add(MenuSeparator.Instance);
            menu.Add(Description(repo, branch));
            menu.Add(Popup(repo, App.Text("BranchCM.Delete", name), () => new ViewModels.DeleteBranch(repo, branch)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(Popup(repo, App.Text("CreateBranch"), () => new ViewModels.CreateBranch(repo, branch)));
            menu.Add(Popup(repo, App.Text("CreateTag"), () => new ViewModels.CreateTag(repo, branch)));
            if (repo.Remotes.Find(x => x.Name.Equals(branch.Remote, StringComparison.Ordinal)) is { } remote
                && remote.TryGetCreatePullRequestURL(out var url, branch.Name))
                menu.Add(new MenuItem(App.Text("BranchCM.CreatePR"), () => Native.OS.OpenBrowser(url)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(Popup(repo, App.Text("Archive"), () => new ViewModels.Archive(repo, branch)));
            menu.Add(MenuSeparator.Instance);
            CustomActions(menu, repo, Models.CustomActionScope.Branch, "BranchCM.CustomAction", branch);
            menu.Add(new MenuItem(App.Text("BranchCM.CopyName"), () => Clipboard.SetText(name)));
            return menu;
        }

        private static List<MenuEntry> Remote(ViewModels.Repository repo, Models.Remote remote)
        {
            var menu = new List<MenuEntry>();
            if (remote.TryGetVisitURL(out var visit))
            {
                menu.Add(new MenuItem(App.Text("RemoteCM.OpenInBrowser"), () => Native.OS.OpenBrowser(visit)));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(Popup(repo, App.Text("RemoteCM.Fetch"), () => new ViewModels.Fetch(repo, remote)));
            menu.Add(Started(repo, App.Text("RemoteCM.Prune"), () => new ViewModels.PruneRemote(repo, remote)));
            menu.Add(MenuSeparator.Instance);
            if (ViewModels.Preferences.Instance.EnableAutoFetch)
            {
                // Upstream's ON or OFF at the item's end: a native menu
                // checks the item.
                menu.Add(new MenuItem(App.Text("RemoteCM.EnableAutoFetch"), async () => await repo.ToggleAutoFetchOnRemoteAsync(remote))
                { Checked = !remote.DisableAutoFetch });
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(Popup(repo, App.Text("RemoteCM.Edit"), () => new ViewModels.EditRemote(repo, remote)));
            menu.Add(Popup(repo, App.Text("RemoteCM.SetPushURL"), () => new ViewModels.SetPushURL(repo, remote)));
            menu.Add(Popup(repo, App.Text("RemoteCM.Delete"), () => new ViewModels.DeleteRemote(repo, remote)));
            menu.Add(MenuSeparator.Instance);
            CustomActions(menu, repo, Models.CustomActionScope.Remote, "RemoteCM.CustomAction", remote);
            menu.Add(new MenuItem(App.Text("RemoteCM.CopyURL"), () => Clipboard.SetText(remote.URL)));
            return menu;
        }

        /// <summary>Upstream's <c>OnTagsContextMenuRequested</c>: a tag's
        /// menu, or deleting several.</summary>
        public static IReadOnlyList<MenuEntry> Tags(ViewModels.Repository repo, IReadOnlyList<Models.Tag> tags)
        {
            if (tags.Count > 1)
            {
                var several = new List<MenuEntry>();
                if (tags.Count == 2)
                    several.Add(NotPorted(App.Text("TagCM.CompareTwo"), "Comparing tags"));
                several.Add(Popup(repo, App.Text("TagCM.DeleteMultiple", tags.Count), () => new ViewModels.DeleteMultipleTags(repo, [.. tags])));
                return several;
            }
            if (tags is not [var tag])
                return [];

            var menu = new List<MenuEntry> { Popup(repo, App.Text("CreateBranch"), () => new ViewModels.CreateBranch(repo, tag)) };
            if (repo.CurrentBranch != null && !tag.SHA.Equals(repo.CurrentBranch.Head, StringComparison.Ordinal))
                menu.Add(Popup(repo, App.Text("TagCM.Checkout"), () => new ViewModels.CheckoutDetached(repo, tag)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(Popup(repo, App.Text("TagCM.Push", tag.Name), () => new ViewModels.PushTag(repo, tag), repo.Remotes.Count > 0));
            if (repo.CurrentBranch is { IsDetachedHead: false } current)
                menu.Add(Popup(repo, App.Text("TagCM.Merge", tag.Name, current.Name), () => new ViewModels.Merge(repo, tag, current.Name)));
            menu.Add(Popup(repo, App.Text("TagCM.Delete", tag.Name), () => new ViewModels.DeleteTag(repo, tag)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(NotPorted(App.Text("TagCM.CompareWithHead"), "Comparing tags"));
            menu.Add(NotPorted(App.Text("TagCM.CompareWith"), "Comparing tags"));
            menu.Add(MenuSeparator.Instance);
            menu.Add(Popup(repo, App.Text("Archive"), () => new ViewModels.Archive(repo, tag)));
            menu.Add(MenuSeparator.Instance);
            CustomActions(menu, repo, Models.CustomActionScope.Tag, "TagCM.CustomAction", tag);

            var copy = new List<MenuEntry>
            {
                new MenuItem(App.Text("TagCM.Copy.Name"), () => Clipboard.SetText(tag.Name)),
                new MenuItem(App.Text("TagCM.Copy.Message"), () => Clipboard.SetText(tag.Message)) { Enabled = !string.IsNullOrEmpty(tag.Message) },
            };
            if (tag.Creator is { Email: { Length: > 0 } })
                copy.Add(new MenuItem(App.Text("TagCM.Copy.Tagger"), () => Clipboard.SetText(tag.Creator.ToString())));
            menu.Add(new Submenu(App.Text("Copy"), copy));
            return menu;
        }

        public static IReadOnlyList<MenuEntry> Submodule(ViewModels.Repository repo, Models.Submodule submodule)
        {
            var inited = submodule.Status != Models.SubmoduleStatus.NotInited;
            return
            [
                new MenuItem(App.Text("Submodule.Open"), () => repo.OpenSubmodule(submodule.Path)) { Enabled = inited },
                MenuSeparator.Instance,
                Popup(repo, App.Text("Submodule.Update"), () => new ViewModels.UpdateSubmodules(repo, submodule)),
                Popup(repo, App.Text("Submodule.SetURL"), () => new ViewModels.ChangeSubmoduleUrl(repo, submodule)),
                Popup(repo, App.Text("Submodule.SetBranch"), () => new ViewModels.SetSubmoduleBranch(repo, submodule)),
                Popup(repo, App.Text("Submodule.Move"), () => new ViewModels.MoveSubmodule(repo, submodule)),
                Popup(repo, App.Text("Submodule.Deinit"), () => new ViewModels.DeinitSubmodule(repo, submodule.Path), inited),
                Popup(repo, App.Text("Submodule.Remove"), () => new ViewModels.DeleteSubmodule(repo, submodule.Path)),
                MenuSeparator.Instance,
                NotPorted(App.Text("Submodule.Histories"), "A file's history"),
                MenuSeparator.Instance,
                new Submenu(App.Text("Copy"),
                [
                    new MenuItem(App.Text("CommitDetail.Info.SHA"), () => Clipboard.SetText(submodule.SHA)),
                    new MenuItem(App.Text("Submodule.CopyBranch"), () => Clipboard.SetText(submodule.Branch)),
                    new MenuItem(App.Text("Submodule.CopyPath"), () => Clipboard.SetText(submodule.Path)),
                    new MenuItem(App.Text("Submodule.URL"), () => Clipboard.SetText(submodule.URL)),
                ]),
            ];
        }

        public static IReadOnlyList<MenuEntry> Worktree(ViewModels.Repository repo, ViewModels.Worktree worktree) =>
        [
            new MenuItem(App.Text("Worktree.Open"), () => repo.OpenWorktree(worktree)),
            MenuSeparator.Instance,
            worktree.IsLocked
                ? new MenuItem(App.Text("Worktree.Unlock"), async () => await repo.UnlockWorktreeAsync(worktree))
                : new MenuItem(App.Text("Worktree.Lock"), async () => await repo.LockWorktreeAsync(worktree)) { Enabled = !worktree.IsMain },
            Popup(repo, App.Text("Worktree.Remove"), () => new ViewModels.RemoveWorktree(repo, worktree), !worktree.IsCurrent && !worktree.IsMain),
            MenuSeparator.Instance,
            new MenuItem(App.Text("Worktree.CopyPath"), () => Clipboard.SetText(worktree.FullPath)),
        ];

        /// <summary>Rebasing the current branch onto another by hand: the
        /// interactive rebase on the other's head.</summary>
        private static MenuItem Rebase(ViewModels.Repository repo, Models.Branch current, Models.Branch onto, string name) =>
            new(App.Text("BranchCM.InteractiveRebase.Manually", current.Name, name), async () =>
            {
                var commit = await new Commands.QuerySingleCommit(repo.FullPath, onto.Head).GetResultAsync();
                if (commit != null)
                    InteractiveRebase.Show(repo, commit);
            })
            { Enabled = !current.Head.Equals(onto.Head, StringComparison.Ordinal) };

        /// <summary>Upstream reads the description before showing its
        /// popup.</summary>
        private static MenuItem Description(ViewModels.Repository repo, Models.Branch branch) =>
            new(App.Text("BranchCM.EditDescription", branch.Name), async () =>
            {
                var desc = await new Commands.Config(repo.FullPath).GetAsync($"branch.{branch.Name}.description");
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.EditBranchDescription(repo, branch, desc));
            });

        /// <summary>An item for a popup upstream starts right away, without
        /// a form.</summary>
        private static MenuItem Started(ViewModels.Repository repo, string title, Func<ViewModels.Popup> popup, bool enabled = true) =>
            new(title, async () =>
            {
                if (repo.CanCreatePopup())
                    await repo.ShowAndStartPopupAsync(popup());
            })
            { Enabled = enabled };

        internal static void CustomActions(List<MenuEntry> menu, ViewModels.Repository repo, Models.CustomActionScope scope, string title, object target)
        {
            var actions = repo.GetCustomActions(scope);
            if (actions.Count == 0)
                return;
            var custom = new List<MenuEntry>();
            foreach (var (action, label) in actions)
            {
                // As the history's: upstream's GLOBAL badge in the title.
                var name = label.IsGlobal ? $"{label.Name} (GLOBAL)" : label.Name;
                custom.Add(new MenuItem(name, async () => await repo.ExecCustomActionAsync(action, target)));
            }
            menu.Add(new Submenu(App.Text(title), custom));
            menu.Add(MenuSeparator.Instance);
        }

        /// <summary>Upstream's menus can leave a separator first, last or
        /// doubled (a current branch without an upstream starts with Push,
        /// then one); they're left out.</summary>
        private static List<MenuEntry> Tidy(List<MenuEntry> menu)
        {
            var tidy = new List<MenuEntry>();
            foreach (var entry in menu)
            {
                if (entry is MenuSeparator && (tidy.Count == 0 || tidy[^1] is MenuSeparator))
                    continue;
                tidy.Add(entry);
            }
            if (tidy.Count > 0 && tidy[^1] is MenuSeparator)
                tidy.RemoveAt(tidy.Count - 1);
            return tidy;
        }
    }
}
