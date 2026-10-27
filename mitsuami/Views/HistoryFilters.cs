using System;
using System.Collections.Specialized;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's history filter bar, over the history while branches,
    /// tags or folders filter it: the mode's icon (filtered in, or hidden),
    /// the filters as chips that wrap (or, collapsed, how many there are),
    /// a toggle collapsing them and a button clearing them all. The bar's
    /// background and the chips' outlines are drawn; the rest is the
    /// platform's.
    /// </summary>
    public static class HistoryFilters
    {
        public static Node Create(ViewModels.Repository vm)
        {
            var bar = new Column { AlignItems = Align.Stretch };
            bar.Shrink = 0;
            var row = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 12, 0, 8) };
            row.MinHeight = 30;
            row.Add(new Background { Inset = new Thickness(0) });
            var mode = new Icon(Icons.Get("Filter")) { Size = 12, Color = Color.SecondaryLabel };
            row.Add(mode);

            var chips = new Row { AlignItems = Align.Center, Wrap = true, Margin = new Thickness(4, 8) };
            chips.Grow = 1;
            chips.Width = 0;
            row.Add(chips);
            var summary = new Text { Color = Color.SecondaryLabel, Margin = new Thickness(0, 0, 0, 6) };
            summary.Grow = 1;
            row.Add(summary);

            var collapse = new Button(App.Text("Repository.FilterCommits.Collapse"))
            {
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                Margin = new Thickness(0, 0, 0, 16),
            };
            collapse.Click += () => vm.IsHistoryFiltersCollapsed = !vm.IsHistoryFiltersCollapsed;
            row.Add(collapse);
            var clear = new Button(App.Text("Repository.ClearAllCommitsFilter"))
            {
                Icon = Icons.Get("RemoveAll", 14, 1),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Repository.ClearAllCommitsFilter"),
                Margin = new Thickness(0, 0, 0, Icons.ButtonGap(0)),
            };
            clear.Click += vm.ClearHistoryFilters;
            row.Add(clear);
            bar.Add(row);
            bar.Add(new Separator());

            // A filter's chip follows it: whether its branch or tag is still
            // there is known once they've loaded.
            var followed = new System.Collections.Generic.List<Models.HistoryFilter>();
            void FilterChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => Show();
            void Show()
            {
                var filters = vm.UIStates?.HistoryFilters;
                foreach (var f in followed)
                    f.PropertyChanged -= FilterChanged;
                followed.Clear();
                if (filters != null)
                {
                    followed.AddRange(filters);
                    foreach (var f in followed)
                        f.PropertyChanged += FilterChanged;
                }
                bar.IsVisible = filters is { Count: > 0 };
                if (filters is not { Count: > 0 })
                    return;
                mode.IsVisible = vm.HistoryFilterMode != Models.FilterMode.None;
                mode.Vector = Icons.Get(vm.HistoryFilterMode == Models.FilterMode.Excluded ? "EyeClose" : "Filter");
                var collapsed = vm.IsHistoryFiltersCollapsed;
                chips.IsVisible = !collapsed;
                summary.IsVisible = collapsed;
                summary.Value = $"{filters.Count} {App.Text("Repository.FilterCommits.Summary")}";
                var toggle = App.Text(collapsed ? "Repository.FilterCommits.Expand" : "Repository.FilterCommits.Collapse");
                collapse.Label = toggle;
                collapse.ToolTip = toggle;
                collapse.Icon = Icons.Get(collapsed ? "DoubleDown" : "DoubleUp", 12);
                foreach (var chip in chips.Children.ToArray())
                    chip.Destroy();
                foreach (var filter in filters)
                    chips.Add(Chip(vm, filter));
            }

            // The filters live in the repository's UI state, which opening
            // it replaces.
            Models.RepositoryUIStates watched = null;
            void Changed(object sender, NotifyCollectionChangedEventArgs e) => Show();
            void Watch()
            {
                if (watched != null)
                    watched.HistoryFilters.CollectionChanged -= Changed;
                watched = vm.UIStates;
                if (watched != null)
                    watched.HistoryFilters.CollectionChanged += Changed;
                Show();
            }
            Bind.OneWay(vm, nameof(vm.UIStates), () => vm.UIStates, _ => Watch(), owner: bar);
            Bind.OneWay(vm, nameof(vm.HistoryFilterMode), () => vm.HistoryFilterMode, _ => Show(), owner: bar);
            Bind.OneWay(vm, nameof(vm.IsHistoryFiltersCollapsed), () => vm.IsHistoryFiltersCollapsed, _ => Show(), owner: bar);
            bar.Forgotten += () =>
            {
                if (watched != null)
                    watched.HistoryFilters.CollectionChanged -= Changed;
                foreach (var f in followed)
                    f.PropertyChanged -= FilterChanged;
            };
            return bar;
        }

        /// <summary>A filter: 20 points high in a 22-point line, outlined
        /// in green (in) or red (hidden), its kind's icon (orange Error when
        /// its branch or tag is gone), the pattern without its refs prefix
        /// and a button removing it.</summary>
        private static Node Chip(ViewModels.Repository vm, Models.HistoryFilter filter)
        {
            var line = new Row { AlignItems = Align.Center, Margin = new Thickness(1, 6, 1, 0) };
            line.MinHeight = 20;
            var chip = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 8) };
            chip.MinHeight = 20;
            var (r, g, b) = filter.Mode == Models.FilterMode.Excluded ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)128, (byte)0);
            chip.Add(new Outline(Color.Rgba(r, g, b)) { Inset = new Thickness(0) });

            var isRef = filter.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch or Models.FilterType.Tag;
            var icon = !isRef ? "Folder" : !filter.IsValid ? "Error" : filter.Type == Models.FilterType.Tag ? "Tag" : "Branch";
            chip.Add(new Icon(Icons.Get(icon)) { Size = 12, Color = isRef && !filter.IsValid ? Color.Rgba(255, 140, 0) : Color.Label });

            var pattern = filter.Pattern ?? "";
            if (pattern.StartsWith("refs/heads/", StringComparison.Ordinal))
                pattern = pattern[11..];
            else if (pattern.StartsWith("refs/remotes/", StringComparison.Ordinal))
                pattern = pattern[13..];
            var text = new Text(pattern) { MaxLines = 1, Margin = new Thickness(0, 8, 0, 4) };
            text.MaxWidth = 480;
            text.ToolTip = filter.Pattern;
            chip.Add(text);

            var remove = new Button($"{App.Text("Repository.FilterCommits.Default")} {pattern}")
            {
                Icon = Icons.Get("Close", 8),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
            };
            remove.Click += () => vm.RemoveHistoryFilter(filter);
            chip.Add(remove);
            line.Add(chip);
            return line;
        }

        /// <summary>Behind the bar: upstream's popup colour, the controls'
        /// background here.</summary>
        private sealed class Background() : DrawnWidget("HistoryFilterBar", 0, 0)
        {
            protected override void Draw(Canvas canvas) => canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.ControlBackground);
        }

        /// <summary>A chip's outline, its ends round.</summary>
        private sealed class Outline(Color color) : DrawnWidget("HistoryFilterChip", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.StrokeRect(0.5f, 0.5f, canvas.Width - 1, canvas.Height - 1, color, 1, (canvas.Height - 1) / 2);
        }
    }
}
