using System;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's details under the history (<c>Views/Histories.axaml</c>'s
    /// bottom area, <c>CommitDetail.axaml</c>, <c>CommitBaseInfo.axaml</c>):
    /// the platform's tabs, Information, Changes and Files. Information
    /// shows what the selection is: a tip with none, the commit with one,
    /// how many with more. Changes, Files and comparing two commits wait
    /// for the code view (bemtvi).
    /// </summary>
    public static class CommitDetail
    {
        public static Node Create(ViewModels.Histories vm)
        {
            var tabs = new Tabs();
            var info = new ScrollView();
            info.Content.Padding = new Thickness(0, 12, 8, 4);
            // A tab's page is a container, here filled by the scroll view.
            info.Grow = 1;
            info.MinHeight = 0;
            var infoPage = new Column { AlignItems = Align.Stretch };
            infoPage.Add(info);
            tabs.Add(App.Text("CommitDetail.Info"), infoPage);
            tabs.Add(App.Text("CommitDetail.Changes"), NotPorted());
            tabs.Add(App.Text("CommitDetail.Files"), NotPorted());

            // The commit shown follows its view model's changes; another
            // selection brings another view model.
            ViewModels.CommitDetail shown = null;
            void Changed(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(shown.Commit) or nameof(shown.FullMessage) or nameof(shown.Changes))
                    Show(info.Content, vm, shown);
                else if (e.PropertyName == nameof(shown.ActiveTabIndex))
                    tabs.SelectedIndex = shown.ActiveTabIndex;
            }
            tabs.Changed += index =>
            {
                if (shown != null)
                    shown.ActiveTabIndex = index;
            };
            Bind.OneWay(vm, nameof(vm.DetailContext), () => vm.DetailContext, context =>
            {
                if (shown != null)
                    shown.PropertyChanged -= Changed;
                shown = context as ViewModels.CommitDetail;
                if (shown != null)
                {
                    shown.PropertyChanged += Changed;
                    tabs.SelectedIndex = shown.ActiveTabIndex;
                }
                else
                {
                    tabs.SelectedIndex = 0;
                }
                Show(info.Content, vm, context);
            }, owner: tabs);
            tabs.Forgotten += () =>
            {
                if (shown != null)
                    shown.PropertyChanged -= Changed;
            };
            return tabs;
        }

        private static Node NotPorted()
        {
            var page = new Column { Padding = 20 };
            page.Add(new Text("This page isn't ported to mitsuami yet: it needs the code view.") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            return page;
        }

        /// <summary>Fills the Information page for what's selected.</summary>
        private static void Show(Container page, ViewModels.Histories vm, object context)
        {
            foreach (var child in page.Children.ToArray())
                child.Destroy();

            switch (context)
            {
                case ViewModels.CommitDetail { Commit: { } commit } detail:
                    BaseInfo(page, vm, detail, commit);
                    Changes(page, detail);
                    break;
                case Models.Count count:
                    page.Add(Placeholder(App.Text("Histories.Selected", count.Value)));
                    break;
                case ViewModels.RevisionCompare:
                    page.Add(Placeholder("Comparing two commits isn't ported to mitsuami yet."));
                    break;
                default:
                    var hint = OperatingSystem.IsMacOS() ? "Histories.Tips.MacOS" : "Histories.Tips";
                    page.Add(Placeholder($"{App.Text("Histories.Tips.Prefix")} {App.Text(hint)}"));
                    break;
            }
        }

        /// <summary>Upstream's empty page: its 128-point detail icon over a
        /// line in the secondary colour.</summary>
        private static Node Placeholder(string text)
        {
            var column = new Column { AlignItems = Align.Center, Gap = 16, Padding = new Thickness(24, 0) };
            column.Add(new Icon(Icons.Get("Detail")) { Size = 128, Color = Color.SecondaryLabel });
            column.Add(new Text(text) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            return column;
        }

        /// <summary>
        /// Upstream's <c>CommitBaseInfo</c>: the author and the committer
        /// (when they differ) side by side, a line, then the SHA, the
        /// parents, the refs and the message, labelled in a 96-point column.
        /// The avatars aren't ported: their column is kept, so the names
        /// line up with the values below as upstream's do.
        /// </summary>
        private static void BaseInfo(Container page, ViewModels.Histories vm, ViewModels.CommitDetail detail, Models.Commit commit)
        {
            var people = new Row { AlignItems = Align.Start, Padding = new Thickness(8, 0) };
            people.Add(Person(App.Text("CommitDetail.Info.Author"), commit.Author, commit.AuthorTime));
            if (commit.IsCommitterVisible)
                people.Add(Person(App.Text("CommitDetail.Info.Committer"), commit.Committer, commit.CommitterTime));
            page.Add(people);
            page.Add(new Separator { Margin = new Thickness(8, 0, 8, 8) });

            var sha = new Text(commit.SHA) { Style = TextStyle.Monospace };
            page.Add(Field(App.Text("CommitDetail.Info.SHA"), sha));

            if (commit.Parents.Count > 0)
            {
                var parents = new Row { AlignItems = Align.Center, Gap = 16 };
                foreach (var parent in commit.Parents)
                {
                    // Upstream's links to the parents, in its dark orange.
                    var link = new Row();
                    link.Add(new Text(parent.Length > 10 ? parent[..10] : parent) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
                    link.Clicked += () => detail.NavigateTo(parent);
                    link.ToolTip = parent;
                    parents.Add(link);
                }
                page.Add(Field(App.Text("CommitDetail.Info.Parents"), parents));
            }

            if (commit.HasDecorators)
            {
                var refs = new CommitRefs();
                refs.Show(commit, vm.HasSingleRemote, useGraphColor: false);
                page.Add(Field(App.Text("CommitDetail.Info.Refs"), refs.View));
            }

            var message = new Text(detail.FullMessage?.Message?.Trim() ?? commit.Subject);
            page.Add(Field(App.Text("CommitDetail.Info.Message"), message));
        }

        /// <summary>A person's half: their name in the label colour and
        /// their email in the secondary one, then the date, smaller.</summary>
        private static Node Person(string title, Models.User user, ulong time)
        {
            var half = new Row { AlignItems = Align.Start };
            half.Grow = 1;
            half.Width = 0;
            var text = new Column { Margin = new Thickness(0, 4, 0, 112) };
            text.Grow = 1;
            text.Add(new Text(title) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            var who = new Row { AlignItems = Align.Center, Gap = 4, Margin = new Thickness(10, 0, 8, 0) };
            who.ToolTip = $"{user.Name} <{user.Email}>";
            who.Add(new Text(user.Name) { MaxLines = 1 });
            who.Add(new Text(user.Email) { Color = Color.SecondaryLabel, MaxLines = 1 });
            text.Add(who);
            text.Add(new Text(Models.DateTimeFormat.Format(time)) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            half.Add(text);
            return half;
        }

        /// <summary>A labelled value: the label right-aligned in 96 points,
        /// bold and secondary, the value 12 after it.</summary>
        private static Node Field(string label, Node value)
        {
            var row = new Row { AlignItems = Align.Start, Gap = 12, Padding = new Thickness(4, 0) };
            var name = new Column { AlignItems = Align.End };
            name.Width = 96;
            name.Add(new Text(label) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            row.Add(name);
            var holder = new Column { AlignItems = Align.Start };
            holder.Grow = 1;
            holder.Width = 0;
            holder.Add(value);
            row.Add(holder);
            return row;
        }

        /// <summary>
        /// Upstream's list of the commit's first 100 changes under a line:
        /// each a status mark (its letter on its colour) and the path.
        /// </summary>
        private static void Changes(Container page, ViewModels.CommitDetail detail)
        {
            if (detail.Changes is not { Count: > 0 } changes)
                return;
            page.Add(new Separator { Margin = new Thickness(8, 0, 8, 8) });
            var list = new Column { Margin = new Thickness(0, 0, 4, 64) };
            foreach (var change in changes.Take(100))
            {
                var row = new Row { AlignItems = Align.Center, Gap = 8, Padding = new Thickness(0, 0, 0, 16) };
                row.Height = 24;
                row.ToolTip = $"{change.Path} • {change.IndexDesc}";
                row.Add(new StatusMark(change.Index).View);
                row.Add(new Text(change.Path) { MaxLines = 1 });
                list.Add(row);
            }
            page.Add(list);
            if (changes.Count > 100)
                page.Add(new Text(App.Text("CommitDetail.Info.GotoChangesPage")) { Color = Color.SecondaryLabel, Margin = new Thickness(0, 12, 16, 108) });
        }
    }

    /// <summary>
    /// Upstream's <c>ChangeStatusIcon</c>: a change's state as a letter on
    /// its colour, 14 points square. The square is drawn; the letter is the
    /// platform's text over it.
    /// </summary>
    public sealed class StatusMark
    {
        private static readonly string[] s_letters = ["?", "M", "T", "+", "−", "➜", "❏", "?", "!"];
        private static readonly (byte R, byte G, byte B)[] s_colors =
        [
            (0, 0, 0),
            (218, 165, 32),
            (218, 165, 32),
            (50, 205, 50),
            (255, 99, 71),
            (218, 112, 214),
            (218, 165, 32),
            (135, 206, 235),
            (255, 69, 0),
        ];

        public StatusMark(Models.ChangeState state)
        {
            var i = (int)state;
            var (r, g, b) = s_colors[i];
            var mark = new Row { AlignItems = Align.Center, Justify = Justify.Center };
            mark.Width = 14;
            mark.Height = 14;
            mark.Add(new Square(Color.Rgba(r, g, b, (byte)(i == 0 ? 0 : 255))) { Inset = new Thickness(0) });
            mark.Add(new Text(s_letters[i]) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.Rgba(255, 255, 255) });
            View = mark;
        }

        public Node View { get; }

        private sealed class Square(Color color) : DrawnWidget("ChangeStatus", 0, 0)
        {
            protected override void Draw(Canvas canvas) => canvas.FillRect(0, 0, canvas.Width, canvas.Height, color, 2);
        }
    }
}
