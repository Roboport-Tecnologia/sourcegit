using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's git command logs (<c>Views/ViewLogs.axaml</c>), a sheet on
    /// the main window where upstream has a window of its own: the commands
    /// run, newest first, each with its start and how long it took (counting
    /// while it runs), beside the selected one's output in a read-only
    /// monospace text area that follows a running command's end; Clear All.
    /// </summary>
    public static class Logs
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo)
        {
            var vm = new ViewModels.ViewLogs(repo);
            var title = Popups.ButtonText("ViewLogs");
            var sheet = Shown = new Window(App.MainWindow, title, 800, 500);

            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            column.Grow = 1;
            column.Add(new Text(title) { Style = TextStyle.Headline });

            // Upstream's list beside the output, 300 wide.
            var split = new Split { Position = 300 };
            split.Grow = 1;
            split.MinHeight = 0;
            var durations = new Dictionary<ViewModels.CommandLog, Text>();
            var list = new ListView<ViewModels.CommandLog>(KeyOf, log => Row(vm, log, durations), itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 28,
                Items = vm.Logs,
            };
            list.Grow = 1;
            list.MinHeight = 0;
            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            area.Add(list);
            var empty = new Icon(Icons.Get("Empty")) { Size = 48, Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(empty);
            split.Add(area);

            var output = new TextArea { ReadOnly = true, LineWrap = false, FollowsEnd = true, Style = TextStyle.Monospace, AccessibleName = title };
            split.Add(output);
            column.Add(split);

            // A running command's output comes a line at a time.
            var receiver = new Receiver(output);
            Bind.OneWay(vm, nameof(vm.SelectedLog), () => vm.SelectedLog, log =>
            {
                receiver.Follow(log);
                if (list.SelectedItem != log)
                    list.SelectedItems = log != null ? [log] : [];
            }, owner: sheet);
            list.SelectionChanged += selected => vm.SelectedLog = selected.Count > 0 ? selected[0] : null;
            list.ContextMenuOnOpen = () => list.SelectedItem is { } log ? Menu(vm, log) : [];

            void Remove()
            {
                if (vm.SelectedLog is { } log)
                    vm.Logs.Remove(log);
            }
            list.OnKey(Shortcut.Of(Key.Delete), Remove);
            list.OnKey(Shortcut.Of(Key.Backspace), Remove);

            var buttons = new Row { AlignItems = Align.Center };
            var clear = new Button(Popups.ButtonText("ViewLogs.Clear"));
            clear.Click += vm.ClearAll;
            buttons.Add(clear);
            var gap = new Row();
            gap.Grow = 1;
            buttons.Add(gap);
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += sheet.Destroy;
            buttons.Add(close);
            column.Add(buttons);

            void Count()
            {
                list.IsVisible = vm.Logs.Count > 0;
                empty.IsVisible = vm.Logs.Count == 0;
                clear.IsEnabled = vm.Logs.Count > 0;
            }
            Count();
            void Changed(object _, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Count();
            vm.Logs.CollectionChanged += Changed;

            // Upstream's running times count up, ten times a second.
            var timer = new System.Threading.Timer(_ => Application.Post(() =>
            {
                foreach (var (log, duration) in durations)
                {
                    if (!log.IsComplete)
                        duration.Value = Duration(log);
                }
            }), null, 100, 100);
            sheet.Closed += () =>
            {
                timer.Dispose();
                receiver.Follow(null);
                vm.Logs.CollectionChanged -= Changed;
                Shown = null;
            };
            sheet.Content = column;

            // Upstream opens on the newest.
            if (vm.Logs.Count > 0)
                vm.SelectedLog = vm.Logs[0];
        }

        // Logs have no name of their own: each gets one while it lives.
        private static readonly ConditionalWeakTable<ViewModels.CommandLog, string> s_keys = new();
        private static long s_next;

        private static string KeyOf(ViewModels.CommandLog log) =>
            s_keys.GetValue(log, _ => (++s_next).ToString());

        /// <summary>Upstream's row: a spinner while it runs, the command,
        /// when it started and how long it took.</summary>
        private static Node Row(ViewModels.ViewLogs vm, ViewModels.CommandLog log, Dictionary<ViewModels.CommandLog, Text> durations)
        {
            var row = new Mitsuamia.Row { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(0, 4) };
            row.Height = 28;
            var spinner = new Spinner();
            row.Add(spinner);
            var name = new Text(log.Name) { MaxLines = 1 };
            name.Grow = 1;
            name.Shrink = 1;
            name.MinWidth = 0;
            row.Add(name);
            row.Add(new Text(log.StartTime.ToString("HH:mm:ss")) { Color = Color.SecondaryLabel });
            // Upstream's durations line up on the right, in a column of
            // their own.
            var cell = new Mitsuamia.Row { Justify = Justify.End };
            cell.Width = 64;
            cell.Shrink = 0;
            var duration = new Text(Duration(log)) { Color = Color.SecondaryLabel };
            cell.Add(duration);
            row.Add(cell);
            durations[log] = duration;
            Bind.OneWay(log, nameof(log.IsComplete), () => log.IsComplete, done =>
            {
                spinner.IsVisible = !done;
                duration.Value = Duration(log);
            }, owner: row);
            return row;
        }

        /// <summary>Upstream's <c>CommandLogTime</c>: three digits of
        /// milliseconds, seconds or minutes.</summary>
        private static string Duration(ViewModels.CommandLog log)
        {
            var duration = (log.IsComplete ? log.EndTime : DateTime.Now) - log.StartTime;
            if (duration.TotalMinutes >= 1)
                return $"{duration.TotalMinutes:G3} min";
            if (duration.TotalSeconds >= 1)
                return $"{duration.TotalSeconds:G3} s";
            return $"{duration.TotalMilliseconds:G3} ms";
        }

        /// <summary>Upstream's log menu: copy it, delete it.</summary>
        private static IReadOnlyList<MenuEntry> Menu(ViewModels.ViewLogs vm, ViewModels.CommandLog log) =>
        [
            new MenuItem(App.Text("ViewLogs.CopyLog"), () => Clipboard.SetText(log.Content)),
            new MenuItem(App.Text("ViewLogs.Delete"), () => vm.Logs.Remove(log)),
        ];

        /// <summary>Upstream's <c>CommandLogContentPresenter</c>: the log's
        /// output, and each line a running command adds.</summary>
        private sealed class Receiver(TextArea output) : Models.ICommandLogReceiver
        {
            private ViewModels.CommandLog _log;

            public void Follow(ViewModels.CommandLog log)
            {
                _log?.Unsubscribe(this);
                _log = log;
                output.Value = log?.Content ?? "";
                log?.Subscribe(this);
            }

            public void OnReceiveCommandLog(string line) => output.Value = _log.Content;
        }
    }
}
