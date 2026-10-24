using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The repository's popups, upstream's views of the same names: what the
    /// history's and the branches' menus open. Each is upstream's form, its
    /// label column and rows, in native controls: its combo boxes are the
    /// platform's drop-downs (an option's description after its name, as
    /// the welcome page's group picker has it), its radio buttons a radio
    /// group, a branch, tag or commit an icon with its name, or a commit's
    /// short SHA and subject.
    /// </summary>
    public static partial class Popups
    {
        /// <summary>A repository popup's form, or null when it isn't one.</summary>
        private static Func<Window, Node> RepositoryForm(ViewModels.Popup popup, out string title)
        {
            switch (popup)
            {
                case ViewModels.CreateBranch vm:
                    title = App.Text("CreateBranch.Title");
                    return sheet => CreateBranchForm(sheet, vm);
                case ViewModels.CreateTag vm:
                    title = App.Text("CreateTag.Title");
                    return sheet => CreateTagForm(sheet, vm);
                case ViewModels.Reset vm:
                    title = App.Text("Reset");
                    return _ => ResetForm(vm);
                case ViewModels.Rebase vm:
                    title = App.Text("Rebase");
                    return _ => RebaseForm(vm);
                case ViewModels.Merge vm:
                    title = App.Text("Merge");
                    return _ => MergeForm(vm);
                case ViewModels.MergeMultiple vm:
                    title = App.Text("MergeMultiple");
                    return _ => MergeMultipleForm(vm);
                case ViewModels.CherryPick vm:
                    title = App.Text("CherryPick");
                    return _ => CherryPickForm(vm);
                case ViewModels.Revert vm:
                    title = App.Text("Revert");
                    return _ => Rows(
                        (App.Text("Revert.Commit"), CommitLabel(vm.Target)),
                        ("", Check(vm, nameof(vm.AutoCommit), App.Text("Revert.CommitChanges"), () => vm.AutoCommit, v => vm.AutoCommit = v)));
                case ViewModels.CheckoutDetached vm:
                    title = App.Text("CheckoutDetached");
                    return _ => CheckoutDetachedForm(vm);
                case ViewModels.PushRevision vm:
                    title = App.Text("Push.Revision.Title");
                    return _ => Rows(
                        (App.Text("Push.Revision"), CommitLabel(vm.Revision)),
                        (App.Text("Push.To"), RefLabel(vm.RemoteBranch)),
                        ("", Check(vm, nameof(vm.Force), App.Text("Push.Force"), () => vm.Force, v => vm.Force = v, "--force-with-lease")));
                case ViewModels.Archive vm:
                    title = App.Text("Archive.Title");
                    return sheet => Rows(
                        (App.Text("Archive.Revision"), RefLabel(vm.BasedOn)),
                        (App.Text("Archive.File"), SaveField(sheet, vm, nameof(vm.SaveFile), () => vm.SaveFile, v => vm.SaveFile = v,
                            App.Text("Archive.File.Placeholder"))));
                case ViewModels.Pull vm:
                    title = App.Text("Pull.Title");
                    return _ => PullForm(vm);
                case ViewModels.Push vm:
                    title = App.Text("Push.Title");
                    return sheet => PushForm(sheet, vm);
                case ViewModels.RenameBranch vm:
                    title = App.Text("RenameBranch");
                    return sheet => Rows(
                        (App.Text("RenameBranch.Target"), RefLabel(vm.Target, vm.Target.Name)),
                        (App.Text("RenameBranch.Name"), TextField(sheet, vm, nameof(vm.Name), () => vm.Name, v => vm.Name = v,
                            App.Text("RenameBranch.Name.Placeholder"))));
                case ViewModels.DeleteBranch vm:
                    title = App.Text("DeleteBranch");
                    return _ => DeleteBranchForm(vm);
                case ViewModels.CheckoutAsWorktree vm:
                    title = App.Text("CheckoutAsWorktree");
                    return sheet => Rows(
                        (App.Text("CheckoutAsWorktree.LocalBranch"), RefLabel(vm.Branch, vm.Branch.Name)),
                        (App.Text("CheckoutAsWorktree.WorktreePath"), PathField(sheet, vm, nameof(vm.WorktreePath), () => vm.WorktreePath,
                            v => vm.WorktreePath = v, App.Text("CheckoutAsWorktree.WorktreePath.Placeholder"), folders: true)));
                case ViewModels.CheckoutRemoteBranchAsWorktree vm:
                    title = App.Text("CheckoutAsWorktree");
                    return sheet => Rows(
                        (App.Text("CheckoutAsWorktree.RemoteBranch"), RefLabel(vm.RemoteBranch)),
                        (App.Text("CheckoutAsWorktree.WorktreePath"), PathField(sheet, vm, nameof(vm.WorktreePath), () => vm.WorktreePath,
                            v => vm.WorktreePath = v, App.Text("CheckoutAsWorktree.WorktreePath.Placeholder"), folders: true)),
                        (App.Text("CheckoutAsWorktree.NewBranchName"), TextField(sheet, vm, nameof(vm.BranchName), () => vm.BranchName,
                            v => vm.BranchName = v, App.Text("CheckoutAsWorktree.NewBranchName.Placeholder"))),
                        ("", Check(vm, nameof(vm.Tracking), App.Text("CheckoutAsWorktree.Tracking"), () => vm.Tracking, v => vm.Tracking = v)));
                case ViewModels.PushTag vm:
                    title = App.Text("PushTag");
                    return _ => PushTagForm(vm);
                case ViewModels.DeleteTag vm:
                    title = App.Text("DeleteTag");
                    return _ => Rows(
                        (App.Text("DeleteTag.Tag"), RefLabel(vm.Target)),
                        ("", Check(vm, nameof(vm.PushToRemotes), App.Text("DeleteTag.WithRemote"), () => vm.PushToRemotes, v => vm.PushToRemotes = v)));
                case ViewModels.GitFlowFinish vm:
                    title = App.Text(vm.Type switch
                    {
                        Models.GitFlowBranchType.Feature => "GitFlow.FinishFeature",
                        Models.GitFlowBranchType.Release => "GitFlow.FinishRelease",
                        _ => "GitFlow.FinishHotfix",
                    });
                    return _ => Rows(
                        (App.Text("GitFlow.FinishTarget"), RefLabel(vm.Branch, vm.Branch.Name)),
                        ("", Check(vm, nameof(vm.RebaseBeforeMerging), App.Text("GitFlow.FinishWithRebase"), () => vm.RebaseBeforeMerging,
                            v => vm.RebaseBeforeMerging = v, "--rebase")),
                        ("", Check(vm, nameof(vm.Squash), App.Text("GitFlow.FinishWithSquash"), () => vm.Squash, v => vm.Squash = v, "--squash")),
                        ("", Check(vm, nameof(vm.KeepBranch), App.Text("GitFlow.KeepBranchAfterFinish"), () => vm.KeepBranch, v => vm.KeepBranch = v, "--keep")));
                default:
                    return SidebarForm(popup, out title);
            }
        }

        private static Node CreateBranchForm(Window sheet, ViewModels.CreateBranch vm)
        {
            var form = Rows(
                (App.Text("CreateBranch.BasedOn"), RefLabel(vm.BasedOn)),
                (App.Text("CreateBranch.Name"), TextField(sheet, vm, nameof(vm.Name), () => vm.Name, v => vm.Name = v,
                    App.Text("CreateBranch.Name.Placeholder"))));

            // Upstream asks what to do with local changes only when the new
            // branch is checked out.
            var changes = TopField(App.Text("CreateBranch.LocalChanges"),
                LocalChanges(vm, nameof(vm.DealWithLocalChanges), () => vm.DealWithLocalChanges, v => vm.DealWithLocalChanges = v));
            Bind.OneWay(vm, nameof(vm.CheckoutAfterCreated), () => vm.HasLocalChanges && vm.CheckoutAfterCreated,
                v => changes.IsVisible = v, owner: changes);
            form.Add(changes);

            var overwrite = Check(vm, nameof(vm.AllowOverwrite), App.Text("CreateBranch.OverwriteExisting"), () => vm.AllowOverwrite,
                v => vm.AllowOverwrite = v);
            Bind.OneWay(vm, nameof(vm.OverrideTip), () => vm.OverrideTip ?? "", tip => overwrite.ToolTip = tip, owner: overwrite);
            form.Add(Field("", overwrite));
            if (!vm.IsBareRepository)
                form.Add(Field("", Check(vm, nameof(vm.CheckoutAfterCreated), App.Text("CreateBranch.Checkout"), () => vm.CheckoutAfterCreated,
                    v => vm.CheckoutAfterCreated = v)));
            return form;
        }

        private static Node CreateTagForm(Window sheet, ViewModels.CreateTag vm)
        {
            var form = Rows(
                (App.Text("CreateTag.BasedOn"), RefLabel(vm.BasedOn)),
                (App.Text("CreateTag.Name"), TextField(sheet, vm, nameof(vm.TagName), () => vm.TagName, v => vm.TagName = v,
                    App.Text("CreateTag.Name.Placeholder"))));

            // Upstream's two radio buttons, annotated or lightweight.
            var type = new RadioGroup(App.Text("CreateTag.Type"))
            {
                Options = [App.Text("CreateTag.Type.Annotated"), App.Text("CreateTag.Type.Lightweight")],
            };
            Bind.OneWay(vm, nameof(vm.Annotated), () => vm.Annotated, a => type.SelectedIndex = a ? 0 : 1, owner: type);
            type.Changed += i => vm.Annotated = i == 0;
            form.Add(TopField(App.Text("CreateTag.Type"), type));

            // The message and signing are an annotated tag's.
            var message = new TextArea { Placeholder = App.Text("CreateTag.Message.Placeholder"), AccessibleName = App.Text("CreateTag.Message") };
            message.Height = 64;
            Bind.Value(message, vm, nameof(vm.Message), () => vm.Message, v => vm.Message = v);
            var messageRow = TopField(App.Text("CreateTag.Message"), message);
            var sign = Field("", Check(vm, nameof(vm.SignTag), App.Text("CreateTag.GPGSign"), () => vm.SignTag, v => vm.SignTag = v));
            Bind.OneWay(vm, nameof(vm.Annotated), () => vm.Annotated, a =>
            {
                messageRow.IsVisible = a;
                sign.IsVisible = a;
            }, owner: form);
            form.Add(messageRow);
            form.Add(sign);
            form.Add(Field("", Check(vm, nameof(vm.PushToRemotes), App.Text("CreateTag.PushToAllRemotes"), () => vm.PushToRemotes,
                v => vm.PushToRemotes = v)));
            return form;
        }

        private static Node ResetForm(ViewModels.Reset vm)
        {
            var modes = Models.ResetMode.Supported;
            var mode = Choice(App.Text("Reset.Mode"), vm, nameof(vm.SelectedMode), modes, m => $"{m.Name}  ({m.Desc})",
                () => vm.SelectedMode, m => vm.SelectedMode = m);
            return Rows(
                (App.Text("Reset.Target"), RefLabel(vm.Current, vm.Current.Name)),
                (App.Text("Reset.MoveTo"), CommitLabel(vm.To)),
                (App.Text("Reset.Mode"), mode));
        }

        private static Node RebaseForm(ViewModels.Rebase vm)
        {
            var form = Rows(
                (App.Text("Reset.Target"), RefLabel(vm.Current, vm.Current.Name)),
                (App.Text("Rebase.On"), RefLabel(vm.On)),
                ("", Check(vm, nameof(vm.AutoStash), App.Text("Rebase.AutoStash"), () => vm.AutoStash, v => vm.AutoStash = v, "--autostash")),
                ("", Check(vm, nameof(vm.NoVerify), App.Text("Rebase.NoVerify"), () => vm.NoVerify, v => vm.NoVerify = v, "--no-verify")));
            form.Add(Field("", Testing(vm, nameof(vm.TestingState), () => vm.TestingState switch
            {
                ViewModels.RebaseTestingState.Testing => (Tested.Testing, App.Text("Rebase.Test")),
                ViewModels.RebaseTestingState.NoConflicts => (Tested.Fine, App.Text("Rebase.Test.OK")),
                ViewModels.RebaseTestingState.WillCauseConflicts => (Tested.Conflicts, App.Text("Rebase.Test.WillCauseConflicts")),
                ViewModels.RebaseTestingState.UnknownError => (Tested.Conflicts, App.Text("Rebase.Test.UnknownError")),
                _ => (Tested.Not, ""),
            })));
            return form;
        }

        private static Node MergeForm(ViewModels.Merge vm)
        {
            var mode = Choice(App.Text("Merge.Mode"), vm, nameof(vm.Mode), Models.MergeMode.Supported, m => $"{m.Name}  ({m.Desc})",
                () => vm.Mode, m => vm.Mode = m);
            var form = Rows(
                (App.Text("Merge.Source"), RefLabel(vm.Source)),
                (App.Text("Merge.Into"), IconLabel("Branch", vm.Into)),
                (App.Text("Merge.Mode"), mode));
            var edit = Field("", Check(vm, nameof(vm.Edit), App.Text("Merge.Edit"), () => vm.Edit, v => vm.Edit = v, "--edit"));
            Bind.OneWay(vm, nameof(vm.CanEditMessage), () => vm.CanEditMessage, v => edit.IsVisible = v, owner: edit);
            form.Add(edit);
            form.Add(Field("", Testing(vm, nameof(vm.TestingState), () => vm.TestingState switch
            {
                ViewModels.MergeTestingState.Testing => (Tested.Testing, App.Text("Merge.Test")),
                ViewModels.MergeTestingState.NoConflicts => (Tested.Fine, App.Text("Merge.Test.NoConflicts")),
                ViewModels.MergeTestingState.WillCauseConflicts => (Tested.Conflicts, App.Text("Merge.Test.WillCauseConflicts")),
                ViewModels.MergeTestingState.UnknownError => (Tested.Conflicts, App.Text("Merge.Test.UnknownError")),
                _ => (Tested.Not, ""),
            })));
            return form;
        }

        private static Node MergeMultipleForm(ViewModels.MergeMultiple vm)
        {
            var strategy = Choice(App.Text("MergeMultiple.Strategy"), vm, nameof(vm.Strategy), Models.MergeStrategy.ForMultiple,
                s => $"{s.Name}  ({s.Desc})", () => vm.Strategy, s => vm.Strategy = s);
            var form = Rows();
            form.Add(TopField(App.Text("MergeMultiple.Targets"), Targets(vm.Targets)));
            form.Add(Field(App.Text("MergeMultiple.Strategy"), strategy));
            form.Add(Field("", Check(vm, nameof(vm.AutoCommit), App.Text("MergeMultiple.CommitChanges"), () => vm.AutoCommit, v => vm.AutoCommit = v)));
            return form;
        }

        private static Node CherryPickForm(ViewModels.CherryPick vm)
        {
            var form = Rows();
            form.Add(TopField(App.Text("CherryPick.Commit"), Targets(vm.Targets.Cast<object>().ToList())));

            // A merge commit's mainline: which parent it's picked against.
            if (vm.IsMergeCommit)
            {
                var parents = vm.ParentsForMergeCommit;
                var mainline = new Select(App.Text("CherryPick.Mainline"))
                {
                    Options = parents.Select(p => $"{ShortSha(p.SHA)}  {p.Subject}").ToArray(),
                    ToolTip = App.Text("CherryPick.Mainline.Tips"),
                };
                Bind.OneWay(vm, nameof(vm.MainlineForMergeCommit), () => vm.MainlineForMergeCommit, i => mainline.SelectedIndex = i, owner: mainline);
                mainline.Changed += i => vm.MainlineForMergeCommit = i;
                form.Add(Field(App.Text("CherryPick.Mainline"), mainline));
            }

            form.Add(Field("", Check(vm, nameof(vm.AutoCommit), App.Text("CherryPick.CommitChanges"), () => vm.AutoCommit, v => vm.AutoCommit = v)));
            var append = Check(vm, nameof(vm.AppendSourceToMessage), App.Text("CherryPick.AppendSourceToMessage"), () => vm.AppendSourceToMessage,
                v => vm.AppendSourceToMessage = v, "-x");
            Bind.OneWay(vm, nameof(vm.AutoCommit), () => vm.AutoCommit, v => append.IsEnabled = v, owner: append);
            form.Add(Field("", append));
            return form;
        }

        private static Node CheckoutDetachedForm(ViewModels.CheckoutDetached vm)
        {
            var form = Rows((App.Text("CheckoutDetached.Target"), RefLabel(vm.Target)));
            if (vm.HasLocalChanges)
                form.Add(TopField(App.Text("Checkout.LocalChanges"),
                    LocalChanges(vm, nameof(vm.DealWithLocalChanges), () => vm.DealWithLocalChanges, v => vm.DealWithLocalChanges = v)));
            form.Add(Field("", Warning(App.Text("CheckoutDetached.Warning"))));
            return form;
        }

        private static Node PullForm(ViewModels.Pull vm)
        {
            var form = Rows(
                (App.Text("Pull.Remote"), Choice(App.Text("Pull.Remote"), vm, nameof(vm.SelectedRemote), vm.Remotes, r => r.Name,
                    () => vm.SelectedRemote, r => vm.SelectedRemote = r)),
                (App.Text("Pull.Branch"), Choice(App.Text("Pull.Branch"), vm, nameof(vm.RemoteBranches), () => vm.RemoteBranches,
                    nameof(vm.SelectedBranch), b => b.Name, () => vm.SelectedBranch, b => vm.SelectedBranch = b)),
                (App.Text("Pull.Into"), RefLabel(vm.Current, vm.Current.Name)));
            if (vm.HasLocalChanges)
                form.Add(TopField(App.Text("Pull.LocalChanges"),
                    LocalChanges(vm, nameof(vm.DealWithLocalChanges), () => vm.DealWithLocalChanges, v => vm.DealWithLocalChanges = v)));
            form.Add(Field("", Check(vm, nameof(vm.UseRebase), App.Text("Pull.UseRebase"), () => vm.UseRebase, v => vm.UseRebase = v, "--rebase")));
            return form;
        }

        private static Node PushForm(Window sheet, ViewModels.Push vm)
        {
            // Upstream's remote branch picker, and a button beside it that
            // asks for a new branch's name.
            var to = new Row { Gap = 4, AlignItems = Align.Center };
            var remoteBranch = Choice(App.Text("Push.To"), vm, nameof(vm.RemoteBranches), () => vm.RemoteBranches,
                nameof(vm.SelectedRemoteBranch), b => b.Name, () => vm.SelectedRemoteBranch, b => vm.SelectedRemoteBranch = b);
            remoteBranch.Grow = 1;
            var add = new Button(App.Text("PushToNewBranch")) { Icon = Icons.Get("Branch.Add", 14), IconOnly = true, ToolTip = App.Text("PushToNewBranch") };
            add.Click += () => AskNewBranch(sheet, vm);
            to.Add(remoteBranch);
            to.Add(add);

            var form = Rows(
                (App.Text("Push.Local"), Choice(App.Text("Push.Local"), vm, nameof(vm.LocalBranches), () => vm.LocalBranches,
                    nameof(vm.SelectedLocalBranch), b => b.Name, () => vm.SelectedLocalBranch, b => vm.SelectedLocalBranch = b)),
                (App.Text("Push.Remote"), Choice(App.Text("Push.Remote"), vm, nameof(vm.SelectedRemote), vm.Remotes, r => r.Name,
                    () => vm.SelectedRemote, r => vm.SelectedRemote = r)),
                (App.Text("Push.To"), to));

            var tracking = Field("", Check(vm, nameof(vm.Tracking), App.Text("Push.Tracking"), () => vm.Tracking, v => vm.Tracking = v, "-u"));
            Bind.OneWay(vm, nameof(vm.IsSetTrackOptionVisible), () => vm.IsSetTrackOptionVisible, v => tracking.IsVisible = v, owner: tracking);
            form.Add(tracking);
            if (vm.IsCheckSubmodulesVisible)
                form.Add(Field("", Check(vm, nameof(vm.CheckSubmodules), App.Text("Push.CheckSubmodules"), () => vm.CheckSubmodules,
                    v => vm.CheckSubmodules = v, "--recurse-submodules=check")));
            var tags = Field("", Check(vm, nameof(vm.PushAllTags), App.Text("Push.WithAllTags"), () => vm.PushAllTags, v => vm.PushAllTags = v, "--tags"));
            Bind.OneWay(vm, nameof(vm.SelectedRemote), () => !string.IsNullOrEmpty(vm.SelectedRemote?.URL), v => tags.IsVisible = v, owner: tags);
            form.Add(tags);
            form.Add(Field("", Check(vm, nameof(vm.NoVerify), App.Text("Push.NoVerify"), () => vm.NoVerify, v => vm.NoVerify = v, "--no-verify")));
            form.Add(Field("", Check(vm, nameof(vm.ForcePush), App.Text("Push.Force"), () => vm.ForcePush, v => vm.ForcePush = v, "--force-with-lease")));
            return form;
        }

        /// <summary>Upstream's <c>PushToNewBranch</c> dialog, a sheet on the
        /// push's: a new branch's name on the selected remote.</summary>
        private static void AskNewBranch(Window owner, ViewModels.Push push)
        {
            var vm = new ViewModels.PushToNewBranch(push.SelectedRemote.Name);
            var sheet = new Window(owner, App.Text("PushToNewBranch.Title"), 400);
            var column = new Column { Padding = 20, Gap = 12, AlignItems = Align.Stretch };
            column.Add(new Text(App.Text("PushToNewBranch.Title")) { Style = TextStyle.Headline });
            var name = TextField(sheet, vm, nameof(vm.BranchName), () => vm.BranchName, v => vm.BranchName = v);
            column.Add(Field(App.Text("PushToNewBranch.Remote", vm.Remote), name));
            var errors = new Text { Style = TextStyle.Caption, IsVisible = false };
            column.Add(errors);

            var buttons = new Row { Gap = 8, Justify = Justify.End };
            var cancel = new Button(ButtonText("Cancel")) { Role = ButtonRole.Cancel };
            cancel.Click += sheet.Destroy;
            var sure = new Button(ButtonText("Sure")) { Role = ButtonRole.Default };
            sure.Click += () =>
            {
                if (vm.HasErrors || !vm.Check())
                {
                    var messages = vm.GetErrors(null).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)).ToArray();
                    errors.Value = string.Join("\n", messages);
                    errors.IsVisible = messages.Length > 0;
                    return;
                }
                sheet.Destroy();
                push.PushToNewBranch(vm.BranchName);
            };
            buttons.Add(cancel);
            buttons.Add(sure);
            column.Add(buttons);
            sheet.Content = column;
        }

        private static Node DeleteBranchForm(ViewModels.DeleteBranch vm)
        {
            var target = vm.Target;
            var branch = new Row { Gap = 8, AlignItems = Align.Center };
            branch.Add(RefLabel(target));
            if (target.IsTrackStatusVisible)
                branch.Add(new Text(target.TrackStatusDescription) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });

            var form = Rows((App.Text("DeleteBranch.Branch"), branch));
            if (target.IsLocal)
                form.Add(Field("", Check(vm, nameof(vm.Force), App.Text("DeleteBranch.Force"), () => vm.Force, v => vm.Force = v, "--force")));
            if (vm.Upstream != null)
                form.Add(Field("", Check(vm, nameof(vm.DeleteUpstream), vm.DeleteUpstreamTip, () => vm.DeleteUpstream, v => vm.DeleteUpstream = v)));
            if (!target.IsLocal)
                form.Add(Field("", Warning(App.Text("DeleteBranch.IsRemoteTip"))));
            return form;
        }

        private static Node PushTagForm(ViewModels.PushTag vm)
        {
            var remote = Choice(App.Text("PushTag.Remote"), vm, nameof(vm.SelectedRemote), vm.Remotes, r => r.Name,
                () => vm.SelectedRemote, r => vm.SelectedRemote = r);
            Bind.OneWay(vm, nameof(vm.PushAllRemotes), () => vm.PushAllRemotes, all => remote.IsEnabled = !all, owner: remote);
            return Rows(
                (App.Text("PushTag.Tag"), RefLabel(vm.Target)),
                (App.Text("PushTag.Remote"), remote),
                ("", Check(vm, nameof(vm.PushAllRemotes), App.Text("PushTag.PushAllRemotes"), () => vm.PushAllRemotes, v => vm.PushAllRemotes = v)));
        }

        // Building blocks of upstream's repository forms.

        private static Column Rows(params (string Label, Node Control)[] rows)
        {
            var column = new Column { Gap = 8, AlignItems = Align.Stretch };
            foreach (var (label, control) in rows)
                column.Add(Field(label, control));
            return column;
        }

        /// <summary>A row whose control is taller than a line (radio
        /// buttons, a message, a list): its label at the top, as upstream's.</summary>
        private static Row TopField(string label, Node control)
        {
            var row = Field(label, control);
            row.AlignItems = Align.Start;
            return row;
        }

        private static string ShortSha(string sha) => sha.Length > 10 ? sha[..10] : sha;

        /// <summary>Upstream's SHA colour, as the details show it.</summary>
        private static readonly Color s_sha = Color.Rgba(255, 140, 0);

        /// <summary>A branch, tag or commit as upstream shows what a popup
        /// acts on: its icon, then its name, or a commit's short SHA and
        /// subject.</summary>
        private static Node RefLabel(object target, string name = null)
        {
            switch (target)
            {
                case Models.Commit commit:
                    return CommitLabel(commit);
                case Models.Branch branch:
                    return IconLabel("Branch", name ?? branch.FriendlyName);
                case Models.Tag tag:
                    return IconLabel("Tag", tag.Name);
                default:
                    return new Text(target?.ToString() ?? "");
            }
        }

        private static Row IconLabel(string icon, string text)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            row.Add(new Icon(Icons.Get(icon)) { Size = 14 });
            row.Add(new Text(text) { MaxLines = 1 });
            return row;
        }

        private static Row CommitLabel(Models.Commit commit)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            row.Add(new Icon(Icons.Get("Commit")) { Size = 14 });
            row.Add(new Text(ShortSha(commit.SHA)) { Style = TextStyle.Monospace, Color = s_sha });
            var subject = new Text(commit.Subject) { MaxLines = 1 };
            subject.Shrink = 1;
            row.Add(subject);
            return row;
        }

        /// <summary>Upstream's framed list of what a popup acts on (commits,
        /// branches or tags), from 32 points high: as high as its rows, up
        /// to <paramref name="most"/>. Deleting branches shows how far each
        /// is ahead and behind, as upstream's badge does.</summary>
        private static Node Targets(List<object> targets, float most = 100, bool trackStatus = false)
        {
            var list = new ListView<object>(t => t switch
            {
                Models.Commit c => c.SHA,
                Models.Tag tag => "refs/tags/" + tag.Name,
                _ => ((Models.Branch)t).FullName,
            }, t =>
            {
                var row = new Row { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(0, 6) };
                row.Height = 26;
                row.Add(RefLabel(t));
                if (trackStatus && t is Models.Branch { IsTrackStatusVisible: true } branch)
                    row.Add(new Text(branch.TrackStatusDescription) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                return row;
            }, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = targets,
            };
            list.Height = Math.Clamp(targets.Count * 26 + 8, 32, most);
            return list;
        }

        private static Checkbox Check(INotifyPropertyChanged vm, string property, string label, Func<bool> get, Action<bool> set,
            string tip = null)
        {
            var box = new Checkbox(label);
            if (tip != null)
                box.ToolTip = tip;
            Bind.Checked(box, vm, property, get, set);
            return box;
        }

        /// <summary>A drop-down of fixed items, bound both ways to the
        /// item the view model holds.</summary>
        private static Select Choice<T>(string label, INotifyPropertyChanged vm, string property, IReadOnlyList<T> items,
            Func<T, string> text, Func<T> get, Action<T> set) where T : class =>
            Choice(label, vm, null, () => items, property, text, get, set);

        /// <summary>A drop-down of items the view model may replace (a
        /// remote's branches), bound both ways to the one chosen.</summary>
        private static Select Choice<T>(string label, INotifyPropertyChanged vm, string itemsProperty, Func<IReadOnlyList<T>> items,
            string property, Func<T, string> text, Func<T> get, Action<T> set) where T : class
        {
            // Narrower than its longest option when the sheet is: AppKit
            // truncates the title.
            var select = new Select(label) { MinWidth = 0 };
            IReadOnlyList<T> shown = [];
            void Show()
            {
                shown = items() ?? [];
                select.Options = shown.Select(text).ToArray();
                select.SelectedIndex = Math.Max(IndexOf(shown, get()), 0);
            }
            if (itemsProperty != null)
                Bind.OneWay(vm, itemsProperty, () => items(), _ => Show(), owner: select);
            Bind.OneWay(vm, property, get, _ => Show(), owner: select);
            select.Changed += i =>
            {
                if (i < shown.Count)
                    set(shown[i]);
            };
            return select;
        }

        private static int IndexOf<T>(IReadOnlyList<T> items, T item) where T : class
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], item))
                    return i;
            }
            return -1;
        }

        /// <summary>Upstream's <c>DealWithLocalChangesMethod</c>: its three
        /// radio buttons, in the enum's order.</summary>
        private static RadioGroup LocalChanges(INotifyPropertyChanged vm, string property, Func<Models.DealWithLocalChanges> get,
            Action<Models.DealWithLocalChanges> set)
        {
            var group = new RadioGroup(App.Text("Checkout.LocalChanges"))
            {
                Options =
                [
                    App.Text("DealWithLocalChanges.DoNothing"),
                    App.Text("DealWithLocalChanges.StashAndReapply"),
                    App.Text("DealWithLocalChanges.Discard"),
                ],
            };
            Bind.OneWay(vm, property, get, m => group.SelectedIndex = (int)m, owner: group);
            group.Changed += i => set((Models.DealWithLocalChanges)i);
            return group;
        }

        /// <summary>Upstream's warning: its error icon and the text, in
        /// the platform's warning colour.</summary>
        private static Row Warning(string text)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Start };
            row.Add(new Icon(Icons.Get("Error")) { Size = 14, Color = Color.Warning });
            var message = new Text(text) { Color = Color.Warning };
            message.Shrink = 1;
            row.Add(message);
            return row;
        }

        private enum Tested { Not, Testing, Fine, Conflicts }

        /// <summary>Upstream's test of a merge or rebase, run as the popup
        /// opens: a spinner while it runs, then whether it conflicts.</summary>
        private static Row Testing(INotifyPropertyChanged vm, string property, Func<(Tested State, string Text)> get)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            var spinner = new Spinner();
            var fine = new Icon(Icons.Get("CheckCircled")) { Size = 14, Color = Color.Success };
            var conflicts = new Icon(Icons.Get("Error")) { Size = 14, Color = Color.Warning };
            var text = new Text();
            row.Add(spinner);
            row.Add(fine);
            row.Add(conflicts);
            row.Add(text);
            Bind.OneWay(vm, property, get, state =>
            {
                row.IsVisible = state.State != Tested.Not;
                spinner.IsVisible = state.State == Tested.Testing;
                fine.IsVisible = state.State == Tested.Fine;
                conflicts.IsVisible = state.State == Tested.Conflicts;
                text.Value = state.Text;
            }, owner: row);
            return row;
        }

        /// <summary>A path to save to, with a button that asks the
        /// platform's save dialog, as upstream's archive file field has.</summary>
        private static Row SaveField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get, Action<string> set,
            string placeholder)
        {
            var row = new Row { Gap = 4, AlignItems = Align.Center };
            var input = TextField(sheet, vm, property, get, set, placeholder);
            input.Grow = 1;
            var browse = new Button("…");
            browse.Click += async () =>
            {
                var current = get() ?? "";
                var chosen = await Dialogs.SaveFile(sheet, name: Path.GetFileName(current), startFolder: Path.GetDirectoryName(current) ?? "");
                if (chosen != null)
                    set(chosen);
            };
            row.Add(input);
            row.Add(browse);
            return row;
        }
    }
}
