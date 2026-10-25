using System;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The popups the local changes and the stashes open, upstream's views
    /// of the same names: discarding changes, stashing them, ignoring a
    /// file; applying, dropping or clearing stashes, and a branch from one.
    /// </summary>
    public static partial class Popups
    {
        /// <summary>A local changes popup's form, or null when it isn't one.</summary>
        private static Func<Window, Node> LocalChangesForm(ViewModels.Popup popup, out string title)
        {
            switch (popup)
            {
                case ViewModels.Discard vm:
                    title = App.Text("Discard");
                    return _ => DiscardForm(vm);
                case ViewModels.StashChanges vm:
                    title = App.Text("Stash.Title");
                    return _ => StashForm(vm);
                case ViewModels.AddToIgnore vm:
                    title = App.Text("AddToIgnore");
                    return sheet => AddToIgnoreForm(sheet, vm);
                case ViewModels.ApplyStash vm:
                    title = App.Text("ApplyStash");
                    return _ => Rows(
                        (App.Text("ApplyStash.Stash"), StashLabel(vm.Stash)),
                        ("", Check(vm, nameof(vm.RestoreIndex), App.Text("ApplyStash.RestoreIndex"), () => vm.RestoreIndex, v => vm.RestoreIndex = v, "--index")),
                        ("", Check(vm, nameof(vm.DropAfterApply), App.Text("ApplyStash.DropAfterApply"), () => vm.DropAfterApply, v => vm.DropAfterApply = v)));
                case ViewModels.DropStash vm:
                    title = App.Text("StashDropConfirm");
                    return _ => Rows((App.Text("StashDropConfirm.Label"), StashLabel(vm.Stash)));
                case ViewModels.ClearStashes:
                    title = App.Text("ClearStashes");
                    return _ => new Text(App.Text("ClearStashes.Message"));
                case ViewModels.CheckoutBranchFromStash vm:
                    title = App.Text("CheckoutBranchFromStash");
                    return sheet => Rows(
                        (App.Text("CheckoutBranchFromStash.Stash"), StashLabel(vm.Target)),
                        (App.Text("CheckoutBranchFromStash.Branch"), TextField(sheet, vm, nameof(vm.BranchName), () => vm.BranchName, v => vm.BranchName = v)));
                default:
                    return ToolForm(popup, out title);
            }
        }

        /// <summary>Upstream's three forms: what's discarded (everything,
        /// with what kinds to include, one file, or how many), then the
        /// warning that it can't be undone.</summary>
        private static Node DiscardForm(ViewModels.Discard vm)
        {
            Column form;
            switch (vm.Mode)
            {
                case ViewModels.DiscardAllMode all:
                    form = Rows((App.Text("Discard.Changes"), new Text(App.Text("Discard.All"))));
                    Checkbox Include(string label, bool value, Action<bool> set)
                    {
                        var box = new Checkbox(App.Text(label)) { IsChecked = value };
                        box.Changed += set;
                        return box;
                    }
                    form.Add(Field("", Include("Discard.IncludeModified", all.IncludeModified, v => all.IncludeModified = v)));
                    form.Add(Field("", Include("Discard.IncludeUntracked", all.IncludeUntracked, v => all.IncludeUntracked = v)));
                    form.Add(Field("", Include("Discard.IncludeIgnored", all.IncludeIgnored, v => all.IncludeIgnored = v)));
                    break;
                case ViewModels.DiscardSingleFile single:
                    form = Rows((App.Text("Discard.Changes"), IconLabel("File", single.Path)));
                    break;
                case ViewModels.DiscardMultipleFiles multiple:
                    form = Rows((App.Text("Discard.Changes"), IconLabel("File", App.Text("Discard.Total", multiple.Count))));
                    break;
                default:
                    form = Rows();
                    break;
            }
            form.Add(Field("", Warning(App.Text("Discard.Warning"))));
            return form;
        }

        /// <summary>Upstream's stash form: what's left after stashing, a
        /// message, and for all the changes, whether only the staged ones go
        /// and whether untracked files do; for selected files, a tip.</summary>
        private static Node StashForm(ViewModels.StashChanges vm)
        {
            var modes = Models.DealWithChangesAfterStashing.Supported;
            var mode = new Select(App.Text("Stash.Mode"))
            {
                Options = modes.Select(m => $"{m.Label}  ({m.Desc})").ToArray(),
                SelectedIndex = Math.Clamp(vm.ChangesAfterStashing, 0, modes.Count - 1),
                MinWidth = 0,
            };
            mode.Changed += i => vm.ChangesAfterStashing = i;

            var editor = new TextAreaWithPlaceholder(App.Text("Stash.Message.Placeholder"));
            editor.Height = 100;
            var message = editor.Area;
            message.AccessibleName = App.Text("Stash.Message");
            message.Value = vm.Message ?? "";
            message.Changed += v => vm.Message = v;

            var form = Rows((App.Text("Stash.Mode"), mode));
            form.Add(TopField(App.Text("Stash.Message"), editor));
            if (vm.HasSelectedFiles)
            {
                var tip = new Text(App.Text("Stash.TipForSelectedFiles")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel };
                tip.Shrink = 1;
                form.Add(Field("", tip));
            }
            else
            {
                form.Add(Field("", Check(vm, nameof(vm.OnlyStaged), App.Text("Stash.OnlyStagedChanges"), () => vm.OnlyStaged, v => vm.OnlyStaged = v,
                    "--staged. If file is staged partically, the unstaged part of it will also be stashed.")));
                var untracked = Field("", Check(vm, nameof(vm.IncludeUntracked), App.Text("Stash.IncludeUntracked"), () => vm.IncludeUntracked,
                    v => vm.IncludeUntracked = v, "--include-untracked"));
                Bind.OneWay(vm, nameof(vm.OnlyStaged), () => vm.OnlyStaged, only => untracked.IsVisible = !only, owner: untracked);
                form.Add(untracked);
            }
            return form;
        }

        /// <summary>A stash as upstream's popups show it: its icon, its name
        /// in dark orange, and its subject.</summary>
        private static Row StashLabel(Models.Stash stash)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            row.Add(new Icon(Icons.Get("Stashes")) { Size = 12 });
            row.Add(new Text(stash.Name) { Color = s_sha });
            var subject = new Text(stash.Subject) { MaxLines = 1 };
            subject.Shrink = 1;
            row.Add(subject);
            return row;
        }

        /// <summary>Upstream's pattern and the file it goes in: the
        /// repository's <c>.gitignore</c>s, or <c>.git/info/exclude</c>
        /// (local only).</summary>
        private static Node AddToIgnoreForm(Window sheet, ViewModels.AddToIgnore vm)
        {
            var storage = Choice(App.Text("AddToIgnore.Storage"), vm, nameof(vm.SelectedStorageFile), vm.StorageFiles,
                f => f.IsLocalOnly ? $"{f.DisplayName}  (LOCAL ONLY)" : f.DisplayName, () => vm.SelectedStorageFile, f => vm.SelectedStorageFile = f);
            return Rows(
                (App.Text("AddToIgnore.Pattern"), TextField(sheet, vm, nameof(vm.Pattern), () => vm.Pattern, v => vm.Pattern = v)),
                (App.Text("AddToIgnore.Storage"), storage));
        }
    }
}
