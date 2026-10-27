using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's interactive rebase (<c>Views/InteractiveRebase.axaml</c>), a
    /// sheet on the main window: what's rebased on what, the commits with
    /// their actions, the selected commit's details under them, then the
    /// options and Start. A row's action is the platform's drop-down where
    /// upstream has a button opening a menu of them; the rows reorder with
    /// the list's own row drag, as upstream's drag and drop does. Upstream's
    /// keys (P, E, R, S, F, D) set the selected rows' action. Its ⌘↑ and ⌘↓
    /// aren't taken: AppKit's table keeps them (mitsuami's keys tests).
    /// </summary>
    public static class InteractiveRebase
    {
        /// <summary>Opens it for the commits after <paramref name="on"/>,
        /// one of them given an action already (upstream's prefill).</summary>
        public static void Show(ViewModels.Repository repo, Models.Commit on, ViewModels.InteractiveRebasePrefill prefill = null)
        {
            var vm = new ViewModels.InteractiveRebase(repo, on, prefill);
            var sheet = new Window(App.MainWindow, App.Text("InteractiveRebase"), 1080, 680);
            Shown = sheet;
            sheet.Closed += () => Shown = null;

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Add(Heading(vm));

            var list = Commits(sheet, vm);
            var loading = new Row { Justify = Justify.Center, AlignItems = Align.Center };
            loading.Grow = 1;
            loading.Add(new Spinner());
            Bind.OneWay(vm, nameof(vm.IsLoading), () => vm.IsLoading, busy =>
            {
                loading.IsVisible = busy;
                list.IsVisible = !busy;
            }, owner: list);
            var top = new Column { AlignItems = Align.Stretch };
            top.Add(loading);
            top.Add(list);

            // Upstream's list over the details, half and half, with a divider.
            var split = new Split { Orientation = Orientation.Vertical, Position = 300 };
            split.Grow = 1;
            split.MinHeight = 0;
            split.Add(top);
            split.Add(CommitDetail.Create(vm, nameof(vm.Detail), () => vm.Detail, () => repo.Remotes.Count == 1, tips: false));
            column.Add(split);

            column.Add(Footer(sheet, vm));
            sheet.Content = column;
        }

        /// <summary>Upstream's prefilled rebase from the commit menu: the
        /// commit rewords, edits, squashes, fixes up or drops, the rebase on
        /// its parent (or grandparent, to meld into the parent).</summary>
        public static async Task ShowWithAction(ViewModels.Repository repo, Models.Commit target, Models.InteractiveRebaseAction action)
        {
            var start = action is Models.InteractiveRebaseAction.Squash or Models.InteractiveRebaseAction.Fixup
                ? $"{target.SHA}~~"
                : $"{target.SHA}~";
            var on = await new Commands.QuerySingleCommit(repo.FullPath, start).GetResultAsync();
            if (on == null)
                repo.SendNotification($"Commit '{start}' is not a valid revision for `git rebase -i`!", true);
            else
                Show(repo, on, new ViewModels.InteractiveRebasePrefill(target.SHA, action));
        }

        /// <summary>The message's sheet showing, if any: for captures.</summary>
        public static Window Editing { get; private set; }

        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        /// <summary>Upstream's line over the list: the branch rebased and
        /// the commit it goes on.</summary>
        private static Node Heading(ViewModels.InteractiveRebase vm)
        {
            var row = new Row { Gap = Spacing.Sm, AlignItems = Align.Center };
            row.Add(new Text(App.Text("InteractiveRebase.Target")) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            row.Add(new Icon(Icons.Get("Branch")) { Size = 14 });
            row.Add(new Text(vm.Current.FriendlyName));
            var on = new Text(App.Text("InteractiveRebase.On")) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold, Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Lg) };
            row.Add(on);
            row.Add(new Icon(Icons.Get("Commit")) { Size = 14 });
            row.Add(new Text(Sha(vm.On.SHA)) { Style = TextStyle.Monospace, Color = s_sha });
            var subject = new Text(vm.On.Subject) { MaxLines = 1 };
            subject.Shrink = 1;
            subject.Grow = 1;
            row.Add(subject);
            var tip = new Icon(Icons.Get("Info")) { Size = 14, Color = Color.SecondaryLabel };
            tip.ToolTip = App.Text("InteractiveRebase.ReorderTip");
            row.Add(tip);
            return row;
        }

        private static readonly Models.InteractiveRebaseAction[] s_actions =
        [
            Models.InteractiveRebaseAction.Pick,
            Models.InteractiveRebaseAction.Edit,
            Models.InteractiveRebaseAction.Reword,
            Models.InteractiveRebaseAction.Squash,
            Models.InteractiveRebaseAction.Fixup,
            Models.InteractiveRebaseAction.Drop,
        ];

        private static ListView<ViewModels.InteractiveRebaseItem> Commits(Window sheet, ViewModels.InteractiveRebase vm)
        {
            ListView<ViewModels.InteractiveRebaseItem> list = null;
            list = new ListView<ViewModels.InteractiveRebaseItem>(item => item.Commit.SHA, item => Row(sheet, vm, list, item),
                SelectionMode.Multiple, RowMoves.BetweenRows, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 28,
                Items = vm.Items,
            };
            list.Grow = 1;
            list.MinHeight = 0;

            // Upstream opens the editor at once for a commit the menu chose
            // to reword.
            var first = true;
            list.SelectionChanged += selected =>
            {
                var items = selected.ToList();
                vm.SelectCommits(items);
                if (first && items.Count == 1 && items[0].Action == Models.InteractiveRebaseAction.Reword)
                    EditMessage(sheet, items[0]);
                first = false;
            };
            Bind.OneWay(vm, nameof(vm.PreSelected), () => vm.PreSelected, pre =>
            {
                if (pre == null)
                    return;
                list.SelectedItem = pre;
                list.ScrollTo(pre);
                vm.SelectCommits([pre]);
                if (first && pre.Action == Models.InteractiveRebaseAction.Reword)
                    EditMessage(sheet, pre);
                first = false;
            }, owner: list);

            list.Moved += (moved, drop) =>
            {
                var items = moved.ToList();
                var index = drop.Kind == RowDropKind.End ? vm.Items.Count : vm.Items.IndexOf(drop.Item);
                vm.Move(items, index);
                list.SelectedItems = items;
            };

            // Upstream's keys: an action for the selected rows, then the next
            // row; R on one row opens its message.
            void Key(char key, Models.InteractiveRebaseAction action) => list.OnKey(Shortcut.Of(key), () =>
            {
                var items = list.SelectedItems.ToList();
                if (items.Count == 0)
                    return;
                vm.ChangeAction(items, action);
                if (action == Models.InteractiveRebaseAction.Reword && items.Count == 1)
                {
                    EditMessage(sheet, items[0]);
                    return;
                }
                var next = vm.Items.IndexOf(items[^1]) + 1;
                if (next < vm.Items.Count)
                {
                    list.SelectedItem = vm.Items[next];
                    list.ScrollTo(vm.Items[next]);
                    vm.SelectCommits([vm.Items[next]]);
                }
            });
            Key('p', Models.InteractiveRebaseAction.Pick);
            Key('e', Models.InteractiveRebaseAction.Edit);
            Key('r', Models.InteractiveRebaseAction.Reword);
            Key('s', Models.InteractiveRebaseAction.Squash);
            Key('f', Models.InteractiveRebaseAction.Fixup);
            Key('d', Models.InteractiveRebaseAction.Drop);
            return list;
        }

        /// <summary>Upstream's row: the commit's place, its action, the
        /// squash indicator, the message's edit button and subject, the
        /// author, SHA and time; a dropped commit's in the secondary colour,
        /// where upstream strikes it through.</summary>
        private static Node Row(Window sheet, ViewModels.InteractiveRebase vm, ListView<ViewModels.InteractiveRebaseItem> list,
            ViewModels.InteractiveRebaseItem item)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Spacing.Xs, Padding = new Thickness(Spacing.None, Spacing.Sm) };
            row.Height = 28;

            var order = new Text($"#{item.OriginalOrder}") { Style = TextStyle.Monospace };
            order.Width = 36;
            order.Shrink = 0;
            row.Add(order);

            // The actions this commit can take: squash and fixup need one
            // before it to meld into.
            var action = new Select(App.Text("InteractiveRebase")) { MinWidth = 0 };
            action.Width = 110;
            action.Shrink = 0;
            Models.InteractiveRebaseAction[] offered = [];
            Bind.OneWay(item, nameof(item.CanSquashOrFixup), () => item.CanSquashOrFixup, can =>
            {
                offered = can ? s_actions : s_actions.Where(a => a is not (Models.InteractiveRebaseAction.Squash or Models.InteractiveRebaseAction.Fixup)).ToArray();
                action.Options = offered.Select(a => a.ToString()).ToArray();
                action.SelectedIndex = Math.Max(Array.IndexOf(offered, item.Action), 0);
            }, owner: action);
            Bind.OneWay(item, nameof(item.Action), () => item.Action, a => action.SelectedIndex = Math.Max(Array.IndexOf(offered, a), 0), owner: action);
            action.Changed += i =>
            {
                if (i >= offered.Length)
                    return;
                // The selected rows, when this one's among them; else it alone.
                var items = list.SelectedItems.Contains(item) ? list.SelectedItems.ToList() : [item];
                vm.ChangeAction(items, offered[i]);
                if (items.Count == 1 && offered[i] == Models.InteractiveRebaseAction.Reword)
                    EditMessage(sheet, item);
            };
            row.Add(action);

            var indicator = new PendingIndicator();
            indicator.Shrink = 0;
            Bind.OneWay(item, nameof(item.PendingType), () => item.PendingType, p => indicator.Pending = p, owner: indicator);
            row.Add(indicator);

            var edit = new Button(App.Text("CodeEditor"))
            {
                Icon = Icons.Get("Edit", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
            };
            edit.Click += () => EditMessage(sheet, item);
            Bind.OneWay(item, nameof(item.ShowEditMessageButton), () => item.ShowEditMessageButton, v => edit.IsVisible = v, owner: edit);
            row.Add(edit);

            // The subject gives way, truncated; the columns after it keep
            // their places.
            var subject = new Text { MaxLines = 1, MinWidth = 0 };
            subject.Grow = 1;
            subject.Shrink = 1;
            Bind.OneWay(item, nameof(item.Subject), () => item.Subject, s => subject.Value = s, owner: subject);
            row.Add(subject);

            var avatar = new Avatar(16, menu: false) { User = item.Commit.Author };
            avatar.View.Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Sm);
            row.Add(avatar.View);
            var author = new Text(item.Commit.Author.Name) { MaxLines = 1 };
            author.Width = 108;
            author.Shrink = 0;
            row.Add(author);
            var sha = new Text(Sha(item.Commit.SHA)) { Style = TextStyle.Monospace, MaxLines = 1 };
            sha.Shrink = 0;
            row.Add(sha);
            var time = new Text(Models.DateTimeFormat.Format(item.Commit.CommitterTime)) { Margin = new Thickness(Spacing.None, Spacing.Sm, Spacing.None, Spacing.Lg), MaxLines = 1 };
            time.Shrink = 0;
            row.Add(time);

            Bind.OneWay(item, nameof(item.Action), () => item.Action == Models.InteractiveRebaseAction.Drop, dropped =>
            {
                var color = dropped ? Color.SecondaryLabel : Color.Label;
                subject.Color = color;
                author.Color = color;
                sha.Color = color;
                time.Color = color;
            }, owner: row);
            return row;
        }

        /// <summary>Upstream's options and buttons under the list; a
        /// spinner while the rebase runs, then the sheet closes.</summary>
        private static Node Footer(Window sheet, ViewModels.InteractiveRebase vm)
        {
            var row = new Row { Gap = Spacing.Lg, AlignItems = Align.Center };
            var autoStash = new Checkbox(App.Text("InteractiveRebase.AutoStash")) { IsChecked = vm.AutoStash };
            autoStash.Changed += v => vm.AutoStash = v;
            var noVerify = new Checkbox(App.Text("InteractiveRebase.NoVerify")) { IsChecked = vm.NoVerify };
            noVerify.Changed += v => vm.NoVerify = v;
            row.Add(autoStash);
            row.Add(noVerify);

            var gap = new Row();
            gap.Grow = 1;
            row.Add(gap);

            var running = new Spinner { IsVisible = false };
            row.Add(running);
            var buttons = new Row { Gap = Spacing.Sm };
            var cancel = new Button(Popups.ButtonText("Cancel")) { Role = ButtonRole.Cancel };
            cancel.Click += sheet.Destroy;
            var start = new Button(Popups.ButtonText("Start")) { Role = ButtonRole.Default };
            start.Click += async () =>
            {
                running.IsVisible = true;
                start.IsEnabled = false;
                cancel.IsEnabled = false;
                await vm.Start();
                sheet.Destroy();
            };
            Bind.OneWay(vm, nameof(vm.IsLoading), () => vm.IsLoading, busy => start.IsEnabled = !busy, owner: start);
            buttons.Add(cancel);
            buttons.Add(start);
            row.Add(buttons);
            return row;
        }

        /// <summary>Upstream's commit message editor, a sheet on the
        /// rebase's: the whole message in the platform's text view.</summary>
        private static void EditMessage(Window owner, ViewModels.InteractiveRebaseItem item)
        {
            // As tall as its content: a text view that grows has nothing to
            // grow into in a sheet, and showed one line.
            var sheet = new Window(owner, App.Text("CodeEditor"), 640);
            Editing = sheet;
            sheet.Closed += () => Editing = null;
            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            var editor = new TextAreaWithPlaceholder(App.Text("CommitMessageTextBox.Placeholder"));
            editor.Height = 280;
            var message = editor.Area;
            message.Value = item.FullMessage;
            message.AccessibleName = App.Text("CodeEditor");
            column.Add(editor);

            var buttons = new Row { Gap = Spacing.Sm, Justify = Justify.End };
            var cancel = new Button(Popups.ButtonText("Cancel")) { Role = ButtonRole.Cancel };
            cancel.Click += sheet.Destroy;
            var sure = new Button(Popups.ButtonText("Sure")) { Role = ButtonRole.Default };
            sure.Click += () =>
            {
                var text = message.Value.Trim();
                if (!string.IsNullOrEmpty(text) && !text.Equals(item.FullMessage, StringComparison.Ordinal))
                {
                    item.FullMessage = text;
                    item.IsMessageUserEdited = true;
                }
                sheet.Destroy();
            };
            buttons.Add(cancel);
            buttons.Add(sure);
            column.Add(buttons);
            sheet.Content = column;
        }

        // Path data in the invariant culture: a comma decimal isn't SVG's.
        private static string Path(FormattableString data) => FormattableString.Invariant(data);

        private static string Sha(string sha) => sha.Length > 10 ? sha[..10] : sha;

        private static readonly Color s_sha = Color.Rgba(255, 140, 0);

        /// <summary>Upstream's <c>InteractiveRebaseIndicator</c>: how a
        /// squash or fixup melds commits, a line down the ones melded into
        /// the one they go in, which an arrow points at.</summary>
        private sealed class PendingIndicator : DrawnWidget
        {
            public PendingIndicator() : base("InteractiveRebaseIndicator", 24, 28) { }

            public Models.InteractiveRebasePendingType Pending
            {
                set
                {
                    _pending = value;
                    Redraw();
                }
            }

            protected override void Draw(Canvas canvas)
            {
                const float x = 4;
                var h = canvas.Height;
                var mid = h / 2;
                var end = canvas.Width - 6;
                switch (_pending)
                {
                    case Models.InteractiveRebasePendingType.None:
                        return;
                    case Models.InteractiveRebasePendingType.Last:
                        canvas.FillEllipse(x - 4, mid - 4, 8, 8, Color.Label);
                        canvas.StrokePath(Path($"M{x} {mid}L{x} {h}"), Color.Label, 2);
                        break;
                    case Models.InteractiveRebasePendingType.Ignore:
                        canvas.StrokePath(Path($"M{x} 0L{x} {h}"), Color.Label, 2);
                        break;
                    case Models.InteractiveRebasePendingType.Pending:
                        canvas.FillEllipse(x - 4, mid - 4, 8, 8, Color.Label);
                        canvas.StrokePath(Path($"M{x} 0L{x} {h}"), Color.Label, 2);
                        break;
                    default:
                        canvas.StrokePath(Path($"M{x} 0Q{x} {mid} {end} {mid}"), Color.Label, 2);
                        canvas.FillPath(Path($"M{end} {mid}L{end - 4} {mid + 2}L{end - 4} {mid - 2}Z"), Color.Label);
                        break;
                }
            }

            private Models.InteractiveRebasePendingType _pending;
        }
    }
}
