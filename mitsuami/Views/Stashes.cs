using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's stashes page (<c>Views/StashesPage.axaml</c>): on the left,
    /// the stashes under their toolbar and a field filtering them, and the
    /// selected stash's changes under theirs; on the right, the selected
    /// file's diff (<see cref="DiffView"/>).
    /// </summary>
    public static class Stashes
    {
        public static Node Create(ViewModels.Repository repo)
        {
            var vm = repo.StashesPage;
            // Upstream's left column is a preference, and both sides are at
            // least 300 points. On Windows the default (300) opens 350
            // wide, for WinUI's wider buttons; a width the user dragged to
            // is kept.
            var layout = ViewModels.Preferences.Instance.Layout;
            var leftWidth = layout.StashesLeftWidth.Value;
            var split = new Split { Position = OperatingSystem.IsWindows() && leftWidth == 300 ? 350 : leftWidth };
            split.Grow = 1;
            split.Changed += width => layout.StashesLeftWidth = new Avalonia.Controls.GridLength(width);
            var left = Left(repo, vm);
            left.MinWidth = 300;
            split.Add(left);
            var right = Detail(vm);
            right.MinWidth = 300;
            split.Add(right);
            return split;
        }


        private static Container Left(ViewModels.Repository repo, ViewModels.StashesPage vm)
        {
            var column = new Column { AlignItems = Align.Stretch };

            // Upstream's stash bar: the count, and clearing them all.
            var stashBar = Bar("Stashes", "Stashes.Stashes", vm, nameof(vm.Stashes), () => vm.Stashes?.Count ?? 0);
            var clear = new Button(App.Text("Repository.ClearStashes"))
            {
                Icon = Icons.Get("RemoveAll", 13),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Repository.ClearStashes"),
            };
            clear.Click += vm.ClearStashes;
            stashBar.Add(clear);
            column.Add(stashBar);
            column.Add(new Separator());

            // Upstream's search row, 36 points high.
            var search = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 4) };
            search.Height = 36;
            search.Shrink = 0;
            var filter = new SearchInput();
            filter.Grow = 1;
            Bind.Search(filter, vm, nameof(vm.SearchFilter), () => vm.SearchFilter, v => vm.SearchFilter = v, owner: search);
            search.Add(filter);
            column.Add(search);
            column.Add(new Separator());

            var stashes = StashList(repo, vm);
            stashes.Grow = 1;
            stashes.MinHeight = 0;
            column.Add(stashes);
            column.Add(new Separator());

            // Upstream's changes bar: the count and the view mode.
            var changesBar = Bar("Changes", "Stashes.Changes", vm, nameof(vm.Changes), () => vm.Changes?.Count ?? 0);
            changesBar.Add(ChangeCollection.ModeButton());
            column.Add(changesBar);
            column.Add(new Separator());

            var changes = ChangeList(repo, vm);
            changes.Grow = 1;
            changes.MinHeight = 0;
            column.Add(changes);
            return column;
        }

        /// <summary>Upstream's 28-point bar over a list: its icon, its name
        /// and count in bold, both in the secondary colour; its buttons go
        /// at the end.</summary>
        private static Row Bar(string icon, string title, ViewModels.StashesPage vm, string countProperty, Func<int> count)
        {
            var bar = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 4, 0, 8) };
            bar.MinHeight = 28;
            bar.Shrink = 0;
            bar.Add(new Icon(Icons.Get(icon)) { Size = 14, Color = Color.SecondaryLabel });
            // One line, cut off in a narrow column so the buttons keep
            // their room.
            var text = new Text { Weight = FontWeight.Bold, Color = Color.SecondaryLabel, MaxLines = 1 };
            Bind.OneWay(vm, countProperty, count, c => text.Value = $"{App.Text(title)} ({c})", owner: bar);
            bar.Add(text);
            var space = new Column();
            space.Grow = 1;
            bar.Add(space);
            return bar;
        }

        /// <summary>
        /// Upstream's stash list, rows 50 points high: the stash's name in
        /// dark orange and its time, then its subject, the branch it was
        /// made on as a chip before it. Double-click or Return applies one;
        /// Delete or Backspace drops it; ⌘C copies its message.
        /// </summary>
        private static ListView<Models.Stash> StashList(ViewModels.Repository repo, ViewModels.StashesPage vm)
        {
            var list = new ListView<Models.Stash>(s => s.SHA, s => Row(repo, vm, s), itemAnimations: false) { EstimatedRowHeight = 50 };
            Bind.OneWay(vm, nameof(vm.VisibleStashes), () => vm.VisibleStashes, s => list.Items = s ?? [], owner: list);
            Bind.OneWay(vm, nameof(vm.SelectedStash), () => vm.SelectedStash, s =>
            {
                if (list.SelectedItem?.SHA != s?.SHA)
                    list.SelectedItems = s != null && list.Items.FirstOrDefault(i => i.SHA == s.SHA) is { } shown ? [shown] : [];
            }, owner: list);
            list.SelectionChanged += selected => vm.SelectedStash = selected.Count > 0 ? selected[0] : null;
            list.Activated += vm.Apply;

            void Drop()
            {
                if (vm.SelectedStash is { } stash)
                    vm.Drop(stash);
            }
            list.OnKey(Shortcut.Of(Key.Delete), Drop);
            list.OnKey(Shortcut.Of(Key.Backspace), Drop);
            list.OnKey(Shortcut.Of('c').WithPrimary(), () =>
            {
                if (vm.SelectedStash is { } stash)
                    Clipboard.SetText(stash.Message);
            });
            return list;
        }

        private static Node Row(ViewModels.Repository repo, ViewModels.StashesPage vm, Models.Stash stash)
        {
            var row = new Column { AlignItems = Align.Stretch, Justify = Justify.SpaceBetween, Padding = 4 };
            row.Height = 50;
            row.ToolTip = stash.Message;
            row.ContextMenuOnOpen = () => Menu(repo, vm, stash);

            var top = new Mitsuamia.Row { AlignItems = Align.Center, Gap = 8 };
            var name = new Text(stash.Name) { Color = s_name, MaxLines = 1 };
            name.Grow = 1;
            top.Add(name);
            top.Add(new Text(Models.DateTimeFormat.Format(stash.Time)) { Color = Color.SecondaryLabel });
            row.Add(top);
            row.Add(Subject(stash.Subject));
            return row;
        }

        /// <summary>Upstream's <c>StashSubjectPresenter</c>: git's "On
        /// branch: " or "WIP on branch: sha " as a chip with the branch's
        /// name (and "WIP |"), 11 points, then the rest of the subject.</summary>
        private static Node Subject(string subject)
        {
            var row = new Mitsuamia.Row { AlignItems = Align.Center, Gap = 4 };
            string prefix = null;
            if (s_on.Match(subject) is { Success: true } on)
            {
                prefix = on.Groups[1].Value;
                subject = subject[on.Length..];
            }
            else if (s_wip.Match(subject) is { Success: true } wip)
            {
                prefix = $"WIP | {wip.Groups[1].Value}";
                subject = subject[wip.Length..];
            }
            if (prefix != null)
            {
                var chip = new Mitsuamia.Row { AlignItems = Align.Center, Padding = new Thickness(2, 6) };
                chip.Shrink = 0;
                chip.Add(new Chip { Inset = new Thickness(0) });
                chip.Add(new Text(prefix) { Style = TextStyle.Caption, MaxLines = 1 });
                row.Add(chip);
            }
            var rest = new Text(subject) { MaxLines = 1 };
            rest.Shrink = 1;
            row.Add(rest);
            return row;
        }

        /// <summary>Behind the branch's name: a pill in upstream's inline
        /// code colour, a light grey in either appearance.</summary>
        private sealed class Chip() : DrawnWidget("StashChip", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.Rgba(128, 128, 128, 56), canvas.Height / 2);
        }

        /// <summary>Upstream's stash menu: apply, a branch from it, drop;
        /// save as a patch; copy its message.</summary>
        private static IReadOnlyList<MenuEntry> Menu(ViewModels.Repository repo, ViewModels.StashesPage vm, Models.Stash stash)
        {
            return
            [
                new MenuItem(App.Text("StashCM.Apply"), () => vm.Apply(stash)),
                new MenuItem(App.Text("StashCM.Branch"), () => vm.CheckoutBranch(stash)),
                new MenuItem(App.Text("StashCM.Drop"), () => vm.Drop(stash)),
                MenuSeparator.Instance,
                new MenuItem(App.Text("StashCM.SaveAsPatch"), async () =>
                {
                    if (await Dialogs.SaveFile(App.MainWindow, App.Text("StashCM.SaveAsPatch"), "stash.patch") is not { } file)
                        return;
                    try
                    {
                        await vm.SaveStashAsPatchAsync(stash, file);
                    }
                    catch (Exception exception)
                    {
                        repo.SendNotification($"Failed to save as patch: {exception.Message}", true);
                    }
                }),
                MenuSeparator.Instance,
                new MenuItem(App.Text("StashCM.CopyMessage"), () => Clipboard.SetText(stash.Message)),
            ];
        }

        /// <summary>The selected stash's changes, with upstream's menu and
        /// its ⌘C and ⌘⇧C.</summary>
        private static ListView ChangeList(ViewModels.Repository repo, ViewModels.StashesPage vm)
        {
            var prefs = ViewModels.Preferences.Instance;
            var list = new ChangeCollection(false);
            var owner = list.View;
            Bind.OneWay(prefs, nameof(prefs.ChangeViewMode), () => prefs.ChangeViewMode, m => list.Mode = m, owner: owner);
            Bind.OneWay(prefs, nameof(prefs.EnableCompactFoldersInChangesTree), () => prefs.EnableCompactFoldersInChangesTree,
                v => list.CompactFolders = v, owner: owner);
            Bind.OneWay(vm, nameof(vm.Changes), () => vm.Changes, c => list.Changes = c, owner: owner);
            Bind.OneWay(vm, nameof(vm.ChangeSelection), () => vm.ChangeSelection, s => list.Selection = s, owner: owner);
            list.SelectionChanged += s => vm.ChangeSelection = s;
            list.Menu = s => ChangeMenu(repo, vm, s);
            owner.OnKey(Shortcut.Of('c').WithPrimary(), () => ChangeMenus.CopyPaths(repo, vm.ChangeSelection, false));
            owner.OnKey(Shortcut.Of('c').WithPrimary().WithShift(), () => ChangeMenus.CopyPaths(repo, vm.ChangeSelection, true));
            return owner;
        }

        /// <summary>Upstream's menu for a stash's changes: for one, the
        /// external diff tool and revealing it; then applying the changes,
        /// checking them out, and copying their paths.</summary>
        private static IReadOnlyList<MenuEntry> ChangeMenu(ViewModels.Repository repo, ViewModels.StashesPage vm, ViewModels.ChangeSelection selection)
        {
            var menu = new List<MenuEntry>();
            if (selection.Count == 1)
            {
                var folder = selection.IsSingleFolder ? vm.GetAbsPath(selection.SingleFolderPath) : null;
                if (!selection.HasFolder)
                {
                    var change = selection.Changes[0];
                    menu.Add(new MenuItem(App.Text("OpenInExternalMergeTool"), () => vm.OpenChangeWithExternalDiffTool(change)));
                }
                if (!selection.HasFolder || selection.IsSingleFolder)
                {
                    var path = folder ?? vm.GetAbsPath(selection.Changes[0].Path);
                    menu.Add(new MenuItem(App.Text("RevealFile"), () => Native.OS.OpenInFileManager(path))
                    { Enabled = folder != null ? Directory.Exists(path) : File.Exists(path) });
                }
                if (menu.Count > 0)
                    menu.Add(MenuSeparator.Instance);
            }
            var changes = selection.Changes;
            menu.Add(new MenuItem(App.Text("StashCM.ApplyFileChanges"), () => _ = vm.ApplySelectedChanges(changes)));
            menu.Add(new MenuItem(App.Text("ChangeCM.CheckoutThisRevision"), () => _ = vm.CheckoutFilesAsync(changes)));
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("CopyPath"), () => ChangeMenus.CopyPaths(repo, selection, false)));
            menu.Add(new MenuItem(App.Text("CopyFullPath"), () => ChangeMenus.CopyPaths(repo, selection, true)));
            return menu;
        }

        /// <summary>Upstream's right side: the picked change's diff
        /// (<see cref="DiffView"/>).</summary>
        private static Node Detail(ViewModels.StashesPage vm) =>
            DiffView.Create(vm, nameof(vm.DiffContext), () => vm.DiffContext);

        private static readonly Color s_name = Color.Rgba(255, 140, 0);
        private static readonly Regex s_on = new(@"^On ([^\s]+)\: ");
        private static readonly Regex s_wip = new(@"^WIP on ([^\s]+)\: ([a-f0-9]{6,64}) ");
    }
}
