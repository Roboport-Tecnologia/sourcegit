using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's command palettes (<c>Views/*CommandPalette.axaml</c>), as
    /// Xcode's Open Quickly: a sheet with a search field over a list that
    /// filters as it's typed. The field keeps the keyboard: ↑ and ↓ move
    /// through the list, Return runs the pick (as a double-click does),
    /// Escape closes. Upstream opens one by setting the launcher's
    /// <c>CommandPalette</c> and closes it by clearing it, which the sheet
    /// follows: a palette's command opening another replaces it.
    /// </summary>
    public static class CommandPalette
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Attach(ViewModels.Launcher launcher)
        {
            Bind.OneWay(launcher, nameof(launcher.CommandPalette), () => launcher.CommandPalette, palette =>
            {
                Shown?.Destroy();
                Shown = null;
                if (palette != null)
                    Show(launcher, palette);
            }, owner: null);
        }

        /// <summary>One palette: what it lists, how, and what a pick does.</summary>
        private sealed record PaletteSpec(
            string Placeholder,
            string Caption,
            string FilterProperty,
            Func<string> Filter,
            Action<string> SetFilter,
            string[] ItemsProperties,
            Func<IReadOnlyList<object>> Items,
            Func<object> Selected,
            Action<object> Select,
            Func<object, string> Key,
            Func<object, Node> Row,
            Action Run,
            string LoadingProperty = null,
            Func<bool> Loading = null);

        private static void Show(ViewModels.Launcher launcher, ViewModels.ICommandPalette palette)
        {
            if (Spec(launcher, palette) is not { } spec)
            {
                palette.Close();
                return;
            }
            var sheet = Shown = new Window(App.MainWindow, spec.Placeholder, 560, 420);
            // The sheet closed by itself (its close button): the palette too.
            sheet.Closed += () =>
            {
                if (Shown == sheet)
                {
                    Shown = null;
                    if (launcher.CommandPalette == palette)
                        launcher.CommandPalette = null;
                }
            };

            var column = new Column { Padding = Spacing.Md, Gap = Spacing.Sm, AlignItems = Align.Stretch };
            column.Grow = 1;
            var field = new SearchInput { Placeholder = spec.Placeholder };
            Bind.Search(field, palette, spec.FilterProperty, spec.Filter, spec.SetFilter, owner: field);
            column.Add(field);
            if (spec.Caption != null)
                column.Add(new Text(spec.Caption) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });

            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            area.Grow = 1;
            area.MinHeight = 0;
            var list = new ListView<object>(spec.Key, spec.Row, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 28,
            };
            list.Grow = 1;
            list.MinHeight = 0;
            area.Add(list);
            if (spec.LoadingProperty != null)
            {
                var spinner = new Spinner { AlignSelf = Align.Center };
                area.Add(spinner);
                Bind.OneWay(palette, spec.LoadingProperty, spec.Loading, busy =>
                {
                    spinner.IsVisible = busy;
                    list.IsVisible = !busy;
                }, owner: spinner);
            }
            column.Add(area);
            sheet.Content = column;

            void Refresh()
            {
                var items = spec.Items() ?? [];
                list.Items = items;
                ShowSelected();
            }
            void ShowSelected()
            {
                var selected = spec.Selected();
                list.SelectedItems = selected != null && list.Items.Contains(selected) ? [selected] : [];
                if (selected != null && list.Items.Contains(selected))
                    list.ScrollTo(selected);
            }
            foreach (var property in spec.ItemsProperties)
                Bind.OneWay(palette, property, spec.Items, _ => Refresh(), owner: list);
            list.SelectionChanged += selected =>
            {
                if (selected is [var item] && item != spec.Selected())
                    spec.Select(item);
            };
            list.Activated += item =>
            {
                spec.Select(item);
                spec.Run();
            };

            // The field keeps the keyboard and drives the list.
            void Step(int by)
            {
                var items = list.Items;
                if (items.Count == 0)
                    return;
                var at = items.ToList().IndexOf(spec.Selected());
                var next = at < 0 ? (by > 0 ? 0 : items.Count - 1) : Math.Clamp(at + by, 0, items.Count - 1);
                spec.Select(items[next]);
                ShowSelected();
            }
            field.OnKey(Shortcut.Of(Key.Down), () => Step(1));
            field.OnKey(Shortcut.Of(Key.Up), () => Step(-1));
            field.OnKey(Shortcut.Of(Key.Enter), spec.Run);
            field.OnKey(Shortcut.Of(Key.Escape), palette.Close);
            Refresh();
        }

        private static ViewModels.Repository Repo(ViewModels.Launcher launcher) => launcher.ActivePage?.Data as ViewModels.Repository;

        private static PaletteSpec Spec(ViewModels.Launcher launcher, ViewModels.ICommandPalette palette)
        {
            switch (palette)
            {
                case ViewModels.LauncherPagesCommandPalette pages:
                    return new(App.Text("Launcher.Pages"), null,
                        nameof(pages.SearchFilter), () => pages.SearchFilter, v => pages.SearchFilter = v,
                        [nameof(pages.VisiblePages), nameof(pages.VisibleRepos)],
                        () => [.. pages.VisiblePages ?? [], .. pages.VisibleRepos ?? []],
                        () => (object)pages.SelectedPage ?? pages.SelectedRepo,
                        item =>
                        {
                            if (item is ViewModels.LauncherPage page)
                                pages.SelectedPage = page;
                            else if (item is ViewModels.RepositoryNode node)
                                pages.SelectedRepo = node;
                        },
                        item => item switch
                        {
                            ViewModels.LauncherPage page => $"page {page.Node.Id}",
                            ViewModels.RepositoryNode node => $"repo {node.Id}",
                            _ => item.GetHashCode().ToString(),
                        },
                        item => item switch
                        {
                            ViewModels.LauncherPage page => Line(page.Node.IsRepository ? "Repository" : "Welcome",
                                page.Node.IsRepository ? page.Node.Name : App.Text("PageTabBar.Welcome.Title"), App.Text("Launcher.Pages")),
                            ViewModels.RepositoryNode node => Line("Folder.Open", node.Name, node.Id, App.Text("Launcher.OpenRepository")),
                            _ => new Text(item.ToString()),
                        },
                        pages.OpenOrSwitchTo);
                case WorkspaceCommandPalette workspaces:
                    return new(App.Text("Launcher.Workspaces"), null,
                        nameof(workspaces.Filter), () => workspaces.Filter, v => workspaces.Filter = v,
                        [nameof(workspaces.Visible)], () => workspaces.Visible.Cast<object>().ToList(),
                        () => workspaces.Selected, item => workspaces.Selected = (ViewModels.Workspace)item,
                        item => item.GetHashCode().ToString(),
                        item =>
                        {
                            var workspace = (ViewModels.Workspace)item;
                            var row = new Row { AlignItems = Align.Center, Gap = Metrics.RowGap, Padding = Metrics.ListRow(Spacing.Sm, Spacing.Sm) };
                            row.Add(Workspaces.IconOf(workspace));
                            var name = new Text(workspace.Name ?? "") { MaxLines = 1 };
                            name.Grow = 1;
                            name.Shrink = 1;
                            name.MinWidth = 0;
                            row.Add(name);
                            if (workspace.IsActive)
                                row.Add(new Icon(Icons.Get("Check")) { Size = 12 });
                            return row;
                        },
                        workspaces.Switch);
                case ViewModels.RepositoryCommandPalette commands:
                    return new(App.Text("Hotkeys.Repo.OpenCommandPalette"), null,
                        nameof(commands.Filter), () => commands.Filter, v => commands.Filter = v,
                        [nameof(commands.VisibleCmds)], () => commands.VisibleCmds?.Cast<object>().ToList() ?? [],
                        () => commands.SelectedCmd, item => commands.SelectedCmd = (ViewModels.RepositoryCommandPaletteCmd)item,
                        item => ((ViewModels.RepositoryCommandPaletteCmd)item).Label,
                        item => Line(((ViewModels.RepositoryCommandPaletteCmd)item).Icon, ((ViewModels.RepositoryCommandPaletteCmd)item).Label),
                        commands.Exec);
                case ViewModels.CheckoutCommandPalette checkout:
                    return Branches(App.Text("Checkout"), checkout, nameof(checkout.Filter), () => checkout.Filter, v => checkout.Filter = v,
                        nameof(checkout.Branches), () => checkout.Branches, () => checkout.SelectedBranch, b => checkout.SelectedBranch = b,
                        () => _ = checkout.ExecAsync());
                case ViewModels.MergeCommandPalette merge:
                    return Branches(App.Text("Merge"), merge, nameof(merge.Filter), () => merge.Filter, v => merge.Filter = v,
                        nameof(merge.Branches), () => merge.Branches, () => merge.SelectedBranch, b => merge.SelectedBranch = b,
                        merge.Launch);
                case ViewModels.CompareCommandPalette compare:
                    var compareTitle = compare.BasedOn switch
                    {
                        Models.Branch b => $"{App.Text("Compare")} {b.FriendlyName}",
                        Models.Tag t => $"{App.Text("Compare")} {t.Name}",
                        _ => App.Text("Compare"),
                    };
                    return new(compareTitle, App.Text("CommandPalette.BranchesAndTags"),
                        nameof(compare.Filter), () => compare.Filter, v => compare.Filter = v,
                        [nameof(compare.Refs)], () => compare.Refs ?? [],
                        () => compare.CompareTo, item => compare.CompareTo = item,
                        item => item switch
                        {
                            Models.Branch b => $"branch {b.FullName}",
                            Models.Tag t => $"tag {t.Name}",
                            _ => item.ToString(),
                        },
                        item => item switch
                        {
                            Models.Branch b => Line("Branch", b.FriendlyName),
                            Models.Tag t => Line("Tag", t.Name),
                            _ => new Text(item.ToString()),
                        },
                        () =>
                        {
                            if (Repo(launcher) is { } repo && compare.Launch() is { } launched)
                                Compare.Show(repo, launched);
                        });
                case ViewModels.FileHistoryCommandPalette history:
                    return Files(history, nameof(history.Filter), () => history.Filter, v => history.Filter = v,
                        nameof(history.VisibleFiles), () => history.VisibleFiles, () => history.SelectedFile, f => history.SelectedFile = f,
                        () =>
                        {
                            if (history.Launch() is { } launched)
                                HistoryWindows.ShowFile(launched);
                        },
                        nameof(history.IsLoading), () => history.IsLoading);
                case ViewModels.OpenFileCommandPalette open:
                    return Files(open, nameof(open.Filter), () => open.Filter, v => open.Filter = v,
                        nameof(open.VisibleFiles), () => open.VisibleFiles, () => open.SelectedFile, f => open.SelectedFile = f,
                        open.Launch, nameof(open.IsLoading), () => open.IsLoading);
                case ViewModels.BlameCommandPalette blame:
                    return Files(blame, nameof(blame.Filter), () => blame.Filter, v => blame.Filter = v,
                        nameof(blame.VisibleFiles), () => blame.VisibleFiles, () => blame.SelectedFile, f => blame.SelectedFile = f,
                        () =>
                        {
                            blame.Close();
                            _ = Dialogs.Alert(App.MainWindow, "Blame isn't ported to mitsuami yet: it needs the code view.", "", [Popups.ButtonText("Sure")]);
                        },
                        nameof(blame.IsLoading), () => blame.IsLoading);
                case ViewModels.ExecuteCustomActionCommandPalette actions:
                    return new(App.Text("Repository.CustomActions"), App.Text("CommandPalette.RepositoryActions"),
                        nameof(actions.Filter), () => actions.Filter, v => actions.Filter = v,
                        [nameof(actions.VisibleActions)], () => actions.VisibleActions?.Cast<object>().ToList() ?? [],
                        () => actions.Selected, item => actions.Selected = (ViewModels.ExecuteCustomActionCommandPaletteCmd)item,
                        item => ((ViewModels.ExecuteCustomActionCommandPaletteCmd)item).Name,
                        item =>
                        {
                            var cmd = (ViewModels.ExecuteCustomActionCommandPaletteCmd)item;
                            return Line("Action", cmd.Name, cmd.IsGlobal ? "GLOBAL" : null);
                        },
                        () => _ = actions.ExecAsync());
                default:
                    return null;
            }
        }

        private static PaletteSpec Branches(string title, INotifyPropertyChanged vm, string filterProperty, Func<string> filter, Action<string> setFilter,
            string itemsProperty, Func<List<Models.Branch>> branches, Func<Models.Branch> selected, Action<Models.Branch> select, Action run) =>
            new(title, App.Text("CommandPalette.Branches"), filterProperty, filter, setFilter,
                [itemsProperty], () => branches()?.Cast<object>().ToList() ?? [],
                selected, item => select((Models.Branch)item),
                item => ((Models.Branch)item).FullName,
                item => Line("Branch", ((Models.Branch)item).FriendlyName),
                run);

        private static PaletteSpec Files(INotifyPropertyChanged vm, string filterProperty, Func<string> filter, Action<string> setFilter,
            string itemsProperty, Func<List<string>> files, Func<string> selected, Action<string> select, Action run,
            string loadingProperty, Func<bool> loading) =>
            new(App.Text("CommandPalette.RevisionFiles"), App.Text("CommandPalette.RevisionFiles"), filterProperty, filter, setFilter,
                [itemsProperty], () => files()?.Cast<object>().ToList() ?? [],
                selected, item => select((string)item),
                item => (string)item,
                item => Line("File", Path.GetFileName((string)item), Path.GetDirectoryName((string)item)),
                run, loadingProperty, loading);

        /// <summary>A row: its icon and title, then a detail in the
        /// secondary colour, and a note at the end.</summary>
        private static Node Line(string icon, string title, string detail = null, string note = null)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Metrics.RowGap, Padding = Metrics.ListRow(Spacing.Sm, Spacing.Sm) };
            row.Add(new Icon(Icons.Get(icon)) { Size = 14 });
            var text = new Text(title) { MaxLines = 1 };
            text.Shrink = 0;
            row.Add(text);
            if (!string.IsNullOrEmpty(detail))
            {
                var more = new Text(detail) { MaxLines = 1, Color = Color.SecondaryLabel };
                more.Shrink = 1;
                more.MinWidth = 0;
                row.Add(more);
            }
            var gap = new Row();
            gap.Grow = 1;
            row.Add(gap);
            if (!string.IsNullOrEmpty(note))
                row.Add(new Text(note) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            return row;
        }
    }
}
