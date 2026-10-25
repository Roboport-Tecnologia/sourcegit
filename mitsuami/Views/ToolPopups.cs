using System;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The rest of upstream's popups, its views of the same names: adding
    /// a remote, submodule or worktree, pruning worktrees, cleaning up,
    /// applying a patch, initializing or trusting a repository, Git-Flow,
    /// Git LFS and custom actions with inputs. As the other forms, in
    /// native controls.
    /// </summary>
    public static partial class Popups
    {
        /// <summary>One of these popups' forms, or null.</summary>
        private static Func<Window, Node> ToolForm(ViewModels.Popup popup, out string title)
        {
            switch (popup)
            {
                case ViewModels.AddRemote vm:
                    title = App.Text("Remote.AddTitle");
                    return sheet => AddRemoteForm(sheet, vm);
                case ViewModels.AddSubmodule vm:
                    title = App.Text("Submodule.Add");
                    return sheet => Rows(
                        (App.Text("URL"), UrlField(sheet, vm, nameof(vm.Url), () => vm.Url, v => vm.Url = v, App.Text("RepositoryURL"))),
                        (App.Text("Submodule.RelativePath"), TextField(sheet, vm, nameof(vm.RelativePath), () => vm.RelativePath,
                            v => vm.RelativePath = v, App.Text("Submodule.RelativePath.Placeholder"))),
                        ("", Check(vm, nameof(vm.Recursive), App.Text("Submodule.FetchNested"), () => vm.Recursive, v => vm.Recursive = v)));
                case ViewModels.AddWorktree vm:
                    title = App.Text("AddWorktree");
                    return sheet => Rows(
                        (App.Text("AddWorktree.Location"), PathField(sheet, vm, nameof(vm.WorktreePath), () => vm.WorktreePath,
                            v => vm.WorktreePath = v, App.Text("AddWorktree.Location.Placeholder"), folders: true)),
                        (App.Text("AddWorktree.Name"), TextField(sheet, vm, nameof(vm.BranchName), () => vm.BranchName,
                            v => vm.BranchName = v, App.Text("AddWorktree.Name.Placeholder"))));
                case ViewModels.PruneWorktrees:
                    title = App.Text("PruneWorktrees");
                    return _ => new Text(App.Text("PruneWorktrees.Tip"));
                case ViewModels.Cleanup:
                    title = App.Text("Repository.Clean");
                    return _ => new Text(App.Text("Repository.CleanTips"));
                case ViewModels.Apply vm:
                    title = App.Text("Apply.Title");
                    return sheet => ApplyForm(sheet, vm);
                case ViewModels.Init vm:
                    title = App.Text("Init");
                    return _ =>
                    {
                        var form = Rows((App.Text("Init.Path"), new Text(vm.TargetPath) { Selectable = true }));
                        form.Add(Field("", Note($"{App.Text("Init.ErrorMessageTip").TrimEnd()} {vm.Reason}")));
                        form.Add(Field("", new Text(App.Text("Init.CommandTip"))));
                        return form;
                    };
                case ViewModels.TrustRepository vm:
                    title = App.Text("TrustRepository");
                    return _ =>
                    {
                        var form = Rows((App.Text("TrustRepository.Path"), new Text(vm.TargetPath) { Selectable = true }));
                        form.Add(Field("", Note($"{App.Text("TrustRepository.Description").TrimEnd()} {vm.Reason}")));
                        form.Add(Field("", new Text(vm.Command) { Selectable = true }));
                        return form;
                    };
                case ViewModels.InitGitFlow vm:
                    title = App.Text("GitFlow.Init");
                    return sheet => InitGitFlowForm(sheet, vm);
                case ViewModels.GitFlowStart vm:
                    title = App.Text(vm.Type switch
                    {
                        Models.GitFlowBranchType.Release => "GitFlow.StartReleaseTitle",
                        Models.GitFlowBranchType.Hotfix => "GitFlow.StartHotfixTitle",
                        _ => "GitFlow.StartFeatureTitle",
                    });
                    return sheet => GitFlowStartForm(sheet, vm);
                case ViewModels.LFSFetch vm:
                    title = App.Text("GitLFS.Fetch.Title");
                    return _ => LFSRemoteForm(vm, nameof(vm.SelectedRemote), vm.Remotes, () => vm.SelectedRemote, r => vm.SelectedRemote = r,
                        "GitLFS.Fetch.Tips");
                case ViewModels.LFSPull vm:
                    title = App.Text("GitLFS.Pull.Title");
                    return _ => LFSRemoteForm(vm, nameof(vm.SelectedRemote), vm.Remotes, () => vm.SelectedRemote, r => vm.SelectedRemote = r,
                        "GitLFS.Pull.Tips");
                case ViewModels.LFSPush vm:
                    title = App.Text("GitLFS.Push.Title");
                    return _ => LFSRemoteForm(vm, nameof(vm.SelectedRemote), vm.Remotes, () => vm.SelectedRemote, r => vm.SelectedRemote = r,
                        "GitLFS.Push.Tips");
                case ViewModels.LFSPrune:
                    title = App.Text("GitLFS.Prune");
                    return _ => new Text(App.Text("GitLFS.Prune.Tips"));
                case ViewModels.LFSTrackCustomPattern vm:
                    title = App.Text("GitLFS.AddTrackPattern.Title");
                    return sheet => Rows(
                        (App.Text("GitLFS.AddTrackPattern.Pattern"), TextField(sheet, vm, nameof(vm.Pattern), () => vm.Pattern, v => vm.Pattern = v)),
                        ("", Check(vm, nameof(vm.IsFilename), App.Text("GitLFS.AddTrackPattern.IsFilename"), () => vm.IsFilename,
                            v => vm.IsFilename = v)));
                case ViewModels.ExecuteCustomAction vm:
                    title = vm.CustomAction.Name;
                    return sheet => CustomActionForm(sheet, vm);
                default:
                    title = null;
                    return null;
            }
        }

        /// <summary>Upstream's: the name, the URL with its protocol
        /// switcher, an SSH key for an SSH URL, and fetching without tags.</summary>
        private static Node AddRemoteForm(Window sheet, ViewModels.AddRemote vm)
        {
            var key = FileField(sheet, vm, nameof(vm.SSHKey), () => vm.SSHKey, v => vm.SSHKey = v, App.Text("SSHKey.Placeholder"));
            var form = Rows(
                (App.Text("Remote.Name"), TextField(sheet, vm, nameof(vm.Name), () => vm.Name, v => vm.Name = v, App.Text("Remote.Name.Placeholder"))),
                (App.Text("Remote.URL"), UrlField(sheet, vm, nameof(vm.Url), () => vm.Url, v => vm.Url = v, App.Text("Remote.URL.Placeholder"))));
            var keyRow = Field(App.Text("SSHKey"), key);
            Bind.OneWay(vm, nameof(vm.UseSSH), () => vm.UseSSH, ssh => keyRow.IsVisible = ssh, owner: keyRow);
            form.Add(keyRow);
            form.Add(Field("", Check(vm, nameof(vm.FetchWithoutTags), App.Text("Fetch.NoTags"), () => vm.FetchWithoutTags,
                v => vm.FetchWithoutTags = v)));
            return form;
        }

        /// <summary>Upstream's: a file or the clipboard (radio buttons), the
        /// patch file, the whitespace mode, ignoring whitespace, a three-way
        /// merge. The clipboard is the platform's, read as it applies.</summary>
        private static Node ApplyForm(Window sheet, ViewModels.Apply vm)
        {
            vm.Clipboard = s_clipboard;
            var source = new RadioGroup(App.Text("Apply.Source"))
            {
                Options = [App.Text("Apply.Source.File"), App.Text("Apply.Source.Clipboard")],
            };
            Bind.OneWay(vm, nameof(vm.FromClipboard), () => vm.FromClipboard, c => source.SelectedIndex = c ? 1 : 0, owner: source);
            source.Changed += i => vm.FromClipboard = i == 1;
            var file = FileField(sheet, vm, nameof(vm.PatchFile), () => vm.PatchFile, v => vm.PatchFile = v, App.Text("Apply.File.Placeholder"));
            var fileRow = Field("", file);
            Bind.OneWay(vm, nameof(vm.FromClipboard), () => vm.FromClipboard, c => fileRow.IsVisible = !c, owner: fileRow);
            var mode = Choice(App.Text("Apply.WS"), vm, nameof(vm.SelectedWhiteSpaceMode), Models.ApplyWhiteSpaceMode.Supported,
                m => $"{m.Name}  ({m.Desc})", () => vm.SelectedWhiteSpaceMode, m => vm.SelectedWhiteSpaceMode = m);
            Bind.OneWay(vm, nameof(vm.IgnoreWhiteSpace), () => vm.IgnoreWhiteSpace, ignore => mode.IsEnabled = !ignore, owner: mode);

            var form = new Column { Gap = 8, AlignItems = Align.Stretch };
            form.Add(TopField(App.Text("Apply.Source"), source));
            form.Add(fileRow);
            form.Add(Field(App.Text("Apply.WS"), mode));
            form.Add(Field("", Check(vm, nameof(vm.IgnoreWhiteSpace), App.Text("Apply.IgnoreWS"), () => vm.IgnoreWhiteSpace,
                v => vm.IgnoreWhiteSpace = v, "--ignore-whitespace")));
            form.Add(Field("", Check(vm, nameof(vm.ThreeWayMerge), App.Text("Apply.3Way"), () => vm.ThreeWayMerge,
                v => vm.ThreeWayMerge = v, "--3way")));
            return form;
        }

        private static readonly ClipboardText s_clipboard = new();

        /// <summary>The platform's clipboard, as <c>Apply</c> reads it.</summary>
        private sealed class ClipboardText : Avalonia.Input.Platform.IClipboard
        {
            public Task<string> TryGetTextAsync() => Clipboard.GetText();
        }

        /// <summary>Upstream's: the two base branches, a line, then the
        /// branch prefixes and an optional tag prefix.</summary>
        private static Node InitGitFlowForm(Window sheet, ViewModels.InitGitFlow vm)
        {
            var form = Rows(
                (App.Text("GitFlow.ProductionBranch"), TextField(sheet, vm, nameof(vm.Production), () => vm.Production, v => vm.Production = v)),
                (App.Text("GitFlow.DevelopBranch"), TextField(sheet, vm, nameof(vm.Develop), () => vm.Develop, v => vm.Develop = v)));
            form.Add(new Separator());
            form.Add(Field(App.Text("GitFlow.FeaturePrefix"),
                TextField(sheet, vm, nameof(vm.FeaturePrefix), () => vm.FeaturePrefix, v => vm.FeaturePrefix = v)));
            form.Add(Field(App.Text("GitFlow.ReleasePrefix"),
                TextField(sheet, vm, nameof(vm.ReleasePrefix), () => vm.ReleasePrefix, v => vm.ReleasePrefix = v)));
            form.Add(Field(App.Text("GitFlow.HotfixPrefix"),
                TextField(sheet, vm, nameof(vm.HotfixPrefix), () => vm.HotfixPrefix, v => vm.HotfixPrefix = v)));
            form.Add(Field(App.Text("GitFlow.TagPrefix"),
                TextField(sheet, vm, nameof(vm.TagPrefix), () => vm.TagPrefix, v => vm.TagPrefix = v, App.Text("Optional"))));
            return form;
        }

        /// <summary>Upstream's: the name after its prefix (inside the field
        /// there; beside it here), and the branch it starts at.</summary>
        private static Node GitFlowStartForm(Window sheet, ViewModels.GitFlowStart vm)
        {
            var name = new Row { Gap = 4, AlignItems = Align.Center };
            var prefix = new Text(vm.Prefix) { Color = Color.SecondaryLabel };
            prefix.Shrink = 0;
            name.Add(prefix);
            var input = TextField(sheet, vm, nameof(vm.Name), () => vm.Name, v => vm.Name = v, App.Text("GitFlow.StartPlaceholder"));
            input.Grow = 1;
            name.Add(input);
            var start = Choice(App.Text("GitFlow.StartAt"), vm, nameof(vm.StartPoint), vm.LocalBranches, b => b.Name,
                () => vm.StartPoint, b => vm.StartPoint = b);
            return Rows((App.Text("GitFlow.StartName"), name), (App.Text("GitFlow.StartAt"), start));
        }

        /// <summary>Upstream's LFS fetch, pull and push: the remote, and
        /// what it does.</summary>
        private static Node LFSRemoteForm(System.ComponentModel.INotifyPropertyChanged vm, string property,
            System.Collections.Generic.List<Models.Remote> remotes, Func<Models.Remote> get, Action<Models.Remote> set, string tips)
        {
            var form = Rows((App.Text("GitLFS.Remote"), Choice(App.Text("GitLFS.Remote"), vm, property, remotes, r => r.Name, get, set)));
            form.Add(Field("", Note(App.Text(tips))));
            return form;
        }

        /// <summary>Upstream's: what it runs on, then a control for each of
        /// the action's inputs (text, a path, a check, a choice, a branch),
        /// a choice's description under it.</summary>
        private static Node CustomActionForm(Window sheet, ViewModels.ExecuteCustomAction vm)
        {
            var target = vm.Target switch
            {
                Models.Branch branch => IconLabel("Branch", branch.FriendlyName),
                Models.Commit commit => CommitLabel(commit),
                Models.Tag tag => IconLabel("Tag", tag.Name),
                Models.Remote remote => IconLabel("Remote", remote.Name),
                _ => IconLabel("Repositories", App.Text("ExecuteCustomAction.Repository")),
            };
            var form = Rows((App.Text("ExecuteCustomAction.Target"), target));
            foreach (var parameter in vm.ControlParameters)
            {
                switch (parameter)
                {
                    case ViewModels.CustomActionControlTextBox text:
                    {
                        var input = new TextInput { Placeholder = text.Placeholder ?? "", AccessibleName = text.Label, Value = text.Text ?? "" };
                        input.Changed += v => text.Text = v;
                        form.Add(Field(text.Label, input));
                        break;
                    }
                    case ViewModels.CustomActionControlPathSelector path:
                    {
                        var field = PathField(sheet, path, nameof(path.Path), () => path.Path, v => path.Path = v, path.Placeholder ?? "",
                            folders: path.IsFolder);
                        form.Add(Field(path.Label, field));
                        break;
                    }
                    case ViewModels.CustomActionControlCheckBox check:
                    {
                        var box = new Checkbox(check.Label) { IsChecked = check.IsChecked };
                        if (!string.IsNullOrEmpty(check.ToolTip))
                            box.ToolTip = check.ToolTip;
                        box.Changed += v => check.IsChecked = v;
                        form.Add(Field("", box));
                        break;
                    }
                    case ViewModels.CustomActionControlComboBox combo:
                    {
                        var select = new Select(combo.Label) { MinWidth = 0, Options = combo.Options.ToArray() };
                        select.SelectedIndex = Math.Max(combo.Options.IndexOf(combo.Value), 0);
                        select.Changed += i =>
                        {
                            if (i < combo.Options.Count)
                                combo.Value = combo.Options[i];
                        };
                        form.Add(Field(combo.Label, select));
                        if (!string.IsNullOrEmpty(combo.Description))
                            form.Add(Field("", Note(combo.Description)));
                        break;
                    }
                    case ViewModels.CustomActionControlBranchSelector branches:
                    {
                        var select = Choice(branches.Label, branches, nameof(branches.SelectedBranch), branches.Branches, b => b.FriendlyName,
                            () => branches.SelectedBranch, b => branches.SelectedBranch = b);
                        form.Add(Field(branches.Label, select));
                        if (!string.IsNullOrEmpty(branches.Description))
                            form.Add(Field("", Note(branches.Description)));
                        break;
                    }
                }
            }
            return form;
        }
    }
}
