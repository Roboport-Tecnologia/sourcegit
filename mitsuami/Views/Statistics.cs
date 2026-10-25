using System;
using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's statistics (<c>Views/Statistics.axaml</c>), a window of
    /// its own, as upstream's, so it can be made larger; 1080 × 720 as
    /// upstream's least size: the branch to count, and tabs for the whole
    /// history, this month and this week (upstream's switcher). Each shows
    /// the authors and their commits, upstream's pie of the top authors and
    /// its bars of commits over time, which a picked author's join; both
    /// drawn, with the platform's text, and the bars scroll natively where
    /// upstream's drag.
    /// </summary>
    public static class Statistics
    {
        /// <summary>The window last opened, while it's open: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo)
        {
            var vm = new ViewModels.Statistics(repo.FullPath);
            var title = App.Text("Statistics");
            var window = Shown = new Window(title, 1080, 720);
            window.Closed += () =>
            {
                if (Shown == window)
                    Shown = null;
            };

            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            column.Grow = 1;
            var header = new Row { AlignItems = Align.Center, Justify = Justify.End };
            header.Add(BranchSelect(vm));
            column.Add(header);

            // Upstream's switcher: the reports as tabs.
            var tabs = new Tabs();
            tabs.Grow = 1;
            tabs.MinHeight = 0;
            var pages = new List<Column>();
            foreach (var key in new[] { "Statistics.Overview", "Statistics.ThisMonth", "Statistics.ThisWeek" })
            {
                var page = new Column { AlignItems = Align.Stretch, Justify = Justify.Center, Padding = 12 };
                page.Grow = 1;
                pages.Add(page);
                tabs.Add(Popups.ButtonText(key), page);
            }
            tabs.Changed += index => vm.ViewMode = (Models.StatisticsMode)index;
            column.Add(tabs);

            // The report shown, on its tab; a spinner while it's counted.
            Node shown = null;
            void Fill()
            {
                shown?.Destroy();
                var page = pages[(int)vm.ViewMode];
                shown = vm.IsLoading || vm.SelectedReport == null ? Loading() : Report(vm, vm.SelectedReport);
                page.Add(shown);
            }
            Bind.OneWay(vm, nameof(vm.SelectedReport), () => vm.SelectedReport, _ => Fill(), owner: window);
            Bind.OneWay(vm, nameof(vm.IsLoading), () => vm.IsLoading, _ => Fill(), owner: window);
            window.Content = column;
        }

        /// <summary>Upstream's <c>BranchSelector</c>: every branch, or all
        /// of them (upstream's first entry), as a drop-down.</summary>
        private static Select BranchSelect(ViewModels.Statistics vm)
        {
            var select = new Select(App.Text("Statistics")) { MinWidth = 0 };
            select.Width = 256;
            Bind.OneWay(vm, nameof(vm.Branches), () => vm.Branches, branches =>
            {
                select.Options = branches.Select(b => b.FriendlyName).ToList();
                select.SelectedIndex = Math.Max(0, branches.IndexOf(vm.SelectedBranch));
            }, owner: select);
            select.Changed += index =>
            {
                if (index >= 0 && index < vm.Branches.Count)
                    vm.SelectedBranch = vm.Branches[index];
            };
            return select;
        }

        private static Node Loading()
        {
            var row = new Row { Justify = Justify.Center, AlignItems = Align.Center };
            row.Grow = 1;
            row.Add(new Spinner());
            return row;
        }

        /// <summary>A report: its authors, with their totals under them,
        /// beside the pie and the bars.</summary>
        private static Node Report(ViewModels.Statistics vm, Models.StatisticsReport report)
        {
            var row = new Row { AlignItems = Align.Stretch, Gap = 16 };
            row.Grow = 1;
            row.MinHeight = 0;

            var left = new Column { AlignItems = Align.Stretch, Gap = 6 };
            left.Width = 256;
            left.Shrink = 0;
            var authors = new ListView<Models.StatisticsAuthor>(a => a.User.ToString(), AuthorRow, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = report.Authors,
            };
            authors.Grow = 1;
            authors.MinHeight = 0;
            authors.SelectionChanged += selected => vm.ChangeAuthor(selected.Count > 0 ? selected[0] : null);
            left.Add(authors);
            left.Add(Totals(report));
            row.Add(left);

            var right = new Column { AlignItems = Align.Stretch, Gap = 6 };
            right.Grow = 1;
            right.MinWidth = 0;
            right.Add(Heading("Statistics.TopAuthors", App.Text("Statistics.TopAuthors.Tip")));
            // Picking a slice picks its author in the list, or lets go.
            var pie = new Pie(report, author =>
            {
                var picked = author == null || authors.SelectedItem == author ? [] : new[] { author };
                authors.SelectedItems = picked;
                vm.ChangeAuthor(picked.Length > 0 ? author : null);
            });
            right.Add(pie.View);
            right.Add(Heading("Statistics.Commits", null));
            var bars = new Bars(() => vm.SelectedAuthor?.User.Name);
            Bind.OneWay(vm, nameof(vm.Samples), () => vm.Samples, bars.Show, owner: row);
            right.Add(bars.View);
            row.Add(right);
            return row;
        }

        /// <summary>Upstream's author row: their avatar, name and count.</summary>
        private static Node AuthorRow(Models.StatisticsAuthor author)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(0, 4) };
            row.Height = 26;
            row.ToolTip = author.User.ToString();
            var avatar = new Avatar(16) { User = author.User };
            row.Add(avatar.View);
            var name = new Text(author.User.Name) { MaxLines = 1 };
            name.Grow = 1;
            name.Shrink = 1;
            name.MinWidth = 0;
            row.Add(name);
            row.Add(new Text(author.Count.ToString()) { Color = Color.SecondaryLabel });
            return row;
        }

        /// <summary>Upstream's totals: the authors and the commits, with a
        /// warning mark when it stopped at the most it reads.</summary>
        private static Node Totals(Models.StatisticsReport report)
        {
            var row = new Row { AlignItems = Align.Center, Justify = Justify.SpaceBetween };
            row.Add(Total("Statistics.TotalAuthors", report.Authors.Count));
            var commits = Total("Statistics.TotalCommits", report.Total);
            if (report.Total >= 200000)
                commits.Add(new Icon(Icons.Get("Info")) { Size = 14, Color = Color.Warning, ToolTip = App.Text("Statistics.Tips") });
            row.Add(commits);
            return row;

            static Row Total(string key, int count)
            {
                var total = new Row { AlignItems = Align.Center, Gap = 4 };
                total.Add(new Text(Popups.ButtonText(key).Trim()) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                total.Add(new Text(count.ToString()) { Style = TextStyle.Caption });
                return total;
            }
        }

        private static Node Heading(string key, string tip)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 4 };
            row.Add(new Text(Popups.ButtonText(key)) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            if (tip != null)
                row.Add(new Icon(Icons.Get("Info")) { Size = 14, Color = Color.SecondaryLabel, ToolTip = tip });
            return row;
        }

        /// <summary>
        /// Upstream's <c>PieChart</c>: the authors with at least 1% of the
        /// commits as slices (the rest as one grey "Others"), each named
        /// with its share beside the pie, a line leading to it. The names
        /// are the platform's text, placed once the pie's size is known; a
        /// name's tooltip says its count, which upstream shows on hover.
        /// </summary>
        private sealed class Pie : DrawnWidget
        {
            private readonly Models.StatisticsReport _report;
            private readonly Action<Models.StatisticsAuthor> _picked;
            private readonly Column _holder;
            private readonly List<Node> _labels = [];
            private List<Slice> _slices = [];
            private (float W, float H) _placed;

            public Pie(Models.StatisticsReport report, Action<Models.StatisticsAuthor> picked) : base("StatisticsPie", 0, 0)
            {
                _report = report;
                _picked = picked;
                Label = App.Text("Statistics.TopAuthors");
                Grow = 1;
                MinHeight = 0;
                _holder = new Column { AlignItems = Align.Stretch };
                _holder.Grow = 1;
                _holder.MinHeight = 0;
                _holder.Add(this);
            }

            public Node View => _holder;

            private sealed record Slice(double Start, double Sweep, string Title, int Count, double Share, Color Color, Models.StatisticsAuthor Author)
            {
                /// <summary>Where its name is, down the side.</summary>
                public float NameY { get; set; }

                public double Mid => Start + Sweep / 2;
                public bool Left => Mid > Math.PI * 0.5;
            }

            protected override void Draw(Canvas canvas)
            {
                float w = canvas.Width, h = canvas.Height;
                var (cx, cy, r) = Geometry(w, h);
                var authors = _report.Authors;
                _slices = Slices();
                Spread(_slices, cx, cy, r);
                if (authors.Count == 0)
                {
                    canvas.StrokeEllipse(cx - r, cy - r, r * 2, r * 2, s_colors[0]);
                }
                else if (_slices.Count == 1)
                {
                    canvas.FillEllipse(cx - r, cy - r, r * 2, r * 2, s_colors[0]);
                }
                else
                {
                    foreach (var slice in _slices)
                    {
                        var (x0, y0) = At(cx, cy, r, slice.Start);
                        var (x1, y1) = At(cx, cy, r, slice.Start + slice.Sweep);
                        var large = slice.Sweep > Math.PI ? 1 : 0;
                        canvas.FillPath(FormattableString.Invariant($"M{cx},{cy} L{x0},{y0} A{r},{r} 0 {large} 1 {x1},{y1} Z"), slice.Color);
                        // The line out to its name.
                        var (ex, ey) = At(cx, cy, r, slice.Mid);
                        var (ox, oy) = At(cx, cy, r + 32, slice.Mid);
                        var end = slice.Left ? cx - r - 32 : cx + r + 32;
                        canvas.StrokePath(FormattableString.Invariant($"M{ex},{ey} L{ox},{oy} L{end},{slice.NameY}"), slice.Color);
                    }
                }
                // Names go where the pie now is, once it's drawn.
                if (_placed != (w, h))
                {
                    _placed = (w, h);
                    Application.Post(() => Place(w, h));
                }
            }

            private static (float, float, float) Geometry(float w, float h) =>
                (w / 2, h / 2, Math.Max(8, Math.Min(w, h) / 2 - 48));

            private static (float, float) At(float cx, float cy, float r, double angle) =>
                ((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));

            private List<Slice> Slices()
            {
                var slices = new List<Slice>();
                var authors = _report.Authors;
                if (authors.Count == 1)
                {
                    slices.Add(new(0, 2 * Math.PI, authors[0].User.Name, authors[0].Count, 1, s_colors[0], authors[0]));
                    return slices;
                }
                var total = _report.Total;
                var counted = 0;
                var start = -Math.PI / 2;
                var left = 2 * Math.PI;
                for (var i = 0; i < authors.Count; i++)
                {
                    var author = authors[i];
                    var share = (double)author.Count / total;
                    if (share > 0.01)
                    {
                        var index = i % s_colors.Length;
                        // The last never meets the first in its colour.
                        var color = i == authors.Count - 1 && index == 0 ? s_colors[1] : s_colors[index];
                        var sweep = share * 2 * Math.PI;
                        slices.Add(new(start, sweep, author.User.Name, author.Count, share, color, author));
                        left -= sweep;
                        start += sweep;
                        counted += author.Count;
                    }
                    else
                    {
                        var count = total - counted;
                        slices.Add(new(start, left, "Others", count, (double)count / total, s_others, null));
                        break;
                    }
                }
                return slices;
            }

            /// <summary>Names by their slices, but at least a line apart down
            /// each side: upstream's overlap for small slices.</summary>
            private static void Spread(List<Slice> slices, float cx, float cy, float r)
            {
                foreach (var left in new[] { true, false })
                {
                    var side = slices.Where(s => s.Left == left).ToList();
                    foreach (var slice in side)
                        slice.NameY = At(cx, cy, r + 32, slice.Mid).Item2;
                    side.Sort((a, b) => a.NameY.CompareTo(b.NameY));
                    for (var i = 1; i < side.Count; i++)
                        side[i].NameY = Math.Max(side[i].NameY, side[i - 1].NameY + 20);
                }
            }

            /// <summary>Upstream's names: by the line's end, left of it on
            /// the left side; in the middle for one author, or none.</summary>
            private void Place(float w, float h)
            {
                foreach (var label in _labels)
                    label.Destroy();
                _labels.Clear();
                var (cx, cy, r) = Geometry(w, h);
                if (_slices.Count <= 1)
                {
                    var only = _slices.Count == 1 ? _slices[0].Title : "0";
                    var text = new Text(only) { Color = _slices.Count == 1 ? Color.Rgba(255, 255, 255) : Color.Label };
                    Add(text, top: cy - 10, start: null, end: null, centred: true, w);
                    return;
                }
                foreach (var slice in _slices)
                {
                    var oy = slice.NameY;
                    var label = new Row { AlignItems = Align.Center, Gap = 4 };
                    label.Height = 20;
                    label.Add(new Text(slice.Title) { MaxLines = 1 });
                    label.Add(new Text(slice.Share.ToString("P1")) { Color = Color.SecondaryLabel });
                    label.ToolTip = $"{App.Text("Statistics.TopAuthors.Count")}: {slice.Count}";
                    if (slice.Left)
                        Add(label, oy - 10, start: null, end: w - (cx - r - 32 - 8), centred: false, w);
                    else
                        Add(label, oy - 10, start: cx + r + 32 + 8, end: null, centred: false, w);
                }
            }

            private void Add(Node label, float top, float? start, float? end, bool centred, float w)
            {
                if (centred)
                {
                    var row = new Row { Justify = Justify.Center, AlignItems = Align.Center };
                    row.Height = 20;
                    row.Inset = new Thickness(top, 0, Length.Auto, 0);
                    row.Add(label);
                    label = row;
                }
                else
                {
                    label.Inset = new Thickness(top, end.HasValue ? end.Value : Length.Auto, Length.Auto, start.HasValue ? start.Value : Length.Auto);
                }
                _holder.Add(label);
                _labels.Add(label);
            }

            protected override void OnPointerPressed(float x, float y)
            {
                var (cx, cy, r) = Geometry(_placed.W, _placed.H);
                var (dx, dy) = (x - cx, y - cy);
                if (dx * dx + dy * dy > r * r)
                    return;
                var angle = Math.Atan2(dy, dx);
                if (angle < -Math.PI / 2)
                    angle += 2 * Math.PI;
                if (_slices.FirstOrDefault(s => angle >= s.Start && angle < s.Start + s.Sweep) is { } slice)
                    _picked(slice.Author);
            }

            // Upstream's colours, the same in either appearance.
            private static readonly Color[] s_colors =
            [
                Color.Rgba(255, 165, 0), // Orange
                Color.Rgba(34, 139, 34), // ForestGreen
                Color.Rgba(64, 224, 208), // Turquoise
                Color.Rgba(128, 128, 0), // Olive
                Color.Rgba(255, 0, 255), // Magenta
                Color.Rgba(255, 0, 0), // Red
                Color.Rgba(240, 230, 140), // Khaki
                Color.Rgba(0, 255, 0), // Lime
                Color.Rgba(65, 105, 225), // RoyalBlue
                Color.Rgba(0, 128, 128), // Teal
            ];

            private static readonly Color s_others = Color.Rgba(128, 128, 128);
        }

        /// <summary>
        /// Upstream's <c>Chart</c>: a bar per month (or day) up to the
        /// latest, on lines at eighths of the scale, with the platform's
        /// text for the scale and the times. A picked author's bar stands
        /// beside the total's, which steps back. Bars are at least 10 points
        /// apart, and scroll sideways past the view, natively, from the
        /// latest; a bar's tooltip says its counts, as upstream's hover does.
        /// </summary>
        private sealed class Bars
        {
            private const float Band = 24;
            private readonly Row _view;
            private readonly Column _scale;
            private readonly ScrollView _scroll;
            private readonly Plot _plot;
            private readonly List<Node> _placed = [];
            private readonly Func<string> _author;
            private Models.StatisticsSamples _samples;

            /// <param name="author">The picked author's name.</param>
            public Bars(Func<string> author)
            {
                _author = author;
                _view = new Row { AlignItems = Align.Stretch };
                _view.Grow = 1;
                _view.MinHeight = 0;
                _scale = new Column();
                _scale.Width = 40;
                _scale.Shrink = 0;
                _view.Add(_scale);
                _scroll = new ScrollView(ScrollAxes.Horizontal);
                _scroll.Grow = 1;
                _scroll.MinWidth = 0;
                _scroll.Content.MinWidth = Length.Percent(100);
                _scroll.Content.AlignItems = Align.Stretch;
                _plot = new Plot(this);
                _scroll.Content.Add(_plot);
                _view.Add(_scroll);
            }

            public Node View => _view;

            public void Show(Models.StatisticsSamples samples)
            {
                _samples = samples;
                _plot.Samples = Collect(samples);
            }

            private sealed record Sample(string Label, int Total, int User);

            /// <summary>Upstream's walk from the latest back to the first.</summary>
            private static List<Sample> Collect(Models.StatisticsSamples samples)
            {
                var all = new List<Sample>();
                if (samples == null || samples.Count == 0)
                    return all;
                var time = samples.EndTime;
                while (all.Count < samples.Count && time >= samples.StartTime)
                {
                    var (label, total, user) = samples.GetSample(time);
                    all.Add(new(label, total, user));
                    time = samples.NextSampleTime(time);
                }
                all.Reverse();
                return all;
            }

            /// <summary>The scale, the times and the bars' tooltips, where
            /// the plot was drawn; then the latest in view.</summary>
            private void Place(float w, float h, List<Sample> samples, bool latest)
            {
                foreach (var node in _placed)
                    node.Destroy();
                _placed.Clear();
                var max = _samples?.MaxValue ?? 8;
                var chart = h - Band;
                for (var i = 1; i <= 8; i++)
                {
                    var y = (float)Math.Floor(chart * i * 0.125);
                    var value = Math.Floor(max * (1.0 - i * 0.125));
                    var label = Caption(value.ToString());
                    label.Inset = new Thickness(y - 8, 8, Length.Auto, Length.Auto);
                    _scale.Add(label);
                    _placed.Add(label);
                }
                if (samples.Count == 0)
                    return;
                var step = w / samples.Count;
                // Upstream's names keep 16 points apart.
                var every = Math.Max(1, (int)Math.Ceiling((samples[0].Label.Length * 7 + 16) / step));
                for (var i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    var x = i * step;
                    // Upstream leaves out a name that would run off the end.
                    var half = sample.Label.Length * 3.5f;
                    if ((samples.Count - 1 - i) % every == 0 && x + step / 2 + half <= w && x + step / 2 - half >= 0)
                    {
                        var time = new Row { Justify = Justify.Center };
                        time.Width = step * every;
                        time.Inset = new Thickness(chart + 6, Length.Auto, Length.Auto, x + step / 2 - step * every / 2);
                        time.Add(Caption(sample.Label));
                        _scroll.Content.Add(time);
                        _placed.Add(time);
                    }
                    if (sample.Total == 0)
                        continue;
                    var tip = new Row();
                    tip.Width = step;
                    tip.Height = chart;
                    tip.Inset = new Thickness(0, Length.Auto, Length.Auto, x);
                    var commits = $"{sample.Label}\n{Popups.ButtonText("Statistics.TopAuthors.Count")}: {sample.Total}";
                    tip.ToolTip = _samples.HasSpecialUser ? $"{commits}\n{_author()}: {sample.User}" : commits;
                    _scroll.Content.Add(tip);
                    _placed.Add(tip);
                }
                if (latest)
                    _scroll.ScrollTo(float.MaxValue, 0);
            }

            private static Text Caption(string text) => new(text) { Style = TextStyle.Caption, Color = Color.SecondaryLabel };

            private sealed class Plot : DrawnWidget
            {
                private readonly Bars _bars;
                private List<Sample> _samples = [];
                private (float W, float H, List<Sample> Samples) _placed;

                public Plot(Bars bars) : base("StatisticsBars", 0, 0)
                {
                    _bars = bars;
                    Label = App.Text("Statistics.Commits");
                    Grow = 1;
                }

                public List<Sample> Samples
                {
                    set
                    {
                        _samples = value;
                        // Upstream's bars are at least 10 points apart.
                        MinWidth = value.Count * 10;
                        Redraw();
                    }
                }

                protected override void Draw(Canvas canvas)
                {
                    float w = canvas.Width, h = canvas.Height, chart = h - Band;
                    for (var i = 1; i <= 8; i++)
                    {
                        var y = (float)Math.Floor(chart * i * 0.125) + 0.5f;
                        canvas.FillRect(0, y, w, 0.5f, s_lines);
                    }
                    var samples = _samples;
                    var max = _bars._samples?.MaxValue ?? 8;
                    if (samples.Count > 0)
                    {
                        var step = w / samples.Count;
                        var user = _bars._samples.HasSpecialUser;
                        var width = user ? Math.Min(step * 0.5f - 2.5f, 14) : Math.Min(step - 3, 28);
                        for (var i = 0; i < samples.Count; i++)
                        {
                            var sample = samples[i];
                            var middle = i * step + step / 2;
                            canvas.FillRect(middle - 0.5f, chart, 1, 4, s_ticks);
                            if (sample.Total == 0)
                                continue;
                            var top = chart * (1 - (float)sample.Total / max);
                            if (user)
                            {
                                var start = middle - 1 - width;
                                Bar(canvas, start, top, width, chart - top, s_behind);
                                if (sample.User > 0)
                                {
                                    var mine = chart * (1 - (float)sample.User / max);
                                    Bar(canvas, start + width + 2, mine, width, chart - mine, Color.Accent);
                                }
                            }
                            else
                            {
                                Bar(canvas, middle - width / 2, top, width, chart - top, Color.Accent);
                            }
                        }
                    }
                    // The labels and tips go where this was drawn; new
                    // samples start from the latest.
                    if (_placed.W != w || _placed.H != h || _placed.Samples != samples)
                    {
                        var latest = _placed.Samples != samples;
                        _placed = (w, h, samples);
                        Application.Post(() => _bars.Place(w, h, samples, latest));
                    }
                }

                // Upstream's rounded tops: a rounded rect with its bottom
                // corners squared by a plain one.
                private static void Bar(Canvas canvas, float x, float y, float w, float h, Color color)
                {
                    canvas.FillRect(x, y, w, h, color, Math.Min(2, h / 2));
                    if (h > 2)
                        canvas.FillRect(x, y + h - 2, w, 2, color);
                }
            }

            private static readonly Color s_lines = Color.Rgba(128, 128, 128, 102);
            private static readonly Color s_ticks = Color.Rgba(128, 128, 128);
            private static readonly Color s_behind = Color.Rgba(128, 128, 128, 72);
        }
    }
}
