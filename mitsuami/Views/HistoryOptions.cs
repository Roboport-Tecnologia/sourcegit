using System;
using System.Collections.Generic;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's history toolbar (a search toggle and Advanced Options),
    /// in the repository's toolbar band: a native table's header holds no
    /// controls. The options are upstream's menu: the layout, the show
    /// flags, the order, what the graph highlights and which columns show,
    /// each group under its heading (a disabled item, as upstream's), the
    /// chosen ones checked.
    /// </summary>
    public static class HistoryOptions
    {
        public static void AddTo(Container group, ViewModels.Histories vm)
        {
            var search = new ToggleButton(App.Text("Repository.Search.ToolTip"))
            {
                Icon = Icons.Get("Search", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Repository.Search.ToolTip"),
            };
            search.Width = 32;
            search.Changed += on => vm.IsSearchingCommits = on;
            Bind.OneWay(vm, nameof(vm.IsSearchingCommits), () => vm.IsSearchingCommits, on => search.IsChecked = on, owner: search);
            group.Add(search);

            var options = new MenuButton(App.Text("Histories.AdvancedOptions"))
            {
                Icon = Icons.Get("Settings2", 13),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Histories.AdvancedOptions"),
            };
            options.Width = 32;
            // The checks follow the settings, however they change.
            void Refresh() => options.Menu = Menu(vm);
            Refresh();
            var prefs = ViewModels.Preferences.Instance;
            void Changed(object sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
            vm.PropertyChanged += Changed;
            prefs.PropertyChanged += Changed;
            options.Forgotten += () =>
            {
                vm.PropertyChanged -= Changed;
                prefs.PropertyChanged -= Changed;
            };
            group.Add(options);
        }

        private static IReadOnlyList<MenuEntry> Menu(ViewModels.Histories vm)
        {
            var prefs = ViewModels.Preferences.Instance;
            MenuItem Heading(string key) => new(App.Text(key), () => { }) { Enabled = false };
            MenuItem Radio(string key, bool on, Action choose) => new(App.Text(key), choose) { Checked = on, Radio = true };
            MenuItem Check(string key, bool on, Action toggle) => new(App.Text(key), toggle) { Checked = on };
            MenuItem Flag(string key, Models.HistoryShowFlags flag) => Check(key, vm.HasShowFlag(flag), () => vm.ToggleShowFlag(flag));
            MenuItem Highlight(string key, Models.CommitGraphHighlighting h) => Radio(key, vm.GraphHighlighting == h, () => vm.GraphHighlighting = h);

            var horizontal = prefs.UseTwoColumnsLayoutInHistories;
            return
            [
                Heading("Repository.HistoriesLayout"),
                Radio("Repository.HistoriesLayout.Horizontal", horizontal, () => prefs.UseTwoColumnsLayoutInHistories = true),
                Radio("Repository.HistoriesLayout.Vertical", !horizontal, () => prefs.UseTwoColumnsLayoutInHistories = false),
                MenuSeparator.Instance,
                Heading("Repository.ShowFlags"),
                Flag("Repository.ShowLostCommits", Models.HistoryShowFlags.Reflog),
                Flag("Repository.ShowFirstParentOnly", Models.HistoryShowFlags.FirstParentOnly),
                Flag("Repository.ShowDecoratedCommitsOnly", Models.HistoryShowFlags.SimplifyByDecoration),
                MenuSeparator.Instance,
                Heading("Repository.HistoriesOrder"),
                Radio("Repository.HistoriesOrder.ByDate", !vm.EnableTopoOrder, () => vm.EnableTopoOrder = false),
                Radio("Repository.HistoriesOrder.Topo", vm.EnableTopoOrder, () => vm.EnableTopoOrder = true),
                MenuSeparator.Instance,
                Heading("Histories.HighlightsInGraph"),
                Highlight("Histories.HighlightsInGraph.All", Models.CommitGraphHighlighting.All),
                Highlight("Histories.HighlightsInGraph.CurrentBranchOnly", Models.CommitGraphHighlighting.CurrentBranchOnly),
                Highlight("Histories.HighlightsInGraph.SelectedCommitsOnly", Models.CommitGraphHighlighting.SelectedCommitsOnly),
                Highlight("Histories.HighlightsInGraph.SelectedCommitsOnlyFirstParent", Models.CommitGraphHighlighting.SelectedCommitsOnlyFirstParent),
                Highlight("Histories.HighlightsInGraph.CurrentBranchAndSelectedCommits", Models.CommitGraphHighlighting.CurrentBranchAndSelectedCommits),
                MenuSeparator.Instance,
                Heading("Histories.ShowColumns"),
                Check("Histories.Header.Author", vm.IsAuthorColumnVisible, () => vm.IsAuthorColumnVisible = !vm.IsAuthorColumnVisible),
                Check("Histories.Header.SHA", vm.IsSHAColumnVisible, () => vm.IsSHAColumnVisible = !vm.IsSHAColumnVisible),
                Check("Histories.Header.AuthorTime", vm.IsAuthorTimeColumnVisible, () => vm.IsAuthorTimeColumnVisible = !vm.IsAuthorTimeColumnVisible),
                Check("Histories.Header.CommitTime", vm.IsCommitTimeColumnVisible, () => vm.IsCommitTimeColumnVisible = !vm.IsCommitTimeColumnVisible),
            ];
        }
    }
}
