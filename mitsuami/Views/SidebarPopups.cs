using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The popups the sidebar's menus open, upstream's views of the same
    /// names: a branch's description and tracking branch, fetching, a
    /// remote's settings, deleting several branches or tags, submodules and
    /// worktrees. As the other repository forms, in native controls.
    /// </summary>
    public static partial class Popups
    {
        /// <summary>A sidebar popup's form, or null when it isn't one.</summary>
        private static Func<Window, Node> SidebarForm(ViewModels.Popup popup, out string title)
        {
            switch (popup)
            {
                case ViewModels.EditBranchDescription vm:
                    title = App.Text("EditBranchDescription");
                    return _ => DescriptionForm(vm);
                case ViewModels.SetUpstream vm:
                    title = App.Text("SetUpstream");
                    return _ => SetUpstreamForm(vm);
                case ViewModels.Fetch vm:
                    title = App.Text("Fetch.Title");
                    return _ => FetchForm(vm);
                case ViewModels.FetchInto vm:
                    title = App.Text("Fetch.Title");
                    return _ => FetchIntoForm(vm);
                case ViewModels.PruneRemote vm:
                    title = App.Text("PruneRemote");
                    return _ => Rows((App.Text("PruneRemote.Target"), IconLabel("Remote", vm.Remote.Name)));
                case ViewModels.EditRemote vm:
                    title = App.Text("Remote.EditTitle");
                    return sheet => EditRemoteForm(sheet, vm);
                case ViewModels.SetPushURL vm:
                    title = App.Text("SetPushUrl");
                    return sheet => Rows(
                        (App.Text("SetPushUrl.Remote"), IconLabel("Remote", vm.Remote.Name)),
                        (App.Text("SetPushUrl.URL"), new Text(vm.Remote.URL) { MaxLines = 1, Selectable = true }),
                        (App.Text("SetPushUrl.PushURL"), UrlField(sheet, vm, nameof(vm.PushURL), () => vm.PushURL, v => vm.PushURL = v, "")));
                case ViewModels.DeleteRemote vm:
                    title = App.Text("DeleteRemote");
                    return _ => Rows((App.Text("DeleteRemote.Remote"), IconLabel("Remote", vm.Remote.Name)));
                case ViewModels.ResetWithoutCheckout vm:
                    title = App.Text("ResetWithoutCheckout");
                    return _ => Rows(
                        (App.Text("ResetWithoutCheckout.Target"), IconLabel("Branch", vm.Target.Name)),
                        (App.Text("ResetWithoutCheckout.MoveTo"), RefLabel(vm.To)));
                case ViewModels.DeleteMultipleBranches vm:
                    title = App.Text("DeleteMultiBranch");
                    return _ => DeleteBranchesForm(vm);
                case ViewModels.DeleteMultipleTags vm:
                    title = App.Text("DeleteMultiTags");
                    return _ => DeleteTagsForm(vm);
                case ViewModels.UpdateSubmodules vm:
                    title = App.Text("UpdateSubmodules");
                    return _ => UpdateSubmodulesForm(vm);
                case ViewModels.MoveSubmodule vm:
                    title = App.Text("MoveSubmodule");
                    return sheet => Rows(
                        (App.Text("MoveSubmodule.Submodule"), IconLabel("Submodule", vm.Submodule.Path)),
                        (App.Text("MoveSubmodule.MoveTo"), TextField(sheet, vm, nameof(vm.MoveTo), () => vm.MoveTo, v => vm.MoveTo = v,
                            App.Text("Submodule.RelativePath.Placeholder"))));
                case ViewModels.ChangeSubmoduleUrl vm:
                    title = App.Text("ChangeSubmoduleUrl");
                    return sheet => Rows(
                        (App.Text("ChangeSubmoduleUrl.Submodule"), IconLabel("Submodule", vm.Submodule.Path)),
                        (App.Text("ChangeSubmoduleUrl.URL"), UrlField(sheet, vm, nameof(vm.Url), () => vm.Url, v => vm.Url = v,
                            App.Text("RepositoryURL"))));
                case ViewModels.SetSubmoduleBranch vm:
                    title = App.Text("SetSubmoduleBranch");
                    return sheet => Rows(
                        (App.Text("SetSubmoduleBranch.Submodule"), IconLabel("Submodule", vm.Submodule.Path)),
                        (App.Text("SetSubmoduleBranch.Current"), IconLabel("Branch", vm.Submodule.Branch ?? "")),
                        (App.Text("SetSubmoduleBranch.New"), TextField(sheet, vm, nameof(vm.ChangeTo), () => vm.ChangeTo, v => vm.ChangeTo = v,
                            App.Text("SetSubmoduleBranch.New.Tip"))));
                case ViewModels.DeinitSubmodule vm:
                    title = App.Text("DeinitSubmodule");
                    return _ => Rows(
                        (App.Text("DeinitSubmodule.Path"), IconLabel("Submodule", vm.Submodule)),
                        ("", Check(vm, nameof(vm.Force), App.Text("DeinitSubmodule.Force"), () => vm.Force, v => vm.Force = v, "--force")));
                case ViewModels.DeleteSubmodule vm:
                    title = App.Text("DeleteSubmodule");
                    return _ => Rows((App.Text("DeleteSubmodule.Path"), IconLabel("Submodule", vm.Submodule)));
                case ViewModels.RemoveWorktree vm:
                    title = App.Text("RemoveWorktree");
                    return _ => RemoveWorktreeForm(vm);
                default:
                    return LocalChangesForm(popup, out title);
            }
        }

        /// <summary>Upstream's 120-point text box, in the platform's text
        /// view.</summary>
        private static Node DescriptionForm(ViewModels.EditBranchDescription vm)
        {
            var form = Rows((App.Text("EditBranchDescription.Target"), IconLabel("Branch", vm.Target.FriendlyName)));
            var text = new TextArea { AccessibleName = App.Text("EditBranchDescription") };
            text.Height = 120;
            Bind.Value(text, vm, nameof(vm.Description), () => vm.Description, v => vm.Description = v);
            form.Add(text);
            return form;
        }

        /// <summary>Upstream's branch selector, a searchable drop-down: the
        /// platform's drop-down of the remote branches.</summary>
        private static Node SetUpstreamForm(ViewModels.SetUpstream vm)
        {
            var upstream = Choice(App.Text("SetUpstream.Upstream"), vm, nameof(vm.SelectedRemoteBranch), vm.RemoteBranches,
                b => b.FriendlyName, () => vm.SelectedRemoteBranch, b => vm.SelectedRemoteBranch = b);
            Bind.OneWay(vm, nameof(vm.Unset), () => vm.Unset, unset => upstream.IsEnabled = !unset, owner: upstream);
            return Rows(
                (App.Text("SetUpstream.Local"), IconLabel("Branch", vm.Local.Name)),
                (App.Text("SetUpstream.Upstream"), upstream),
                ("", Check(vm, nameof(vm.Unset), App.Text("SetUpstream.Unset"), () => vm.Unset, v => vm.Unset = v)));
        }

        private static Node FetchForm(ViewModels.Fetch vm)
        {
            var remote = Choice(App.Text("Fetch.Remote"), vm, nameof(vm.SelectedRemote), vm.Remotes, r => r.Name,
                () => vm.SelectedRemote, r => vm.SelectedRemote = r);
            Bind.OneWay(vm, nameof(vm.FetchAllRemotes), () => vm.IsFetchAllRemoteVisible && !vm.FetchAllRemotes,
                v => remote.IsEnabled = v, owner: remote);
            var form = Rows(
                (App.Text("Fetch.Remote"), remote),
                ("", Check(vm, nameof(vm.Force), App.Text("Fetch.Force"), () => vm.Force, v => vm.Force = v, "--force")));
            if (vm.IsFetchAllRemoteVisible)
                form.Add(Field("", Check(vm, nameof(vm.FetchAllRemotes), App.Text("Fetch.AllRemotes"), () => vm.FetchAllRemotes,
                    v => vm.FetchAllRemotes = v, "--all")));
            form.Add(Field("", Check(vm, nameof(vm.NoTags), App.Text("Fetch.NoTags"), () => vm.NoTags, v => vm.NoTags = v, "--no-tags")));
            return form;
        }

        /// <summary>Upstream's line, centred: the upstream, an arrow, the
        /// local branch.</summary>
        private static Node FetchIntoForm(ViewModels.FetchInto vm)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center, Justify = Justify.Center };
            row.Add(IconLabel("Branch", vm.Upstream.FriendlyName));
            row.Add(new Text("→"));
            row.Add(IconLabel("Branch", vm.Local.Name));
            return row;
        }

        private static Node EditRemoteForm(Window sheet, ViewModels.EditRemote vm)
        {
            var form = Rows(
                (App.Text("Remote.Name"), TextField(sheet, vm, nameof(vm.Name), () => vm.Name, v => vm.Name = v, App.Text("Remote.Name.Placeholder"))),
                (App.Text("Remote.URL"), UrlField(sheet, vm, nameof(vm.Url), () => vm.Url, v => vm.Url = v, App.Text("Remote.URL.Placeholder"))));
            var key = Field(App.Text("SSHKey"), FileField(sheet, vm, nameof(vm.SSHKey), () => vm.SSHKey, v => vm.SSHKey = v,
                App.Text("SSHKey.Placeholder")));
            Bind.OneWay(vm, nameof(vm.UseSSH), () => vm.UseSSH, v => key.IsVisible = v, owner: key);
            form.Add(key);
            var reset = Field("", Check(vm, nameof(vm.ResetPushURL), App.Text("Remote.ResetPushURL"), () => vm.ResetPushURL, v => vm.ResetPushURL = v));
            Bind.OneWay(vm, nameof(vm.IsResetPushURLVisible), () => vm.IsResetPushURLVisible, v => reset.IsVisible = v, owner: reset);
            form.Add(reset);
            return form;
        }

        private static Node DeleteBranchesForm(ViewModels.DeleteMultipleBranches vm)
        {
            var form = Rows();
            form.Add(Note(App.Text("DeleteMultiBranch.Tip")));
            form.Add(Targets([.. vm.Targets], 200, trackStatus: true));
            form.Add(Check(vm, nameof(vm.Force), App.Text("DeleteBranch.Force"), () => vm.Force, v => vm.Force = v, "--force"));
            return form;
        }

        private static Node DeleteTagsForm(ViewModels.DeleteMultipleTags vm)
        {
            var form = Rows();
            form.Add(Targets([.. vm.Tags], 200));
            form.Add(Check(vm, nameof(vm.DeleteFromRemote), App.Text("DeleteMultiTags.DeleteFromRemotes"), () => vm.DeleteFromRemote,
                v => vm.DeleteFromRemote = v));
            form.Add(Note(App.Text("DeleteMultiTags.Tip")));
            return form;
        }

        private static Node UpdateSubmodulesForm(ViewModels.UpdateSubmodules vm)
        {
            var target = Choice(App.Text("UpdateSubmodules.Target"), vm, nameof(vm.SelectedSubmodule), vm.Submodules, s => s.Path,
                () => vm.SelectedSubmodule, s => vm.SelectedSubmodule = s);
            Bind.OneWay(vm, nameof(vm.UpdateAll), () => !vm.UpdateAll && !vm.HasPreSelectedSubmodule, v => target.IsEnabled = v, owner: target);
            var form = Rows((App.Text("UpdateSubmodules.Target"), target));
            if (!vm.HasPreSelectedSubmodule)
                form.Add(Field("", Check(vm, nameof(vm.UpdateAll), App.Text("UpdateSubmodules.All"), () => vm.UpdateAll, v => vm.UpdateAll = v)));
            var init = Field("", Check(vm, nameof(vm.EnableInit), App.Text("UpdateSubmodules.Init"), () => vm.EnableInit, v => vm.EnableInit = v, "--init"));
            Bind.OneWay(vm, nameof(vm.IsEnableInitVisible), () => vm.IsEnableInitVisible, v => init.IsVisible = v, owner: init);
            form.Add(init);
            form.Add(Field("", Check(vm, nameof(vm.EnableRemote), App.Text("UpdateSubmodules.UpdateToRemoteTrackingBranch"),
                () => vm.EnableRemote, v => vm.EnableRemote = v, "--remote")));
            form.Add(Field("", Check(vm, nameof(vm.EnableRecursive), App.Text("UpdateSubmodules.Recursive"),
                () => vm.EnableRecursive, v => vm.EnableRecursive = v, "--recursive")));
            return form;
        }

        /// <summary>The worktree's name, then its path in the secondary
        /// colour, as upstream's runs.</summary>
        private static Node RemoveWorktreeForm(ViewModels.RemoveWorktree vm)
        {
            var target = new Row { Gap = 8, AlignItems = Align.Center };
            target.Add(new Icon(Icons.Get("Worktree")) { Size = 12 });
            // The path gives way, not the name.
            var name = new Text(vm.Target.Name) { MaxLines = 1 };
            name.Shrink = 0;
            target.Add(name);
            var path = new Text(vm.Target.FullPath) { MaxLines = 1, Color = Color.SecondaryLabel };
            path.Shrink = 1;
            target.Add(path);
            return Rows(
                (App.Text("RemoveWorktree.Target"), target),
                ("", Check(vm, nameof(vm.Force), App.Text("RemoveWorktree.Force"), () => vm.Force, v => vm.Force = v)));
        }

        /// <summary>Upstream's italic tip in the secondary colour.</summary>
        private static Text Note(string text) => new(text) { Style = TextStyle.Caption, Color = Color.SecondaryLabel };

        /// <summary>
        /// A repository URL, with upstream's <c>RemoteProtocolSwitcher</c>
        /// at its end: for an HTTPS or SSH URL, a menu button titled with
        /// its protocol, whose menu has the URL in both, as upstream's
        /// drop-down does.
        /// </summary>
        private static Row UrlField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get, Action<string> set,
            string placeholder)
        {
            var row = new Row { Gap = 4, AlignItems = Align.Center };
            var input = TextField(sheet, vm, property, get, set, placeholder);
            input.Grow = 1;
            row.Add(input);
            var protocol = new MenuButton { Style = ButtonStyle.Borderless };
            Bind.OneWay(vm, property, get, url =>
            {
                var choices = Protocols(url ?? "", out var active);
                protocol.IsVisible = choices.Count > 0;
                protocol.Label = active;
                protocol.Menu = choices.Select(c => (MenuEntry)new MenuItem(c, () => set(c))).ToList();
            }, owner: protocol);
            row.Add(protocol);
            return row;
        }

        private static List<string> Protocols(string url, out string active)
        {
            if (url.StartsWith("https://", StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                active = "HTTPS";
                return [url, $"git@{uri.Host}:{uri.AbsolutePath.TrimStart('/')}"];
            }
            if (s_ssh.Match(url) is { Success: true } match)
            {
                active = "SSH";
                return [$"https://{match.Groups[1].Value}/{match.Groups[2].Value}", url];
            }
            active = "";
            return [];
        }

        private static readonly Regex s_ssh = new(@"^git@([\w\.\-]+):(.+)$");
    }
}
