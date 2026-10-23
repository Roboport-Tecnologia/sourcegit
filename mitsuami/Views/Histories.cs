using System;
using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>Views/Histories.axaml</c>, first stage: the commits as
    /// the platform's table, the graph and subject, then the author, the
    /// SHA and the times, as the repository's settings show them. Rows are
    /// 26 points high, upstream's.
    /// </summary>
    public static class Histories
    {
        public static Node Create(ViewModels.Histories vm)
        {
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;

            var rows = new CommitGraphRows(vm.Graph);
            var index = new Dictionary<string, int>();
            var live = new LiveCells();

            var columns = new List<TableColumn<Models.Commit>>
            {
                new(App.Text("Histories.Header.GraphAndSubject"), c => SubjectCell(c, () => rows, index, live, () => vm.HasSingleRemote), Expand: true),
            };
            if (vm.IsAuthorColumnVisible)
                columns.Add(new(App.Text("Histories.Header.Author"), c => AuthorCell(c, live), Width: (float)Math.Max(80, vm.AuthorColumnWidth)));
            if (vm.IsSHAColumnVisible)
                columns.Add(new(App.Text("Histories.Header.SHA"), c => ShaCell(c, live), Width: 100));
            if (vm.IsAuthorTimeColumnVisible)
                columns.Add(new(App.Text("Histories.Header.AuthorTime"), c => TimeCell(c, live, commit => commit.AuthorTime), Width: 160));
            if (vm.IsCommitTimeColumnVisible)
                columns.Add(new(App.Text("Histories.Header.CommitTime"), c => TimeCell(c, live, commit => commit.CommitterTime), Width: 160));

            var table = new TableView<Models.Commit>(c => c.SHA, columns, SelectionMode.Multiple, itemAnimations: false)
            {
                EstimatedRowHeight = CommitGraphRows.RowHeight,
            };
            table.Grow = 1;

            // A refresh brings new commits under the same SHAs, whose rows
            // the table keeps: their cells show the new ones.
            Bind.OneWay(vm, nameof(vm.Commits), () => vm.Commits ?? [], commits =>
            {
                rows = new CommitGraphRows(vm.Graph);
                index.Clear();
                for (var i = 0; i < commits.Count; i++)
                    index[commits[i].SHA] = i;
                table.Items = commits;
                live.Show(commits);
            }, owner: page);
            // The graph changes alone when the highlighted commits do.
            Bind.OneWay(vm, nameof(vm.Graph), () => vm.Graph, graph =>
            {
                rows = new CommitGraphRows(graph);
                live.Show(vm.Commits ?? []);
            }, owner: page);

            Bind.OneWay(vm, nameof(vm.SelectedCommits), () => vm.SelectedCommits ?? [], selected => table.SelectedItems = selected, owner: page);
            table.SelectionChanged += selected => vm.SelectedCommits = selected.ToList();
            table.Activated += commit =>
            {
                if (vm.Bisect != null)
                    vm.CheckoutCommitDetached(commit);
                else
                    _ = vm.CheckoutBranchByCommitAsync(commit);
            };

            // The commits over their details, split by the platform's
            // divider; upstream's least heights are 100 and 28.
            var split = new Split { Orientation = Orientation.Vertical };
            split.Grow = 1;
            split.MinHeight = 0;
            var top = new Column { AlignItems = Align.Stretch };
            top.MinHeight = 100;
            top.Add(table);
            split.Add(top);
            // The tab view stands off the divider and the window's edges
            // by the platform's spacing.
            var details = new Column { AlignItems = Align.Stretch, Padding = Spacing.Md };
            details.MinHeight = 28;
            var tabs = CommitDetail.Create(vm);
            tabs.Grow = 1;
            details.Add(tabs);
            split.Add(details);
            // Upstream shares the height half and half; a split's divider
            // is in points, so it's put halfway once the page has a height
            // (the page may be built hidden, before its tab is chosen).
            async void Halve()
            {
                for (var tries = 0; tries < 100 && split.Position == null; tries++)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                    if (split.Frame.Height is var height and > 0)
                        split.Position = Math.Floor(height / 2);
                }
            }
            Halve();
            page.Add(split);
            return page;
        }

        /// <summary>The graph's part in the row, the commit's branches and
        /// tags, then the subject on one line: bold for the commit checked
        /// out, and in the secondary colour if it isn't in the current
        /// branch (upstream fades it).</summary>
        private static Node SubjectCell(Models.Commit commit, Func<CommitGraphRows> rows, Dictionary<string, int> index, LiveCells live,
            Func<bool> hasSingleRemote)
        {
            var cell = new Row { AlignItems = Align.Center };
            var graph = new CommitGraphCell(rows);
            cell.Add(graph);
            // Upstream's 2 before the labels and 4 after them.
            var refs = new CommitRefs();
            refs.View.Margin = new Thickness(0, 4, 0, 2);
            cell.Add(refs.View);
            var subject = new Text { MaxLines = 1 };
            subject.Grow = 1;
            cell.Add(subject);
            live.Add(cell, commit, c =>
            {
                graph.Show(index.GetValueOrDefault(c.SHA), c.LeftMargin);
                refs.Show(c, hasSingleRemote());
                subject.Value = c.Subject;
                Emphasis(subject, c);
            });
            return cell;
        }

        private static Node AuthorCell(Models.Commit commit, LiveCells live)
        {
            var name = new Text { MaxLines = 1 };
            live.Add(name, commit, c =>
            {
                name.Value = c.Author.Name;
                Emphasis(name, c);
            });
            return name;
        }

        private static Node ShaCell(Models.Commit commit, LiveCells live)
        {
            var sha = new Text { Style = TextStyle.Monospace, MaxLines = 1 };
            live.Add(sha, commit, c =>
            {
                sha.Value = c.SHA.Length > 10 ? c.SHA[..10] : c.SHA;
                Emphasis(sha, c);
            });
            return sha;
        }

        private static Node TimeCell(Models.Commit commit, LiveCells live, Func<Models.Commit, ulong> time)
        {
            var text = new Text { MaxLines = 1 };
            live.Add(text, commit, c =>
            {
                text.Value = TimeText(time(c));
                Emphasis(text, c);
            });
            return text;
        }

        private static void Emphasis(Text text, Models.Commit commit)
        {
            text.Weight = commit.IsCurrentHead ? FontWeight.Bold : FontWeight.Regular;
            text.Color = commit.IsMerged ? Color.Label : Color.SecondaryLabel;
        }

        /// <summary>Upstream's <c>CommitTimeTextBlock</c>: a date and time,
        /// or how long ago, as the preferences say.</summary>
        private static string TimeText(ulong timestamp)
        {
            if (!ViewModels.Preferences.Instance.DisplayTimeAsPeriodInHistories)
                return Models.DateTimeFormat.Format(timestamp);

            var now = DateTime.Now;
            var localTime = DateTime.UnixEpoch.AddSeconds(timestamp).ToLocalTime();
            var span = now - localTime;
            if (span.TotalMinutes < 1)
                return App.Text("Period.JustNow");
            if (span.TotalHours < 1)
                return App.Text("Period.MinutesAgo", (int)span.TotalMinutes);
            if (span.TotalDays < 1)
            {
                var hours = (int)span.TotalHours;
                return hours == 1 ? App.Text("Period.HourAgo") : App.Text("Period.HoursAgo", hours);
            }

            var lastDay = now.AddDays(-1).Date;
            if (localTime >= lastDay)
                return App.Text("Period.Yesterday");
            if ((localTime.Year == now.Year && localTime.Month == now.Month) || span.TotalDays < 28)
                return App.Text("Period.DaysAgo", (int)(now.Date - localTime.Date).TotalDays);

            var lastMonth = now.AddMonths(-1).Date;
            if (localTime.Year == lastMonth.Year && localTime.Month == lastMonth.Month)
                return App.Text("Period.LastMonth");
            if (localTime.Year == now.Year || localTime > now.AddMonths(-11))
                return App.Text("Period.MonthsAgo", (12 + now.Month - localTime.Month) % 12);

            var diffYear = now.Year - localTime.Year;
            return diffYear == 1 ? App.Text("Period.LastYear") : App.Text("Period.YearsAgo", diffYear);
        }

        /// <summary>The cells the table shows, each with how it shows its
        /// commit, so they follow the commits a refresh brings.</summary>
        private sealed class LiveCells
        {
            private readonly Dictionary<Node, (string Sha, Action<Models.Commit> Show)> _cells = [];

            public void Add(Node cell, Models.Commit commit, Action<Models.Commit> show)
            {
                show(commit);
                _cells[cell] = (commit.SHA, show);
                cell.Forgotten += () => _cells.Remove(cell);
            }

            public void Show(IReadOnlyList<Models.Commit> commits)
            {
                var bySha = new Dictionary<string, Models.Commit>(commits.Count);
                foreach (var c in commits)
                    bySha[c.SHA] = c;
                foreach (var (sha, show) in _cells.Values.ToArray())
                {
                    if (bySha.TryGetValue(sha, out var commit))
                        show(commit);
                }
            }
        }
    }
}
