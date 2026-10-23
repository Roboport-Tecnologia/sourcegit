using System.Collections.Generic;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>CommitSearchView</c>, in the sidebar's place while
    /// searching: its title, the platform's search field, what to search
    /// by, whether only in the current branch, then the commits found
    /// (picking one navigates to it). Upstream's suggestions (paths or
    /// authors) show under the field as the platform's popover.
    /// </summary>
    public static class CommitSearch
    {
        public static Node Create(ViewModels.SearchCommitContext ctx)
        {
            var panel = new Column { AlignItems = Align.Stretch, Padding = new Thickness(0, 4, 0, 6) };
            panel.Add(new Text(App.Text("Repository.Search"))
            {
                Color = Color.SecondaryLabel,
                Weight = FontWeight.Bold,
                Margin = new Thickness(6, 0, 8, 0),
                AlignSelf = Align.Center,
            });

            var field = new SearchInput { Placeholder = App.Text("Repository.Search.ToolTip") };
            Bind.OneWay(ctx, nameof(ctx.Filter), () => ctx.Filter, v => field.Value = v ?? "", owner: panel);
            field.Changed += text => ctx.Filter = text;
            // The platform's field searches when it does: on Return, or as
            // the user pauses typing.
            field.Search += text =>
            {
                ctx.Filter = text;
                if (string.IsNullOrEmpty(text))
                    ctx.ClearFilter();
                else
                    ctx.StartSearch();
            };
            var anchor = new Column { AlignItems = Align.Stretch };
            anchor.Add(field);
            anchor.Add(Suggestions(ctx));
            panel.Add(anchor);

            // Upstream's "SEARCH BY" label beside the methods, and under
            // them the current-branch check for every method but SHA.
            var by = new Row { AlignItems = Align.Center, Gap = 8, Margin = new Thickness(4) };
            by.Add(new Text(App.Text("Repository.Search.Method")) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            var method = new Select(App.Text("Repository.Search.Method"))
            {
                Options =
                [
                    App.Text("Repository.Search.BySHA"),
                    App.Text("Repository.Search.ByAuthor"),
                    App.Text("Repository.Search.ByMessage"),
                    App.Text("Repository.Search.ByPath"),
                    App.Text("Repository.Search.ByContent"),
                ],
            };
            method.Grow = 1;
            method.Changed += i => ctx.Method = i;
            by.Add(method);
            panel.Add(by);
            var current = new Checkbox(App.Text("Repository.Search.InCurrentBranch")) { Margin = new Thickness(0, 4, 4, 0) };
            current.Changed += on => ctx.OnlySearchCurrentBranch = on;
            panel.Add(current);
            Bind.OneWay(ctx, nameof(ctx.Method), () => ctx.Method, i =>
            {
                method.SelectedIndex = i;
                current.IsVisible = i > 0;
            }, owner: panel);
            Bind.OneWay(ctx, nameof(ctx.OnlySearchCurrentBranch), () => ctx.OnlySearchCurrentBranch, on => current.IsChecked = on, owner: panel);

            // The commits found, over upstream's empty icon or a spinner.
            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center, Margin = new Thickness(6, 0) };
            area.Grow = 1;
            area.MinHeight = 0;
            var results = new ListView<Models.Commit>(c => c.SHA, Row, itemAnimations: false) { EstimatedRowHeight = 28 };
            results.Grow = 1;
            results.MinHeight = 0;
            results.SelectionChanged += selected => ctx.Selected = selected is [var commit] ? commit : null;
            area.Add(results);
            var empty = new Icon(Icons.Get("Empty")) { Size = 48, Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(empty);
            var spinner = new Spinner { AlignSelf = Align.Center };
            area.Add(spinner);
            panel.Add(area);

            void Show()
            {
                var found = ctx.Results ?? [];
                results.Items = found;
                results.IsVisible = found.Count > 0;
                empty.IsVisible = found.Count == 0 && !ctx.IsQuerying;
                spinner.IsVisible = ctx.IsQuerying;
            }
            Bind.OneWay(ctx, nameof(ctx.Results), () => ctx.Results, _ => Show(), owner: panel);
            Bind.OneWay(ctx, nameof(ctx.IsQuerying), () => ctx.IsQuerying, _ => Show(), owner: panel);
            Bind.OneWay(ctx, nameof(ctx.Selected), () => ctx.Selected, c => results.SelectedItems = c == null ? [] : [c], owner: panel);
            return panel;
        }

        /// <summary>
        /// Upstream's suggestions while typing a path or an author: a
        /// popover under the field, each a row 42 high (a file's icon, its
        /// name and folder; or an author's avatar, name and email), up to
        /// 400 high. Picking one searches for it, as upstream's Enter or tap.
        /// </summary>
        private static Node Suggestions(ViewModels.SearchCommitContext ctx)
        {
            var popover = new Popover { Padding = 4 };
            var list = new ListView<object>(Key, Suggestion, itemAnimations: false) { EstimatedRowHeight = 42 };
            list.MinWidth = 300;
            popover.Add(list);
            list.SelectionChanged += picked =>
            {
                if (picked is not [var chosen])
                    return;
                ctx.Filter = chosen is Models.User user ? user.ToString().EscapeForBRE() : chosen as string;
                ctx.StartSearch();
            };
            popover.Closed += ctx.ClearSuggestions;
            Bind.OneWay(ctx, nameof(ctx.Suggestions), () => ctx.Suggestions, suggestions =>
            {
                var shown = suggestions is { Count: > 0 };
                if (shown)
                {
                    list.Items = suggestions;
                    list.Height = System.Math.Min(400, suggestions.Count * 42);
                }
                popover.IsShown = shown;
            }, owner: popover);
            return popover;
        }

        private static string Key(object suggestion) => suggestion is Models.User user ? $"@{user}" : suggestion.ToString();

        private static Node Suggestion(object suggestion)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(4, 8) };
            row.Height = 42;
            var lines = new Column();
            if (suggestion is Models.User user)
            {
                var avatar = new Avatar(24, menu: false) { User = user };
                row.Add(avatar.View);
                lines.Add(new Text(user.Name) { MaxLines = 1 });
                lines.Add(new Text(user.Email) { Style = TextStyle.Caption, Color = Color.SecondaryLabel, MaxLines = 1, Margin = new Thickness(2, 0, 0, 0) });
            }
            else if (suggestion is string path)
            {
                row.Add(new Icon(Icons.Get("File")) { Size = 24 });
                lines.Add(new Text(System.IO.Path.GetFileName(path)) { MaxLines = 1 });
                lines.Add(new Text(System.IO.Path.GetDirectoryName(path) ?? "") { Style = TextStyle.Caption, Color = Color.SecondaryLabel, MaxLines = 1, Margin = new Thickness(2, 0, 0, 0) });
            }
            row.Add(lines);
            return row;
        }

        /// <summary>A commit found: its author's avatar in 24 points, a
        /// green check when it's in the current branch, the subject, and
        /// the date in the secondary colour.</summary>
        private static Node Row(Models.Commit commit)
        {
            var row = new Row { AlignItems = Align.Center };
            row.Height = 28;
            var face = new Row { AlignItems = Align.Center, Justify = Justify.Center };
            face.Width = 24;
            var avatar = new Avatar(16, menu: false) { User = commit.Author };
            face.Add(avatar.View);
            row.Add(face);
            if (commit.IsMerged)
                row.Add(new Icon(Icons.Get("Check")) { Size = 14, Color = Color.Rgba(0, 128, 0), Margin = new Thickness(0, 2, 0, 0) });
            var subject = new Text(commit.Subject) { MaxLines = 1, Margin = new Thickness(0, 0, 0, 2) };
            subject.Grow = 1;
            subject.Width = 0;
            row.Add(subject);
            row.Add(new Text(Models.DateTimeFormat.Format(commit.CommitterTime, true)) { Color = Color.SecondaryLabel, Margin = new Thickness(0, 4) });
            row.ToolTip = $"{commit.SHA[..10]}  {commit.Subject}";
            return row;
        }
    }
}
